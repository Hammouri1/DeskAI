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

/// <summary>
/// Moves a whole folder, and everything in it, to another place inside the same connected
/// folder (ADR 0044). Windows does it as one rename, so a folder is never half-moved.
/// </summary>
/// <remarks>Only a Desktop Studio plan may hold one; <see cref="OrganizationPlan.CreateDraft"/> refuses it in any other.</remarks>
public sealed record MoveFolderOperation(
    Guid Id,
    string SourceRelativePath,
    string DestinationRelativePath,
    string Reason,
    OperationProvenance Provenance)
    : PlanOperation(Id, Reason, Provenance)
{
    public override PlanOperationKind Kind => PlanOperationKind.MoveFolder;
}

public enum PlanOperationKind
{
    CreateDirectory,
    MoveFile,
    RenameFile,

    /// <summary>A whole folder (ADR 0044). Stored as a number, so appended last.</summary>
    MoveFolder,
}

public enum OperationProvenance
{
    Rule,
    Heuristic,
    LocalAi,
    CloudAi,
    User,
}
