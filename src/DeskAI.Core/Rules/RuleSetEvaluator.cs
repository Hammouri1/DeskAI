namespace DeskAI.Core.Rules;

/// <summary>One file a set of rules agreed should move, and why.</summary>
/// <remarks>
/// <see cref="RuleNames"/> can name more than one rule: when several rules match a file and
/// all want the same destination there is nothing to resolve, and saying which rules agreed
/// is more useful than silently crediting the first.
/// </remarks>
public sealed record RuleProposal(
    string RelativePath,
    string DestinationRelativeDirectory,
    IReadOnlyList<Guid> RuleIds,
    IReadOnlyList<string> RuleNames,
    string Reason);

/// <summary>Two or more rules that want the same file in different places.</summary>
/// <remarks>
/// A conflict produces no proposal at all. DeskAI could pick the first rule, or the most
/// specific, or the most recently edited — and every one of those is a guess about what
/// someone meant. Guessing quietly is how automation moves a file somewhere its owner never
/// intended, so the file is left exactly where it is and the disagreement is reported.
/// </remarks>
public sealed record RuleConflict(
    string RelativePath,
    IReadOnlyList<Guid> RuleIds,
    IReadOnlyList<string> RuleNames,
    string Explanation);

/// <summary>
/// What a set of rules would do, worked out without touching anything.
/// </summary>
/// <remarks>
/// This is the same object whether it was produced to show someone a simulation or to
/// prepare a real run. A simulation that took a different path than the real thing would be
/// worth very little.
/// </remarks>
public sealed record RuleRunPreview(
    IReadOnlyList<RuleProposal> Proposals,
    IReadOnlyList<RuleConflict> Conflicts,
    int RulesApplied,
    int FilesConsidered,
    int AlreadyInPlace)
{
    public static RuleRunPreview Empty { get; } = new([], [], 0, 0, 0);

    public bool HasProposals => Proposals.Count > 0;

    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>
/// Works out what a set of rules would do to a set of files.
/// </summary>
/// <remarks>
/// <para>
/// A pure calculation: no clock, no filesystem, no database, no AI. It is handed the rules,
/// the files, and the moment, and it returns a description. Nothing here can move anything.
/// </para>
/// <para>
/// It deliberately returns proposals rather than plan operations. Turning a proposal into
/// something executable is a separate step that goes through the ordinary planner, safety
/// validation, preview, and approval, so there is no shortcut from "a rule matched" to "a
/// file moved".
/// </para>
/// <para>
/// The result does not depend on the order the rules are given in. A file two rules disagree
/// about is left alone whichever way round they arrive, because automation that behaves
/// differently depending on the order rules were written cannot be reasoned about.
/// </para>
/// </remarks>
public sealed class RuleSetEvaluator
{
    public RuleRunPreview Evaluate(
        IReadOnlyList<AutomationRule> rules,
        IReadOnlyList<RuleSubject> subjects,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(subjects);

        // A disabled rule is not a rule that runs. Excluding them here rather than at every
        // call site means "turned off" cannot be forgotten by one caller.
        var active = rules.Where(rule => rule.IsEnabled).ToArray();
        if (active.Length == 0 || subjects.Count == 0)
        {
            return RuleRunPreview.Empty with { RulesApplied = active.Length, FilesConsidered = subjects.Count };
        }

        var proposals = new List<RuleProposal>();
        var conflicts = new List<RuleConflict>();
        var alreadyInPlace = 0;

        foreach (var subject in subjects.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var matched = active.Where(rule => rule.Matches(subject, nowUtc)).ToArray();
            if (matched.Length == 0)
            {
                continue;
            }

            var destinations = matched
                .Select(rule => DestinationOf(rule))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (destinations.Length > 1)
            {
                conflicts.Add(new RuleConflict(
                    subject.RelativePath,
                    matched.Select(rule => rule.Id).ToArray().AsReadOnly(),
                    matched.Select(rule => rule.Name).ToArray().AsReadOnly(),
                    $"{matched.Length} rules want this file in different places, so it was left alone. "
                        + "Change one of them, or turn one off, and check again."));
                continue;
            }

            var destination = destinations[0];
            var currentDirectory = Path.GetDirectoryName(subject.RelativePath) ?? string.Empty;
            if (string.Equals(currentDirectory, destination, StringComparison.OrdinalIgnoreCase))
            {
                alreadyInPlace++;
                continue;
            }

            proposals.Add(new RuleProposal(
                subject.RelativePath,
                destination,
                matched.Select(rule => rule.Id).ToArray().AsReadOnly(),
                matched.Select(rule => rule.Name).ToArray().AsReadOnly(),
                Reason(matched)));
        }

        return new RuleRunPreview(
            proposals.AsReadOnly(),
            conflicts.AsReadOnly(),
            active.Length,
            subjects.Count,
            alreadyInPlace);
    }

    private static string DestinationOf(AutomationRule rule) => rule.Action switch
    {
        MoveToFolderAction move => move.DestinationRelativeDirectory,

        // Unreachable while moving is the only action, and deliberately loud rather than a
        // silent default: a new action type must be handled here, not quietly ignored.
        _ => throw new NotSupportedException($"No destination is defined for {rule.Action.GetType().Name}."),
    };

    private static string Reason(IReadOnlyList<AutomationRule> matched) => matched.Count == 1
        ? $"Rule \"{matched[0].Name}\": {matched[0].Describe()}"
        : $"{matched.Count} rules agree: {string.Join(", ", matched.Select(rule => $"\"{rule.Name}\""))}";
}
