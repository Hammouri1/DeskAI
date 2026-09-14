using DeskAI.Core.Rules;

namespace DeskAI.Core.Workspace;

/// <summary>One saved search a starter pack offers to add.</summary>
public sealed record StarterPackSearch(string Name, string Phrase);

/// <summary>
/// One rule a starter pack offers to add, described rather than built.
/// </summary>
/// <remarks>
/// Held as parts, not as an <see cref="AutomationRule"/>, because a rule needs an ID of its
/// own each time it is added, and because <see cref="ToRule"/> is then the single place a pack
/// rule becomes a real one — which is where it is switched off.
/// </remarks>
public sealed record StarterPackRule(string Name, IReadOnlyList<RuleCondition> Conditions, string Destination)
{
    /// <summary>
    /// Builds the rule, always switched off.
    /// </summary>
    /// <remarks>
    /// Goes through the same factory and destination checks as a rule typed on Automatic tasks,
    /// so a pack can never hold a rule a person could not have written. Switched off because a
    /// pack is a suggestion: the person decides which rules run, one switch at a time.
    /// </remarks>
    public AutomationRule ToRule(Guid id) =>
        AutomationRule.Create(id, Name, Conditions, new MoveToFolderAction(Destination), isEnabled: false);
}

/// <summary>
/// A named bundle of saved searches and rules for one kind of person.
/// </summary>
/// <remarks>
/// A pack is copied in once and then forgotten. DeskAI stores no "current profile": what a
/// pack adds becomes ordinary searches and rules the person can edit or delete like their own.
/// </remarks>
public sealed record StarterPack(
    string Id,
    string Name,
    string Summary,
    IReadOnlyList<StarterPackSearch> Searches,
    IReadOnlyList<StarterPackRule> Rules);
