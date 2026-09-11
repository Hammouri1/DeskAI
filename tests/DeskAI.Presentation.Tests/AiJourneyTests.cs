using System.Net;
using DeskAI.AI.Transport;
using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Turning on online AI in Privacy and AI, then asking it about a folder on Tidy a folder, as a
/// person does it. The internet is a recorder: nothing here reaches a real AI service.
/// </summary>
/// <remarks>
/// Until 2026-09-11 these journeys went through the practice page's "Get AI ideas". That page
/// was retired; Ask AI on a folder the person allowed DeskAI to tidy is the one way to ask AI.
/// What may be sent from a real folder is tested in detail in <see cref="TidyAiTests"/>.
/// </remarks>
public sealed class AiJourneyTests
{
    private const string GeneratedKey = "generated-test-key-not-real";

    [Fact]
    public async Task Turning_on_online_AI_then_asking_sends_one_request_to_the_chosen_service()
    {
        await using var app = await TestApp.StartAsync();
        app.Internet.Reply = TidyAiTests.Answer("Documents");
        await TurnOnOpenRouterAsync(app);
        var page = await OpenAsync(app, "mystery.zzz");

        await page.AskAiAsync((await page.PrepareAiQuestionAsync())!);

        var request = Assert.Single(app.Internet.Requests);
        Assert.Equal("openrouter.ai", request.Endpoint.Host);
        Assert.Equal($"Bearer {GeneratedKey}", request.Headers["Authorization"]);
        Assert.Contains("\"model\":\"openai/gpt-4o-mini\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("OpenRouter", page.AiMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_key_is_reported_in_plain_words()
    {
        await using var app = await TestApp.StartAsync();
        app.Internet.Reply = _ => new AiHttpResponse(HttpStatusCode.Unauthorized, "{}");
        await TurnOnOpenRouterAsync(app);
        var page = await OpenAsync(app, "mystery.zzz");

        await page.AskAiAsync((await page.PrepareAiQuestionAsync())!);

        Assert.Single(app.Internet.Requests);
        Assert.Contains("did not accept the saved key", page.AiMessage, StringComparison.Ordinal);
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
    public async Task Removing_the_key_turns_AI_off_and_nothing_can_be_sent_afterwards()
    {
        await using var app = await TestApp.StartAsync();
        await TurnOnOpenRouterAsync(app);
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        await settings.RemoveCloudKeyAsync();

        Assert.Null(await app.Vault.RetrieveAsync("DeskAI/OpenRouter", TestContext.Current.CancellationToken));
        var page = await OpenAsync(app, "mystery.zzz");
        Assert.False(page.CanAskAi);
        Assert.Null(await page.PrepareAiQuestionAsync());
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

    /// <summary>Tidy a folder on a generated folder DeskAI may tidy.</summary>
    private static async Task<TidyViewModel> OpenAsync(TestApp app, params string[] files)
    {
        var folder = app.MakeFolder("Downloads", files);
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();
        return page;
    }
}
