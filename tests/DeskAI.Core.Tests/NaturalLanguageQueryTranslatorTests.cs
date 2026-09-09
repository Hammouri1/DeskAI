using DeskAI.Core.Classification;
using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

public sealed class NaturalLanguageQueryTranslatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 14, 30, 0, TimeSpan.Zero);


    /// <summary>
    /// Understanding nothing must be visible. Returning a filterless query and calling it a
    /// result would list the whole folder and look like a successful search.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPhraseUnderstandsNothingAndFiltersNothing(string? text)
    {
        var translation = NaturalLanguageQueryTranslator.Translate(text, Now);

        Assert.False(translation.UnderstoodAnything);
        Assert.False(translation.Query.HasFilters);
        Assert.Empty(translation.Chips);
    }

    [Fact]
    public void APhraseLongerThanTheLimitIsRejected()
    {
        var text = new string('a', NaturalLanguageQueryTranslator.MaxInputLength + 1);

        Assert.Throws<ArgumentException>(() => NaturalLanguageQueryTranslator.Translate(text, Now));
    }

    [Fact]
    public void TheWholeExampleSentenceBecomesTheExpectedQuery()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("big videos from last month", Now);

        Assert.True(translation.UnderstoodAnything);
        Assert.Equal([FileCategory.Videos], translation.Query.Categories);
        Assert.Equal(NaturalLanguageQueryTranslator.LargeFileThresholdBytes, translation.Query.MinSizeBytes);
        Assert.Equal(Now.AddMonths(-1), translation.Query.ModifiedAfterUtc);

        // "from" carries no meaning, so it must not survive as a text search.
        Assert.Null(translation.Query.PathContains);
    }

    [Fact]
    public void EveryUnderstoodPartProducesOneReadableChip()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("big videos from last month", Now);

        Assert.Equal(
            [QueryFilter.Category, QueryFilter.MinimumSize, QueryFilter.ChangedAfter],
            translation.Chips.Select(chip => chip.Filter));
        Assert.Contains(translation.Chips, chip => chip.Label == "Videos");
        Assert.Contains(translation.Chips, chip => chip.Label == "Changed in the last month");
    }

    /// <summary>A vague threshold must state its real number rather than hide it.</summary>
    [Fact]
    public void TheVagueWordBigNamesTheSizeItActuallyMeans()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("big files", Now);

        Assert.Contains(translation.Chips, chip => chip.Label == "Larger than 100 MB");
    }

    [Theory]
    [InlineData("photos", FileCategory.Images)]
    [InlineData("pictures", FileCategory.Images)]
    [InlineData("screenshots", FileCategory.Screenshots)]
    [InlineData("movies", FileCategory.Videos)]
    [InlineData("music", FileCategory.Audio)]
    [InlineData("spreadsheets", FileCategory.Spreadsheets)]
    [InlineData("slides", FileCategory.Presentations)]
    [InlineData("zips", FileCategory.Archives)]
    [InlineData("installers", FileCategory.Installers)]
    [InlineData("documents", FileCategory.Documents)]
    public void CommonWordsMapToCategories(string word, FileCategory expected)
    {
        var translation = NaturalLanguageQueryTranslator.Translate(word, Now);

        Assert.Equal([expected], translation.Query.Categories);
        Assert.Null(translation.Query.PathContains);
    }

    [Fact]
    public void ScreenshotIsReadAsScreenshotsRatherThanPlainImages()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("screenshots", Now);

        Assert.Equal([FileCategory.Screenshots], translation.Query.Categories);
        Assert.DoesNotContain(FileCategory.Images, translation.Query.Categories);
    }

    [Fact]
    public void SeveralCategoriesInOnePhraseAreAllKept()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("photos and videos", Now);

        Assert.Contains(FileCategory.Images, translation.Query.Categories);
        Assert.Contains(FileCategory.Videos, translation.Query.Categories);
    }

    [Theory]
    [InlineData("over 10 mb", 10L * 1024 * 1024)]
    [InlineData("larger than 2gb", 2L * 1024 * 1024 * 1024)]
    [InlineData("more than 500 kb", 500L * 1024)]
    public void ExplicitSizesAreRead(string phrase, long expected)
    {
        var translation = NaturalLanguageQueryTranslator.Translate(phrase, Now);

        Assert.Equal(expected, translation.Query.MinSizeBytes);
    }

    [Fact]
    public void SmallerThanIsReadAsAMaximum()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("under 5 mb", Now);

        Assert.Equal(5L * 1024 * 1024, translation.Query.MaxSizeBytes);
        Assert.Null(translation.Query.MinSizeBytes);
    }

    /// <summary>
    /// "big files under 5 MB" is contradictory. The number is what the person actually
    /// typed, so the vague half is dropped rather than producing an impossible query.
    /// </summary>
    [Fact]
    public void AVagueWordNeverContradictsAnExplicitNumber()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("big files under 5 mb", Now);

        Assert.Equal(5L * 1024 * 1024, translation.Query.MaxSizeBytes);
        Assert.Null(translation.Query.MinSizeBytes);
    }

    [Fact]
    public void AnExplicitMinimumWinsOverTheVagueWordSmall()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("small files over 100 mb", Now);

        Assert.Equal(100L * 1024 * 1024, translation.Query.MinSizeBytes);
        Assert.Null(translation.Query.MaxSizeBytes);
    }

    [Theory]
    [InlineData("today")]
    [InlineData("yesterday")]
    [InlineData("last week")]
    [InlineData("last month")]
    [InlineData("last year")]
    [InlineData("last 30 days")]
    public void RelativeDatesAreUnderstood(string phrase)
    {
        var translation = NaturalLanguageQueryTranslator.Translate(phrase, Now);

        Assert.NotNull(translation.Query.ModifiedAfterUtc);
        Assert.True(translation.Query.ModifiedAfterUtc <= Now);
        Assert.Contains(translation.Chips, chip => chip.Filter == QueryFilter.ChangedAfter);
    }

    [Fact]
    public void LastNumberOfDaysUsesTheNumberGiven()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("last 30 days", Now);

        Assert.Equal(Now.AddDays(-30), translation.Query.ModifiedAfterUtc);
        Assert.Contains(translation.Chips, chip => chip.Label == "Changed in the last 30 days");
    }

    /// <summary>
    /// The same phrase must mean the same thing every time, which is why the current moment
    /// is an argument instead of a call to the system clock.
    /// </summary>
    [Fact]
    public void TheSamePhraseAtTheSameMomentAlwaysGivesTheSameDate()
    {
        var first = NaturalLanguageQueryTranslator.Translate("last month", Now);
        var second = NaturalLanguageQueryTranslator.Translate("last month", Now);

        Assert.Equal(first.Query.ModifiedAfterUtc, second.Query.ModifiedAfterUtc);
    }

    [Fact]
    public void ADifferentMomentMovesTheDateWindow()
    {
        var earlier = NaturalLanguageQueryTranslator.Translate("last month", Now);
        var later = NaturalLanguageQueryTranslator.Translate("last month", Now.AddDays(10));

        Assert.NotEqual(earlier.Query.ModifiedAfterUtc, later.Query.ModifiedAfterUtc);
    }

    [Fact]
    public void AFileEndingIsRecognisedAndNotLeftAsText()
    {
        var translation = NaturalLanguageQueryTranslator.Translate(".pdf", Now);

        Assert.Equal([".pdf"], translation.Query.Extensions);
        Assert.Null(translation.Query.PathContains);
        Assert.Contains(translation.Chips, chip => chip.Label == "Ends with .pdf");
    }

    [Fact]
    public void WordsThatMeanNothingElseBecomeTheTextSearch()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("invoice", Now);

        Assert.Equal("invoice", translation.Query.PathContains);
        Assert.Contains(translation.Chips, chip => chip.Filter == QueryFilter.Text);
    }

    [Fact]
    public void TextAndFiltersCombine()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("invoice documents from last year", Now);

        Assert.Equal("invoice", translation.Query.PathContains);
        Assert.Equal([FileCategory.Documents], translation.Query.Categories);
        Assert.Equal(Now.AddYears(-1), translation.Query.ModifiedAfterUtc);
    }

    [Fact]
    public void FillerWordsAloneAreNotTreatedAsASearchTerm()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("show me all my files", Now);

        Assert.Null(translation.Query.PathContains);
        Assert.False(translation.UnderstoodAnything);
    }

    [Fact]
    public void UpperCaseIsUnderstoodTheSameWay()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("BIG VIDEOS", Now);

        Assert.Equal([FileCategory.Videos], translation.Query.Categories);
        Assert.Equal(NaturalLanguageQueryTranslator.LargeFileThresholdBytes, translation.Query.MinSizeBytes);
    }

    /// <summary>
    /// Nothing here builds SQL, but the text still reaches a LIKE parameter downstream, so
    /// it must survive as ordinary characters rather than being interpreted.
    /// </summary>
    [Fact]
    public void PunctuationInASearchTermIsCarriedThroughAsPlainText()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("report_final%", Now);

        Assert.Equal("report_final%", translation.Query.PathContains);
    }

    [Fact]
    public void TheResultLimitIsAlwaysCarriedOntoTheQuery()
    {
        var translation = NaturalLanguageQueryTranslator.Translate("photos", Now, limit: 25);

        Assert.Equal(25, translation.Query.Limit);
    }
}
