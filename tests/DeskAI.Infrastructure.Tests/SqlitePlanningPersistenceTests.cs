using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqlitePlanningPersistenceTests
{
    [Fact]
    public async Task Repositories_RoundTripRootPlanIssuesAndJournalOutcome()
    {
        using var sandbox = new TemporaryDirectory();
        var databasePath = Path.Combine(sandbox.Path, "deskai.db");
        var options = Options.Create(new DatabaseOptions { DatabasePath = databasePath });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);

        var root = AuthorizedRoot.Create(
            Guid.NewGuid(),
            sandbox.Path,
            "Generated test root",
            RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly);
        var rootRepository = new SqliteAuthorizedRootRepository(options, new SystemClock());
        await rootRepository.SaveAsync(root, TestContext.Current.CancellationToken);
        var operation = new MoveFileOperation(
            Guid.NewGuid(), "sample.txt", @"Documents\sample.txt", "Test move", OperationProvenance.Rule);
        var issue = new PlanIssue(
            PlanIssueCode.DestinationOccupiedByFile,
            PlanIssueSeverity.Conflict,
            "Generated conflict",
            [Guid.NewGuid()],
            [operation.Id]);
        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(), root.Id, 2, DateTimeOffset.UtcNow, "1", [operation], [issue]);
        var planRepository = new SqlitePlanRepository(options);
        await planRepository.SaveAsync(plan, TestContext.Current.CancellationToken);

        var journal = new SqliteOperationJournal(options);
        var transaction = new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, plan.Revision, Guid.NewGuid(),
            ExecutionTransactionKind.Execute, null, ExecutionTransactionState.Prepared,
            DateTimeOffset.UtcNow, null,
            [new OperationJournalEntry(
                0, operation.Id, operation.Kind, operation.SourceRelativePath,
                operation.DestinationRelativePath, 42, DateTimeOffset.UtcNow,
                JournalOperationState.Pending, null)]);
        await journal.CreateAsync(transaction, TestContext.Current.CancellationToken);
        await journal.UpdateOperationAsync(
            transaction.Id, operation.Id, JournalOperationState.Completed, null,
            TestContext.Current.CancellationToken);
        await journal.UpdateTransactionAsync(
            transaction.Id, ExecutionTransactionState.Completed, DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        var storedRoot = await rootRepository.FindAsync(root.Id, TestContext.Current.CancellationToken);
        var storedPlan = await planRepository.FindAsync(plan.Id, plan.Revision, TestContext.Current.CancellationToken);
        var storedTransaction = await journal.FindAsync(transaction.Id, TestContext.Current.CancellationToken);

        Assert.Equal(root, storedRoot);
        Assert.NotNull(storedPlan);
        Assert.Equal(operation, Assert.Single(storedPlan.Operations));
        var storedIssue = Assert.Single(storedPlan.Issues);
        Assert.Equal(issue.Code, storedIssue.Code);
        Assert.Equal(issue.Severity, storedIssue.Severity);
        Assert.Equal(issue.Explanation, storedIssue.Explanation);
        Assert.Equal(issue.FileIds, storedIssue.FileIds);
        Assert.Equal(issue.OperationIds, storedIssue.OperationIds);
        Assert.Equal(ExecutionTransactionState.Completed, storedTransaction!.State);
        Assert.Equal(JournalOperationState.Completed, Assert.Single(storedTransaction.Operations).State);
        Assert.Empty(await journal.ListIncompleteAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_folder_move_its_made_at_time_and_the_plan_purpose_survive_saving()
    {
        using var sandbox = new TemporaryDirectory();
        var options = Options.Create(new DatabaseOptions { DatabasePath = Path.Combine(sandbox.Path, "deskai.db") });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);
        var root = AuthorizedRoot.Create(Guid.NewGuid(), sandbox.Path, "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);
        await new SqliteAuthorizedRootRepository(options, new SystemClock()).SaveAsync(root, TestContext.Current.CancellationToken);
        var move = new MoveFolderOperation(Guid.NewGuid(), "Old project", @"Old stuff\Old project", "Unchanged for 6 months", OperationProvenance.Heuristic);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, "1", [move], purpose: PlanPurpose.ClearOldStuff);
        var plans = new SqlitePlanRepository(options);
        await plans.SaveAsync(plan, TestContext.Current.CancellationToken);
        var madeAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var journal = new SqliteOperationJournal(options);
        var record = new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Prepared, DateTimeOffset.UtcNow, null,
            [new OperationJournalEntry(0, move.Id, PlanOperationKind.MoveFolder, move.SourceRelativePath, move.DestinationRelativePath,
                null, madeAt.AddDays(1), JournalOperationState.Pending, null) { BeforeCreatedAtUtc = madeAt }]);
        await journal.CreateAsync(record, TestContext.Current.CancellationToken);

        var storedPlan = await plans.FindAsync(plan.Id, 1, TestContext.Current.CancellationToken);
        var storedRecord = await journal.FindAsync(record.Id, TestContext.Current.CancellationToken);

        Assert.Equal(PlanPurpose.ClearOldStuff, storedPlan!.Purpose);
        Assert.IsType<MoveFolderOperation>(Assert.Single(storedPlan.Operations));
        Assert.Equal(PlanPurpose.ClearOldStuff, storedRecord!.Purpose);
        Assert.Equal(madeAt, Assert.Single(storedRecord.Operations).BeforeCreatedAtUtc);
    }
}
