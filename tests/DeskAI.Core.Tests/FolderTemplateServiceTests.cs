using System.Reflection;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Rules;
using DeskAI.Core.Templates;

namespace DeskAI.Core.Tests;

/// <summary>
/// Folder templates are the one thing on My workspace that changes a folder. These tests fix
/// what that may and may not do: a preview looks and changes nothing; without the tidy
/// permission the executor is never called; a name already there is never re-made; a file or
/// link in the way blocks that folder only; the policy sees every name; Make rebuilds from
/// fresh state and refuses to guess when the folder changed; and the approval covers exactly
/// the folders the person saw.
/// </summary>
public sealed class FolderTemplateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Preview_lists_each_folder_as_new_already_there_or_blocked_and_changes_nothing()
    {
        var world = new World();
        world.Lookup.Present["slides"] = FolderEntryKind.Folder;
        world.Lookup.Present["Notes"] = FolderEntryKind.File;
        world.Lookup.Present["Screenshots"] = FolderEntryKind.Link;

        var preview = await world.Service.PreviewAsync(world.RootId, "student", TestContext.Current.CancellationToken);

        Assert.False(preview.NeedsPermission);
        Assert.Null(preview.Problem);
        Assert.Equal("Downloads", preview.FolderName);
        Assert.Equal(
            [
                ("Assignments", FolderTemplateLineKind.WillMake, null),
                ("slides", FolderTemplateLineKind.AlreadyThere, null),
                ("Screenshots", FolderTemplateLineKind.Blocked, "Screenshots is a link or shortcut, so it was left alone."),
                ("Notes", FolderTemplateLineKind.Blocked, "A file called Notes is already there."),
            ],
            preview.Lines.Select(line => (line.Name, line.Kind, line.Reason)));
        Assert.Equal(1, preview.ToMake);
        Assert.True(preview.CanMake);
        Assert.Equal("Assignments", Assert.Single(preview.Plan!.Operations.OfType<CreateDirectoryOperation>()).DestinationRelativePath);
        Assert.Empty(world.Executor.Runs);
    }

    [Fact]
    public async Task Without_the_tidy_permission_nothing_is_looked_at_and_the_executor_is_never_called()
    {
        var world = new World(tidyAllowed: false);

        var preview = await world.Service.PreviewAsync(world.RootId, "student", TestContext.Current.CancellationToken);
        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);

        Assert.True(preview.NeedsPermission);
        Assert.False(preview.CanMake);
        Assert.Empty(preview.Lines);
        Assert.Equal(0, world.Lookup.Looks);
        Assert.Empty(world.Executor.Runs);
        Assert.Equal("There is nothing to make.", outcome.Problem);
    }

    [Fact]
    public async Task A_folder_that_no_longer_passes_the_safety_re_check_gets_no_list()
    {
        var world = new World();
        world.Folders.Problem = "This folder crosses a link or shortcut, so DeskAI will not tidy it.";

        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);

        Assert.Equal(world.Folders.Problem, preview.Problem);
        Assert.False(preview.CanMake);
        Assert.Equal(0, world.Lookup.Looks);
    }

    [Fact]
    public async Task A_name_the_policy_refuses_is_blocked_and_left_out_of_the_plan()
    {
        var world = new World();
        world.Safety.Blocked.Add("Installers");

        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);

        var installers = preview.Lines.Single(line => line.Name == "Installers");
        Assert.Equal(FolderTemplateLineKind.Blocked, installers.Kind);
        Assert.Equal("DeskAI's safety rules don't allow a folder with that name here.", installers.Reason);
        Assert.Equal("Screenshots", Assert.Single(preview.Plan!.Operations.OfType<CreateDirectoryOperation>()).DestinationRelativePath);
    }

    [Fact]
    public async Task When_every_folder_is_already_there_there_is_nothing_to_make()
    {
        var world = new World();
        world.Lookup.Present["Screenshots"] = FolderEntryKind.Folder;
        world.Lookup.Present["Installers"] = FolderEntryKind.Folder;

        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);

        Assert.Equal(0, preview.ToMake);
        Assert.False(preview.CanMake);
        Assert.Null(preview.Plan);
    }

    [Fact]
    public async Task Make_approves_exactly_the_folders_shown_and_reports_each_by_name()
    {
        var world = new World();
        world.Lookup.Present["Installers"] = FolderEntryKind.Folder;
        var preview = await world.Service.PreviewAsync(world.RootId, "developer", TestContext.Current.CancellationToken);

        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);

        var run = Assert.Single(world.Executor.Runs);
        Assert.All(run.Plan.Operations, operation => Assert.IsType<CreateDirectoryOperation>(operation));
        Assert.Equal(run.Plan.Operations.Select(operation => operation.Id).ToHashSet(), run.Approval.SelectedOperationIds);
        Assert.Equal(run.Plan.Id, run.Approval.PlanId);
        Assert.Empty(run.Expected);
        Assert.Equal(["Projects", "Archives"], outcome.Made);
        Assert.Equal(["Installers"], outcome.AlreadyThere);
        Assert.Empty(outcome.NotMade);
        Assert.True(outcome.CanUndo);
        Assert.Null(outcome.LookAgain);
        Assert.Equal(2, outcome.MadeById.Count);
    }

    [Fact]
    public async Task Make_refuses_and_shows_the_fresh_list_when_the_folder_changed_since_the_preview()
    {
        var world = new World();
        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);
        world.Lookup.Present["Installers"] = FolderEntryKind.File;

        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);

        Assert.Equal("The folder changed while the list was open. Look again.", outcome.Problem);
        Assert.NotNull(outcome.LookAgain);
        Assert.Equal(FolderTemplateLineKind.Blocked, outcome.LookAgain.Lines.Single(line => line.Name == "Installers").Kind);
        Assert.Empty(outcome.Made);
        Assert.Empty(world.Executor.Runs);
    }

    [Fact]
    public async Task Make_after_the_permission_was_taken_back_makes_nothing()
    {
        var world = new World();
        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);
        world.Roots.TidyAllowed = false;

        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);

        Assert.Equal("DeskAI may not tidy this folder any more, so no folder was made.", outcome.Problem);
        Assert.Empty(world.Executor.Runs);
    }

    [Fact]
    public async Task A_folder_the_executor_found_already_there_is_never_listed_as_made()
    {
        var world = new World();
        world.Executor.AlreadyPresent.Add("Installers");
        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);

        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);

        Assert.Equal(["Screenshots"], outcome.Made);
        Assert.Equal(["Installers"], outcome.AlreadyThere);
        Assert.Single(outcome.MadeById);
    }

    [Fact]
    public async Task A_folder_the_executor_refused_is_named_with_its_reason()
    {
        var world = new World();
        world.Executor.Refuse["Installers"] = "Windows did not let DeskAI change it.";
        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);

        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);

        Assert.Equal(["Screenshots"], outcome.Made);
        var notMade = Assert.Single(outcome.NotMade);
        Assert.Equal("Installers", notMade.Name);
        Assert.Equal("Windows did not let DeskAI change it.", notMade.Reason);
        Assert.True(outcome.CanUndo);
    }

    [Fact]
    public async Task Typed_names_are_checked_before_anything_is_looked_at()
    {
        var world = new World();

        var bad = await world.Service.PreviewOwnAsync(world.RootId, @"Tax, ..\Outside", TestContext.Current.CancellationToken);
        var good = await world.Service.PreviewOwnAsync(world.RootId, "Tax 2026, Receipts", TestContext.Current.CancellationToken);

        Assert.StartsWith(@"..\Outside can't be used.", bad.Problem, StringComparison.Ordinal);
        Assert.False(bad.CanMake);
        Assert.Equal(FolderTemplate.OwnId, good.Template.Id);
        Assert.Equal(["Tax 2026", "Receipts"], good.Lines.Select(line => line.Name));
        Assert.True(good.CanMake);
        Assert.Equal(1, world.Lookup.Looks);
    }

    [Fact]
    public async Task Undo_needs_the_tidy_permission_and_names_what_was_left_in_place()
    {
        var world = new World();
        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);
        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);
        world.Executor.UndoRefuse["Installers"] = "The folder is no longer empty, so DeskAI left it in place.";

        world.Roots.TidyAllowed = false;
        var refused = await world.Service.UndoAsync(world.RootId, outcome.TransactionId!.Value, outcome.MadeById, TestContext.Current.CancellationToken);
        world.Roots.TidyAllowed = true;
        var undone = await world.Service.UndoAsync(world.RootId, outcome.TransactionId!.Value, outcome.MadeById, TestContext.Current.CancellationToken);

        Assert.True(refused.NeedsPermission);
        Assert.Equal(0, refused.Removed);
        Assert.False(undone.NeedsPermission);
        Assert.Equal(1, undone.Removed);
        Assert.Equal("Removed 1 of 2 folders. Installers: The folder is no longer empty, so DeskAI left it in place.", undone.Summary);
    }

    [Fact]
    public async Task FindLast_offers_the_latest_folder_only_run_and_nothing_once_a_tidy_ran_after_it()
    {
        var world = new World();
        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);
        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);

        var last = await world.Service.FindLastAsync(world.RootId, TestContext.Current.CancellationToken);

        Assert.Equal(outcome.TransactionId, last!.TransactionId);
        Assert.Equal(["Screenshots", "Installers"], last.Made);

        world.Journal.AddTidy(world.RootId);
        Assert.Null(await world.Service.FindLastAsync(world.RootId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindLast_offers_nothing_after_undo_and_a_preview_is_refused_while_a_question_is_open()
    {
        var world = new World();
        var preview = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);
        var outcome = await world.Service.MakeAsync(preview, TestContext.Current.CancellationToken);
        await world.Service.UndoAsync(world.RootId, outcome.TransactionId!.Value, outcome.MadeById, TestContext.Current.CancellationToken);

        Assert.Null(await world.Service.FindLastAsync(world.RootId, TestContext.Current.CancellationToken));

        world.Journal.AddInterrupted(world.RootId);
        var refused = await world.Service.PreviewAsync(world.RootId, "minimal", TestContext.Current.CancellationToken);
        Assert.Equal("DeskAI stopped part-way in this folder last time. Open it in Organize and answer the question first.", refused.Problem);
        Assert.True(await world.Service.HasOpenQuestionAsync(world.RootId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_disconnected_folder_is_said_to_be_gone()
    {
        var world = new World();

        var preview = await world.Service.PreviewAsync(Guid.NewGuid(), "minimal", TestContext.Current.CancellationToken);

        Assert.Equal("That folder is no longer connected.", preview.Problem);
    }

    [Fact]
    public void Constructor_cannot_reach_a_scanner_a_reader_a_credential_a_rule_evaluator_or_AI()
    {
        var forbidden = new[]
        {
            typeof(IFileScanner),
            typeof(IContentTextExtractor),
            typeof(ICredentialVault),
            typeof(IOrganizationSuggestionProvider),
            typeof(IFileIndex),
            typeof(IMetadataIndexService),
            typeof(RuleSetEvaluator),
        };

        var dependencies = typeof(FolderTemplateService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));
        Assert.Contains(typeof(IFolderTidyExecutor), dependencies);
    }

    /// <summary>No type in the Workspace namespace may hold the executor; only the template service does.</summary>
    [Fact]
    public void No_Workspace_type_holds_an_executor()
    {
        var workspaceTypes = typeof(Workspace.StarterPackService).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(Workspace.StarterPackService).Namespace);

        foreach (var type in workspaceTypes)
        {
            var dependencies = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType);
            Assert.DoesNotContain(typeof(IFolderTidyExecutor), dependencies);
            Assert.DoesNotContain(typeof(IOperationJournal), dependencies);
        }
    }

    private sealed class World
    {
        public World(bool tidyAllowed = true)
        {
            RootId = Guid.NewGuid();
            Roots = new FakeRoots(RootId) { TidyAllowed = tidyAllowed };
            Journal = new FakeJournal();
            Executor = new FakeExecutor(Journal);
            Service = new FolderTemplateService(Roots, Folders, Lookup, Safety, Executor, Journal, new FixedClock(Now));
        }

        public Guid RootId { get; }

        public FakeRoots Roots { get; }

        public FakeFolders Folders { get; } = new();

        public FakeLookup Lookup { get; } = new();

        public FakeSafety Safety { get; } = new();

        public FakeExecutor Executor { get; }

        public FakeJournal Journal { get; }

        public FolderTemplateService Service { get; }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeRoots(Guid rootId) : IAuthorizedRootRepository
    {
        public bool TidyAllowed { get; set; } = true;

        public Task<AuthorizedRoot?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id != rootId)
            {
                return Task.FromResult<AuthorizedRoot?>(null);
            }

            var root = AuthorizedRoot.Create(
                rootId, Path.Combine(Path.GetTempPath(), "deskai-tests", "Downloads"), "Downloads",
                RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);
            return Task.FromResult<AuthorizedRoot?>(TidyAllowed ? root.WithTidyAllowedSince(Now) : root);
        }

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AllowTidyAsync(Guid id, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopTidyAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeFolders : IReadOnlyFolderService
    {
        public string? Problem { get; set; }

        public Task<string?> CheckStillSafeAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) => Task.FromResult(Problem);

        public Task<FolderPreviewResult> AuthorizeAndPreviewAsync(string selectedPath, Core.Files.MetadataScanOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuthorizedRoot>> ListAuthorizedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RevokeAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeLookup : IFolderNameLookup
    {
        public Dictionary<string, FolderEntryKind> Present { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Looks { get; private set; }

        public Task<IReadOnlyList<FolderEntryPresence>> LookAsync(AuthorizedRoot root, IReadOnlyList<string> names, CancellationToken cancellationToken = default)
        {
            Looks++;
            IReadOnlyList<FolderEntryPresence> result = names
                .Select(name =>
                {
                    var key = Present.Keys.FirstOrDefault(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
                    return key is null ? new FolderEntryPresence(name, FolderEntryKind.Missing) : new FolderEntryPresence(key, Present[key]);
                })
                .ToArray();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeSafety : IPlanSafetyCheck
    {
        public HashSet<string> Blocked { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string PolicyVersion => "1";

        public IReadOnlyDictionary<Guid, string> FindBlocked(OrganizationPlan plan, AuthorizedRoot root) =>
            plan.Operations
                .OfType<CreateDirectoryOperation>()
                .Where(create => Blocked.Contains(create.DestinationRelativePath))
                .ToDictionary(create => create.Id, _ => "The path overlaps a user-protected entry.");

        public bool IsProtected(AuthorizedRoot root, string relativePath) => Blocked.Contains(relativePath);
    }

    /// <summary>Stands in for the executor: records each run and writes the journal the way the real one does.</summary>
    private sealed class FakeExecutor(FakeJournal journal) : IFolderTidyExecutor
    {
        public List<(OrganizationPlan Plan, Approval Approval, IReadOnlyDictionary<Guid, ExpectedFile> Expected)> Runs { get; } = [];

        public HashSet<string> AlreadyPresent { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Refuse { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> UndoRefuse { get; } = new(StringComparer.OrdinalIgnoreCase);

        public async Task<ExecutionResult> ExecuteAsync(OrganizationPlan plan, Approval approval, IReadOnlyDictionary<Guid, ExpectedFile> expected, CancellationToken cancellationToken = default)
        {
            Runs.Add((plan, approval, expected));
            var results = new List<OperationExecutionResult>();
            var entries = new List<OperationJournalEntry>();
            var sequence = 0;
            foreach (var create in plan.Operations.OfType<CreateDirectoryOperation>().Where(operation => approval.SelectedOperationIds.Contains(operation.Id)))
            {
                var name = create.DestinationRelativePath;
                var (state, outcome, error) = Refuse.TryGetValue(name, out var reason)
                    ? (JournalOperationState.Failed, ExecutionOutcome.Failed, (string?)reason)
                    : AlreadyPresent.Contains(name)
                        ? (JournalOperationState.AlreadyPresent, ExecutionOutcome.Completed, null)
                        : (JournalOperationState.Completed, ExecutionOutcome.Completed, null);
                results.Add(new OperationExecutionResult(create.Id, outcome, error));
                entries.Add(new OperationJournalEntry(sequence++, create.Id, PlanOperationKind.CreateDirectory, null, name, null, null, state, error));
            }

            var id = Guid.NewGuid();
            var state2 = results.All(item => item.Outcome == ExecutionOutcome.Completed)
                ? ExecutionTransactionState.Completed
                : results.Any(item => item.Outcome == ExecutionOutcome.Completed) ? ExecutionTransactionState.PartiallyCompleted : ExecutionTransactionState.Failed;
            await journal.CreateAsync(new ExecutionJournalEntry(id, plan.Id, plan.Revision, approval.Id, ExecutionTransactionKind.Execute, null, state2, Now, Now, entries), cancellationToken);
            journal.RootOf[plan.Id] = plan.RootId;
            return new ExecutionResult(id, plan.Id, results, Now, Now);
        }

        public async Task<UndoResult> UndoAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            var original = await journal.FindAsync(transactionId, cancellationToken) ?? throw new InvalidOperationException("DeskAI could not find that tidy in its history.");
            var results = original.Operations
                .Where(operation => operation.State == JournalOperationState.Completed)
                .Select(operation => UndoRefuse.TryGetValue(operation.DestinationRelativePath, out var reason)
                    ? new OperationExecutionResult(operation.OperationId, ExecutionOutcome.Failed, reason)
                    : new OperationExecutionResult(operation.OperationId, ExecutionOutcome.Completed, null))
                .ToArray();
            var undoId = Guid.NewGuid();
            var state = results.All(item => item.Outcome == ExecutionOutcome.Completed) ? ExecutionTransactionState.Completed : ExecutionTransactionState.PartiallyCompleted;
            await journal.CreateAsync(new ExecutionJournalEntry(undoId, original.PlanId, original.PlanRevision, Guid.Empty, ExecutionTransactionKind.Undo, transactionId, state, Now, Now, []), cancellationToken);
            if (state == ExecutionTransactionState.Completed)
            {
                await journal.UpdateTransactionAsync(transactionId, ExecutionTransactionState.Undone, Now, cancellationToken);
            }

            return new UndoResult(undoId, transactionId, results, Now, Now);
        }

        public Task<IReadOnlyList<ExecutionJournalEntry>> CheckInterruptedAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ExecutionTransactionState> CloseInterruptedAsync(Guid rootId, Guid transactionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeJournal : IOperationJournal
    {
        private readonly List<ExecutionJournalEntry> _entries = [];

        public Dictionary<Guid, Guid> RootOf { get; } = [];

        public void AddTidy(Guid rootId)
        {
            var planId = Guid.NewGuid();
            RootOf[planId] = rootId;
            _entries.Add(new ExecutionJournalEntry(Guid.NewGuid(), planId, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
                ExecutionTransactionState.Completed, Now.AddMinutes(1), Now.AddMinutes(1),
                [new OperationJournalEntry(0, Guid.NewGuid(), PlanOperationKind.MoveFile, "a.pdf", @"Documents\a.pdf", 1, Now, JournalOperationState.Completed, null)]));
        }

        public void AddInterrupted(Guid rootId)
        {
            var planId = Guid.NewGuid();
            RootOf[planId] = rootId;
            _entries.Add(new ExecutionJournalEntry(Guid.NewGuid(), planId, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
                ExecutionTransactionState.RecoveryRequired, Now.AddMinutes(2), null, []));
        }

        public Task CreateAsync(ExecutionJournalEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task UpdateOperationAsync(Guid transactionId, Guid operationId, JournalOperationState state, string? failureMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateTransactionAsync(Guid transactionId, ExecutionTransactionState state, DateTimeOffset? finishedAtUtc, CancellationToken cancellationToken = default)
        {
            var index = _entries.FindIndex(entry => entry.Id == transactionId);
            _entries[index] = _entries[index] with { State = state, FinishedAtUtc = finishedAtUtc };
            return Task.CompletedTask;
        }

        public Task<ExecutionJournalEntry?> FindAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.FirstOrDefault(entry => entry.Id == transactionId));

        public Task<IReadOnlyList<ExecutionJournalEntry>> ListRecentAsync(int maximumCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExecutionJournalEntry>>(_entries.OrderByDescending(entry => entry.StartedAtUtc).Take(maximumCount).ToArray());

        public Task<IReadOnlyList<ExecutionJournalEntry>> ListIncompleteAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExecutionJournalEntry>> ListForRootAsync(Guid rootId, int maximumCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExecutionJournalEntry>>(_entries
                .Where(entry => RootOf.GetValueOrDefault(entry.PlanId) == rootId)
                .OrderByDescending(entry => entry.StartedAtUtc)
                .ThenByDescending(entry => _entries.IndexOf(entry))
                .Take(maximumCount)
                .ToArray());
    }
}
