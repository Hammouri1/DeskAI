using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Studio;

public sealed record DesktopTypeCount(string Ending, int Count);

/// <summary>One thing sitting directly on the Desktop, summarized for grouping. Opens no file.</summary>
public sealed record DesktopItem(
    string RelativePath, bool IsFolder, IReadOnlyList<DesktopTypeCount> Types, IReadOnlyList<string> SampleNames)
{
    public string Name => Path.GetFileName(RelativePath);
}

/// <summary>What sits on the Desktop, within the bounds, and what the bounds left out (never sent to AI).</summary>
public sealed record DesktopLook(
    IReadOnlyList<DesktopItem> Items, int FoldersLeftOut, int FilesLeftOut, string? Problem, IReadOnlyList<DesktopItem> LeftOutItems)
{
    public IEnumerable<DesktopItem> Everything => Items.Concat(LeftOutItems);

    /// <summary>True when the look stopped at its item limit inside a folder: every Desktop item is listed, but some folders were not looked all the way inside.</summary>
    public bool StoppedEarly { get; init; }
}

/// <summary>
/// A fresh, read-only look at what sits directly on a connected Desktop (ADR 0042). Folders are
/// summarized by the kinds of files found up to <see cref="Bounds"/> deep and a few file names.
/// </summary>
/// <remarks>
/// A top-level folder with a protected or link entry anywhere under it is left out entirely:
/// DeskAI's own program folder can sit on the Desktop, and a later card must never be able to
/// place or move the folder that holds it.
/// </remarks>
public sealed class DesktopLookService(IFileScanner scanner)
{
    public const int MaxFolders = 60;
    public const int MaxFiles = 200;
    public const int MaxSampleNames = 5;
    public const int MaxTypesPerFolder = 8;
    public const string RootProblem = "DeskAI could not look at your Desktop safely. It may have moved or become a link.";
    public const string TooManyProblem = "There are too many things directly on your Desktop for DeskAI to sort at once.";

    public static MetadataScanOptions Bounds { get; } = new(maxDepth: 4, maxEntries: 5000);

    public async Task<DesktopLook> LookAsync(AuthorizedRoot root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var folders = new List<string>();
        var looseFiles = new List<string>();
        var filesByFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hiddenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sawInsideAFolder = false;
        var stoppedEarly = false;
        await foreach (var scanEvent in scanner.ScanAsync(root, Bounds, cancellationToken).ConfigureAwait(false))
        {
            sawInsideAFolder |= PathOf(scanEvent) is { } path && path.Contains(Path.DirectorySeparatorChar);
            switch (scanEvent)
            {
                // The scanner lists the Desktop itself completely before going into any folder, so
                // a limit reached once it is inside a folder still leaves every top-level item known.
                case ScanIssue { RelativePath: ".", Code: ScanIssueCode.EntryLimitReached }:
                    if (!sawInsideAFolder)
                    {
                        return new DesktopLook([], 0, 0, TooManyProblem, []);
                    }

                    stoppedEarly = true;
                    break;
                case ScanIssue { RelativePath: "." }:
                    return new DesktopLook([], 0, 0, RootProblem, []);
                case ScanIssue { Code: ScanIssueCode.ProtectedEntrySkipped or ScanIssueCode.ReparsePointSkipped } issue:
                    excluded.Add(TopSegment(issue.RelativePath));
                    break;
                case FolderDiscovered folder when IsHiddenOrSystem(folder.Traits):
                    (folder.RelativePath.Contains(Path.DirectorySeparatorChar) ? hiddenFolders : excluded).Add(folder.RelativePath);
                    break;
                case FolderDiscovered folder when !folder.RelativePath.Contains(Path.DirectorySeparatorChar):
                    folders.Add(folder.RelativePath);
                    break;
                case FileDiscovered { File: var file } when !IsHiddenOrSystem(file.Traits) && !IsUnder(file.RelativePath, hiddenFolders):
                    var top = TopSegment(file.RelativePath);
                    if (top == file.RelativePath)
                    {
                        looseFiles.Add(file.RelativePath);
                    }
                    else if (filesByFolder.TryGetValue(top, out var list))
                    {
                        list.Add(file.RelativePath);
                    }
                    else
                    {
                        filesByFolder[top] = [file.RelativePath];
                    }

                    break;
            }
        }

        var keptFolders = folders.Where(f => !excluded.Contains(f)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var keptFiles = looseFiles.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var items = keptFolders.Take(MaxFolders).Select(f => Summarize(f, filesByFolder.GetValueOrDefault(f) ?? []))
            .Concat(keptFiles.Take(MaxFiles).Select(f => new DesktopItem(f, false, [], [])))
            .ToList();
        var leftOut = keptFolders.Skip(MaxFolders).Select(f => Summarize(f, filesByFolder.GetValueOrDefault(f) ?? []))
            .Concat(keptFiles.Skip(MaxFiles).Select(f => new DesktopItem(f, false, [], [])))
            .ToList();
        return new DesktopLook(
            items, Math.Max(0, keptFolders.Count - MaxFolders), Math.Max(0, keptFiles.Count - MaxFiles), null, leftOut)
        {
            StoppedEarly = stoppedEarly,
        };
    }

    private static DesktopItem Summarize(string folder, List<string> files)
    {
        var types = files
            .Select(f => Path.GetExtension(f).ToLowerInvariant())
            .Where(e => e.Length > 1)
            .GroupBy(e => e)
            .Select(g => new DesktopTypeCount(g.Key, g.Count()))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Ending, StringComparer.Ordinal)
            .Take(MaxTypesPerFolder).ToList();
        var samples = files.Select(Path.GetFileName).OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase).Take(MaxSampleNames).ToList();
        return new DesktopItem(folder, true, types, samples);
    }

    private static bool IsHiddenOrSystem(FileTraits traits) => (traits & (FileTraits.Hidden | FileTraits.System)) != 0;

    /// <summary>True when the path sits anywhere inside one of these folders.</summary>
    private static bool IsUnder(string relativePath, HashSet<string> folders)
    {
        for (var index = relativePath.LastIndexOf(Path.DirectorySeparatorChar); index > 0;
             index = relativePath.LastIndexOf(Path.DirectorySeparatorChar, index - 1))
        {
            if (folders.Contains(relativePath[..index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string? PathOf(ScanEvent scanEvent) => scanEvent switch
    {
        FileDiscovered found => found.File.RelativePath,
        FolderDiscovered folder => folder.RelativePath,
        ScanIssue issue => issue.RelativePath,
        _ => null,
    };

    private static string TopSegment(string relativePath)
    {
        var index = relativePath.IndexOf(Path.DirectorySeparatorChar);
        return index < 0 ? relativePath : relativePath[..index];
    }
}
