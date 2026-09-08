using DeskAI.Core.Classification;
using DeskAI.Core.Files;

namespace DeskAI.Core.Indexing;

/// <summary>
/// One remembered file inside the local metadata index.
/// </summary>
/// <remarks>
/// The index is a convenience cache, never an authority. It proves only that a file
/// looked like this when it was last scanned, so any later mutation must still
/// revalidate the live filesystem. Entries therefore keep a root ID plus a
/// root-relative path and deliberately hold no absolute path and no file content.
/// </remarks>
public sealed record IndexedFile
{
    /// <summary>Bounds a single stored path so one hostile name cannot bloat the index.</summary>
    public const int MaxRelativePathLength = 1024;

    public IndexedFile(
        Guid rootId,
        Guid fileId,
        string relativePath,
        FileKind kind,
        FileCategory category,
        long sizeBytes,
        DateTimeOffset createdAtUtc,
        DateTimeOffset modifiedAtUtc,
        DateTimeOffset indexedAtUtc)
    {
        if (rootId == Guid.Empty)
        {
            throw new ArgumentException("An indexed file needs the authorized root it came from.", nameof(rootId));
        }

        if (fileId == Guid.Empty)
        {
            throw new ArgumentException("An indexed file needs a stable ID.", nameof(fileId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (relativePath.Length > MaxRelativePathLength)
        {
            throw new ArgumentException("The indexed path is longer than the supported limit.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("The index stores root-relative paths only.", nameof(relativePath));
        }

        var segments = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Indexed paths cannot contain traversal segments.", nameof(relativePath));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        RootId = rootId;
        FileId = fileId;
        RelativePath = string.Join(Path.DirectorySeparatorChar, segments);
        Name = segments[^1];
        Extension = Path.GetExtension(Name).ToLowerInvariant();
        Kind = kind;
        Category = category;
        SizeBytes = sizeBytes;
        CreatedAtUtc = createdAtUtc;
        ModifiedAtUtc = modifiedAtUtc;
        IndexedAtUtc = indexedAtUtc;
    }

    public Guid RootId { get; }

    public Guid FileId { get; }

    /// <summary>Normalized path relative to the authorized root, never absolute.</summary>
    public string RelativePath { get; }

    /// <summary>The final path segment, derived so it can never disagree with the path.</summary>
    public string Name { get; }

    /// <summary>Lower-case extension including the dot, or an empty string.</summary>
    public string Extension { get; }

    public FileKind Kind { get; }

    public FileCategory Category { get; }

    public long SizeBytes { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset ModifiedAtUtc { get; }

    /// <summary>When DeskAI last confirmed these facts by scanning.</summary>
    public DateTimeOffset IndexedAtUtc { get; }

    /// <summary>Compares only the facts a rescan can change, so unchanged files are not rewritten.</summary>
    public bool MatchesStoredFacts(IndexedFile other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return RelativePath.Equals(other.RelativePath, StringComparison.Ordinal)
            && Kind == other.Kind
            && Category == other.Category
            && SizeBytes == other.SizeBytes
            && CreatedAtUtc == other.CreatedAtUtc
            && ModifiedAtUtc == other.ModifiedAtUtc;
    }
}
