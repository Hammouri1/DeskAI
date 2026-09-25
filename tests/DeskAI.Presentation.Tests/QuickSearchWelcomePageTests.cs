using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>The welcome's page about quick search: how people learn the shortcut.</summary>
/// <remarks>
/// The one-line tip on Home and Search was removed at the owner's request (2026-09-25): it was not
/// needed, and on Home it covered what was behind it. The welcome is the one place that teaches it.
/// </remarks>
public sealed class QuickSearchWelcomePageTests
{
    [Fact]
    public async Task The_welcome_has_a_page_about_quick_search_before_the_last_one()
    {
        await using var app = await TestApp.StartAsync();
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();

        welcome.Next();
        welcome.Next();

        Assert.Equal("Find any file, from anywhere", welcome.Current.Title);
        Assert.Equal("Press Ctrl + Alt + D in any app. Type what you're looking for, and press Enter to open it.", welcome.Current.Body);
        Assert.True(welcome.Current.ShowsBuddy);
        Assert.Empty(welcome.Current.Promises);
        Assert.False(welcome.IsLastPage);
        Assert.Equal("Page 3 of 4", welcome.PageNumberText);
        Assert.Equal([false, false, true, false], welcome.Dots);
        welcome.Next();
        Assert.Equal("Let's start", welcome.Current.Title);
        Assert.False(welcome.Current.ShowsBuddy);
    }

    [Fact]
    public async Task The_welcome_names_the_chosen_shortcut()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSettingsService>().SetShortcutAsync(QuickSearchShortcut.CtrlShiftSpace, TestContext.Current.CancellationToken);
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();

        welcome.Next();
        welcome.Next();

        Assert.Equal("Press Ctrl + Shift + Space in any app. Type what you're looking for, and press Enter to open it.", welcome.Current.Body);
    }
}
