using DeskAI.Core.Execution;
using DeskAI.Core.Plans;

namespace DeskAI.Core.Abstractions;

public interface IPlanExecutor
{
    Task<ExecutionResult> ExecuteAsync(
        OrganizationPlan plan,
        Approval approval,
        CancellationToken cancellationToken = default);
}
