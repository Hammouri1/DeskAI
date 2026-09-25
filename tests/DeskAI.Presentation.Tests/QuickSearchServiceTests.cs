using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Quick search's two looks over generated folders: names first, then the words inside files,
/// only where reading inside was allowed on Search, never listing a file twice.
/// </summary>
public sealed class QuickSearchServiceTests
{
    [Fact]
    public async Task No_folder_connected_says_so()
    {
        await using var app = await TestApp.StartAsync();

        var result = await app.Get<QuickSearchService>().FindByNameAsync("essay", DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(NameFact.NoFolders, result.Fact);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Names_give_at_most_five_rows_each_saying_where_it_is()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay 1.pdf", "essay 2.pdf", "essay 3.pdf", "essay 4.pdf", "essay 5.pdf", "essay 6.pdf", "essay.exe");

        var result = await app.Get<QuickSearchService>().FindByNameAsync("essay", DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(QuickSearchService.MaxRows, result.Rows.Count);
        Assert.Equal(NameFact.MoreThanShown, result.Fact);
        Assert.All(result.Rows, row => Assert.Equal("School", row.Where));
        Assert.False(result.AnyFolderReadsInside);
    }

    [Fact]
    public async Task A_name_nothing_matches_says_nothing_matched()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");

        var result = await app.Get<QuickSearchService>().FindByNameAsync("zzqx", DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(NameFact.NothingMatched, result.Fact);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task A_familiar_file_can_be_opened_and_a_program_only_shown_in_its_folder()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Stuff", "invoice.pdf.exe", "invoice.pdf");

        var rows = (await app.Get<QuickSearchService>().FindByNameAsync("invoice", DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).Rows;

        Assert.Equal(OpenChoice.ShowInFolderOnly, Assert.Single(rows, row => row.Name == "invoice.pdf.exe").Choice);
        Assert.Equal(OpenChoice.Open, Assert.Single(rows, row => row.Name == "invoice.pdf").Choice);
    }

    [Fact]
    public async Task A_file_one_folder_down_says_so()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Documents");
        app.MakeFile(Path.Combine("Documents", "School"), "essay.docx");
        await ConnectFolderAsync(app, folder);

        var row = Assert.Single((await app.Get<QuickSearchService>().FindByNameAsync("essay", DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).Rows);

        Assert.Equal("Documents › School", row.Where);
        Assert.Equal(Path.Combine("School", "essay.docx"), row.RelativePath);
    }

    [Fact]
    public async Task Words_inside_are_found_only_where_reading_inside_is_allowed_and_never_twice()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Notes");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "shopping.txt"), "buy bananas and bread");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "bananas.txt"), "a list of bananas");
        var search = await ConnectFolderAsync(app, folder);
        var quick = app.Get<QuickSearchService>();
        var token = TestContext.Current.CancellationToken;

        var before = await quick.FindInsideAsync("bananas", DateTimeOffset.UtcNow, new HashSet<string>(), token);
        Assert.False(before.WasSearched);
        Assert.Equal(0, before.FilesRead);

        var rootId = Assert.Single(search.Folders).Id;
        await search.SetContentPermissionAsync(rootId, allow: true);
        var byName = await quick.FindByNameAsync("bananas", DateTimeOffset.UtcNow, token);
        Assert.True(byName.AnyFolderReadsInside);
        var inside = await quick.FindInsideAsync("bananas", DateTimeOffset.UtcNow, byName.Rows.Select(row => row.Key).ToHashSet(), token);

        var row = Assert.Single(inside.Rows);
        Assert.Equal("shopping.txt", row.Name);
        Assert.Equal(rootId, row.RootId);
        Assert.Contains("bananas", row.Snippet, StringComparison.Ordinal);
        Assert.Equal(2, inside.FilesRead);
        Assert.True(inside.WasSearched);

        await search.SetContentPermissionAsync(rootId, allow: false);
        Assert.False((await quick.FindInsideAsync("bananas", DateTimeOffset.UtcNow, new HashSet<string>(), token)).WasSearched);
    }

    [Fact]
    public async Task Two_letters_start_no_inside_look()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Notes");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "a.txt"), "ok ok ok");
        var search = await ConnectFolderAsync(app, folder);
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);

        var inside = await app.Get<QuickSearchService>().FindInsideAsync("ok", DateTimeOffset.UtcNow, new HashSet<string>(), TestContext.Current.CancellationToken);

        Assert.False(inside.WasSearched);
        Assert.Equal(0, inside.FilesRead);
    }

    [Fact]
    public void It_holds_nothing_that_can_open_change_or_send()
    {
        var parameters = typeof(QuickSearchService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name);

        Assert.DoesNotContain(parameters, name =>
            name.Contains("Launcher", StringComparison.Ordinal) || name.Contains("Executor", StringComparison.Ordinal)
            || name.Contains("Journal", StringComparison.Ordinal) || name.Contains("Provider", StringComparison.Ordinal)
            || name.Contains("Writer", StringComparison.Ordinal) || name.Contains("Settings", StringComparison.Ordinal)
            || name.Contains("Extractor", StringComparison.Ordinal) || name.Contains("Ocr", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Documents", "essay.pdf", "Documents")]
    [InlineData("Documents", @"School\essay.pdf", "Documents › School")]
    [InlineData("Downloads", @"a\b\c.txt", "Downloads › a › b")]
    public void Where_names_the_folder_and_the_folders_inside_it(string root, string relative, string expected) =>
        Assert.Equal(expected, QuickSearchService.WhereText(root, relative));

    private static async Task<SearchViewModel> ConnectAsync(TestApp app, string name, params string[] files) =>
        await ConnectFolderAsync(app, app.MakeFolder(name, files));

    private static async Task<SearchViewModel> ConnectFolderAsync(TestApp app, string folder)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        return search;
    }
}
