using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskAI.Core.Ai;
using DeskAI.Core.Classification;

namespace DeskAI.AI;

public static class StructuredSuggestionParser
{
    private const int MaximumReasonLength = 240;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static StructuredSuggestionParseResult Parse(
        string json,
        IReadOnlySet<Guid> requestedFileIds,
        int maximumResponseBytes,
        AiSuggestionProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(requestedFileIds);
        if (maximumResponseBytes < 1 || Encoding.UTF8.GetByteCount(json) > maximumResponseBytes)
        {
            return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.ResponseTooLarge);
        }

        SuggestionEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SuggestionEnvelope>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.MalformedJson);
        }

        if (envelope is null || envelope.SchemaVersion != OrganizationSuggestionRequest.CurrentSchemaVersion ||
            envelope.Suggestions is null)
        {
            return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.UnsupportedSchema);
        }

        if (envelope.Suggestions.Count > requestedFileIds.Count)
        {
            return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.TooManySuggestions);
        }

        var seen = new HashSet<Guid>();
        var suggestions = new List<OrganizationSuggestion>(envelope.Suggestions.Count);
        foreach (var item in envelope.Suggestions)
        {
            if (!requestedFileIds.Contains(item.FileId))
            {
                return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.UnknownFileId);
            }

            if (!seen.Add(item.FileId))
            {
                return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.DuplicateFileId);
            }

            if (!Enum.TryParse<FileCategory>(item.Category, ignoreCase: false, out var category) ||
                !Enum.IsDefined(category))
            {
                return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.UnknownCategory);
            }

            if (!double.IsFinite(item.Confidence) || item.Confidence is < 0 or > 1)
            {
                return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.InvalidConfidence);
            }

            if (string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > MaximumReasonLength ||
                item.Reason.Any(char.IsControl))
            {
                return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.InvalidReason);
            }

            suggestions.Add(new OrganizationSuggestion(
                item.FileId, category, item.Confidence, item.Reason, provenance));
        }

        return StructuredSuggestionParseResult.Valid(suggestions.AsReadOnly());
    }

    private sealed record SuggestionEnvelope(
        [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
        [property: JsonPropertyName("suggestions")] IReadOnlyList<SuggestionItem>? Suggestions);

    private sealed record SuggestionItem(
        [property: JsonPropertyName("fileId")] Guid FileId,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("reason")] string? Reason);
}

public sealed record StructuredSuggestionParseResult(
    bool IsValid,
    IReadOnlyList<OrganizationSuggestion> Suggestions,
    StructuredOutputFailure? Failure)
{
    public static StructuredSuggestionParseResult Valid(IReadOnlyList<OrganizationSuggestion> suggestions) =>
        new(true, suggestions, null);

    public static StructuredSuggestionParseResult Invalid(StructuredOutputFailure failure) =>
        new(false, [], failure);
}

public enum StructuredOutputFailure
{
    ResponseTooLarge,
    MalformedJson,
    UnsupportedSchema,
    TooManySuggestions,
    UnknownFileId,
    DuplicateFileId,
    UnknownCategory,
    InvalidConfidence,
    InvalidReason,
}
