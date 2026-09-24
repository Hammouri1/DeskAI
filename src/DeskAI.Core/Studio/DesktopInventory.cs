using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Files;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Studio;

/// <summary>Why a folder should not move without a second thought. Its row starts unticked.</summary>
[Flags]
public enum DesktopThingWarnings
{
    None = 0,
    ActiveProject = 1,
    HasPrograms = 2,
    OnlineOnly = 4,
    NotFullyLooked = 8,
}

/// <summary>One thing directly on the Desktop, as the moving cards need it. Opens no file.</summary>
/// <param name="LastChangedUtc">A file's own last change; for a folder, the newest of itself and everything found inside.</param>
/// <param name="Facts">What the executor checks again right before it moves.</param>
/// <param name="FileCount">Files found inside a folder; 0 for a file.</param>
/// <param name="ChildNames">Names directly inside a folder, to see a same-name clash before moving something into it.</param>
public sealed record DesktopThing(
    string RelativePath,
    bool IsFolder,
    DateTimeOffset LastChangedUtc,
    ExpectedFile Facts,
    int FileCount,
    DesktopThingWarnings Warnings,
    IReadOnlySet<string> ChildNames)
{
    public string Name => Path.GetFileName(RelativePath);

    public bool LookedAllTheWay => (Warnings & DesktopThingWarnings.NotFullyLooked) == 0;
}

public sealed record DesktopInventory(IReadOnlyList<DesktopThing> Things, string? Problem);

/// <summary>
/// A fresh, read-only look at what sits directly on a connected Desktop, for Clear old stuff and
/// Folder by group (ADR 0044). Nothing it sees is sent anywhere.
/// </summary>
/// <remarks>
/// It looks deeper than Find groups, with the search bounds of ADR 0041, because a folder counts
/// as old only when everything found inside it is old. A folder the look could not finish is
/// marked, never guessed about. Hidden, system, protected, and link items are left out as in
/// <see cref="DesktopLookService"/>, including any top-level folder holding one, so DeskAI's own
/// program folder can never be listed or moved.
/// </remarks>
public sealed class DesktopInventoryService(IFileScanner scanner)
{
    private static readonly HashSet<string> ProjectFolders = new(StringComparer.OrdinalIgnoreCase) { ".git", ".venv" };
    private static readonly HashSet<string> ProjectFiles = new(StringComparer.OrdinalIgnoreCase) { "package.json", "pyvenv.cfg" };
    private static readonly HashSet<string> ProjectEndings = new(StringComparer.OrdinalIgnoreCase) { ".sln", ".csproj" };
    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>();

    public static MetadataScanOptions Bounds { get; } = new(maxDepth: 8, maxEntries: 20_000);

    public async Task<DesktopInventory> LookAsync(AuthorizedRoot root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var folders = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileItem>();
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sawInsideAFolder = false;
        var stoppedEarly = false;
        await foreach (var scanEvent in scanner.ScanAsync(root, Bounds, cancellationToken).ConfigureAwait(false))
        {
            var nested = PathOf(scanEvent)?.Contains(Path.DirectorySeparatorChar) == true;
            sawInsideAFolder |= nested;
            switch (scanEvent)
            {
                // The scanner lists the Desktop itself completely before going into any folder.
                case ScanIssue { RelativePath: ".", Code: ScanIssueCode.EntryLimitReached }:
                    if (!sawInsideAFolder)
                    {
                        return new([], DesktopLookService.TooManyProblem);
                    }

                    stoppedEarly = true;
                    break;
                case ScanIssue { RelativePath: "." }:
                    return new([], DesktopLookService.RootProblem);
                case ScanIssue { Code: ScanIssueCode.ProtectedEntrySkipped or ScanIssueCode.ReparsePointSkipped } issue:
                    excluded.Add(TopSegment(issue.RelativePath));
                    break;
                case ScanIssue issue when nested:
                    if (folders.TryGetValue(TopSegment(issue.RelativePath), out var unfinished))
                    {
                        unfinished.Warnings |= DesktopThingWarnings.NotFullyLooked;
                    }

                    break;
                case ScanIssue issue:
                    // A top-level entry DeskAI could not read is not something it can vouch for.
                    excluded.Add(issue.RelativePath);
                    break;
                case FolderDiscovered folder when !nested:
                    if (IsHiddenOrSystem(folder.Traits) || folder.CreatedAtUtc is not { } made || folder.ModifiedAtUtc is not { } changed)
                    {
                        excluded.Add(folder.RelativePath);
                    }
                    else
                    {
                        folders[folder.RelativePath] = new Folder(made, changed);
                    }

                    break;
                case FolderDiscovered folder:
                    if (folders.TryGetValue(TopSegment(folder.RelativePath), out var parent))
                    {
                        parent.AddFolder(folder, IsDirectChild(folder.RelativePath));
                    }

                    break;
                case FileDiscovered { File: var file } when !nested:
                    if (!IsHiddenOrSystem(file.Traits))
                    {
                        files.Add(file);
                    }

                    break;
                case FileDiscovered { File: var file }:
                    if (folders.TryGetValue(TopSegment(file.RelativePath), out var holder))
                    {
                        holder.AddFile(file, IsDirectChild(file.RelativePath));
                    }

                    break;
            }
        }

        var things = new List<DesktopThing>();
        foreach (var (name, folder) in folders.Where(pair => !excluded.Contains(pair.Key)))
        {
            // Stopped at the item limit: which folders were finished is not known, so none is vouched for.
            var warnings = folder.Warnings | (stoppedEarly ? DesktopThingWarnings.NotFullyLooked : DesktopThingWarnings.None);
            things.Add(new DesktopThing(
                name, true, folder.Newest, new ExpectedFile(0, folder.Modified) { CreatedAtUtc = folder.Created },
                folder.FileCount, warnings, folder.Children));
        }

        foreach (var file in files.Where(file => !excluded.Contains(file.RelativePath)))
        {
            var warnings = (file.Traits & FileTraits.OnlineOnly) != 0 ? DesktopThingWarnings.OnlineOnly : DesktopThingWarnings.None;
            things.Add(new DesktopThing(
                file.RelativePath, false, file.ModifiedAtUtc, new ExpectedFile(file.SizeBytes, file.ModifiedAtUtc), 0, warnings, NoNames));
        }

        return new(things.OrderBy(thing => thing.Name, StringComparer.OrdinalIgnoreCase).ToList(), null);
    }

    private static bool IsHiddenOrSystem(FileTraits traits) => (traits & (FileTraits.Hidden | FileTraits.System)) != 0;

    private static bool IsDirectChild(string relativePath) =>
        relativePath.IndexOf(Path.DirectorySeparatorChar) == relativePath.LastIndexOf(Path.DirectorySeparatorChar);

    private static string TopSegment(string relativePath)
    {
        var index = relativePath.IndexOf(Path.DirectorySeparatorChar);
        return index < 0 ? relativePath : relativePath[..index];
    }

    private static string? PathOf(ScanEvent scanEvent) => scanEvent switch
    {
        FileDiscovered found => found.File.RelativePath,
        FolderDiscovered folder => folder.RelativePath,
        ScanIssue issue => issue.RelativePath,
        _ => null,
    };

    /// <summary>What the look found under one top-level folder.</summary>
    private sealed class Folder(DateTimeOffset created, DateTimeOffset modified)
    {
        public DateTimeOffset Created { get; } = created;

        public DateTimeOffset Modified { get; } = modified;

        public DateTimeOffset Newest { get; private set; } = modified;

        public int FileCount { get; private set; }

        public DesktopThingWarnings Warnings { get; set; }

        public HashSet<string> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddFolder(FolderDiscovered folder, bool direct)
        {
            Saw(folder.ModifiedAtUtc);
            var name = Path.GetFileName(folder.RelativePath);
            if (direct)
            {
                Children.Add(name);
            }

            if (ProjectFolders.Contains(name))
            {
                Warnings |= DesktopThingWarnings.ActiveProject;
            }
        }

        public void AddFile(FileItem file, bool direct)
        {
            FileCount++;
            Saw(file.ModifiedAtUtc);
            var name = Path.GetFileName(file.RelativePath);
            if (direct)
            {
                Children.Add(name);
            }

            if (ProjectFiles.Contains(name) || ProjectEndings.Contains(Path.GetExtension(name)))
            {
                Warnings |= DesktopThingWarnings.ActiveProject;
            }

            if (string.Equals(Path.GetExtension(name), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                Warnings |= DesktopThingWarnings.HasPrograms;
            }

            if ((file.Traits & FileTraits.OnlineOnly) != 0)
            {
                Warnings |= DesktopThingWarnings.OnlineOnly;
            }
        }

        private void Saw(DateTimeOffset? changed)
        {
            if (changed is { } value && value > Newest)
            {
                Newest = value;
            }
        }
    }
}
