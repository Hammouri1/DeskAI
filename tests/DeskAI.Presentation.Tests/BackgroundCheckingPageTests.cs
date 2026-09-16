using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Keeping DeskAI running after the window is closed, as a person meets it: a switch, a
/// dialog that asks rather than announces, an icon near the clock, and a way back out.
/// </summary>
public sealed class BackgroundCheckingPageTests
{
    [Fact]
    public async Task It_is_off_until_someone_turns_it_on()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();

        await page.InitializeAsync();

        Assert.False(page.KeepsRunningWhenClosed);
        Assert.False(app.Presence.IsShowing);
        Assert.Equal(
            AutomaticCheckMode.WhileAppIsOpen,
            (await app.Get<IAutomaticCheckSettingsRepository>().LoadAsync(TestContext.Current.CancellationToken)).Mode);
    }

    [Fact]
    public async Task Turning_it_on_asks_first_and_stores_nothing_yet()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        var question = page.AskAboutKeepingRunning();

        Assert.Contains("Keep DeskAI running", question.Title, StringComparison.Ordinal);
        Assert.Contains("Windows startup", question.Body, StringComparison.Ordinal);
        Assert.False(page.KeepsRunningWhenClosed);
        Assert.False(app.Presence.IsShowing);
    }

    [Fact]
    public async Task The_question_says_a_check_cannot_move_anything()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        var question = page.AskAboutKeepingRunning();

        Assert.Contains("cannot move", question.LimitLine, StringComparison.Ordinal);
        Assert.Contains("does not tidy while you are away", question.LimitLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_question_carries_the_notification_switch_and_says_what_off_means()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        var question = page.AskAboutKeepingRunning();

        Assert.False(question.NotifyWhenSomethingIsFound);
        Assert.Contains("next time you open DeskAI", question.NotifyCaption, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saying_no_changes_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        page.AskAboutKeepingRunning();
        // The person pressed "No thanks", so nothing is confirmed.

        Assert.False(page.KeepsRunningWhenClosed);
        Assert.False(app.Presence.IsShowing);
    }

    [Fact]
    public async Task Saying_yes_stores_it_and_shows_the_icon_while_the_window_is_still_open()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.True(page.KeepsRunningWhenClosed);
        Assert.True(app.Presence.IsShowing);
    }

    [Fact]
    public async Task Saying_yes_with_notifications_ticked_turns_them_on_too()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: true);

        Assert.True(page.NotifyWhenSomethingIsFound);
        var stored = await app.Get<IAutomaticCheckSettingsRepository>().LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(stored.NotifyWhenSomethingIsFound);
    }

    [Fact]
    public async Task Saying_yes_without_ticking_notifications_leaves_them_off()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.False(page.NotifyWhenSomethingIsFound);
    }

    [Fact]
    public async Task The_choice_is_still_there_after_closing_DeskAI_and_opening_it_again()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        await using var reopened = await app.ReopenAsync();
        var again = reopened.Get<AutomationViewModel>();
        await again.InitializeAsync();

        Assert.True(again.KeepsRunningWhenClosed);
        Assert.True(reopened.Presence.IsShowing);
    }

    [Fact]
    public async Task Turning_it_off_takes_the_icon_away_at_once()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        await page.StopKeepingRunningAsync();

        Assert.False(page.KeepsRunningWhenClosed);
        Assert.False(app.Presence.IsShowing);
    }

    [Fact]
    public async Task A_fresh_app_that_has_never_turned_the_mode_on_would_let_the_window_really_close()
    {
        await using var app = await TestApp.StartAsync();

        Assert.False(app.Get<BackgroundPresenceController>().KeepsRunningWhenClosed);
    }

    [Fact]
    public async Task Turning_it_on_makes_the_controller_say_closing_should_hide_the_window()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.True(app.Get<BackgroundPresenceController>().KeepsRunningWhenClosed);
    }

    [Fact]
    public async Task Turning_it_off_makes_the_controller_say_closing_should_really_close_at_once()
    {
        // This is the regression the whole fix is about: MainWindow used to keep its own copy
        // of this answer, refreshed only on navigation. Someone who switched this off and then
        // closed the window immediately — without visiting another page first — would find the
        // stale "on" copy still in place. Nothing here navigates or reloads the page; the
        // controller has to be correct the instant the switch changes.
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);
        Assert.True(app.Get<BackgroundPresenceController>().KeepsRunningWhenClosed);

        await page.StopKeepingRunningAsync();

        Assert.False(app.Get<BackgroundPresenceController>().KeepsRunningWhenClosed);
    }

    [Fact]
    public async Task While_it_is_on_the_page_does_not_claim_checking_stops_when_you_close_it()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.DoesNotContain("only while DeskAI is open", page.MoreDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("While DeskAI is open", page.AutomaticCheckSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_never_stops_promising_that_nothing_moves_by_itself()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.Contains("never moves anything by itself", page.AutomaticCheckSummary, StringComparison.Ordinal);
        Assert.Contains("does not move anything", page.MoreDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_promises_no_Windows_startup_whether_it_is_on_or_off()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        Assert.Contains("does not add itself to Windows startup", page.MoreDetails, StringComparison.Ordinal);

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.Contains("does not add itself to Windows startup", page.MoreDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_icon_says_how_often_it_is_looking()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        page.SelectedFrequency = page.FrequencyOptions.Single(
            option => option.Value == AutomaticCheckFrequency.EveryHour);

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.Equal("DeskAI — looking every hour", app.Presence.Tooltips[^1]);
    }

    [Fact]
    public async Task Pausing_changes_what_the_icon_says()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        page.IsPaused = true;

        Assert.Equal("DeskAI — checks paused", app.Presence.Tooltips[^1]);
    }

    [Fact]
    public async Task The_icon_never_says_a_file_name()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Study", "invoice-april.txt");
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);
        page.SelectedFrequency = page.FrequencyOptions.Single(
            option => option.Value == AutomaticCheckFrequency.EveryFifteenMinutes);

        Assert.All(app.Presence.Tooltips, tooltip =>
        {
            Assert.DoesNotContain("invoice", tooltip, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Study", tooltip, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(app.Sandbox, tooltip, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Pausing_from_the_icon_pauses_on_the_page_too()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        await app.Get<BackgroundPresenceController>().TogglePauseAsync();

        Assert.True(page.IsPaused);
        Assert.Equal("DeskAI — checks paused", app.Presence.Tooltips[^1]);
        Assert.True(app.Presence.PausedStates[^1]);
    }

    [Fact]
    public async Task Pausing_from_the_icon_is_still_paused_after_reopening()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        await app.Get<BackgroundPresenceController>().TogglePauseAsync();

        await using var reopened = await app.ReopenAsync();
        var again = reopened.Get<AutomationViewModel>();
        await again.InitializeAsync();

        Assert.True(again.IsPaused);
    }

    [Fact]
    public async Task Resuming_from_the_icon_resumes()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);
        var controller = app.Get<BackgroundPresenceController>();
        await controller.TogglePauseAsync();

        await controller.TogglePauseAsync();

        Assert.False(page.IsPaused);
        Assert.False(app.Presence.PausedStates[^1]);
    }

    [Fact]
    public async Task The_menu_item_is_wired_to_the_same_thing_the_page_uses()
    {
        // Proves the event actually reaches the controller. The tests above call the method
        // directly so they can assert rather than race an async void handler; without this
        // one, a disconnected menu item would pass all of them.
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        app.Presence.RaisePauseToggle();

        // The handler is fire-and-forget by necessity — an event handler cannot be awaited.
        await WaitUntil(() => page.IsPaused);
        Assert.True(page.IsPaused);
    }

    /// <summary>Waits briefly for something a fire-and-forget handler will do.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Only_one_thing_listens_to_the_icon_however_many_times_the_page_is_opened()
    {
        // A transient view model subscribing to a singleton's event would toggle pause once
        // per page visit. Opening the page three times must still mean one toggle.
        await using var app = await TestApp.StartAsync();
        var first = app.Get<AutomationViewModel>();
        await first.InitializeAsync();
        await first.KeepRunningAsync(notifyWhenSomethingIsFound: false);
        foreach (var _ in Enumerable.Range(0, 3))
        {
            await app.Get<AutomationViewModel>().InitializeAsync();
        }

        await app.Get<BackgroundPresenceController>().TogglePauseAsync();

        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        Assert.True(page.IsPaused);
    }

    [Fact]
    public async Task A_page_opened_before_the_mode_was_turned_on_does_not_hide_the_icon_when_pausing_from_the_tray()
    {
        // A view model already on screen before background checking was turned on — here,
        // from outside this page entirely — still holds the old mode. If it re-synced only
        // the pause flag from the icon's event, its own stale mode would make it hide the
        // icon on the very next pause or resume, even though the store says checking is
        // still on and nothing else is left to put the icon back.
        await using var app = await TestApp.StartAsync();
        var stale = app.Get<AutomationViewModel>();
        await stale.InitializeAsync();

        var repository = app.Get<IAutomaticCheckSettingsRepository>();
        var current = await repository.LoadAsync(TestContext.Current.CancellationToken);
        var turnedOn = current with { Mode = AutomaticCheckMode.InBackground };
        await repository.SaveAsync(turnedOn, TestContext.Current.CancellationToken);
        var controller = app.Get<BackgroundPresenceController>();
        controller.Refresh(turnedOn);
        Assert.True(app.Presence.IsShowing);

        await controller.TogglePauseAsync();

        Assert.True(app.Presence.IsShowing);
        Assert.Equal("DeskAI — checks paused", app.Presence.Tooltips[^1]);
    }
}
