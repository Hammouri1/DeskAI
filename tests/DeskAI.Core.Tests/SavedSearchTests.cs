using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

public sealed class SavedSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreateKeepsThePhraseAsTypedAndTrimsIt()
    {
        var saved = SavedSearch.Create(Guid.NewGuid(), "  University photos  ", "  photos from last month  ", Now);

        Assert.Equal("University photos", saved.Name);
        Assert.Equal("photos from last month", saved.Phrase);
        Assert.Equal(Now, saved.CreatedAtUtc);
    }

    [Fact]
    public void ASavedSearchNeedsAStableId()
    {
        Assert.Throws<ArgumentException>(() => SavedSearch.Create(Guid.Empty, "Name", "photos", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNameIsRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => SavedSearch.Create(Guid.NewGuid(), name, "photos", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPhraseIsRejected(string phrase)
    {
        Assert.Throws<ArgumentException>(() => SavedSearch.Create(Guid.NewGuid(), "Name", phrase, Now));
    }

    [Fact]
    public void AnOverLongNameIsRejected()
    {
        var name = new string('a', SavedSearch.MaxNameLength + 1);

        Assert.Throws<ArgumentException>(() => SavedSearch.Create(Guid.NewGuid(), name, "photos", Now));
    }

    /// <summary>
    /// The phrase limit matches the translator's, so a saved search can never hold a phrase
    /// the translator would then refuse to read.
    /// </summary>
    [Fact]
    public void APhraseTooLongForTheTranslatorIsRejected()
    {
        var phrase = new string('a', NaturalLanguageQueryTranslator.MaxInputLength + 1);

        Assert.Throws<ArgumentException>(() => SavedSearch.Create(Guid.NewGuid(), "Name", phrase, Now));
    }

    [Fact]
    public void ANewSavedSearchIsNotPinned()
    {
        var saved = SavedSearch.Create(Guid.NewGuid(), "Photos", "photos", Now);

        Assert.False(saved.IsPinned);
    }

    [Fact]
    public void PinningChangesOnlyThePin()
    {
        var saved = SavedSearch.Create(Guid.NewGuid(), "Photos", "photos", Now);

        var pinned = saved.WithPinned(true);

        Assert.True(pinned.IsPinned);
        Assert.Equal(saved.Id, pinned.Id);
        Assert.Equal(saved.Name, pinned.Name);
        Assert.Equal(saved.Phrase, pinned.Phrase);
    }

    /// <summary>
    /// Storing the phrase rather than a resolved query is what keeps a relative phrase
    /// relative. This pins that decision down: the saved value is still the words.
    /// </summary>
    [Fact]
    public void TheStoredValueIsThePhraseNotAResolvedQuery()
    {
        var saved = SavedSearch.Create(Guid.NewGuid(), "Recent photos", "photos from last month", Now);

        var early = NaturalLanguageQueryTranslator.Translate(saved.Phrase, Now);
        var later = NaturalLanguageQueryTranslator.Translate(saved.Phrase, Now.AddMonths(3));

        Assert.NotEqual(early.Query.ModifiedAfterUtc, later.Query.ModifiedAfterUtc);
    }
}
