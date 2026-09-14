using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// My workspace as a person uses it: add a starter pack after seeing what it adds, look at
/// pinned searches, pin and unpin, and open a pinned search in Search. Every folder is generated.
/// </summary>
public sealed class WorkspacePageTests
{
    [Fact]
    public async Task Open_in_Search_opens_Search_with_that_saved_search_already_run()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "holiday.jpg");
        var id = await SaveSearchAsync(app, folder, "Photos", "photos");

        app.Get<SearchRequest>().Ask(id);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        Assert.Equal("photos", search.Phrase);
        Assert.Equal("holiday.jpg", Assert.Single(search.Results).Name);
        Assert.NotEmpty(search.Chips);
    }

    [Fact]
    public async Task The_request_is_used_once_and_a_later_visit_to_Search_opens_as_usual()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "holiday.jpg");
        var id = await SaveSearchAsync(app, folder, "Photos", "photos");
        app.Get<SearchRequest>().Ask(id);
        await app.Get<SearchViewModel>().InitializeAsync();

        var later = app.Get<SearchViewModel>();
        await later.InitializeAsync();

        Assert.Empty(later.Phrase);
        Assert.Empty(later.Results);
    }

    [Fact]
    public async Task A_saved_search_removed_in_the_meantime_is_said_to_be_gone()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "holiday.jpg");
        var id = await SaveSearchAsync(app, folder, "Photos", "photos");
        var remover = app.Get<SearchViewModel>();
        await remover.InitializeAsync();
        await remover.DeleteSavedSearchCommand.ExecuteAsync(id);

        app.Get<SearchRequest>().Ask(id);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        Assert.Equal("That saved search no longer exists", search.StatusTitle);
        Assert.Empty(search.Results);
    }

    /// <summary>Connects a generated folder and saves a search the way the Search page does.</summary>
    private static async Task<Guid> SaveSearchAsync(TestApp app, string folder, string name, string phrase)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        if (search.Folders.All(item => item.Path != folder))
        {
            await search.ConnectFolderAsync(folder);
        }

        search.Phrase = phrase;
        await search.SaveCurrentSearchAsync(name);
        return search.SavedSearches.Single(item => item.Name == name).Id;
    }
}
