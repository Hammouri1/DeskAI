using DeskAI.App.ViewModels;
using DeskAI.Core.Execution;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Organize page after DeskAI is closed and opened again: the last tidy with Undo, and the
/// question about a tidy that was interrupted — used the way a person uses them.
/// </summary>
public sealed class TidyRecoveryPageTests
{
    [Fact]
    public async Task After_reopening_DeskAI_the_last_tidy_is_shown_and_Undo_puts_the_files_back()
    {
        await using var first = await TestApp.StartAsync();
        var (tidyPage, folder) = await OpenAsync(first, "invoice.pdf", "holiday.jpg");
        await tidyPage.TidyCommand.ExecuteAsync(null);
        await using var app = await first.ReopenAsync();

        var page = await ShowAsync(app);

        Assert.StartsWith("Last tidy: 2 files tidied into 2 folders, at ", page.ResultSummary, StringComparison.Ordinal);
        Assert.True(page.CanUndo);
        Assert.False(page.HasInterrupted);

        await page.UndoCommand.ExecuteAsync(null);

        Assert.StartsWith("Undone. 2 files went back", page.UndoSummary, StringComparison.Ordinal);
        Assert.False(page.CanUndo);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Documents")));

        // Once undone, it is not offered again next time either.
        await using var again = await app.ReopenAsync();
        var later = await ShowAsync(again);
        Assert.False(later.HasResult);
        Assert.False(later.CanUndo);
    }

    [Fact]
    public async Task After_reopening_with_tidying_taken_back_Undo_asks_for_it_first()
    {
        await using var first = await TestApp.StartAsync();
        var (tidyPage, folder) = await OpenAsync(first, "invoice.pdf");
        await tidyPage.TidyCommand.ExecuteAsync(null);
        await tidyPage.StopTidyingCommand.ExecuteAsync(null);
        await using var app = await first.ReopenAsync();
        var page = await ShowAsync(app);

        Assert.True(page.NeedsPermission);
        Assert.True(page.CanUndo);
        var refused = await page.UndoLastTidyAsync();
        Assert.True(refused!.NeedsPermission);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));

        // What the page does after the person accepts the permission dialog.
        await page.AllowTidyAsync();
        var undone = await page.UndoLastTidyAsync();

        Assert.Equal(1, undone!.Restored);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task An_interrupted_tidy_is_asked_about_first_and_Tidy_waits_for_the_answer()
    {
        var (app, folder) = await InterruptAsync("a.pdf", "b.pdf", "c.pdf");
        await using var _ = app;
        app.MakeFile("Downloads", "d.pdf");

        var page = await ShowAsync(app);

        Assert.True(page.HasInterrupted);
        Assert.Equal("Your last tidy was interrupted: 2 of 3 files moved.", page.InterruptedTitle);
        Assert.Equal("Undo those 2", page.UndoInterruptedText);
        Assert.Equal("Keep them", page.KeepInterruptedText);
        Assert.True(page.CanUndoInterrupted);
        Assert.False(page.HasInterruptedFiles);
        Assert.False(page.CanUndo);
        Assert.True(page.HasSuggestions);
        Assert.False(page.CanPressTidy);
        Assert.Equal("Answer the question about your last tidy first.", page.TidyNote);
        Assert.True(File.Exists(Path.Combine(folder, "d.pdf")));
    }

    [Fact]
    public async Task Undo_those_puts_them_back_and_the_question_goes()
    {
        var (app, folder) = await InterruptAsync("a.pdf", "b.pdf", "c.pdf");
        await using var _ = app;
        var page = await ShowAsync(app);

        var result = await page.UndoInterruptedAsync();

        Assert.True(result!.Finished);
        Assert.False(page.HasInterrupted);
        Assert.StartsWith("Undone. 2 files went back", page.ResultSummary, StringComparison.Ordinal);
        Assert.False(page.CanUndo);
        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "b.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "c.pdf")));
        Assert.True(page.CanPressTidy);
        Assert.Equal("Nothing moves until you press it. You can undo it.", page.TidyNote);
    }

    [Fact]
    public async Task Keep_them_closes_the_question_and_the_tidy_can_still_be_undone_later()
    {
        var (app, folder) = await InterruptAsync("a.pdf", "b.pdf", "c.pdf");
        await using var _ = app;
        var page = await ShowAsync(app);

        await page.KeepInterruptedCommand.ExecuteAsync(null);

        Assert.False(page.HasInterrupted);
        Assert.StartsWith("Last tidy: 2 files tidied into 1 folder", page.ResultSummary, StringComparison.Ordinal);
        Assert.True(page.CanUndo);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "b.pdf")));

        await page.UndoCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "b.pdf")));
    }

    [Fact]
    public async Task A_file_DeskAI_could_not_tell_about_is_listed_with_where_to_look()
    {
        var (app, folder) = await InterruptAsync(
            ["a.pdf", "b.pdf"],
            changeAfterStop: path => File.AppendAllText(Path.Combine(path, "Documents", "b.pdf"), " edited after the crash"));
        await using var _ = app;

        var page = await ShowAsync(app);

        var file = Assert.Single(page.InterruptedFiles);
        Assert.Equal("b.pdf", file.FileName);
        Assert.Contains("Look for it in Documents", file.Reason, StringComparison.Ordinal);
        Assert.Equal("Your last tidy was interrupted: 1 of 2 files moved.", page.InterruptedTitle);
        Assert.Equal("Undo that file", page.UndoInterruptedText);
        Assert.Equal("Keep it", page.KeepInterruptedText);

        await page.UndoInterruptedAsync();

        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "b.pdf")));
    }

    [Fact]
    public async Task Undo_those_without_permission_asks_for_it_and_keeps_the_question_until_then()
    {
        var (app, folder) = await InterruptAsync("a.pdf", "b.pdf");
        await using var _ = app;
        await app.Get<TidyPermissionService>().StopAsync(await FolderIdAsync(app), TestContext.Current.CancellationToken);
        var page = await ShowAsync(app);

        var refused = await page.UndoInterruptedAsync();

        Assert.True(refused!.NeedsPermission);
        Assert.True(page.HasInterrupted);
        Assert.Contains("permission", page.InterruptedMessage, StringComparison.Ordinal);

        // What the page does after the person accepts the permission dialog.
        await page.AllowTidyAsync();
        Assert.True(page.HasInterrupted);
        var undone = await page.UndoInterruptedAsync();

        Assert.True(undone!.Finished);
        Assert.False(page.HasInterrupted);
        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
    }

    [Fact]
    public async Task An_interrupted_undo_says_how_many_went_back_and_offers_only_OK()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (page, folder) = await OpenAsync(first, "a.pdf", "b.pdf");
        var aMove = page.Groups.SelectMany(group => group.Items).Single(item => item.FileName == "a.pdf").MoveOperationId!.Value;
        await page.TidyCommand.ExecuteAsync(null);
        first.Stopping.StopBefore(aMove, JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => page.UndoLastTidyAsync());
        await using var app = await first.ReopenAsync();

        var reopened = await ShowAsync(app);

        Assert.Equal("Your last undo was interrupted: 2 of 2 files went back.", reopened.InterruptedTitle);
        Assert.Equal("DeskAI checked each file. Every one had gone back.", reopened.InterruptedNote);
        Assert.False(reopened.CanUndoInterrupted);
        Assert.Equal("OK", reopened.KeepInterruptedText);

        await reopened.KeepInterruptedCommand.ExecuteAsync(null);

        Assert.False(reopened.HasInterrupted);
        Assert.False(reopened.CanUndo);
        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "b.pdf")));
    }

    /// <summary>
    /// Tidies a generated Downloads folder in a DeskAI that stops just before recording that the
    /// second file moved, as a power cut would, then opens DeskAI again over the same database.
    /// </summary>
    private static Task<(TestApp App, string Folder)> InterruptAsync(params string[] files) =>
        InterruptAsync(files, changeAfterStop: null);

    private static async Task<(TestApp App, string Folder)> InterruptAsync(string[] files, Action<string>? changeAfterStop)
    {
        var first = await TestApp.StartStoppableAsync();
        var (_, folder) = await OpenAsync(first, files);

        // Through the service rather than the button: the page turns a failure into a message,
        // but a stop is not a failure anyone sees — the window is simply gone.
        var preview = (await first.Get<TidySuggestionService>().PreviewAsync(
            await FolderIdAsync(first), Guid.NewGuid(), 1, new Dictionary<Guid, SameNameChoice>(),
            TidySuggestionMode.TypesAndRules, new Dictionary<Guid, TidyAiAdvice>(), TestContext.Current.CancellationToken))!;
        first.Stopping.StopBefore(
            preview.Suggestions.Single(item => item.FileName == files[1]).MoveOperationId!.Value,
            JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => first.Get<TidyRunService>().TidyAsync(
            preview,
            [.. preview.Suggestions.Select(item => item.MoveOperationId!.Value)],
            TestContext.Current.CancellationToken));
        changeAfterStop?.Invoke(folder);
        return (await first.ReopenAsync(), folder);
    }

    private static async Task<Guid> FolderIdAsync(TestApp app) =>
        Assert.Single(await app.Get<DeskAI.Core.Search.ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken)).Id;

    /// <summary>Opens the Organize page, which shows the first connected folder straight away.</summary>
    private static async Task<TidyViewModel> ShowAsync(TestApp app)
    {
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        return page;
    }

    private static async Task<(TidyViewModel Page, string Folder)> OpenAsync(TestApp app, params string[] files)
    {
        var folder = app.MakeFolder("Downloads", files);
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();
        return (page, folder);
    }
}
