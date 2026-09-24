namespace DeskAI.Core.Files;

public abstract record ScanEvent;

public sealed record FileDiscovered(FileItem File) : ScanEvent;

/// <summary>A folder the scan came across, entered or not. Reported so a caller can list empty folders too.</summary>
/// <param name="CreatedAtUtc">When it was made; moving it keeps this (ADR 0044). Null when not known.</param>
/// <param name="ModifiedAtUtc">Its own last-changed time, which moves when something directly inside is added, removed, or renamed.</param>
public sealed record FolderDiscovered(
    string RelativePath,
    FileTraits Traits,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? ModifiedAtUtc = null) : ScanEvent;

public sealed record ScanIssue(
    string RelativePath,
    ScanIssueCode Code,
    string Explanation) : ScanEvent;

public enum ScanIssueCode
{
    RootUnavailable,
    RootProtected,
    UnsupportedRoot,
    AccessDenied,
    EntryDisappeared,
    MetadataUnavailable,
    ReparsePointSkipped,
    ProtectedEntrySkipped,
    UnsafePathSkipped,
    DepthLimitReached,
    EntryLimitReached,
}
