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

    private static FileCategory CategoryFor(string? extension) => extension?.ToLowerInvariant() switch
    {
        ".pdf" or ".doc" or ".docx" or ".txt" => FileCategory.Documents,
        ".png" or ".jpg" or ".jpeg" => FileCategory.Images,
        ".xlsx" or ".csv" => FileCategory.Spreadsheets,
        _ => FileCategory.Unknown,
    };
}
