using DeskAI.Core.Indexing;

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

    Task<FileIndexStatistics> GetStatisticsAsync(Guid rootId, CancellationToken cancellationToken = default);

    /// <summary>Forgets everything remembered about one root. Files on disk are untouched.</summary>
    Task ClearRootAsync(Guid rootId, CancellationToken cancellationToken = default);
}
