using System.Net;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;
using DeskAI.Core.Roots;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// "Ask DeskAI" on Home, the way a person uses it (V1.1, ADR 0035): type a question, see it in
/// the dialog, press Send, read DeskAI's reply, press the one button. The internet is a
/// recorder; every reply is DeskAI's own wording from its own memory.
/// </summary>
public sealed class AskDeskAiPageTests
{
    [Fact]
    public async Task With_AI_off_the_card_says_how_to_turn_it_on_and_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        home.Ask.Question = "what's taking space?";

        Assert.False(home.Ask.HasAi);
        Assert.False(home.Ask.CanAsk);
        Assert.Equal("Turn on AI in Privacy and AI to ask questions here.", home.Ask.Note);
        Assert.Null(await home.Ask.PrepareAsync());
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task A_search_question_sends_the_words_alone_and_replies_with_matching_names_and_Open_in_Search()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var pictures = app.MakeFolder("Pictures", "beach.jpg", "secret-plan.pdf");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(pictures);
        app.Internet.Reply = _ => QuestionAnswer("search", "Pictures", SearchPart(categories: ["Images"]));
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        home.Ask.Question = "find my holiday photos in Pictures";

        Assert.True(home.Ask.HasAi);
        Assert.Equal("Ask OpenRouter", home.Ask.AskText);
        Assert.Contains("Only your question is sent to OpenRouter", home.Ask.Note, StringComparison.Ordinal);
        var question = await home.Ask.PrepareAsync();
        Assert.NotNull(question);
        Assert.Equal("find my holiday photos in Pictures", question.Sentence);
        Assert.Equal(SentenceTask.Question, question.Task);
        Assert.Empty(app.Internet.Requests);

        await home.Ask.SendAsync(question);

        var body = Assert.Single(app.Internet.Requests).Body;
        Assert.Contains("find my holiday photos in Pictures", body, StringComparison.Ordinal);
        Assert.DoesNotContain("beach", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-plan", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(app.Directory.Path, body, StringComparison.OrdinalIgnoreCase);
        var exchange = Assert.Single(home.Ask.Exchanges);
        Assert.Equal("find my holiday photos in Pictures", exchange.Question);
        Assert.Equal("Found 1 file for \"photos\" in Pictures: beach.jpg.", exchange.Reply);
        Assert.Equal("Open in Search", exchange.ActionText);
        Assert.Empty(home.Ask.Question);

        Assert.Equal("search", home.Ask.Act(exchange));
        var opened = app.Get<SearchViewModel>();
        await opened.InitializeAsync();
        Assert.Equal("photos", opened.Phrase);
        Assert.Equal("beach.jpg", Assert.Single(opened.Results).Name);
    }

    [Fact]
    public async Task A_space_question_is_answered_from_the_storage_summary()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var downloads = app.MakeFolder("Downloads");
        app.MakeFile("Downloads", "movie.mp4", new string('x', 4096));
        app.MakeFile("Downloads", "notes.txt", "small");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(downloads);
        app.Internet.Reply = _ => QuestionAnswer("space", "Downloads", null);
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        home.Ask.Question = "what's taking space in Downloads?";

        await home.Ask.SendAsync((await home.Ask.PrepareAsync())!);

        var exchange = Assert.Single(home.Ask.Exchanges);
        Assert.StartsWith("Your 1 connected folder hold", exchange.Reply, StringComparison.Ordinal);
        Assert.Contains("across 2 files", exchange.Reply, StringComparison.Ordinal);
        Assert.Contains("Largest file: movie.mp4", exchange.Reply, StringComparison.Ordinal);
        Assert.Equal(AskAction.OpenSearch, exchange.Action);
        Assert.Equal("larger than 100 mb", exchange.SearchPhrase);
    }

    [Fact]
    public async Task A_tidy_question_about_a_connected_folder_offers_Organize_on_that_folder()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var downloads = app.MakeFolder("Downloads", "setup.exe");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(downloads);
        app.Internet.Reply = _ => QuestionAnswer("tidy", "downloads", null);
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        home.Ask.Question = "tidy my downloads please";

        await home.Ask.SendAsync((await home.Ask.PrepareAsync())!);

        var exchange = Assert.Single(home.Ask.Exchanges);
        Assert.Equal("Open Downloads in Organize to see what DeskAI would move. Nothing moves until you press Tidy.", exchange.Reply);
        Assert.Equal("Open in Organize", exchange.ActionText);
        Assert.Equal("organize", home.Ask.Act(exchange));
        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        Assert.Equal("Downloads", organize.SelectedFolder!.Name);
        Assert.True(organize.NeedsPermission);
        Assert.True(File.Exists(Path.Combine(downloads, "setup.exe")));
    }

    [Fact]
    public async Task A_tidy_question_about_an_unconnected_personal_folder_offers_to_connect_it()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.MakeFolder("Desktop", "report.pdf");
        app.Internet.Reply = _ => QuestionAnswer("tidy", "Desktop", null);
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        home.Ask.Question = "clean up my desktop";

        await home.Ask.SendAsync((await home.Ask.PrepareAsync())!);

        var exchange = Assert.Single(home.Ask.Exchanges);
        Assert.Equal("Desktop is not connected yet. Connect it and DeskAI will show what it would tidy.", exchange.Reply);
        Assert.Equal("Connect Desktop", exchange.ActionText);
        Assert.Equal(PersonalFolderKind.Desktop, exchange.ConnectKind);
        Assert.Null(home.Ask.Act(exchange));
        Assert.Empty(await app.Get<DeskAI.Core.Search.ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_tidy_question_about_somewhere_else_says_where_DeskAI_works()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        app.Internet.Reply = _ => QuestionAnswer("tidy", @"C:\Program Files", null);
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        home.Ask.Question = @"tidy C:\Program Files";

        await home.Ask.SendAsync((await home.Ask.PrepareAsync())!);

        var exchange = Assert.Single(home.Ask.Exchanges);
        Assert.Contains("is not one of your connected folders", exchange.Reply, StringComparison.Ordinal);
        Assert.Contains("only inside your Desktop, Downloads, Documents, and Pictures", exchange.Reply, StringComparison.Ordinal);
        Assert.False(exchange.HasAction);
    }

    [Fact]
    public async Task An_unsure_answer_says_what_can_be_asked_and_a_bad_answer_is_refused()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        app.Internet.Reply = _ => QuestionAnswer("unsure", null, null);
        home.Ask.Question = "hello?";
        await home.Ask.SendAsync((await home.Ask.PrepareAsync())!);
        Assert.Equal(AskDeskAiService.UnsureReply, Assert.Single(home.Ask.Exchanges).Reply);

        app.Internet.Reply = _ => Envelope("""{"schemaVersion":"1","kind":"delete","folder":"Desktop","search":null}""");
        home.Ask.Question = "delete everything";
        await home.Ask.SendAsync((await home.Ask.PrepareAsync())!);

        Assert.Single(home.Ask.Exchanges);
        Assert.Contains("did not pass DeskAI's checks", home.Ask.Message, StringComparison.Ordinal);
        Assert.Equal(2, app.Internet.Requests.Count);
    }

    [Fact]
    public async Task Cancelling_the_dialog_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        home.Ask.Question = "what's taking space?";

        Assert.NotNull(await home.Ask.PrepareAsync());

        Assert.Empty(app.Internet.Requests);
        Assert.Empty(home.Ask.Exchanges);
        Assert.Equal("what's taking space?", home.Ask.Question);
    }

    private static AiHttpResponse QuestionAnswer(string kind, string? folder, string? search) =>
        Envelope($$"""{"schemaVersion":"1","kind":{{JsonSerializer.Serialize(kind)}},"folder":{{JsonSerializer.Serialize(folder)}},"search":{{search ?? "null"}}}""");

    private static string SearchPart(string[]? categories = null, string? text = null) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = "1",
            endings = Array.Empty<string>(),
            categories = categories ?? [],
            largerThanBytes = (long?)null,
            smallerThanBytes = (long?)null,
            changedInLastDays = (int?)null,
            text,
        });

    private static AiHttpResponse Envelope(string content) =>
        new(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 },
        }));
}
