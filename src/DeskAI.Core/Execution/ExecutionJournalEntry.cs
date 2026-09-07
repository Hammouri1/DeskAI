using DeskAI.Core.Plans;

namespace DeskAI.Core.Execution;

public sealed record ExecutionJournalEntry(
    Guid Id,
    Guid PlanId,
    int PlanRevision,
    Guid ApprovalId,
    ExecutionTransactionKind Kind,
    Guid? OriginalTransactionId,
    ExecutionTransactionState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    IReadOnlyList<OperationJournalEntry> Operations);

public sealed record OperationJournalEntry(
    int Sequence,
    Guid OperationId,
    PlanOperationKind Kind,
    string? SourceRelativePath,
    string DestinationRelativePath,
    long? BeforeSizeBytes,
    DateTimeOffset? BeforeModifiedAtUtc,
    JournalOperationState State,
    string? Error);

public enum ExecutionTransactionKind
{
    Execute,
    Undo,
}

public enum ExecutionTransactionState
{
    Prepared,
    Executing,
    Completed,
    PartiallyCompleted,
    Failed,
    Cancelled,
    RecoveryRequired,
    Undone,
}

public enum JournalOperationState
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled,
}
