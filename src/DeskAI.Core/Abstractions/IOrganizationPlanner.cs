using DeskAI.Core.Plans;

namespace DeskAI.Core.Abstractions;

public interface IOrganizationPlanner
{
    OrganizationPlan CreatePlan(OrganizationPlanningRequest request);
}
