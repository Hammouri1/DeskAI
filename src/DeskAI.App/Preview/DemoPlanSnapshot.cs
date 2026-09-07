using DeskAI.Core.Plans;
using DeskAI.Safety;
using DeskAI.Core.Roots;

namespace DeskAI.App.Preview;

public sealed record DemoPlanSnapshot(
    OrganizationPlan Plan,
    PlanValidationReport Validation,
    AuthorizedRoot Root);
