using System.Net;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// "Let AI read this" on Search and Automatic tasks, the way a person uses it (V1.1, ADR 0033):
/// see the words that would be sent, press Send, and find the AI's reading in the box. The
/// internet is a recorder; nothing here can reach a real service or move a file.
/// </summary>
/// <remarks>
/// The dialog itself is a WinUI object checked by hand; these tests drive the two view-model
/// calls on either side of it and assert that nothing is sent before the second.
/// </remarks>
public sealed class SentenceAiPageTests
{
    [Fact]
    public async Task With_AI_off_Search_and_Automatic_tasks_offer_no_AI_button_and_send_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        search.Phrase = "photos from my trip";
        automation.Sentence = "put my bank statements somewhere tidy";

        Assert.False(search.HasAi);
        Assert.False(search.CanAskAi);
        Assert.False(automation.HasAi);
        Assert.False(automation.CanAskAi);
        Assert.Null(await search.PrepareAiReadingAsync());
        Assert.Null(await automation.PrepareAiDraftAsync());
        Assert.Equal("Turn on AI in Privacy and AI first.", search.AiMessage);
        Assert.Equal("Turn on AI in Privacy and AI first.", automation.AiMessage);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Search_shows_the_words_and_the_service_first_and_Send_sends_the_words_alone()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var folder = app.MakeFolder("Pictures", "holiday.jpg", "secret-invoice.pdf");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        app.Internet.Reply = _ => SearchAnswer(categories: ["Images"], text: "holiday");
        search.Phrase = "the pictures from my trip";

        Assert.True(search.HasAi);
        Assert.True(search.CanAskAi);
        Assert.Equal("Let OpenRouter read this", search.AskAiText);

        var question = await search.PrepareAiReadingAsync();

        Assert.NotNull(question);
        Assert.Equal("the pictures from my trip", question.Sentence);
        Assert.Equal("OpenRouter", question.ServiceName);
        Assert.Equal("openrouter.ai", question.Destination);
        Assert.Empty(app.Internet.Requests);

        await search.AskAiToReadAsync(question);

        var request = Assert.Single(app.Internet.Requests);
        Assert.Equal("openrouter.ai", request.Endpoint.Host);
        Assert.Contains("the pictures from my trip", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("holiday", request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-invoice", request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pictures", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(app.Directory.Path, request.Body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("photos holiday", search.Phrase);
        Assert.Contains("OpenRouter read it as \"photos holiday\"", search.AiMessage, StringComparison.Ordinal);
        Assert.Contains("Photos", search.Chips);
        Assert.Contains("Look for \"holiday\"", search.Chips);
        Assert.Equal("holiday.jpg", Assert.Single(search.Results).Name);
        Assert.True(search.CanSaveCurrentSearch);
    }

    [Fact]
    public async Task Cancelling_the_dialog_sends_nothing_and_leaves_the_phrase_alone()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        search.Phrase = "the pictures from my trip";

        Assert.NotNull(await search.PrepareAiReadingAsync());

        Assert.Empty(app.Internet.Requests);
        Assert.Equal("the pictures from my trip", search.Phrase);
        Assert.Empty(search.Chips);
    }

    [Fact]
    public async Task An_answer_DeskAI_cannot_read_is_ignored_and_the_phrase_is_untouched()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        app.Internet.Reply = _ => Envelope("""{"schemaVersion":"1","endings":[],"categories":["Malware"],"largerThanBytes":null,"smallerThanBytes":null,"changedInLastDays":null,"text":null,"command":"del *"}""");
        search.Phrase = "the pictures from my trip";

        await search.AskAiToReadAsync((await search.PrepareAiReadingAsync())!);

        Assert.Single(app.Internet.Requests);
        Assert.Equal("the pictures from my trip", search.Phrase);
        Assert.Contains("did not pass DeskAI's checks", search.AiMessage, StringComparison.Ordinal);
        Assert.Empty(search.Chips);
        Assert.Empty(search.Results);
    }

    [Fact]
    public async Task A_service_refusal_is_said_in_words_and_nothing_else_is_tried()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        app.Internet.Reply = _ => new AiHttpResponse(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}""");
        search.Phrase = "photos";

        await search.AskAiToReadAsync((await search.PrepareAiReadingAsync())!);

        Assert.Single(app.Internet.Requests);
        Assert.Contains("did not accept the saved key", search.AiMessage, StringComparison.Ordinal);
        Assert.Equal("photos", search.Phrase);
    }

    [Fact]
    public async Task The_daily_limit_counts_a_sentence_like_any_other_request()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, dailyLimit: 1);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        app.Internet.Reply = _ => SearchAnswer(categories: ["Images"]);
        search.Phrase = "pictures";
        await search.AskAiToReadAsync((await search.PrepareAiReadingAsync())!);
        Assert.Equal("photos", search.Phrase);

        search.Phrase = "more pictures";
        await search.AskAiToReadAsync((await search.PrepareAiReadingAsync())!);

        Assert.Single(app.Internet.Requests);
        Assert.Contains("today's online AI limit", search.AiMessage, StringComparison.Ordinal);
        Assert.Equal("more pictures", search.Phrase);
    }

    [Fact]
    public async Task Turning_AI_off_between_looking_and_sending_refuses_without_sending()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        search.Phrase = "photos";
        var question = await search.PrepareAiReadingAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.RuleEngineOnly;
        await settings.SaveProviderAsync(string.Empty);

        await search.AskAiToReadAsync(question!);

        Assert.Empty(app.Internet.Requests);
        Assert.Contains("changed since you looked", search.AiMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Automatic_tasks_lets_AI_draft_a_rule_into_the_boxes_and_saves_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        app.Internet.Reply = _ => Envelope("""{"schemaVersion":"1","ending":".pdf","category":null,"largerThanBytes":null,"smallerThanBytes":null,"olderThanDays":null,"nameContains":"statement","destination":"Bank"}""");
        page.Sentence = "put my bank statements somewhere tidy";

        Assert.True(page.HasAi);
        Assert.Equal("Let OpenRouter read this", page.AskAiText);
        var question = await page.PrepareAiDraftAsync();
        Assert.NotNull(question);
        Assert.Equal("put my bank statements somewhere tidy", question.Sentence);
        Assert.Empty(app.Internet.Requests);

        await page.AskAiToDraftAsync(question);

        var request = Assert.Single(app.Internet.Requests);
        Assert.Contains("put my bank statements somewhere tidy", request.Body, StringComparison.Ordinal);
        Assert.Equal("move .pdf statement into Bank", page.Sentence);
        Assert.Equal(".pdf", page.NewRuleExtension);
        Assert.Equal("statement", page.NewRuleNameContains);
        Assert.Equal("Bank", page.NewRuleDestination);
        Assert.Empty(page.NewRuleName);
        Assert.Contains("OpenRouter read it as", page.AiMessage, StringComparison.Ordinal);
        Assert.StartsWith("DeskAI read that as:", page.FormMessage, StringComparison.Ordinal);
        Assert.Empty(page.Rules);
    }

    [Fact]
    public async Task A_folder_the_AI_invents_outside_the_connected_folder_is_refused_and_the_boxes_stay_empty()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        app.Internet.Reply = _ => Envelope("""{"schemaVersion":"1","ending":".pdf","category":null,"largerThanBytes":null,"smallerThanBytes":null,"olderThanDays":null,"nameContains":"statement","destination":"..\\Windows\\System32"}""");
        page.Sentence = "put my bank statements somewhere tidy";

        await page.AskAiToDraftAsync((await page.PrepareAiDraftAsync())!);

        Assert.Equal("put my bank statements somewhere tidy", page.Sentence);
        Assert.Empty(page.NewRuleExtension);
        Assert.Empty(page.NewRuleNameContains);
        Assert.Empty(page.NewRuleDestination);
        Assert.Contains("did not pass DeskAI's checks", page.AiMessage, StringComparison.Ordinal);
        Assert.Empty(page.Rules);
    }

    private static AiHttpResponse SearchAnswer(string[]? categories = null, string? text = null) =>
        Envelope(JsonSerializer.Serialize(new
        {
            schemaVersion = "1",
            endings = Array.Empty<string>(),
            categories = categories ?? [],
            largerThanBytes = (long?)null,
            smallerThanBytes = (long?)null,
            changedInLastDays = (int?)null,
            text,
        }));

    private static AiHttpResponse Envelope(string content) =>
        new(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 },
        }));
}
