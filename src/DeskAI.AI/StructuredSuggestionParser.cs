using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskAI.Core.Ai;
using DeskAI.Core.Classification;
using DeskAI.Core.Templates;

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
        AiSuggestionProvenance provenance) =>
        Parse(json, requestedFileIds, maximumResponseBytes, provenance, AiSuggestionTask.Classify);

    /// <param name="task">
    /// For <see cref="AiSuggestionTask.PlanFolder"/> every suggestion must carry a folder name
    /// that passes <see cref="FolderNameCheck"/>, and category may be left out; for
    /// <see cref="AiSuggestionTask.Classify"/> a folder is refused. At most
    /// <see cref="OrganizationSuggestionRequest.MaxPlanFolders"/> distinct folders.
    /// </param>
    public static StructuredSuggestionParseResult Parse(
        string json,
        IReadOnlySet<Guid> requestedFileIds,
        int maximumResponseBytes,
        AiSuggestionProvenance provenance,
        AiSuggestionTask task)
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
            using var document = JsonDocument.Parse(json);
            if (ContainsDuplicateProperty(document.RootElement))
            {
                return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.MalformedJson);
            }

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
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            // A folder name is untrusted text that will become part of a path. Only a plan may
            // carry one, it must pass the same check a typed template name passes, and a plan
            // that names too many folders is refused whole rather than trimmed.
            if (task == AiSuggestionTask.Classify && item.Folder is not null)
            {
                return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.UnexpectedFolder);
            }

            if (task == AiSuggestionTask.PlanFolder)
            {
                if (item.Folder is null || FolderNameCheck.Check(item.Folder) is not null)
                {
                    return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.InvalidFolderName);
                }

                if (folders.Add(item.Folder) && folders.Count > OrganizationSuggestionRequest.MaxPlanFolders)
                {
                    return StructuredSuggestionParseResult.Invalid(StructuredOutputFailure.TooManyFolders);
                }
            }

            FileCategory category;
            if (task == AiSuggestionTask.PlanFolder && item.Category is null)
            {
                category = FileCategory.Unknown;
            }
            else if (!Enum.TryParse(item.Category, ignoreCase: false, out category) || !Enum.IsDefined(category))
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
                item.FileId, category, item.Confidence, item.Reason, provenance, item.Folder));
        }

        return StructuredSuggestionParseResult.Valid(suggestions.AsReadOnly());
    }

    private static bool ContainsDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || ContainsDuplicateProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsDuplicateProperty);
        }

        return false;
    }

    private sealed record SuggestionEnvelope(
        [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
        [property: JsonPropertyName("suggestions")] IReadOnlyList<SuggestionItem>? Suggestions);

    private sealed record SuggestionItem(
        [property: JsonPropertyName("fileId")] Guid FileId,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("folder")] string? Folder = null);
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
    UnexpectedFolder,
    InvalidFolderName,
    TooManyFolders,
}
