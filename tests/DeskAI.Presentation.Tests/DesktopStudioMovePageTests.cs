using DeskAI.App.ViewModels;
using DeskAI.Core.Execution;
using DeskAI.Core.Studio;
using DeskAI.Core.Tidy;

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

    /// <summary>
    /// Found in review 2026-09-24: a Desktop Studio change that stopped part-way was also offered
    /// on Organize, which checked the tidy permission, closed the question, and then could not put
    /// anything back. Each question is now answered only where its change was made.
    /// </summary>
    [Fact]
    public async Task A_stopped_Desktop_Studio_change_is_answered_in_Desktop_Studio_not_on_Organize()
    {
        await using var first = await TestApp.StartStoppableAsync();
        DesktopMoveServiceTests.MakeOldDesktop(first);
        var studio = await OpenAllowedAsync(first);
        await studio.PreviewAsync(studio.OldStuff);
        first.Stopping.StopBefore(studio.OldStuff.Items.Single(item => item.Name == "Old project").Item.OperationId, JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => studio.ApplyAsync(studio.OldStuff));
        await using var app = await first.ReopenAsync();
        var desktop = (await app.Get<DesktopGroupingService>().FindDesktopAsync(TestContext.Current.CancellationToken))!;
        await app.Get<TidyPermissionService>().AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        await app.Get<DesktopMoveService>().StopAsync(desktop.Id, TestContext.Current.CancellationToken);

        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        await organize.ConnectAndSelectAsync(app.DesktopPath);

        Assert.True(organize.HasInterrupted);
        Assert.Contains("Desktop Studio", organize.InterruptedTitle, StringComparison.Ordinal);
        Assert.False(organize.CanUndoInterrupted);
        Assert.False(organize.CanAnswerInterrupted);
        var stopped = (await app.Get<TidyRunService>().FindInterruptedAsync(desktop.Id, TestContext.Current.CancellationToken))!;
        var refused = await app.Get<TidyRunService>().UndoInterruptedAsync(desktop.Id, stopped, TestContext.Current.CancellationToken);
        Assert.Contains("Desktop Studio", refused.Summary, StringComparison.Ordinal);

        // The question is still open, and Desktop Studio can answer it.
        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();
        Assert.True(again.HasInterrupted);
        Assert.True(again.CanPutBackInterrupted);
    }

    /// <summary>
    /// Found in review 2026-09-24: an Organize tidy that stopped part-way on the Desktop was also
    /// offered here, and Put them back asked for the broader yes to move things on the Desktop.
    /// </summary>
    [Fact]
    public async Task A_stopped_Organize_tidy_is_answered_on_Organize_not_here()
    {
        await using var first = await TestApp.StartStoppableAsync();
        first.MakeFile("Desktop", "invoice.pdf");
        first.MakeFile("Desktop", "notes.pdf");
        var desktop = await DesktopMoveServiceTests.ConnectDesktopAsync(first);
        await first.Get<TidyPermissionService>().AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        var preview = (await first.Get<TidySuggestionService>().PreviewAsync(
            desktop.Id, Guid.NewGuid(), 1, new Dictionary<Guid, SameNameChoice>(),
            TidySuggestionMode.TypesAndRules, new Dictionary<Guid, TidyAiAdvice>(), TestContext.Current.CancellationToken))!;
        first.Stopping.StopBefore(
            preview.Suggestions.Single(item => item.FileName == "notes.pdf").MoveOperationId!.Value,
            JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => first.Get<TidyRunService>().TidyAsync(
            preview, [.. preview.Suggestions.Select(item => item.MoveOperationId!.Value)], TestContext.Current.CancellationToken));
        await using var app = await first.ReopenAsync();

        var studio = app.Get<DesktopStudioViewModel>();
        await studio.InitializeAsync();

        Assert.True(studio.HasInterrupted);
        Assert.Contains("Organize", studio.InterruptedText, StringComparison.Ordinal);
        Assert.False(studio.CanPutBackInterrupted);
        Assert.False(studio.CanKeepInterrupted);
        var refused = await studio.PutBackInterruptedAsync();
        Assert.False(refused!.NeedsPermission);
        Assert.True(studio.HasInterrupted);
    }

    [Fact]
    public async Task Tag_names_shows_the_new_name_for_each_folder_and_never_renames_files()
    {
        await using var app = await TestApp.StartAsync();
        MakeGroupDesktop(app);
        var before = Snapshot(app);
        var studio = await OpenAsync(app);
        await studio.GuessAsync();

        await studio.PreviewAsync(studio.TagNames);

        Assert.Equal(["Essays", "Python stuff"], studio.TagNames.Items.Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.Contains("becomes \"Coding – Python stuff\"", studio.TagNames.Items.Single(item => item.Name == "Python stuff").Detail, StringComparison.Ordinal);
        Assert.Equal("Rename 2 folders", studio.TagNames.ApplyButtonText);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task Rename_then_Put_back_restores_the_old_names()
    {
        await using var first = await TestApp.StartAsync();
        MakeGroupDesktop(first);
        var before = Snapshot(first);
        var studio = await OpenAllowedAsync(first);
        await studio.GuessAsync();
        await studio.PreviewAsync(studio.TagNames);

        var done = await studio.ApplyAsync(studio.TagNames);

        Assert.Equal("Done. 2 folders renamed.", done!.Summary);
        Assert.True(Directory.Exists(Path.Combine(first.DesktopPath, "Coding – Python stuff")));
        Assert.True(Directory.Exists(Path.Combine(first.DesktopPath, "Documents – Essays")));
        Assert.True(File.Exists(Path.Combine(first.DesktopPath, "report.docx")));
        await using var app = await first.ReopenAsync();
        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();
        Assert.True(again.TagNames.CanPutBack);
        await again.PutBackAsync(again.TagNames);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task A_folder_already_named_with_its_group_is_left_alone()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Coding – Tools", "tool.py"));
        var studio = await OpenAllowedAsync(app);
        await studio.GuessAsync();

        await studio.PreviewAsync(studio.TagNames);

        Assert.False(studio.TagNames.HasPreview);
        Assert.Contains(studio.TagNames.LeftAlone, line => line == "Coding – Tools: Its name already starts with the group's name.");
    }

    /// <summary>Found in review 2026-09-24: the board kept the old names, so a second press said the folder was gone and reopening moved it to Not sure.</summary>
    [Fact]
    public async Task Renamed_folders_stay_in_their_group_and_a_second_press_leaves_them_alone()
    {
        await using var first = await TestApp.StartAsync();
        MakeGroupDesktop(first);
        var studio = await OpenAllowedAsync(first);
        await studio.GuessAsync();
        await studio.PreviewAsync(studio.TagNames);
        await studio.ApplyAsync(studio.TagNames);

        await studio.PreviewAsync(studio.TagNames);

        Assert.Contains("Coding – Python stuff: Its name already starts with the group's name.", studio.TagNames.LeftAlone);
        Assert.DoesNotContain(studio.TagNames.LeftAlone, line => line.Contains("no longer on your Desktop", StringComparison.Ordinal));
        await using var app = await first.ReopenAsync();
        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();
        Assert.Contains(again.Groups.Single(group => group.Name == "Coding").Items, item => item.Name == "Coding – Python stuff");
        Assert.Empty(again.NotSure);
        await again.PutBackAsync(again.TagNames);
        Assert.Contains(again.Groups.Single(group => group.Name == "Coding").Items, item => item.Name == "Python stuff");
    }

    /// <summary>Found in the end-to-end check 2026-09-25: the next look emptied every group and put the new folders under Not sure.</summary>
    [Fact]
    public async Task Folder_by_group_keeps_the_groups_with_their_new_folders_even_after_reopening()
    {
        await using var first = await TestApp.StartAsync();
        MakeGroupDesktop(first);
        var studio = await OpenAllowedAsync(first);
        await studio.GuessAsync();
        await studio.RenameGroupAsync("Documents", "Writing");
        await studio.PreviewAsync(studio.FolderByGroup);

        await studio.ApplyAsync(studio.FolderByGroup);

        AssertGroups(studio, ("Coding", ["Coding"]), ("Writing", ["Writing"]));
        Assert.True(Directory.Exists(Path.Combine(first.DesktopPath, "Writing", "Essays")));
        await using var app = await first.ReopenAsync();
        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();
        AssertGroups(again, ("Coding", ["Coding"]), ("Writing", ["Writing"]));
        Assert.False(again.HasMessage);
    }

    /// <summary>Found in the end-to-end check 2026-09-25: the board kept showing folders Put back had removed.</summary>
    [Fact]
    public async Task Put_back_of_Folder_by_group_returns_each_thing_to_its_group_on_the_board()
    {
        await using var first = await TestApp.StartAsync();
        MakeGroupDesktop(first);
        var studio = await OpenAllowedAsync(first);
        await studio.GuessAsync();
        await studio.RenameGroupAsync("Documents", "Writing");
        await studio.PreviewAsync(studio.FolderByGroup);
        await studio.ApplyAsync(studio.FolderByGroup);

        await studio.PutBackAsync(studio.FolderByGroup);

        AssertGroups(studio, ("Coding", ["Python stuff"]), ("Writing", ["Essays", "report.docx"]));
        await using var app = await first.ReopenAsync();
        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();
        AssertGroups(again, ("Coding", ["Python stuff"]), ("Writing", ["Essays", "report.docx"]));
        Assert.False(again.HasMessage);
    }

    [Fact]
    public async Task Tag_names_after_Folder_by_group_leaves_the_group_folder_its_own_name()
    {
        await using var app = await TestApp.StartAsync();
        MakeGroupDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.GuessAsync();
        await studio.PreviewAsync(studio.FolderByGroup);
        await studio.ApplyAsync(studio.FolderByGroup);

        await studio.PreviewAsync(studio.TagNames);

        Assert.False(studio.TagNames.HasPreview);
        Assert.Contains("Coding: This is the Coding folder itself, so it keeps its name.", studio.TagNames.LeftAlone);
    }

    /// <summary>Found in the end-to-end check 2026-09-25: the other cards kept lists of things that had moved.</summary>
    [Fact]
    public async Task After_one_card_moves_things_the_other_cards_lists_are_cleared()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        MakeGroupDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.GuessAsync();
        await studio.PreviewAsync(studio.OldStuff);
        await studio.PreviewAsync(studio.TagNames);
        await studio.PreviewAsync(studio.FolderByGroup);
        Assert.True(studio.OldStuff.HasPreview);
        Assert.True(studio.TagNames.HasPreview);

        await studio.ApplyAsync(studio.FolderByGroup);

        Assert.False(studio.OldStuff.HasPreview);
        Assert.False(studio.TagNames.HasPreview);
        Assert.Equal(DesktopStudioViewModel.ListOutOfDate, studio.OldStuff.Message);
        Assert.Equal(DesktopStudioViewModel.ListOutOfDate, studio.TagNames.Message);
    }

    [Fact]
    public async Task After_Put_back_the_other_cards_lists_are_cleared()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.PreviewAsync(studio.OldStuff);
        await studio.ApplyAsync(studio.OldStuff);
        await studio.GuessAsync();
        await studio.PreviewAsync(studio.FolderByGroup);
        Assert.True(studio.FolderByGroup.HasPreview);

        await studio.PutBackAsync(studio.OldStuff);

        Assert.False(studio.FolderByGroup.HasPreview);
        Assert.Equal(DesktopStudioViewModel.ListOutOfDate, studio.FolderByGroup.Message);
    }

    /// <summary>Found in the end-to-end check 2026-09-25: a stale list left an empty Old stuff folder that Put back did not offer to remove.</summary>
    [Fact]
    public async Task When_nothing_on_the_list_is_still_there_no_empty_folder_is_left_behind()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.PreviewAsync(studio.OldStuff);
        Directory.Delete(Path.Combine(app.DesktopPath, "Old project"), recursive: true);
        File.Delete(Path.Combine(app.DesktopPath, "old notes.txt"));
        var before = Snapshot(app);

        var result = await studio.ApplyAsync(studio.OldStuff);

        Assert.Equal("Nothing was moved.", result!.Summary);
        Assert.Equal(before, Snapshot(app));
        Assert.False(Directory.Exists(Path.Combine(app.DesktopPath, "Old stuff")));
        Assert.False(studio.OldStuff.CanPutBack);
    }

    /// <summary>Found in the end-to-end check 2026-09-25: after Folder by group the old things sit inside the group folders, and Clear old stuff said only "nothing old".</summary>
    [Fact]
    public async Task Clear_old_stuff_after_Folder_by_group_says_why_it_found_nothing()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.GuessAsync();
        await studio.PreviewAsync(studio.FolderByGroup);
        await studio.ApplyAsync(studio.FolderByGroup);
        var before = Snapshot(app);

        await studio.PreviewAsync(studio.OldStuff);

        Assert.False(studio.OldStuff.HasPreview);
        Assert.Equal(DesktopMoveService.OldStuffInGroupFolders, studio.OldStuff.Message);
        Assert.Equal(before, Snapshot(app));

        // Put back, then Clear old stuff finds the old things again, as the message promises.
        await studio.PutBackAsync(studio.FolderByGroup);
        await studio.PreviewAsync(studio.OldStuff);

        Assert.Equal(["old notes.txt", "Old project"], studio.OldStuff.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Clear_old_stuff_with_nothing_old_and_no_group_folders_says_just_that()
    {
        await using var app = await TestApp.StartAsync();
        MakeGroupDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.GuessAsync();

        await studio.PreviewAsync(studio.OldStuff);

        Assert.Equal("Nothing on your Desktop has been left unchanged for 6 months.", studio.OldStuff.Message);
    }

    private static void AssertGroups(DesktopStudioViewModel studio, params (string Name, string[] Items)[] expected)
    {
        Assert.Equal(
            expected.Select(group => group.Name).Order(StringComparer.OrdinalIgnoreCase),
            studio.Groups.Select(group => group.Name).Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (name, items) in expected)
        {
            Assert.Equal(
                items.Order(StringComparer.OrdinalIgnoreCase),
                studio.Groups.Single(group => group.Name == name).Items.Select(item => item.Name).Order(StringComparer.OrdinalIgnoreCase));
        }

        Assert.Empty(studio.NotSure);
    }

    private static void MakeGroupDesktop(TestApp app)
    {
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        app.MakeFile("Desktop", Path.Combine("Essays", "essay.docx"));
        app.MakeFile("Desktop", "report.docx");
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
