using DeskAI.App.ViewModels;
using DeskAI.Core.Execution;
using DeskAI.Core.Studio;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Desktop Studio's Clear old stuff and Folder by group cards (ADR 0044), used the way a person
/// uses them, on a generated Desktop inside the test's own folder.
/// </summary>
public sealed class DesktopStudioMovePageTests
{
    private static readonly TimeSpan SevenMonths = TimeSpan.FromDays(213);

    [Fact]
    public async Task Clear_old_stuff_shows_only_old_things_and_moves_nothing_until_Move()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var before = Snapshot(app);
        var studio = await OpenAsync(app);

        await studio.PreviewAsync(studio.OldStuff);

        Assert.Equal(["old notes.txt", "Old project"], studio.OldStuff.Items.Select(item => item.Name));
        Assert.All(studio.OldStuff.Items, item => Assert.True(item.IsTicked));
        Assert.Equal("Ticked: 1 folder holding 2 files, plus 1 file", studio.OldStuff.TotalText);
        Assert.Equal("Move 2 things", studio.OldStuff.ApplyButtonText);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task Move_asks_for_the_yes_first_then_moves_the_ticked_things_into_Old_stuff()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAsync(app);
        await studio.PreviewAsync(studio.OldStuff);

        Assert.False(studio.CanMoveThings);
        var refused = await studio.ApplyAsync(studio.OldStuff);

        Assert.True(refused!.NeedsPermission);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old project")));

        // What the page does after the person accepts the dialog.
        await studio.AllowMovingAsync();
        var done = await studio.ApplyAsync(studio.OldStuff);

        Assert.True(studio.CanMoveThings);
        Assert.Equal("Done. 2 things moved into Old stuff.", done!.Summary);
        Assert.Equal(done.Summary, studio.OldStuff.Message);
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Old stuff", "Old project", "main.py")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Old stuff", "old notes.txt")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "this week.pdf")));
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Current work")));
        Assert.False(studio.OldStuff.HasPreview);
        Assert.True(studio.OldStuff.CanPutBack);
    }

    [Fact]
    public async Task Put_back_returns_everything_and_removes_the_Old_stuff_folder_DeskAI_made()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var before = Snapshot(app);
        var studio = await OpenAllowedAsync(app);
        await studio.PreviewAsync(studio.OldStuff);
        await studio.ApplyAsync(studio.OldStuff);

        var back = await studio.PutBackAsync(studio.OldStuff);

        Assert.Equal("Put back. 2 things are where they were.", back!.Summary);
        Assert.Equal(before, Snapshot(app));
        Assert.False(studio.OldStuff.CanPutBack);
    }

    [Fact]
    public async Task Put_back_still_works_after_reopening_DeskAI()
    {
        await using var first = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(first);
        var before = Snapshot(first);
        var studio = await OpenAllowedAsync(first);
        await studio.PreviewAsync(studio.OldStuff);
        await studio.ApplyAsync(studio.OldStuff);
        await using var app = await first.ReopenAsync();

        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();

        Assert.True(again.OldStuff.CanPutBack);
        Assert.StartsWith("Last change: 2 things moved, at ", again.OldStuff.LastText, StringComparison.Ordinal);
        await again.PutBackAsync(again.OldStuff);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task A_project_folder_starts_unticked_with_its_warning()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Game mod", "package.json"), age: SevenMonths);
        DesktopMoveServiceTests.Age(app, "Game mod");
        app.MakeFile("Desktop", Path.Combine("Tools", "setup.exe"), age: SevenMonths);
        DesktopMoveServiceTests.Age(app, "Tools");
        var studio = await OpenAllowedAsync(app);

        await studio.PreviewAsync(studio.OldStuff);

        var project = studio.OldStuff.Items.Single(item => item.Name == "Game mod");
        Assert.False(project.IsTicked);
        Assert.StartsWith("It looks like a project", project.Warning, StringComparison.Ordinal);
        Assert.False(studio.OldStuff.Items.Single(item => item.Name == "Tools").IsTicked);
        Assert.False(studio.OldStuff.CanApply);

        project.IsTicked = true;
        Assert.Equal("Move 1 thing", studio.OldStuff.ApplyButtonText);
        await studio.ApplyAsync(studio.OldStuff);

        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old stuff", "Game mod")));
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Tools")));
    }

    [Fact]
    public async Task An_Old_stuff_folder_already_there_is_used_and_kept_and_a_clash_is_left_alone()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Old stuff", "old notes.txt"));
        app.MakeFile("Desktop", "old notes.txt", age: SevenMonths);
        app.MakeFile("Desktop", "ancient.txt", age: SevenMonths);
        var studio = await OpenAllowedAsync(app);

        await studio.PreviewAsync(studio.OldStuff);

        Assert.Equal(["ancient.txt"], studio.OldStuff.Items.Select(item => item.Name));
        Assert.Contains(studio.OldStuff.LeftAlone, line =>
            line.StartsWith("old notes.txt: Old stuff already has something called old notes.txt", StringComparison.Ordinal));

        await studio.ApplyAsync(studio.OldStuff);
        await studio.PutBackAsync(studio.OldStuff);

        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Old stuff", "old notes.txt")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "ancient.txt")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "old notes.txt")));
    }

    [Fact]
    public async Task A_folder_changed_after_the_list_stays_where_it_is()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.PreviewAsync(studio.OldStuff);
        File.WriteAllText(Path.Combine(app.DesktopPath, "Old project", "new idea.txt"), "Generated DeskAI test data");

        var result = await studio.ApplyAsync(studio.OldStuff);

        Assert.Equal("1 of 2 things moved. The rest stayed where they were.", result!.Summary);
        Assert.Contains(studio.OldStuff.LeftAlone, line => line.StartsWith("Old project: It changed after the list", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old project")));
    }

    [Fact]
    public async Task Folder_by_group_puts_each_group_into_its_own_folder_and_Put_back_undoes_it()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        app.MakeFile("Desktop", Path.Combine("Python stuff", "utils.py"));
        app.MakeFile("Desktop", Path.Combine("Essays", "essay.docx"));
        app.MakeFile("Desktop", "report.docx");
        app.MakeFile("Desktop", "holiday.jpg");
        app.MakeFile("Desktop", "mystery.zzz");
        var before = Snapshot(app);
        var studio = await OpenAllowedAsync(app);
        await studio.GuessAsync();

        await studio.PreviewAsync(studio.FolderByGroup);

        Assert.Contains(studio.FolderByGroup.Items, item => item.Name == "Python stuff" && item.Detail.EndsWith("goes into Coding", StringComparison.Ordinal));
        Assert.DoesNotContain(studio.FolderByGroup.Items, item => item.Name == "mystery.zzz");
        var done = await studio.ApplyAsync(studio.FolderByGroup);

        Assert.StartsWith("Done. ", done!.Summary, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Coding", "Python stuff", "main.py")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Documents", "report.docx")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Pictures", "holiday.jpg")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "mystery.zzz")));

        await studio.PutBackAsync(studio.FolderByGroup);

        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task Folder_by_group_without_groups_says_find_groups_first()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAsync(app);

        await studio.PreviewAsync(studio.FolderByGroup);

        Assert.False(studio.FolderByGroup.HasPreview);
        Assert.Equal(DesktopMoveService.FindGroupsFirst, studio.FolderByGroup.Message);
    }

    [Fact]
    public async Task Stop_moving_things_takes_the_yes_back()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);

        await studio.StopMovingAsync();

        Assert.False(studio.CanMoveThings);
        await studio.PreviewAsync(studio.OldStuff);
        Assert.True((await studio.ApplyAsync(studio.OldStuff))!.NeedsPermission);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old project")));
    }

    [Fact]
    public async Task Organize_does_not_offer_to_undo_a_Desktop_Studio_change()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.PreviewAsync(studio.OldStuff);
        await studio.ApplyAsync(studio.OldStuff);

        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        await organize.ConnectAndSelectAsync(app.DesktopPath);

        Assert.Equal("Desktop", organize.SelectedFolder?.Name);
        Assert.False(organize.CanUndo);
    }

    [Fact]
    public async Task An_interrupted_move_is_asked_about_and_Put_them_back_returns_it()
    {
        await using var first = await TestApp.StartStoppableAsync();
        DesktopMoveServiceTests.MakeOldDesktop(first);
        var before = Snapshot(first);
        var studio = await OpenAllowedAsync(first);
        await studio.PreviewAsync(studio.OldStuff);
        var folder = studio.OldStuff.Items.Single(item => item.Name == "Old project");
        first.Stopping.StopBefore(folder.Item.OperationId, JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => studio.ApplyAsync(studio.OldStuff));
        await using var app = await first.ReopenAsync();

        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();

        Assert.True(again.HasInterrupted);
        Assert.Contains("2 of 2 things had moved", again.InterruptedText, StringComparison.Ordinal);
        Assert.False(again.OldStuff.CanPutBack);
        var back = await again.PutBackInterruptedAsync();

        Assert.False(back!.NeedsPermission);
        Assert.Equal(before, Snapshot(app));
        Assert.False(again.HasInterrupted);
    }

    [Fact]
    public async Task DeskAIs_program_folder_and_hidden_things_never_appear_or_move()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile(Path.Combine("Desktop", "DeskAI", "app"), "DeskAI.App.exe", age: SevenMonths);
        Directory.SetLastWriteTimeUtc(app.ProgramFolderPath, DateTime.UtcNow - SevenMonths);
        DesktopMoveServiceTests.Age(app, "DeskAI");
        var hidden = app.MakeFile("Desktop", "secret.txt", age: SevenMonths);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        app.MakeFile("Desktop", "old notes.txt", age: SevenMonths);
        var studio = await OpenAllowedAsync(app);

        await studio.PreviewAsync(studio.OldStuff);

        Assert.Equal(["old notes.txt"], studio.OldStuff.Items.Select(item => item.Name));
        await studio.ApplyAsync(studio.OldStuff);
        Assert.True(File.Exists(Path.Combine(app.ProgramFolderPath, "DeskAI.App.exe")));
        Assert.True(File.Exists(hidden));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Old stuff", "old notes.txt")));
    }

    private static async Task<DesktopStudioViewModel> OpenAsync(TestApp app)
    {
        await DesktopMoveServiceTests.ConnectDesktopAsync(app);
        var studio = app.Get<DesktopStudioViewModel>();
        await studio.InitializeAsync();
        Assert.True(studio.IsDesktopConnected);
        return studio;
    }

    private static async Task<DesktopStudioViewModel> OpenAllowedAsync(TestApp app)
    {
        var studio = await OpenAsync(app);
        await studio.AllowMovingAsync();
        Assert.True(studio.CanMoveThings);
        return studio;
    }

    private static string[] Snapshot(TestApp app)
    {
        Directory.CreateDirectory(app.DesktopPath);
        return Directory.EnumerateFileSystemEntries(app.DesktopPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(app.DesktopPath, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
