using DeskAI.Core.Ai;

namespace DeskAI.AI.Tests;

public sealed class StructuredSuggestionParserTests
{
    private static readonly Guid RequestedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly HashSet<Guid> RequestedIds = [RequestedId];

    [Fact]
    public void Parse_MapsOnlyValidTypedSuggestion()
    {
        var json = $$"""{"schemaVersion":"1","suggestions":[{"fileId":"{{RequestedId}}","category":"Documents","confidence":0.8,"reason":"Likely a document."}]}""";

        var result = StructuredSuggestionParser.Parse(json, RequestedIds, 4096, AiSuggestionProvenance.CloudAi);

        Assert.True(result.IsValid);
        Assert.Equal(AiSuggestionProvenance.CloudAi, Assert.Single(result.Suggestions).Provenance);
    }

    [Theory]
    [InlineData("not-json", StructuredOutputFailure.MalformedJson)]
    [InlineData("{\"schemaVersion\":\"99\",\"suggestions\":[]}", StructuredOutputFailure.UnsupportedSchema)]
    [InlineData("{\"schemaVersion\":\"1\",\"suggestions\":[],\"command\":\"delete everything\"}", StructuredOutputFailure.MalformedJson)]
    public void Parse_RejectsMalformedSchemaAndRawCommands(string json, StructuredOutputFailure expected)
    {
        var result = StructuredSuggestionParser.Parse(json, RequestedIds, 4096, AiSuggestionProvenance.CloudAi);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public void Parse_RejectsInventedAndDuplicateIds()
    {
        var invented = $$"""{"schemaVersion":"1","suggestions":[{"fileId":"{{Guid.NewGuid()}}","category":"Documents","confidence":0.5,"reason":"Invented."}]}""";
        var duplicate = $$"""{"schemaVersion":"1","suggestions":[{"fileId":"{{RequestedId}}","category":"Documents","confidence":0.5,"reason":"One."},{"fileId":"{{RequestedId}}","category":"Documents","confidence":0.6,"reason":"Two."}]}""";

        Assert.Equal(StructuredOutputFailure.UnknownFileId,
            StructuredSuggestionParser.Parse(invented, RequestedIds, 4096, AiSuggestionProvenance.CloudAi).Failure);
        Assert.Equal(StructuredOutputFailure.DuplicateFileId,
            StructuredSuggestionParser.Parse(
                duplicate,
                new HashSet<Guid> { RequestedId, Guid.NewGuid() },
                4096,
                AiSuggestionProvenance.CloudAi).Failure);
    }

    [Theory]
    [InlineData("DeleteEverything", 0.5, StructuredOutputFailure.UnknownCategory)]
    [InlineData("Documents", 1.1, StructuredOutputFailure.InvalidConfidence)]
    public void Parse_RejectsUnknownEnumsAndInvalidConfidence(
        string category,
        double confidence,
        StructuredOutputFailure expected)
    {
        var json = $$"""{"schemaVersion":"1","suggestions":[{"fileId":"{{RequestedId}}","category":"{{category}}","confidence":{{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"reason":"Test."}]}""";

        var result = StructuredSuggestionParser.Parse(json, RequestedIds, 4096, AiSuggestionProvenance.CloudAi);

        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public void Parse_RejectsOversizedResponseBeforeDeserialization()
    {
        var result = StructuredSuggestionParser.Parse(new string('x', 100), RequestedIds, 10, AiSuggestionProvenance.CloudAi);

        Assert.Equal(StructuredOutputFailure.ResponseTooLarge, result.Failure);
    }

    [Fact]
    public void Parse_RejectsDuplicateJsonProperties()
    {
        var json = "{\"schemaVersion\":\"1\",\"schemaVersion\":\"1\",\"suggestions\":[]}";

        var result = StructuredSuggestionParser.Parse(json, RequestedIds, 4096, AiSuggestionProvenance.CloudAi);

        Assert.Equal(StructuredOutputFailure.MalformedJson, result.Failure);
    }
}
