using System.Security.Cryptography;
using System.Text;

namespace DeskAI.Core.Rules;

/// <summary>One rule as it was worded when someone approved it.</summary>
public sealed record ApprovedRuleVersion(Guid RuleId, int Version);

/// <summary>Why a stored approval no longer covers what is about to happen.</summary>
public enum RuleApprovalStatus
{
    Valid,

    /// <summary>A rule was edited, so it no longer does what was approved.</summary>
    RuleChanged,

    /// <summary>A rule now runs that was not part of what was approved.</summary>
    RuleAdded,

    /// <summary>A rule that was approved is gone or turned off.</summary>
    RuleRemoved,

    /// <summary>The approval belongs to a different folder.</summary>
    DifferentFolder,

    /// <summary>The same rules now want to move different files.</summary>
    DifferentOutcome,
}

/// <summary>The answer to "does this approval still cover this run?", with a reason.</summary>
public sealed record RuleApprovalCheck(RuleApprovalStatus Status, string Explanation)
{
    public bool IsValid => Status == RuleApprovalStatus.Valid;
}

/// <summary>
/// Permission to carry out one particular set of rule proposals, for one folder.
/// </summary>
/// <remarks>
/// <para>
/// An approval is given to rules <em>as they were worded</em> and to an outcome <em>as it
/// was shown</em>. It is not a standing permission for whatever those rules become. Editing
/// a rule raises its version and this stops applying; adding a rule, removing one, changing
/// folder, or the same rules wanting to move different files all do the same.
/// </para>
/// <para>
/// That last one is the point of <see cref="OutcomeFingerprint"/>. Rules can be untouched
/// and still produce something nobody agreed to, simply because the files changed — a new
/// download appears and suddenly a rule wants to move it. Checking only the rules would let
/// that through as "already approved". A run whose proposals differ from the ones shown goes
/// back to preview, which is the safe default the roadmap asks for: risky or novel outcomes
/// return to preview.
/// </para>
/// <para>
/// The fingerprint covers proposals only. Conflicts propose nothing, so a new disagreement
/// between rules cannot cause an unapproved move; it simply means fewer files move than
/// before.
/// </para>
/// </remarks>
public sealed record RuleApproval
{
    private RuleApproval(
        Guid id,
        Guid rootId,
        IReadOnlyList<ApprovedRuleVersion> rules,
        string outcomeFingerprint,
        DateTimeOffset approvedAtUtc)
    {
        Id = id;
        RootId = rootId;
        Rules = rules;
        OutcomeFingerprint = outcomeFingerprint;
        ApprovedAtUtc = approvedAtUtc;
    }

    public Guid Id { get; }

    public Guid RootId { get; }

    /// <summary>Every rule that ran, at the version it was when approved.</summary>
    public IReadOnlyList<ApprovedRuleVersion> Rules { get; }

    /// <summary>A fingerprint of the exact moves that were shown and agreed to.</summary>
    public string OutcomeFingerprint { get; }

    public DateTimeOffset ApprovedAtUtc { get; }

    /// <summary>
    /// Records approval of exactly what <paramref name="preview"/> described.
    /// </summary>
    /// <remarks>
    /// The enabled rules are captured from the same list the preview was produced from, so
    /// an approval cannot be recorded against a different set than the one that was shown.
    /// </remarks>
    public static RuleApproval Record(
        Guid id,
        Guid rootId,
        IReadOnlyList<AutomationRule> rules,
        RuleRunPreview preview,
        DateTimeOffset approvedAtUtc)
    {
        if (id == Guid.Empty || rootId == Guid.Empty)
        {
            throw new ArgumentException("An approval needs a stable ID and a folder.");
        }

        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(preview);

        var approved = rules
            .Where(rule => rule.IsEnabled)
            .Select(rule => new ApprovedRuleVersion(rule.Id, rule.Version))
            .OrderBy(rule => rule.RuleId)
            .ToArray();

        return new RuleApproval(id, rootId, approved.AsReadOnly(), Fingerprint(preview), approvedAtUtc);
    }

    /// <summary>
    /// Decides whether this approval still covers a run, and says why when it does not.
    /// </summary>
    /// <remarks>
    /// The reasons are separate values rather than one "invalid", because "you edited a
    /// rule" and "there are new files to move" need different words in front of a person.
    /// </remarks>
    public RuleApprovalCheck Covers(
        Guid rootId,
        IReadOnlyList<AutomationRule> rules,
        RuleRunPreview preview)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(preview);

        if (rootId != RootId)
        {
            return new RuleApprovalCheck(
                RuleApprovalStatus.DifferentFolder,
                "That approval was given for a different folder.");
        }

        var active = rules
            .Where(rule => rule.IsEnabled)
            .ToDictionary(rule => rule.Id, rule => rule.Version);
        var approved = Rules.ToDictionary(rule => rule.RuleId, rule => rule.Version);

        foreach (var (ruleId, version) in approved)
        {
            if (!active.TryGetValue(ruleId, out var currentVersion))
            {
                return new RuleApprovalCheck(
                    RuleApprovalStatus.RuleRemoved,
                    "A rule you approved has been removed or turned off, so this needs checking again.");
            }

            if (currentVersion != version)
            {
                return new RuleApprovalCheck(
                    RuleApprovalStatus.RuleChanged,
                    "A rule has been edited since you approved it, so this needs checking again.");
            }
        }

        if (active.Keys.Any(ruleId => !approved.ContainsKey(ruleId)))
        {
            return new RuleApprovalCheck(
                RuleApprovalStatus.RuleAdded,
                "There is a new rule that was not part of what you approved, so this needs checking again.");
        }

        if (!string.Equals(Fingerprint(preview), OutcomeFingerprint, StringComparison.Ordinal))
        {
            return new RuleApprovalCheck(
                RuleApprovalStatus.DifferentOutcome,
                "These rules now want to move different files than the ones you saw, "
                    + "so this needs checking again.");
        }

        return new RuleApprovalCheck(RuleApprovalStatus.Valid, "This is exactly what you approved.");
    }

    /// <summary>
    /// A stable fingerprint of the moves a preview proposes.
    /// </summary>
    /// <remarks>
    /// Built from the ordered list of "this file goes there" pairs, so it changes when a
    /// file is added, removed, or sent somewhere else, and does not change merely because
    /// the same work was recalculated. Rule names and reasons are excluded: renaming a rule
    /// does not change what happens to anyone's files.
    /// </remarks>
    public static string Fingerprint(RuleRunPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var canonical = string.Join(
            "\n",
            preview.Proposals
                .Select(proposal => $"{proposal.RelativePath}=>{proposal.DestinationRelativeDirectory}")
                .OrderBy(line => line, StringComparer.OrdinalIgnoreCase));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
