using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tidy;

public enum TidySuggestionSource
{
    FileType,
    Rule,
}

public enum SameNameChoice
{
    Skip,
    KeepBoth,
}

public enum LeftAloneReason
{
    StillDownloading,
    ChangedRecently,
    OnlineOnly,
    HiddenOrSystem,
    UnknownType,
    RulesDisagree,
    InTheWay,
    TooManyWithThisName,
    BlockedBySafety,
}

/// <summary>One loose file and where DeskAI suggests it goes.</summary>
/// <remarks>
/// <see cref="MoveOperationId"/> is null when nothing would move: the file has the same name
/// as one already in its destination and the person has not chosen "Keep both".
/// </remarks>
public sealed record TidySuggestion(
    Guid FileId,
    string FileName,
    string DestinationFolder,
    string DestinationRelativePath,
    TidySuggestionSource Source,
    string Reason,
    Guid? MoveOperationId,
    bool HasSameName,
    SameNameChoice Choice);

/// <summary>A file DeskAI decided not to touch, and the reason in plain words.</summary>
public sealed record TidyLeftAlone(string FileName, LeftAloneReason Reason, string Explanation);

/// <summary>Everything the Organize page shows for one folder, and the plan behind it.</summary>
public sealed record TidyPreview(
    AuthorizedRoot Root,
    OrganizationPlan Plan,
    IReadOnlyList<TidySuggestion> Suggestions,
    IReadOnlyList<TidyLeftAlone> LeftAlone,
    bool ReachedLimit,
    bool ScanWasIncomplete,
    bool CanTidy,
    string? FolderProblem);
