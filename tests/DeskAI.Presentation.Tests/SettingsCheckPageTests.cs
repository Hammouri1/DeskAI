using System.Net;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// "Check this now" on Privacy and AI, and the refusals around it, the way a person meets them.
/// </summary>
/// <remarks>
/// The owner set up their own key, saw every box on the page still say "AI is off", and had no
/// way to find out why: saving only wrote the choice down, a refusal appeared as small grey text
/// below the fold, and one refusal was a framework sentence about a parameter. These tests hold
/// the page to the opposite: a check that really asks, and a refusal in words someone can act on.
/// The internet here is a recorder — nothing reaches a real service.
/// </remarks>
public sealed class SettingsCheckPageTests
{
    [Fact]
    public async Task With_AI_off_the_check_says_what_to_do_first_and_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.False(settings.ConnectionWorked);
        Assert.Contains("AI is off", settings.ConnectionStatus, StringComparison.Ordinal);
        Assert.Contains("Save AI choice", settings.ConnectionStatus, StringComparison.Ordinal);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Online_AI_that_answers_is_reported_as_working()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync("sk-or-generated-test-key");
        app.Internet.Reply = _ => Answer("ok");

        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.True(settings.ConnectionWorked);
        Assert.Equal("Working. OpenRouter answered.", settings.ConnectionStatus);
        var (endpoint, body, headers) = Assert.Single(app.Internet.Requests);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", endpoint.AbsoluteUri);
        Assert.Equal("Bearer sk-or-generated-test-key", headers["Authorization"]);
        Assert.Contains("Reply with the single word", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check exists to prove a connection, so it must never become a quiet way to send
    /// information about the computer. Nothing about any file belongs in it.
    /// </summary>
    [Fact]
    public async Task The_check_carries_nothing_about_this_computer_even_when_sharing_is_wide_open()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.ShareFileName = true;
        settings.ShareFolderNames = true;
        settings.ShareFullPath = true;
        settings.ShareMetadata = true;
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync("sk-or-generated-test-key");
        app.Internet.Reply = _ => Answer("ok");

        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        var body = Assert.Single(app.Internet.Requests).Body;
        using var sent = JsonDocument.Parse(body);
        var message = sent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Equal("Reply with the single word: ok", message);
        Assert.Single(sent.RootElement.GetProperty("messages").EnumerateArray());
        Assert.DoesNotContain(app.Sandbox, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Desktop", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_key_the_service_rejects_is_reported_with_what_to_do_about_it()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync("sk-or-generated-test-key");
        app.Internet.Reply = _ => new AiHttpResponse(
            HttpStatusCode.Unauthorized, """{"error":{"message":"No auth credentials found"}}""");

        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.False(settings.ConnectionWorked);
        Assert.Contains("did not accept your key", settings.ConnectionStatus, StringComparison.Ordinal);
        Assert.Contains("No auth credentials found", settings.ConnectionStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_key_itself_is_never_repeated_back_onto_the_page()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync("sk-or-generated-test-key");
        // A service that echoes the key back in its refusal.
        app.Internet.Reply = _ => new AiHttpResponse(
            HttpStatusCode.Unauthorized, """{"error":{"message":"key sk-or-generated-test-key is invalid"}}""");

        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("sk-or-generated-test-key", settings.ConnectionStatus, StringComparison.Ordinal);
        Assert.Contains("[your key]", settings.ConnectionStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AI_on_this_computer_is_checked_at_the_address_the_person_typed()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Local;
        settings.LocalEndpoint = "http://127.0.0.1:11434/v1/chat/completions";
        settings.LocalModel = "llama3";
        await settings.SaveProviderAsync(string.Empty);
        app.Internet.Reply = _ => Answer("ok");

        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.True(settings.ConnectionWorked);
        Assert.Equal("Working. The AI on this computer answered.", settings.ConnectionStatus);
        var (endpoint, _, headers) = Assert.Single(app.Internet.Requests);
        Assert.Equal("http://127.0.0.1:11434/v1/chat/completions", endpoint.AbsoluteUri);
        Assert.Empty(headers);
    }

    [Fact]
    public async Task A_local_AI_app_that_is_not_running_is_explained_as_that()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Local;
        settings.LocalEndpoint = "http://127.0.0.1:11434/v1/chat/completions";
        settings.LocalModel = "llama3";
        await settings.SaveProviderAsync(string.Empty);
        app.Internet.Reply = _ => throw new HttpRequestException("connection refused");

        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.False(settings.ConnectionWorked);
        Assert.Contains("is running", settings.ConnectionStatus, StringComparison.Ordinal);
    }

    /// <summary>
    /// The owner's own bug: the model box shows a grey example, they left it empty, and DeskAI
    /// answered with the framework's sentence about a parameter.
    /// </summary>
    [Fact]
    public async Task An_empty_model_name_is_refused_in_words_a_person_can_act_on()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = string.Empty;
        settings.CloudConsent = true;

        await settings.SaveProviderAsync("sk-or-generated-test-key");

        Assert.Contains("Model name", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("modelId", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.NotNull(settings.ProviderProblem);
        Assert.Equal("AI is off", settings.AiProcessing);
        Assert.Null(await app.Vault.RetrieveAsync("DeskAI/OpenRouter", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_empty_local_address_is_refused_in_words_a_person_can_act_on()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Local;
        settings.LocalEndpoint = string.Empty;
        settings.LocalModel = "llama3";

        await settings.SaveProviderAsync(string.Empty);

        Assert.Contains("127.0.0.1", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.NotNull(settings.ProviderProblem);
        Assert.Equal("AI is off", settings.AiProcessing);
    }

    [Fact]
    public async Task Saving_again_clears_a_result_that_belonged_to_the_old_choice()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync("sk-or-generated-test-key");
        app.Internet.Reply = _ => Answer("ok");
        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);
        Assert.True(settings.ConnectionWorked);

        settings.SelectedModeIndex = (int)AiMode.RuleEngineOnly;
        await settings.SaveProviderAsync(string.Empty);

        Assert.Null(settings.ConnectionWorked);
        Assert.False(settings.HasConnectionStatus);
    }

    /// <summary>A check is a real paid request, so it is counted like one.</summary>
    [Fact]
    public async Task Checking_spends_one_request_from_the_daily_allowance()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        settings.DailyRequestLimit = 1;
        await settings.SaveProviderAsync("sk-or-generated-test-key");
        app.Internet.Reply = _ => Answer("ok");

        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);
        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Single(app.Internet.Requests);
        Assert.False(settings.ConnectionWorked);
        Assert.Contains("today's online AI limit", settings.ConnectionStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_the_key_takes_the_old_working_result_with_it()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync("sk-or-generated-test-key");
        app.Internet.Reply = _ => Answer("ok");
        await settings.CheckConnectionAsync(TestContext.Current.CancellationToken);

        await settings.RemoveCloudKeyAsync();

        Assert.Null(settings.ConnectionWorked);
        Assert.False(settings.HasConnectionStatus);
    }

    private static AiHttpResponse Answer(string content) =>
        new(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
        }));
}
