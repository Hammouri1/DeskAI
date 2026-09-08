using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Indexing;

/// <summary>
/// Turns a bounded read-only metadata scan of one authorized root into index entries.
/// </summary>
/// <remarks>
/// This service is the only path into <see cref="IFileIndex"/> that touches the
/// filesystem, and it reaches the filesystem solely through <see cref="IFileScanner"/>,
/// which opens no file content. It refuses a protected root before scanning, so a
/// mistake elsewhere cannot turn indexing into a way to enumerate a blocked location.
/// </remarks>
public sealed class MetadataIndexService(
    IFileScanner scanner,
    IFileClassifier classifier,
    IFileIndex index,
    IPathPolicy pathPolicy,
    IClock clock) : IMetadataIndexService
{
    public async Task<IndexUpdateResult> RefreshAsync(
        AuthorizedRoot root,
        MetadataScanOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);

        if (root.Permission == RootAccessLevel.Protected)
        {
            return IndexUpdateResult.Refused("This folder is protected, so DeskAI did not look inside it.");
        }

        if (pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked)
        {
            return IndexUpdateResult.Refused("This location is protected, so DeskAI did not look inside it.");
        }

        var indexedAtUtc = clock.UtcNow;
        var files = new List<IndexedFile>();
        var issues = new List<ScanIssue>();

        await foreach (var scanEvent in scanner.ScanAsync(root, options, cancellationToken).ConfigureAwait(false))
        {
            switch (scanEvent)
            {
                case FileDiscovered discovered:
                    // The scanner reports FileKind.Unknown; deterministic classification is a
                    // separate stage so the index records why a file was categorized.
                    var classification = classifier.Classify(discovered.File);
                    files.Add(new IndexedFile(
                        root.Id,
                        discovered.File.Id,
                        discovered.File.RelativePath,
                        classification.Kind,
                        classification.Category,
                        discovered.File.SizeBytes,
                        discovered.File.CreatedAtUtc,
                        discovered.File.ModifiedAtUtc,
                        indexedAtUtc));
                    break;
                case ScanIssue issue:
                    issues.Add(issue);
                    break;
            }
        }

        var changes = await index
            .SynchronizeRootAsync(root.Id, files, cancellationToken)
            .ConfigureAwait(false);

        return new IndexUpdateResult(true, Describe(changes, files.Count), changes, issues.AsReadOnly());
    }

    public Task<FileIndexStatistics> GetStatisticsAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        index.GetStatisticsAsync(rootId, cancellationToken);

    public Task ForgetAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        index.ClearRootAsync(rootId, cancellationToken);

    private static string Describe(FileIndexSyncResult changes, int fileCount)
    {
        if (fileCount == 0)
        {
            return "No files were found to remember. File contents were not opened.";
        }

        return changes.ChangedAnything
            ? $"Remembered {fileCount} file name(s): {changes.Added} new, {changes.Updated} updated, {changes.Removed} no longer there. File contents were not opened."
            : $"Remembered {fileCount} file name(s). Nothing changed since last time. File contents were not opened.";
    }
}
