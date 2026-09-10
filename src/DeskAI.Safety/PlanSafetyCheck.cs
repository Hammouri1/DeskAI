using DeskAI.Core.Abstractions;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Safety;

/// <summary>The safety policy, answered through the Core contract.</summary>
public sealed class PlanSafetyCheck(PlanValidator validator, IPathPolicy pathPolicy) : IPlanSafetyCheck
{
    public string PolicyVersion => PlanValidator.CurrentPolicyVersion;

    public IReadOnlyDictionary<Guid, string> FindBlocked(OrganizationPlan plan, AuthorizedRoot root)
    {
        var blocked = new Dictionary<Guid, string>();
        foreach (var item in validator.Validate(plan, root).Operations
                     .Where(item => item.Result.Status == ValidationStatus.Blocked))
        {
            blocked.TryAdd(item.OperationId, item.Result.Explanation);
        }

        return blocked;
    }

    public bool IsProtected(AuthorizedRoot root, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(root);
        return pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked ||
            pathPolicy.ValidateRelativePath(root, relativePath).Status == ValidationStatus.Blocked;
    }
}
