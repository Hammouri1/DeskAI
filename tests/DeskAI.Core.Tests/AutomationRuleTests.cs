using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

public sealed class AutomationRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A rule with no conditions would match every file in the folder, which is exactly how
    /// a person accidentally moves everything they own. It is refused rather than quietly
    /// treated as "match all".
    /// </summary>
    [Fact]
    public void Create_RefusesARuleWithNoConditions()
    {
        var exception = Assert.Throws<ArgumentException>(() => AutomationRule.Create(
            Guid.NewGuid(),
            "Everything",
            [],
            new MoveToFolderAction("Sorted")));

        Assert.Contains("every file", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RefusesMoreConditionsThanAnyoneCanHoldInTheirHead()
    {
        var tooMany = Enumerable
            .Range(0, AutomationRule.MaxConditions + 1)
            .Select(index => new NameContainsCondition($"part{index}"))
            .ToArray();

        Assert.Throws<ArgumentException>(() => AutomationRule.Create(
            Guid.NewGuid(),
            "Overloaded",
            tooMany,
            new MoveToFolderAction("Sorted")));
    }

    /// <summary>
    /// Every condition must hold, never any of them. Someone who wants either case writes
    /// two rules and can then see and disable each one independently.
    /// </summary>
    [Fact]
    public void Matches_RequiresEveryConditionToHold()
    {
        var rule = Rule(
            new CategoryIsCondition(FileCategory.Images),
            new LargerThanCondition(1_000));

        Assert.True(rule.Matches(Subject("holiday.png", FileCategory.Images, 5_000), Now));
        Assert.False(rule.Matches(Subject("holiday.png", FileCategory.Images, 500), Now));
        Assert.False(rule.Matches(Subject("notes.txt", FileCategory.Documents, 5_000), Now));
    }

    [Fact]
    public void Matches_IgnoresCapitalisationInNames()
    {
        var rule = Rule(new NameContainsCondition("invoice"));

        Assert.True(rule.Matches(Subject("INVOICE-2026.pdf", FileCategory.Documents, 10), Now));
    }

    [Fact]
    public void ExtensionCondition_MatchesTheWholeEndingAndNotAPartOfTheName()
    {
        var rule = Rule(new ExtensionIsCondition("txt"));

        Assert.True(rule.Matches(Subject("notes.txt", FileCategory.Documents, 10), Now));
        Assert.False(rule.Matches(Subject("txt-notes.md", FileCategory.Documents, 10), Now));
    }

    /// <summary>
    /// A file ending, not a pattern and not a path. Accepting either would turn a
    /// deliberately narrow condition into something with reach nobody reviewed.
    /// </summary>
    [Theory]
    [InlineData("*.txt")]
    [InlineData(@"..\secrets")]
    [InlineData("C:")]
    [InlineData("tar.gz")]
    [InlineData(".")]
    public void ExtensionCondition_RefusesAnythingThatIsNotAPlainEnding(string extension)
    {
        Assert.Throws<ArgumentException>(() => new ExtensionIsCondition(extension));
    }

    /// <summary>
    /// The moment is passed in, so the same rule against the same files gives the same
    /// answer in a simulation as in a run. A rule whose meaning depends on when it happens
    /// to be evaluated cannot be simulated honestly.
    /// </summary>
    [Fact]
    public void AgeConditions_AnswerFromTheMomentTheyAreGiven()
    {
        var subject = new RuleSubject(
            "old.txt",
            FileCategory.Documents,
            FileKind.Document,
            10,
            Now.AddDays(-200));
        var older = new OlderThanCondition(TimeSpan.FromDays(180));
        var newer = new NewerThanCondition(TimeSpan.FromDays(180));

        Assert.True(older.Matches(subject, Now));
        Assert.False(newer.Matches(subject, Now));

        // The same file, judged a year earlier, is not old at all.
        Assert.False(older.Matches(subject, Now.AddDays(-190)));
    }

    /// <summary>
    /// A rule that is stored and re-run must never hold a destination that would be rejected
    /// every time it fires, so the shape is refused when the rule is written.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"..\..\elsewhere")]
    [InlineData(@"Sorted\..\..\escape")]
    [InlineData("Sorted*")]
    [InlineData("  ")]
    public void MoveAction_RefusesADestinationThatIsNotASimpleFolderInside(string destination)
    {
        Assert.Throws<ArgumentException>(() => new MoveToFolderAction(destination));
    }

    [Fact]
    public void MoveAction_NormalisesSeparatorsSoOneDestinationHasOneForm()
    {
        var action = new MoveToFolderAction("Documents/Invoices/");

        Assert.Equal(Path.Combine("Documents", "Invoices"), action.DestinationRelativeDirectory);
    }

    /// <summary>
    /// The sentence is built from the same objects that decide the behaviour, so it cannot
    /// drift away from what the rule actually does.
    /// </summary>
    [Fact]
    public void Describe_ReadsAsOnePlainSentence()
    {
        var rule = AutomationRule.Create(
            Guid.NewGuid(),
            "Invoices",
            [new NameContainsCondition("invoice"), new ExtensionIsCondition(".pdf")],
            new MoveToFolderAction("Documents"));

        Assert.Equal(
            "When the name contains \"invoice\", and it is a .pdf file, move it into Documents.",
            rule.Describe());
    }

    /// <summary>
    /// Editing always produces a new version, so an approval given to the old wording stops
    /// applying to the new one.
    /// </summary>
    [Fact]
    public void WithChanges_RaisesTheVersion()
    {
        var rule = Rule(new NameContainsCondition("invoice"));

        var edited = rule.WithChanges(action: new MoveToFolderAction("Elsewhere"));

        Assert.Equal(rule.Version + 1, edited.Version);
        Assert.Equal(rule.Id, edited.Id);
    }

    /// <summary>
    /// Turning a rule off changes whether it runs, not what it would do, so it must not
    /// invalidate an approval by bumping the version.
    /// </summary>
    [Fact]
    public void WithEnabled_DoesNotChangeTheVersion()
    {
        var rule = Rule(new NameContainsCondition("invoice"));

        var disabled = rule.WithEnabled(false);

        Assert.False(disabled.IsEnabled);
        Assert.Equal(rule.Version, disabled.Version);
    }

    /// <summary>
    /// A rule reads what DeskAI remembered about a file: its name, kind, size, and date.
    /// It is never handed a path on the disk or anything from inside the file.
    /// </summary>
    [Fact]
    public void Subject_DescribesAFileWithoutItsLocationOrContents()
    {
        var file = new FileItem(
            Guid.NewGuid(),
            @"Invoices\march.pdf",
            FileKind.Document,
            2_048,
            Now.AddDays(-10),
            Now.AddDays(-5));
        var classification = new Core.Classification.Classification(
            FileCategory.Documents,
            FileKind.Document,
            ClassificationSource.Rule,
            1,
            "Known document extension");

        var subject = RuleSubject.From(file, classification);

        Assert.Equal("march.pdf", subject.Name);
        Assert.Equal(".pdf", subject.Extension);
        Assert.Equal(FileCategory.Documents, subject.Category);
        Assert.Equal(2_048, subject.SizeBytes);
    }

    private static AutomationRule Rule(params RuleCondition[] conditions) =>
        AutomationRule.Create(Guid.NewGuid(), "Test rule", conditions, new MoveToFolderAction("Sorted"));

    private static RuleSubject Subject(string relativePath, FileCategory category, long sizeBytes) =>
        new(relativePath, category, FileKind.Document, sizeBytes, Now);
}
