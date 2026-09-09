using DeskAI.Core.Rules;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Stores the rules a person has written.
/// </summary>
/// <remarks>
/// <para>
/// A rule carries no folder. Like a saved search, it must not be able to outlive or widen an
/// authorization: which folders a rule could ever touch is resolved from the connected
/// folders each time it runs, never captured when it is written. A rule stored against a
/// folder would keep pointing at it after the folder was disconnected.
/// </para>
/// <para>
/// Storing a rule does not schedule it, and reading one does not run it. Nothing in this
/// contract moves a file.
/// </para>
/// </remarks>
public interface IRuleRepository
{
    Task<IReadOnlyList<AutomationRule>> ListAsync(CancellationToken cancellationToken = default);

    Task<AutomationRule?> FindAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Adds a rule, or replaces the stored copy of one that already exists.</summary>
    Task SaveAsync(AutomationRule rule, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default);
}
