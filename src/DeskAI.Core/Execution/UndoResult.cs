namespace DeskAI.Core.Execution;

public sealed record UndoResult(
    Guid UndoTransactionId,
    Guid OriginalTransactionId,
    IReadOnlyList<OperationExecutionResult> Operations,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);
