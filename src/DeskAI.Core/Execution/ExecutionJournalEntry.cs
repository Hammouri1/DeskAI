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

    /// <summary>
    /// A folder the plan needed already existed, so nothing was created. Undo must leave it
    /// alone: it belonged to the person before DeskAI ran. Appended last because states are
    /// stored as integers.
    /// </summary>
    AlreadyPresent,
}
