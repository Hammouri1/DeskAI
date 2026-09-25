using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Appearance;
using DeskAI.Core.Desktop;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Rules;
using DeskAI.Core.Welcome;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Start fresh on Privacy and AI: afterwards DeskAI remembers nothing, and every generated
/// file is exactly where it was.
/// </summary>
public sealed class FreshStartPageTests
{
    [Fact]
    public async Task Start_fresh_forgets_everything_DeskAI_remembers_and_touches_no_file()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "holiday.jpg");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        await BackupPageTests.AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        await BackupPageTests.SaveSearchAsync(app, "Photos", "photos");
        await app.Vault.SaveAsync("DeskAI/OpenRouter", "sk-or-generated-not-a-real-key", TestContext.Current.CancellationToken);
        await app.Get<IAiSettingsRepository>().SaveAsync(AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
        }, TestContext.Current.CancellationToken);
        await app.Get<IAutomaticCheckSettingsRepository>().SaveAsync(
            AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground }, TestContext.Current.CancellationToken);
        app.Get<BackgroundPresenceController>().Refresh(AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground });
        Assert.True(app.Presence.IsShowing);
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        await automation.CheckNowCommand.ExecuteAsync(null);
        await app.Get<IAppearanceSettingsRepository>().SaveAsync(new AppearanceSettings(ThemeMode.Dark, "ocean"), TestContext.Current.CancellationToken);
        await app.Get<IAppSettingsStore>().WriteAsync(WallpaperService.PreviousKey, "old.jpg", TestContext.Current.CancellationToken);
        var before = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order().ToArray();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        Assert.Equal("1", settings.AuthorizedFolderCount);

        await settings.StartFreshAsync();

        Assert.Equal("Done. DeskAI forgot 1 folder, 1 rule, 1 saved search, and 1 saved key. Your files were not touched.", settings.FreshStartStatus);
        Assert.Equal("0", settings.AuthorizedFolderCount);
        Assert.Equal("AI is off", settings.AiProcessing);
        Assert.Empty(await app.Get<IAuthorizedRootRepository>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await app.Get<IRuleRepository>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await app.Get<ISavedSearchRepository>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Null(await app.Vault.RetrieveAsync("DeskAI/OpenRouter", TestContext.Current.CancellationToken));
        var ai = await app.Get<IAiSettingsRepository>().LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AiMode.RuleEngineOnly, ai.Mode);
        Assert.Null(ai.CredentialReference);
        Assert.False(ai.CloudConsentGranted);
        Assert.Empty(await app.Get<IAutomaticCheckHistoryRepository>().ListRecentAsync(50, TestContext.Current.CancellationToken));
        Assert.Equal(AutomaticCheckSettings.Default, await app.Get<IAutomaticCheckSettingsRepository>().LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(AppearanceSettings.Default, await app.Get<IAppearanceSettingsRepository>().LoadAsync(TestContext.Current.CancellationToken));
        Assert.Null(await app.Get<IAppSettingsStore>().ReadAsync(WallpaperService.PreviousKey, TestContext.Current.CancellationToken));
        // Checking in the background stopped; the icon stays only because quick search is on again.
        Assert.Equal(QuickSearchWords.Tooltip(QuickSearchShortcut.CtrlAltD), app.Presence.Tooltips[^1]);
        Assert.False(app.Presence.Menus[^1].OffersPause);
        Assert.Empty(app.Wallpaper.Sets);
        Assert.Equal(before, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.Null(await app.Get<IAuthorizedRootRepository>().FindAsync(rootId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Start_fresh_with_nothing_remembered_says_so_and_still_works()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        await settings.StartFreshAsync();

        Assert.Equal("Done. DeskAI forgot 0 folders, 0 rules, 0 saved searches, and 0 saved keys. Your files were not touched.", settings.FreshStartStatus);
    }

    [Fact]
    public void The_version_line_names_DeskAI_and_a_number()
    {
        Assert.Matches(@"^DeskAI \d+\.\d+\.\d+$", SettingsViewModel.Version);
    }

    [Fact]
    public async Task Start_fresh_brings_the_welcome_back_for_the_next_start()
    {
        await using var app = await TestApp.StartAsync();
        var welcome = app.Get<WelcomeService>();
        Assert.True(await welcome.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        await settings.StartFreshAsync();

        Assert.True(await welcome.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Start_fresh_turns_quick_search_back_on_with_Sparky_and_forgets_the_old_tip_mark()
    {
        await using var app = await TestApp.StartAsync();
        var quick = app.Get<QuickSearchSettingsService>();
        var store = app.Get<IAppSettingsStore>();
        await quick.SetOnAsync(false, TestContext.Current.CancellationToken);
        await quick.SetBuddyAsync(SearchBuddy.Mochi, TestContext.Current.CancellationToken);
        await quick.SetShortcutAsync(QuickSearchShortcut.CtrlAltSpace, TestContext.Current.CancellationToken);
        // Written by a build that still had the Home and Search tip.
        await store.WriteAsync(QuickSearchSettingsService.RetiredTipKey, "yes", TestContext.Current.CancellationToken);
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        await settings.StartFreshAsync();

        Assert.Equal(QuickSearchSettings.Default, await quick.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Null(await store.ReadAsync(QuickSearchSettingsService.RetiredTipKey, TestContext.Current.CancellationToken));
        Assert.Null(await store.ReadAsync(QuickSearchSettingsService.ShortcutKey, TestContext.Current.CancellationToken));
    }
}
