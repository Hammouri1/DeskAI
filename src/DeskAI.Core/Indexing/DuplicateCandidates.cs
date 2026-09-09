namespace DeskAI.Core.Indexing;

/// <summary>How many remembered files in one root share an exact byte size.</summary>
public sealed record SizeGroup(long SizeBytes, int FileCount);

/// <summary>One file that might be a duplicate of the others in its group.</summary>
/// <remarks>
/// Carries a folder name and a path relative to it, never an absolute path, for the same
/// reason search results do.
/// </remarks>
public sealed record DuplicateCandidate(string RootName, string RelativePath, string Name);

/// <summary>
/// Files that share an exact size, and therefore might be copies of each other.
/// </summary>
/// <remarks>
/// <para>
/// Sharing a size is evidence, not proof. Two different files can be the same length, so
/// this is deliberately named a candidate group and must be presented as "possible"
/// duplicates. Confirming would mean reading and comparing the bytes, which the
/// metadata-only authorization these folders were connected under does not permit.
/// </para>
/// <para>
/// <see cref="ReclaimableBytes"/> is what would be freed if every copy but one turned out
/// to be identical and were removed. It is a ceiling on the possible saving, not a promise,
/// and nothing here proposes removing anything.
/// </para>
/// </remarks>
public sealed record DuplicateGroup(long SizeBytes, IReadOnlyList<DuplicateCandidate> Files)
{
    public int Count => Files.Count;

    /// <summary>The most that could be freed if all but one copy were identical.</summary>
    public long ReclaimableBytes => SizeBytes * Math.Max(0, Files.Count - 1);
}

/// <summary>The possible-duplicate picture across every connected folder.</summary>
public sealed record DuplicateReport(
    IReadOnlyList<DuplicateGroup> Groups,
    int FoldersIncluded,
    bool ReachedGroupLimit)
{
    public static DuplicateReport Empty { get; } = new([], 0, false);

    public int TotalFiles => Groups.Sum(group => group.Count);

    /// <summary>A ceiling on what could be freed, never a promise that it can be.</summary>
    public long ReclaimableBytes => Groups.Sum(group => group.ReclaimableBytes);

    public bool HasAnything => Groups.Count > 0;
}
