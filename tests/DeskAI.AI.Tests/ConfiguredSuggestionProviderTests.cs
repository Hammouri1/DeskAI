using System.Net;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI.Tests;

public sealed class ConfiguredSuggestionProviderTests
{
    [Fact]
    public async Task SuggestAsync_CloudWithoutConsentNeverCallsTransport()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = false,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var provider = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings), new FakeCredentialVault("key"), transport, new FakeAiUsageBudget(), new FakeClock());

        var response = await provider.SuggestAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Disabled, response.Status);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task SuggestAsync_RequestOutsideDisclosurePolicyNeverCallsTransport()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
            CloudDisclosures = new HashSet<DisclosureCategory> { DisclosureCategory.Extension },
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var provider = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings), new FakeCredentialVault("key"), transport, new FakeAiUsageBudget(), new FakeClock());
        var request = CreateRequest() with
        {
            Disclosure = new DisclosureSummary(
                new HashSet<DisclosureCategory> { DisclosureCategory.Extension, DisclosureCategory.FileName }, 1, 0),
        };

        var response = await provider.SuggestAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.SafetyRejected, response.Status);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task GroupItems_refuses_online_AI_when_folder_names_are_not_shared()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
            CloudDisclosures = new HashSet<DisclosureCategory> { DisclosureCategory.Extension, DisclosureCategory.FileName },
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var provider = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings), new FakeCredentialVault("key"), transport, new FakeAiUsageBudget(), new FakeClock());

        var response = await provider.GroupItemsAsync(GroupItemsProviderTests.Sample(), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.SafetyRejected, response.Status);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task GroupItems_with_everything_shared_reaches_the_chosen_service()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
            CloudDisclosures = new HashSet<DisclosureCategory> { DisclosureCategory.Extension, DisclosureCategory.FileName, DisclosureCategory.FolderNames },
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, GroupItemsProviderTests.Envelope("""{"schemaVersion":"1","groups":[]}"""));
        var provider = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings), new FakeCredentialVault("key"), transport, new FakeAiUsageBudget(), new FakeClock());

        var response = await provider.GroupItemsAsync(GroupItemsProviderTests.Sample(), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Success, response.Status);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task GroupItems_daily_cap_stops_before_the_network()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
            CloudDisclosures = new HashSet<DisclosureCategory> { DisclosureCategory.Extension, DisclosureCategory.FileName, DisclosureCategory.FolderNames },
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var provider = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings), new FakeCredentialVault("key"), transport, new FakeAiUsageBudget(allow: false), new FakeClock());

        var response = await provider.GroupItemsAsync(GroupItemsProviderTests.Sample(), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.CostLimitReached, response.Status);
        Assert.Equal(0, transport.CallCount);
    }

    private static OrganizationSuggestionRequest CreateRequest() => new(
        "1", Guid.NewGuid(),
        [new AiFileCandidate(Guid.NewGuid(), ".pdf", null, null, null, null, null)],
        new DisclosureSummary(new HashSet<DisclosureCategory> { DisclosureCategory.Extension }, 1, 0),
        AiRequestLimits.Default);
}

internal sealed class FakeAiSettingsRepository(AiSettings settings) : IAiSettingsRepository
{
    public Task<AiSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);

    public Task SaveAsync(AiSettings updated, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeAiUsageBudget(bool allow = true) : IAiUsageBudget
{
    public Task<bool> TryReserveRequestAsync(
        string providerId,
        int dailyLimit,
        DateOnly utcDate,
        CancellationToken cancellationToken = default) => Task.FromResult(allow);

    public Task<int> GetRequestCountAsync(
        string providerId,
        DateOnly utcDate,
        CancellationToken cancellationToken = default) => Task.FromResult(0);
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow => new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
}
