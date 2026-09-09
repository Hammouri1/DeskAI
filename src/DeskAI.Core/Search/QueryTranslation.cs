namespace DeskAI.Core.Search;

/// <summary>Which part of a <see cref="SearchQuery"/> a chip explains.</summary>
public enum QueryFilter
{
    Text,
    FileEnding,
    Category,
    MinimumSize,
    MaximumSize,
    ChangedAfter,
}

/// <summary>
/// One understood piece of a typed phrase, written for a person to read back.
/// </summary>
/// <remarks>
/// Chips exist so interpretation is visible and correctable. A search that quietly decides
/// what someone meant is worse than one that shows its reading and lets them remove a part
/// of it, so every filter the translator sets produces exactly one chip.
/// </remarks>
public sealed record QueryChip(QueryFilter Filter, string Label);

/// <summary>
/// The result of reading a typed phrase: what it means, and how that was understood.
/// </summary>
/// <remarks>
/// <see cref="UnderstoodAnything"/> is the honest signal. When it is false the query has no
/// filters, so running it would list the whole folder. Callers must say they did not
/// understand rather than presenting that listing as a search result.
/// </remarks>
public sealed record QueryTranslation(
    SearchQuery Query,
    IReadOnlyList<QueryChip> Chips)
{
    /// <summary>True when at least one part of the phrase became a filter.</summary>
    public bool UnderstoodAnything => Chips.Count > 0;
}
