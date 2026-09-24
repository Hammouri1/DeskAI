namespace DeskAI.Core.Plans;

/// <summary>
/// Which feature made a plan, so each feature offers undo only for its own runs and only Desktop
/// Studio may move a folder (ADR 0044).
/// </summary>
/// <remarks>Stored as a number: append only, never reorder.</remarks>
public enum PlanPurpose
{
    Tidy,
    ClearOldStuff,
    FolderByGroup,
    TagNames,
}
