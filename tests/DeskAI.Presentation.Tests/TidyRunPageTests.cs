using DeskAI.App.ViewModels;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Pressing Tidy and Undo on the Organize page, the way a person does, in a generated folder.
/// </summary>
public sealed class TidyRunPageTests
{
    [Fact]
    public async Task Pressing_Tidy_moves_the_ticked_files_and_says_what_happened()
    {
        await using var app = await TestApp.StartAsync();
        var (page, folder) = await OpenAsync(app, "invoice.pdf", "notes.pdf", "holiday.jpg", "mystery.zzz");
        page.Groups.Single(group => group.Folder == "Pictures").IsIncluded = false;
        Assert.Equal("Tidy 2 files", page.TidyButtonText);

        await page.TidyCommand.ExecuteAsync(null);

        Assert.Equal("Done. Downloads: 2 files tidied into 1 folder.", page.ResultSummary);
        Assert.False(page.HasResultSkipped);
        Assert.True(page.CanUndo);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "notes.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));
        Assert.True(File.Exists(Path.Combine(folder, "mystery.zzz")));

        // The list is worked out again: only the unticked photo is still loose and suggested.
        var left = Assert.Single(page.Groups);
        Assert.Equal("Pictures", left.Folder);
        Assert.Equal("mystery.zzz", Assert.Single(page.LeftAlone).FileName);
    }

    [Fact]
    public async Task A_file_that_is_not_safe_to_move_any_more_is_listed_under_the_result_with_its_reason()
    {
        await using var app = await TestApp.StartAsync();
        var (page, folder) = await OpenAsync(app, "invoice.pdf", "notes.pdf");
        File.AppendAllText(Path.Combine(folder, "notes.pdf"), " edited after the list was shown");

        await page.TidyCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Done", page.ResultSummary, StringComparison.Ordinal);
        Assert.Contains("1 of 2", page.ResultSummary, StringComparison.Ordinal);
        var skipped = Assert.Single(page.ResultSkipped);
        Assert.Equal("notes.pdf", skipped.FileName);
        Assert.Contains("changed after the list", skipped.Reason, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
    }

    [Fact]
    public async Task Undo_puts_the_files_back_and_the_Undo_button_goes_away()
    {
        await using var app = await TestApp.StartAsync();
        var (page, folder) = await OpenAsync(app, "invoice.pdf", "holiday.jpg");
        await page.TidyCommand.ExecuteAsync(null);

        await page.UndoCommand.ExecuteAsync(null);

        Assert.StartsWith("Undone.", page.UndoSummary, StringComparison.Ordinal);
        Assert.False(page.CanUndo);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Documents")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Pictures")));
        Assert.Equal(2, page.Groups.Sum(group => group.Items.Count));
    }

    [Fact]
    public async Task Undo_after_taking_back_permission_asks_for_it_first_then_works()
    {
        await using var app = await TestApp.StartAsync();
        var (page, folder) = await OpenAsync(app, "invoice.pdf");
        await page.TidyCommand.ExecuteAsync(null);
        await page.StopTidyingCommand.ExecuteAsync(null);

        Assert.True(page.CanUndo);
        var refused = await page.UndoLastTidyAsync();

        Assert.NotNull(refused);
        Assert.True(refused.NeedsPermission);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));

        // What the page does after the person accepts the permission dialog.
        await page.AllowTidyAsync();
        var undone = await page.UndoLastTidyAsync();

        Assert.NotNull(undone);
        Assert.Equal(1, undone.Restored);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task Choosing_another_folder_clears_the_last_result()
    {
        await using var app = await TestApp.StartAsync();
        var other = app.MakeFolder("Desktop", "notes.pdf");
        var (page, _) = await OpenAsync(app, "invoice.pdf");
        await page.TidyCommand.ExecuteAsync(null);
        Assert.True(page.HasResult);

        await page.ConnectAndSelectAsync(other);

        Assert.False(page.HasResult);
        Assert.False(page.CanUndo);
    }

    [Fact]
    public async Task An_unsure_AI_idea_is_not_moved_unless_it_is_ticked()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.Internet.Reply = TidyAiTests.Answer("Documents", confidence: 0.4);
        var (page, folder) = await OpenAsync(app, "invoice.pdf", "mystery.zzz");
        await page.AskAiAsync((await page.PrepareAiQuestionAsync())!);

        await page.TidyCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "mystery.zzz")));
    }

    [Fact]
    public async Task A_ticked_AI_idea_moves_into_the_folder_DeskAI_named()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.Internet.Reply = TidyAiTests.Answer("Documents");
        var (page, folder) = await OpenAsync(app, "mystery.zzz");
        await page.AskAiAsync((await page.PrepareAiQuestionAsync())!);

        await page.TidyCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(folder, "Documents", "mystery.zzz")));
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
