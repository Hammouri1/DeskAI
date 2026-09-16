using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;

namespace DeskAI.Infrastructure.Scanning;

/// <summary>
/// Looks directly inside a connected folder for a few names. Names and kinds only.
/// </summary>
/// <remarks>
/// It lists the folder's top level once and matches the asked names against it, ignoring
/// capitals the way Windows does, so a preview can show a folder that is already there under
/// the name it actually has. It never descends, opens, or changes anything, and it refuses a
/// folder DeskAI may not read.
/// </remarks>
public sealed class FolderNameLookup : IFolderNameLookup
{
    public Task<IReadOnlyList<FolderEntryPresence>> LookAsync(
        AuthorizedRoot root,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(names.Count, ((IFolderNameLookup)this).MaxNames);
        if (!RootCapabilities.CanReadMetadata(root))
        {
            throw new InvalidOperationException("DeskAI may not look inside that folder.");
        }

        if (names.Any(name => name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0))
        {
            throw new ArgumentException("A name to look for must be a single folder name.", nameof(names));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entries = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in new DirectoryInfo(root.CanonicalPath).EnumerateFileSystemInfos())
        {
            entries.TryAdd(entry.Name, entry);
        }

        IReadOnlyList<FolderEntryPresence> result = names
            .Select(name => entries.TryGetValue(name, out var entry)
                ? new FolderEntryPresence(entry.Name, KindOf(entry))
                : new FolderEntryPresence(name, FolderEntryKind.Missing))
            .ToArray();
        return Task.FromResult(result);
    }

    private static FolderEntryKind KindOf(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint) ? FolderEntryKind.Link
        : entry.Attributes.HasFlag(FileAttributes.Directory) ? FolderEntryKind.Folder
        : FolderEntryKind.File;
}
