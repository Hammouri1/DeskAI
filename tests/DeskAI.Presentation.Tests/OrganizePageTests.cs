using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Organize page: the practice run on generated sample files, undo, and the read-only
/// preview of a folder.
/// </summary>
public sealed class OrganizePageTests
{
    [Fact]
    public async Task The_practice_preview_lists_suggested_moves_before_anything_runs()
    {
        await using var app = await TestApp.StartAsync();
        var organize = app.Get<OrganizeViewModel>();

        await organize.InitializeAsync();

        Assert.NotEmpty(organize.Operations);
        Assert.True(organize.SelectedOperationCount > 0);
        Assert.True(organize.ExecuteDemoCommand.CanExecute(null));
        Assert.False(organize.UndoDemoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Running_the_practice_moves_sample_files_and_undo_puts_them_back()
    {
        await using var app = await TestApp.StartAsync();
        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();
        var selected = organize.SelectedOperationCount;

        await organize.ExecuteDemoCommand.ExecuteAsync(null);

        Assert.StartsWith("Done", organize.ResultMessage, StringComparison.Ordinal);
        Assert.Contains($"{selected} sample file(s)", organize.ResultMessage, StringComparison.Ordinal);
        Assert.StartsWith(app.Directory.Path, organize.DemoRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Sample organization", organize.ActivityTitle);
        Assert.True(organize.UndoDemoCommand.CanExecute(null));

        await organize.UndoDemoCommand.ExecuteAsync(null);

        Assert.StartsWith("Undo complete", organize.ResultMessage, StringComparison.Ordinal);
        // The latest activity is the undo itself, and it must not read as partial: the
        // sample workspace already had a Documents folder, which undo correctly leaves.
        Assert.Equal("Undo", organize.ActivityTitle);
        Assert.Equal($"Completed · {selected} file(s)", organize.ActivityMessage);
    }

    [Fact]
    public async Task Unticking_everything_disables_the_run_button()
    {
        await using var app = await TestApp.StartAsync();
        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();

        organize.ClearSelectionCommand.Execute(null);

        Assert.Equal(0, organize.SelectedOperationCount);
        Assert.False(organize.ExecuteDemoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Previewing_a_folder_lists_its_files_and_changes_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "essay.docx");
        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();

        await organize.PreviewFolderAsync(folder);

        Assert.Equal("Coursework", organize.FolderPreviewTitle);
        Assert.Equal(2, organize.FolderFiles.Count);
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
    }

    [Fact]
    public async Task Disconnecting_from_Organize_forgets_the_folder_everywhere()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "holiday.jpg");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);

        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();
        await organize.RevokeFolderCommand.ExecuteAsync(null);

        var reopenedSearch = app.Get<SearchViewModel>();
        await reopenedSearch.InitializeAsync();
        Assert.Empty(reopenedSearch.Folders);
        reopenedSearch.Phrase = "photos";
        await reopenedSearch.SearchCommand.ExecuteAsync(null);
        Assert.Empty(reopenedSearch.Results);

        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        Assert.False(home.HasStorage);
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));
    }

    [Fact]
    public async Task Previewing_the_same_folder_twice_does_not_connect_it_twice()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt");
        var organize = app.Get<OrganizeViewModel>();
        await organize.InitializeAsync();

        await organize.PreviewFolderAsync(folder);
        await organize.PreviewFolderAsync(folder);

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        Assert.Single(search.Folders);
    }
}
