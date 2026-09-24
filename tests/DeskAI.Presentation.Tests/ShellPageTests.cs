using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;
using DeskAI.Core.Appearance;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The frame around every page — the top bar and the pane footer — used the way a person uses
/// it: read the AI pill, type in the search box, flip the dark-mode switch. The window painter
/// is a recording one and the internet is replaced, so nothing here reaches Windows or a service.
/// </summary>
public sealed class ShellPageTests
{
    [Fact]
    public async Task The_AI_pill_says_AI_is_off_until_a_service_is_really_ready()
    {
        await using var app = await TestApp.StartAsync();
        var shell = app.Get<ShellViewModel>();

        await shell.RefreshAsync();
        Assert.Equal("AI off", shell.AiState);

        // Choosing online AI without agreeing to send anything is not a service that can be asked.
        var settings = app.Get<DeskAI.Core.Abstractions.IAiSettingsRepository>();
        await settings.SaveAsync(AiSettings.Default with { Mode = AiMode.Cloud, ProviderId = "openrouter" }, TestContext.Current.CancellationToken);
        await shell.RefreshAsync();
        Assert.Equal("AI off", shell.AiState);

        await settings.SaveAsync(AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
        }, TestContext.Current.CancellationToken);
        await shell.RefreshAsync();
        Assert.Equal("AI: OpenRouter", shell.AiState);

        await settings.SaveAsync(AiSettings.Default with
        {
            Mode = AiMode.Local,
            ProviderId = "local-compatible",
            Endpoint = "http://127.0.0.1:11434/v1/chat/completions",
        }, TestContext.Current.CancellationToken);
        await shell.RefreshAsync();
        Assert.Equal("AI on this computer", shell.AiState);
        shell.Dispose();
    }

    [Fact]
    public async Task Typing_in_the_top_bar_opens_Search_with_that_phrase_already_run_once()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "holiday.jpg");
        var connect = app.Get<SearchViewModel>();
        await connect.InitializeAsync();
        await connect.ConnectFolderAsync(folder);
        var shell = app.Get<ShellViewModel>();

        Assert.False(shell.FindFile("   "));
        Assert.True(shell.FindFile("  photos "));

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        Assert.Equal("photos", search.Phrase);
        Assert.Equal("holiday.jpg", Assert.Single(search.Results).Name);

        var later = app.Get<SearchViewModel>();
        await later.InitializeAsync();
        Assert.Empty(later.Phrase);
        Assert.Empty(later.Results);
        shell.Dispose();
    }

    [Fact]
    public async Task The_top_bar_names_each_page_by_the_menu_name()
    {
        await using var app = await TestApp.StartAsync();
        var shell = app.Get<ShellViewModel>();

        Assert.Equal("Home", shell.PageTitle);
        shell.ShowPage("automation");
        Assert.Equal("Automatic tasks", shell.PageTitle);
        shell.ShowPage("no-such-page");
        Assert.Equal("Automatic tasks", shell.PageTitle);
        shell.ShowPage("studio");
        Assert.Equal("Desktop Studio", shell.PageTitle);
        Assert.Equal(
            ["Home", "Organize", "Search", "Automatic tasks", "Desktop Studio", "My workspace", "Privacy and AI"],
            ShellViewModel.Pages.Select(page => page.Title));
        shell.Dispose();
    }

    [Fact]
    public async Task The_dark_mode_switch_saves_light_or_dark_for_the_window_and_is_remembered()
    {
        await using var first = await TestApp.StartAsync();
        var shell = first.Get<ShellViewModel>();
        await shell.RefreshAsync();

        // Following Windows: the switch shows what the window reports.
        shell.ReportWindowTheme(isDark: false);
        Assert.False(shell.IsDark);
        shell.ReportWindowTheme(isDark: true);
        Assert.True(shell.IsDark);

        await shell.SetDarkAsync(false);
        Assert.False(shell.IsDark);
        Assert.Equal(new AppearanceSettings(ThemeMode.Light, "slate"), Assert.Single(first.Painter.Applied));

        // Once a choice is saved the window's report no longer matters.
        shell.ReportWindowTheme(isDark: true);
        Assert.False(shell.IsDark);

        await shell.SetDarkAsync(false);
        Assert.Single(first.Painter.Applied);
        shell.Dispose();

        await using var app = await first.ReopenAsync();
        var later = app.Get<ShellViewModel>();
        await later.RefreshAsync();
        Assert.False(later.IsDark);
        var workspace = app.Get<WorkspaceViewModel>();
        await workspace.InitializeAsync();
        Assert.Equal(1, workspace.ThemeModeIndex);
        Assert.Equal("Slate", workspace.ChosenLookName);
        later.Dispose();
    }
}
