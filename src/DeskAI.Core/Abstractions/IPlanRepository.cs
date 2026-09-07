using DeskAI.Core.Plans;

namespace DeskAI.Core.Abstractions;

public interface IPlanRepository
{
    Task SaveAsync(OrganizationPlan plan, CancellationToken cancellationToken = default);

    Task<OrganizationPlan?> FindAsync(Guid planId, int revision, CancellationToken cancellationToken = default);
}
