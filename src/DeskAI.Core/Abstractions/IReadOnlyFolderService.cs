using DeskAI.Core.Files;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

public interface IReadOnlyFolderService
{
    Task<FolderPreviewResult> AuthorizeAndPreviewAsync(
        string selectedPath,
        MetadataScanOptions options,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthorizedRoot>> ListAuthorizedAsync(CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid rootId, CancellationToken cancellationToken = default);
}

public sealed record FolderPreviewResult(
    bool IsAllowed,
    string Explanation,
    AuthorizedRoot? Root,
    IReadOnlyList<FileItem> Files,
    IReadOnlyList<ScanIssue> Issues);
