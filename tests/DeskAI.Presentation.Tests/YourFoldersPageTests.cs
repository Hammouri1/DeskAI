using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The "Your folders" card on Home and My workspace, and the owner's rule of 2026-09-16 (ADR
/// 0032) that DeskAI connects only a person's Desktop, Downloads, Documents, and Pictures. All
/// four "Windows folders" are generated inside the test's own temp folder; nothing here can
/// reach a real one.
/// </summary>
public sealed class YourFoldersPageTests
{
    [Fact]
    public async Task Home_lists_the_four_folders_not_connected_with_a_Connect_button_each()
    {
        await using var app = await TestApp.StartAsync();
        var home = app.Get<DashboardViewModel>();

        await home.InitializeAsync();

        Assert.Equal(["Desktop", "Downloads", "Documents", "Pictures"], home.Folders.Rows.Select(row => row.Name));
        Assert.All(home.Folders.Rows, row =>
        {
            Assert.False(row.IsConnected);
            Assert.Equal("Not connected yet.", row.Status);
            Assert.Equal("Connect", row.ButtonText);
            Assert.Equal($"Connect {row.Name}", row.ButtonName);
        });
        Assert.True(home.Folders.HasRows);
        Assert.False(home.Folders.HasMessage);
    }

    [Fact]
    public async Task Connecting_Downloads_from_Home_remembers_names_only_and_hands_it_to_Organize_which_asks_permission()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Downloads", "setup.exe", "report.pdf", "holiday.jpg");
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();

        var id = await home.Folders.ConnectAsync(PersonalFolderKind.Downloads);

        Assert.NotNull(id);
        var row = home.Folders.Find(PersonalFolderKind.Downloads)!;
        Assert.True(row.IsConnected);
        Assert.Equal("Connected. Tidy it in Organize.", row.Status);
        Assert.Equal("Tidy", row.ButtonText);
        Assert.False(row.CanTidy);
        var connected = Assert.Single(await app.Get<ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Downloads", connected.Name);
        Assert.False(connected.CanReadContent);
        Assert.False(connected.CanTidy);

        // What Organize shows next: Downloads, asking for permission before suggesting anything.
        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        Assert.Equal("Downloads", organize.SelectedFolder!.Name);
        Assert.True(organize.NeedsPermission);
        Assert.False(organize.HasSuggestions);
        Assert.Equal(3, Directory.EnumerateFiles(Path.Combine(app.Sandbox, "Downloads")).Count());
    }

    [Fact]
    public async Task My_workspace_shows_the_same_card_and_a_connected_folder_appears_in_its_template_list()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Pictures", "holiday.jpg");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        Assert.Equal(4, page.Folders.Rows.Count);
        Assert.True(page.HasNoTemplateFolders);

        var id = await page.ConnectFolderAsync(PersonalFolderKind.Pictures);

        Assert.NotNull(id);
        Assert.True(page.Folders.Find(PersonalFolderKind.Pictures)!.IsConnected);
        Assert.Equal("Pictures", Assert.Single(page.TemplateFolders).Name);
    }

    [Fact]
    public async Task A_folder_allowed_to_tidy_says_so_on_its_row()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Desktop", "report.pdf");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.ConnectFolderAsync(PersonalFolderKind.Desktop);
        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        await organize.AllowTidyAsync();

        await page.Folders.ReloadAsync();

        var row = page.Folders.Find(PersonalFolderKind.Desktop)!;
        Assert.True(row.CanTidy);
        Assert.Equal("Connected and allowed to tidy.", row.Status);
    }

    /// <summary>
    /// Found by the owner 2026-09-16: "Tidy my Desktop" said the Desktop was protected. DeskAI
    /// was running from a folder on the Desktop, and its own program folder is protected, so
    /// the Desktop "overlapped" a protected place. The Desktop connects; the program folder is
    /// skipped and its files are never remembered.
    /// </summary>
    [Fact]
    public async Task The_Desktop_connects_when_DeskAI_itself_lives_on_it_and_its_own_folder_is_skipped()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Desktop", "report.pdf");
        app.MakeFile(@"Desktop\DeskAI\app", "DeskAI.App.exe");
        app.MakeFile(@"Desktop\DeskAI\app", "deskai.db");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        var id = await page.ConnectFolderAsync(PersonalFolderKind.Desktop);

        Assert.NotNull(id);
        Assert.True(page.Folders.Find(PersonalFolderKind.Desktop)!.IsConnected);
        Assert.False(page.Folders.HasMessage);
        var remembered = await app.Get<IMetadataIndexService>().GetStatisticsAsync(id.Value, TestContext.Current.CancellationToken);
        Assert.Equal(1, remembered.FileCount);
    }

    [Fact]
    public async Task A_folder_inside_DeskAI_s_own_program_folder_still_cannot_be_connected()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile(@"Desktop\DeskAI\app\logs", "today.log");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        await search.ConnectFolderAsync(Path.Combine(app.ProgramFolderPath, "logs"));

        Assert.Equal("This location is protected and cannot be connected.", search.FolderMessage);
        Assert.Empty(search.Folders);
    }

    [Fact]
    public async Task Tidying_the_Desktop_leaves_shortcuts_alone_and_moves_nothing_until_Tidy()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Desktop", "report.pdf", "Notes.lnk", "site.url");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.ConnectFolderAsync(PersonalFolderKind.Desktop);
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
    public async Task Pressing_the_button_again_reuses_the_connected_folder()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Desktop", "report.pdf");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        var first = await page.ConnectFolderAsync(PersonalFolderKind.Desktop);

        var second = await page.ConnectFolderAsync(PersonalFolderKind.Desktop);

        Assert.Equal(first, second);
        Assert.Single(await app.Get<ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_folder_Windows_does_not_have_is_left_off_the_card()
    {
        await using var app = await TestApp.StartAsync();
        app.KnownFolders.Downloads = null;
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();

        Assert.Equal(["Desktop", "Documents", "Pictures"], home.Folders.Rows.Select(row => row.Name));
        Assert.Null(await home.Folders.ConnectAsync(PersonalFolderKind.Downloads));
        Assert.Equal("DeskAI could not find your Downloads folder.", home.Folders.Message);
        Assert.Empty(await app.Get<ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task With_none_of_the_four_the_card_says_so_and_nothing_can_be_connected()
    {
        await using var app = await TestApp.StartAsync();
        app.KnownFolders.Desktop = null;
        app.KnownFolders.Downloads = null;
        app.KnownFolders.Documents = null;
        app.KnownFolders.Pictures = null;
        var elsewhere = app.MakeFolder("Coursework", "notes.txt");
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        await search.ConnectFolderAsync(elsewhere);

        Assert.True(home.Folders.HasNoRows);
        Assert.Equal(PersonalFolderPolicy.NoneKnownReason, home.Folders.NoneKnownText);
        Assert.Equal(PersonalFolderPolicy.NoneKnownReason, search.FolderMessage);
        Assert.Empty(search.Folders);
    }

    /// <summary>The owner's rule: picking a folder outside the four in the Windows dialog is refused in plain words.</summary>
    [Fact]
    public async Task A_picked_folder_outside_the_four_is_refused_on_Search_and_on_Organize()
    {
        await using var app = await TestApp.StartAsync();
        var outside = app.Directory.CreateDummyDirectory("Elsewhere");
        File.WriteAllText(Path.Combine(outside, "notes.txt"), "dummy");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();

        await search.ConnectFolderAsync(outside);
        await organize.ConnectAndSelectAsync(outside);

        Assert.Equal(PersonalFolderPolicy.OutsideReason, search.FolderMessage);
        Assert.Equal(PersonalFolderPolicy.OutsideReason, organize.Message);
        Assert.Empty(search.Folders);
        Assert.Empty(await app.Get<ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_picked_folder_inside_one_of_the_four_is_accepted()
    {
        await using var app = await TestApp.StartAsync();
        var inside = app.MakeFolder(@"Pictures\2026", "holiday.jpg");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        await search.ConnectFolderAsync(inside);

        Assert.Equal("2026", Assert.Single(search.Folders).Name);
    }

    /// <summary>A folder connected before the rule, or moved since, cannot be tidied either.</summary>
    [Fact]
    public async Task A_connected_folder_that_is_no_longer_inside_the_four_cannot_be_tidied()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder(@"Pictures\Old", "holiday.jpg");
        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        await organize.ConnectAndSelectAsync(folder);

        // In the test sandbox "Documents" is the folders root, so it has to go too.
        app.KnownFolders.Pictures = null;
        app.KnownFolders.Documents = null;
        await organize.AllowTidyAsync();

        Assert.True(organize.NeedsPermission);
        Assert.Contains(PersonalFolderPolicy.OutsideReason, organize.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));
    }

    [Fact]
    public async Task The_test_folders_are_never_the_real_ones()
    {
        await using var app = await TestApp.StartAsync();
        string?[] test = [app.KnownFolders.Desktop, app.KnownFolders.Downloads, app.KnownFolders.Documents, app.KnownFolders.Pictures];
        // The temp folder itself sits under the user profile, so only the personal folders
        // Windows names are compared, not the profile as a whole.
        string[] real =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        ];

        Assert.All(test, path =>
        {
            Assert.StartsWith(app.Directory.Path, path!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(real.Where(item => item.Length > 0), item => path!.StartsWith(item, StringComparison.OrdinalIgnoreCase));
        });
    }
}
