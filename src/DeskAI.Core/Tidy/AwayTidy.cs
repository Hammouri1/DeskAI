using DeskAI.Core.Rules;

namespace DeskAI.Core.Tidy;

/// <summary>The hard ceiling on what may happen while nobody is watching. Decided before code (ADR 0031).</summary>
public static class AwayTidyLimits
{
    /// <summary>The most files one unattended run may move.</summary>
    public const int MaxFilesPerRun = 25;

    /// <summary>How many unseen runs the "While you were away" card lists.</summary>
    public const int MaxRunsShown = 5;
}

/// <summary>
/// A person's standing yes for one folder: "move what these rules, exactly as worded now,
/// place — and nothing else — while I'm away".
/// </summary>
/// <remarks>
/// The same shape as ADR 0016's rule approval — the folder and every enabled rule at its
/// version — without an outcome fingerprint, because an outcome that changes as files arrive is
/// the point. It is stored in its own table, cascade-erased with the folder, and never written
/// by anything but the dialog's yes. A row with a <see cref="StoppedReason"/> is the switch
/// turned off by DeskAI, kept so the folder can say why.
/// </remarks>
public sealed record AwayTidyApproval(
    Guid RootId,
    IReadOnlyList<ApprovedRuleVersion> Rules,
    DateTimeOffset ApprovedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string? StoppedReason)
{
    public bool IsActive => StoppedReason is null;

    public static AwayTidyApproval Record(Guid rootId, IReadOnlyList<AutomationRule> rules, DateTimeOffset approvedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rootId == Guid.Empty)
        {
            throw new ArgumentException("An approval needs a folder.", nameof(rootId));
        }

        var approved = rules
            .Where(rule => rule.IsEnabled)
            .Select(rule => new ApprovedRuleVersion(rule.Id, rule.Version))
            .OrderBy(rule => rule.RuleId)
            .ToArray();
        return new AwayTidyApproval(rootId, Array.AsReadOnly(approved), approvedAtUtc, null, null);
    }

    /// <summary>
    /// Whether the rules as they are now are exactly the rules that were agreed to.
    /// </summary>
    /// <remarks>
    /// Any edit, addition, removal, or on/off change fails. This is checked before every run and
    /// whenever the folder is shown, and a failure turns the mode off with the reason.
    /// </remarks>
    public RuleApprovalCheck Covers(IReadOnlyList<AutomationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var active = rules.Where(rule => rule.IsEnabled).ToDictionary(rule => rule.Id, rule => rule.Version);
        var approved = Rules.ToDictionary(rule => rule.RuleId, rule => rule.Version);

        foreach (var (ruleId, version) in approved)
        {
            if (!active.TryGetValue(ruleId, out var current))
            {
                return new RuleApprovalCheck(RuleApprovalStatus.RuleRemoved, "A rule you agreed to has been removed or turned off since you agreed.");
            }

            if (current != version)
            {
                return new RuleApprovalCheck(RuleApprovalStatus.RuleChanged, "A rule has been edited since you agreed.");
            }
        }

        if (active.Keys.Any(ruleId => !approved.ContainsKey(ruleId)))
        {
            return new RuleApprovalCheck(RuleApprovalStatus.RuleAdded, "A rule was added or turned on since you agreed.");
        }

        return new RuleApprovalCheck(RuleApprovalStatus.Valid, "These are exactly the rules you agreed to.");
    }
}

/// <summary>One unattended run, as the folder shows it. Never names a file.</summary>
public sealed record AwayTidyRun(
    Guid Id,
    Guid RootId,
    Guid? TransactionId,
    DateTimeOffset RanAtUtc,
    int Moved,
    int FoldersUsed,
    int Skipped,
    string? StoppedReason,
    DateTimeOffset? SeenAtUtc);

/// <summary>What the switch on Organize shows for one folder.</summary>
public sealed record AwayTidyStatus(bool IsOn, bool CanTurnOn, string Line, bool IsCaution, int RuleCount);

/// <summary>
/// The one thing an automatic check may hand a folder to that can move a file. Implemented by
/// <see cref="AwayTidyService"/> alone; a test asserts no other type implements it.
/// </summary>
public interface IAwayTidyRunner
{
    Task<AwayTidySummary> RunAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>What one pass over every folder with the mode on did, for the notice.</summary>
public sealed record AwayTidySummary(int FoldersTidied, int FilesMoved, string? FolderName, Guid? FolderToReview, int FoldersStopped)
{
    public bool MovedAnything => FilesMoved > 0;
}
