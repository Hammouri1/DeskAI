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

    /// <summary>
    /// "Plan this folder" (ADR 0034): the same dialog with one more line, then the AI's own folder
    /// names as groups, and Tidy makes exactly those folders inside this one.
    /// </summary>
    [Fact]
    public async Task Plan_this_folder_asks_about_every_file_rules_do_not_place_and_groups_by_the_AI_s_folder_names()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true);
        app.Internet.Reply = PlanAnswer(name => name.Contains("invoice", StringComparison.OrdinalIgnoreCase) ? "Bank" : "Trip 2026");
        var page = await OpenWithPermissionAsync(app, "invoice-march.pdf", "beach.jpg", "mystery.zzz");
        Assert.True(page.CanPlanAi);
        Assert.Equal("Plan this folder with AI", page.PlanAiText);

        var question = await page.PreparePlanQuestionAsync();

        Assert.NotNull(question);
        Assert.True(question.IsPlan);
        Assert.Equal(1, page.SuggestionModeIndex);
        Assert.Equal(3, question.FileLines.Count);
        Assert.Empty(app.Internet.Requests);

        await page.AskAiAsync(question);

        var body = Assert.Single(app.Internet.Requests).Body;
        Assert.Contains("at most 12", body, StringComparison.Ordinal);
        Assert.Contains("OpenRouter planned 2 folders for 3 of 3 files", page.AiMessage, StringComparison.Ordinal);
        Assert.Equal(["Bank", "Trip 2026"], page.Groups.Select(group => group.Folder).Order());
        Assert.Contains(page.Groups.Single(group => group.Folder == "Bank").Items, item => item.FileName == "invoice-march.pdf");
        Assert.All(page.Groups.SelectMany(group => group.Items), item => Assert.Equal("AI idea from OpenRouter", item.Reason));
        Assert.True(File.Exists(Path.Combine(app.Sandbox, "Downloads", "beach.jpg")));

        await page.TidyCommand.ExecuteAsync(null);

        Assert.Equal("Done. Downloads: 3 files tidied into 2 folders.", page.ResultSummary);
        Assert.True(File.Exists(Path.Combine(app.Sandbox, "Downloads", "Bank", "invoice-march.pdf")));
        Assert.True(File.Exists(Path.Combine(app.Sandbox, "Downloads", "Trip 2026", "beach.jpg")));
        Assert.True(File.Exists(Path.Combine(app.Sandbox, "Downloads", "Trip 2026", "mystery.zzz")));

        var undone = await page.UndoLastTidyAsync();

        Assert.NotNull(undone);
        Assert.True(File.Exists(Path.Combine(app.Sandbox, "Downloads", "invoice-march.pdf")));
        Assert.True(File.Exists(Path.Combine(app.Sandbox, "Downloads", "beach.jpg")));
    }

    [Fact]
    public async Task A_plan_that_names_a_folder_outside_this_one_is_refused_whole_and_nothing_changes()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true);
        app.Internet.Reply = PlanAnswer(name => name.EndsWith(".pdf", StringComparison.Ordinal) ? @"..\Windows" : "Fine");
        var page = await OpenWithPermissionAsync(app, "invoice.pdf", "mystery.zzz");

        await page.AskAiAsync((await page.PreparePlanQuestionAsync())!);

        Assert.Contains("safety checks", page.AiMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(page.Groups, group => group.Folder == "Fine");
        Assert.Equal("invoice.pdf", Assert.Single(Assert.Single(page.Groups).Items).FileName);
        Assert.Equal("mystery.zzz", Assert.Single(page.LeftAlone).FileName);
    }

    [Fact]
    public async Task A_rule_still_wins_over_the_AI_s_plan()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.AddRuleAsync(app, "Keep invoices", "invoice", "Invoices");
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true);
        app.Internet.Reply = PlanAnswer(_ => "Everything");
        var page = await OpenWithPermissionAsync(app, "invoice.pdf", "mystery.zzz");

        var question = await page.PreparePlanQuestionAsync();
        Assert.Equal("mystery.zzz", Assert.Single(question!.FileLines));
        await page.AskAiAsync(question);

        Assert.Contains(page.Groups.Single(group => group.Folder == "Invoices").Items, item => item.FileName == "invoice.pdf");
        Assert.Contains(page.Groups.Single(group => group.Folder == "Everything").Items, item => item.FileName == "mystery.zzz");
    }

    /// <summary>A plan reply that gives every file the request describes a folder chosen from its name.</summary>
    private static Func<string, DeskAI.AI.Transport.AiHttpResponse> PlanAnswer(Func<string, string> folderFor) =>
        body =>
        {
            using var envelope = System.Text.Json.JsonDocument.Parse(body);
            var prompt = envelope.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            var start = prompt.IndexOf("BEGIN_UNTRUSTED_FILE_DATA", StringComparison.Ordinal) + "BEGIN_UNTRUSTED_FILE_DATA".Length;
            var end = prompt.IndexOf("END_UNTRUSTED_FILE_DATA", StringComparison.Ordinal);
            using var files = System.Text.Json.JsonDocument.Parse(prompt[start..end].Trim());
            var items = files.RootElement.EnumerateArray().Select(file =>
            {
                var id = file.GetProperty("fileId").GetString();
                var name = file.TryGetProperty("fileName", out var fileName) ? fileName.GetString() ?? string.Empty : string.Empty;
                return $$"""{"fileId":"{{id}}","folder":{{System.Text.Json.JsonSerializer.Serialize(folderFor(name))}},"confidence":0.9,"reason":"Planned."}""";
            });
            return new DeskAI.AI.Transport.AiHttpResponse(
                System.Net.HttpStatusCode.OK,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = $$"""{"schemaVersion":"1","suggestions":[{{string.Join(',', items)}}]}""" } } },
                    usage = new { prompt_tokens = 10, completion_tokens = 5 },
                }));
        };

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
