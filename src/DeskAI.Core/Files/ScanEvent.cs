namespace DeskAI.Core.Files;

public abstract record ScanEvent;

public sealed record FileDiscovered(FileItem File) : ScanEvent;

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
