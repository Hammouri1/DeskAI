namespace DeskAI.Core.Plans;

public sealed record PlanIssue
{
    public PlanIssue(
        PlanIssueCode code,
        PlanIssueSeverity severity,
        string explanation,
        IEnumerable<Guid> fileIds,
        IEnumerable<Guid> operationIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        ArgumentNullException.ThrowIfNull(fileIds);
        ArgumentNullException.ThrowIfNull(operationIds);

        Code = code;
        Severity = severity;
        Explanation = explanation;
        FileIds = Array.AsReadOnly(fileIds.Distinct().ToArray());
        OperationIds = Array.AsReadOnly(operationIds.Distinct().ToArray());
    }

    public PlanIssueCode Code { get; }
    public PlanIssueSeverity Severity { get; }
    public string Explanation { get; }
    public IReadOnlyList<Guid> FileIds { get; }
    public IReadOnlyList<Guid> OperationIds { get; }
}

public enum PlanIssueSeverity
{
    Information,
    Warning,
    Conflict,
}

public enum PlanIssueCode
{
    UnclassifiedFile,
    NoRecipeDestination,
    AlreadyOrganized,
    DuplicateDestination,
    DestinationOccupiedByFile,
    DirectoryPathOccupiedByFile,
}
