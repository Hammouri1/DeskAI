using DeskAI.Core.Rules;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Keeps a short history of the checks that happened.
/// </summary>
/// <remarks>
/// Bounded on purpose. A check happens as often as every fifteen minutes and records nothing
/// anyone needs months later, so the store keeps a recent window and discards the rest.
/// An unbounded log of a thing that changes nothing would grow forever to no benefit — and
/// a longer record of when someone's folders were looked at is not a neutral thing to keep.
/// </remarks>
public interface IAutomaticCheckHistoryRepository
{
    /// <summary>How many runs are kept. Older ones are discarded as new ones arrive.</summary>
    const int MaximumRunsKept = 50;

    Task AppendAsync(AutomaticCheckRun run, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutomaticCheckRun>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets every recorded check.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
