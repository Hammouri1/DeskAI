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
/// <remarks>
/// <see cref="LastLookedAtUtc"/> is when DeskAI last looked at the folder even if nothing had
/// changed; <see cref="LastIndexedAtUtc"/> only moves when a remembered file was written.
/// <see cref="StoppedEarly"/> and <see cref="DeepFoldersSkipped"/> describe that last look, so
/// a page can say some files may be missing instead of implying a full sweep.
/// </remarks>
public sealed record FileIndexStatistics(
    int FileCount,
    long TotalSizeBytes,
    DateTimeOffset? LastIndexedAtUtc,
    DateTimeOffset? LastLookedAtUtc = null,
    bool StoppedEarly = false,
    int DeepFoldersSkipped = 0)
{
    public static FileIndexStatistics Empty { get; } = new(0, 0, null);
}

/// <summary>How one look at a folder went, recorded alongside the files it found.</summary>
/// <remarks>
/// A look that <see cref="StoppedEarly"/> saw only part of the folder. Not reaching a file is
/// no evidence that it is gone, so the index keeps what it remembered about the rest instead
/// of forgetting it as "no longer there".
/// </remarks>
public sealed record FileIndexLook(DateTimeOffset LookedAtUtc, bool StoppedEarly, int DeepFoldersSkipped)
{
    public static FileIndexLook Complete(DateTimeOffset lookedAtUtc) => new(lookedAtUtc, false, 0);
}
