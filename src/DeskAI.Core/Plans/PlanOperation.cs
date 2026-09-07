namespace DeskAI.Core.Plans;

public abstract record PlanOperation(
    Guid Id,
    string Reason,
    OperationProvenance Provenance)
{
    public abstract PlanOperationKind Kind { get; }
}

public sealed record CreateDirectoryOperation(
    Guid Id,
    string DestinationRelativePath,
    string Reason,
    OperationProvenance Provenance)
    : PlanOperation(Id, Reason, Provenance)
{
    public override PlanOperationKind Kind => PlanOperationKind.CreateDirectory;
}

public sealed record MoveFileOperation(
    Guid Id,
    string SourceRelativePath,
    string DestinationRelativePath,
    string Reason,
    OperationProvenance Provenance)
    : PlanOperation(Id, Reason, Provenance)
{
    public override PlanOperationKind Kind => PlanOperationKind.MoveFile;
}

public sealed record RenameFileOperation(
    Guid Id,
    string SourceRelativePath,
    string DestinationRelativePath,
    string Reason,
    OperationProvenance Provenance)
    : PlanOperation(Id, Reason, Provenance)
{
    public override PlanOperationKind Kind => PlanOperationKind.RenameFile;
}

public enum PlanOperationKind
{
    CreateDirectory,
    MoveFile,
    RenameFile,
}

public enum OperationProvenance
{
    Rule,
    Heuristic,
    LocalAi,
    CloudAi,
    User,
}
