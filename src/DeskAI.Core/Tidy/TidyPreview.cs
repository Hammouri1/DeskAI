using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tidy;

public enum TidySuggestionSource
{
    FileType,
    Rule,
    Ai,
}

/// <summary>Which files AI may be asked about. Rules win over AI in both.</summary>
public enum TidySuggestionMode
{
    /// <summary>File type first; AI only for files DeskAI cannot place.</summary>
    TypesAndRules,

    /// <summary>AI for every file the person's rules do not place.</summary>
    AiForEveryFile,
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
/// <see cref="IsUnsure"/> marks an AI idea the AI itself was not confident about; the page
/// starts it unticked.
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
    SameNameChoice Choice,
    bool IsUnsure = false);

/// <summary>A file DeskAI decided not to touch, and the reason in plain words.</summary>
public sealed record TidyLeftAlone(string FileName, LeftAloneReason Reason, string Explanation);

/// <summary>
/// What AI said about one file, remembered with how the file looked when it was asked.
/// </summary>
/// <remarks>
/// AI can only name a category; DeskAI's own folder recipe turns that into a folder. The
/// size and last-changed time make the advice expire by itself: an idea about a file that has
/// since changed is an idea about a different file.
/// </remarks>
/// <param name="FolderName">
/// From "Plan this folder" (ADR 0034): the plain folder name the AI chose, checked by
/// <c>FolderNameCheck</c>, which takes the place of the recipe's folder. Null for a category idea.
/// </param>
public sealed record TidyAiAdvice(
    FileCategory Category,
    bool IsUnsure,
    string ServiceName,
    OperationProvenance Provenance,
    long SizeBytes,
    DateTimeOffset ModifiedAtUtc,
    string? FolderName = null)
{
    public bool StillAppliesTo(FileItem file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.SizeBytes == SizeBytes && file.ModifiedAtUtc == ModifiedAtUtc;
    }
}

/// <summary>Everything the Organize page shows for one folder, and the plan behind it.</summary>
/// <remarks>
/// <see cref="AskableFiles"/> are the files AI could still be asked about in the chosen mode:
/// never a file left alone, never one a rule places, and never one AI already answered for.
/// <see cref="MoveSources"/> holds, for each move in <see cref="Plan"/>, the file as it was when this list was made;
/// tidying checks each file against it again right before moving it.
/// </remarks>
public sealed record TidyPreview(
    AuthorizedRoot Root,
    OrganizationPlan Plan,
    IReadOnlyList<TidySuggestion> Suggestions,
    IReadOnlyList<TidyLeftAlone> LeftAlone,
    bool ReachedLimit,
    bool ScanWasIncomplete,
    bool CanTidy,
    string? FolderProblem,
    IReadOnlyList<FileItem> AskableFiles,
    IReadOnlyDictionary<Guid, FileItem> MoveSources);
