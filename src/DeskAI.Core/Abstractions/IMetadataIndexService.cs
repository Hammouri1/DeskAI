using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Refreshes the local metadata index for one explicitly authorized root.
/// </summary>
/// <remarks>
/// Implementations must refuse a root that deterministic path policy blocks, must read
/// metadata only, and must never accept a raw path string. The caller supplies an
/// <see cref="AuthorizedRoot"/> that already exists because the user selected and
/// confirmed it.
/// </remarks>
public interface IMetadataIndexService
{
    Task<IndexUpdateResult> RefreshAsync(
        AuthorizedRoot root,
        MetadataScanOptions options,
        CancellationToken cancellationToken = default);

    Task<FileIndexStatistics> GetStatisticsAsync(Guid rootId, CancellationToken cancellationToken = default);

    Task ForgetAsync(Guid rootId, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of one refresh, including refusals and per-item scan problems.</summary>
public sealed record IndexUpdateResult(
    bool IsAllowed,
    string Explanation,
    FileIndexSyncResult Changes,
    IReadOnlyList<ScanIssue> Issues)
{
    public static IndexUpdateResult Refused(string explanation) =>
        new(false, explanation, FileIndexSyncResult.Empty, []);

    /// <summary>True when the look hit its item limit, so part of the folder was not seen.</summary>
    public bool StoppedEarly => Issues.Any(issue => issue.Code == ScanIssueCode.EntryLimitReached);

    /// <summary>How many folders were too deep to enter.</summary>
    public int DeepFoldersSkipped => Issues.Count(issue => issue.Code == ScanIssueCode.DepthLimitReached);
}
