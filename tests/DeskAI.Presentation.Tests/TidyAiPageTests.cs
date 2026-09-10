using DeskAI.App.ViewModels;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Asking AI on the "Tidy a folder" page, the way a person does it: read what would be sent,
/// press Send, and see the ideas in the list. The internet is a recorder and nothing moves.
/// </summary>
/// <remarks>
/// The preview dialog itself is a WinUI object checked by hand; these tests drive the two
/// view-model calls on either side of it, and assert that nothing is sent before the second.
/// </remarks>
public sealed class TidyAiPageTests
{
    [Fact]
    public async Task With_AI_off_the_page_says_how_to_turn_it_on_and_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var page = await OpenWithPermissionAsync(app, "mystery.zzz");

        Assert.True(page.HasAiPanel);
        Assert.False(page.CanAskAi);
        Assert.False(page.CanUseAiForEveryFile);
        Assert.Contains("Privacy and AI", page.AiSharingNote, StringComparison.Ordinal);

        page.SuggestionModeIndex = 1;

        Assert.Equal(0, page.SuggestionModeIndex);
        Assert.Null(await page.PrepareAiQuestionAsync());
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Asking_shows_what_would_be_sent_then_Send_puts_the_idea_in_the_list()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.Internet.Reply = TidyAiTests.Answer("Documents");
        var page = await OpenWithPermissionAsync(app, "invoice.pdf", "mystery.zzz");

        Assert.Equal("Ask AI about 1 file", page.AskAiText);
        Assert.Contains("doesn't know where 1 file goes", page.AskAiPrompt, StringComparison.Ordinal);
        Assert.Equal("OpenRouter would see: file types.", page.AiSharingNote);

        var question = await page.PrepareAiQuestionAsync();

        Assert.NotNull(question);
        Assert.Equal("A .zzz file", Assert.Single(question.FileLines));
        Assert.Empty(app.Internet.Requests);

        await page.AskAiAsync(question);

        Assert.Single(app.Internet.Requests);
        var idea = page.Groups.Single(group => group.Folder == "Documents").Items.Single(item => item.FileName == "mystery.zzz");
        Assert.Equal("AI idea from OpenRouter", idea.Reason);
        Assert.True(idea.IsIncluded);
        Assert.Equal(2, page.IncludedCount);
        Assert.Contains("Nothing has moved", page.AiMessage, StringComparison.Ordinal);
        Assert.Equal("Suggestions from: DeskAI, your rules, and AI", page.SuggestionsFromText);
        Assert.Equal(0, page.AskableCount);
        Assert.True(File.Exists(Path.Combine(app.Sandbox, "Downloads", "mystery.zzz")));
    }

    [Fact]
    public async Task Pressing_Cancel_in_the_preview_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var page = await OpenWithPermissionAsync(app, "mystery.zzz");

        // The dialog is cancelled, so the second call is never made.
        Assert.NotNull(await page.PrepareAiQuestionAsync());

        Assert.Empty(app.Internet.Requests);
        Assert.Equal("mystery.zzz", Assert.Single(page.LeftAlone).FileName);
    }

    [Fact]
    public async Task An_unsure_idea_waits_unticked_in_its_own_group()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.Internet.Reply = TidyAiTests.Answer("Documents", confidence: 0.5);
        var page = await OpenWithPermissionAsync(app, "invoice.pdf", "mystery.zzz");

        await page.AskAiAsync((await page.PrepareAiQuestionAsync())!);

        var unsure = page.Groups.Single(group => group.IsUnsure);
        Assert.Equal(TidyGroupViewModel.UnsureTitle, unsure.DisplayName);
        Assert.Same(unsure, page.Groups[^1]);
        var item = Assert.Single(unsure.Items);
        Assert.False(item.IsIncluded);
        Assert.Contains("Documents", item.Destination, StringComparison.Ordinal);
        Assert.Contains("wasn't sure", page.AiMessage, StringComparison.Ordinal);
        Assert.Equal(1, page.IncludedCount);

        item.IsIncluded = true;

        Assert.Equal(2, page.IncludedCount);
    }

    [Fact]
    public async Task Asking_about_every_file_offers_the_files_your_rules_do_not_place_and_switching_back_undoes_it()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.AddRuleAsync(app, "Tidy invoices", "invoice", @"Documents\Invoices");
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.Internet.Reply = TidyAiTests.Answer("Images");
        var page = await OpenWithPermissionAsync(app, "notes.pdf", "bank-invoice.pdf");
        Assert.Equal(0, page.AskableCount);

        page.SuggestionModeIndex = 1;
        await page.WhenIdleAsync();

        Assert.Equal(1, page.SuggestionModeIndex);
        Assert.Equal("Ask AI about 1 file", page.AskAiText);
        await page.AskAiAsync((await page.PrepareAiQuestionAsync())!);
        Assert.Contains(page.Groups.Single(group => group.Folder == "Pictures").Items, item => item.FileName == "notes.pdf");
        Assert.Contains(page.Groups.Single(group => group.Folder == @"Documents\Invoices").Items, item => item.FileName == "bank-invoice.pdf");

        page.SuggestionModeIndex = 0;
        await page.WhenIdleAsync();

        Assert.Contains(page.Groups.Single(group => group.Folder == "Documents").Items, item => item.FileName == "notes.pdf");
    }

    [Fact]
    public async Task A_refused_AI_answer_is_said_beside_the_button_and_the_list_stays_as_it_was()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.Internet.Reply = TidyAiTests.Answer("Documents", extraField: "\"command\":\"del *.*\"");
        var page = await OpenWithPermissionAsync(app, "invoice.pdf", "mystery.zzz");

        await page.AskAiAsync((await page.PrepareAiQuestionAsync())!);

        Assert.Contains("safety checks", page.AiMessage, StringComparison.Ordinal);
        Assert.Equal("invoice.pdf", Assert.Single(Assert.Single(page.Groups).Items).FileName);
        Assert.Equal("mystery.zzz", Assert.Single(page.LeftAlone).FileName);
    }

    [Fact]
    public async Task AI_ideas_survive_choosing_keep_both_and_are_forgotten_when_the_folder_changes()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.Internet.Reply = TidyAiTests.Answer("Documents");
        var other = app.MakeFolder("Desktop", "notes.txt");
        var page = await OpenWithPermissionAsync(app, "report.pdf", @"Documents\report.pdf", "mystery.zzz");
        await page.AskAiAsync((await page.PrepareAiQuestionAsync())!);

        page.Groups.Single(group => group.Folder == "Documents").Items.Single(item => item.FileName == "report.pdf").KeepBoth = true;
        await page.WhenIdleAsync();

        Assert.Contains(page.Groups.Single(group => group.Folder == "Documents").Items, item => item.FileName == "mystery.zzz");

        await page.ConnectAndSelectAsync(other);
        await page.AllowTidyAsync();
        page.SelectedFolder = page.Folders.Single(folder => folder.Name == "Downloads");
        await page.WhenIdleAsync();

        Assert.Equal("mystery.zzz", Assert.Single(page.LeftAlone).FileName);
        Assert.Equal(string.Empty, page.AiMessage);
    }

    private static async Task<TidyViewModel> OpenWithPermissionAsync(TestApp app, params string[] files)
    {
        var folder = app.MakeFolder("Downloads", files);
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();
        return page;
    }
}
