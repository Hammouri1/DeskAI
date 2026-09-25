using System.ComponentModel;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Plans;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Launching;

/// <summary>
/// Opens or shows one file from a connected folder, after checking the live file (ADR 0047).
/// </summary>
/// <remarks>
/// Every check runs on the file as it is now, not as the index remembered it, and the first
/// refusal wins: the folder is still connected; the path stays inside it after normalising; the
/// place is not protected; no folder or file on the way is a link or junction; the file exists;
/// and for Open, the live name is still a familiar kind. Nothing is started on any refusal.
/// </remarks>
public sealed class WindowsFileLauncher(IAuthorizedRootRepository roots, IPathPolicy pathPolicy, IShellStarter shell) : IFileLauncher
{
    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IPathPolicy _pathPolicy = pathPolicy;
    private readonly IShellStarter _shell = shell;

    public Task<LaunchResult> OpenAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) =>
        LaunchAsync(rootId, relativePath, open: true, cancellationToken);

    public Task<LaunchResult> ShowInFolderAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) =>
        LaunchAsync(rootId, relativePath, open: false, cancellationToken);

    private async Task<LaunchResult> LaunchAsync(Guid rootId, string relativePath, bool open, CancellationToken cancellationToken)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadMetadata(root))
        {
            return LaunchResult.Refused("That folder is no longer connected in DeskAI.");
        }

        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':', StringComparison.Ordinal))
        {
            return LaunchResult.Refused("That path leads outside the connected folder.");
        }

        if (_pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked
            || _pathPolicy.ValidateRelativePath(root, relativePath).Status == ValidationStatus.Blocked)
        {
            return LaunchResult.Refused("This location is protected, so DeskAI did not open it.");
        }

        var full = ResolveInsideRoot(root.CanonicalPath, relativePath);
        if (full is null)
        {
            return LaunchResult.Refused("That path leads outside the connected folder.");
        }

        try
        {
            if (CrossesLink(root.CanonicalPath, full))
            {
                return LaunchResult.Refused("That name is a shortcut to somewhere else, so DeskAI did not follow it.");
            }

            if (!File.Exists(full))
            {
                return LaunchResult.Refused("It is no longer there. Press Refresh on Search so DeskAI catches up.");
            }

            if (open && FileOpenRule.For(Path.GetFileName(full)) != OpenChoice.Open)
            {
                return LaunchResult.Refused("DeskAI only opens familiar kinds of files. Use Show in folder.");
            }

            if (open)
            {
                _shell.OpenWithUsualApp(full);
            }
            else
            {
                _shell.ShowInFolder(full);
            }

            return LaunchResult.Success;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            return LaunchResult.Refused("Windows couldn't open it.");
        }
    }

    /// <summary>Same containment rule as the text reader: both sides canonical, separator required after the root.</summary>
    private static string? ResolveInsideRoot(string rootPath, string relativePath)
    {
        try
        {
            var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var full = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
            return full.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>True when the folder itself or anything between it and the file is a link, junction, or reparse point.</summary>
    private static bool CrossesLink(string rootPath, string fullPath)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (IsReparse(current))
        {
            return true;
        }

        foreach (var segment in fullPath[(current.Length + 1)..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparse(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReparse(string path)
    {
        var info = new FileInfo(path);
        return (info.Exists || Directory.Exists(path))
            && (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null);
    }
}
