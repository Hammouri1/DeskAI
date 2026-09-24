using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Safety.Tests;

public sealed class PlanValidatorTests
{
    [Fact]
    public void AReadingFolderWithTidyingAllowedAcceptsASafeMove()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Tidy", "Tidy", RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly).WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
        var move = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"Documents\notes.pdf", "PDF file", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UnixEpoch, PlanValidator.CurrentPolicyVersion, [move]);

        Assert.True(new PlanValidator(new WindowsPathPolicy()).Validate(plan, root).CanBeApproved);
    }

    [Fact]
    public void AReadingFolderWithoutTidyingAllowedStillRefusesEveryMove()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Tidy", "Tidy", RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly);
        var move = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"Documents\notes.pdf", "PDF file", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UnixEpoch, PlanValidator.CurrentPolicyVersion, [move]);

        Assert.False(new PlanValidator(new WindowsPathPolicy()).Validate(plan, root).CanBeApproved);
    }

    [Fact]
    public void ATidyPermissionDoesNotLetAMoveLeaveTheFolder()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Tidy", "Tidy", RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly).WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
        var move = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"..\Elsewhere\notes.pdf", "Escape", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UnixEpoch, PlanValidator.CurrentPolicyVersion, [move]);

        Assert.False(new PlanValidator(new WindowsPathPolicy()).Validate(plan, root).CanBeApproved);
    }

    [Fact]
    public void Validate_BlocksPlanBoundToDifferentRoot()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Root", "Synthetic", RootAccessLevel.Allowed, RootAuthorizationScope.Organize);
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
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Root", "Synthetic", RootAccessLevel.Allowed, RootAuthorizationScope.Organize);
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
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Root", "Synthetic", RootAccessLevel.Allowed, RootAuthorizationScope.Organize);
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

    /// <summary>
    /// A folder connected so DeskAI may read what is inside its files still grants no
    /// permission to move, rename, or delete them. Reading and changing are separate
    /// consents, and a plan must not be able to borrow one for the other.
    /// </summary>
    [Theory]
    [InlineData(RootAuthorizationScope.MetadataOnly)]
    [InlineData(RootAuthorizationScope.MetadataAndContent)]
    public void Validate_BlocksEveryMutationForAScopeThatWasNotConnectedForChanges(
        RootAuthorizationScope scope)
    {
        var root = AuthorizedRoot.Create(
            Guid.NewGuid(),
            @"C:\DeskAITests\ReadOnly",
            "Read-only test root",
            RootAccessLevel.Allowed,
            scope);
        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            root.Id,
            1,
            DateTimeOffset.UtcNow,
            PlanValidator.CurrentPolicyVersion,
            [new MoveFileOperation(
                Guid.NewGuid(),
                "source.txt",
                @"Documents\source.txt",
                "Generated test operation",
                OperationProvenance.Rule)]);

        var result = new PlanValidator(new WindowsPathPolicy()).Validate(plan, root);

        Assert.False(result.CanBeApproved);
        Assert.Equal(ValidationReasonCode.InvalidOperation, Assert.Single(result.Operations).Result.ReasonCode);
    }

    [Fact]
    public void Validate_BlocksEveryMutationForMetadataOnlyRoot()
    {
        var root = AuthorizedRoot.Create(
            Guid.NewGuid(),
            @"C:\DeskAITests\ReadOnly",
            "Read-only test root",
            RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly);
        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            root.Id,
            1,
            DateTimeOffset.UtcNow,
            PlanValidator.CurrentPolicyVersion,
            [new MoveFileOperation(
                Guid.NewGuid(),
                "source.txt",
                @"Documents\source.txt",
                "Generated test operation",
                OperationProvenance.Rule)]);

        var result = new PlanValidator(new WindowsPathPolicy()).Validate(plan, root);

        Assert.False(result.CanBeApproved);
        Assert.Equal(ValidationReasonCode.InvalidOperation, Assert.Single(result.Operations).Result.ReasonCode);
    }

    [Fact]
    public void A_folder_move_inside_the_folder_is_allowed()
    {
        var root = FolderMoveRoot();

        Assert.True(new PlanValidator(new WindowsPathPolicy())
            .Validate(FolderMovePlan(root, "Old project", @"Old stuff\Old project"), root).CanBeApproved);
    }

    [Fact]
    public void A_folder_cannot_be_moved_into_itself()
    {
        var root = FolderMoveRoot();

        var report = new PlanValidator(new WindowsPathPolicy())
            .Validate(FolderMovePlan(root, "Projects", @"Projects\Archive\Projects"), root);

        Assert.False(report.CanBeApproved);
        Assert.Contains(report.Operations, item => item.Result.Explanation == "A folder cannot be moved into itself.");
    }

    [Fact]
    public void A_folder_holding_a_protected_entry_cannot_be_moved()
    {
        var root = FolderMoveRoot();
        var validator = new PlanValidator(new WindowsPathPolicy(userProtectedEntries: [@"C:\DeskAITests\Desktop\DeskAI\app"]));

        Assert.False(validator.Validate(FolderMovePlan(root, "DeskAI", @"Old stuff\DeskAI"), root).CanBeApproved);
    }

    [Fact]
    public void A_folder_move_cannot_leave_the_folder()
    {
        var root = FolderMoveRoot();

        Assert.False(new PlanValidator(new WindowsPathPolicy())
            .Validate(FolderMovePlan(root, "Old project", @"..\Elsewhere\Old project"), root).CanBeApproved);
    }

    private static AuthorizedRoot FolderMoveRoot() =>
        AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Desktop", "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly)
            .WithFolderMovesAllowedSince(DateTimeOffset.UnixEpoch);

    private static OrganizationPlan FolderMovePlan(AuthorizedRoot root, string from, string to) =>
        OrganizationPlan.CreateDraft(
            Guid.NewGuid(), root.Id, 1, DateTimeOffset.UnixEpoch, PlanValidator.CurrentPolicyVersion,
            [new MoveFolderOperation(Guid.NewGuid(), from, to, "Unchanged for 6 months", OperationProvenance.Heuristic)],
            purpose: PlanPurpose.ClearOldStuff);
}
