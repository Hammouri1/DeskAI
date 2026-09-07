using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Recipes;
using FileClassification = DeskAI.Core.Classification.Classification;

namespace DeskAI.Core.Tests;

public sealed class OrganizationPlannerTests
{
    private static readonly Guid PlanId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedDirectories = ["Documents", "Images", @"Images\Screenshots"];

    [Fact]
    public void CreatePlan_ProducesDirectoryAndMoveProposalsWithExplanations()
    {
        var files = new[]
        {
            Classified("report.pdf", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule),
            Classified("Screenshot 1.png", FileKind.Image, FileCategory.Screenshots, ClassificationSource.Heuristic),
        };

        var plan = new OrganizationPlanner().CreatePlan(Request(files));

        var directories = plan.Operations.OfType<CreateDirectoryOperation>().ToArray();
        var moves = plan.Operations.OfType<MoveFileOperation>().ToArray();
        Assert.Equal(ExpectedDirectories,
            directories.Select(operation => operation.DestinationRelativePath));
        Assert.Contains(moves, operation =>
            operation.SourceRelativePath == "report.pdf" &&
            operation.DestinationRelativePath == @"Documents\report.pdf" &&
            operation.Provenance == OperationProvenance.Rule);
        Assert.Contains(moves, operation =>
            operation.DestinationRelativePath == @"Images\Screenshots\Screenshot 1.png" &&
            operation.Provenance == OperationProvenance.Heuristic);
        Assert.All(plan.Operations, operation => Assert.False(string.IsNullOrWhiteSpace(operation.Reason)));
    }

    [Fact]
    public void CreatePlan_IsStableWhenInputOrderChanges()
    {
        var first = Classified("b.pdf", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule);
        var second = Classified("a.png", FileKind.Image, FileCategory.Images, ClassificationSource.Rule);
        var planner = new OrganizationPlanner();

        var firstPlan = planner.CreatePlan(Request([first, second]));
        var reorderedPlan = planner.CreatePlan(Request([second, first]));

        Assert.Equal(firstPlan.Operations.Select(operation => operation.Id), reorderedPlan.Operations.Select(operation => operation.Id));
        Assert.Equal(
            firstPlan.Operations.Select(OperationSignature),
            reorderedPlan.Operations.Select(OperationSignature));
    }

    [Fact]
    public void CreatePlan_RecordsUnknownFileWithoutProposingMove()
    {
        var file = Classified("mystery.bin", FileKind.Unknown, FileCategory.Unknown, ClassificationSource.Heuristic);

        var plan = new OrganizationPlanner().CreatePlan(Request([file]));

        Assert.Empty(plan.Operations);
        Assert.Equal(PlanIssueCode.UnclassifiedFile, Assert.Single(plan.Issues).Code);
    }

    [Fact]
    public void CreatePlan_RecordsMissingRecipeMappingWithoutGuessing()
    {
        var recipe = new FolderRecipe("empty", "Empty", 1, []);
        var file = Classified("report.pdf", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule);

        var plan = new OrganizationPlanner().CreatePlan(Request([file], recipe));

        Assert.Empty(plan.Operations);
        Assert.Equal(PlanIssueCode.NoRecipeDestination, Assert.Single(plan.Issues).Code);
    }

    [Fact]
    public void CreatePlan_DoesNotMoveFileAlreadyAtRecipeDestination()
    {
        var file = Classified(@"Documents\report.pdf", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule);

        var plan = new OrganizationPlanner().CreatePlan(Request([file]));

        Assert.Empty(plan.Operations);
        Assert.Equal(PlanIssueCode.AlreadyOrganized, Assert.Single(plan.Issues).Code);
    }

    [Fact]
    public void CreatePlan_DetectsDuplicateDestinationCaseInsensitively()
    {
        var files = new[]
        {
            Classified(@"First\Report.pdf", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule),
            Classified(@"Second\report.PDF", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule),
        };

        var plan = new OrganizationPlanner().CreatePlan(Request(files));

        var conflict = Assert.Single(plan.Issues, issue => issue.Code == PlanIssueCode.DuplicateDestination);
        Assert.Equal(PlanIssueSeverity.Conflict, conflict.Severity);
        Assert.Equal(2, conflict.OperationIds.Count);
    }

    [Fact]
    public void CreatePlan_DetectsDestinationOccupiedByScannedFile()
    {
        var files = new[]
        {
            Classified(@"Inbox\report.pdf", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule),
            Classified(@"Documents\report.pdf", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule),
        };

        var plan = new OrganizationPlanner().CreatePlan(Request(files));

        Assert.Contains(plan.Issues, issue => issue.Code == PlanIssueCode.DestinationOccupiedByFile);
        Assert.Contains(plan.Issues, issue => issue.Code == PlanIssueCode.AlreadyOrganized);
    }

    [Fact]
    public void CreatePlan_DetectsRequiredDirectoryOccupiedByFile()
    {
        var file = Classified("Documents", FileKind.Document, FileCategory.Documents, ClassificationSource.User);

        var plan = new OrganizationPlanner().CreatePlan(Request([file]));

        Assert.Contains(plan.Issues, issue => issue.Code == PlanIssueCode.DirectoryPathOccupiedByFile);
    }

    [Fact]
    public void PlanningRequest_RejectsDuplicateSourcePaths()
    {
        var files = new[]
        {
            Classified("same.pdf", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule),
            Classified("SAME.PDF", FileKind.Document, FileCategory.Documents, ClassificationSource.Rule),
        };

        var action = () => Request(files);

        Assert.Throws<ArgumentException>(action);
    }

    private static OrganizationPlanningRequest Request(
        IEnumerable<ClassifiedFile> files,
        FolderRecipe? recipe = null) =>
        new(PlanId, RootId, 1, CreatedAt, "1", recipe ?? DefaultFolderRecipe.Create(), files);

    private static ClassifiedFile Classified(
        string path,
        FileKind kind,
        FileCategory category,
        ClassificationSource source) =>
        new(
            new FileItem(Guid.NewGuid(), path, FileKind.Unknown, 10, CreatedAt, CreatedAt),
            new FileClassification(category, kind, source, source == ClassificationSource.Heuristic ? 0.95 : 1, "Test classification"));

    private static string OperationSignature(PlanOperation operation) => operation switch
    {
        CreateDirectoryOperation create => $"directory:{create.DestinationRelativePath}",
        MoveFileOperation move => $"move:{move.SourceRelativePath}:{move.DestinationRelativePath}",
        RenameFileOperation rename => $"rename:{rename.SourceRelativePath}:{rename.DestinationRelativePath}",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
