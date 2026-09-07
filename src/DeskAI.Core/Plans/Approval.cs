namespace DeskAI.Core.Plans;

public sealed record Approval(
    Guid Id,
    Guid PlanId,
    int PlanRevision,
    string PolicyVersion,
    IReadOnlySet<Guid> SelectedOperationIds,
    DateTimeOffset ApprovedAtUtc)
{
    public static Approval Create(
        Guid id,
        OrganizationPlan plan,
        IEnumerable<Guid> selectedOperationIds,
        DateTimeOffset approvedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selectedOperationIds);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An approval needs a stable ID.", nameof(id));
        }

        var selected = selectedOperationIds.ToHashSet();
        var known = plan.Operations.Select(operation => operation.Id).ToHashSet();
        if (!selected.IsSubsetOf(known))
        {
            throw new ArgumentException("Approval cannot include operations outside its plan.", nameof(selectedOperationIds));
        }

        return new Approval(id, plan.Id, plan.Revision, plan.PolicyVersion, selected, approvedAtUtc);
    }
}
