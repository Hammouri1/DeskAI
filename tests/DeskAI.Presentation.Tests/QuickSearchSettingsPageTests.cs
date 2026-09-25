using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Rules;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Quick search card on My workspace, and what it means for the icon near the clock and for
/// closing the window. The shortcut and the icon are recorded, never real.
/// </summary>
public sealed class QuickSearchSettingsPageTests
{
    [Fact]
    public async Task The_card_starts_on_with_Sparky_chosen_among_seven()
    {
        await using var app = await TestApp.StartAsync();
        var card = await OpenCardAsync(app);

        Assert.True(card.IsOn);
        Assert.Equal(7, card.Buddies.Count);
        Assert.Equal("Sparky", Assert.Single(card.Buddies, tile => tile.IsChosen).Name);
        Assert.Equal("Works while DeskAI is open or near the clock. Closing the window keeps it near the clock; quit from the icon's menu there.", QuickSearchCardViewModel.WorksWhen);
        Assert.False(card.HasShortcutProblem);
        Assert.Equal(["Choose Sparky", "Choose Archie the owl"], card.Buddies.Take(2).Select(tile => tile.ChooseName));
    }

    [Fact]
    public async Task The_switch_stops_and_starts_listening_for_the_shortcut()
    {
        await using var app = await TestApp.StartAsync();
        var card = await OpenCardAsync(app);

        await card.SetOnAsync(false);
        Assert.False(app.HotKey.IsListening);
        Assert.False(card.IsOn);
        Assert.False(app.Presence.IsShowing);

        await card.SetOnAsync(true);
        Assert.True(app.HotKey.IsListening);
        Assert.True(app.Presence.IsShowing);
    }

    [Fact]
    public async Task Off_is_kept_after_reopening()
    {
        await using var app = await TestApp.StartAsync();
        await (await OpenCardAsync(app)).SetOnAsync(false);

        await using var reopened = await app.ReopenAsync();
        await reopened.Get<QuickSearchSwitch>().ApplyStoredAsync();

        Assert.False((await OpenCardAsync(reopened)).IsOn);
        Assert.False(reopened.HotKey.IsListening);
    }

    [Fact]
    public async Task The_chosen_buddy_is_kept_after_reopening()
    {
        await using var app = await TestApp.StartAsync();
        await (await OpenCardAsync(app)).ChooseBuddyAsync(SearchBuddy.Fetch);

        await using var reopened = await app.ReopenAsync();
        var card = await OpenCardAsync(reopened);

        Assert.Equal("Fetch the fox", Assert.Single(card.Buddies, tile => tile.IsChosen).Name);
    }

    [Fact]
    public async Task Another_program_using_the_shortcut_is_said_plainly()
    {
        await using var app = await TestApp.StartAsync();
        app.HotKey.RefuseNext = true;
        var card = await OpenCardAsync(app);

        await card.SetOnAsync(true);

        Assert.Equal("Another program already uses Ctrl + Alt + Space, so quick search can't listen for it.", card.ShortcutProblem);
        Assert.True(card.HasShortcutProblem);
    }

    [Fact]
    public async Task With_quick_search_on_and_checking_off_closing_keeps_DeskAI_near_the_clock_without_Pause()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        var presence = app.Get<BackgroundPresenceController>();

        Assert.True(app.Presence.IsShowing);
        Assert.Equal(QuickSearchWords.Tooltip, app.Presence.Tooltips[^1]);
        Assert.Equal(new PresenceMenu(OffersPause: false, IsPaused: false, OffersFind: true), app.Presence.Menus[^1]);
        Assert.True(presence.KeepsRunningWhenClosed);
        Assert.True(presence.QuickSearchKeepsItRunning);
    }

    [Fact]
    public async Task With_both_off_closing_quits()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().SetOnAsync(false);

        Assert.False(app.Presence.IsShowing);
        Assert.False(app.Get<BackgroundPresenceController>().KeepsRunningWhenClosed);
    }

    [Fact]
    public async Task When_the_icon_cannot_show_closing_really_quits()
    {
        await using var app = await TestApp.StartAsync();
        app.Presence.RefuseToShow = true;

        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();

        Assert.False(app.Presence.IsShowing);
        Assert.False(app.Get<BackgroundPresenceController>().KeepsRunningWhenClosed);
    }

    [Fact]
    public async Task With_checking_on_the_checking_words_stay_and_the_shortcut_is_added()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        var checking = AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground };
        var presence = app.Get<BackgroundPresenceController>();

        presence.Refresh(checking);

        Assert.Equal(QuickSearchWords.WithChecking(BackgroundCheckingChoice.Tooltip(checking, null)), app.Presence.Tooltips[^1]);
        Assert.True(app.Presence.Menus[^1].OffersPause);
        Assert.True(app.Presence.Menus[^1].OffersFind);
        Assert.False(presence.QuickSearchKeepsItRunning);

        await app.Get<QuickSearchSwitch>().SetOnAsync(false);

        Assert.Equal(BackgroundCheckingChoice.Tooltip(checking, null), app.Presence.Tooltips[^1]);
        Assert.False(app.Presence.Menus[^1].OffersFind);
        Assert.True(presence.KeepsRunningWhenClosed);
    }

    [Fact]
    public void The_longest_tooltip_fits_what_Windows_allows()
    {
        var paused = AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground, IsPaused = true };
        var running = AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground };

        foreach (var tooltip in new[]
        {
            QuickSearchWords.Tooltip,
            QuickSearchWords.WithChecking(BackgroundCheckingChoice.Tooltip(paused, 999)),
            QuickSearchWords.WithChecking(BackgroundCheckingChoice.Tooltip(running, 999)),
        })
        {
            Assert.True(tooltip.Length < 128, tooltip);
        }
    }

    [Fact]
    public async Task Find_a_file_from_the_icon_asks_for_the_bar()
    {
        await using var app = await TestApp.StartAsync();
        var asked = 0;
        app.Get<BackgroundPresenceController>().FindRequested += (_, _) => asked++;

        app.Presence.RaiseFind();

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Start_fresh_turns_quick_search_back_on()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().SetOnAsync(false);
        await app.Get<QuickSearchSettingsService>().SetBuddyAsync(SearchBuddy.Inky, TestContext.Current.CancellationToken);

        await app.Get<SettingsViewModel>().StartFreshAsync();

        Assert.True(app.HotKey.IsListening);
        Assert.True(app.Presence.IsShowing);
        var card = await OpenCardAsync(app);
        Assert.True(card.IsOn);
        Assert.Equal("Sparky", Assert.Single(card.Buddies, tile => tile.IsChosen).Name);
    }

    [Fact]
    public void Closing_no_longer_claims_to_stop_everything() =>
        Assert.StartsWith(
            "Checking happens only while DeskAI is open. Closing it stops checking.",
            BackgroundCheckingChoice.MoreDetails(AutomaticCheckMode.WhileAppIsOpen, 0),
            StringComparison.Ordinal);

    private static async Task<QuickSearchCardViewModel> OpenCardAsync(TestApp app)
    {
        var workspace = app.Get<WorkspaceViewModel>();
        await workspace.InitializeAsync();
        return workspace.QuickSearch;
    }
}
