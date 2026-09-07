namespace DeskAI.Core.Plans;

public sealed record OrganizationPlan
{
    private OrganizationPlan(
        Guid id,
        Guid rootId,
        int revision,
        DateTimeOffset createdAtUtc,
        string policyVersion,
        IReadOnlyList<PlanOperation> operations,
        IReadOnlyList<PlanIssue> issues,
        PlanState state)
    {
        Id = id;
        RootId = rootId;
        Revision = revision;
        CreatedAtUtc = createdAtUtc;
        PolicyVersion = policyVersion;
        Operations = operations;
        Issues = issues;
        State = state;
    }

    public Guid Id { get; }
    public Guid RootId { get; }
    public int Revision { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public string PolicyVersion { get; }
    public IReadOnlyList<PlanOperation> Operations { get; }
    public IReadOnlyList<PlanIssue> Issues { get; }
    public PlanState State { get; }

    public static OrganizationPlan CreateDraft(
        Guid id,
        Guid rootId,
        int revision,
        DateTimeOffset createdAtUtc,
        string policyVersion,
        IEnumerable<PlanOperation> operations,
        IEnumerable<PlanIssue>? issues = null)
    {
        if (id == Guid.Empty || rootId == Guid.Empty)
        {
            throw new ArgumentException("Plan and root IDs must be non-empty.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);

        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentNullException.ThrowIfNull(operations);

        var operationList = operations.ToArray();
        if (operationList.Any(operation => operation.Id == Guid.Empty))
        {
            throw new ArgumentException("Every operation needs a stable ID.", nameof(operations));
        }

        if (operationList.Select(operation => operation.Id).Distinct().Count() != operationList.Length)
        {
            throw new ArgumentException("Operation IDs must be unique within a plan.", nameof(operations));
        }

        var issueList = (issues ?? []).ToArray();
        var operationIds = operationList.Select(operation => operation.Id).ToHashSet();
        if (issueList.SelectMany(issue => issue.OperationIds).Any(id => !operationIds.Contains(id)))
        {
            throw new ArgumentException("Plan issues may reference only operations in the same plan.", nameof(issues));
        }

        return new OrganizationPlan(
            id,
            rootId,
            revision,
            createdAtUtc,
            policyVersion,
            Array.AsReadOnly(operationList),
            Array.AsReadOnly(issueList),
            PlanState.Draft);
    }
}

public enum PlanState
{
    Draft,
    Validated,
    Approved,
    Executing,
    Completed,
    PartiallyCompleted,
    Failed,
    Expired,
    Cancelled,
}
