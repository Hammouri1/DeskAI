using DeskAI.Core.Abstractions;
using DeskAI.Core.Indexing;

namespace DeskAI.Core.Search;

/// <summary>
/// Finds files that might be copies of each other, using remembered sizes only.
/// </summary>
/// <remarks>
/// <para>
/// This is the first of the two stages the roadmap describes. It groups files by exact byte
/// size, which is cheap, needs no file access, and rules out the overwhelming majority of
/// pairs. Sharing a size is evidence, not proof, so every result is a <em>possible</em>
/// duplicate and must be presented that way.
/// </para>
/// <para>
/// The second stage, comparing contents by hash, is deliberately not here. Hashing reads
/// the bytes of a file, and these folders were connected under a metadata-only
/// authorization that does not permit that. Confirming duplicates therefore belongs with
/// the permission-gated content work, not sneaked in behind a size check.
/// </para>
/// <para>
/// Nothing here proposes removing anything. The report describes what might be duplicated
/// and stops.
/// </para>
/// </remarks>
public sealed class DuplicateFinderService(IAuthorizedRootRepository roots, IFileIndex index)
{
    /// <summary>
    /// Files smaller than this are ignored.
    /// </summary>
    /// <remarks>
    /// Small files collide on size constantly — empty files, tiny configs, icons — so
    /// reporting them would bury the real candidates in noise while offering almost nothing
    /// to reclaim. Four kilobytes is one common filesystem block.
    /// </remarks>
    public const long MinimumSizeBytes = 4 * 1024;

    /// <summary>Caps how many groups are examined, so one folder cannot stall the page.</summary>
    public const int MaxGroups = 50;

    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IFileIndex _index = index;

    public async Task<DuplicateReport> FindAsync(CancellationToken cancellationToken = default)
    {
        var included = (await _roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(FileSearchService.IsSearchable)
            .ToArray();

        if (included.Length == 0)
        {
            return DuplicateReport.Empty;
        }

        // Merged across roots before deciding what repeats, so a file copied into a second
        // connected folder still counts as a candidate.
        var countsBySize = new Dictionary<long, int>();
        foreach (var root in included)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var group in await _index
                .GetSizeCountsAsync(root.Id, MinimumSizeBytes, cancellationToken)
                .ConfigureAwait(false))
            {
                countsBySize[group.SizeBytes] =
                    countsBySize.GetValueOrDefault(group.SizeBytes) + group.FileCount;
            }
        }

        // Largest possible saving first: that is where a real duplicate would matter most.
        var repeated = countsBySize
            .Where(pair => pair.Value > 1)
            .OrderByDescending(pair => pair.Key * (pair.Value - 1))
            .ToArray();

        var reachedLimit = repeated.Length > MaxGroups;
        var groups = new List<DuplicateGroup>();

        foreach (var (sizeBytes, _) in repeated.Take(MaxGroups))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = new List<DuplicateCandidate>();

            foreach (var root in included)
            {
                var query = new SearchQuery(
                    minSizeBytes: sizeBytes,
                    maxSizeBytes: sizeBytes,
                    limit: SearchQuery.DefaultLimit);

                foreach (var file in await _index
                    .SearchRootAsync(root.Id, query, cancellationToken)
                    .ConfigureAwait(false))
                {
                    files.Add(new DuplicateCandidate(root.DisplayName, file.RelativePath, file.Name));
                }
            }

            // A size can stop repeating between the two reads if the index changed, so the
            // group is only kept when more than one file is actually still there.
            if (files.Count > 1)
            {
                groups.Add(new DuplicateGroup(sizeBytes, files.AsReadOnly()));
            }
        }

        return new DuplicateReport(groups.AsReadOnly(), included.Length, reachedLimit);
    }
}
