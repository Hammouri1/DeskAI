using System.Reflection;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;
using DeskAI.Core.Workspace;

namespace DeskAI.Core.Tests;

/// <summary>
/// Adding a starter pack writes searches and rules on someone's behalf. These tests fix what
/// that may and may not do: preview saves nothing, clashes are skipped rather than overwritten,
/// limits hold, rules arrive switched off, and nothing here can reach a file.
/// </summary>
public sealed class StarterPackServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PreviewAsync_lists_everything_in_the_pack_and_saves_nothing()
    {
        var world = new World();

        var preview = await world.Service.PreviewAsync("student", TestContext.Current.CancellationToken);

        Assert.Equal(3, preview.Items.Count(item => item.Kind == StarterPackItemKind.Search));
        Assert.Equal(3, preview.Items.Count(item => item.Kind == StarterPackItemKind.Rule));
        Assert.All(preview.Items, item => Assert.True(item.WillBeAdded));
        Assert.True(preview.AddsAnything);
        Assert.Empty(world.Searches.Stored);
        Assert.Empty(world.Rules.Stored);
    }

    [Fact]
    public async Task PreviewAsync_describes_a_search_by_its_words_and_a_rule_by_its_sentence()
    {
        var world = new World();

        var preview = await world.Service.PreviewAsync("minimal", TestContext.Current.CancellationToken);
        var student = await world.Service.PreviewAsync("student", TestContext.Current.CancellationToken);

        Assert.Equal("screenshots", preview.Items.Single(item => item.Name == "Screenshots").Description);
        var rule = student.Items.Single(item => item.Kind == StarterPackItemKind.Rule && item.Name == "Assignments");
        Assert.Equal("When the name contains \"assignment\", move it into Assignments.", rule.Description);
    }

    [Fact]
    public async Task PreviewAsync_skips_a_search_or_rule_whose_name_is_already_used_whatever_the_capitals()
    {
        var world = new World();
        world.Searches.Stored.Add(SavedSearch.Create(Guid.NewGuid(), "SCREENSHOTS", "pictures", Now));
        world.Rules.Stored.Add(AutomationRule.Create(
            Guid.NewGuid(), "slides", [new NameContainsCondition("lecture")], new MoveToFolderAction("Lectures")));

        var preview = await world.Service.PreviewAsync("student", TestContext.Current.CancellationToken);

        var search = preview.Items.Single(item => item.Kind == StarterPackItemKind.Search && item.Name == "Screenshots");
        Assert.Equal("You already have a search called Screenshots.", search.SkipReason);
        var rule = preview.Items.Single(item => item.Kind == StarterPackItemKind.Rule && item.Name == "Slides");
        Assert.Equal("You already have a rule called Slides.", rule.SkipReason);
    }

    [Fact]
    public async Task PreviewAsync_skips_searches_beyond_the_saved_search_limit()
    {
        var world = new World();
        for (var i = 0; i < SavedSearch.MaxSavedSearches - 1; i++)
        {
            world.Searches.Stored.Add(SavedSearch.Create(Guid.NewGuid(), $"Mine {i}", "photos", Now));
        }

        var preview = await world.Service.PreviewAsync("minimal", TestContext.Current.CancellationToken);

        Assert.True(preview.Items.Single(item => item.Name == "Screenshots").WillBeAdded);
        Assert.Equal(
            "You already have 50 saved searches.",
            preview.Items.Single(item => item.Name == "Installers").SkipReason);
    }

    [Fact]
    public async Task PreviewAsync_says_when_a_pack_adds_nothing()
    {
        var world = new World();
        world.Searches.Stored.Add(SavedSearch.Create(Guid.NewGuid(), "Screenshots", "screenshots", Now));
        world.Searches.Stored.Add(SavedSearch.Create(Guid.NewGuid(), "Installers", "installers", Now));

        var preview = await world.Service.PreviewAsync("minimal", TestContext.Current.CancellationToken);

        Assert.False(preview.AddsAnything);
    }

    [Fact]
    public async Task Asking_for_a_pack_that_does_not_exist_is_refused()
    {
        var world = new World();

        await Assert.ThrowsAsync<ArgumentException>(
            () => world.Service.PreviewAsync("custom", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => world.Service.AddAsync("custom", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAsync_saves_the_searches_pinned_and_the_rules_switched_off()
    {
        var world = new World();

        var outcome = await world.Service.AddAsync("student", TestContext.Current.CancellationToken);

        Assert.Equal(["Slides", "Recent documents", "Screenshots"], outcome.AddedSearches);
        Assert.Equal(["Slides", "Assignments", "Screenshots"], outcome.AddedRules);
        Assert.Equal(outcome.AddedSearches, outcome.Pinned);
        Assert.Empty(outcome.AddedButNotPinned);
        Assert.Empty(outcome.Skipped);
        Assert.Null(outcome.StoppedBecause);
        Assert.All(world.Searches.Stored, search => Assert.True(search.IsPinned));
        Assert.Equal(3, world.Rules.Stored.Count);
        Assert.All(world.Rules.Stored, rule => Assert.False(rule.IsEnabled));
        Assert.Equal("documents from last month", world.Searches.Stored.Single(s => s.Name == "Recent documents").Phrase);
        Assert.Equal(Now, world.Searches.Stored[0].CreatedAtUtc);
    }

    [Fact]
    public async Task AddAsync_never_replaces_what_the_person_already_has()
    {
        var world = new World();
        var mine = SavedSearch.Create(Guid.NewGuid(), "Screenshots", "pictures from last week", Now.AddDays(-5));
        world.Searches.Stored.Add(mine);

        var outcome = await world.Service.AddAsync("minimal", TestContext.Current.CancellationToken);

        Assert.Equal(["Installers"], outcome.AddedSearches);
        Assert.Equal("Screenshots", Assert.Single(outcome.Skipped).Name);
        Assert.Same(mine, world.Searches.Stored.Single(search => search.Name == "Screenshots"));
    }

    /// <summary>
    /// Something can change while the preview dialog is open. Add reads again rather than
    /// trusting the preview, so a search made in the meantime is skipped, not overwritten.
    /// </summary>
    [Fact]
    public async Task AddAsync_reads_again_rather_than_trusting_an_earlier_preview()
    {
        var world = new World();
        var preview = await world.Service.PreviewAsync("minimal", TestContext.Current.CancellationToken);
        Assert.All(preview.Items, item => Assert.True(item.WillBeAdded));
        world.Searches.Stored.Add(SavedSearch.Create(Guid.NewGuid(), "Installers", "setups", Now));

        var outcome = await world.Service.AddAsync("minimal", TestContext.Current.CancellationToken);

        Assert.Equal(["Screenshots"], outcome.AddedSearches);
        Assert.Equal("setups", world.Searches.Stored.Single(search => search.Name == "Installers").Phrase);
    }

    [Fact]
    public async Task Adding_the_same_pack_twice_adds_nothing_the_second_time()
    {
        var world = new World();
        await world.Service.AddAsync("gaming", TestContext.Current.CancellationToken);

        var second = await world.Service.AddAsync("gaming", TestContext.Current.CancellationToken);

        Assert.Empty(second.AddedSearches);
        Assert.Empty(second.AddedRules);
        Assert.Equal(6, second.Skipped.Count);
        Assert.Equal(3, world.Searches.Stored.Count);
        Assert.Equal(3, world.Rules.Stored.Count);
    }

    [Fact]
    public async Task AddAsync_pins_only_while_there_is_room_and_says_which_were_not_pinned()
    {
        var world = new World();
        for (var i = 0; i < SavedSearch.MaxPinned - 1; i++)
        {
            world.Searches.Stored.Add(SavedSearch.Create(Guid.NewGuid(), $"Pinned {i}", "photos", Now, isPinned: true));
        }

        var outcome = await world.Service.AddAsync("developer", TestContext.Current.CancellationToken);

        Assert.Equal(["Archives"], outcome.Pinned);
        Assert.Equal(["Installers", "Big files"], outcome.AddedButNotPinned);
        Assert.Equal(SavedSearch.MaxPinned, world.Searches.Stored.Count(search => search.IsPinned));
    }

    /// <summary>
    /// A failure part-way must not pretend nothing happened. What was added stays — each is an
    /// ordinary search or switched-off rule the person can delete — and the outcome says so.
    /// </summary>
    [Fact]
    public async Task A_failure_part_way_reports_what_was_added_before_it_stopped()
    {
        var world = new World();
        world.Rules.FailOnSave = new InvalidOperationException("The database is busy.");

        var outcome = await world.Service.AddAsync("developer", TestContext.Current.CancellationToken);

        Assert.Equal(3, outcome.AddedSearches.Count);
        Assert.Empty(outcome.AddedRules);
        Assert.Equal("The database is busy.", outcome.StoppedBecause);
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

        var dependencies = typeof(StarterPackService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));
    }

    private sealed class World
    {
        public World() => Service = new StarterPackService(Searches, Rules, new FixedClock(Now));

        public FakeSearches Searches { get; } = new();

        public FakeRules Rules { get; } = new();

        public StarterPackService Service { get; }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    internal sealed class FakeSearches : ISavedSearchRepository
    {
        public List<SavedSearch> Stored { get; } = [];

        public Task SaveAsync(SavedSearch collection, CancellationToken cancellationToken = default)
        {
            Stored.RemoveAll(item => item.Id == collection.Id);
            Stored.Add(collection);
            return Task.CompletedTask;
        }

        public Task SetPinnedAsync(Guid collectionId, bool isPinned, CancellationToken cancellationToken = default)
        {
            var index = Stored.FindIndex(item => item.Id == collectionId);
            if (index >= 0)
            {
                Stored[index] = Stored[index].WithPinned(isPinned);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SavedSearch>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SavedSearch>>(Stored.ToArray());

        public Task RemoveAsync(Guid collectionId, CancellationToken cancellationToken = default)
        {
            Stored.RemoveAll(item => item.Id == collectionId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRules : IRuleRepository
    {
        public List<AutomationRule> Stored { get; } = [];

        public Exception? FailOnSave { get; set; }

        public Task<IReadOnlyList<AutomationRule>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AutomationRule>>(Stored.ToArray());

        public Task<AutomationRule?> FindAsync(Guid ruleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.FirstOrDefault(rule => rule.Id == ruleId));

        public Task SaveAsync(AutomationRule rule, CancellationToken cancellationToken = default)
        {
            if (FailOnSave is not null)
            {
                throw FailOnSave;
            }

            Stored.Add(rule);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
        {
            Stored.RemoveAll(rule => rule.Id == ruleId);
            return Task.CompletedTask;
        }
    }
}
