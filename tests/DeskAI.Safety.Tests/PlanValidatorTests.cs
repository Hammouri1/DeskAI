using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Safety.Tests;

public sealed class PlanValidatorTests
{
    [Fact]
    public void Validate_BlocksPlanBoundToDifferentRoot()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Root", "Synthetic", RootAccessLevel.Allowed);
        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow,
            PlanValidator.CurrentPolicyVersion,
            []);

        var result = new PlanValidator(new WindowsPathPolicy()).Validate(plan, root);

        Assert.False(result.CanBeApproved);
        Assert.Equal(ValidationReasonCode.OutsideAuthorizedRoot, result.Operations.Single().Result.ReasonCode);
    }

    [Fact]
    public void Validate_AllowsOnlyClosedTypedOperationsInsideRoot()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Root", "Synthetic", RootAccessLevel.Allowed);
        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            root.Id,
            1,
            DateTimeOffset.UtcNow,
            PlanValidator.CurrentPolicyVersion,
            [new MoveFileOperation(
                Guid.NewGuid(),
                "inbox.txt",
                @"Documents\inbox.txt",
                "Known document type",
                OperationProvenance.Rule)]);

        var result = new PlanValidator(new WindowsPathPolicy()).Validate(plan, root);

        Assert.True(result.CanBeApproved);
    }

    [Fact]
    public void Validate_BlocksPlanContainingPlannerConflict()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Root", "Synthetic", RootAccessLevel.Allowed);
        var operation = new MoveFileOperation(
            Guid.NewGuid(),
            "source.txt",
            @"Documents\source.txt",
            "Test move",
            OperationProvenance.Rule);
        var conflict = new PlanIssue(
            PlanIssueCode.DestinationOccupiedByFile,
            PlanIssueSeverity.Conflict,
            "The destination is occupied.",
            [],
            [operation.Id]);
        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            root.Id,
            1,
            DateTimeOffset.UtcNow,
            PlanValidator.CurrentPolicyVersion,
            [operation],
            [conflict]);

        var result = new PlanValidator(new WindowsPathPolicy()).Validate(plan, root);

        Assert.False(result.CanBeApproved);
        Assert.Contains(result.Operations, item => item.Result.ReasonCode == ValidationReasonCode.Collision);
    }
}
