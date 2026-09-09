using DeskAI.Core.Classification;

namespace DeskAI.Core.Indexing;

/// <summary>How much of one category a folder holds.</summary>
public sealed record CategoryUsage(FileCategory Category, int FileCount, long TotalSizeBytes);

/// <summary>What one root contributes to the storage picture.</summary>
/// <remarks>
/// Aggregates are computed by the store rather than by loading every row, so a folder with
/// a hundred thousand files costs about the same as one with ten.
/// </remarks>
public sealed record RootStorageSummary(
    IReadOnlyList<CategoryUsage> Categories,
    IReadOnlyList<IndexedFile> LargestFiles,
    int OldFileCount,
    long OldFileBytes)
{
    public static RootStorageSummary Empty { get; } = new([], [], 0, 0);
}

/// <summary>One large file, named the way a person can place it.</summary>
/// <remarks>
/// Holds a folder name and a path relative to it, never an absolute path, for the same
/// reason search results do.
/// </remarks>
public sealed record LargestFile(string RootName, string RelativePath, string Name, long SizeBytes);

/// <summary>
/// The storage picture across every connected folder.
/// </summary>
/// <remarks>
/// <para>
/// This is a reading of remembered metadata, not of the live disk.
/// <see cref="LastCheckedUtc"/> is carried so the UI can say when the numbers were true
/// rather than implying they are current, and <see cref="FoldersIncluded"/> so it can state
/// the real scope.
/// </para>
/// <para>
/// A summary describes; it never proposes. Nothing here can become a file operation, and
/// any cleanup a person eventually chooses still goes through the ordinary preview and
/// approval path.
/// </para>
/// </remarks>
public sealed record StorageSummary(
    int FoldersIncluded,
    int TotalFiles,
    long TotalSizeBytes,
    IReadOnlyList<CategoryUsage> Categories,
    IReadOnlyList<LargestFile> LargestFiles,
    int OldFileCount,
    long OldFileBytes,
    DateTimeOffset? LastCheckedUtc)
{
    public static StorageSummary Empty { get; } = new(0, 0, 0, [], [], 0, 0, null);

    public bool HasAnything => TotalFiles > 0;
}
