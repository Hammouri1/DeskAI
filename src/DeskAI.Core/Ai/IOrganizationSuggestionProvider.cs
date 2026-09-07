using DeskAI.Core.Files;

namespace DeskAI.Core.Ai;

public interface IOrganizationSuggestionProvider
{
    Task<OrganizationSuggestionResponse> SuggestAsync(
        OrganizationSuggestionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OrganizationSuggestionRequest(
    Guid RootId,
    IReadOnlyList<FileItem> Files,
    IReadOnlySet<DisclosureCategory> AllowedDisclosures);

public sealed record OrganizationSuggestionResponse(
    bool IsAvailable,
    IReadOnlyList<OrganizationSuggestion> Suggestions,
    string? UnavailableReason);

public sealed record OrganizationSuggestion(Guid FileId, string Category, double Confidence, string Reason);

public enum DisclosureCategory
{
    Extension,
    Metadata,
    FileName,
    FolderNames,
    FullPath,
    ExtractedContent,
    ImageContent,
}
