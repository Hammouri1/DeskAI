using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.QuickSearch;

/// <summary>One row in the quick search bar.</summary>
/// <remarks>Carries the folder's ID and a path relative to it, never a full path, like <see cref="SearchHit"/>.</remarks>
public sealed record QuickSearchRow(Guid RootId, string RelativePath, string Name, string Where, FileCategory Category, OpenChoice Choice)
{
    /// <summary>For Words inside rows: the short piece of text around the match. Untrusted; shown as plain text only.</summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>For Words inside rows: "Page 3", "Slide 2", when known.</summary>
    public string? Section { get; init; }

    /// <summary>Identifies the file across the two groups, so it is never listed twice.</summary>
    public string Key => $"{RootId:N}|{RelativePath}";
}

/// <summary>The one plain fact the bar states under the By name group, if any.</summary>
public enum NameFact { None, NoFolders, NotUnderstood, NothingMatched, MoreThanShown }

public sealed record NameResults(IReadOnlyList<QuickSearchRow> Rows, NameFact Fact, bool AnyFolderReadsInside);

public sealed record InsideResults(IReadOnlyList<QuickSearchRow> Rows, bool WasSearched, int FilesRead, bool ReachedLimit)
{
    public static InsideResults NotSearched { get; } = new([], false, 0, false);
}

/// <summary>
/// The quick search bar's two looks: remembered names first, then the words inside files.
/// </summary>
/// <remarks>
/// <para>
/// Adds no reading power of its own. Names come from <see cref="FileSearchService"/>; words inside
/// come from <see cref="ContentSearchService.SearchAsync(string?, DateTimeOffset, Guid?, CancellationToken)"/>,
/// with exactly the per-folder, Word and Excel, PDF, and slide permissions and the 50-file and
/// 20-second limits Search has. It never calls the scanned-PDF reader or picture reading (ADR 0047).
/// </para>
/// <para>
/// It holds no launcher, AI connection, setting changer, executor, or writer, and stores nothing:
/// the rows live as long as the bar shows them. A test asserts it.
/// </para>
/// </remarks>
public sealed class QuickSearchService(FileSearchService names, ContentSearchService inside, IAuthorizedRootRepository roots)
{
    /// <summary>Rows per group. Small on purpose: this is a pop-up, and "See more in DeskAI" shows everything.</summary>
    public const int MaxRows = 5;

    private readonly FileSearchService _names = names;
    private readonly ContentSearchService _inside = inside;
    private readonly IAuthorizedRootRepository _roots = roots;

    public async Task<NameResults> FindByNameAsync(string? phrase, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var connected = await _roots.ListAsync(cancellationToken).ConfigureAwait(false);
        var anyInside = connected.Any(RootCapabilities.CanReadContent);
        if (!connected.Any(FileSearchService.IsSearchable))
        {
            return new NameResults([], NameFact.NoFolders, anyInside);
        }

        var outcome = await _names.SearchAsync(phrase, nowUtc, selectedRootId: null, cancellationToken).ConfigureAwait(false);
        if (outcome.UnderstoodNothing)
        {
            return new NameResults([], NameFact.NotUnderstood, anyInside);
        }

        var rows = outcome.Hits
            .Take(MaxRows)
            .Select(hit => new QuickSearchRow(hit.RootId, hit.File.RelativePath, hit.File.Name,
                WhereText(hit.RootName, hit.File.RelativePath), hit.File.Category, FileOpenRule.For(hit.File.Name)))
            .ToArray();
        var fact = outcome.Hits.Count == 0 ? NameFact.NothingMatched
            : outcome.Hits.Count > MaxRows ? NameFact.MoreThanShown
            : NameFact.None;
        return new NameResults(rows, fact, anyInside);
    }

    public async Task<InsideResults> FindInsideAsync(
        string? phrase,
        DateTimeOffset nowUtc,
        IReadOnlySet<string> alreadyListed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alreadyListed);
        var outcome = await _inside.SearchAsync(phrase, nowUtc, selectedRootId: null, cancellationToken).ConfigureAwait(false);
        if (!outcome.WasSearched)
        {
            return InsideResults.NotSearched;
        }

        // The index knows each file's kind, but a snippet row only needs a plain icon; Unknown
        // keeps this from asking the index a second time for every hit.
        var rows = outcome.Hits
            .Select(hit => new QuickSearchRow(hit.RootId, hit.RelativePath, hit.Name, WhereText(hit.RootName, hit.RelativePath),
                FileCategory.Unknown, FileOpenRule.For(hit.Name))
            {
                Snippet = hit.Snippet,
                Section = hit.Section,
            })
            .Where(row => !alreadyListed.Contains(row.Key))
            .Take(MaxRows)
            .ToArray();
        return new InsideResults(rows, true, outcome.FilesRead, outcome.ReachedLimit);
    }

    /// <summary>"Documents › School": the folder's name and the folders inside it, never a full path.</summary>
    public static string WhereText(string rootName, string relativePath)
    {
        var folder = Path.GetDirectoryName(relativePath);
        var parts = string.IsNullOrEmpty(folder)
            ? []
            : folder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" › ", new[] { rootName }.Concat(parts));
    }
}
