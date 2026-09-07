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
}
