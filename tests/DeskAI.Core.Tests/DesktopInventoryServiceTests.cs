using DeskAI.Core.Files;
using DeskAI.Core.Studio;

namespace DeskAI.Core.Tests;

/// <summary>The read-only look Clear old stuff and Folder by group start from (ADR 0044). No disk is touched.</summary>
public sealed class DesktopInventoryServiceTests
{
    private static readonly DateTimeOffset Made = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_folder_takes_the_newest_date_found_inside_it()
    {
        var seen = await LookAsync(
            Folder("Old project", Day(2024, 1)),
            File("notes.txt", Day(2024, 2)),
            Folder(@"Old project\deep", Day(2024, 3)),
            File(@"Old project\a.txt", Day(2024, 4)),
            File(@"Old project\deep\b.txt", Day(2025, 5)));

        var folder = seen.Things.Single(thing => thing.Name == "Old project");
        Assert.True(folder.IsFolder);
        Assert.Equal(Day(2025, 5), folder.LastChangedUtc);
        Assert.Equal(2, folder.FileCount);
        Assert.Equal(["a.txt", "deep"], folder.ChildNames.Order(StringComparer.Ordinal));
        Assert.Equal(Made, folder.Facts.CreatedAtUtc);
        Assert.Equal(Day(2024, 1), folder.Facts.ModifiedAtUtc);
        Assert.True(folder.LookedAllTheWay);
        var file = seen.Things.Single(thing => thing.Name == "notes.txt");
        Assert.False(file.IsFolder);
        Assert.Equal(Day(2024, 2), file.LastChangedUtc);
    }

    [Fact]
    public async Task Project_program_and_online_only_folders_carry_a_warning()
    {
        var seen = await LookAsync(
            Folder("Code", Day(2024, 1)),
            Folder("Games", Day(2024, 1)),
            Folder("Cloud", Day(2024, 1)),
            Folder("Plain", Day(2024, 1)),
            Folder(@"Code\.git", Day(2024, 1), FileTraits.Hidden),
            File(@"Games\game.exe", Day(2024, 1)),
            File(@"Cloud\photo.jpg", Day(2024, 1), FileTraits.OnlineOnly),
            File(@"Plain\a.txt", Day(2024, 1)));

        Assert.Equal(DesktopThingWarnings.ActiveProject, Warnings(seen, "Code"));
        Assert.Equal(DesktopThingWarnings.HasPrograms, Warnings(seen, "Games"));
        Assert.Equal(DesktopThingWarnings.OnlineOnly, Warnings(seen, "Cloud"));
        Assert.Equal(DesktopThingWarnings.None, Warnings(seen, "Plain"));
    }

    [Fact]
    public async Task A_folder_the_look_could_not_finish_is_marked()
    {
        var deep = await LookAsync(
            Folder("Deep", Day(2024, 1)),
            Folder("Shallow", Day(2024, 1)),
            new ScanIssue(@"Deep\a\b\c\d\e\f\g\h", ScanIssueCode.DepthLimitReached, "x"));
        var stopped = await LookAsync(
            Folder("One", Day(2024, 1)),
            File(@"One\a.txt", Day(2024, 1)),
            new ScanIssue(".", ScanIssueCode.EntryLimitReached, "x"));

        Assert.False(deep.Things.Single(thing => thing.Name == "Deep").LookedAllTheWay);
        Assert.True(deep.Things.Single(thing => thing.Name == "Shallow").LookedAllTheWay);
        Assert.False(Assert.Single(stopped.Things).LookedAllTheWay);
    }

    [Fact]
    public async Task Hidden_protected_and_linked_things_are_left_out()
    {
        var seen = await LookAsync(
            Folder("DeskAI", Day(2024, 1)),
            Folder("Secret", Day(2024, 1), FileTraits.Hidden),
            File("hidden.txt", Day(2024, 1), FileTraits.Hidden),
            new ScanIssue("Link", ScanIssueCode.ReparsePointSkipped, "x"),
            Folder("Kept", Day(2024, 1)),
            new ScanIssue(@"DeskAI\app", ScanIssueCode.ProtectedEntrySkipped, "x"));

        Assert.Equal(["Kept"], seen.Things.Select(thing => thing.Name));
    }

    [Fact]
    public async Task Too_many_things_on_the_Desktop_is_a_problem()
    {
        var seen = await LookAsync(new ScanIssue(".", ScanIssueCode.EntryLimitReached, "x"));

        Assert.Empty(seen.Things);
        Assert.Equal(DesktopLookService.TooManyProblem, seen.Problem);
    }

    private static Task<DesktopInventory> LookAsync(params ScanEvent[] events) =>
        new DesktopInventoryService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

    private static DesktopThingWarnings Warnings(DesktopInventory seen, string name) =>
        seen.Things.Single(thing => thing.Name == name).Warnings;

    private static DateTimeOffset Day(int year, int month) => new(year, month, 1, 0, 0, 0, TimeSpan.Zero);

    private static FolderDiscovered Folder(string path, DateTimeOffset changed, FileTraits traits = FileTraits.None) =>
        new(path, traits, Made, changed);

    private static FileDiscovered File(string path, DateTimeOffset changed, FileTraits traits = FileTraits.None) =>
        new(new FileItem(Guid.NewGuid(), path, FileKind.Unknown, 10, Made, changed, traits));
}
