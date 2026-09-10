using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>The new Organize page, used the way a person uses it. Nothing here moves a file.</summary>
public sealed class TidyPageTests
{
    [Fact]
    public async Task With_no_folders_the_page_asks_you_to_pick_one()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<TidyViewModel>();

        await page.InitializeAsync();

        Assert.False(page.HasFolders);
        Assert.True(page.HasNoFolders);
        Assert.False(page.HasSuggestions);
        Assert.False(page.NeedsPermission);
    }

    [Fact]
    public async Task Picking_a_new_folder_connects_it_and_asks_for_permission_before_suggesting_anything()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();

        await page.ConnectAndSelectAsync(folder);

        Assert.Equal("Downloads", page.SelectedFolder!.Name);
        Assert.True(page.NeedsPermission);
        Assert.Equal("Allow DeskAI to tidy Downloads?", page.PermissionTitle);
        Assert.False(page.HasSuggestions);
    }

    [Fact]
    public async Task Allowing_tidying_shows_suggestions_grouped_by_folder_with_reasons()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "notes.pdf", "holiday.jpg");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);

        await page.AllowTidyAsync();

        Assert.False(page.NeedsPermission);
        Assert.True(page.HasSuggestions);
        var documents = page.Groups.Single(group => group.Folder == "Documents");
        Assert.Equal(2, documents.Items.Count);
        Assert.All(documents.Items, item => Assert.Equal("PDF file", item.Reason));
        Assert.Equal(3, page.IncludedCount);
        Assert.Equal("Tidy 3 files", page.TidyButtonText);
        Assert.Equal("3 loose files could be tidied", page.SummaryTitle);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task The_Tidy_button_is_on_only_while_something_is_ticked_and_says_it_can_be_undone()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();

        Assert.True(page.CanPressTidy);
        Assert.Equal("Nothing moves until you press it. You can undo it.", page.TidyNote);

        page.Groups.Single().IsIncluded = false;

        Assert.False(page.CanPressTidy);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task Unticking_a_group_or_a_file_changes_the_count()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "notes.pdf", "holiday.jpg");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();

        page.Groups.Single(group => group.Folder == "Documents").IsIncluded = false;
        Assert.Equal(1, page.IncludedCount);

        var documents = page.Groups.Single(group => group.Folder == "Documents");
        documents.Items[0].IsIncluded = true;
        Assert.Equal(2, page.IncludedCount);
        Assert.Null(documents.IsIncluded);
    }

    [Fact]
    public async Task Choosing_keep_both_shows_the_new_name()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "report.pdf", @"Documents\report.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();
        var item = page.Groups.Single().Items.Single();
        Assert.True(item.HasSameName);
        Assert.False(item.IsIncluded);

        item.KeepBoth = true;
        await page.WhenIdleAsync();

        var updated = page.Groups.Single().Items.Single();
        Assert.True(updated.KeepBoth);
        Assert.True(updated.IsIncluded);
        Assert.Contains("report (2).pdf", updated.Destination, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Left_alone_files_are_listed_with_reasons()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        app.MakeFile("Downloads", "movie.mp4.crdownload");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();

        Assert.Equal(2, page.LeftAlone.Count);
        Assert.All(page.LeftAlone, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
        Assert.Equal("Left alone (2 files)", page.LeftAloneTitle);
    }

    [Fact]
    public async Task Stopping_tidying_asks_for_permission_again_and_keeps_the_folder_connected()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();

        await page.StopTidyingCommand.ExecuteAsync(null);

        Assert.True(page.NeedsPermission);
        Assert.False(page.HasSuggestions);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        Assert.Single(search.Folders);
    }

    [Fact]
    public async Task Disconnecting_in_Search_takes_the_tidy_permission_with_it()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.DisconnectFolderCommand.ExecuteAsync(Assert.Single(search.Folders).Id);

        var reopened = app.Get<TidyViewModel>();
        await reopened.InitializeAsync();
        await reopened.ConnectAndSelectAsync(folder);

        Assert.True(reopened.NeedsPermission);
    }

    [Fact]
    public async Task A_protected_folder_cannot_be_picked()
    {
        await using var app = await TestApp.StartAsync();
        var protectedFolder = app.Directory.CreateDummyDirectory("protected");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();

        await page.ConnectAndSelectAsync(protectedFolder);

        Assert.Null(page.SelectedFolder);
        Assert.False(string.IsNullOrWhiteSpace(page.Message));
    }

    [Fact]
    public async Task A_folder_already_connected_in_Search_can_be_picked_from_the_list()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var page = app.Get<TidyViewModel>();

        await page.InitializeAsync();
        page.SelectedFolder = Assert.Single(page.Folders);
        await page.WhenIdleAsync();

        Assert.True(page.NeedsPermission);
    }
}
