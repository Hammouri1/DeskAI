using System.Reflection;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Classification;
using DeskAI.Core.Search;
using DeskAI.Core.Workspace;

namespace DeskAI.Core.Tests;

/// <summary>
/// A pinned tile shows a number, and a number on a page is read as true. These tests fix the
/// four answers a tile can give, so an empty count is never shown for "nothing connected" or
/// "not understood", and a count that stopped at the limit never looks exact.
/// </summary>
public sealed class PinnedSearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CountAsync_counts_the_files_the_search_finds()
    {
        var world = new World();
        var study = world.Roots.Add("Study");
        world.Index.Put(study, FileSearchServiceTests.Entry(study, 1, "holiday.png", FileCategory.Images));
        world.Index.Put(study, FileSearchServiceTests.Entry(study, 2, "beach.png", FileCategory.Images));
        world.Index.Put(study, FileSearchServiceTests.Entry(study, 3, "notes.txt", FileCategory.Documents));

        var count = await world.Service.CountAsync(Saved("photos"), TestContext.Current.CancellationToken);

        Assert.Equal(new PinnedCount(PinnedCountKind.Counted, 2), count);
    }

    [Fact]
    public async Task CountAsync_says_when_nothing_is_connected_rather_than_counting_zero()
    {
        var world = new World();

        var count = await world.Service.CountAsync(Saved("photos"), TestContext.Current.CancellationToken);

        Assert.Equal(PinnedCountKind.NoFolders, count.Kind);
    }

    [Fact]
    public async Task CountAsync_says_when_the_words_are_not_understood_rather_than_counting_zero()
    {
        var world = new World();
        world.Roots.Add("Study");

        var count = await world.Service.CountAsync(Saved("the"), TestContext.Current.CancellationToken);

        Assert.Equal(PinnedCountKind.NotUnderstood, count.Kind);
    }

    [Fact]
    public async Task CountAsync_says_when_the_search_stopped_at_its_limit()
    {
        var world = new World();
        var study = world.Roots.Add("Study");
        for (var seed = 1; seed <= SearchQuery.DefaultLimit + 5; seed++)
        {
            world.Index.Put(study, FileSearchServiceTests.Entry(study, seed, $"photo-{seed}.png", FileCategory.Images));
        }

        var count = await world.Service.CountAsync(Saved("photos"), TestContext.Current.CancellationToken);

        Assert.Equal(new PinnedCount(PinnedCountKind.AtLimit, SearchQuery.DefaultLimit), count);
    }

    [Fact]
    public async Task PinAsync_pins_a_saved_search_and_ListPinnedAsync_shows_only_pinned_ones()
    {
        var world = new World();
        var photos = Saved("photos", "Photos");
        var videos = Saved("videos", "Videos");
        world.Searches.Stored.AddRange([photos, videos]);

        Assert.True(await world.Service.PinAsync(photos.Id, TestContext.Current.CancellationToken));

        var pinned = Assert.Single(await world.Service.ListPinnedAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Photos", pinned.Name);
    }

    [Fact]
    public async Task PinAsync_refuses_once_the_limit_is_reached()
    {
        var world = new World();
        for (var i = 0; i < SavedSearch.MaxPinned; i++)
        {
            world.Searches.Stored.Add(Saved("photos", $"Pinned {i}").WithPinned(true));
        }

        var extra = Saved("videos", "Extra");
        world.Searches.Stored.Add(extra);

        Assert.False(await world.Service.PinAsync(extra.Id, TestContext.Current.CancellationToken));
        Assert.False(world.Searches.Stored.Single(search => search.Name == "Extra").IsPinned);
    }

    [Fact]
    public async Task PinAsync_is_a_harmless_yes_for_an_already_pinned_search_and_a_no_for_an_unknown_one()
    {
        var world = new World();
        var photos = Saved("photos", "Photos").WithPinned(true);
        world.Searches.Stored.Add(photos);

        Assert.True(await world.Service.PinAsync(photos.Id, TestContext.Current.CancellationToken));
        Assert.False(await world.Service.PinAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnpinAsync_takes_the_pin_away_and_keeps_the_search()
    {
        var world = new World();
        var photos = Saved("photos", "Photos").WithPinned(true);
        world.Searches.Stored.Add(photos);

        await world.Service.UnpinAsync(photos.Id, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(world.Searches.Stored).IsPinned);
        Assert.Empty(await world.Service.ListPinnedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_cannot_reach_anything_that_changes_a_file_or_talks_to_AI()
    {
        var forbidden = new[]
        {
            typeof(IFolderTidyExecutor),
            typeof(IOperationJournal),
            typeof(IOrganizationPlanner),
            typeof(IFileScanner),
            typeof(IContentTextExtractor),
            typeof(ICredentialVault),
            typeof(IOrganizationSuggestionProvider),
        };

        var dependencies = typeof(PinnedSearchService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));
    }

    private static SavedSearch Saved(string phrase, string name = "Pinned") =>
        SavedSearch.Create(Guid.NewGuid(), name, phrase, Now);

    private sealed class World
    {
        public World() =>
            Service = new PinnedSearchService(Searches, new FileSearchService(Roots, Index), new FixedClock(Now));

        public FileSearchServiceTests.FakeRoots Roots { get; } = new();

        public FileSearchServiceTests.FakeIndex Index { get; } = new();

        public StarterPackServiceTests.FakeSearches Searches { get; } = new();

        public PinnedSearchService Service { get; }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
