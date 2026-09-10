using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Execution;

/// <summary>
/// Executes only inside a generated, marker-protected directory below the system temp root.
/// It deliberately has no API for accepting an arbitrary user path.
/// </summary>
/// <remarks>
/// How files are moved, recorded, and undone is shared with the real-folder executor in
/// <see cref="FileOperationRunner"/>. What is particular to the practice workspace is how it is
/// trusted: a unique folder under a dedicated temp base, holding a marker whose secret only
/// this process knows.
/// </remarks>
public sealed class TemporaryDemoPlanExecutor : IPlanExecutor, IUndoService
{
    private const string MarkerName = ".deskai-demo-root";
    private const string OwnedPrefix = "DeskAI.Demo.";
    private static readonly IReadOnlyDictionary<Guid, ExpectedFile> NoExpectations = new Dictionary<Guid, ExpectedFile>();
    private readonly string _canonicalTempRoot;
    private readonly string _basePath;
    private readonly string _markerToken = Guid.NewGuid().ToString("N");
    private readonly IClock _clock;
    private readonly IOperationJournal _journal;
    private readonly IAuthorizedRootRepository _rootRepository;
    private readonly IPlanRepository _planRepository;
    private readonly FileOperationRunner _runner;
    private readonly DemoTrust _trust;
    private bool _prepared;

    public TemporaryDemoPlanExecutor(
        IOptions<DemoWorkspaceOptions> options,
        PlanValidator validator,
        IClock clock,
        IOperationJournal journal,
        IAuthorizedRootRepository rootRepository,
        IPlanRepository planRepository,
        IPathPolicy? pathPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _rootRepository = rootRepository ?? throw new ArgumentNullException(nameof(rootRepository));
        _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
        _canonicalTempRoot = FileOperationRunner.Normalize(Path.GetTempPath());
        _basePath = FileOperationRunner.Normalize(options.Value.BasePath);
        EnsureContained(_canonicalTempRoot, _basePath, "Demo base must stay inside the system temporary directory.");

        var rootPath = Path.Combine(_basePath, OwnedPrefix + Guid.NewGuid().ToString("N"));
        Root = AuthorizedRoot.Create(
            Guid.NewGuid(), rootPath, "Safe temporary demo", RootAccessLevel.Allowed,
            RootAuthorizationScope.ControlledDemo);
        _runner = new FileOperationRunner(validator, pathPolicy ?? new WindowsPathPolicy(), clock, journal, planRepository);
        _trust = new DemoTrust(this);
    }

    public AuthorizedRoot Root { get; }
    public bool IsPrepared => _prepared;

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (_prepared)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        VerifyRootShape();
        RejectReparsePointsInExistingPath(_canonicalTempRoot, _basePath);
        Directory.CreateDirectory(_basePath);
        RejectReparsePointsInExistingPath(_canonicalTempRoot, _basePath);
        if (Directory.Exists(Root.CanonicalPath) || File.Exists(Root.CanonicalPath))
        {
            throw new InvalidOperationException("The unique temporary demo path already exists.");
        }

        Directory.CreateDirectory(Root.CanonicalPath);

        await File.WriteAllTextAsync(Path.Combine(Root.CanonicalPath, MarkerName), _markerToken, cancellationToken)
            .ConfigureAwait(false);
        await WriteDummyAsync(@"Inbox\course-notes.pdf", "Generated course notes A", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync(@"Archive\course-notes.pdf", "Generated course notes B", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync("Screenshot 2026-09-07.png", "Generated screenshot placeholder", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync("semester-budget.xlsx", "Generated spreadsheet placeholder", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync(@"Documents\reading-list.md", "Generated reading list", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync("unrecognized.deskai-demo", "Generated unknown placeholder", cancellationToken).ConfigureAwait(false);
        _prepared = true;
        VerifyOwnedRoot();
    }

    public async Task<ExecutionResult> ExecuteAsync(
        OrganizationPlan plan,
        Approval approval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(approval);
        if (!_prepared)
        {
            var now = _clock.UtcNow;
            var refused = plan.Operations
                .Where(operation => approval.SelectedOperationIds.Contains(operation.Id))
                .Select(operation => new OperationExecutionResult(
                    operation.Id, ExecutionOutcome.Failed, "The temporary demo workspace was not prepared."))
                .ToArray();
            return new ExecutionResult(Guid.NewGuid(), plan.Id, refused, now, now);
        }

        return await _runner.ExecuteAsync(_trust, plan, approval, NoExpectations, cancellationToken).ConfigureAwait(false);
    }

    public Task<UndoResult> UndoAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        _runner.UndoAsync(_trust, transactionId, cancellationToken);

    public async Task<int> RecoverIncompleteAsync(CancellationToken cancellationToken = default)
    {
        var incomplete = await _journal.ListIncompleteAsync(cancellationToken).ConfigureAwait(false);
        var handled = 0;
        foreach (var transaction in incomplete)
        {
            var storedPlan = await _planRepository.FindAsync(
                transaction.PlanId, transaction.PlanRevision, cancellationToken).ConfigureAwait(false);
            if (storedPlan is not null && storedPlan.RootId != Root.Id)
            {
                // A record from a folder someone connected is not this workspace's to judge:
                // it is left exactly as it is for that folder's own recovery. Only records from
                // an earlier practice session, whose workspace this process cannot prove it
                // owns, are marked for review.
                var owner = await _rootRepository.FindAsync(storedPlan.RootId, cancellationToken).ConfigureAwait(false);
                if (owner is not null && owner.AuthorizationScope != RootAuthorizationScope.ControlledDemo)
                {
                    continue;
                }

                await _journal.UpdateTransactionAsync(
                    transaction.Id, ExecutionTransactionState.RecoveryRequired, null, cancellationToken).ConfigureAwait(false);
                handled++;
                continue;
            }

            handled++;
            foreach (var operation in transaction.Operations)
            {
                if (operation.State == JournalOperationState.Pending)
                {
                    await _journal.UpdateOperationAsync(
                        transaction.Id, operation.OperationId, JournalOperationState.Cancelled,
                        "Recovered before the operation began.", cancellationToken).ConfigureAwait(false);
                }
                else if (operation.State == JournalOperationState.InProgress)
                {
                    var finished = transaction.Kind == ExecutionTransactionKind.Execute && DidOperationFinish(operation);

                    // An interrupted folder creation cannot show whether the folder was made
                    // by DeskAI or was already there, so it is assumed to have been there.
                    // The cost is an empty folder undo leaves behind; the alternative risks
                    // undo deleting a folder that belonged to the person.
                    var recoveredState = !finished
                        ? JournalOperationState.Failed
                        : operation.Kind == PlanOperationKind.CreateDirectory
                            ? JournalOperationState.AlreadyPresent
                            : JournalOperationState.Completed;
                    var explanation = finished
                        ? "Recovered by verifying the resulting filesystem state."
                        : "Could not prove that the interrupted operation completed; manual review is required.";
                    await _journal.UpdateOperationAsync(
                        transaction.Id, operation.OperationId, recoveredState, explanation, cancellationToken).ConfigureAwait(false);
                }
            }

            var refreshed = await _journal.FindAsync(transaction.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The recovery journal disappeared.");
            var completed = refreshed.Operations.Count(item =>
                item.State is JournalOperationState.Completed or JournalOperationState.AlreadyPresent);
            var state = completed == refreshed.Operations.Count
                ? ExecutionTransactionState.Completed
                : completed > 0
                    ? ExecutionTransactionState.PartiallyCompleted
                    : ExecutionTransactionState.Failed;
            await _journal.UpdateTransactionAsync(transaction.Id, state, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        return handled;
    }

    private bool DidOperationFinish(OperationJournalEntry operation)
    {
        try
        {
            VerifyOwnedRoot();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }

        return _runner.DidOperationFinish(Root, operation);
    }

    private async Task WriteDummyAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        VerifyOwnedRoot();
        var destination = Path.GetFullPath(relativePath, Root.CanonicalPath);
        EnsureContained(Root.CanonicalPath, destination, "The operation escaped the temporary demo root.");
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Dummy file path has no parent.");
        RejectReparsePointsInExistingPath(Root.CanonicalPath, parent);
        Directory.CreateDirectory(parent);
        RejectReparsePointsInExistingPath(Root.CanonicalPath, parent);
        await File.WriteAllTextAsync(destination, content, cancellationToken).ConfigureAwait(false);
    }

    private void VerifyOwnedRoot()
    {
        VerifyRootShape();
        RejectReparsePointsInExistingPath(_canonicalTempRoot, Root.CanonicalPath);
        var marker = Path.Combine(Root.CanonicalPath, MarkerName);
        if (!File.Exists(marker) || File.GetAttributes(marker).HasFlag(FileAttributes.ReparsePoint) ||
            !string.Equals(File.ReadAllText(marker), _markerToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The temporary demo ownership marker is missing or invalid.");
        }
    }

    private void VerifyRootShape()
    {
        EnsureContained(_basePath, Root.CanonicalPath, "The demo root escaped its dedicated base directory.");
        if (!Path.GetFileName(Root.CanonicalPath).StartsWith(OwnedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The demo root does not have the required owned name.");
        }
    }

    private static void RejectReparsePointsInExistingPath(string ancestor, string candidate)
    {
        EnsureContained(ancestor, candidate, "The checked path escaped its expected ancestor.");
        var relative = Path.GetRelativePath(ancestor, candidate);
        var current = ancestor;
        if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("A reparse point is not allowed in the demo path.");
        }

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current)) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException("A reparse point is not allowed in the demo path.");
            }
        }
    }

    private static void EnsureContained(string ancestor, string candidate, string message)
    {
        try
        {
            FileOperationRunner.EnsureContained(ancestor, candidate);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>The practice workspace is trusted by its shape and its secret marker.</summary>
    private sealed class DemoTrust(TemporaryDemoPlanExecutor owner) : IRootTrust
    {
        public AuthorizedRoot Root => owner.Root;

        public Task VerifyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!owner._prepared)
            {
                throw new InvalidOperationException("The temporary demo workspace was not prepared.");
            }

            owner.VerifyOwnedRoot();
            return Task.CompletedTask;
        }

        public Task PersistRootAsync(CancellationToken cancellationToken) =>
            owner._rootRepository.SaveAsync(owner.Root, cancellationToken);
    }
}
