using DeskAI.Core.Classification;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;
using DeskAI.Core.Workspace;

namespace DeskAI.Core.Tests;

/// <summary>
/// The starter packs are the one place DeskAI writes searches and rules on someone's behalf,
/// so everything in them is checked here: that each search means what its name says, and
/// that each rule is one a person could have written, arrives switched off, and is readable.
/// </summary>
public sealed class StarterPackCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Words kept out of anything a person reads. Repeated from the help catalog, which Core
    /// cannot see, so pack names and summaries obey the same rule as every "?" explanation.
    /// </summary>
    private static readonly string[] TechnicalWords =
    [
        "metadata", "endpoint", "provider", "schema", "sqlite", "deterministic", "authorization",
        "telemetry", "dto", "api", "index", "token", "json", "http", "llm", "scope", "query", "regex",
    ];

    [Fact]
    public void There_are_five_packs_in_a_fixed_order()
    {
        Assert.Equal(
            ["student", "developer", "gaming", "productivity", "minimal"],
            StarterPackCatalog.All.Select(pack => pack.Id));
    }

    [Fact]
    public void Find_returns_a_pack_by_its_id_and_nothing_for_anything_else()
    {
        Assert.Equal("Student", StarterPackCatalog.Find("student")?.Name);
        Assert.Null(StarterPackCatalog.Find("Student"));
        Assert.Null(StarterPackCatalog.Find("custom"));
        Assert.Null(StarterPackCatalog.Find(""));
        Assert.Null(StarterPackCatalog.Find(null));
    }

    [Fact]
    public void Every_search_can_be_saved_as_an_ordinary_saved_search()
    {
        foreach (var search in StarterPackCatalog.All.SelectMany(pack => pack.Searches))
        {
            var saved = SavedSearch.Create(Guid.NewGuid(), search.Name, search.Phrase, Now);
            Assert.Equal(search.Name, saved.Name);
        }
    }

    [Fact]
    public void Search_names_and_rule_names_are_unique_within_each_pack()
    {
        foreach (var pack in StarterPackCatalog.All)
        {
            Assert.Equal(
                pack.Searches.Count,
                pack.Searches.Select(search => search.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(
                pack.Rules.Count,
                pack.Rules.Select(rule => rule.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    /// <summary>
    /// A pack search is only kept if the translator reads it the way the pack claims. "big
    /// files" must become a size, not a hunt for file names containing "big" — a search that
    /// quietly means something else is worse than no search.
    /// </summary>
    [Theory]
    [InlineData("slides", new[] { QueryFilter.Category })]
    [InlineData("documents from last month", new[] { QueryFilter.Category, QueryFilter.ChangedAfter })]
    [InlineData("screenshots", new[] { QueryFilter.Category })]
    [InlineData("archives", new[] { QueryFilter.Category })]
    [InlineData("installers", new[] { QueryFilter.Category })]
    [InlineData("big files", new[] { QueryFilter.MinimumSize })]
    [InlineData("videos", new[] { QueryFilter.Category })]
    [InlineData("big videos", new[] { QueryFilter.Category, QueryFilter.MinimumSize })]
    [InlineData("documents", new[] { QueryFilter.Category })]
    [InlineData("spreadsheets", new[] { QueryFilter.Category })]
    [InlineData("presentations", new[] { QueryFilter.Category })]
    public void Each_phrase_is_understood_as_the_pack_claims(string phrase, QueryFilter[] expected)
    {
        var translation = NaturalLanguageQueryTranslator.Translate(phrase, Now);

        Assert.True(translation.UnderstoodAnything);
        Assert.Equal(
            expected.OrderBy(filter => filter),
            translation.Chips.Select(chip => chip.Filter).OrderBy(filter => filter));
    }

    [Fact]
    public void Every_phrase_used_by_a_pack_is_covered_by_the_translation_test()
    {
        var tested = new HashSet<string>(StringComparer.Ordinal)
        {
            "slides", "documents from last month", "screenshots", "archives", "installers",
            "big files", "videos", "big videos", "documents", "spreadsheets", "presentations",
        };

        foreach (var search in StarterPackCatalog.All.SelectMany(pack => pack.Searches))
        {
            Assert.True(tested.Contains(search.Phrase), $"\"{search.Phrase}\" has no translation test.");
        }
    }

    [Theory]
    [InlineData("slides", FileCategory.Presentations)]
    [InlineData("screenshots", FileCategory.Screenshots)]
    [InlineData("big videos", FileCategory.Videos)]
    public void Category_words_land_on_the_category_they_name(string phrase, FileCategory category)
    {
        var translation = NaturalLanguageQueryTranslator.Translate(phrase, Now);

        Assert.Contains(category, translation.Query.Categories);
    }

    [Fact]
    public void Every_rule_is_one_a_person_could_have_written_and_arrives_switched_off()
    {
        foreach (var rule in StarterPackCatalog.All.SelectMany(pack => pack.Rules))
        {
            var built = rule.ToRule(Guid.NewGuid());

            Assert.False(built.IsEnabled);
            Assert.Equal(rule.Name, built.Name);
            var action = Assert.IsType<MoveToFolderAction>(built.Action);
            Assert.Equal(rule.Destination, action.DestinationRelativeDirectory);
        }
    }

    /// <summary>
    /// A rule's name is its destination, so Automatic tasks shows the same word a person would
    /// have typed rather than a pack's label they never chose.
    /// </summary>
    [Fact]
    public void A_rule_is_named_after_the_folder_it_places_files_in()
    {
        foreach (var rule in StarterPackCatalog.All.SelectMany(pack => pack.Rules))
        {
            Assert.Equal(rule.Destination, rule.Name);
        }
    }

    [Fact]
    public void The_minimal_pack_adds_no_rules()
    {
        Assert.Empty(StarterPackCatalog.Find("minimal")!.Rules);
    }

    [Fact]
    public void Every_pack_has_something_to_add_and_a_short_plain_summary()
    {
        foreach (var pack in StarterPackCatalog.All)
        {
            Assert.NotEmpty(pack.Searches);
            Assert.False(string.IsNullOrWhiteSpace(pack.Name));
            Assert.InRange(pack.Summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 1, 8);
        }
    }

    [Fact]
    public void Nothing_a_person_reads_in_a_pack_uses_technical_words()
    {
        var texts = StarterPackCatalog.All.SelectMany(pack =>
            new[] { pack.Name, pack.Summary }
                .Concat(pack.Searches.Select(search => search.Name))
                .Concat(pack.Rules.Select(rule => rule.ToRule(Guid.NewGuid()).Describe())));

        foreach (var text in texts)
        {
            var words = text.ToLowerInvariant().Split([' ', ',', '.', '"'], StringSplitOptions.RemoveEmptyEntries);
            Assert.DoesNotContain(words, word => TechnicalWords.Contains(word));
        }
    }
}
