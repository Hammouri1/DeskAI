using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Content;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The quick search bar as a person uses it: press the shortcut, read the buddy, type, move with
/// the arrows, press Enter. Generated folders only; opening is recorded, never real.
/// </summary>
public sealed class QuickSearchPageTests
{
    [Fact]
    public async Task The_empty_bar_shows_the_buddys_greeting_and_three_examples()
    {
        await using var app = await TestApp.StartAsync();
        var bar = app.Get<QuickSearchViewModel>();

        await bar.ShowAsync();

        Assert.Equal(SearchBuddy.Sparky, bar.Buddy);
        Assert.Equal(BuddyMood.Idle, bar.Mood);
        Assert.Equal("Hi! What are we looking for?", bar.BuddyLine);
        Assert.True(bar.ShowsExamples);
        Assert.Equal(["pdf from last week", "photos from this month", "big videos"], QuickSearchViewModel.Examples);
    }

    [Fact]
    public async Task Clicking_an_example_fills_the_box_and_searches()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Music", "long song.mp4");
        var bar = await OpenBarAsync(app);

        await bar.ChooseExampleAsync("big videos");

        Assert.Equal("big videos", bar.Phrase);
        Assert.False(bar.ShowsExamples);
        Assert.NotEqual(BuddyMood.Idle, bar.Mood);
    }

    [Fact]
    public async Task Typing_finds_a_file_by_name_and_the_buddy_says_so()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf", "notes.txt");
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "essay");

        var row = Assert.Single(bar.NameRows);
        Assert.Equal("essay.pdf", row.Name);
        Assert.Equal("School", row.Where);
        Assert.Equal("Open", row.ActionText);
        Assert.Same(row, bar.Selected);
        Assert.True(row.IsSelected);
        Assert.Equal(BuddyMood.Found, bar.Mood);
        Assert.Equal("Found 1!", bar.BuddyLine);
    }

    [Fact]
    public async Task More_than_five_says_only_the_first_five_are_shown()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay 1.pdf", "essay 2.pdf", "essay 3.pdf", "essay 4.pdf", "essay 5.pdf", "essay 6.pdf");
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "essay");

        Assert.Equal(5, bar.NameRows.Count);
        Assert.Equal("Showing the first 5 by name.", bar.NameFact);
        Assert.True(bar.ShowsSeeMore);
    }

    [Fact]
    public async Task No_folder_connected_says_to_connect_one_and_offers_Open_DeskAI()
    {
        await using var app = await TestApp.StartAsync();
        var bar = await OpenBarAsync(app);
        string? route = null;
        bar.OpenDeskAiRequested += (_, asked) => route = asked;

        await TypeAsync(bar, "essay");

        Assert.Equal("Connect a folder in DeskAI first. Quick search looks only in folders you connected.", bar.NameFact);
        Assert.True(bar.ShowsOpenDeskAi);
        Assert.False(bar.HasInsideFact);
        bar.OpenDeskAi();
        Assert.Equal("dashboard", route);
    }

    [Fact]
    public async Task Nothing_matched_says_so_and_the_buddy_is_sorry()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "zzqx");

        Assert.Empty(bar.NameRows);
        Assert.Equal("Nothing matched in your connected folders.", bar.NameFact);
        Assert.Equal(BuddyMood.Nothing, bar.Mood);
        Assert.Equal("Hmm, nothing yet.", bar.BuddyLine);
    }

    [Fact]
    public async Task Words_inside_a_file_appear_under_their_own_heading_with_a_piece_of_text()
    {
        await using var app = await TestApp.StartAsync();
        var search = await ConnectWithTextAsync(app, "Notes", ("shopping.txt", "buy bananas\nand bread"));
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "bananas");

        var row = Assert.Single(bar.InsideRows);
        Assert.Equal("shopping.txt", row.Name);
        Assert.Contains("bananas", row.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', row.Snippet);
        Assert.False(bar.IsLookingInside);
        Assert.True(bar.ShowsInsideGroup);
        Assert.Equal(BuddyMood.Found, bar.Mood);

        Assert.True(await bar.ActivateAsync(row, showInFolder: false));
        Assert.EndsWith("shopping.txt", Assert.Single(app.Shell.Opened), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_reading_inside_the_bar_says_where_to_allow_it_and_reads_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Notes", "shopping.txt");
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "bananas");

        Assert.Empty(bar.InsideRows);
        Assert.Equal("To find words inside files too, allow it for a folder on the Search page.", bar.InsideFact);
    }

    [Fact]
    public async Task A_file_already_listed_by_name_is_not_listed_again()
    {
        await using var app = await TestApp.StartAsync();
        var search = await ConnectWithTextAsync(app, "Notes", ("bananas.txt", "all about bananas"));
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "bananas");

        Assert.Single(bar.NameRows);
        Assert.Empty(bar.InsideRows);
        Assert.Equal("Nothing else found inside the 1 file DeskAI checked.", bar.InsideFact);
    }

    [Fact]
    public async Task Enter_on_a_program_shows_it_in_its_folder_instead()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Stuff", "invoice.pdf.exe");
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "invoice");

        Assert.Equal("Show in folder", bar.Selected!.ActionText);
        Assert.True(await bar.ActivateAsync(bar.Selected, showInFolder: false));

        Assert.Empty(app.Shell.Opened);
        Assert.Single(app.Shell.Shown);
    }

    [Fact]
    public async Task Ctrl_Enter_always_shows_in_folder()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "essay");

        await bar.ActivateAsync(bar.Selected, showInFolder: true);

        Assert.Empty(app.Shell.Opened);
        Assert.Single(app.Shell.Shown);
    }

    [Fact]
    public async Task A_file_that_went_away_keeps_the_bar_open_with_the_reason()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "essay");
        File.Delete(Path.Combine(app.Sandbox, "School", "essay.pdf"));

        Assert.False(await bar.ActivateAsync(bar.Selected, showInFolder: false));

        Assert.Equal("DeskAI couldn't open it: It is no longer there. Press Refresh on Search so DeskAI catches up.", bar.OpenMessage);
        Assert.True(bar.HasOpenMessage);
        Assert.Empty(app.Shell.Opened);
    }

    [Fact]
    public async Task When_Windows_refuses_the_bar_says_so()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "essay");
        app.Shell.Fail = true;

        Assert.False(await bar.ActivateAsync(bar.Selected, showInFolder: false));

        Assert.Equal("DeskAI couldn't open it: Windows couldn't open it.", bar.OpenMessage);
    }

    [Fact]
    public async Task Up_and_Down_move_across_both_groups()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay 1.pdf", "essay 2.pdf");
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "essay");

        bar.MoveSelection(+1);
        Assert.Same(bar.NameRows[1], bar.Selected);
        bar.MoveSelection(+1);
        Assert.Same(bar.NameRows[1], bar.Selected);
        bar.MoveSelection(-5);
        Assert.Same(bar.NameRows[0], bar.Selected);
        Assert.True(bar.NameRows[0].IsSelected);
        Assert.False(bar.NameRows[1].IsSelected);
    }

    [Fact]
    public async Task Each_buddy_speaks_in_its_own_voice()
    {
        foreach (var buddy in Enum.GetValues<SearchBuddy>())
        {
            await using var app = await TestApp.StartAsync();
            await app.Get<QuickSearchSettingsService>().SetBuddyAsync(buddy, TestContext.Current.CancellationToken);
            await ConnectAsync(app, "School", "essay.pdf");
            var bar = await OpenBarAsync(app);

            Assert.Equal(buddy, bar.Buddy);
            Assert.Equal(SearchBuddyLines.Line(buddy, BuddyMood.Idle), bar.BuddyLine);
            await TypeAsync(bar, "essay");
            Assert.Equal(SearchBuddyLines.Line(buddy, BuddyMood.Found, 1), bar.BuddyLine);
            await TypeAsync(bar, "zzqx");
            Assert.Equal(SearchBuddyLines.Line(buddy, BuddyMood.Nothing), bar.BuddyLine);
        }
    }

    [Fact]
    public async Task Clicking_the_buddy_makes_it_happy_and_then_it_goes_back()
    {
        await using var app = await TestApp.StartAsync();
        var bar = await OpenBarAsync(app);

        var petting = bar.PetBuddyAsync();
        Assert.Equal(BuddyMood.Happy, bar.Mood);
        Assert.Equal("Wheee!", bar.BuddyLine);
        await petting;

        Assert.Equal(BuddyMood.Idle, bar.Mood);
    }

    [Fact]
    public async Task Nothing_typed_or_found_is_remembered()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");
        var store = app.Get<IAppSettingsStore>();
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "essay");
        await bar.ActivateAsync(bar.Selected, showInFolder: false);
        bar.Hide();

        foreach (var key in new[] { "quicksearch.history", "quicksearch.recent", "quicksearch.last" })
        {
            Assert.Null(await store.ReadAsync(key, TestContext.Current.CancellationToken));
        }

        var reopened = await OpenBarAsync(app);
        Assert.Equal(string.Empty, reopened.Phrase);
        Assert.Empty(reopened.NameRows);
        Assert.True(reopened.ShowsExamples);
    }

    [Fact]
    public async Task See_more_opens_Search_with_the_same_words()
    {
        await using var app = await TestApp.StartAsync();
        var bar = await OpenBarAsync(app);
        string? route = null;
        bar.OpenDeskAiRequested += (_, asked) => route = asked;
        await TypeAsync(bar, "essay");

        bar.SeeMoreInDeskAi();

        Assert.Equal("search", route);
        Assert.Equal("essay", app.Get<SearchRequest>().TakePhrase());
    }

    [Fact]
    public async Task Typing_again_stops_a_slow_inside_look_and_its_late_rows_never_appear()
    {
        await using var app = await TestApp.StartAsync();
        var search = await ConnectAsync(app, "Notes", "a.txt");
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var reader = new BlockingReader();
        var bar = BarWith(app, reader);
        await bar.ShowAsync();

        bar.Phrase = "bananas";
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(bar.IsLookingInside);
        Assert.Equal(BuddyMood.Thinking, bar.Mood);

        bar.Phrase = "zzqx";
        await bar.Pending;

        Assert.True(reader.WasCancelled);
        Assert.Empty(bar.InsideRows);
        Assert.False(bar.IsLookingInside);
    }

    [Fact]
    public async Task Hiding_the_bar_stops_an_inside_look()
    {
        await using var app = await TestApp.StartAsync();
        var search = await ConnectAsync(app, "Notes", "a.txt");
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var reader = new BlockingReader();
        var bar = BarWith(app, reader);
        await bar.ShowAsync();
        bar.Phrase = "bananas";
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        bar.Hide();
        await bar.Pending;

        Assert.True(reader.WasCancelled);
        Assert.False(bar.IsLookingInside);
    }

    private static async Task TypeAsync(QuickSearchViewModel bar, string words)
    {
        bar.Phrase = words;
        await bar.Pending;
    }

    private static async Task<QuickSearchViewModel> OpenBarAsync(TestApp app)
    {
        var bar = app.Get<QuickSearchViewModel>();
        await bar.ShowAsync();
        return bar;
    }

    private static async Task<SearchViewModel> ConnectAsync(TestApp app, string name, params string[] files)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(app.MakeFolder(name, files));
        return search;
    }

    /// <summary>Makes text files with the given words before connecting, so no Refresh is needed.</summary>
    private static async Task<SearchViewModel> ConnectWithTextAsync(TestApp app, string name, params (string File, string Text)[] files)
    {
        var folder = app.MakeFolder(name);
        foreach (var (file, text) in files)
        {
            app.Directory.CreateDummyFile(Path.Combine("folders", name, file), text);
        }

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        return search;
    }

    private static QuickSearchViewModel BarWith(TestApp app, IContentTextExtractor reader)
    {
        var roots = app.Get<IAuthorizedRootRepository>();
        var index = app.Get<IFileIndex>();
        var service = new QuickSearchService(app.Get<FileSearchService>(), new ContentSearchService(roots, index, reader), roots);
        return new QuickSearchViewModel(service, app.Get<QuickSearchSettingsService>(), app.Get<IFileLauncher>(),
            app.Get<SearchRequest>(), app.Get<IClock>(), app.Get<QuickSearchTiming>());
    }

    /// <summary>
    /// The first read never finishes until it is cancelled, like a big PDF on a slow disk. Later
    /// reads answer at once with "could not read", so the newer words' look can finish.
    /// </summary>
    private sealed class BlockingReader : IContentTextExtractor
    {
        private int _calls;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCancelled { get; private set; }

        public async Task<TextExtraction> ExtractAsync(AuthorizedRoot root, string relativePath, TextExtractionOptions options, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                return TextExtraction.Refused(relativePath, TextExtractionStatus.Unavailable, "Generated test refusal.");
            }

            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }

            throw new InvalidOperationException("unreachable");
        }
    }
}
