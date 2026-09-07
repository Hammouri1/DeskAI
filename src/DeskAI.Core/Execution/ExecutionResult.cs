namespace DeskAI.Core.Execution;

public sealed record OperationExecutionResult(Guid OperationId, ExecutionOutcome Outcome, string? Error);

public sealed record ExecutionResult(
    Guid TransactionId,
    Guid PlanId,
    IReadOnlyList<OperationExecutionResult> Operations,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);

public enum ExecutionOutcome
{
    Completed,
    Failed,
    Skipped,
    Cancelled,
}
