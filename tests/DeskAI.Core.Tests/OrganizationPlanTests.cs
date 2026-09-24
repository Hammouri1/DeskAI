using DeskAI.Core.Plans;

namespace DeskAI.Core.Tests;

public sealed class OrganizationPlanTests
{
    [Fact]
    public void CreateDraft_RejectsDuplicateOperationIds()
    {
        var operationId = Guid.NewGuid();
        var operations = new PlanOperation[]
        {
            new CreateDirectoryOperation(operationId, "Documents", "Group documents", OperationProvenance.Rule),
            new CreateDirectoryOperation(operationId, "Images", "Group images", OperationProvenance.Rule),
        };

        var action = () => OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow,
            "1",
            operations);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Approval_BindsToExactPlanRevisionAndSelection()
    {
        var operation = new CreateDirectoryOperation(
            Guid.NewGuid(),
            "Documents",
            "Group documents",
            OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            3,
            DateTimeOffset.UtcNow,
            "1",
            [operation]);

        var approval = Approval.Create(Guid.NewGuid(), plan, [operation.Id], DateTimeOffset.UtcNow);

        Assert.Equal(plan.Id, approval.PlanId);
        Assert.Equal(3, approval.PlanRevision);
        Assert.Equal("1", approval.PolicyVersion);
        Assert.Contains(operation.Id, approval.SelectedOperationIds);
    }

    [Fact]
    public void Approval_RejectsOperationFromAnotherPlan()
    {
        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow,
            "1",
            []);

        var action = () => Approval.Create(Guid.NewGuid(), plan, [Guid.NewGuid()], DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void A_tidy_plan_cannot_move_a_folder()
    {
        var move = new MoveFolderOperation(Guid.NewGuid(), "Old project", @"Old stuff\Old project", "Unchanged for 6 months", OperationProvenance.Heuristic);

        Assert.Throws<ArgumentException>(() => OrganizationPlan.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "1", [move]));
    }

    [Fact]
    public void A_desktop_studio_plan_may_move_a_folder_and_keeps_its_purpose()
    {
        var move = new MoveFolderOperation(Guid.NewGuid(), "Old project", @"Old stuff\Old project", "Unchanged for 6 months", OperationProvenance.Heuristic);

        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "1", [move], purpose: PlanPurpose.ClearOldStuff);

        Assert.Equal(PlanPurpose.ClearOldStuff, plan.Purpose);
        Assert.Equal(PlanOperationKind.MoveFolder, Assert.Single(plan.Operations).Kind);
    }

    [Fact]
    public void A_plan_made_without_a_purpose_is_a_tidy() =>
        Assert.Equal(PlanPurpose.Tidy, OrganizationPlan.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "1", []).Purpose);
}
