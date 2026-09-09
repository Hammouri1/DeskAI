using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

/// <summary>
/// An approval is permission for one particular outcome, not a standing permission for
/// whatever the rules later become. Nearly every test here is a way that distinction could
/// be lost.
/// </summary>
public sealed class RuleApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Root = Guid.NewGuid();
    private readonly RuleSetEvaluator _evaluator = new();

    [Fact]
    public void Covers_AcceptsTheExactRunThatWasApproved()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var files = new[] { Subject("invoice-march.pdf") };
        var preview = _evaluator.Evaluate([rule], files, Now);
        var approval = Approve([rule], preview);

        var check = approval.Covers(Root, [rule], _evaluator.Evaluate([rule], files, Now));

        Assert.True(check.IsValid);
    }

    /// <summary>
    /// Editing a rule raises its version, and the approval given to the old wording stops
    /// applying. Otherwise someone could approve a narrow rule and then broaden it.
    /// </summary>
    [Fact]
    public void Covers_RefusesAfterARuleIsEdited()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var files = new[] { Subject("invoice-march.pdf") };
        var approval = Approve([rule], _evaluator.Evaluate([rule], files, Now));

        var broadened = rule.WithChanges(conditions: [new ExtensionIsCondition(".pdf")]);
        var check = approval.Covers(Root, [broadened], _evaluator.Evaluate([broadened], files, Now));

        Assert.False(check.IsValid);
        Assert.Equal(RuleApprovalStatus.RuleChanged, check.Status);
    }

    [Fact]
    public void Covers_RefusesWhenANewRuleHasAppeared()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var files = new[] { Subject("invoice-march.pdf"), Subject("holiday.png") };
        var approval = Approve([rule], _evaluator.Evaluate([rule], files, Now));

        var extra = Rule("Pictures", "Images", new ExtensionIsCondition(".png"));
        var check = approval.Covers(Root, [rule, extra], _evaluator.Evaluate([rule, extra], files, Now));

        Assert.Equal(RuleApprovalStatus.RuleAdded, check.Status);
    }

    [Fact]
    public void Covers_RefusesWhenAnApprovedRuleIsGone()
    {
        var kept = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var removed = Rule("Pictures", "Images", new ExtensionIsCondition(".png"));
        var files = new[] { Subject("invoice-march.pdf"), Subject("holiday.png") };
        var approval = Approve([kept, removed], _evaluator.Evaluate([kept, removed], files, Now));

        var check = approval.Covers(Root, [kept], _evaluator.Evaluate([kept], files, Now));

        Assert.Equal(RuleApprovalStatus.RuleRemoved, check.Status);
    }

    /// <summary>
    /// Turning a rule off is not a neutral act for an approval: fewer files move than were
    /// shown, which is still not what was agreed to.
    /// </summary>
    [Fact]
    public void Covers_RefusesWhenAnApprovedRuleIsTurnedOff()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var files = new[] { Subject("invoice-march.pdf") };
        var approval = Approve([rule], _evaluator.Evaluate([rule], files, Now));

        var disabled = rule.WithEnabled(false);
        var check = approval.Covers(Root, [disabled], _evaluator.Evaluate([disabled], files, Now));

        Assert.Equal(RuleApprovalStatus.RuleRemoved, check.Status);
    }

    /// <summary>
    /// The case that checking rule versions alone would miss. Nobody edited anything; a new
    /// file simply appeared, and the untouched rule now wants to move it. That is a move
    /// nobody agreed to, so it goes back to preview.
    /// </summary>
    [Fact]
    public void Covers_RefusesWhenTheSameRulesNowWantToMoveDifferentFiles()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var approval = Approve([rule], _evaluator.Evaluate([rule], [Subject("invoice-march.pdf")], Now));

        var laterFiles = new[] { Subject("invoice-march.pdf"), Subject("invoice-april.pdf") };
        var check = approval.Covers(Root, [rule], _evaluator.Evaluate([rule], laterFiles, Now));

        Assert.False(check.IsValid);
        Assert.Equal(RuleApprovalStatus.DifferentOutcome, check.Status);
    }

    [Fact]
    public void Covers_RefusesAnApprovalGivenForADifferentFolder()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var files = new[] { Subject("invoice-march.pdf") };
        var approval = Approve([rule], _evaluator.Evaluate([rule], files, Now));

        var check = approval.Covers(Guid.NewGuid(), [rule], _evaluator.Evaluate([rule], files, Now));

        Assert.Equal(RuleApprovalStatus.DifferentFolder, check.Status);
    }

    /// <summary>
    /// Renaming a rule changes nothing about what happens to anyone's files, so it must not
    /// force a person to approve the same moves again for no reason.
    /// </summary>
    [Fact]
    public void Fingerprint_IgnoresRuleNamesAndReasons()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var renamed = rule.WithChanges(name: "Bills");
        var files = new[] { Subject("invoice-march.pdf") };

        Assert.Equal(
            RuleApproval.Fingerprint(_evaluator.Evaluate([rule], files, Now)),
            RuleApproval.Fingerprint(_evaluator.Evaluate([renamed], files, Now)));
    }

    [Fact]
    public void Fingerprint_ChangesWhenAFileWouldGoSomewhereElse()
    {
        var toDocuments = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var toArchive = Rule("Invoices", "Archive", new NameContainsCondition("invoice"));
        var files = new[] { Subject("invoice-march.pdf") };

        Assert.NotEqual(
            RuleApproval.Fingerprint(_evaluator.Evaluate([toDocuments], files, Now)),
            RuleApproval.Fingerprint(_evaluator.Evaluate([toArchive], files, Now)));
    }

    /// <summary>
    /// Conflicts propose nothing, so a new disagreement between rules cannot cause a move
    /// nobody approved. It only means fewer files move than before.
    /// </summary>
    [Fact]
    public void Fingerprint_CoversProposalsRatherThanConflicts()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));
        var files = new[] { Subject("invoice-march.pdf"), Subject("holiday.png") };
        var withoutConflict = _evaluator.Evaluate([rule], files, Now);

        var competing = Rule("Pictures", "Images", new ExtensionIsCondition(".png"));
        var alsoPictures = Rule("Snaps", "Snapshots", new ExtensionIsCondition(".png"));
        var withConflict = _evaluator.Evaluate([rule, competing, alsoPictures], files, Now);

        Assert.True(withConflict.HasConflicts);
        Assert.Equal(
            RuleApproval.Fingerprint(withoutConflict),
            RuleApproval.Fingerprint(withConflict));
    }

    private static RuleApproval Approve(IReadOnlyList<AutomationRule> rules, RuleRunPreview preview) =>
        RuleApproval.Record(Guid.NewGuid(), Root, rules, preview, Now);

    private static AutomationRule Rule(string name, string destination, params RuleCondition[] conditions) =>
        AutomationRule.Create(Guid.NewGuid(), name, conditions, new MoveToFolderAction(destination));

    private static RuleSubject Subject(string relativePath) =>
        new(relativePath, FileCategory.Documents, FileKind.Document, 1_000, Now);
}
