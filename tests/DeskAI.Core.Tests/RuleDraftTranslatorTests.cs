using DeskAI.Core.Classification;
using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

/// <summary>
/// A draft is a suggestion for review, never a saved rule. These tests care as much about
/// what is reported as unread as about what is understood.
/// </summary>
public sealed class RuleDraftTranslatorTests
{
    [Fact]
    public void Draft_ReadsASimpleSentenceIntoAConditionAndADestination()
    {
        var draft = RuleDraftTranslator.Draft("move invoices to Documents");

        var condition = Assert.IsType<NameContainsCondition>(Assert.Single(draft.Conditions));
        Assert.Equal("invoice", condition.Text);
        Assert.Equal("Documents", draft.Action!.DestinationRelativeDirectory);
        Assert.True(draft.IsComplete);
    }

    /// <summary>
    /// "move invoices" should find "invoice-march.pdf". Trimming the plural is a guess,
    /// which is exactly why it becomes a chip the person can correct.
    /// </summary>
    [Fact]
    public void Draft_TrimsAPluralSoTheWordStillMatchesRealFileNames()
    {
        var draft = RuleDraftTranslator.Draft("put screenshots in Pictures");

        Assert.Contains(draft.Chips, chip => chip.Label.Contains("Screenshots", StringComparison.Ordinal));
    }

    [Fact]
    public void Draft_ReadsAFileEnding()
    {
        var draft = RuleDraftTranslator.Draft("move pdf files into Documents");

        var condition = Assert.IsType<ExtensionIsCondition>(Assert.Single(draft.Conditions));
        Assert.Equal(".pdf", condition.Extension);
    }

    [Fact]
    public void Draft_ReadsACategoryWord()
    {
        var draft = RuleDraftTranslator.Draft("move photos to Images");

        var condition = Assert.IsType<CategoryIsCondition>(Assert.Single(draft.Conditions));
        Assert.Equal(FileCategory.Images, condition.Category);
    }

    [Fact]
    public void Draft_ReadsASizeLimit()
    {
        var draft = RuleDraftTranslator.Draft("move videos bigger than 100 mb to Archive");

        var size = Assert.Single(draft.Conditions.OfType<LargerThanCondition>());
        Assert.Equal(100L * 1024 * 1024, size.SizeBytes);
    }

    [Fact]
    public void Draft_ReadsAnAge()
    {
        var draft = RuleDraftTranslator.Draft("move documents not changed in 6 months to Archive");

        var age = Assert.Single(draft.Conditions.OfType<OlderThanCondition>());
        Assert.Equal(TimeSpan.FromDays(180), age.Age);
    }

    /// <summary>
    /// "in" is a destination far less often than "to" or "into" — "files in 2026" is usually
    /// part of a name — so the clearer markers win when both appear.
    /// </summary>
    [Fact]
    public void Draft_PrefersTheClearerDestinationWord()
    {
        var draft = RuleDraftTranslator.Draft("move invoices in 2026 to Documents");

        Assert.Equal("Documents", draft.Action!.DestinationRelativeDirectory);
    }

    [Fact]
    public void Draft_KeepsTheCapitalsSomeoneTypedInAFolderName()
    {
        var draft = RuleDraftTranslator.Draft("move invoices to Bank Statements");

        Assert.Equal("Bank Statements", draft.Action!.DestinationRelativeDirectory);
    }

    /// <summary>
    /// A folder name DeskAI cannot use is something to show the person, not an error to
    /// throw at them, so the draft comes back with the problem written down.
    /// </summary>
    [Theory]
    [InlineData(@"move invoices to C:\Windows")]
    [InlineData(@"move invoices to ..\..\elsewhere")]
    public void Draft_ReportsADestinationItCannotUseRatherThanThrowing(string sentence)
    {
        var draft = RuleDraftTranslator.Draft(sentence);

        Assert.Null(draft.Action);
        Assert.NotNull(draft.DestinationProblem);
        Assert.False(draft.IsComplete);
    }

    /// <summary>
    /// Half a rule someone can finish is more useful than a refusal, so a sentence with no
    /// destination still drafts — but it is not complete, and must not be saved for them.
    /// </summary>
    [Fact]
    public void Draft_IsIncompleteWithoutADestination()
    {
        var draft = RuleDraftTranslator.Draft("invoices");

        Assert.NotEmpty(draft.Conditions);
        Assert.Null(draft.Action);
        Assert.False(draft.IsComplete);
    }

    [Fact]
    public void Draft_IsIncompleteWithoutAnythingToLookFor()
    {
        var draft = RuleDraftTranslator.Draft("move everything to Documents");

        Assert.Empty(draft.Conditions);
        Assert.NotNull(draft.Action);
        Assert.False(draft.IsComplete);
    }

    /// <summary>
    /// Without this, "move invoices" would look for files with "move" in the name.
    /// </summary>
    [Fact]
    public void Draft_DoesNotTreatInstructionWordsAsSomethingToLookFor()
    {
        var draft = RuleDraftTranslator.Draft("please move all my invoice files to Documents");

        var condition = Assert.IsType<NameContainsCondition>(Assert.Single(draft.Conditions));
        Assert.Equal("invoice", condition.Text);
    }

    [Fact]
    public void Draft_ReportsUnderstandingNothingForASentenceItCannotRead()
    {
        var draft = RuleDraftTranslator.Draft("   ");

        Assert.False(draft.UnderstoodAnything);
        Assert.False(draft.IsComplete);
    }

    /// <summary>
    /// Every part understood becomes a chip, so a person can see the reading and correct it
    /// rather than trusting that DeskAI guessed right.
    /// </summary>
    [Fact]
    public void Draft_ExplainsEveryPartItUnderstood()
    {
        var draft = RuleDraftTranslator.Draft("move pdf files bigger than 5 mb to Archive");

        Assert.Contains(draft.Chips, chip => chip.Label.Contains(".pdf", StringComparison.Ordinal));
        Assert.Contains(draft.Chips, chip => chip.Label.Contains("Bigger than", StringComparison.Ordinal));
        Assert.Contains(draft.Chips, chip => chip.Label.Contains("Archive", StringComparison.Ordinal));
    }

    [Fact]
    public void Draft_RefusesASentenceLongEnoughToBeAPastedDocument()
    {
        var tooLong = new string('a', RuleDraftTranslator.MaxInputLength + 1);

        Assert.Throws<ArgumentException>(() => RuleDraftTranslator.Draft(tooLong));
    }

    /// <summary>
    /// The same sentence must always draft the same rule, or a person could not trust what
    /// they reviewed a moment ago.
    /// </summary>
    [Fact]
    public void Draft_ReadsTheSameSentenceTheSameWayEveryTime()
    {
        const string Sentence = "move invoice pdf files bigger than 2 mb to Documents";

        var first = RuleDraftTranslator.Draft(Sentence);
        var second = RuleDraftTranslator.Draft(Sentence);

        Assert.Equal(first.Conditions, second.Conditions);
        Assert.Equal(first.Action, second.Action);
    }

    /// <summary>
    /// A draft never becomes a rule on its own, but everything it produces must be usable by
    /// the real thing — otherwise the form would be filled with something unsavable.
    /// </summary>
    [Fact]
    public void Draft_ProducesPartsARealRuleAccepts()
    {
        var draft = RuleDraftTranslator.Draft("move invoice pdf files to Documents");

        var rule = AutomationRule.Create(Guid.NewGuid(), "Invoices", draft.Conditions, draft.Action!);

        Assert.True(rule.Conditions.Count >= 1);
        Assert.Contains("Documents", rule.Describe(), StringComparison.Ordinal);
    }
}
