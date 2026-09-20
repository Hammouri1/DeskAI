using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

/// <summary>
/// The tidy permission as it is stored: separate from what may be read, erased with the
/// folder, and impossible to attach to the practice workspace.
/// </summary>
public sealed class SqliteAuthorizedRootRepositoryTests
{
    [Fact]
    public async Task Allowing_tidying_is_remembered_and_can_be_taken_back()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var root = Reading(sandbox, RootAuthorizationScope.MetadataOnly);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);

        await repository.AllowTidyAsync(root.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        Assert.True(RootCapabilities.CanTidy((await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!));
        Assert.True(RootCapabilities.CanTidy(Assert.Single(await repository.ListAsync(TestContext.Current.CancellationToken))));

        await repository.StopTidyAsync(root.Id, TestContext.Current.CancellationToken);
        Assert.False(RootCapabilities.CanTidy((await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!));
    }

    [Fact]
    public async Task Changing_what_may_be_read_keeps_the_tidy_permission()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var root = Reading(sandbox, RootAuthorizationScope.MetadataOnly);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);
        await repository.AllowTidyAsync(root.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        await repository.SaveAsync(
            AuthorizedRoot.Create(root.Id, root.CanonicalPath, root.DisplayName, RootAccessLevel.Allowed, RootAuthorizationScope.MetadataAndContent),
            TestContext.Current.CancellationToken);

        var reloaded = (await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!;
        Assert.True(RootCapabilities.CanTidy(reloaded));
        Assert.True(RootCapabilities.CanReadContent(reloaded));
    }

    [Theory]
    [InlineData(RootAuthorizationScope.MetadataAndDocuments)]
    [InlineData(RootAuthorizationScope.MetadataDocumentsAndPdf)]
    [InlineData(RootAuthorizationScope.MetadataDocumentsAndSlides)]
    [InlineData(RootAuthorizationScope.MetadataDocumentsPdfAndSlides)]
    public async Task Document_reading_scope_can_be_disconnected_and_forgets_its_tidy_grant(
        RootAuthorizationScope scope)
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var root = Reading(sandbox, scope);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);
        await repository.AllowTidyAsync(root.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        Assert.True(RootCapabilities.CanTidy((await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!));

        await repository.RemoveAsync(root.Id, TestContext.Current.CancellationToken);

        Assert.Null(await repository.FindAsync(root.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disconnecting_a_folder_forgets_its_tidy_permission()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var root = Reading(sandbox, RootAuthorizationScope.MetadataOnly);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);
        await repository.AllowTidyAsync(root.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        await repository.RemoveAsync(root.Id, TestContext.Current.CancellationToken);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);

        Assert.False(RootCapabilities.CanTidy((await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!));
    }

    [Fact]
    public async Task Disconnecting_a_tidied_folder_forgets_its_tidy_history_too()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var root = Reading(sandbox, RootAuthorizationScope.MetadataOnly);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);
        var (journal, tidy) = await RecordTidyAndUndoAsync(sandbox, root);

        await repository.RemoveAsync(root.Id, TestContext.Current.CancellationToken);

        Assert.Null(await repository.FindAsync(root.Id, TestContext.Current.CancellationToken));
        Assert.Null(await journal.FindAsync(tidy, TestContext.Current.CancellationToken));
        Assert.Empty(await journal.ListRecentAsync(10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Removing_is_refused_for_the_practice_workspace_and_its_history_stays()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var demo = Reading(sandbox, RootAuthorizationScope.ControlledDemo);
        await repository.SaveAsync(demo, TestContext.Current.CancellationToken);
        var (journal, tidy) = await RecordTidyAndUndoAsync(sandbox, demo);

        await repository.RemoveAsync(demo.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(await repository.FindAsync(demo.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await journal.FindAsync(tidy, TestContext.Current.CancellationToken));
        Assert.Equal(2, (await journal.ListRecentAsync(10, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task The_practice_workspace_cannot_be_given_a_tidy_permission()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var demo = Reading(sandbox, RootAuthorizationScope.ControlledDemo);
        await repository.SaveAsync(demo, TestContext.Current.CancellationToken);

        await repository.AllowTidyAsync(demo.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        Assert.Null((await repository.FindAsync(demo.Id, TestContext.Current.CancellationToken))!.TidyAllowedSinceUtc);
    }

    /// <summary>Stores a plan for the folder, a tidy of it, and an undo of that tidy.</summary>
    private static async Task<(SqliteOperationJournal Journal, Guid TidyId)> RecordTidyAndUndoAsync(
        TemporaryDirectory sandbox,
        AuthorizedRoot root)
    {
        var options = Options.Create(new DatabaseOptions { DatabasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db") });
        var move = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"Documents\notes.pdf", "PDF file", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UnixEpoch, "v1", [move]);
        await new SqlitePlanRepository(options).SaveAsync(plan, TestContext.Current.CancellationToken);
        var journal = new SqliteOperationJournal(options);
        var operation = new OperationJournalEntry(
            0, move.Id, PlanOperationKind.MoveFile, move.SourceRelativePath, move.DestinationRelativePath,
            10, DateTimeOffset.UnixEpoch, JournalOperationState.Completed, null);
        var tidy = Guid.NewGuid();
        await journal.CreateAsync(new ExecutionJournalEntry(
            tidy, plan.Id, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Completed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [operation]),
            TestContext.Current.CancellationToken);
        await journal.CreateAsync(new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, 1, Guid.Empty, ExecutionTransactionKind.Undo, tidy,
            ExecutionTransactionState.Completed, DateTimeOffset.UnixEpoch.AddMinutes(1), DateTimeOffset.UnixEpoch.AddMinutes(1), [operation]),
            TestContext.Current.CancellationToken);
        return (journal, tidy);
    }

    private static AuthorizedRoot Reading(TemporaryDirectory sandbox, RootAuthorizationScope scope) =>
        AuthorizedRoot.Create(Guid.NewGuid(), sandbox.CreateDummyDirectory("Folder"), "Folder", RootAccessLevel.Allowed, scope);

    private static async Task<SqliteAuthorizedRootRepository> CreateAsync(TemporaryDirectory sandbox)
    {
        var options = Options.Create(new DatabaseOptions { DatabasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db") });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);
        return new SqliteAuthorizedRootRepository(options, new SystemClock());
    }
}
