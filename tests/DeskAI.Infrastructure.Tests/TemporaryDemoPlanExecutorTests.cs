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
            new SystemClock(),
            new InMemoryOperationJournal(),
            new InMemoryAuthorizedRootRepository(),
            new InMemoryPlanRepository()));
    }

    [Fact]
    public async Task ExecuteAsync_WritesIntentAndOutcomeToJournal()
    {
        using var sandbox = new TemporaryDirectory();
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(sandbox, journal);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(executor.Root.CanonicalPath, "Sorted"));
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sorted\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, move);

        var result = await executor.ExecuteAsync(plan, Approve(plan, move.Id), TestContext.Current.CancellationToken);
        var entry = await journal.FindAsync(result.TransactionId, TestContext.Current.CancellationToken);

        Assert.NotNull(entry);
        Assert.Equal(ExecutionTransactionState.Completed, entry.State);
        var recorded = Assert.Single(entry.Operations);
        Assert.Equal(JournalOperationState.Completed, recorded.State);
        Assert.NotNull(recorded.BeforeSizeBytes);
        Assert.NotNull(recorded.BeforeModifiedAtUtc);
    }

    [Fact]
    public async Task UndoAsync_RestoresMovedFileAndRemovesEmptyCreatedFolder()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        var create = new CreateDirectoryOperation(Guid.NewGuid(), "Sorted", "Demo folder", OperationProvenance.Rule);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sorted\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, create, move);
        var execution = await executor.ExecuteAsync(plan, Approve(plan, create.Id, move.Id), TestContext.Current.CancellationToken);

        var undo = await executor.UndoAsync(execution.TransactionId, TestContext.Current.CancellationToken);

        Assert.All(undo.Operations, item => Assert.Equal(ExecutionOutcome.Completed, item.Outcome));
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
        Assert.False(Directory.Exists(Path.Combine(executor.Root.CanonicalPath, "Sorted")));
    }

    [Fact]
    public async Task UndoAsync_NeverRemovesAFolderThatExistedBeforeTheRun()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        // An empty folder the person already had. The plan asks for it to exist, which it
        // does, so nothing is created — and undo therefore has nothing of its own to remove.
        var existing = Path.Combine(executor.Root.CanonicalPath, "Sorted");
        Directory.CreateDirectory(existing);
        var create = new CreateDirectoryOperation(Guid.NewGuid(), "Sorted", "Demo folder", OperationProvenance.Rule);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sorted\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, create, move);
        var execution = await executor.ExecuteAsync(plan, Approve(plan, create.Id, move.Id), TestContext.Current.CancellationToken);
        Assert.All(execution.Operations, item => Assert.Equal(ExecutionOutcome.Completed, item.Outcome));

        var undo = await executor.UndoAsync(execution.TransactionId, TestContext.Current.CancellationToken);

        Assert.All(undo.Operations, item => Assert.Equal(ExecutionOutcome.Completed, item.Outcome));
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
        Assert.True(Directory.Exists(existing));
    }

    [Fact]
    public async Task UndoAsync_ReportsCompleteWhenAPreExistingFolderIsLeftInPlace()
    {
        using var sandbox = new TemporaryDirectory();
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(sandbox, journal);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        // The sample workspace already has Documents\reading-list.md.
        var create = new CreateDirectoryOperation(Guid.NewGuid(), "Documents", "Demo folder", OperationProvenance.Rule);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Documents\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, create, move);
        var execution = await executor.ExecuteAsync(plan, Approve(plan, create.Id, move.Id), TestContext.Current.CancellationToken);

        var undo = await executor.UndoAsync(execution.TransactionId, TestContext.Current.CancellationToken);

        var undoEntry = await journal.FindAsync(undo.UndoTransactionId, TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionTransactionState.Completed, undoEntry!.State);
        var original = await journal.FindAsync(execution.TransactionId, TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionTransactionState.Undone, original!.State);
        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "Documents", "reading-list.md")));
    }

    [Fact]
    public async Task UndoAsync_RefusesFileChangedAfterExecution()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(executor.Root.CanonicalPath, "Sorted"));
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sorted\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, move);
        var execution = await executor.ExecuteAsync(plan, Approve(plan, move.Id), TestContext.Current.CancellationToken);
        var movedPath = Path.Combine(executor.Root.CanonicalPath, "Sorted", "semester-budget.xlsx");
        await File.AppendAllTextAsync(movedPath, "changed", TestContext.Current.CancellationToken);

        var undo = await executor.UndoAsync(execution.TransactionId, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(undo.Operations).Outcome);
        Assert.True(File.Exists(movedPath));
        Assert.False(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
    }

    [Fact]
    public async Task RecoverIncompleteAsync_VerifiesCompletedMoveAndRepairsJournalState()
    {
        using var sandbox = new TemporaryDirectory();
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(sandbox, journal);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(executor.Root.CanonicalPath, "Sorted"));
        var source = Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx");
        var destination = Path.Combine(executor.Root.CanonicalPath, "Sorted", "semester-budget.xlsx");
        var info = new FileInfo(source);
        var operationId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        await journal.CreateAsync(new ExecutionJournalEntry(
            transactionId, Guid.NewGuid(), 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Executing, DateTimeOffset.UtcNow, null,
            [new OperationJournalEntry(0, operationId, PlanOperationKind.MoveFile,
                "semester-budget.xlsx", @"Sorted\semester-budget.xlsx", info.Length, info.LastWriteTimeUtc,
                JournalOperationState.InProgress, null)]), TestContext.Current.CancellationToken);
        File.Move(source, destination);

        var recovered = await executor.RecoverIncompleteAsync(TestContext.Current.CancellationToken);
        var entry = await journal.FindAsync(transactionId, TestContext.Current.CancellationToken);

        Assert.Equal(1, recovered);
        Assert.Equal(ExecutionTransactionState.Completed, entry!.State);
        Assert.Equal(JournalOperationState.Completed, Assert.Single(entry.Operations).State);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWriteAheadJournalFails_MakesNoFileChange()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox, new FailingCreateOperationJournal());
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(executor.Root.CanonicalPath, "Sorted"));
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sorted\semester-budget.xlsx", "Demo move", OperationProvenance.Rule);
        var plan = Plan(executor, move);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(plan, Approve(plan, move.Id), TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(executor.Root.CanonicalPath, "semester-budget.xlsx")));
        Assert.False(File.Exists(Path.Combine(executor.Root.CanonicalPath, "Sorted", "semester-budget.xlsx")));
    }

    /// <summary>
    /// The practice mover is not a way into real folders. Allowing tidying lets a plan pass
    /// validation, but this executor is bound to its own generated workspace and must still
    /// refuse a plan for any other folder. Real tidying gets its own executor and review.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RefusesAPlanForARealFolderEvenWhenTidyingIsAllowed()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        var realFolder = sandbox.CreateDummyDirectory("RealFolder");
        sandbox.CreateDummyFile(@"RealFolder\notes.pdf");
        var real = DeskAI.Core.Roots.AuthorizedRoot.Create(Guid.NewGuid(), realFolder, "RealFolder",
                DeskAI.Core.Roots.RootAccessLevel.Allowed, DeskAI.Core.Roots.RootAuthorizationScope.MetadataOnly)
            .WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
        var move = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"Documents\notes.pdf", "PDF file", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), real.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        var result = await executor.ExecuteAsync(plan, Approve(plan, move.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(Path.Combine(realFolder, "notes.pdf")));
        Assert.False(Directory.Exists(Path.Combine(realFolder, "Documents")));
    }

    private static TemporaryDemoPlanExecutor CreateExecutor(
        TemporaryDirectory sandbox,
        InMemoryOperationJournal? journal = null) =>
        new(
            Options.Create(new DemoWorkspaceOptions { BasePath = Path.Combine(sandbox.Path, "DeskAI-Demos") }),
            new PlanValidator(new WindowsPathPolicy()),
            new SystemClock(),
            journal ?? new InMemoryOperationJournal(),
            new InMemoryAuthorizedRootRepository(),
            new InMemoryPlanRepository());

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
