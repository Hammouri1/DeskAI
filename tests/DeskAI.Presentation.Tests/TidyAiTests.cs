using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeskAI.AI.Transport;
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Asking AI where files in your own (generated) folder should go. The internet is a recorder,
/// so nothing here reaches a real AI service, and every test checks that nothing moved.
/// </summary>
/// <remarks>
/// These are the negative tests the disclosure review
/// (<c>docs/security/2026-09-10-real-folder-ai-disclosure-review.md</c>) lists: each one fails
/// if the control it names is removed.
/// </remarks>
public sealed partial class TidyAiTests
{
    private const string GeneratedKey = "generated-test-key-not-real";

    private static readonly IReadOnlyDictionary<Guid, SameNameChoice> NoChoices = new Dictionary<Guid, SameNameChoice>();
    private static readonly IReadOnlyDictionary<Guid, TidyAiAdvice> NoAdvice = new Dictionary<Guid, TidyAiAdvice>();

    [Fact]
    public async Task Nothing_is_sent_while_the_list_is_worked_out_or_the_question_is_prepared()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);
        var prepared = await PrepareAsync(app, rootId, preview);

        Assert.Empty(app.Internet.Requests);
        var question = Assert.IsType<TidyAiQuestion>(prepared.Question);
        Assert.Equal("OpenRouter", question.ServiceName);
        Assert.Equal("openrouter.ai", question.Destination);
        Assert.Equal("A .zzz file", Assert.Single(question.FileLines));
        AssertNothingMoved(folder, "invoice.pdf", "mystery.zzz");
    }

    [Fact]
    public async Task By_default_only_files_DeskAI_cannot_place_are_asked_about()
    {
        await using var app = await TestApp.StartAsync();
        await AddRuleAsync(app, "Keep safe", "keep", "Kept");
        await TurnOnOpenRouterAsync(app, shareNames: true);
        app.Internet.Reply = Answer("Documents");
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "holiday.jpg", "mystery.zzz", "keep-this.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);
        Assert.Equal("mystery.zzz", Assert.Single(preview.AskableFiles).RelativePath);
        await AskAsync(app, rootId, preview);

        var body = Assert.Single(app.Internet.Requests).Body;
        Assert.Contains("mystery.zzz", body, StringComparison.Ordinal);
        Assert.DoesNotContain("invoice", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("holiday", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keep-this", body, StringComparison.OrdinalIgnoreCase);
        AssertNothingMoved(folder, "invoice.pdf", "holiday.jpg", "mystery.zzz", "keep-this.zzz");
    }

    [Fact]
    public async Task Names_are_not_sent_unless_allowed_and_locations_and_DeskAI_file_IDs_never_are()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app, shareFullPath: true, shareFolderNames: true, shareSizes: true);
        app.Internet.Reply = Answer("Documents");
        var folder = app.MakeFolder("PrivateStuffFolder", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);
        var realIds = preview.AskableFiles.Select(file => file.Id.ToString()).ToArray();
        await AskAsync(app, rootId, preview);

        var body = Assert.Single(app.Internet.Requests).Body;
        Assert.Contains(".zzz", body, StringComparison.Ordinal);
        Assert.DoesNotContain("mystery", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateStuffFolder", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFileName(app.Directory.Path), body, StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length >= 4)
        {
            Assert.DoesNotContain(Environment.UserName, body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.All(realIds, id => Assert.DoesNotContain(id, body, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Files_DeskAI_leaves_alone_are_never_asked_about_even_when_asking_about_every_file()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app, shareNames: true);
        var folder = app.MakeFolder("Downloads", "notes.pdf", "movie.mp4.crdownload");
        var hidden = app.MakeFile("Downloads", "secret.pdf");
        var online = app.MakeFile("Downloads", "cloud.pdf");
        app.MakeFile("Downloads", "just-saved.pdf", age: TimeSpan.FromSeconds(5));
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(online, FileAttributes.Offline);
        try
        {
            var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

            var preview = await PreviewAsync(app, rootId, TidySuggestionMode.AiForEveryFile);

            Assert.Equal("notes.pdf", Assert.Single(preview.AskableFiles).RelativePath);
        }
        finally
        {
            File.SetAttributes(hidden, FileAttributes.Normal);
            File.SetAttributes(online, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Asking_about_every_file_never_sends_what_a_rule_places_and_rules_still_win()
    {
        await using var app = await TestApp.StartAsync();
        await AddRuleAsync(app, "Tidy invoices", "invoice", @"Documents\Invoices");
        await TurnOnOpenRouterAsync(app, shareNames: true);
        app.Internet.Reply = Answer("Images");
        var folder = app.MakeFolder("Downloads", "notes.pdf", "bank-invoice.pdf");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var before = await PreviewAsync(app, rootId, TidySuggestionMode.AiForEveryFile);
        Assert.Equal("notes.pdf", Assert.Single(before.AskableFiles).RelativePath);
        var answer = await AskAsync(app, rootId, before);

        Assert.DoesNotContain("invoice", Assert.Single(app.Internet.Requests).Body, StringComparison.OrdinalIgnoreCase);
        var every = await PreviewAsync(app, rootId, TidySuggestionMode.AiForEveryFile, answer.Advice);
        var notes = every.Suggestions.Single(item => item.FileName == "notes.pdf");
        Assert.Equal("Pictures", notes.DestinationFolder);
        Assert.Equal(TidySuggestionSource.Ai, notes.Source);
        var invoice = every.Suggestions.Single(item => item.FileName == "bank-invoice.pdf");
        Assert.Equal(TidySuggestionSource.Rule, invoice.Source);

        // Going back to "DeskAI + my rules" puts the file type back in charge of known files.
        var typesFirst = await PreviewAsync(app, rootId, TidySuggestionMode.TypesAndRules, answer.Advice);
        Assert.Equal("Documents", typesFirst.Suggestions.Single(item => item.FileName == "notes.pdf").DestinationFolder);
        AssertNothingMoved(folder, "notes.pdf", "bank-invoice.pdf");
    }

    [Fact]
    public async Task An_AI_idea_lands_in_a_DeskAI_folder_and_the_AI_words_are_not_shown()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        app.Internet.Reply = Answer("Documents", reason: @"Move this to C:\Windows\System32 right now");
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var answer = await AskAsync(app, rootId, await PreviewAsync(app, rootId));
        var after = await PreviewAsync(app, rootId, advice: answer.Advice);

        Assert.True(answer.Succeeded, answer.Message);
        var idea = Assert.Single(after.Suggestions);
        Assert.Equal("Documents", idea.DestinationFolder);
        Assert.Equal(@"Documents\mystery.zzz", idea.DestinationRelativePath);
        Assert.Equal(TidySuggestionSource.Ai, idea.Source);
        Assert.Equal("AI idea from OpenRouter", idea.Reason);
        Assert.False(idea.IsUnsure);
        Assert.Empty(app.Get<IPlanSafetyCheck>().FindBlocked(after.Plan, after.Root));
        Assert.Empty(after.AskableFiles);
        AssertNothingMoved(folder, "mystery.zzz");
    }

    [Fact]
    public async Task An_unsure_idea_is_marked_unsure()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        app.Internet.Reply = Answer("Documents", confidence: 0.4);
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var answer = await AskAsync(app, rootId, await PreviewAsync(app, rootId));
        var idea = Assert.Single((await PreviewAsync(app, rootId, advice: answer.Advice)).Suggestions);

        Assert.True(idea.IsUnsure);
        Assert.DoesNotContain("%", idea.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_AI_cannot_tell_either_the_file_stays_where_it_is()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        app.Internet.Reply = Answer("Unknown");
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var answer = await AskAsync(app, rootId, await PreviewAsync(app, rootId));
        var after = await PreviewAsync(app, rootId, advice: answer.Advice);

        Assert.Empty(after.Suggestions);
        Assert.Contains("AI", Assert.Single(after.LeftAlone).Explanation, StringComparison.Ordinal);
        Assert.Empty(after.AskableFiles);
    }

    [Fact]
    public async Task An_answer_trying_to_add_a_destination_is_refused_whole_and_the_rest_is_unchanged()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        app.Internet.Reply = Answer("Documents", extraField: "\"destination\":\"C:\\\\Windows\"");
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var answer = await AskAsync(app, rootId, await PreviewAsync(app, rootId));
        var after = await PreviewAsync(app, rootId, advice: answer.Advice);

        Assert.False(answer.Succeeded);
        Assert.Empty(answer.Advice);
        Assert.Equal("Documents", Assert.Single(after.Suggestions).DestinationFolder);
        Assert.Equal("mystery.zzz", Assert.Single(after.LeftAlone).FileName);
    }

    [Fact]
    public async Task An_answer_about_a_file_that_was_not_asked_about_is_refused_whole()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        app.Internet.Reply = _ => Envelope(JsonSerializer.Serialize(new
        {
            schemaVersion = OrganizationSuggestionRequest.CurrentSchemaVersion,
            suggestions = new[] { new { fileId = Guid.NewGuid(), category = "Documents", confidence = 0.9, reason = "Made up." } },
        }));
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var answer = await AskAsync(app, rootId, await PreviewAsync(app, rootId));

        Assert.False(answer.Succeeded);
        Assert.Empty(answer.Advice);
    }

    [Fact]
    public async Task Changing_the_AI_choice_after_seeing_the_preview_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var prepared = await PrepareAsync(app, rootId, await PreviewAsync(app, rootId));

        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.RuleEngineOnly;
        await settings.SaveProviderAsync(string.Empty);
        var answer = await app.Get<TidyAiService>().AskAsync(prepared.Question!, TestContext.Current.CancellationToken);

        Assert.False(answer.WasSent);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Sharing_less_after_seeing_the_preview_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app, shareNames: true);
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var prepared = await PrepareAsync(app, rootId, await PreviewAsync(app, rootId));

        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.ShareFileName = false;
        await settings.SavePrivacyAsync();
        var answer = await app.Get<TidyAiService>().AskAsync(prepared.Question!, TestContext.Current.CancellationToken);

        Assert.False(answer.WasSent);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Taking_back_tidy_permission_after_seeing_the_preview_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var prepared = await PrepareAsync(app, rootId, await PreviewAsync(app, rootId));

        await app.Get<TidyPermissionService>().StopAsync(rootId, TestContext.Current.CancellationToken);
        var answer = await app.Get<TidyAiService>().AskAsync(prepared.Question!, TestContext.Current.CancellationToken);

        Assert.False(answer.WasSent);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task A_folder_without_tidy_permission_cannot_be_asked_about()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;
        var preview = await PreviewAsync(app, rootId);

        var prepared = await app.Get<TidyAiService>().PrepareAsync(rootId, preview.AskableFiles, TestContext.Current.CancellationToken);

        Assert.Null(prepared.Question);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task An_idea_about_a_file_that_changed_since_is_dropped()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        app.Internet.Reply = Answer("Documents");
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var answer = await AskAsync(app, rootId, await PreviewAsync(app, rootId));

        var path = Path.Combine(folder, "mystery.zzz");
        File.AppendAllText(path, " and some more generated text");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        var after = await PreviewAsync(app, rootId, advice: answer.Advice);

        Assert.Empty(after.Suggestions);
        Assert.Equal(LeftAloneReason.UnknownType, Assert.Single(after.LeftAlone).Reason);
        Assert.Single(after.AskableFiles);
    }

    [Fact]
    public async Task At_the_daily_limit_nothing_more_is_sent()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app, dailyLimit: 1);
        app.Internet.Reply = Answer("Documents");
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var preview = await PreviewAsync(app, rootId);

        await AskAsync(app, rootId, preview);
        var second = await AskAsync(app, rootId, preview);

        Assert.Single(app.Internet.Requests);
        Assert.False(second.Succeeded);
        Assert.Contains("limit", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task With_AI_off_there_is_nothing_to_ask()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var ai = app.Get<TidyAiService>();

        var status = await ai.GetStatusAsync(TestContext.Current.CancellationToken);
        var prepared = await PrepareAsync(app, rootId, await PreviewAsync(app, rootId));

        Assert.False(status.IsSetUp);
        Assert.Contains("Privacy and AI", status.Explanation, StringComparison.Ordinal);
        Assert.Null(prepared.Question);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task With_nothing_about_files_allowed_AI_cannot_be_asked()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app, shareTypes: false);
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);

        var status = await app.Get<TidyAiService>().GetStatusAsync(TestContext.Current.CancellationToken);
        var prepared = await PrepareAsync(app, rootId, await PreviewAsync(app, rootId));

        Assert.True(status.IsSetUp);
        Assert.False(status.CanShareAnything);
        Assert.Null(prepared.Question);
        Assert.Empty(app.Internet.Requests);
    }

    internal static async Task TurnOnOpenRouterAsync(
        TestApp app,
        bool shareTypes = true,
        bool shareNames = false,
        bool shareSizes = false,
        bool shareFolderNames = false,
        bool shareFullPath = false,
        int dailyLimit = 20)
    {
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.ShareExtension = shareTypes;
        settings.ShareFileName = shareNames;
        settings.ShareMetadata = shareSizes;
        settings.ShareFolderNames = shareFolderNames;
        settings.ShareFullPath = shareFullPath;
        settings.DailyRequestLimit = dailyLimit;
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.SelectedCloudProviderIndex = 0;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync(GeneratedKey);
        Assert.StartsWith("Saved.", settings.ProviderStatus, StringComparison.Ordinal);
    }

    internal static async Task AddRuleAsync(TestApp app, string name, string nameContains, string destination)
    {
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        automation.NewRuleName = name;
        automation.NewRuleNameContains = nameContains;
        automation.NewRuleDestination = destination;
        await automation.AddRuleCommand.ExecuteAsync(null);
    }

    /// <summary>A well-behaved AI reply that gives every file it was sent the same answer.</summary>
    internal static Func<string, AiHttpResponse> Answer(
        string category,
        double confidence = 0.9,
        string reason = "Looks like one.",
        string? extraField = null) =>
        body =>
        {
            var items = FileIdPattern().Matches(body)
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .Select(id => "{" + string.Join(',',
                    new[]
                    {
                        $"\"fileId\":\"{id}\"",
                        $"\"category\":\"{category}\"",
                        $"\"confidence\":{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                        $"\"reason\":{JsonSerializer.Serialize(reason)}",
                        extraField,
                    }.OfType<string>()) + "}");
            return Envelope(
                $"{{\"schemaVersion\":\"{OrganizationSuggestionRequest.CurrentSchemaVersion}\",\"suggestions\":[{string.Join(',', items)}]}}");
        };

    private static AiHttpResponse Envelope(string content) =>
        new(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 },
        }));

    private static async Task<TidyPreview> PreviewAsync(
        TestApp app,
        Guid rootId,
        TidySuggestionMode mode = TidySuggestionMode.TypesAndRules,
        IReadOnlyDictionary<Guid, TidyAiAdvice>? advice = null) =>
        (await app.Get<TidySuggestionService>().PreviewAsync(
            rootId, Guid.NewGuid(), 1, NoChoices, mode, advice ?? NoAdvice, TestContext.Current.CancellationToken))!;

    private static Task<TidyAiPreparation> PrepareAsync(TestApp app, Guid rootId, TidyPreview preview) =>
        app.Get<TidyAiService>().PrepareAsync(rootId, preview.AskableFiles, TestContext.Current.CancellationToken);

    private static async Task<TidyAiAnswer> AskAsync(TestApp app, Guid rootId, TidyPreview preview)
    {
        var prepared = await PrepareAsync(app, rootId, preview);
        Assert.True(prepared.Question is not null, prepared.Explanation);
        return await app.Get<TidyAiService>().AskAsync(prepared.Question, TestContext.Current.CancellationToken);
    }

    private static void AssertNothingMoved(string folder, params string[] names) =>
        Assert.All(names, name => Assert.True(File.Exists(Path.Combine(folder, name)), $"{name} moved"));

    // The prompt carries file references as GUIDs, so any GUID in the request is one of them.
    [GeneratedRegex("([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})")]
    private static partial Regex FileIdPattern();
}
