using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Infrastructure.Execution;
using DeskAI.Infrastructure.Time;
using DeskAI.Safety;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class TemporaryDemoPlanExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CreatesFolderAndMovesDummyFileInsideOwnedSandbox()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        var create = new CreateDirectoryOperation(Guid.NewGuid(), "Sorted", "Demo folder", OperationProvenance.Rule);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sorted\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, create, move);
        var approval = Approve(plan, create.Id, move.Id);

        var result = await executor.ExecuteAsync(plan, approval, TestContext.Current.CancellationToken);

        Assert.All(result.Operations, item => Assert.Equal(ExecutionOutcome.Completed, item.Outcome));
        Assert.False(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "Sorted", "semester-budget.xlsx")));
    }

    [Fact]
    public async Task ExecuteAsync_RefusesOverwriteAndPreservesBothFiles()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        var destination = Path.Combine(executor.Root.CanonicalPath, "Documents", "semester-budget.xlsx");
        await File.WriteAllTextAsync(destination, "existing dummy", TestContext.Current.CancellationToken);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Documents\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, move);

        var result = await executor.ExecuteAsync(plan, Approve(plan, move.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.Equal("existing dummy", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
    }

    [Fact]
    public async Task ExecuteAsync_RefusesStaleApprovalWithoutMovingFile()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Documents\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, move);
        var stale = new Approval(Guid.NewGuid(), plan.Id, plan.Revision + 1, plan.PolicyVersion, new HashSet<Guid> { move.Id }, DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(plan, stale, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
    }

    [Fact]
    public async Task ExecuteAsync_RefusesTraversalAndLeavesOutsideSentinelUntouched()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        var sentinel = sandbox.CreateDummyFile("sentinel.txt", "outside sentinel");
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"..\sentinel.txt", "Unsafe move", OperationProvenance.Rule);
        var plan = Plan(executor, move);

        var result = await executor.ExecuteAsync(plan, Approve(plan, move.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.Equal("outside sentinel", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesOnlyApprovedOperationIds()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(executor.Root.CanonicalPath, "Sorted"));
        var first = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sorted\semester-budget.xlsx", "Selected", OperationProvenance.Rule);
        var second = new MoveFileOperation(Guid.NewGuid(), "Screenshot 2026-09-07.png", @"Sorted\Screenshot 2026-09-07.png", "Not selected", OperationProvenance.Rule);
        var plan = Plan(executor, first, second);

        var result = await executor.ExecuteAsync(plan, Approve(plan, first.Id), TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, Assert.Single(result.Operations).OperationId);
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "Screenshot 2026-09-07.png")));
    }

    [Fact]
    public async Task ExecuteAsync_RefusesWhenOwnershipMarkerWasChanged()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(executor.Root.CanonicalPath, ".deskai-demo-root"), "tampered", TestContext.Current.CancellationToken);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Documents\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, move);

        var result = await executor.ExecuteAsync(plan, Approve(plan, move.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
    }

    [Fact]
    public async Task ExecuteAsync_RefusesApprovalContainingUnknownOperationId()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Documents\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, move);
        var approval = new Approval(
            Guid.NewGuid(), plan.Id, plan.Revision, plan.PolicyVersion,
            new HashSet<Guid> { move.Id, Guid.NewGuid() }, DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(plan, approval, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
    }

    [Fact]
    public void Constructor_RefusesDemoBaseOutsideSystemTemporaryDirectory()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Throws<InvalidOperationException>(() => new TemporaryDemoPlanExecutor(
            Options.Create(new DemoWorkspaceOptions { BasePath = driveRoot }),
            new PlanValidator(new WindowsPathPolicy()),
            new SystemClock()));
    }

    private static TemporaryDemoPlanExecutor CreateExecutor(TemporaryDirectory sandbox) =>
        new(
            Options.Create(new DemoWorkspaceOptions { BasePath = Path.Combine(sandbox.Path, "DeskAI-Demos") }),
            new PlanValidator(new WindowsPathPolicy()),
            new SystemClock());

    private static OrganizationPlan Plan(TemporaryDemoPlanExecutor executor, params PlanOperation[] operations) =>
        OrganizationPlan.CreateDraft(
            Guid.NewGuid(),
            executor.Root.Id,
            1,
            DateTimeOffset.UtcNow,
            PlanValidator.CurrentPolicyVersion,
            operations);

    private static Approval Approve(OrganizationPlan plan, params Guid[] operationIds) =>
        Approval.Create(Guid.NewGuid(), plan, operationIds, DateTimeOffset.UtcNow);
}
