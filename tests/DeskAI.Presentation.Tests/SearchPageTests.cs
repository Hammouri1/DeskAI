using System.IO.Compression;
using DeskAI.App.ViewModels;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Search page as a person uses it: connect a generated folder, search it, save a
/// search, allow reading inside, and disconnect.
/// </summary>
public sealed class SearchPageTests
{
    [Fact]
    public async Task Connecting_a_folder_lists_it_with_how_many_files_were_remembered()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "essay.docx", "photo.jpg");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        await search.ConnectFolderAsync(folder);

        var row = Assert.Single(search.Folders);
        Assert.Equal("Coursework", row.Name);
        Assert.Equal("3 files remembered", row.Remembered);
        Assert.False(row.CanReadContent);
        Assert.Contains("Remembered 3 file(s)", search.FolderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reopening_search_does_not_say_no_folders_when_one_is_connected()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt");
        var firstVisit = app.Get<SearchViewModel>();
        await firstVisit.InitializeAsync();
        await firstVisit.ConnectFolderAsync(folder);

        var reopened = app.Get<SearchViewModel>();
        await reopened.InitializeAsync();

        Assert.Single(reopened.Folders);
        Assert.DoesNotContain("No folders connected", reopened.FolderMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Searching_finds_matching_files_and_says_which_folders_were_searched()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "essay.docx", "holiday.jpg");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);

        search.Phrase = "photos";
        await search.SearchCommand.ExecuteAsync(null);

        var result = Assert.Single(search.Results);
        Assert.Equal("holiday.jpg", result.Name);
        Assert.NotEmpty(search.Chips);
        Assert.Equal("1 file found", search.StatusTitle);
        Assert.Equal("Searched 1 connected folder.", search.ScopeMessage);
    }

    [Fact]
    public async Task A_word_no_file_name_contains_shows_nothing_rather_than_everything()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "holiday.jpg");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);

        search.Phrase = "zzqx";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Empty(search.Results);
        Assert.Equal("Nothing matched", search.StatusTitle);
        Assert.True(search.ShowsNothingFound);
    }

    [Fact]
    public async Task A_phrase_with_nothing_to_search_for_is_refused_rather_than_listing_everything()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "holiday.jpg");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);

        search.Phrase = "the";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Empty(search.Results);
        Assert.Equal("I did not understand that", search.StatusTitle);
    }

    [Fact]
    public async Task Searching_with_nothing_connected_points_to_where_folders_are_connected()
    {
        await using var app = await TestApp.StartAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        search.Phrase = "photos";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Equal("No folders connected yet", search.StatusTitle);
        // The Connect button is on this page, so the message must not send people elsewhere.
        Assert.DoesNotContain("Organize", search.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_saved_search_can_be_run_again_and_deleted()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "holiday.jpg", "notes.txt");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        search.Phrase = "photos";

        await search.SaveCurrentSearchAsync("My photos");
        var saved = Assert.Single(search.SavedSearches);
        Assert.Equal("My photos", saved.Name);

        search.Phrase = string.Empty;
        await search.RunSavedSearchCommand.ExecuteAsync(saved.Id);
        Assert.Equal("photos", search.Phrase);
        Assert.Single(search.Results);

        await search.DeleteSavedSearchCommand.ExecuteAsync(saved.Id);
        Assert.Empty(search.SavedSearches);
    }

    [Fact]
    public async Task Saved_searches_survive_reopening_the_page()
    {
        await using var app = await TestApp.StartAsync();
        var first = app.Get<SearchViewModel>();
        await first.InitializeAsync();
        first.Phrase = "photos";
        await first.SaveCurrentSearchAsync("My photos");

        var reopened = app.Get<SearchViewModel>();
        await reopened.InitializeAsync();

        Assert.Equal("My photos", Assert.Single(reopened.SavedSearches).Name);
    }

    [Fact]
    public async Task Allowing_reading_inside_finds_words_in_text_files_and_says_how_many_were_opened()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Notes");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "shopping.txt"), "buy bananas and bread");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "todo.txt"), "call the dentist");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;

        await search.SetContentPermissionAsync(rootId, allow: true);
        Assert.True(Assert.Single(search.Folders).CanReadContent);

        search.Phrase = "bananas";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.True(search.ShowsInsideFiles);
        var hit = Assert.Single(search.InsideResults);
        Assert.Equal("shopping.txt", hit.Name);
        Assert.Contains("bananas", hit.Snippet, StringComparison.Ordinal);
        Assert.Contains("2 files", search.InsideMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_can_find_words_inside_a_generated_Word_file_after_a_separate_yes()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Study");
        using (var file = File.Create(Path.Combine(folder, "lesson.docx")))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
        {
            writer.Write("<w:document xmlns:w=\"urn:w\"><w:t>galaxy facts</w:t></w:document>");
        }

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;
        Assert.False(search.Folders[0].CanReadDocuments);

        search.Phrase = "Word document containing galaxy";
        await search.SearchCommand.ExecuteAsync(null);
        Assert.Empty(search.InsideResults);

        await search.SetContentPermissionAsync(rootId, allow: true);
        Assert.True(search.Folders[0].CanReadDocuments);
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Equal("lesson.docx", Assert.Single(search.InsideResults).Name);
        Assert.Equal("1 file found", search.StatusTitle);
        Assert.False(search.ShowsNothingFound);
        Assert.Contains("1 file", search.InsideMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_text_needs_a_separate_yes_and_stops_after_revocation()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Study");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("generated nebula lesson", 12, new PdfPoint(25, 700), font);
        File.WriteAllBytes(Path.Combine(folder, "lesson.pdf"), builder.Build());
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;
        search.Phrase = "nebula";

        await search.SearchCommand.ExecuteAsync(null);
        Assert.Empty(search.InsideResults);
        await search.SetContentPermissionAsync(rootId, allow: true);
        await search.SearchCommand.ExecuteAsync(null);
        Assert.Empty(search.InsideResults);
        Assert.Contains("Refresh", search.InsideMessage, StringComparison.Ordinal);
        Assert.Contains("PDF reading", search.InsideMessage, StringComparison.Ordinal);

        await search.SetPdfPermissionAsync(rootId, allow: true);
        Assert.True(Assert.Single(search.Folders).CanReadPdf);
        await search.SearchCommand.ExecuteAsync(null);
        Assert.Equal("lesson.pdf", Assert.Single(search.InsideResults).Name);
        Assert.Contains("nebula", search.InsideResults[0].Snippet, StringComparison.OrdinalIgnoreCase);

        await search.SetPdfPermissionAsync(rootId, allow: false);
        await search.SearchCommand.ExecuteAsync(null);
        Assert.Empty(search.InsideResults);
        Assert.True(Assert.Single(search.Folders).CanReadDocuments);
    }

    [Fact]
    public async Task A_new_Pdf_in_a_subfolder_is_found_after_refresh_and_Pdf_permission()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Presentations");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;

        var subfolder = Directory.CreateDirectory(Path.Combine(folder, "Seminar"));
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("generated eliana presentation", 12, new PdfPoint(25, 700), font);
        File.WriteAllBytes(Path.Combine(subfolder.FullName, "handout.pdf"), builder.Build());
        search.Phrase = "pdf eliana";
        search.SelectedFolder = Assert.Single(search.SearchFolders, choice => choice.Name == "Presentations");

        await search.SearchCommand.ExecuteAsync(null);
        Assert.Empty(search.InsideResults);
        await search.RefreshFolderCommand.ExecuteAsync(rootId);
        await search.SetContentPermissionAsync(rootId, allow: true);
        await search.SearchCommand.ExecuteAsync(null);
        Assert.Empty(search.InsideResults);

        await search.SetPdfPermissionAsync(rootId, allow: true);
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Equal("handout.pdf", Assert.Single(search.InsideResults).Name);
        Assert.Contains("eliana", search.InsideResults[0].Snippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_explains_which_Pdfs_matched_did_not_match_or_could_not_be_read()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Presentations");
        var subfolder = Directory.CreateDirectory(Path.Combine(folder, "Seminar"));
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("generated nebula topic", 12, new PdfPoint(25, 700), font);
        File.WriteAllBytes(Path.Combine(folder, "match.pdf"), builder.Build());
        var other = new PdfDocumentBuilder();
        var otherFont = other.AddStandard14Font(Standard14Font.Helvetica);
        other.AddPage(PageSize.A4).AddText("generated unrelated topic", 12,
            new PdfPoint(25, 700), otherFont);
        File.WriteAllBytes(Path.Combine(subfolder.FullName, "other.pdf"), other.Build());
        File.WriteAllText(Path.Combine(subfolder.FullName, "broken.pdf"), "%PDF-1.4 generated broken file");

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;
        await search.SetContentPermissionAsync(rootId, allow: true);
        await search.SetPdfPermissionAsync(rootId, allow: true);
        search.Phrase = "pdf nebula";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Contains("See Files checked", search.InsideMessage, StringComparison.Ordinal);
        Assert.Equal("Matched the words inside.",
            Assert.Single(search.CheckedFiles, file => file.Name == "match.pdf").Result);
        Assert.Equal("Read, but the words did not match.",
            Assert.Single(search.CheckedFiles, file => file.Name == "other.pdf").Result);
        Assert.StartsWith("Could not read:",
            Assert.Single(search.CheckedFiles, file => file.Name == "broken.pdf").Result,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_explains_when_a_Pdf_match_may_be_beyond_the_reading_limit()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Presentations");
        var nested = Directory.CreateDirectory(Path.Combine(folder, "Slides"));
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var page = 1; page <= 21; page++)
        {
            builder.AddPage(PageSize.A4).AddText(
                page == 21 ? "generated hammouri topic" : "generated other topic",
                12, new PdfPoint(25, 700), font);
        }

        File.WriteAllBytes(Path.Combine(nested.FullName, "long presentation.pdf"), builder.Build());
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;
        await search.SetContentPermissionAsync(rootId, allow: true);
        await search.SetPdfPermissionAsync(rootId, allow: true);
        search.Phrase = "pdf hammouri";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Empty(search.InsideResults);
        var checkedFile = Assert.Single(search.CheckedFiles);
        Assert.Equal("long presentation.pdf", checkedFile.Name);
        Assert.Contains("Slides", checkedFile.Location, StringComparison.Ordinal);
        Assert.Contains("first 20 pages or 64 KB of text", checkedFile.Result, StringComparison.Ordinal);
        Assert.Contains("may be later", checkedFile.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Image_only_pdf_has_no_text_result_and_search_reports_the_skip()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Study");
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        File.WriteAllBytes(Path.Combine(folder, "blank.pdf"), builder.Build());
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;
        await search.SetContentPermissionAsync(rootId, allow: true);
        await search.SetPdfPermissionAsync(rootId, allow: true);

        search.Phrase = "nebula";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Empty(search.InsideResults);
        Assert.Contains("could not be read", search.InsideMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Choosing_one_folder_keeps_other_connected_folders_out_of_the_search()
    {
        await using var app = await TestApp.StartAsync();
        var first = app.MakeFolder("First");
        var second = app.MakeFolder("Second");
        app.Directory.CreateDummyFile(Path.Combine("folders", "First", "alpha.txt"), "orbit");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Second", "beta.txt"), "orbit");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(first);
        await search.ConnectFolderAsync(second);
        foreach (var folder in search.Folders.ToArray())
        {
            await search.SetContentPermissionAsync(folder.Id, allow: true);
        }

        search.SelectedFolder = Assert.Single(search.SearchFolders, choice => choice.Name == "Second");
        search.Phrase = "orbit";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Equal("beta.txt", Assert.Single(search.InsideResults).Name);
        Assert.Equal("Searched 1 connected folder.", search.ScopeMessage);
    }

    [Fact]
    public async Task Without_permission_DeskAI_does_not_look_inside_files()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Notes");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "shopping.txt"), "buy bananas");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);

        search.Phrase = "bananas";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.False(search.ShowsInsideFiles);
        Assert.Empty(search.InsideResults);
    }

    [Fact]
    public async Task Taking_back_reading_permission_stops_looking_inside()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Notes");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "shopping.txt"), "buy bananas");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = Assert.Single(search.Folders).Id;
        await search.SetContentPermissionAsync(rootId, allow: true);

        await search.SetContentPermissionAsync(rootId, allow: false);
        search.Phrase = "bananas";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(search.Folders).CanReadContent);
        Assert.Empty(search.InsideResults);
    }

    [Fact]
    public async Task Refreshing_picks_up_a_file_added_since_connecting()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "later.txt"));

        await search.RefreshFolderCommand.ExecuteAsync(Assert.Single(search.Folders).Id);

        Assert.Equal("2 files remembered", Assert.Single(search.Folders).Remembered);
    }

    [Fact]
    public async Task Disconnecting_forgets_the_folder_and_clears_results_but_leaves_files_alone()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "holiday.jpg");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        search.Phrase = "photos";
        await search.SearchCommand.ExecuteAsync(null);

        await search.DisconnectFolderCommand.ExecuteAsync(Assert.Single(search.Folders).Id);

        Assert.Empty(search.Folders);
        Assert.Empty(search.Results);
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));

        await search.SearchCommand.ExecuteAsync(null);
        Assert.Empty(search.Results);
    }

    [Fact]
    public async Task A_protected_folder_is_refused_with_a_reason()
    {
        await using var app = await TestApp.StartAsync();
        var protectedFolder = app.Directory.CreateDummyDirectory("protected");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        await search.ConnectFolderAsync(protectedFolder);

        Assert.Empty(search.Folders);
        Assert.False(string.IsNullOrWhiteSpace(search.FolderMessage));
        Assert.NotEqual("No folders connected yet.", search.FolderMessage);
    }
}
