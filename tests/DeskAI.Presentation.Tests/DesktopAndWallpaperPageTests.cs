using DeskAI.App.ViewModels;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The wallpaper on My workspace, used the way a person uses it. The wallpaper
/// "Windows" is a recording one and the "Desktop" is a generated folder inside the test's own
/// temp folder, so nothing here can reach the real wallpaper or the real Desktop.
/// </summary>
public sealed class DesktopAndWallpaperPageTests
{
    [Fact]
    public async Task Choosing_a_picture_shows_it_and_what_Windows_shows_now_and_changes_nothing_until_the_button()
    {
        await using var app = await TestApp.StartAsync();
        var picture = app.MakeFile("Pictures", "holiday.jpg");
        app.Wallpaper.Current = app.MakeFile("Pictures", "older.png");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        Assert.False(page.CanPutWallpaperBack);

        var preview = await page.PreviewWallpaperAsync(picture);

        Assert.Equal("holiday.jpg", preview!.Name);
        Assert.Equal("older.png", preview.CurrentDescription);
        Assert.Empty(app.Wallpaper.Sets);
        Assert.False(page.HasWallpaperMessage);
    }

    [Fact]
    public async Task Using_a_picture_sets_it_records_the_old_one_and_Put_back_restores_it_even_after_reopening()
    {
        await using var first = await TestApp.StartAsync();
        var picture = first.MakeFile("Pictures", "holiday.jpg");
        var older = first.MakeFile("Pictures", "older.png");
        first.Wallpaper.Current = older;
        var page = first.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.UseWallpaperAsync((await page.PreviewWallpaperAsync(picture))!);

        Assert.Equal("holiday.jpg is now your wallpaper.", page.WallpaperMessage);
        Assert.Equal([picture], first.Wallpaper.Sets);
        Assert.True(page.CanPutWallpaperBack);
        Assert.Equal("Your old wallpaper: older.png.", page.PutBackSummary);
        Assert.False(page.HasPutBackNote);

        await using var app = await first.ReopenAsync();
        app.Wallpaper.Current = picture;
        var later = app.Get<WorkspaceViewModel>();
        await later.InitializeAsync();

        Assert.True(later.CanPutWallpaperBack);
        Assert.Equal("Your old wallpaper: older.png.", later.PutBackSummary);

        await later.PutWallpaperBackCommand.ExecuteAsync(null);

        Assert.Equal("older.png is your wallpaper again.", later.WallpaperMessage);
        Assert.Equal(older, app.Wallpaper.Current);
        Assert.False(later.CanPutWallpaperBack);
    }

    [Fact]
    public async Task A_picture_that_is_not_a_plain_local_picture_is_refused_on_the_card_and_nothing_changes()
    {
        await using var app = await TestApp.StartAsync();
        var notes = app.MakeFile("Pictures", "notes.txt");
        app.Wallpaper.Current = string.Empty;
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        Assert.Null(await page.PreviewWallpaperAsync(notes));
        Assert.Equal("DeskAI can use JPG, PNG, and BMP pictures.", page.WallpaperMessage);
        Assert.Null(await page.PreviewWallpaperAsync(@"\\server\share\pic.jpg"));
        Assert.Equal("The picture must be a file on this computer.", page.WallpaperMessage);
        Assert.Null(await page.PreviewWallpaperAsync(Path.Combine(app.Sandbox, "Pictures", "missing.jpg")));
        Assert.Equal("That picture is no longer there.", page.WallpaperMessage);
        Assert.Null(await page.PreviewWallpaperAsync(null));
        Assert.Equal("Choose a picture first.", page.WallpaperMessage);
        Assert.Empty(app.Wallpaper.Sets);
        Assert.False(page.CanPutWallpaperBack);
    }

    [Fact]
    public async Task When_Windows_refuses_the_card_says_so_and_Put_back_is_not_offered()
    {
        await using var app = await TestApp.StartAsync();
        var picture = app.MakeFile("Pictures", "holiday.jpg");
        app.Wallpaper.Current = string.Empty;
        app.Wallpaper.RefuseWith = "Windows did not let DeskAI change the wallpaper.";
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.UseWallpaperAsync((await page.PreviewWallpaperAsync(picture))!);

        Assert.Equal("Windows did not let DeskAI change the wallpaper.", page.WallpaperMessage);
        Assert.False(page.CanPutWallpaperBack);
    }

    [Fact]
    public async Task A_wallpaper_changed_in_Windows_since_is_said_beside_Put_back()
    {
        await using var app = await TestApp.StartAsync();
        var picture = app.MakeFile("Pictures", "holiday.jpg");
        app.Wallpaper.Current = app.MakeFile("Pictures", "older.png");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.UseWallpaperAsync((await page.PreviewWallpaperAsync(picture))!);

        app.Wallpaper.Current = app.MakeFile("Pictures", "theirs.bmp");
        var later = app.Get<WorkspaceViewModel>();
        await later.InitializeAsync();

        Assert.True(later.HasPutBackNote);
        Assert.Equal("Windows now shows a different wallpaper than the one DeskAI set. Put back restores the old one anyway.", later.PutBackNote);
    }
}
