using DeskAI.Core.Execution;

namespace DeskAI.Core.Abstractions;

public interface IOperationJournal
{
    Task CreateAsync(ExecutionJournalEntry entry, CancellationToken cancellationToken = default);

    Task UpdateOperationAsync(
        Guid transactionId,
        Guid operationId,
        JournalOperationState state,
        string? failureMessage,
        CancellationToken cancellationToken = default);

    Task UpdateTransactionAsync(
        Guid transactionId,
        ExecutionTransactionState state,
        DateTimeOffset? finishedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ExecutionJournalEntry?> FindAsync(Guid transactionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionJournalEntry>> ListRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionJournalEntry>> ListIncompleteAsync(CancellationToken cancellationToken = default);
}
