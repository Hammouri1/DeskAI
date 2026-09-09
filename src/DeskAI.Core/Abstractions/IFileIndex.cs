using DeskAI.Core.Indexing;
using DeskAI.Core.Search;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Stores remembered file metadata for authorized roots.
/// </summary>
/// <remarks>
/// Every member is scoped to a single root ID on purpose: there is no "read the whole
/// index" operation, so one root's contents cannot leak into another root's results.
/// The index holds metadata only and never file content.
/// </remarks>
public interface IFileIndex
{
    /// <summary>
    /// Makes the stored entries for one root match <paramref name="files"/>, writing only
    /// the difference and forgetting rows whose files were not seen this time.
    /// </summary>
    Task<FileIndexSyncResult> SynchronizeRootAsync(
        Guid rootId,
        IReadOnlyList<IndexedFile> files,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedFile>> ListForRootAsync(Guid rootId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the remembered entries for one root that match <paramref name="query"/>,
    /// ordered by path and capped at the query's limit.
    /// </summary>
    /// <remarks>
    /// The root is a separate argument rather than part of the query so that a query built
    /// from untrusted input cannot select a root the caller did not authorize. This is a
    /// read of remembered metadata only: it touches no file and produces no plan.
    /// </remarks>
    Task<IReadOnlyList<IndexedFile>> SearchRootAsync(
        Guid rootId,
        SearchQuery query,
        CancellationToken cancellationToken = default);

    Task<FileIndexStatistics> GetStatisticsAsync(Guid rootId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Summarizes one root: how much each category holds, the largest few files, and how
    /// much has not changed since <paramref name="unchangedSinceUtc"/>.
    /// </summary>
    /// <remarks>
    /// Implementations must aggregate in the store rather than by reading every row, so a
    /// very large folder stays affordable to summarize. This reads remembered metadata and
    /// touches no file.
    /// </remarks>
    Task<RootStorageSummary> SummarizeRootAsync(
        Guid rootId,
        DateTimeOffset unchangedSinceUtc,
        int largestFileCount,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets everything remembered about one root. Files on disk are untouched.</summary>
    Task ClearRootAsync(Guid rootId, CancellationToken cancellationToken = default);
}
