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

    [Fact]
    public async Task The_page_offers_the_five_starter_packs_and_no_custom_card()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        Assert.Equal(["Student", "Developer", "Gaming", "Productivity", "Minimal"], page.Packs.Select(card => card.Name));
        Assert.True(page.HasNoPins);
    }

    [Fact]
    public async Task Previewing_a_pack_shows_what_it_would_add_and_adds_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        var preview = await page.PreviewPackAsync("student");

        Assert.Equal(6, preview.Items.Count);
        Assert.Contains(preview.Items, item => item.Description == "When the name contains \"assignment\", move it into Assignments.");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        Assert.Empty(search.SavedSearches);
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        Assert.Empty(automation.Rules);
    }

    [Fact]
    public async Task Adding_a_pack_says_what_was_added_on_its_card_and_pins_its_searches()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.AddPackAsync("student");

        var card = page.Packs.Single(item => item.Id == "student");
        Assert.Equal(
            "Added 3 searches and 3 rules. The rules are switched off — turn them on in Automatic tasks.",
            card.Result);
        Assert.All(page.Packs.Where(item => item.Id != "student"), other => Assert.False(other.HasResult));
        Assert.Equal(
            ["Recent documents", "Screenshots", "Slides"],
            page.Pins.Select(tile => tile.Name).Order(StringComparer.Ordinal));
        Assert.True(page.HasPins);
    }

    [Fact]
    public async Task A_search_the_person_already_has_is_skipped_and_named()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "holiday.jpg");
        await SaveSearchAsync(app, folder, "Screenshots", "pictures");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.AddPackAsync("minimal");

        Assert.Equal("Added 1 search. Skipped 1 you already had: Screenshots.", page.Packs.Single(item => item.Id == "minimal").Result);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        Assert.Equal("pictures", search.SavedSearches.Single(item => item.Name == "Screenshots").Phrase);
    }

    [Fact]
    public async Task Adding_a_pack_twice_says_nothing_more_was_added()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.AddPackAsync("minimal");

        await page.AddPackAsync("minimal");

        Assert.Equal("Nothing added — you already have everything in this pack.", page.Packs.Single(item => item.Id == "minimal").Result);
    }

    [Fact]
    public async Task A_pinned_tile_counts_the_matching_files_and_names_none_of_them()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "holiday.jpg");
        var id = await SaveSearchAsync(app, folder, "Photos", "photos");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.PinCommand.ExecuteAsync(id);

        var tile = Assert.Single(page.Pins);
        Assert.Equal("Photos", tile.Name);
        Assert.Equal("1 file", tile.Count);
        Assert.DoesNotContain("holiday", tile.Name + tile.Count + page.PinsCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(page.OtherSearches);
        Assert.StartsWith("Counted at ", page.PinsCaption, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_connected_a_tile_says_so_instead_of_zero()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.AddPackAsync("minimal");

        Assert.All(page.Pins, tile => Assert.Equal("No folders connected", tile.Count));
    }

    /// <summary>
    /// Owner's screenshot 2026-09-16: "No folders connected" was drawn in the big-number style
    /// and cut off at "No folders connect". A tile has a number slot, shown only for a number,
    /// and a words line that always fits.
    /// </summary>
    [Fact]
    public async Task A_tile_keeps_words_out_of_its_number_slot()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.AddPackAsync("minimal");
        Assert.All(page.Pins, tile =>
        {
            Assert.False(tile.HasNumber);
            Assert.Equal(string.Empty, tile.Number);
            Assert.Equal("No folders connected", tile.Words);
        });

        var folder = app.MakeFolder("Coursework", "notes.txt", "holiday.jpg", "beach.png");
        var id = await SaveSearchAsync(app, folder, "Photos", "photos");
        await page.PinCommand.ExecuteAsync(id);

        var counted = Assert.Single(page.Pins, tile => tile.Name == "Photos");
        Assert.True(counted.HasNumber);
        Assert.Equal("2", counted.Number);
        Assert.Equal("files", counted.Words);
        Assert.Equal("2 files", counted.Count);
    }

    [Fact]
    public async Task A_search_whose_words_mean_nothing_says_so_instead_of_zero()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "holiday.jpg");
        var id = await SaveSearchAsync(app, folder, "Odd", "the");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.PinCommand.ExecuteAsync(id);

        Assert.Equal("Search not understood", Assert.Single(page.Pins).Count);
    }

    [Fact]
    public async Task Pinning_a_ninth_search_is_refused_beside_the_list_and_unpinning_makes_room()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "holiday.jpg");
        Guid lastId = Guid.Empty;
        for (var i = 0; i <= WorkspaceViewModel.MaxPinned; i++)
        {
            lastId = await SaveSearchAsync(app, folder, $"Search {i}", "photos");
        }

        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        foreach (var other in page.OtherSearches.ToArray())
        {
            await page.PinCommand.ExecuteAsync(other.Id);
        }

        Assert.Equal(WorkspaceViewModel.MaxPinned, page.Pins.Count);
        Assert.False(page.CanPinMore);
        Assert.Contains("up to 8", page.PinMessage, StringComparison.Ordinal);
        var left = Assert.Single(page.OtherSearches);

        await page.UnpinCommand.ExecuteAsync(page.Pins[0].Id);
        await page.PinCommand.ExecuteAsync(left.Id);

        Assert.Equal(WorkspaceViewModel.MaxPinned, page.Pins.Count);
        Assert.Contains(page.Pins, tile => tile.Id == left.Id);
        Assert.False(page.HasPinMessage);
    }

    [Fact]
    public async Task Open_in_Search_from_a_tile_lands_on_its_results()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "notes.txt", "holiday.jpg");
        var id = await SaveSearchAsync(app, folder, "Photos", "photos");
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.PinCommand.ExecuteAsync(id);

        page.OpenInSearch(page.Pins[0].Id);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        Assert.Equal("holiday.jpg", Assert.Single(search.Results).Name);
    }

    /// <summary>
    /// The promise the pack dialog makes, checked end to end: a pack's rule arrives Off, changes
    /// nothing Tidy suggests or a check counts, and only the person's own switch turns it on.
    /// </summary>
    [Fact]
    public async Task A_pack_rule_is_off_and_changes_nothing_until_the_person_turns_it_on()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Inbox", "invoice-march.pdf");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.AddPackAsync("productivity");

        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        var invoices = automation.Rules.Single(rule => rule.Name == "Invoices");
        Assert.Equal("Off", invoices.State);
        await automation.CheckNowCommand.ExecuteAsync(null);
        Assert.DoesNotContain("Your rules match", automation.Message, StringComparison.Ordinal);
        var before = app.Get<TidyViewModel>();
        await before.InitializeAsync();
        Assert.DoesNotContain(before.Groups, group => group.Folder == "Invoices");

        await automation.ToggleRuleCommand.ExecuteAsync(invoices.Id);

        var after = app.Get<TidyViewModel>();
        await after.InitializeAsync();
        Assert.Contains(after.Groups, group => group.Folder == "Invoices");
        Assert.True(File.Exists(Path.Combine(folder, "invoice-march.pdf")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Invoices")));
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
