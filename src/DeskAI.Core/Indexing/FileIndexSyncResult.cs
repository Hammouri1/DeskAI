namespace DeskAI.Core.Indexing;

/// <summary>
/// What one incremental index refresh actually changed.
/// </summary>
/// <remarks>
/// Reported honestly so the UI can say "nothing changed" instead of implying work
/// happened. <see cref="Removed"/> counts index rows forgotten because the file was no
/// longer found; it never means a file was deleted from disk.
/// </remarks>
public sealed record FileIndexSyncResult(int Added, int Updated, int Unchanged, int Removed)
{
    public static FileIndexSyncResult Empty { get; } = new(0, 0, 0, 0);

    public int TotalSeen => Added + Updated + Unchanged;

    public bool ChangedAnything => Added > 0 || Updated > 0 || Removed > 0;
}

/// <summary>A small, non-sensitive summary of what one root contributes to the index.</summary>
public sealed record FileIndexStatistics(int FileCount, long TotalSizeBytes, DateTimeOffset? LastIndexedAtUtc)
{
    public static FileIndexStatistics Empty { get; } = new(0, 0, null);
}
