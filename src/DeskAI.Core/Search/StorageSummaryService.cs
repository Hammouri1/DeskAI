using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Indexing;

namespace DeskAI.Core.Search;

/// <summary>
/// Builds the storage picture across every connected folder.
/// </summary>
/// <remarks>
/// <para>
/// Scope comes from <see cref="FileSearchService.IsSearchable"/>, the same predicate the
/// search screen uses, so a summary can never describe a folder search would not look in.
/// </para>
/// <para>
/// This describes and never proposes. It produces no plan and cannot start an operation.
/// Cleanup a person eventually chooses still goes through the ordinary preview and approval
/// path, so nothing here is a shortcut around it.
/// </para>
/// </remarks>
public sealed class StorageSummaryService(IAuthorizedRootRepository roots, IFileIndex index)
{
    /// <summary>How many large files are worth showing before the list stops being useful.</summary>
    public const int LargestFileCount = 10;

    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IFileIndex _index = index;

    /// <summary>
    /// How long a file must sit unchanged before it is counted as old.
    /// </summary>
    /// <remarks>
    /// Six months is long enough that a file is unlikely to be part of anything in progress,
    /// and short enough to be worth mentioning. It is stated here rather than buried so the
    /// UI can say "not changed in six months" instead of implying a judgement DeskAI has not
    /// actually made.
    /// </remarks>
    public static TimeSpan OldFileAge { get; } = TimeSpan.FromDays(183);

    public async Task<StorageSummary> BuildAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var included = (await _roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(FileSearchService.IsSearchable)
            .ToArray();

        if (included.Length == 0)
        {
            return StorageSummary.Empty;
        }

        var unchangedSince = nowUtc - OldFileAge;
        var totals = new Dictionary<FileCategory, CategoryUsage>();
        var largest = new List<LargestFile>();
        var totalFiles = 0;
        var totalBytes = 0L;
        var oldCount = 0;
        var oldBytes = 0L;
        DateTimeOffset? lastChecked = null;

        foreach (var root in included)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var statistics = await _index.GetStatisticsAsync(root.Id, cancellationToken).ConfigureAwait(false);
            totalFiles += statistics.FileCount;
            totalBytes += statistics.TotalSizeBytes;

            // The oldest reading wins, so the age shown is never newer than the least
            // recently checked folder.
            if (statistics.LastIndexedAtUtc is { } checkedAt
                && (lastChecked is null || checkedAt < lastChecked))
            {
                lastChecked = checkedAt;
            }

            var summary = await _index
                .SummarizeRootAsync(root.Id, unchangedSince, LargestFileCount, cancellationToken)
                .ConfigureAwait(false);

            foreach (var usage in summary.Categories)
            {
                totals[usage.Category] = totals.TryGetValue(usage.Category, out var running)
                    ? running with
                    {
                        FileCount = running.FileCount + usage.FileCount,
                        TotalSizeBytes = running.TotalSizeBytes + usage.TotalSizeBytes,
                    }
                    : usage;
            }

            foreach (var file in summary.LargestFiles)
            {
                largest.Add(new LargestFile(root.DisplayName, file.RelativePath, file.Name, file.SizeBytes));
            }

            oldCount += summary.OldFileCount;
            oldBytes += summary.OldFileBytes;
        }

        return new StorageSummary(
            included.Length,
            totalFiles,
            totalBytes,
            totals.Values.OrderByDescending(usage => usage.TotalSizeBytes).ToList().AsReadOnly(),
            largest.OrderByDescending(file => file.SizeBytes).Take(LargestFileCount).ToList().AsReadOnly(),
            oldCount,
            oldBytes,
            lastChecked);
    }
}
