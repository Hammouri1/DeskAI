using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Studio;

namespace DeskAI.Core.Tests;

/// <summary>What Clear old stuff and Folder by group would move (ADR 0044): plain rules, no disk, no AI.</summary>
public sealed class DesktopMovePlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Old = Now.AddDays(-200);
    private static readonly DateTimeOffset Recent = Now.AddDays(-3);

    [Fact]
    public void Clear_old_stuff_lists_only_things_unchanged_for_six_months()
    {
        var preview = Clear(Folder("Old project", Old, files: 3), File("old.txt", Old), Folder("Current", Recent), File("new.txt", Recent));

        Assert.Equal(["Old project", "old.txt"], preview.Items.Select(item => item.Name));
        Assert.All(preview.Items, item => Assert.Equal("Old stuff", item.Destination));
        Assert.Equal(PlanPurpose.ClearOldStuff, preview.Plan.Purpose);
        Assert.Equal(
            [PlanOperationKind.CreateDirectory, PlanOperationKind.MoveFolder, PlanOperationKind.MoveFile],
            preview.Plan.Operations.Select(operation => operation.Kind));
        Assert.Equal(preview.Items.Select(item => item.OperationId).Order(), preview.Facts.Keys.Order());
        Assert.Equal(Old, preview.Facts[preview.Items[0].OperationId].CreatedAtUtc);
    }

    [Fact]
    public void Clear_old_stuff_leaves_a_clash_and_an_unfinished_look_alone()
    {
        var preview = Clear(
            Folder("Old stuff", Recent, children: "old.txt"),
            File("old.txt", Old),
            Folder("Deep", Old, warnings: DesktopThingWarnings.NotFullyLooked));

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("old.txt", "Old stuff already has something called old.txt, so nothing was replaced."), preview.LeftAlone);
        Assert.Contains(new DesktopLeftAlone("Deep", "DeskAI couldn't look all the way inside, so it can't tell whether it's old."), preview.LeftAlone);
        Assert.DoesNotContain(preview.Plan.Operations, operation => operation is CreateDirectoryOperation);
    }

    [Fact]
    public void Clear_old_stuff_never_lists_the_Old_stuff_folder_itself()
    {
        var preview = Clear(Folder("Old stuff", Old), File("a.txt", Old));

        Assert.Equal(["a.txt"], preview.Items.Select(item => item.Name));
        Assert.DoesNotContain(preview.Plan.Operations, operation => operation is CreateDirectoryOperation);
    }

    [Fact]
    public void A_file_called_Old_stuff_keeps_everything_where_it_is()
    {
        var preview = Clear(File("Old stuff", Recent), File("a.txt", Old));

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("a.txt", "A file called Old stuff is in the way, so its folder can't be made."), preview.LeftAlone);
    }

    [Fact]
    public void Warned_folders_start_unticked()
    {
        var preview = Clear(
            Folder("Code", Old, warnings: DesktopThingWarnings.ActiveProject | DesktopThingWarnings.HasPrograms),
            Folder("Games", Old, warnings: DesktopThingWarnings.HasPrograms),
            Folder("Plain", Old));

        var code = preview.Items.Single(item => item.Name == "Code");
        Assert.False(code.TickedByDefault);
        Assert.StartsWith("It looks like a project", code.Warning, StringComparison.Ordinal);
        Assert.StartsWith("It has programs inside", preview.Items.Single(item => item.Name == "Games").Warning, StringComparison.Ordinal);
        Assert.True(preview.Items.Single(item => item.Name == "Plain").TickedByDefault);
    }

    [Fact]
    public void A_protected_destination_is_left_alone()
    {
        var preview = DesktopMovePlanner.ClearOldStuff(
            StudioFakes.Root(), Seen(File("secret.txt", Old)), Now, "1", path => path == @"Old stuff\secret.txt");

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("secret.txt", "DeskAI's safety rules keep it where it is."), preview.LeftAlone);
    }

    [Fact]
    public void Folder_by_group_moves_each_group_into_its_own_folder_and_leaves_Not_sure()
    {
        var board = Board([new("Coding", ["Python stuff"]), new("Documents", ["report.docx", "Essays"])], notSure: ["holiday.jpg"]);

        var preview = ByGroup(board, Folder("Python stuff", Recent), File("report.docx", Recent), Folder("Essays", Recent), File("holiday.jpg", Recent));

        Assert.Equal(["Python stuff", "report.docx", "Essays"], preview.Items.Select(item => item.Name));
        Assert.Equal(["Coding", "Documents", "Documents"], preview.Items.Select(item => item.Destination));
        Assert.Equal(["Coding", "Documents"], preview.Plan.Operations.OfType<CreateDirectoryOperation>().Select(create => create.DestinationRelativePath));
        Assert.Equal(PlanPurpose.FolderByGroup, preview.Plan.Purpose);
        Assert.All(preview.Plan.Operations, operation => Assert.Equal(OperationProvenance.User, operation.Provenance));
    }

    [Fact]
    public void Folder_by_group_leaves_a_group_folder_where_it_is_and_never_moves_another_groups_folder()
    {
        var board = Board([new("Coding", ["Coding", "app.py"]), new("Documents", ["School"]), new("School", ["essay.docx"])]);

        var preview = ByGroup(board, Folder("Coding", Recent), File("app.py", Recent), Folder("School", Recent), File("essay.docx", Recent));

        Assert.Equal(["app.py", "essay.docx"], preview.Items.Select(item => item.Name));
        Assert.DoesNotContain(preview.Plan.Operations, operation => operation is CreateDirectoryOperation);
        Assert.Contains(new DesktopLeftAlone("Coding", "This is the Coding folder itself, so the rest of the group goes into it."), preview.LeftAlone);
        Assert.Contains(new DesktopLeftAlone("School", "It's where the School group goes, so it stays."), preview.LeftAlone);
    }

    [Fact]
    public void Folder_by_group_leaves_a_group_alone_when_a_file_has_its_name()
    {
        var board = Board([new("Music", ["song.mp3"])]);

        var preview = ByGroup(board, File("Music", Recent), File("song.mp3", Recent));

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("song.mp3", "A file called Music is in the way, so its folder can't be made."), preview.LeftAlone);
    }

    [Fact]
    public void Folder_by_group_names_what_is_no_longer_on_the_Desktop()
    {
        var preview = ByGroup(Board([new("Coding", ["gone.py"])]));

        Assert.Contains(new DesktopLeftAlone("gone.py", "It is no longer on your Desktop."), preview.LeftAlone);
    }

    [Fact]
    public void The_total_counts_folders_files_inside_and_loose_files()
    {
        DesktopMoveItem Item(bool folder, int files, bool looked = true) =>
            new(Guid.NewGuid(), "x", folder, "Old stuff", Old, files, looked, null);

        Assert.Equal("2 folders holding 1,204 files, plus 5 files",
            DesktopMoveText.Total([Item(true, 3), Item(true, 1201), .. Enumerable.Range(0, 5).Select(_ => Item(false, 0))]));
        Assert.Equal("1 folder holding at least 3 files", DesktopMoveText.Total([Item(true, 3, looked: false)]));
        Assert.Equal("1 file", DesktopMoveText.Total([Item(false, 0)]));
        Assert.Equal("Nothing", DesktopMoveText.Total([]));
    }

    private static DesktopMovePreview Clear(params DesktopThing[] things) =>
        DesktopMovePlanner.ClearOldStuff(StudioFakes.Root(), Seen(things), Now, "1", _ => false);

    private static DesktopMovePreview ByGroup(DesktopGroupBoard board, params DesktopThing[] things) =>
        DesktopMovePlanner.FolderByGroup(StudioFakes.Root(), Seen(things), board, Now, "1", _ => false);

    private static DesktopInventory Seen(params DesktopThing[] things) => new(things, null);

    private static DesktopGroupBoard Board(IReadOnlyList<DesktopGroup> groups, IReadOnlyList<string>? notSure = null) =>
        new(StudioFakes.DesktopId, groups, notSure ?? [], DesktopGroupSource.LocalGuess, Now);

    private static DesktopThing Folder(
        string name, DateTimeOffset changed, int files = 1, DesktopThingWarnings warnings = DesktopThingWarnings.None, params string[] children) =>
        new(name, true, changed, new ExpectedFile(0, changed) { CreatedAtUtc = changed }, files, warnings,
            children.ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static DesktopThing File(string name, DateTimeOffset changed) =>
        new(name, false, changed, new ExpectedFile(10, changed), 0, DesktopThingWarnings.None, new HashSet<string>());

    /// <summary>Found in review 2026-09-24: things could be moved into a hidden or protected Old stuff folder and seem to vanish.</summary>
    [Fact]
    public void A_destination_DeskAI_left_out_of_its_look_keeps_everything_where_it_is()
    {
        var seen = new DesktopInventory([File("a.txt", Old)], null)
        {
            LeftOutNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Old stuff" },
        };

        var preview = DesktopMovePlanner.ClearOldStuff(StudioFakes.Root(), seen, Now, "1", _ => false);

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("a.txt", "Something called Old stuff that DeskAI can't use is in the way, so nothing can go into it."), preview.LeftAlone);
    }
}
