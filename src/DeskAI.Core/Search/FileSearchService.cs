using DeskAI.Core.Abstractions;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Search;

/// <summary>One remembered file that matched, named the way a person can place it.</summary>
/// <remarks>
/// Carries the root's display name and the path relative to it, never an absolute path, so
/// showing a result cannot reveal where on the disk a folder actually lives.
/// </remarks>
public sealed record SearchHit(Guid RootId, string RootName, IndexedFile File);

/// <summary>
/// The full outcome of one search, including the scope it actually covered.
/// </summary>
/// <remarks>
/// <see cref="FoldersSearched"/> exists so the UI can state the real scope rather than
/// implying the whole computer was searched. <see cref="ReachedLimit"/> is reported so a
/// truncated list is never presented as a complete answer.
/// </remarks>
public sealed record SearchOutcome(
    QueryTranslation Translation,
    IReadOnlyList<SearchHit> Hits,
    int FoldersSearched,
    bool ReachedLimit)
{
    /// <summary>True when the phrase produced no filters, so no search was attempted.</summary>
    public bool UnderstoodNothing => !Translation.UnderstoodAnything;
}

/// <summary>
/// Runs a typed phrase against the local index across the folders the user connected.
/// </summary>
/// <remarks>
/// <para>
/// This lives in Core so the search rules are testable without a window, and so the App
/// layer holds no decision about which folders may be read.
/// </para>
/// <para>
/// Only roots the user marked <see cref="RootAccessLevel.Allowed"/> are searched. A
/// restricted or protected root is skipped even though its rows may exist in the index,
/// because permission is a live decision and not something a cached row can grant.
/// </para>
/// <para>
/// Nothing here touches the filesystem. It reads remembered metadata, which means a result
/// proves only how a file looked when last scanned.
/// </para>
/// </remarks>
public sealed class FileSearchService(IAuthorizedRootRepository roots, IFileIndex index)
{
    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IFileIndex _index = index;

    /// <summary>
    /// The single definition of "a folder search may look in".
    /// </summary>
    /// <remarks>
    /// Both the permission and the scope are checked. Filtering on permission alone would
    /// include the controlled demo workspace that the Organize page creates, so the page
    /// would report searching more folders than it lists, and claim to search a temporary
    /// folder the person never connected for that purpose. Anything that counts folders
    /// must use this predicate so the count and the list can never disagree.
    /// </remarks>
    public static bool IsSearchable(AuthorizedRoot root) => RootCapabilities.CanReadMetadata(root);

    /// <summary>
    /// Reads <paramref name="phrase"/> and returns what matched. <paramref name="nowUtc"/>
    /// anchors relative dates so the same phrase means the same thing every time.
    /// </summary>
    public async Task<SearchOutcome> SearchAsync(
        string? phrase,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        await SearchAsync(phrase, nowUtc, selectedRootId: null, cancellationToken).ConfigureAwait(false);

    /// <summary>Searches one selected connected folder, or all when no folder was selected.</summary>
    public async Task<SearchOutcome> SearchAsync(
        string? phrase,
        DateTimeOffset nowUtc,
        Guid? selectedRootId,
        CancellationToken cancellationToken = default)
    {
        var translation = NaturalLanguageQueryTranslator.Translate(phrase, nowUtc);
        var searchable = (await _roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(root => IsSearchable(root)
                && (selectedRootId is null || root.Id == selectedRootId))
            .ToArray();

        // An unfiltered query would list every remembered file, which is not a search
        // result. Reporting that nothing was understood is the honest answer.
        if (!translation.UnderstoodAnything)
        {
            return new SearchOutcome(translation, [], searchable.Length, ReachedLimit: false);
        }

        var hits = new List<SearchHit>();
        foreach (var root in searchable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hits.Count >= translation.Query.Limit)
            {
                break;
            }

            var matches = await _index
                .SearchRootAsync(root.Id, translation.Query, cancellationToken)
                .ConfigureAwait(false);

            foreach (var file in matches)
            {
                if (hits.Count >= translation.Query.Limit)
                {
                    break;
                }

                hits.Add(new SearchHit(root.Id, root.DisplayName, file));
            }
        }

        return new SearchOutcome(
            translation,
            hits,
            searchable.Length,
            ReachedLimit: hits.Count >= translation.Query.Limit);
    }
}
