using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Scanning;

public sealed class WindowsMetadataScanner(IPathPolicy pathPolicy) : IFileScanner
{
    public async IAsyncEnumerable<ScanEvent> ScanAsync(
        AuthorizedRoot root,
        MetadataScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (root.Permission == RootAccessLevel.Protected)
        {
            yield return Issue(".", ScanIssueCode.RootProtected, "The authorized root is protected and was not scanned.");
            yield break;
        }

        var canonicalization = TryCanonicalizeRoot(root.CanonicalPath);
        if (canonicalization.Issue is not null)
        {
            yield return canonicalization.Issue;
            yield break;
        }

        var canonicalRoot = canonicalization.Value!;
        if (IsUnsupportedRoot(canonicalRoot))
        {
            yield return Issue(".", ScanIssueCode.UnsupportedRoot, "Network, device, and non-drive roots are not supported.");
            yield break;
        }

        var rootSafety = pathPolicy.ValidateRoot(root);
        if (rootSafety.Status == ValidationStatus.Blocked)
        {
            yield return Issue(".", ScanIssueCode.RootProtected, "The authorized root overlaps a protected location.");
            yield break;
        }

        if (!Directory.Exists(canonicalRoot))
        {
            yield return Issue(".", ScanIssueCode.RootUnavailable, "The authorized root does not exist or is unavailable.");
            yield break;
        }

        var rootInspection = TryInspectRootChain(canonicalRoot);
        if (rootInspection is not null)
        {
            yield return rootInspection;
            yield break;
        }

        var pendingDirectories = new Stack<(DirectoryInfo Directory, int Depth)>();
        pendingDirectories.Push((new DirectoryInfo(canonicalRoot), 0));
        var entriesSeen = 0;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pendingDirectories.Pop();
            await Task.Yield();

            var opened = TryOpenDirectory(canonicalRoot, directory);
            if (opened.Issue is not null)
            {
                yield return opened.Issue;
                continue;
            }

            using var enumerator = opened.Value!;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = TryReadNext(canonicalRoot, directory, enumerator);
                if (next.Issue is not null)
                {
                    yield return next.Issue;
                    break;
                }

                if (next.IsEnd)
                {
                    break;
                }

                var entry = next.Value!;
                var relativePath = ToSafeRelativePath(canonicalRoot, entry.FullName);
                entriesSeen++;
                if (entriesSeen > options.MaxEntries)
                {
                    yield return Issue(
                        ".",
                        ScanIssueCode.EntryLimitReached,
                        "The configured metadata-scan entry limit was reached.");
                    yield break;
                }

                var safety = pathPolicy.ValidateRelativePath(root, relativePath);
                if (safety.Status == ValidationStatus.Blocked)
                {
                    var code = safety.ReasonCode is ValidationReasonCode.ProtectedEntry or ValidationReasonCode.ProtectedRoot
                        ? ScanIssueCode.ProtectedEntrySkipped
                        : ScanIssueCode.UnsafePathSkipped;
                    yield return Issue(relativePath, code, "The entry was skipped by deterministic path policy.");
                    continue;
                }

                var attributes = TryGetAttributes(relativePath, entry);
                if (attributes.Issue is not null)
                {
                    yield return attributes.Issue;
                    continue;
                }

                if ((attributes.Value & FileAttributes.ReparsePoint) != 0)
                {
                    yield return Issue(relativePath, ScanIssueCode.ReparsePointSkipped, "A link or reparse point was not followed.");
                    continue;
                }

                if ((attributes.Value & FileAttributes.Directory) != 0)
                {
                    yield return new FolderDiscovered(
                        NormalizeRelativePath(relativePath),
                        ToTraits(attributes.Value),
                        new DateTimeOffset(entry.CreationTimeUtc),
                        new DateTimeOffset(entry.LastWriteTimeUtc));
                    if (depth >= options.MaxDepth)
                    {
                        yield return Issue(
                            relativePath,
                            ScanIssueCode.DepthLimitReached,
                            "The directory was not entered because the configured depth limit was reached.");
                        continue;
                    }

                    pendingDirectories.Push((new DirectoryInfo(entry.FullName), depth + 1));
                    continue;
                }

                var file = TryCreateFileItem(root.Id, relativePath, entry, attributes.Value);
                if (file.Issue is not null)
                {
                    yield return file.Issue;
                    continue;
                }

                yield return new FileDiscovered(file.Value!);
            }
        }
    }

    private static Attempt<string> TryCanonicalizeRoot(string path)
    {
        try
        {
            return Attempt<string>.Success(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return Attempt<string>.Failure(Issue(
                ".",
                ScanIssueCode.UnsupportedRoot,
                "The authorized root path is malformed or unsupported."));
        }
    }

    private static Attempt<IEnumerator<FileSystemInfo>> TryOpenDirectory(string root, DirectoryInfo directory)
    {
        try
        {
            var enumerator = directory
                .EnumerateFileSystemInfos("*", CreateEnumerationOptions())
                .GetEnumerator();
            return Attempt<IEnumerator<FileSystemInfo>>.Success(enumerator);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return Attempt<IEnumerator<FileSystemInfo>>.Failure(Issue(
                ToSafeRelativePath(root, directory.FullName),
                MapIssueCode(exception),
                "The directory could not be opened for metadata scanning."));
        }
    }

    private static EnumerationStep TryReadNext(
        string root,
        DirectoryInfo directory,
        IEnumerator<FileSystemInfo> enumerator)
    {
        try
        {
            return enumerator.MoveNext()
                ? EnumerationStep.Item(enumerator.Current)
                : EnumerationStep.End();
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return EnumerationStep.Failure(Issue(
                ToSafeRelativePath(root, directory.FullName),
                MapIssueCode(exception),
                "A directory could not be enumerated completely."));
        }
    }

    private static Attempt<FileAttributes> TryGetAttributes(string relativePath, FileSystemInfo entry)
    {
        try
        {
            return Attempt<FileAttributes>.Success(entry.Attributes);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return Attempt<FileAttributes>.Failure(Issue(
                relativePath,
                MapIssueCode(exception),
                "Entry metadata became unavailable."));
        }
    }

    private static Attempt<FileItem> TryCreateFileItem(
        Guid rootId,
        string relativePath,
        FileSystemInfo entry,
        FileAttributes attributes)
    {
        try
        {
            var file = entry as FileInfo ?? new FileInfo(entry.FullName);
            return Attempt<FileItem>.Success(new FileItem(
                CreateStableId(rootId, relativePath),
                NormalizeRelativePath(relativePath),
                FileKind.Unknown,
                file.Length,
                new DateTimeOffset(file.CreationTimeUtc),
                new DateTimeOffset(file.LastWriteTimeUtc),
                ToTraits(attributes)));
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return Attempt<FileItem>.Failure(Issue(
                relativePath,
                MapIssueCode(exception),
                "File metadata became unavailable."));
        }
    }

    // Not named in the FileAttributes enum, but set by Windows on cloud placeholders.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>
    /// Reads the facts that decide whether a file should be left alone, from attributes the
    /// directory listing already returned. Nothing is opened to find them out.
    /// </summary>
    private static FileTraits ToTraits(FileAttributes attributes)
    {
        var traits = FileTraits.None;
        if ((attributes & FileAttributes.Hidden) != 0)
        {
            traits |= FileTraits.Hidden;
        }

        if ((attributes & FileAttributes.System) != 0)
        {
            traits |= FileTraits.System;
        }

        if ((attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0)
        {
            traits |= FileTraits.OnlineOnly;
        }

        return traits;
    }

    private static EnumerationOptions CreateEnumerationOptions() => new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0,
    };

    private static ScanIssue? TryInspectRootChain(string canonicalRoot)
    {
        try
        {
            var rootPrefix = Path.GetPathRoot(canonicalRoot);
            if (string.IsNullOrEmpty(rootPrefix))
            {
                return Issue(".", ScanIssueCode.UnsupportedRoot, "The authorized root has no supported drive root.");
            }

            var relative = canonicalRoot[rootPrefix.Length..];
            var current = rootPrefix;
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return Issue(".", ScanIssueCode.ReparsePointSkipped, "The authorized root crosses a link or reparse point.");
                }
            }

            return null;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return Issue(".", MapIssueCode(exception), "The authorized root could not be inspected safely.");
        }
    }

    private static bool IsUnsupportedRoot(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            path.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            return true;
        }

        var root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) || root.Length < 3 || root[1] != ':';
    }

    private static string ToSafeRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ? "." : NormalizeRelativePath(relative);
    }

    private static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static Guid CreateStableId(Guid rootId, string relativePath)
    {
        var canonicalName = rootId.ToString("N") + ":" + NormalizeRelativePath(relativePath).ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalName));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static ScanIssue Issue(string relativePath, ScanIssueCode code, string explanation) =>
        new(relativePath, code, explanation);

    private static ScanIssueCode MapIssueCode(Exception exception) => exception switch
    {
        UnauthorizedAccessException => ScanIssueCode.AccessDenied,
        FileNotFoundException or DirectoryNotFoundException => ScanIssueCode.EntryDisappeared,
        _ => ScanIssueCode.MetadataUnavailable,
    };

    private static bool IsPathException(Exception exception) =>
        exception is ArgumentException or NotSupportedException or PathTooLongException;

    private static bool IsExpectedFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private sealed record Attempt<T>(T? Value, ScanIssue? Issue)
    {
        public static Attempt<T> Success(T value) => new(value, null);
        public static Attempt<T> Failure(ScanIssue issue) => new(default, issue);
    }

    private sealed record EnumerationStep(FileSystemInfo? Value, ScanIssue? Issue, bool IsEnd)
    {
        public static EnumerationStep Item(FileSystemInfo value) => new(value, null, false);
        public static EnumerationStep Failure(ScanIssue issue) => new(null, issue, false);
        public static EnumerationStep End() => new(null, null, true);
    }
}
