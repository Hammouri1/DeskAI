using System.Globalization;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;
using DeskAI.Core.Templates;

namespace DeskAI.Core.Studio;

public enum DesktopMoveCard
{
    ClearOldStuff,
    FolderByGroup,
    TagNames,
}

/// <summary>One thing a card would move, and the folder on the Desktop it would go into.</summary>
/// <param name="Warning">Why it starts unticked, in plain words, or null.</param>
public sealed record DesktopMoveItem(
    Guid OperationId,
    string RelativePath,
    bool IsFolder,
    string Destination,
    DateTimeOffset LastChangedUtc,
    int FileCount,
    bool LookedAllTheWay,
    string? Warning)
{
    public string Name => Path.GetFileName(RelativePath);

    public bool TickedByDefault => Warning is null;
}

/// <summary>Something a card will not move, and why.</summary>
public sealed record DesktopLeftAlone(string Name, string Reason);

/// <summary>What a card would do, and the plan behind it. Nothing has changed yet.</summary>
/// <param name="Facts">How each listed thing looked, checked again right before it moves.</param>
public sealed record DesktopMovePreview(
    DesktopMoveCard Card,
    AuthorizedRoot Root,
    OrganizationPlan Plan,
    IReadOnlyList<DesktopMoveItem> Items,
    IReadOnlyList<DesktopLeftAlone> LeftAlone,
    IReadOnlyDictionary<Guid, ExpectedFile> Facts);

/// <summary>The words the moving cards use, kept in one place so the page and tests agree.</summary>
public static class DesktopMoveText
{
    public static string Things(int count) => count == 1 ? "1 thing" : $"{count} things";

    /// <summary>"3 folders holding 1,204 files, plus 5 files", as the design asks.</summary>
    public static string Total(IEnumerable<DesktopMoveItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items.ToList();
        var folders = list.Where(item => item.IsFolder).ToList();
        var looseFiles = list.Count - folders.Count;
        var atLeast = folders.Any(folder => !folder.LookedAllTheWay) ? "at least " : string.Empty;
        var folderText = folders.Count == 0 ? null : $"{Count(folders.Count, "folder")} holding {atLeast}{Count(folders.Sum(folder => folder.FileCount), "file")}";
        var fileText = looseFiles == 0 ? null : Count(looseFiles, "file");
        return (folderText, fileText) switch
        {
            (null, null) => "Nothing",
            ({ } onlyFolders, null) => onlyFolders,
            (null, { } onlyFiles) => onlyFiles,
            ({ } both, { } plus) => $"{both}, plus {plus}",
        };
    }

    /// <summary>The most important warning first; null when there is none.</summary>
    public static string? WarningFor(DesktopThingWarnings warnings) =>
        (warnings & DesktopThingWarnings.ActiveProject) != 0 ? "It looks like a project you're working on. Moving or renaming it can break programs that remember where it is."
        : (warnings & DesktopThingWarnings.HasPrograms) != 0 ? "It has programs inside. Moving or renaming it can break shortcuts or games that remember where it is."
        : (warnings & DesktopThingWarnings.OnlineOnly) != 0 ? "Some of it is stored online only."
        : (warnings & DesktopThingWarnings.NotFullyLooked) != 0 ? "DeskAI couldn't look all the way inside."
        : null;

    private static string Count(int count, string word) =>
        count == 1 ? $"1 {word}" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} {word}s";
}

/// <summary>
/// Works out what Clear old stuff, Folder by group, and Tag names would change (ADR 0044, ADR 0045). Plain rules over a
/// fresh look; no AI and no disk access. Every move lands in a folder directly on the Desktop.
/// </summary>
public static class DesktopMovePlanner
{
    public const string OldStuffFolder = "Old stuff";

    /// <summary>Everything unchanged for <see cref="StorageSummaryService.OldFileAge"/> goes into Old stuff.</summary>
    public static DesktopMovePreview ClearOldStuff(
        AuthorizedRoot root, DesktopInventory inventory, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var cutoff = nowUtc - StorageSummaryService.OldFileAge;
        var existing = inventory.Things.FirstOrDefault(thing => string.Equals(thing.Name, OldStuffFolder, StringComparison.OrdinalIgnoreCase));
        var builder = new Builder(root, DesktopMoveCard.ClearOldStuff, nowUtc, policyVersion, isProtected, inventory.LeftOutNames);
        foreach (var thing in inventory.Things.Where(thing => !ReferenceEquals(thing, existing) && thing.LastChangedUtc <= cutoff))
        {
            if (thing.IsFolder && !thing.LookedAllTheWay)
            {
                builder.LeaveAlone(thing.Name, "DeskAI couldn't look all the way inside, so it can't tell whether it's old.");
                continue;
            }

            builder.Move(thing, OldStuffFolder, existing, $"Unchanged since {thing.LastChangedUtc:yyyy-MM-dd}", OperationProvenance.Heuristic);
        }

        return builder.Build(PlanPurpose.ClearOldStuff);
    }

    /// <summary>Each group on the board goes into a folder with its name. Not sure stays where it is.</summary>
    public static DesktopMovePreview FolderByGroup(
        AuthorizedRoot root, DesktopInventory inventory, DesktopGroupBoard board, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(board);
        var byPath = inventory.Things.ToDictionary(thing => thing.RelativePath, StringComparer.OrdinalIgnoreCase);
        var groupNames = board.Groups.Select(group => group.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builder = new Builder(root, DesktopMoveCard.FolderByGroup, nowUtc, policyVersion, isProtected, inventory.LeftOutNames);
        foreach (var group in board.Groups)
        {
            byPath.TryGetValue(group.Name, out var existing);
            foreach (var path in group.Items)
            {
                if (!byPath.TryGetValue(path, out var thing))
                {
                    builder.LeaveAlone(Path.GetFileName(path), "It is no longer on your Desktop.");
                }
                else if (thing.IsFolder && string.Equals(thing.Name, group.Name, StringComparison.OrdinalIgnoreCase))
                {
                    builder.LeaveAlone(thing.Name, $"This is the {group.Name} folder itself, so the rest of the group goes into it.");
                }
                else if (thing.IsFolder && groupNames.Contains(thing.Name))
                {
                    builder.LeaveAlone(thing.Name, $"It's where the {thing.Name} group goes, so it stays.");
                }
                else
                {
                    builder.Move(thing, group.Name, existing, $"In your {group.Name} group", OperationProvenance.User);
                }
            }
        }

        return builder.Build(PlanPurpose.FolderByGroup);
    }

    /// <summary>Between the group's name and the folder's own name, as in the design's example.</summary>
    public const string TagSeparator = " – ";

    /// <summary>
    /// Each folder in a group gets the group's name in front ("Coding – Python stuff"), with ADR
    /// 0044's folder move to a new name in the same place (ADR 0045). Files and Not sure keep their
    /// names. A new name that is already used, even by something the look left out, is never taken.
    /// </summary>
    public static DesktopMovePreview TagNames(
        AuthorizedRoot root, DesktopInventory inventory, DesktopGroupBoard board, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(board);
        var byPath = inventory.Things.ToDictionary(thing => thing.RelativePath, StringComparer.OrdinalIgnoreCase);
        var taken = inventory.Things.Select(thing => thing.Name).Concat(inventory.LeftOutNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builder = new Builder(root, DesktopMoveCard.TagNames, nowUtc, policyVersion, isProtected, inventory.LeftOutNames);
        foreach (var group in board.Groups)
        {
            var prefix = group.Name + TagSeparator;
            foreach (var path in group.Items)
            {
                if (!byPath.TryGetValue(path, out var thing))
                {
                    builder.LeaveAlone(Path.GetFileName(path), "It is no longer on your Desktop.");
                    continue;
                }

                if (!thing.IsFolder)
                {
                    continue;
                }

                var newName = prefix + thing.Name;
                if (thing.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    builder.LeaveAlone(thing.Name, "Its name already starts with the group's name.");
                }
                else if (FolderNameCheck.Check(newName) is { } problem)
                {
                    builder.LeaveAlone(thing.Name, $"The new name can't be used: {problem}");
                }
                else if (!taken.Add(newName))
                {
                    builder.LeaveAlone(thing.Name, $"Something called {newName} is already there, so nothing was replaced.");
                }
                else
                {
                    builder.Rename(thing, newName, $"In your {group.Name} group");
                }
            }
        }

        return builder.Build(PlanPurpose.TagNames);
    }

    private sealed class Builder(
        AuthorizedRoot root, DesktopMoveCard card, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected,
        IReadOnlySet<string> leftOut)
    {
        private readonly List<PlanOperation> _creates = [];
        private readonly List<PlanOperation> _moves = [];
        private readonly List<DesktopMoveItem> _items = [];
        private readonly List<DesktopLeftAlone> _leftAlone = [];
        private readonly Dictionary<Guid, ExpectedFile> _facts = [];
        private readonly HashSet<string> _made = new(StringComparer.OrdinalIgnoreCase);

        public void LeaveAlone(string name, string reason) => _leftAlone.Add(new(name, reason));

        /// <param name="existing">What already has the destination's name on the Desktop, if anything.</param>
        public void Move(DesktopThing thing, string destination, DesktopThing? existing, string reason, OperationProvenance provenance)
        {
            var target = Path.Combine(destination, thing.Name);
            if (leftOut.Contains(destination))
            {
                LeaveAlone(thing.Name, $"Something called {destination} that DeskAI can't use is in the way, so nothing can go into it.");
                return;
            }

            if (existing is { IsFolder: false })
            {
                LeaveAlone(thing.Name, $"A file called {destination} is in the way, so its folder can't be made.");
                return;
            }

            if (existing is not null && existing.ChildNames.Contains(thing.Name))
            {
                LeaveAlone(thing.Name, $"{destination} already has something called {thing.Name}, so nothing was replaced.");
                return;
            }

            if (isProtected(thing.RelativePath) || isProtected(target))
            {
                LeaveAlone(thing.Name, "DeskAI's safety rules keep it where it is.");
                return;
            }

            if (existing is null && _made.Add(destination))
            {
                _creates.Add(new CreateDirectoryOperation(Guid.NewGuid(), destination, $"A folder called {destination} on your Desktop.", provenance));
            }

            var id = Guid.NewGuid();
            _moves.Add(thing.IsFolder
                ? new MoveFolderOperation(id, thing.RelativePath, target, reason, provenance)
                : new MoveFileOperation(id, thing.RelativePath, target, reason, provenance));
            _items.Add(new DesktopMoveItem(
                id, thing.RelativePath, thing.IsFolder, destination, thing.LastChangedUtc, thing.FileCount, thing.LookedAllTheWay,
                DesktopMoveText.WarningFor(thing.Warnings)));
            _facts[id] = thing.Facts;
        }

        /// <summary>A folder gets a new name in the same place: one rename (ADR 0045).</summary>
        public void Rename(DesktopThing thing, string newName, string reason)
        {
            if (isProtected(thing.RelativePath) || isProtected(newName))
            {
                LeaveAlone(thing.Name, "DeskAI's safety rules keep it where it is.");
                return;
            }

            var id = Guid.NewGuid();
            _moves.Add(new MoveFolderOperation(id, thing.RelativePath, newName, reason, OperationProvenance.User));
            _items.Add(new DesktopMoveItem(
                id, thing.RelativePath, true, newName, thing.LastChangedUtc, thing.FileCount, thing.LookedAllTheWay,
                DesktopMoveText.WarningFor(thing.Warnings)));
            _facts[id] = thing.Facts;
        }

        public DesktopMovePreview Build(PlanPurpose purpose) =>
            new(card, root,
                OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, nowUtc, policyVersion, [.. _creates, .. _moves], purpose: purpose),
                _items, _leftAlone, _facts);
    }
}
