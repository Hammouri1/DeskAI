using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Execution;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using DeskAI.Safety;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

/// <summary>
/// The real-folder executor in situations the app itself cannot stage: a folder disconnected
/// part-way through a tidy, and a protected file inside a connected folder.
/// </summary>
public sealed class FolderTidyExecutorTests
{
    [Fact]
    public async Task A_folder_disconnected_part_way_through_stops_the_remaining_moves()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var first = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var second = sandbox.CreateDummyFile(@"Folder\b.pdf");
        var root = Allowed(folder);

        // Looked up at the start, before the run, and before each file: disconnected after the first file.
        var roots = new DisconnectingRootRepository(root, disconnectAfter: 3);
        var policy = new WindowsPathPolicy();
        var executor = Create(roots, policy, sandbox);
        var moveA = Move("a.pdf");
        var moveB = Move("b.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [moveA, moveB]);

        var result = await executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [moveA.Id, moveB.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [moveA.Id] = Facts(first), [moveB.Id] = Facts(second) },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Operations[0].Outcome);
        Assert.Equal(ExecutionOutcome.Failed, result.Operations[1].Outcome);
        Assert.Contains("no longer connected", result.Operations[1].Error, StringComparison.Ordinal);
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task A_protected_file_inside_a_connected_folder_is_never_moved()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var secret = sandbox.CreateDummyFile(@"Folder\tax-return.pdf");
        var root = Allowed(folder);
        var roots = new DisconnectingRootRepository(root, disconnectAfter: int.MaxValue);
        var executor = Create(roots, new WindowsPathPolicy(userProtectedEntries: [secret]), sandbox);
        var move = Move("tax-return.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        var result = await executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(secret) },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(secret));
    }

    [Fact]
    public async Task A_folder_that_no_longer_passes_the_safety_re_check_moves_nothing()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var file = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var root = Allowed(folder);
        var executor = new FolderTidyExecutor(
            new DisconnectingRootRepository(root, int.MaxValue),
            new FixedFolderService("This folder crosses a link or shortcut, so DeskAI will not tidy it."),
            new PlanValidator(new WindowsPathPolicy()),
            new WindowsPathPolicy(),
            new SystemClock(),
            new InMemoryOperationJournal(),
            new InMemoryPlanRepository(),
            Database(sandbox));
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        var result = await executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(file) },
            TestContext.Current.CancellationToken);

        Assert.Contains("link or shortcut", Assert.Single(result.Operations).Error, StringComparison.Ordinal);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task While_another_window_holds_the_lock_a_tidy_waits_briefly_then_moves_nothing()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var file = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var root = Allowed(folder);
        var executor = new FolderTidyExecutor(
            new DisconnectingRootRepository(root, int.MaxValue), new FixedFolderService(null),
            new PlanValidator(new WindowsPathPolicy()), new WindowsPathPolicy(), new SystemClock(),
            new InMemoryOperationJournal(), new InMemoryPlanRepository(), Database(sandbox))
        {
            BusyWait = TimeSpan.FromMilliseconds(200),
        };
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        ExecutionResult result;
        using (HoldLock(sandbox))
        {
            result = await executor.ExecuteAsync(
                plan,
                Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
                new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(file) },
                TestContext.Current.CancellationToken);
        }

        Assert.Contains("another window", Assert.Single(result.Operations).Error, StringComparison.Ordinal);
        Assert.True(File.Exists(file));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using (HoldLock(sandbox))
            {
                await executor.UndoAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
            }
        });
    }

    [Fact]
    public async Task A_record_is_not_checked_while_another_window_holds_the_lock_and_is_checked_once_it_is_free()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        var file = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var root = Allowed(folder);
        var plans = new InMemoryPlanRepository();
        var journal = new InMemoryOperationJournal(plans);
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);
        await plans.SaveAsync(plan, TestContext.Current.CancellationToken);
        var facts = Facts(file);
        var record = new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Executing, DateTimeOffset.UtcNow, null,
            [new OperationJournalEntry(0, move.Id, PlanOperationKind.MoveFile, move.SourceRelativePath, move.DestinationRelativePath,
                facts.SizeBytes, facts.ModifiedAtUtc, JournalOperationState.InProgress, null)]);
        await journal.CreateAsync(record, TestContext.Current.CancellationToken);
        var executor = new FolderTidyExecutor(
            new DisconnectingRootRepository(root, int.MaxValue), new FixedFolderService(null),
            new PlanValidator(new WindowsPathPolicy()), new WindowsPathPolicy(), new SystemClock(),
            journal, plans, Database(sandbox))
        {
            BusyWait = TimeSpan.FromMilliseconds(200),
        };

        using (HoldLock(sandbox))
        {
            Assert.Empty(await executor.CheckInterruptedAsync(root.Id, TestContext.Current.CancellationToken));
        }

        Assert.Equal(ExecutionTransactionState.Executing, (await journal.FindAsync(record.Id, TestContext.Current.CancellationToken))!.State);
        var checkedRecord = Assert.Single(await executor.CheckInterruptedAsync(root.Id, TestContext.Current.CancellationToken));
        Assert.Equal(ExecutionTransactionState.RecoveryRequired, checkedRecord.State);

        // The file never left, and the disk says so.
        Assert.Equal(JournalOperationState.Failed, Assert.Single(checkedRecord.Operations).State);
    }

    [Fact]
    public async Task The_lock_is_free_again_once_a_run_is_over()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var file = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var root = Allowed(folder);
        var executor = Create(new DisconnectingRootRepository(root, int.MaxValue), new WindowsPathPolicy(), sandbox);
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        await executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(file) },
            TestContext.Current.CancellationToken);

        using var held = HoldLock(sandbox);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "a.pdf")));
    }

    // The next three came from the retired practice executor's tests (2026-09-11). They cover
    // move rules the two executors shared and no real-folder test covered yet.

    [Fact]
    public async Task An_approval_naming_an_operation_not_in_the_plan_moves_nothing()
    {
        using var sandbox = new TemporaryDirectory();
        var (executor, root, file, _) = Folder(sandbox, new InMemoryOperationJournal());
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);
        var approval = new Approval(
            Guid.NewGuid(), plan.Id, plan.Revision, plan.PolicyVersion,
            new HashSet<Guid> { move.Id, Guid.NewGuid() }, DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(
            plan, approval, new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(file) }, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task If_the_journal_cannot_be_written_first_no_file_changes()
    {
        using var sandbox = new TemporaryDirectory();
        var (executor, root, file, folder) = Folder(sandbox, new FailingCreateOperationJournal());
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(file) },
            TestContext.Current.CancellationToken));

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(Path.Combine(folder, "Documents", "a.pdf")));
    }

    [Fact]
    public async Task Every_move_is_journaled_with_the_file_facts_before_and_its_outcome_after()
    {
        using var sandbox = new TemporaryDirectory();
        var journal = new InMemoryOperationJournal();
        var (executor, root, file, _) = Folder(sandbox, journal);
        var facts = Facts(file);
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        var result = await executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = facts },
            TestContext.Current.CancellationToken);

        var entry = await journal.FindAsync(result.TransactionId, TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionTransactionState.Completed, entry!.State);
        var recorded = Assert.Single(entry.Operations);
        Assert.Equal(JournalOperationState.Completed, recorded.State);
        Assert.Equal(facts.SizeBytes, recorded.BeforeSizeBytes);
        Assert.Equal(facts.ModifiedAtUtc, recorded.BeforeModifiedAtUtc);
    }

    /// <summary>
    /// A run that only made folders (a folder template) and stopped part-way. Before the fix a
    /// record with no file moves settled as Failed, so the folders it had made could never be
    /// undone.
    /// </summary>
    [Fact]
    public async Task An_interrupted_run_that_only_made_folders_settles_by_the_folders_it_made_and_can_be_undone()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Assignments");
        sandbox.CreateDummyDirectory(@"Folder\Slides");
        var root = Allowed(folder);
        var plans = new InMemoryPlanRepository();
        var journal = new InMemoryOperationJournal(plans);
        var creates = new[] { MakeFolder("Assignments"), MakeFolder("Slides"), MakeFolder("Notes") };
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, creates);
        await plans.SaveAsync(plan, TestContext.Current.CancellationToken);
        var record = new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Executing, DateTimeOffset.UtcNow, null,
            [
                new OperationJournalEntry(0, creates[0].Id, PlanOperationKind.CreateDirectory, null, "Assignments", null, null, JournalOperationState.Completed, null),
                new OperationJournalEntry(1, creates[1].Id, PlanOperationKind.CreateDirectory, null, "Slides", null, null, JournalOperationState.InProgress, null),
                new OperationJournalEntry(2, creates[2].Id, PlanOperationKind.CreateDirectory, null, "Notes", null, null, JournalOperationState.Pending, null),
            ]);
        await journal.CreateAsync(record, TestContext.Current.CancellationToken);
        var executor = new FolderTidyExecutor(
            new DisconnectingRootRepository(root, int.MaxValue), new FixedFolderService(null),
            new PlanValidator(new WindowsPathPolicy()), new WindowsPathPolicy(), new SystemClock(),
            journal, plans, Database(sandbox));

        var checkedRecord = Assert.Single(await executor.CheckInterruptedAsync(root.Id, TestContext.Current.CancellationToken));
        var settled = await executor.CloseInterruptedAsync(root.Id, record.Id, TestContext.Current.CancellationToken);

        // The folder under way exists, but nothing proves DeskAI made it, so it is "already there".
        Assert.Equal(JournalOperationState.AlreadyPresent, checkedRecord.Operations[1].State);
        Assert.Equal(JournalOperationState.Cancelled, checkedRecord.Operations[2].State);
        Assert.Equal(ExecutionTransactionState.PartiallyCompleted, settled);

        var undo = await executor.UndoAsync(record.Id, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, Assert.Single(undo.Operations).Outcome);
        Assert.False(Directory.Exists(Path.Combine(folder, "Assignments")));
        Assert.True(Directory.Exists(Path.Combine(folder, "Slides")));
    }

    /// <summary>A folder-only run that made nothing at all still settles as Failed, so there is nothing to undo.</summary>
    [Fact]
    public async Task An_interrupted_run_that_made_no_folder_settles_as_failed()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        var root = Allowed(folder);
        var plans = new InMemoryPlanRepository();
        var journal = new InMemoryOperationJournal(plans);
        var create = MakeFolder("Notes");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [create]);
        await plans.SaveAsync(plan, TestContext.Current.CancellationToken);
        var record = new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Executing, DateTimeOffset.UtcNow, null,
            [new OperationJournalEntry(0, create.Id, PlanOperationKind.CreateDirectory, null, "Notes", null, null, JournalOperationState.InProgress, null)]);
        await journal.CreateAsync(record, TestContext.Current.CancellationToken);
        var executor = new FolderTidyExecutor(
            new DisconnectingRootRepository(root, int.MaxValue), new FixedFolderService(null),
            new PlanValidator(new WindowsPathPolicy()), new WindowsPathPolicy(), new SystemClock(),
            journal, plans, Database(sandbox));

        await executor.CheckInterruptedAsync(root.Id, TestContext.Current.CancellationToken);
        var settled = await executor.CloseInterruptedAsync(root.Id, record.Id, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionTransactionState.Failed, settled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.UndoAsync(record.Id, TestContext.Current.CancellationToken));
    }

    private static CreateDirectoryOperation MakeFolder(string name) =>
        new(Guid.NewGuid(), name, "Part of the folder template.", OperationProvenance.User);

    /// <summary>A tidy-permitted folder holding a.pdf and an empty Documents folder.</summary>
    private static (FolderTidyExecutor Executor, AuthorizedRoot Root, string File, string Folder) Folder(
        TemporaryDirectory sandbox,
        InMemoryOperationJournal journal)
    {
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var file = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var root = Allowed(folder);
        var executor = new FolderTidyExecutor(
            new DisconnectingRootRepository(root, int.MaxValue), new FixedFolderService(null),
            new PlanValidator(new WindowsPathPolicy()), new WindowsPathPolicy(), new SystemClock(),
            journal, new InMemoryPlanRepository(), Database(sandbox));
        return (executor, root, file, folder);
    }

    /// <summary>What another DeskAI window does while it is running a tidy.</summary>
    private static FileStream HoldLock(TemporaryDirectory sandbox) =>
        new(FolderTidyExecutor.LockPathFor(Path.Combine(sandbox.Path, "deskai.db")),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static FolderTidyExecutor Create(IAuthorizedRootRepository roots, IPathPolicy policy, TemporaryDirectory sandbox) =>
        new(roots, new FixedFolderService(null), new PlanValidator(policy), policy, new SystemClock(),
            new InMemoryOperationJournal(), new InMemoryPlanRepository(), Database(sandbox));

    private static IOptions<DatabaseOptions> Database(TemporaryDirectory sandbox) =>
        Options.Create(new DatabaseOptions { DatabasePath = Path.Combine(sandbox.Path, "deskai.db") });

    private static AuthorizedRoot Allowed(string folder) =>
        AuthorizedRoot.Create(Guid.NewGuid(), folder, "Folder", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly)
            .WithTidyAllowedSince(DateTimeOffset.UnixEpoch);

    private static MoveFileOperation Move(string name) =>
        new(Guid.NewGuid(), name, Path.Combine("Documents", name), "PDF file", OperationProvenance.Rule);

    private static ExpectedFile Facts(string path)
    {
        var info = new FileInfo(path);
        return new ExpectedFile(info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>Answers with the folder until it has been asked a set number of times.</summary>
    private sealed class DisconnectingRootRepository(AuthorizedRoot root, int disconnectAfter) : IAuthorizedRootRepository
    {
        private int _finds;

        public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(rootId == root.Id && ++_finds <= disconnectAfter ? root : null);

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>([root]);

        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedFolderService(string? problem) : IReadOnlyFolderService
    {
        public Task<string?> CheckStillSafeAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) =>
            Task.FromResult(problem);

        public Task<FolderPreviewResult> AuthorizeAndPreviewAsync(
            string selectedPath, MetadataScanOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuthorizedRoot>> ListAuthorizedAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RevokeAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
