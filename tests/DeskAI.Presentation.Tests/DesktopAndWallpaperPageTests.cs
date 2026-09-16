using DeskAI.App.ViewModels;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Desktop and wallpaper on My workspace, used the way a person uses them. The wallpaper
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

    [Fact]
    public async Task Tidy_my_Desktop_connects_the_Desktop_for_names_only_and_hands_it_to_Organize_which_asks_permission()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Desktop", "report.pdf", "holiday.jpg", "Notes.lnk");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        Assert.True(page.HasDesktop);
        Assert.Equal("Your Desktop is not connected yet.", page.DesktopStatus);
        Assert.False(page.IsDesktopConnected);

        var id = await page.ConnectDesktopAsync();

        Assert.NotNull(id);
        Assert.True(page.IsDesktopConnected);
        Assert.Equal("Your Desktop is connected. Tidy it in Organize.", page.DesktopStatus);
        var connected = Assert.Single(await app.Get<DeskAI.Core.Search.ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(app.DesktopPath, connected.Path);
        Assert.False(connected.CanReadContent);
        Assert.False(connected.CanTidy);

        // What Organize shows next: the Desktop, asking for permission before suggesting anything.
        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        Assert.Equal("Desktop", organize.SelectedFolder!.Name);
        Assert.True(organize.NeedsPermission);
        Assert.False(organize.HasSuggestions);
        Assert.Equal(3, Directory.EnumerateFiles(app.DesktopPath).Count());
    }

    [Fact]
    public async Task Tidying_the_Desktop_leaves_shortcuts_alone_and_moves_nothing_until_Tidy()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Desktop", "report.pdf", "Notes.lnk", "site.url");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.ConnectDesktopAsync();
        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();

        await organize.AllowTidyAsync();

        Assert.Equal(["report.pdf"], organize.Groups.SelectMany(group => group.Items).Select(item => item.FileName));
        var leftAlone = organize.LeftAlone.ToDictionary(item => item.FileName, item => item.Reason);
        Assert.Contains("Notes.lnk", leftAlone.Keys);
        Assert.Contains("site.url", leftAlone.Keys);
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "report.pdf")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Notes.lnk")));
    }

    [Fact]
    public async Task Pressing_Tidy_my_Desktop_again_reuses_the_connected_Desktop()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Desktop", "report.pdf");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        var first = await page.ConnectDesktopAsync();

        var second = await page.ConnectDesktopAsync();

        Assert.Equal(first, second);
        Assert.Single(await app.Get<DeskAI.Core.Search.ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_a_Desktop_folder_the_card_says_so_and_the_button_is_off()
    {
        await using var app = await TestApp.StartAsync();
        app.KnownFolders.Desktop = null;
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        Assert.False(page.HasDesktop);
        Assert.Equal("DeskAI could not find your Desktop folder.", page.DesktopStatus);
        Assert.Null(await page.ConnectDesktopAsync());
        Assert.Empty(await app.Get<DeskAI.Core.Search.ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_test_Desktop_is_never_the_real_one()
    {
        await using var app = await TestApp.StartAsync();

        Assert.StartsWith(app.Directory.Path, app.KnownFolders.Desktop!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            app.KnownFolders.Desktop,
            StringComparer.OrdinalIgnoreCase);
    }
}
