namespace DeskAI.Core.Rules;

/// <summary>
/// One stored "when this, do that", written by a person and applied deterministically.
/// </summary>
/// <remarks>
/// <para>
/// Every condition must match, never any of them. An OR rule is far harder to predict, and a
/// rule someone cannot predict is one they cannot safely approve. Someone who wants either
/// case writes two rules, and can then see and disable each independently.
/// </para>
/// <para>
/// A rule with no conditions would match every file in the folder, which is precisely how a
/// person accidentally moves everything they own. It is refused rather than treated as
/// "match all".
/// </para>
/// <para>
/// A rule proposes and never acts. Matching produces a description of what would happen; the
/// resulting plan is validated, previewed, and approved like any other.
/// </para>
/// </remarks>
public sealed record AutomationRule
{
    public const int MaxNameLength = 60;

    /// <summary>
    /// How many conditions one rule may carry.
    /// </summary>
    /// <remarks>
    /// Not a storage limit. Past a handful of conditions nobody can hold what a rule does in
    /// their head, and a rule that cannot be understood cannot be meaningfully approved.
    /// </remarks>
    public const int MaxConditions = 8;

    private AutomationRule(
        Guid id,
        string name,
        int version,
        IReadOnlyList<RuleCondition> conditions,
        RuleAction action,
        bool isEnabled)
    {
        Id = id;
        Name = name;
        Version = version;
        Conditions = conditions;
        Action = action;
        IsEnabled = isEnabled;
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>
    /// Rises every time the rule's behaviour changes.
    /// </summary>
    /// <remarks>
    /// An approval is given to a rule as it was worded at the time. The version is what lets
    /// a stored approval notice that the rule has since been edited, instead of quietly
    /// carrying permission over to behaviour nobody agreed to.
    /// </remarks>
    public int Version { get; }

    public IReadOnlyList<RuleCondition> Conditions { get; }

    public RuleAction Action { get; }

    public bool IsEnabled { get; }

    public static AutomationRule Create(
        Guid id,
        string name,
        IEnumerable<RuleCondition> conditions,
        RuleAction action,
        int version = 1,
        bool isEnabled = true)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A rule needs a stable ID.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"A rule name must be {MaxNameLength} characters or fewer.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        var list = conditions.ToArray();
        if (list.Length == 0)
        {
            throw new ArgumentException(
                "A rule needs at least one condition, or it would match every file.",
                nameof(conditions));
        }

        if (list.Length > MaxConditions)
        {
            throw new ArgumentException(
                $"A rule may have at most {MaxConditions} conditions.",
                nameof(conditions));
        }

        if (list.Any(condition => condition is null))
        {
            throw new ArgumentException("A rule cannot hold an empty condition.", nameof(conditions));
        }

        return new AutomationRule(id, name.Trim(), version, Array.AsReadOnly(list), action, isEnabled);
    }

    /// <summary>True when every condition holds for this file.</summary>
    public bool Matches(RuleSubject subject, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return Conditions.All(condition => condition.Matches(subject, nowUtc));
    }

    /// <summary>
    /// The rule in one plain sentence, for review, approval, and the run history.
    /// </summary>
    /// <remarks>
    /// Written from the same objects that decide the behaviour, so the sentence cannot drift
    /// away from what the rule actually does the way a separately stored description would.
    /// </remarks>
    public string Describe() =>
        $"When {string.Join(", and ", Conditions.Select(condition => condition.Describe()))}, "
        + $"{Action.Describe()}.";

    /// <summary>
    /// Returns this rule with changed behaviour and a raised version.
    /// </summary>
    /// <remarks>
    /// Editing always produces a new version, so any approval given to the old wording stops
    /// applying. Enabling or disabling goes through <see cref="WithEnabled"/> instead,
    /// because turning a rule off changes whether it runs, not what it would do.
    /// </remarks>
    public AutomationRule WithChanges(
        string? name = null,
        IEnumerable<RuleCondition>? conditions = null,
        RuleAction? action = null) =>
        Create(
            Id,
            name ?? Name,
            conditions ?? Conditions,
            action ?? Action,
            Version + 1,
            IsEnabled);

    public AutomationRule WithEnabled(bool isEnabled) =>
        new(Id, Name, Version, Conditions, Action, isEnabled);
}
