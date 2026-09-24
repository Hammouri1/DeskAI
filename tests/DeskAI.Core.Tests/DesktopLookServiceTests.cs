using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Roots;
using DeskAI.Core.Studio;

namespace DeskAI.Core.Tests;

public sealed class DesktopLookServiceTests
{
    [Fact]
    public async Task Lists_top_level_folders_and_loose_files_with_types_and_five_sample_names()
    {
        var events = new ScanEvent[]
        {
            new FolderDiscovered("Python stuff", FileTraits.None),
            StudioFakes.File(@"Python stuff\main.py"), StudioFakes.File(@"Python stuff\utils.py"), StudioFakes.File(@"Python stuff\a.py"),
            StudioFakes.File(@"Python stuff\b.py"), StudioFakes.File(@"Python stuff\c.py"), StudioFakes.File(@"Python stuff\README.md"),
            new FolderDiscovered("Empty", FileTraits.None),
            StudioFakes.File("report.docx"),
        };
        var look = await new DesktopLookService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

        var python = look.Items.Single(i => i.Name == "Python stuff");
        Assert.True(python.IsFolder);
        Assert.Equal([new DesktopTypeCount(".py", 5), new DesktopTypeCount(".md", 1)], python.Types);
        Assert.Equal(5, python.SampleNames.Count);
        Assert.Contains(look.Items, i => i.Name == "Empty" && i.IsFolder && i.Types.Count == 0);
        Assert.Contains(look.Items, i => i.Name == "report.docx" && !i.IsFolder);
    }

    [Fact]
    public async Task Leaves_out_hidden_system_and_any_folder_holding_a_protected_or_link_entry()
    {
        var events = new ScanEvent[]
        {
            new FolderDiscovered("DeskAI", FileTraits.None),
            new ScanIssue(@"DeskAI\app", ScanIssueCode.ProtectedEntrySkipped, "x"),
            new FolderDiscovered("Secret", FileTraits.Hidden),
            new ScanIssue("Shortcut dir", ScanIssueCode.ReparsePointSkipped, "x"),
            StudioFakes.File("desktop.ini", FileTraits.Hidden | FileTraits.System),
            StudioFakes.File("notes.txt"),
        };
        var look = await new DesktopLookService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

        Assert.Equal(["notes.txt"], look.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Keeps_at_most_60_folders_and_200_files_and_counts_the_rest()
    {
        var events = Enumerable.Range(0, 65).Select(i => (ScanEvent)new FolderDiscovered($"F{i:D2}", FileTraits.None))
            .Concat(Enumerable.Range(0, 205).Select(i => StudioFakes.File($"f{i:D3}.txt"))).ToArray();
        var look = await new DesktopLookService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

        Assert.Equal(60, look.Items.Count(i => i.IsFolder));
        Assert.Equal(200, look.Items.Count(i => !i.IsFolder));
        Assert.Equal(5, look.FoldersLeftOut);
        Assert.Equal(5, look.FilesLeftOut);
        Assert.Equal(["F60", "F61", "F62", "F63", "F64", "f200.txt", "f201.txt", "f202.txt", "f203.txt", "f204.txt"], look.LeftOutItems.Select(i => i.Name));
    }

    [Fact]
    public async Task Stopping_at_the_entry_limit_inside_folders_keeps_the_whole_top_level_and_says_so()
    {
        // The scanner lists the Desktop itself completely before it goes into any folder, so a
        // limit reached inside a big folder still leaves every top-level item known.
        var events = new ScanEvent[]
        {
            new FolderDiscovered("Big project", FileTraits.None),
            new FolderDiscovered("Essays", FileTraits.None),
            StudioFakes.File("notes.txt"),
            StudioFakes.File(@"Big project\main.py"),
            new ScanIssue(".", ScanIssueCode.EntryLimitReached, "x"),
        };
        var look = await new DesktopLookService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

        Assert.Null(look.Problem);
        Assert.True(look.StoppedEarly);
        Assert.Equal(["Big project", "Essays", "notes.txt"], look.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Stopping_at_the_entry_limit_on_the_Desktop_itself_says_there_is_too_much()
    {
        var events = new ScanEvent[]
        {
            StudioFakes.File("a.txt"),
            new ScanIssue(".", ScanIssueCode.EntryLimitReached, "x"),
        };
        var look = await new DesktopLookService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

        Assert.Empty(look.Items);
        Assert.Equal(DesktopLookService.TooManyProblem, look.Problem);
    }

    [Fact]
    public async Task Hidden_and_system_files_inside_folders_are_neither_counted_nor_named()
    {
        var events = new ScanEvent[]
        {
            new FolderDiscovered("Project", FileTraits.None),
            new FolderDiscovered(@"Project\.git", FileTraits.Hidden),
            StudioFakes.File(@"Project\.git\HEAD"),
            new FolderDiscovered(@"Project\.git\hooks", FileTraits.None),
            StudioFakes.File(@"Project\.git\hooks\pre-commit.sample"),
            StudioFakes.File(@"Project\desktop.ini", FileTraits.Hidden | FileTraits.System),
            StudioFakes.File(@"Project\.env", FileTraits.Hidden),
            StudioFakes.File(@"Project\main.py"),
        };
        var look = await new DesktopLookService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

        var project = Assert.Single(look.Items);
        Assert.Equal([new DesktopTypeCount(".py", 1)], project.Types);
        Assert.Equal(["main.py"], project.SampleNames);
    }

    [Fact]
    public async Task A_root_problem_is_reported_and_nothing_is_listed()
    {
        var events = new ScanEvent[] { new ScanIssue(".", ScanIssueCode.RootUnavailable, "x") };
        var look = await new DesktopLookService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

        Assert.Empty(look.Items);
        Assert.Equal(DesktopLookService.RootProblem, look.Problem);
    }
}

/// <summary>A scanner that replays a fixed list of events; it touches no disk.</summary>
internal sealed class ReplayScanner(IReadOnlyList<ScanEvent> events) : IFileScanner
{
    public IReadOnlyList<ScanEvent> Events { get; set; } = events;

    public async IAsyncEnumerable<ScanEvent> ScanAsync(
        AuthorizedRoot root,
        MetadataScanOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var scanEvent in Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return scanEvent;
        }

        await Task.CompletedTask;
    }
}

internal static class StudioFakes
{
    public static readonly string DesktopPath = Path.Combine(Path.GetTempPath(), "deskai-tests", "Desktop");
    public static readonly Guid DesktopId = Guid.Parse("7c0b6c55-5b1a-4a3e-9d4c-0f5f1f6a1d01");

    public static FileDiscovered File(string relativePath, FileTraits traits = FileTraits.None) =>
        new(new FileItem(StableId(relativePath), relativePath, FileKind.Unknown, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, traits));

    public static AuthorizedRoot Root() => AuthorizedRoot.Create(
        DesktopId, DesktopPath, "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);

    private static Guid StableId(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
