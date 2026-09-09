using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

public sealed class RuleSetEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_ProposesAMoveForAFileARuleMatched()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var preview = RuleSetEvaluator.Evaluate([rule], [Subject("invoice-march.pdf")], Now);

        var proposal = Assert.Single(preview.Proposals);
        Assert.Equal("invoice-march.pdf", proposal.RelativePath);
        Assert.Equal("Documents", proposal.DestinationRelativeDirectory);
        Assert.Contains("Invoices", proposal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// DeskAI could pick the first rule, or the most specific one, and every such choice is
    /// a guess about what someone meant. Guessing quietly is how automation moves a file
    /// somewhere its owner never intended, so the file is left alone and the disagreement
    /// is reported instead.
    /// </summary>
    [Fact]
    public void Evaluate_LeavesAFileAloneWhenTwoRulesWantItInDifferentPlaces()
    {
        var toDocuments = Rule("By name", "Documents", new NameContainsCondition("invoice"));
        var toArchive = Rule("By type", "Archive", new ExtensionIsCondition(".pdf"));

        var preview = RuleSetEvaluator.Evaluate([toDocuments, toArchive], [Subject("invoice-march.pdf")], Now);

        Assert.Empty(preview.Proposals);
        var conflict = Assert.Single(preview.Conflicts);
        Assert.Equal("invoice-march.pdf", conflict.RelativePath);
        Assert.Equal(2, conflict.RuleIds.Count);
    }

    /// <summary>
    /// Automation that behaves differently depending on the order rules were written cannot
    /// be reasoned about, so the same set of rules must give the same answer either way.
    /// </summary>
    [Fact]
    public void Evaluate_GivesTheSameAnswerWhicheverOrderTheRulesArriveIn()
    {
        var first = Rule("By name", "Documents", new NameContainsCondition("invoice"));
        var second = Rule("By type", "Archive", new ExtensionIsCondition(".pdf"));
        var subjects = new[] { Subject("invoice-march.pdf") };

        var forwards = RuleSetEvaluator.Evaluate([first, second], subjects, Now);
        var backwards = RuleSetEvaluator.Evaluate([second, first], subjects, Now);

        Assert.Equal(forwards.Proposals.Count, backwards.Proposals.Count);
        Assert.Equal(forwards.Conflicts.Count, backwards.Conflicts.Count);
        Assert.Equal(
            forwards.Conflicts[0].RuleIds.OrderBy(id => id),
            backwards.Conflicts[0].RuleIds.OrderBy(id => id));
    }

    /// <summary>
    /// Rules that all want the same destination are not in conflict. There is nothing to
    /// resolve, and naming every rule that agreed is more useful than crediting the first.
    /// </summary>
    [Fact]
    public void Evaluate_TreatsRulesThatAgreeAsAgreementRatherThanConflict()
    {
        var byName = Rule("By name", "Documents", new NameContainsCondition("invoice"));
        var byType = Rule("By type", "Documents", new ExtensionIsCondition(".pdf"));

        var preview = RuleSetEvaluator.Evaluate([byName, byType], [Subject("invoice-march.pdf")], Now);

        Assert.Empty(preview.Conflicts);
        var proposal = Assert.Single(preview.Proposals);
        Assert.Equal(2, proposal.RuleNames.Count);
        Assert.Contains("agree", proposal.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ProposesNothingForAFileAlreadyWhereTheRuleWantsIt()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var preview = RuleSetEvaluator.Evaluate([rule], [Subject(@"Documents\invoice-march.pdf")], Now);

        Assert.Empty(preview.Proposals);
        Assert.Equal(1, preview.AlreadyInPlace);
    }

    /// <summary>
    /// A disabled rule is not a rule that runs. Excluding them in one place means no caller
    /// can forget that "turned off" was meant to stop it.
    /// </summary>
    [Fact]
    public void Evaluate_IgnoresRulesThatAreTurnedOff()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice")).WithEnabled(false);

        var preview = RuleSetEvaluator.Evaluate([rule], [Subject("invoice-march.pdf")], Now);

        Assert.Empty(preview.Proposals);
        Assert.Equal(0, preview.RulesApplied);
    }

    /// <summary>
    /// A file two rules disagree about must not become a proposal by the back door when one
    /// of the two is switched off — but it must become one again once the tie is broken.
    /// </summary>
    [Fact]
    public void Evaluate_ResolvesAConflictWhenOneOfTheRulesIsTurnedOff()
    {
        var toDocuments = Rule("By name", "Documents", new NameContainsCondition("invoice"));
        var toArchive = Rule("By type", "Archive", new ExtensionIsCondition(".pdf")).WithEnabled(false);

        var preview = RuleSetEvaluator.Evaluate([toDocuments, toArchive], [Subject("invoice-march.pdf")], Now);

        Assert.Empty(preview.Conflicts);
        Assert.Equal("Documents", Assert.Single(preview.Proposals).DestinationRelativeDirectory);
    }

    [Fact]
    public void Evaluate_ProposesNothingForFilesNoRuleMatched()
    {
        var rule = Rule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var preview = RuleSetEvaluator.Evaluate([rule], [Subject("holiday.png"), Subject("notes.md")], Now);

        Assert.Empty(preview.Proposals);
        Assert.Empty(preview.Conflicts);
        Assert.Equal(2, preview.FilesConsidered);
    }

    [Fact]
    public void Evaluate_ReportsNothingWhenThereAreNoRulesAtAll()
    {
        var preview = RuleSetEvaluator.Evaluate([], [Subject("invoice-march.pdf")], Now);

        Assert.False(preview.HasProposals);
        Assert.False(preview.HasConflicts);
    }

    /// <summary>
    /// The same rules against the same files must read identically every time, or a
    /// simulation could not be compared with the run that follows it.
    /// </summary>
    [Fact]
    public void Evaluate_ListsProposalsInAStableOrder()
    {
        var rule = Rule("Everything pdf", "Documents", new ExtensionIsCondition(".pdf"));
        var subjects = new[] { Subject("zeta.pdf"), Subject("alpha.pdf"), Subject("mid.pdf") };

        var preview = RuleSetEvaluator.Evaluate([rule], subjects, Now);

        Assert.Equal(
            ["alpha.pdf", "mid.pdf", "zeta.pdf"],
            preview.Proposals.Select(proposal => proposal.RelativePath));
    }

    private static AutomationRule Rule(string name, string destination, params RuleCondition[] conditions) =>
        AutomationRule.Create(Guid.NewGuid(), name, conditions, new MoveToFolderAction(destination));

    private static RuleSubject Subject(string relativePath) =>
        new(relativePath, FileCategory.Documents, FileKind.Document, 1_000, Now);
}
