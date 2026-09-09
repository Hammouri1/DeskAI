using System.Buffers;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;

namespace DeskAI.Core.Search;

/// <summary>
/// A validated, bounded description of what to look for inside one authorized root.
/// </summary>
/// <remarks>
/// <para>
/// The query deliberately carries no root ID. Callers pass the root separately to
/// <see cref="Abstractions.IFileIndex"/>, so a query value can never widen its own scope,
/// and a query built from untrusted input cannot reach a root the caller did not choose.
/// </para>
/// <para>
/// Every filter is validated here rather than at the storage boundary. A contradictory
/// range is rejected outright instead of quietly degrading into "match everything",
/// because a search that silently widens is worse than one that refuses.
/// </para>
/// <para>
/// This is a class rather than a record on purpose: it holds collections, and the
/// compiler-generated record equality would compare those by reference and therefore lie.
/// </para>
/// </remarks>
public sealed class SearchQuery
{
    /// <summary>Bounds the pattern so one hostile query cannot build a pathological match.</summary>
    public const int MaxTextLength = 128;

    /// <summary>Bounds how many extension parameters a single query can bind.</summary>
    public const int MaxExtensions = 32;

    public const int DefaultLimit = 200;

    /// <summary>A hard ceiling on rows, so no query can exhaust memory.</summary>
    public const int MaxLimit = 1000;

    /// <summary>
    /// Characters that prove a value is a path fragment or a pattern rather than a file
    /// ending, so it is rejected instead of being matched as a filename suffix.
    /// </summary>
    private static readonly SearchValues<char> NotInAFileEnding = SearchValues.Create(@".\/:*?");

    public SearchQuery(
        string? pathContains = null,
        IEnumerable<string>? extensions = null,
        IEnumerable<FileCategory>? categories = null,
        IEnumerable<FileKind>? kinds = null,
        long? minSizeBytes = null,
        long? maxSizeBytes = null,
        DateTimeOffset? modifiedAfterUtc = null,
        DateTimeOffset? modifiedBeforeUtc = null,
        int limit = DefaultLimit)
    {
        PathContains = NormalizeText(pathContains);
        Extensions = NormalizeExtensions(extensions);
        Categories = Freeze(categories);
        Kinds = Freeze(kinds);

        if (minSizeBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSizeBytes), "A size filter cannot be negative.");
        }

        if (maxSizeBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSizeBytes), "A size filter cannot be negative.");
        }

        if (minSizeBytes is { } min && maxSizeBytes is { } max && min > max)
        {
            throw new ArgumentException("The smallest size cannot be larger than the largest size.", nameof(minSizeBytes));
        }

        if (modifiedAfterUtc is { } after && modifiedBeforeUtc is { } before && after > before)
        {
            throw new ArgumentException("The start of the date range cannot be after its end.", nameof(modifiedAfterUtc));
        }

        if (limit is <= 0 or > MaxLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"A search must return between 1 and {MaxLimit} results.");
        }

        MinSizeBytes = minSizeBytes;
        MaxSizeBytes = maxSizeBytes;
        ModifiedAfterUtc = modifiedAfterUtc;
        ModifiedBeforeUtc = modifiedBeforeUtc;
        Limit = limit;
    }

    /// <summary>
    /// Matched anywhere in the file's path relative to the root, so a folder name counts
    /// as well as the file name. Null when no text filter was given.
    /// </summary>
    public string? PathContains { get; }

    /// <summary>Lower-case extensions including the leading dot. Empty means "any".</summary>
    public IReadOnlySet<string> Extensions { get; }

    /// <summary>Empty means "any category".</summary>
    public IReadOnlySet<FileCategory> Categories { get; }

    /// <summary>Empty means "any kind".</summary>
    public IReadOnlySet<FileKind> Kinds { get; }

    public long? MinSizeBytes { get; }

    public long? MaxSizeBytes { get; }

    public DateTimeOffset? ModifiedAfterUtc { get; }

    public DateTimeOffset? ModifiedBeforeUtc { get; }

    /// <summary>Always set, always bounded. There is no unlimited search.</summary>
    public int Limit { get; }

    /// <summary>
    /// False when the query would return everything in the root up to <see cref="Limit"/>.
    /// The UI uses this to say so plainly rather than implying a search happened.
    /// </summary>
    public bool HasFilters =>
        PathContains is not null
        || Extensions.Count > 0
        || Categories.Count > 0
        || Kinds.Count > 0
        || MinSizeBytes.HasValue
        || MaxSizeBytes.HasValue
        || ModifiedAfterUtc.HasValue
        || ModifiedBeforeUtc.HasValue;

    private static string? NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length > MaxTextLength
            ? throw new ArgumentException(
                $"Search text cannot be longer than {MaxTextLength} characters.",
                nameof(text))
            : trimmed;
    }

    private static HashSet<string> NormalizeExtensions(IEnumerable<string>? extensions)
    {
        if (extensions is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in extensions)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new ArgumentException("An extension filter cannot be blank.", nameof(extensions));
            }

            var value = raw.Trim().ToLowerInvariant();
            if (!value.StartsWith('.'))
            {
                value = '.' + value;
            }

            // An extension is a suffix, never a path fragment or a pattern. Rejecting these
            // keeps a stray path or wildcard from being treated as a filename ending.
            if (value.AsSpan(1).ContainsAny(NotInAFileEnding))
            {
                throw new ArgumentException($"'{raw}' is not a file ending.", nameof(extensions));
            }

            normalized.Add(value);
        }

        return normalized.Count > MaxExtensions
            ? throw new ArgumentException(
                $"A search can filter on at most {MaxExtensions} file endings.",
                nameof(extensions))
            : normalized;
    }

    private static HashSet<T> Freeze<T>(IEnumerable<T>? values)
        where T : struct, Enum =>
        values is null ? new HashSet<T>() : new HashSet<T>(values);
}
