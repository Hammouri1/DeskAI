using System.Security.Cryptography;
using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Scanning;

public sealed class ReadOnlyFolderService(
    IFileScanner scanner,
    IAuthorizedRootRepository rootRepository,
    IPathPolicy pathPolicy) : IReadOnlyFolderService
{
    public async Task<FolderPreviewResult> AuthorizeAndPreviewAsync(
        string selectedPath,
        MetadataScanOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        ArgumentNullException.ThrowIfNull(options);
        string canonicalPath;
        try
        {
            canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Refused("That folder path is not supported.");
        }

        if (IsUnsupportedRoot(canonicalPath))
        {
            return Refused("Network, device, and drive-root locations are not supported yet.");
        }

        if (!Directory.Exists(canonicalPath))
        {
            return Refused("That folder is no longer available.");
        }

        if (ContainsReparsePoint(canonicalPath))
        {
            return Refused("This folder crosses a link or shortcut, so DeskAI left it disconnected.");
        }

        var root = AuthorizedRoot.Create(
            CreateStableRootId(canonicalPath),
            canonicalPath,
            Path.GetFileName(canonicalPath),
            RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly);
        var policy = pathPolicy.ValidateRoot(root);
        if (policy.Status == ValidationStatus.Blocked)
        {
            return Refused("This location is protected and cannot be connected.");
        }

        await rootRepository.SaveAsync(root, cancellationToken).ConfigureAwait(false);
        var files = new List<FileItem>();
        var issues = new List<ScanIssue>();
        await foreach (var scanEvent in scanner.ScanAsync(root, options, cancellationToken).ConfigureAwait(false))
        {
            if (scanEvent is FileDiscovered discovered)
            {
                files.Add(discovered.File);
            }
            else if (scanEvent is ScanIssue issue)
            {
                issues.Add(issue);
            }
        }

        return new FolderPreviewResult(
            true,
            $"Previewed {files.Count} file name(s). File contents were not opened.",
            root,
            files.AsReadOnly(),
            issues.AsReadOnly());
    }

    public async Task<IReadOnlyList<AuthorizedRoot>> ListAuthorizedAsync(CancellationToken cancellationToken = default) =>
        (await rootRepository.ListAsync(cancellationToken).ConfigureAwait(false))
        .Where(RootCapabilities.CanReadMetadata)
        .ToArray();

    public Task RevokeAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        rootRepository.RemoveAsync(rootId, cancellationToken);

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
        return string.IsNullOrEmpty(root) ||
               string.Equals(Path.TrimEndingDirectorySeparator(root), path, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReparsePoint(string canonicalPath)
    {
        try
        {
            var root = Path.GetPathRoot(canonicalPath)!;
            var current = root;
            foreach (var segment in canonicalPath[root.Length..]
                         .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static Guid CreateStableRootId(string canonicalPath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath.ToUpperInvariant()));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static FolderPreviewResult Refused(string explanation) =>
        new(false, explanation, null, [], []);
}
