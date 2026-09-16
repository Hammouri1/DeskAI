using DeskAI.Core.Abstractions;
using DeskAI.Core.Search;

namespace DeskAI.Core.Workspace;

public enum PinnedCountKind
{
    /// <summary>The search ran and <see cref="PinnedCount.Files"/> is how many files it found.</summary>
    Counted,

    /// <summary>The search stopped at its result limit, so the real number is at least this.</summary>
    AtLimit,

    /// <summary>No folder may be searched, so there is no count to give.</summary>
    NoFolders,

    /// <summary>The saved words produce nothing to search for, so there is no count to give.</summary>
    NotUnderstood,
}

/// <summary>What a pinned tile may truthfully say about its search.</summary>
public sealed record PinnedCount(PinnedCountKind Kind, int Files);

/// <summary>
/// Pins saved searches to My workspace and counts what each finds.
/// </summary>
/// <remarks>
/// <para>
/// Counting is an ordinary search through <see cref="FileSearchService"/>, so a tile can only
/// ever describe folders search may look in, and it reads remembered names, sizes, and dates —
/// never a file. It holds nothing that can change a file, read one, or reach AI.
/// </para>
/// <para>
/// A count is kept apart from the two cases where there is no count at all. Showing "0 files"
/// when nothing is connected, or when the words mean nothing, would present a guess as a fact.
/// </para>
/// </remarks>
public sealed class PinnedSearchService(ISavedSearchRepository searches, FileSearchService search, IClock clock)
{
    private readonly ISavedSearchRepository _searches = searches;
    private readonly FileSearchService _search = search;
    private readonly IClock _clock = clock;

    public async Task<IReadOnlyList<SavedSearch>> ListPinnedAsync(CancellationToken cancellationToken = default) =>
        (await _searches.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(saved => saved.IsPinned)
            .ToArray();

    /// <summary>
    /// Pins a saved search.
    /// </summary>
    /// <returns>
    /// True when it is pinned afterwards. False when it does not exist, or when
    /// <see cref="SavedSearch.MaxPinned"/> searches are already pinned.
    /// </returns>
    public async Task<bool> PinAsync(Guid savedSearchId, CancellationToken cancellationToken = default)
    {
        var all = await _searches.ListAsync(cancellationToken).ConfigureAwait(false);
        var target = all.FirstOrDefault(saved => saved.Id == savedSearchId);
        if (target is null)
        {
            return false;
        }

        if (target.IsPinned)
        {
            return true;
        }

        if (all.Count(saved => saved.IsPinned) >= SavedSearch.MaxPinned)
        {
            return false;
        }

        await _searches.SetPinnedAsync(savedSearchId, true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task UnpinAsync(Guid savedSearchId, CancellationToken cancellationToken = default) =>
        _searches.SetPinnedAsync(savedSearchId, false, cancellationToken);

    public async Task<PinnedCount> CountAsync(SavedSearch saved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saved);

        var outcome = await _search.SearchAsync(saved.Phrase, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (outcome.FoldersSearched == 0)
        {
            return new PinnedCount(PinnedCountKind.NoFolders, 0);
        }

        if (outcome.UnderstoodNothing)
        {
            return new PinnedCount(PinnedCountKind.NotUnderstood, 0);
        }

        return new PinnedCount(
            outcome.ReachedLimit ? PinnedCountKind.AtLimit : PinnedCountKind.Counted,
            outcome.Hits.Count);
    }
}
