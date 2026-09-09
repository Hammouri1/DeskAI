using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

public sealed class SearchQueryTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DefaultQueryHasNoFiltersAndStillCapsResults()
    {
        var query = new SearchQuery();

        Assert.False(query.HasFilters);
        Assert.Equal(SearchQuery.DefaultLimit, query.Limit);
        Assert.Null(query.PathContains);
        Assert.Empty(query.Extensions);
        Assert.Empty(query.Categories);
        Assert.Empty(query.Kinds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankSearchTextIsTreatedAsNoFilter(string text)
    {
        var query = new SearchQuery(pathContains: text);

        Assert.Null(query.PathContains);
        Assert.False(query.HasFilters);
    }

    [Fact]
    public void SearchTextIsTrimmed()
    {
        var query = new SearchQuery(pathContains: "  budget  ");

        Assert.Equal("budget", query.PathContains);
        Assert.True(query.HasFilters);
    }

    [Fact]
    public void SearchTextLongerThanTheLimitIsRejected()
    {
        var text = new string('a', SearchQuery.MaxTextLength + 1);

        Assert.Throws<ArgumentException>(() => new SearchQuery(pathContains: text));
    }

    [Theory]
    [InlineData("txt", ".txt")]
    [InlineData(".TXT", ".txt")]
    [InlineData("  .Pdf ", ".pdf")]
    public void ExtensionsAreNormalizedToLowerCaseWithALeadingDot(string given, string expected)
    {
        var query = new SearchQuery(extensions: [given]);

        Assert.Equal([expected], query.Extensions);
    }

    [Fact]
    public void DuplicateExtensionsCollapse()
    {
        var query = new SearchQuery(extensions: ["txt", ".TXT", " txt "]);

        Assert.Single(query.Extensions);
    }

    [Theory]
    [InlineData(@"doc\txt")]
    [InlineData("doc/txt")]
    [InlineData("*.txt")]
    [InlineData("tar.gz")]
    [InlineData("c:txt")]
    public void APathOrPatternIsNotAcceptedAsAFileEnding(string value)
    {
        Assert.Throws<ArgumentException>(() => new SearchQuery(extensions: [value]));
    }

    [Fact]
    public void BlankExtensionIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SearchQuery(extensions: [" "]));
    }

    [Fact]
    public void TooManyExtensionsAreRejected()
    {
        var many = Enumerable.Range(0, SearchQuery.MaxExtensions + 1).Select(index => $".e{index}");

        Assert.Throws<ArgumentException>(() => new SearchQuery(extensions: many));
    }

    [Fact]
    public void NegativeSizeFiltersAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery(minSizeBytes: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery(maxSizeBytes: -1));
    }

    /// <summary>
    /// A contradictory range must refuse rather than fall back to matching everything:
    /// a search that silently widens is more dangerous than one that fails.
    /// </summary>
    [Fact]
    public void AnImpossibleSizeRangeIsRejectedRatherThanMatchingEverything()
    {
        Assert.Throws<ArgumentException>(() => new SearchQuery(minSizeBytes: 100, maxSizeBytes: 10));
    }

    [Fact]
    public void AnImpossibleDateRangeIsRejectedRatherThanMatchingEverything()
    {
        Assert.Throws<ArgumentException>(() => new SearchQuery(
            modifiedAfterUtc: Moment,
            modifiedBeforeUtc: Moment.AddDays(-1)));
    }

    [Fact]
    public void AnEqualSizeRangeIsAllowed()
    {
        var query = new SearchQuery(minSizeBytes: 10, maxSizeBytes: 10);

        Assert.Equal(10, query.MinSizeBytes);
        Assert.Equal(10, query.MaxSizeBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SearchQuery.MaxLimit + 1)]
    public void AnUnboundedOrImpossibleLimitIsRejected(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery(limit: limit));
    }

    [Fact]
    public void TheMaximumLimitIsAccepted()
    {
        var query = new SearchQuery(limit: SearchQuery.MaxLimit);

        Assert.Equal(SearchQuery.MaxLimit, query.Limit);
    }

    [Fact]
    public void AnyFilterMarksTheQueryAsFiltered()
    {
        Assert.True(new SearchQuery(categories: [FileCategory.Images]).HasFilters);
        Assert.True(new SearchQuery(kinds: [FileKind.Archive]).HasFilters);
        Assert.True(new SearchQuery(minSizeBytes: 1).HasFilters);
        Assert.True(new SearchQuery(maxSizeBytes: 1).HasFilters);
        Assert.True(new SearchQuery(modifiedAfterUtc: Moment).HasFilters);
        Assert.True(new SearchQuery(modifiedBeforeUtc: Moment).HasFilters);
    }
}
