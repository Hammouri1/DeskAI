using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeskAI.AI.Transport;
using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Turning on online AI in Settings and asking for ideas on the Organize page, as a person
/// does it. The internet is a recorder: nothing here reaches a real AI service.
/// </summary>
public sealed partial class AiJourneyTests
{
    private const string GeneratedKey = "generated-test-key-not-real";

    [Fact]
    public async Task Turning_on_online_AI_then_asking_for_ideas_sends_one_request_to_the_chosen_service()
    {
        await using var app = await TestApp.StartAsync();
        app.Internet.Reply = SuggestEverythingAsDocuments;
        await TurnOnOpenRouterAsync(app);

        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();
        await organize.GetAiSuggestionsCommand.ExecuteAsync(null);

        var request = Assert.Single(app.Internet.Requests);
        Assert.Equal("openrouter.ai", request.Endpoint.Host);
        Assert.Equal($"Bearer {GeneratedKey}", request.Headers["Authorization"]);
        Assert.Contains("\"model\":\"openai/gpt-4o-mini\"", request.Body, StringComparison.Ordinal);
        Assert.True(organize.AiSuggestions.Count > 0, $"{organize.AiPreviewMessage} | sent: {request.Body}");
        Assert.Contains("OpenRouter", organize.AiDisclosureSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_what_was_agreed_to_is_sent()
    {
        await using var app = await TestApp.StartAsync();
        app.Internet.Reply = SuggestEverythingAsDocuments;
        await TurnOnOpenRouterAsync(app);

        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();
        await organize.GetAiSuggestionsCommand.ExecuteAsync(null);

        // The default agreement shares file types only, so no sample file name may appear.
        var body = Assert.Single(app.Internet.Requests).Body;
        Assert.DoesNotContain("course-notes", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("semester-budget", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".pdf", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_AI_off_asking_for_ideas_sends_nothing_and_says_so()
    {
        await using var app = await TestApp.StartAsync();
        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();

        await organize.GetAiSuggestionsCommand.ExecuteAsync(null);

        Assert.Empty(app.Internet.Requests);
        Assert.Empty(organize.AiSuggestions);
        Assert.False(string.IsNullOrWhiteSpace(organize.AiPreviewMessage));
    }

    [Fact]
    public async Task A_rejected_key_is_reported_in_plain_words()
    {
        await using var app = await TestApp.StartAsync();
        app.Internet.Reply = _ => new AiHttpResponse(HttpStatusCode.Unauthorized, "{}");
        await TurnOnOpenRouterAsync(app);

        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();
        await organize.GetAiSuggestionsCommand.ExecuteAsync(null);

        Assert.Single(app.Internet.Requests);
        Assert.Contains("did not accept the saved key", organize.AiPreviewMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_page_shows_online_AI_as_on_after_saving_and_after_reopening()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);

        var reopened = app.Get<SettingsViewModel>();
        await reopened.InitializeAsync();

        Assert.Equal("Online with OpenRouter", reopened.AiProcessing);
        Assert.True(reopened.CloudConsent);
        Assert.Equal("openai/gpt-4o-mini", reopened.CloudModel);
    }

    [Fact]
    public async Task Removing_the_key_turns_AI_off_and_nothing_is_sent_afterwards()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        await settings.RemoveCloudKeyAsync();

        Assert.Null(await app.Vault.RetrieveAsync("DeskAI/OpenRouter", TestContext.Current.CancellationToken));
        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();
        await organize.GetAiSuggestionsCommand.ExecuteAsync(null);
        Assert.Empty(app.Internet.Requests);
    }

    private static async Task TurnOnOpenRouterAsync(TestApp app)
    {
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.SelectedCloudProviderIndex = 0;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync(GeneratedKey);
        Assert.StartsWith("Saved.", settings.ProviderStatus, StringComparison.Ordinal);
    }

    /// <summary>A well-behaved AI reply that echoes the file IDs it was given.</summary>
    private static AiHttpResponse SuggestEverythingAsDocuments(string requestBody)
    {
        var ids = FileIdPattern().Matches(requestBody).Select(match => match.Groups[1].Value).Distinct();
        var content = JsonSerializer.Serialize(new
        {
            schemaVersion = OrganizationSuggestionRequest.CurrentSchemaVersion,
            suggestions = ids.Select(id => new
            {
                fileId = id,
                category = "Documents",
                confidence = 0.8,
                reason = "Looks like a document.",
            }),
        });
        var envelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 },
        });
        return new AiHttpResponse(HttpStatusCode.OK, envelope);
    }

    // The prompt carries only file IDs as GUIDs, so any GUID in the request is one of them.
    [GeneratedRegex("([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})")]
    private static partial Regex FileIdPattern();
}
