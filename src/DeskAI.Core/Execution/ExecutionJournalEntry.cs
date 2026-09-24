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
    IReadOnlyList<OperationJournalEntry> Operations)
{
    /// <summary>Which feature made the plan behind this record (ADR 0044).</summary>
    public PlanPurpose Purpose { get; init; }
}

public sealed record OperationJournalEntry(
    int Sequence,
    Guid OperationId,
    PlanOperationKind Kind,
    string? SourceRelativePath,
    string DestinationRelativePath,
    long? BeforeSizeBytes,
    DateTimeOffset? BeforeModifiedAtUtc,
    JournalOperationState State,
    string? Error)
{
    /// <summary>For a moved folder: when it was made, which a move keeps (ADR 0044). Null for files.</summary>
    public DateTimeOffset? BeforeCreatedAtUtc { get; init; }
}

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

    /// <summary>
    /// DeskAI stopped while this file was moving, and the disk did not prove whether it had
    /// moved. It is never undone or moved again on a guess; the person is asked to look.
    /// Appended last because states are stored as integers.
    /// </summary>
    NeedsReview,
}
