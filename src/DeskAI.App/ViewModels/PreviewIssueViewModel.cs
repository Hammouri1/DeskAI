using DeskAI.Core.Plans;

namespace DeskAI.App.ViewModels;

public sealed record PreviewIssueViewModel(
    string Severity,
    string Title,
    string Explanation,
    string AffectedItems,
    PreviewStatusLevel Level)
{
    public static PreviewIssueViewModel FromIssue(PlanIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var affectedCount = Math.Max(issue.FileIds.Count, issue.OperationIds.Count);
        var isConflict = issue.Severity == PlanIssueSeverity.Conflict;
        return new PreviewIssueViewModel(
            isConflict ? "Needs your choice" : "Note",
            FormatTitle(issue.Code),
            FriendlyExplanation(issue.Code, issue.Explanation),
            affectedCount == 0 ? "No change suggested" : $"{affectedCount} sample file(s)",
            // A conflict is not an error the user caused, so it reads as "needs attention"
            // rather than a failure, but it still must not look like an ordinary note.
            isConflict ? PreviewStatusLevel.Attention : PreviewStatusLevel.Ready);
    }

    private static string FormatTitle(PlanIssueCode code) => code switch
    {
        PlanIssueCode.UnclassifiedFile => "Unclassified file",
        PlanIssueCode.NoRecipeDestination => "No recipe destination",
        PlanIssueCode.AlreadyOrganized => "Already organized",
        PlanIssueCode.DuplicateDestination => "Two files have the same name",
        PlanIssueCode.DestinationOccupiedByFile => "That filename is already used",
        PlanIssueCode.DirectoryPathOccupiedByFile => "A folder name is already used",
        _ => code.ToString(),
    };

    private static string FriendlyExplanation(PlanIssueCode code, string fallback) => code switch
    {
        PlanIssueCode.DuplicateDestination => "DeskAI left both files where they are. Later, you can choose a new name or skip one.",
        PlanIssueCode.DestinationOccupiedByFile => "DeskAI will not replace the existing file.",
        PlanIssueCode.DirectoryPathOccupiedByFile => "DeskAI will not replace the existing item.",
        PlanIssueCode.UnclassifiedFile => "DeskAI did not recognize this type, so it will stay where it is.",
        PlanIssueCode.AlreadyOrganized => "This file is already in the suggested place.",
        _ => fallback,
    };
}
