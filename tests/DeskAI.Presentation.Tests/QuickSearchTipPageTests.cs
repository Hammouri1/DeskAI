using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>The one-line tip about the shortcut on Home and Search, and the welcome's new page.</summary>
public sealed class QuickSearchTipPageTests
{
    [Fact]
    public async Task Home_and_Search_show_the_tip_until_it_is_closed_on_either()
    {
        await using var app = await TestApp.StartAsync();
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        Assert.True(home.Tip.IsShown);
        Assert.True(search.Tip.IsShown);
        Assert.Equal("Tip: press Ctrl + Alt + Space anywhere to find a file.", QuickSearchTipViewModel.Text);

        await search.Tip.DismissAsync();
        Assert.False(search.Tip.IsShown);

        var homeAgain = app.Get<DashboardViewModel>();
        await homeAgain.InitializeAsync();
        Assert.False(homeAgain.Tip.IsShown);

        await using var reopened = await app.ReopenAsync();
        var searchAfterReopen = reopened.Get<SearchViewModel>();
        await searchAfterReopen.InitializeAsync();
        Assert.False(searchAfterReopen.Tip.IsShown);
    }

    [Fact]
    public async Task The_tip_is_hidden_while_quick_search_is_off()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSettingsService>().SetOnAsync(false, TestContext.Current.CancellationToken);
        var home = app.Get<DashboardViewModel>();

        await home.InitializeAsync();

        Assert.False(home.Tip.IsShown);
    }

    [Fact]
    public async Task Start_fresh_brings_a_closed_tip_back()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSettingsService>().DismissTipAsync(TestContext.Current.CancellationToken);

        await app.Get<SettingsViewModel>().StartFreshAsync();
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();

        Assert.True(home.Tip.IsShown);
    }

    [Fact]
    public async Task The_welcome_has_a_page_about_quick_search_before_the_last_one()
    {
        await using var app = await TestApp.StartAsync();
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();

        welcome.Next();
        welcome.Next();

        Assert.Equal("Find any file, from anywhere", welcome.Current.Title);
        Assert.Equal("Press Ctrl + Alt + Space in any app. Type what you're looking for, and press Enter to open it.", welcome.Current.Body);
        Assert.True(welcome.Current.ShowsBuddy);
        Assert.Empty(welcome.Current.Promises);
        Assert.False(welcome.IsLastPage);
        Assert.Equal("Page 3 of 4", welcome.PageNumberText);
        Assert.Equal([false, false, true, false], welcome.Dots);
        welcome.Next();
        Assert.Equal("Let's start", welcome.Current.Title);
        Assert.False(welcome.Current.ShowsBuddy);
    }
}
