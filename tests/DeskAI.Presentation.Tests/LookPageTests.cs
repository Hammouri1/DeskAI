using DeskAI.App.ViewModels;
using DeskAI.Core.Appearance;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// DeskAI's look on My workspace, used the way a person uses it: see which look is chosen,
/// choose another, choose light or dark, and find the choice still there after reopening. The
/// window painter is a recording one; nothing here can reach Windows or a file.
/// </summary>
public sealed class LookPageTests
{
    [Fact]
    public async Task The_page_offers_six_looks_with_Slate_chosen_and_following_Windows()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();

        await page.InitializeAsync();

        Assert.Equal(["Slate", "Graphite", "Sand", "Ocean", "Lavender", "Rose"], page.Looks.Select(look => look.Name));
        Assert.Equal("Slate", Assert.Single(page.Looks, look => look.IsChosen).Name);
        Assert.Equal("Chosen", page.Looks[0].ChosenText);
        Assert.Equal(string.Empty, page.Looks[1].ChosenText);
        Assert.Equal(0, page.ThemeModeIndex);
        Assert.Equal(["Follow Windows", "Light", "Dark"], WorkspaceViewModel.ThemeModeNames);
        Assert.False(page.HasLookMessage);
        Assert.Empty(app.Painter.Applied);
    }

    [Fact]
    public async Task Choosing_a_look_repaints_the_window_at_once_marks_it_chosen_and_is_remembered_after_reopening()
    {
        await using var first = await TestApp.StartAsync();
        var page = first.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.ChooseLookCommand.ExecuteAsync("ocean");

        Assert.Equal("Ocean", Assert.Single(page.Looks, look => look.IsChosen).Name);
        Assert.Equal("Ocean", page.ChosenLookName);
        Assert.Equal(new AppearanceSettings(ThemeMode.FollowWindows, "ocean"), Assert.Single(first.Painter.Applied));

        await using var app = await first.ReopenAsync();
        var later = app.Get<WorkspaceViewModel>();
        await later.InitializeAsync();

        Assert.Equal("Ocean", Assert.Single(later.Looks, look => look.IsChosen).Name);
        Assert.Equal(new AppearanceSettings(ThemeMode.FollowWindows, "ocean"),
            await app.Get<DeskAI.Core.Abstractions.IAppearanceSettingsRepository>().LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Choosing_light_or_dark_keeps_the_look_and_choosing_the_same_again_does_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.ChooseLookCommand.ExecuteAsync("sand");

        await page.ChooseThemeModeAsync(2);
        await page.ChooseThemeModeAsync(2);

        Assert.Equal(2, page.ThemeModeIndex);
        Assert.Equal("Sand", page.ChosenLookName);
        Assert.Equal(
            [new AppearanceSettings(ThemeMode.FollowWindows, "sand"), new AppearanceSettings(ThemeMode.Dark, "sand")],
            app.Painter.Applied);

        await page.ChooseThemeModeAsync(1);
        Assert.Equal(new AppearanceSettings(ThemeMode.Light, "sand"), app.Painter.Applied[^1]);
        await page.ChooseThemeModeAsync(7);
        Assert.Equal(1, page.ThemeModeIndex);
    }

    [Fact]
    public async Task A_look_that_no_longer_exists_shows_as_Slate_and_a_made_up_choice_changes_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<DeskAI.Core.Abstractions.IAppearanceSettingsRepository>()
            .SaveAsync(new AppearanceSettings(ThemeMode.Light, "ocean"), TestContext.Current.CancellationToken);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        Assert.Equal("Ocean", page.ChosenLookName);

        await page.ChooseLookCommand.ExecuteAsync("neon");

        Assert.Equal("Ocean", page.ChosenLookName);
        Assert.Empty(app.Painter.Applied);
    }
}
