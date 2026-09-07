using System.Net;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI.Tests;

public sealed class GeminiSuggestionProviderTests
{
    [Fact]
    public async Task SuggestAsync_UsesFixedGoogleHostAndHeaderCredential()
    {
        var id = Guid.NewGuid();
        var structured = $$"""{"schemaVersion":"1","suggestions":[{"fileId":"{{id}}","category":"Documents","confidence":0.85,"reason":"Document metadata."}]}""";
        var envelope = System.Text.Json.JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = structured } } } } },
            usageMetadata = new { promptTokenCount = 10, candidatesTokenCount = 5 },
        });
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, envelope);
        var vault = new FakeCredentialVault("obvious-test-api-key");
        var provider = new GeminiSuggestionProvider(transport, vault, "DeskAI/Gemini", "test-model");

        var response = await provider.SuggestAsync(CreateRequest(id), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Success, response.Status);
        Assert.Equal("generativelanguage.googleapis.com", transport.Endpoint!.Host);
        Assert.Equal("obvious-test-api-key", transport.Headers!["x-goog-api-key"]);
        Assert.DoesNotContain("obvious-test-api-key", transport.RequestBody, StringComparison.Ordinal);
        Assert.Equal(AiSuggestionProvenance.CloudAi, Assert.Single(response.Suggestions).Provenance);
        Assert.Equal(10, response.Usage!.InputTokens);
    }

    [Fact]
    public async Task SuggestAsync_MissingCredentialStopsBeforeNetwork()
    {
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var provider = new GeminiSuggestionProvider(
            transport, new FakeCredentialVault(null), "DeskAI/Gemini", "test-model");

        var response = await provider.SuggestAsync(CreateRequest(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.AuthenticationFailed, response.Status);
        Assert.Equal(0, transport.CallCount);
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
