using DeskAI.Core.Tidy;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Remembers, per folder, the standing yes for tidying while away and the unattended runs.
/// Both cascade away with the folder. Nothing here touches a file.
/// </summary>
public interface IAwayTidyRepository
{
    Task<AwayTidyApproval?> FindAsync(Guid rootId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AwayTidyApproval>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces the folder's approval. Only the dialog's yes calls this.</summary>
    Task SaveAsync(AwayTidyApproval approval, CancellationToken cancellationToken = default);

    /// <summary>Turns the mode off by DeskAI's decision, keeping the reason for the folder to show.</summary>
    Task StopAsync(Guid rootId, DateTimeOffset stoppedAtUtc, string reason, CancellationToken cancellationToken = default);

    /// <summary>Turns the mode off by the person's decision. The row and its reason go.</summary>
    Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default);

    Task AppendRunAsync(AwayTidyRun run, CancellationToken cancellationToken = default);

    /// <summary>Runs the person has not looked at yet, newest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<AwayTidyRun>> ListUnseenRunsAsync(Guid rootId, int limit, CancellationToken cancellationToken = default);

    Task MarkRunsSeenAsync(Guid rootId, DateTimeOffset seenAtUtc, CancellationToken cancellationToken = default);
}
