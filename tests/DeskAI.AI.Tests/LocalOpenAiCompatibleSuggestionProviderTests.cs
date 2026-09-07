using System.Net;
using DeskAI.Core.Ai;

namespace DeskAI.AI.Tests;

public sealed class LocalOpenAiCompatibleSuggestionProviderTests
{
    [Fact]
    public async Task SuggestAsync_UsesOnlyLoopbackAndStrictlyParsesResponse()
    {
        var id = Guid.NewGuid();
        var structured = $$"""{"schemaVersion":"1","suggestions":[{"fileId":"{{id}}","category":"Documents","confidence":0.8,"reason":"Document-like metadata."}]}""";
        var envelope = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = structured } } },
        });
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, envelope);
        var provider = new LocalOpenAiCompatibleSuggestionProvider(
            transport, "http://127.0.0.1:11434/v1/chat/completions", "test-model");

        var response = await provider.SuggestAsync(CreateRequest(id), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Success, response.Status);
        Assert.True(transport.Endpoint!.IsLoopback);
        Assert.Empty(transport.Headers!);
        Assert.DoesNotContain("fullPath", transport.RequestBody, StringComparison.Ordinal);
        Assert.Equal(AiSuggestionProvenance.LocalAi, Assert.Single(response.Suggestions).Provenance);
    }

    [Theory]
    [InlineData("https://example.com/v1/chat/completions")]
    [InlineData("file:///C:/model")]
    [InlineData("http://localhost:11434/path?secret=value")]
    public void Constructor_RejectsNonLoopbackOrAmbiguousEndpoint(string endpoint)
    {
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");

        Assert.Throws<ArgumentException>(() =>
            new LocalOpenAiCompatibleSuggestionProvider(transport, endpoint, "model"));
    }

    private static OrganizationSuggestionRequest CreateRequest(Guid id) => new(
        "1",
        Guid.NewGuid(),
        [new AiFileCandidate(id, ".pdf", null, null, null, null, null)],
        new DisclosureSummary(new HashSet<DisclosureCategory> { DisclosureCategory.Extension }, 1, 0),
        AiRequestLimits.Default);
}
