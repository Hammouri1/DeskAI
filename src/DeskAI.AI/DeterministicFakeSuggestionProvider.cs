using DeskAI.Core.Ai;
using DeskAI.Core.Classification;

namespace DeskAI.AI;

public sealed class DeterministicFakeSuggestionProvider : IOrganizationSuggestionProvider
{
    public Task<OrganizationSuggestionResponse> SuggestAsync(
        OrganizationSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var suggestions = request.Files
            .OrderBy(file => file.FileId)
            .Select(file => new OrganizationSuggestion(
                file.FileId,
                CategoryFor(file.Extension),
                0.75,
                "Deterministic fake suggestion generated for testing.",
                AiSuggestionProvenance.DeterministicFake))
            .ToArray();
        return Task.FromResult(new OrganizationSuggestionResponse(
            AiProviderStatus.Success,
            "Deterministic test provider",
            suggestions,
            $"Generated {suggestions.Length} test suggestion(s) without network access."));
    }

    /// <summary>A canned reading: any ".xyz" in the sentence becomes an ending, "photo" becomes Images, the rest is text.</summary>
    public Task<AiSentenceResponse> ReadSentenceAsync(
        AiSentenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var words = request.Sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ending = words.FirstOrDefault(word => word.StartsWith('.') && word.Length > 1);
        var isPhoto = words.Any(word => word.Contains("photo", StringComparison.OrdinalIgnoreCase));
        var text = string.Join(' ', words.Where(word => word != ending && !word.Contains("photo", StringComparison.OrdinalIgnoreCase)));
        var json = request.Task == SentenceTask.SearchPhrase
            ? System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = AiSentenceRequest.CurrentSchemaVersion,
                endings = ending is null ? Array.Empty<string>() : [ending],
                categories = isPhoto ? new[] { nameof(FileCategory.Images) } : [],
                largerThanBytes = (long?)null,
                smallerThanBytes = (long?)null,
                changedInLastDays = (int?)null,
                text = text.Length == 0 ? null : text,
            })
            : System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = AiSentenceRequest.CurrentSchemaVersion,
                ending,
                category = isPhoto ? nameof(FileCategory.Images) : null,
                largerThanBytes = (long?)null,
                smallerThanBytes = (long?)null,
                olderThanDays = (int?)null,
                nameContains = text.Length == 0 ? null : text,
                destination = "Sorted",
            });
        return Task.FromResult(new AiSentenceResponse(
            AiProviderStatus.Success,
            "Deterministic test provider",
            json,
            "Generated a test reading without network access."));
    }

    private static FileCategory CategoryFor(string? extension) => extension?.ToLowerInvariant() switch
    {
        ".pdf" or ".doc" or ".docx" or ".txt" => FileCategory.Documents,
        ".png" or ".jpg" or ".jpeg" => FileCategory.Images,
        ".xlsx" or ".csv" => FileCategory.Spreadsheets,
        _ => FileCategory.Unknown,
    };
}
