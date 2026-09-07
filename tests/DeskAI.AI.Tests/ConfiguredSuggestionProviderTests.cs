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
            ProviderId = "gemini",
            ModelId = "test-model",
            CredentialReference = "DeskAI/Gemini",
            CloudConsentGranted = false,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var provider = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings), new FakeCredentialVault("key"), transport);

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
            ProviderId = "gemini",
            ModelId = "test-model",
            CredentialReference = "DeskAI/Gemini",
            CloudConsentGranted = true,
            CloudDisclosures = new HashSet<DisclosureCategory> { DisclosureCategory.Extension },
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var provider = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings), new FakeCredentialVault("key"), transport);
        var request = CreateRequest() with
        {
            Disclosure = new DisclosureSummary(
                new HashSet<DisclosureCategory> { DisclosureCategory.Extension, DisclosureCategory.FileName }, 1, 0),
        };

        var response = await provider.SuggestAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.SafetyRejected, response.Status);
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
