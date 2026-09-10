namespace DeskAI.Core.Rules;

/// <summary>How one check ended.</summary>
public enum AutomaticCheckOutcome
{
    /// <summary>It ran to the end and reported what it found.</summary>
    Completed = 0,

    /// <summary>Someone paused, or DeskAI closed, while it was running.</summary>
    Stopped = 1,

    /// <summary>It could not finish. Nothing was changed by it either way.</summary>
    Failed = 2,
}

/// <summary>
/// One check that happened, kept so it can be looked back at.
/// </summary>
/// <remarks>
/// <para>
/// A run records what a check <em>found</em>, never what it did, because a check does
/// nothing. This is not the operation journal: the journal exists to make file changes
/// auditable and undoable, and there is no file change here to undo. Keeping the two apart
/// matters — a history of checks in the journal would suggest checks were operations.
/// </para>
/// <para>
/// <see cref="WasCatchUp"/> is how a missed period is reported honestly. DeskAI does not
/// replay checks it could not run while closed; it runs one and says the gap was longer than
/// usual, rather than silently presenting a stale-looking history as though it had been
/// watching all along.
/// </para>
/// </remarks>
public sealed record AutomaticCheckRun(
    Guid Id,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    AutomaticCheckOutcome Outcome,
    int FoldersChecked,
    int ProposalCount,
    int ConflictCount,
    bool WasCatchUp)
{
    /// <summary>What happened, in a sentence a person can read.</summary>
    public string Describe() => Outcome switch
    {
        AutomaticCheckOutcome.Stopped => "Stopped before it finished. Nothing was changed.",
        AutomaticCheckOutcome.Failed => "Could not finish. Nothing was changed.",
        _ when ProposalCount == 0 =>
            $"Looked at {Count(FoldersChecked, "folder")}. Nothing matched your rules.",
        _ => $"Looked at {Count(FoldersChecked, "folder")}. "
            + $"{Count(ProposalCount, "file")} matched your rules. Nothing was moved.",
    };

    /// <summary>Why this check happened later than the chosen frequency would suggest.</summary>
    public string? CatchUpNote => WasCatchUp
        ? "First check after DeskAI was closed or paused."
        : null;

    private static string Count(int value, string noun) => value == 1 ? $"1 {noun}" : $"{value} {noun}s";
}
