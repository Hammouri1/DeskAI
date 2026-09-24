namespace DeskAI.Core.Studio;

/// <summary>
/// How the Find groups board follows a Desktop Studio card's own change, so the person's groups,
/// renames, and merges survive it. Pure: it is given only what actually happened and reads no disk.
/// </summary>
/// <remarks>
/// The board lists only things sitting directly on the Desktop. Without this, the next look saw
/// every moved thing as gone and every new group folder as new, so the groups emptied and the
/// folders landed under Not sure (found in the end-to-end check 2026-09-25).
/// </remarks>
public static class DesktopBoardFollow
{
    /// <summary>Tag names: each renamed folder keeps its place under its new name.</summary>
    public static DesktopGroupBoard Renamed(DesktopGroupBoard board, IReadOnlyDictionary<string, string> renamed)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(renamed);
        string Rename(string path) => renamed.TryGetValue(path, out var now) ? now : path;
        return board with
        {
            Groups = board.Groups.Select(group => group with { Items = group.Items.Select(Rename).ToList() }).ToList(),
            NotSure = board.NotSure.Select(Rename).ToList(),
            Folders = board.Folders.Select(Rename).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Folder by group: what moved leaves the board, and the folder it went into stands in its
    /// group. A folder the person already placed in a group stays where they put it.
    /// </summary>
    /// <param name="moved">Each thing that moved, and the Desktop folder it went into.</param>
    public static DesktopGroupBoard PutInGroupFolders(DesktopGroupBoard board, IReadOnlyList<(string Path, string Folder)> moved)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(moved);
        if (moved.Count == 0)
        {
            return board;
        }

        var gone = moved.Select(move => move.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = board.Groups.Select(group => group with { Items = group.Items.Where(item => !gone.Contains(item)).ToList() }).ToList();
        var notSure = board.NotSure.Where(item => !gone.Contains(item)).ToList();
        var folders = board.Folders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in moved.Select(move => move.Folder).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(folder);
            var own = IndexOf(groups, folder);
            if (own < 0 || groups.Any(group => Holds(group.Items, folder)))
            {
                continue;
            }

            // New on the Desktop, or under Not sure: it now holds the group, so it goes there.
            notSure.RemoveAll(item => string.Equals(item, folder, StringComparison.OrdinalIgnoreCase));
            groups[own] = groups[own] with { Items = [.. groups[own].Items, folder] };
        }

        return board with { Groups = groups, NotSure = notSure, Folders = folders };
    }

    /// <summary>
    /// Put back of Folder by group: each thing that came back rejoins the group whose folder it
    /// was in, and a folder Put back removed leaves the board.
    /// </summary>
    /// <param name="returned">Where each thing was inside a group folder, and where it is again.</param>
    /// <param name="removedFolders">Folders DeskAI made for the change and Put back removed.</param>
    public static DesktopGroupBoard TakenOutOfGroupFolders(
        DesktopGroupBoard board, IReadOnlyList<(string From, string To)> returned, IReadOnlyList<string> removedFolders)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(returned);
        ArgumentNullException.ThrowIfNull(removedFolders);
        var groups = board.Groups.Select(group => group with { Items = group.Items.ToList() }).ToList();
        var notSure = board.NotSure.ToList();
        var folders = board.Folders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in returned)
        {
            if (groups.Any(group => Holds(group.Items, to)) || Holds(notSure, to))
            {
                continue;
            }

            var folder = TopFolder(from);
            var index = groups.FindIndex(group => Holds(group.Items, folder));
            if (index < 0)
            {
                index = IndexOf(groups, folder);
            }

            if (index < 0)
            {
                notSure.Add(to);
            }
            else
            {
                groups[index] = groups[index] with { Items = [.. groups[index].Items, to] };
            }
        }

        var removed = removedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return board with
        {
            Groups = groups.Select(group => group with { Items = group.Items.Where(item => !removed.Contains(item)).ToList() }).ToList(),
            NotSure = notSure.Where(item => !removed.Contains(item)).ToList(),
            Folders = folders.Where(item => !removed.Contains(item)).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    private static string TopFolder(string relativePath)
    {
        var cut = relativePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return cut < 0 ? relativePath : relativePath[..cut];
    }

    private static bool Holds(IEnumerable<string> items, string path) => items.Contains(path, StringComparer.OrdinalIgnoreCase);

    private static int IndexOf(List<DesktopGroup> groups, string name) =>
        groups.FindIndex(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));
}
