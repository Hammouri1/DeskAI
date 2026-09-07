using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Recipes;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Execution;
using DeskAI.Safety;

namespace DeskAI.App.Preview;

/// <summary>
/// Produces an in-memory demonstration plan. The displayed root is never created or accessed.
/// </summary>
public sealed class DemoOrganizationPlanFactory(
    IOrganizationPlanner planner,
    IFileClassifier classifier,
    FolderRecipe recipe,
    PlanValidator validator,
    TemporaryDemoPlanExecutor demoWorkspace)
{
    private static readonly Guid PlanId = Guid.Parse("2feee7a1-3a35-4cd4-8a37-62c66caf34e1");
    private static readonly DateTimeOffset DemoTimestamp = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    public DemoPlanSnapshot Create(int revision)
    {
        var root = demoWorkspace.Root;

        var files = CreateDemoFiles()
            .Select(file => new ClassifiedFile(file, classifier.Classify(file)))
            .ToArray();

        var request = new OrganizationPlanningRequest(
            PlanId,
            root.Id,
            revision,
            DemoTimestamp.AddMinutes(revision - 1),
            PlanValidator.CurrentPolicyVersion,
            recipe,
            files);

        var plan = planner.CreatePlan(request);
        return new DemoPlanSnapshot(plan, validator.Validate(plan, root), root);
    }

    private static FileItem[] CreateDemoFiles() =>
    [
        File("9c280a50-59d5-471c-b30c-19b0783f207d", @"Inbox\course-notes.pdf", FileKind.Document, 284_000),
        File("0324a670-9b8f-4c47-85b4-7188d480e29f", @"Archive\course-notes.pdf", FileKind.Document, 190_000),
        File("962596a5-4f86-422c-9705-07ba11301053", "Screenshot 2026-09-07.png", FileKind.Image, 1_240_000),
        File("1c2f204d-a739-4701-bc2a-156b5281b198", "semester-budget.xlsx", FileKind.Spreadsheet, 73_000),
        File("bcb14329-2519-4b17-a1b2-c2177f846f5d", @"Documents\reading-list.md", FileKind.Document, 8_000),
        File("7161330f-cec4-4c56-9a51-46b78341f21b", "unrecognized.deskai-demo", FileKind.Unknown, 512),
    ];

    private static FileItem File(string id, string relativePath, FileKind kind, long sizeBytes) =>
        new(
            Guid.Parse(id),
            relativePath,
            kind,
            sizeBytes,
            DemoTimestamp.AddDays(-30),
            DemoTimestamp.AddDays(-1));
}
