namespace DeskAI.Core.Files;

public abstract record ScanEvent;

public sealed record FileDiscovered(FileItem File) : ScanEvent;

/// <summary>A folder the scan came across, entered or not. Reported so a caller can list empty folders too.</summary>
public sealed record FolderDiscovered(string RelativePath, FileTraits Traits) : ScanEvent;

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
