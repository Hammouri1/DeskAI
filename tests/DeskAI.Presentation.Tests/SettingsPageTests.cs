using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Privacy and AI page: what is shared, where AI runs, and the refusals that keep a
/// key or a request from going somewhere it should not.
/// </summary>
public sealed class SettingsPageTests
{
    [Fact]
    public async Task A_fresh_install_has_AI_off_and_shares_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();

        await settings.InitializeAsync();

        Assert.Equal("AI is off", settings.AiProcessing);
        Assert.Equal("Off", settings.InternetUse);
        Assert.Equal("None — AI is off", settings.CloudDataShared);
    }

    [Fact]
    public async Task Sharing_choices_are_remembered_and_widening_them_is_detected_first()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        settings.ShareFileName = true;
        Assert.Equal([DisclosureCategory.FileName], settings.PendingExpansions());
        await settings.SavePrivacyAsync();

        var reopened = app.Get<SettingsViewModel>();
        await reopened.InitializeAsync();
        Assert.True(reopened.ShareFileName);
        Assert.Empty(reopened.PendingExpansions());
    }

    [Fact]
    public async Task Online_AI_without_the_sharing_agreement_is_refused()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = false;

        await settings.SaveProviderAsync("generated-test-key-not-real");

        Assert.StartsWith("Settings were not enabled", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.Equal("AI is off", settings.AiProcessing);
        Assert.Null(await app.Vault.RetrieveAsync("DeskAI/OpenRouter", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Online_AI_without_a_key_is_refused()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;

        await settings.SaveProviderAsync(string.Empty);

        Assert.Contains("Enter your OpenRouter key", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.Equal("AI is off", settings.AiProcessing);
    }

    [Theory]
    [InlineData("http://192.168.1.20:11434/v1")]
    [InlineData("https://example.com/v1")]
    public async Task AI_on_this_computer_refuses_an_address_that_is_not_this_computer(string endpoint)
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Local;
        settings.LocalEndpoint = endpoint;
        settings.LocalModel = "llama3";

        await settings.SaveProviderAsync(string.Empty);

        Assert.StartsWith("Settings were not enabled", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.Equal("AI is off", settings.AiProcessing);
    }

    [Fact]
    public async Task A_key_saved_for_one_service_is_never_sent_to_another()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.SelectedCloudProviderIndex = 0;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        await settings.SaveProviderAsync("generated-openrouter-key");

        // Switch to a different service without typing a key for it.
        settings.SelectedCloudProviderIndex = 1;
        settings.CloudModel = "gpt-4o-mini";
        await settings.SaveProviderAsync(string.Empty);

        Assert.StartsWith("Settings were not enabled", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.Null(await app.Vault.RetrieveAsync(
            CloudProviderCatalog.All[1].CredentialReference, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Spaces_and_line_breaks_copied_around_a_key_are_removed_before_saving()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;

        await settings.SaveProviderAsync("  sk-or-generated-test-key \r\n");

        Assert.StartsWith("Saved.", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.Equal("sk-or-generated-test-key", await app.Vault.RetrieveAsync(
            "DeskAI/OpenRouter", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Bearer sk-or-generated-test-key")]
    [InlineData("sk-or-generated test-key")]
    public async Task A_key_with_a_space_inside_is_refused_with_a_reason(string pasted)
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;

        await settings.SaveProviderAsync(pasted);

        Assert.Contains("space", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.Null(await app.Vault.RetrieveAsync("DeskAI/OpenRouter", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_key_that_does_not_look_like_the_chosen_services_is_saved_with_a_warning()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.SelectedCloudProviderIndex = 0;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;

        // Shaped like a key from a different service.
        await settings.SaveProviderAsync("sk-proj-generated-test-key");

        Assert.StartsWith("Saved.", settings.ProviderStatus, StringComparison.Ordinal);
        Assert.Contains("usually start with \"sk-or-\"", settings.ProviderStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_daily_limit_stops_requests_once_reached()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        settings.SelectedModeIndex = (int)AiMode.Cloud;
        settings.CloudModel = "openai/gpt-4o-mini";
        settings.CloudConsent = true;
        settings.DailyRequestLimit = 1;
        await settings.SaveProviderAsync("generated-test-key-not-real");
        var organize = app.Get<PracticeViewModel>();
        await organize.InitializeAsync();

        await organize.GetAiSuggestionsCommand.ExecuteAsync(null);
        await organize.GetAiSuggestionsCommand.ExecuteAsync(null);

        Assert.Single(app.Internet.Requests);
        Assert.Contains("today's online AI limit", organize.AiPreviewMessage, StringComparison.Ordinal);
    }
}
