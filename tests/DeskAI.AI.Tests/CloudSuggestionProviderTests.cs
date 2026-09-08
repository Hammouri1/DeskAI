using System.Net;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI.Tests;

public sealed class CloudSuggestionProviderTests
{
    public static TheoryData<string> KnownProviderIds()
    {
        var data = new TheoryData<string>();
        foreach (var provider in CloudProviderCatalog.All)
        {
            data.Add(provider.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(KnownProviderIds))]
    public async Task SuggestAsync_PostsOnlyToTheChosenProvidersFixedAddress(string providerId)
    {
        var provider = CloudProviderCatalog.Find(providerId)!;
        var id = Guid.NewGuid();
        var structured = $$"""{"schemaVersion":"1","suggestions":[{"fileId":"{{id}}","category":"Documents","confidence":0.85,"reason":"Document metadata."}]}""";
        var envelope = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = structured } } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 },
        });
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, envelope);
        var adapter = new CloudChatCompletionsSuggestionProvider(
            transport, new FakeCredentialVault("obvious-test-api-key"), provider, "test/model");

        var response = await adapter.SuggestAsync(CreateRequest(id), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Success, response.Status);
        Assert.Equal(provider.ChatCompletionsEndpoint, transport.Endpoint);
        Assert.Equal(Uri.UriSchemeHttps, transport.Endpoint!.Scheme);
        Assert.Equal("Bearer obvious-test-api-key", transport.Headers!["Authorization"]);
        Assert.DoesNotContain("obvious-test-api-key", transport.RequestBody, StringComparison.Ordinal);
        Assert.Equal(provider.DisplayName, response.ProviderDisplayName);
        Assert.Equal(AiSuggestionProvenance.CloudAi, Assert.Single(response.Suggestions).Provenance);
        Assert.Equal(10, response.Usage!.InputTokens);
    }

    [Fact]
    public async Task SuggestAsync_MissingCredentialStopsBeforeNetwork()
    {
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var adapter = new CloudChatCompletionsSuggestionProvider(
            transport, new FakeCredentialVault(null), CloudProviderCatalog.All[0], "test/model");

        var response = await adapter.SuggestAsync(CreateRequest(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.AuthenticationFailed, response.Status);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task SuggestAsync_ReadsOnlyTheChosenProvidersOwnCredentialEntry()
    {
        var groq = CloudProviderCatalog.Find("groq")!;
        var vault = new RecordingCredentialVault("obvious-test-api-key");
        var adapter = new CloudChatCompletionsSuggestionProvider(
            new FakeAiHttpTransport(HttpStatusCode.OK, "{}"), vault, groq, "test-model");

        await adapter.SuggestAsync(CreateRequest(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(groq.CredentialReference, Assert.Single(vault.RequestedReferences));
        Assert.NotEqual(CloudProviderCatalog.Find("openrouter")!.CredentialReference, groq.CredentialReference);
    }

    [Fact]
    public async Task ConfiguredProvider_RefusesAnUnknownSavedProviderWithoutSendingAnything()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "some-service-deskai-does-not-know",
            ModelId = "test-model",
            CredentialReference = "DeskAI/Unknown",
            CloudConsentGranted = true,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var configured = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings),
            new FakeCredentialVault("key"),
            transport,
            new FakeAiUsageBudget(allow: true),
            new FakeClock());

        var response = await configured.SuggestAsync(CreateRequest(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Disabled, response.Status);
        Assert.Equal(0, transport.CallCount);
    }

    [Theory]
    [MemberData(nameof(KnownProviderIds))]
    public async Task ConfiguredProvider_RoutesEachSavedProviderToItsOwnAddress(string providerId)
    {
        var provider = CloudProviderCatalog.Find(providerId)!;
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = provider.Id,
            ModelId = "test-model",
            CredentialReference = provider.CredentialReference,
            CloudConsentGranted = true,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var configured = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings),
            new FakeCredentialVault("key"),
            transport,
            new FakeAiUsageBudget(allow: true),
            new FakeClock());

        await configured.SuggestAsync(CreateRequest(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(provider.ChatCompletionsEndpoint, transport.Endpoint);
    }

    private static OrganizationSuggestionRequest CreateRequest(Guid id) => new(
        "1", Guid.NewGuid(),
        [new AiFileCandidate(id, ".pdf", null, null, null, null, null)],
        new DisclosureSummary(new HashSet<DisclosureCategory> { DisclosureCategory.Extension }, 1, 0),
        AiRequestLimits.Default);
}

internal sealed class FakeCredentialVault(string? initialSecret) : ICredentialVault
{
    private string? _secret = initialSecret;

    public Task SaveAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        _secret = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secret);

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default)
    {
        _secret = null;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingCredentialVault(string? initialSecret) : ICredentialVault
{
    private readonly List<string> _requested = [];
    private string? _secret = initialSecret;

    public IReadOnlyList<string> RequestedReferences => _requested;

    public Task SaveAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        _secret = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        _requested.Add(reference);
        return Task.FromResult(_secret);
    }

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default)
    {
        _secret = null;
        return Task.CompletedTask;
    }
}
