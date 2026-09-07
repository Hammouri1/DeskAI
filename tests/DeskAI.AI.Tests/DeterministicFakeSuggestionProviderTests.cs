using DeskAI.Core.Ai;
using DeskAI.Core.Classification;

namespace DeskAI.AI.Tests;

public sealed class DeterministicFakeSuggestionProviderTests
{
    [Fact]
    public async Task SuggestAsync_ReturnsStableTypedSuggestionsWithoutTransport()
    {
        var fileId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var request = new OrganizationSuggestionRequest(
            OrganizationSuggestionRequest.CurrentSchemaVersion,
            Guid.NewGuid(),
            [new AiFileCandidate(fileId, ".pdf", 10, null, null, null, null)],
            new DisclosureSummary(new HashSet<DisclosureCategory> { DisclosureCategory.Extension }, 1, 0),
            AiRequestLimits.Default);

        var response = await new DeterministicFakeSuggestionProvider()
            .SuggestAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Success, response.Status);
        var suggestion = Assert.Single(response.Suggestions);
        Assert.Equal(fileId, suggestion.FileId);
        Assert.Equal(FileCategory.Documents, suggestion.Category);
        Assert.Equal(AiSuggestionProvenance.DeterministicFake, suggestion.Provenance);
    }

    [Fact]
    public async Task NoAiProvider_KeepsRuleOnlyModeUsable()
    {
        var request = new OrganizationSuggestionRequest(
            OrganizationSuggestionRequest.CurrentSchemaVersion,
            Guid.NewGuid(),
            [],
            new DisclosureSummary(new HashSet<DisclosureCategory>(), 0, 0),
            AiRequestLimits.Default);

        var response = await new NoAiSuggestionProvider()
            .SuggestAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Disabled, response.Status);
        Assert.Empty(response.Suggestions);
    }
}
