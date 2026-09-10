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
public sealed class TemporaryDemoPlanExecutor : IPlanExecutor, IUndoService
{
    private const string MarkerName = ".deskai-demo-root";
    private const string OwnedPrefix = "DeskAI.Demo.";
    private readonly string _canonicalTempRoot;
    private readonly string _basePath;
    private readonly string _markerToken = Guid.NewGuid().ToString("N");
    private readonly PlanValidator _validator;
    private readonly IClock _clock;
    private readonly IOperationJournal _journal;
    private readonly IAuthorizedRootRepository _rootRepository;
    private readonly IPlanRepository _planRepository;
    private bool _prepared;

    public TemporaryDemoPlanExecutor(
        IOptions<DemoWorkspaceOptions> options,
        PlanValidator validator,
        IClock clock,
        IOperationJournal journal,
        IAuthorizedRootRepository rootRepository,
        IPlanRepository planRepository)
    {
        ArgumentNullException.ThrowIfNull(options);
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _rootRepository = rootRepository ?? throw new ArgumentNullException(nameof(rootRepository));
        _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
        _canonicalTempRoot = Normalize(Path.GetTempPath());
        _basePath = Normalize(options.Value.BasePath);
        EnsureContained(_canonicalTempRoot, _basePath, "Demo base must stay inside the system temporary directory.");

        var rootPath = Path.Combine(_basePath, OwnedPrefix + Guid.NewGuid().ToString("N"));
        Root = AuthorizedRoot.Create(
            Guid.NewGuid(), rootPath, "Safe temporary demo", RootAccessLevel.Allowed,
            RootAuthorizationScope.ControlledDemo);
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
        var started = _clock.UtcNow;
        var results = new List<OperationExecutionResult>();
        var selected = plan.Operations.Where(operation => approval.SelectedOperationIds.Contains(operation.Id)).ToArray();

        var refusal = ValidateEnvelope(plan, approval, selected);
        if (refusal is not null)
        {
            results.AddRange(selected.Select(operation => Failed(operation.Id, refusal)));
            return new ExecutionResult(Guid.NewGuid(), plan.Id, results, started, _clock.UtcNow);
        }

        try
        {
            VerifyOwnedRoot();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            results.AddRange(selected.Select(operation => Failed(operation.Id, exception.Message)));
            return new ExecutionResult(Guid.NewGuid(), plan.Id, results, started, _clock.UtcNow);
        }

        await _rootRepository.SaveAsync(Root, cancellationToken).ConfigureAwait(false);
        await _planRepository.SaveAsync(plan, cancellationToken).ConfigureAwait(false);
        var transactionId = Guid.NewGuid();
        var journalOperations = selected
            .Select((operation, index) => CaptureIntent(index, operation))
            .ToArray();
        await _journal.CreateAsync(new ExecutionJournalEntry(
            transactionId,
            plan.Id,
            plan.Revision,
            approval.Id,
            ExecutionTransactionKind.Execute,
            null,
            ExecutionTransactionState.Prepared,
            started,
            null,
            journalOperations), cancellationToken).ConfigureAwait(false);
        await _journal.UpdateTransactionAsync(
            transactionId, ExecutionTransactionState.Executing, null, cancellationToken).ConfigureAwait(false);

        foreach (var operation in selected)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new OperationExecutionResult(operation.Id, ExecutionOutcome.Cancelled, "Cancelled before this operation started."));
                await _journal.UpdateOperationAsync(
                    transactionId, operation.Id, JournalOperationState.Cancelled,
                    "Cancelled before this operation started.", CancellationToken.None).ConfigureAwait(false);
                break;
            }

            try
            {
                await _journal.UpdateOperationAsync(
                    transactionId, operation.Id, JournalOperationState.InProgress, null, cancellationToken).ConfigureAwait(false);
                VerifyOwnedRoot();
                var finishedState = ExecuteOperation(operation);
                results.Add(new OperationExecutionResult(operation.Id, ExecutionOutcome.Completed, null));
                await _journal.UpdateOperationAsync(
                    transactionId, operation.Id, finishedState, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                results.Add(Failed(operation.Id, exception.Message));
                await _journal.UpdateOperationAsync(
                    transactionId, operation.Id, JournalOperationState.Failed, exception.Message, CancellationToken.None).ConfigureAwait(false);
            }

            await Task.Yield();
        }

        var finished = _clock.UtcNow;
        var transactionState = DetermineState(results, selected.Length);
        await _journal.UpdateTransactionAsync(transactionId, transactionState, finished, CancellationToken.None).ConfigureAwait(false);
        return new ExecutionResult(transactionId, plan.Id, results, started, finished);
    }

    public async Task<UndoResult> UndoAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var original = await _journal.FindAsync(transactionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The activity record could not be found.");
        if (original.Kind != ExecutionTransactionKind.Execute ||
            original.State is not (ExecutionTransactionState.Completed or ExecutionTransactionState.PartiallyCompleted))
        {
            throw new InvalidOperationException("This activity is not eligible for undo.");
        }

        var recent = await _journal.ListRecentAsync(100, cancellationToken).ConfigureAwait(false);
        if (recent.Any(item => item.Kind == ExecutionTransactionKind.Undo &&
                               item.OriginalTransactionId == transactionId &&
                               item.State is ExecutionTransactionState.Completed or ExecutionTransactionState.PartiallyCompleted))
        {
            throw new InvalidOperationException("This activity has already been undone.");
        }

        VerifyOwnedRoot();
        var started = _clock.UtcNow;
        var undoId = Guid.NewGuid();
        var reversible = original.Operations
            .Where(operation => operation.State == JournalOperationState.Completed)
            .Reverse()
            .Select((operation, index) => operation with
            {
                Sequence = index,
                State = JournalOperationState.Pending,
                Error = null,
            })
            .ToArray();
        await _journal.CreateAsync(new ExecutionJournalEntry(
            undoId, original.PlanId, original.PlanRevision, Guid.Empty,
            ExecutionTransactionKind.Undo, transactionId,
            ExecutionTransactionState.Prepared, started, null, reversible), cancellationToken).ConfigureAwait(false);
        await _journal.UpdateTransactionAsync(
            undoId, ExecutionTransactionState.Executing, null, cancellationToken).ConfigureAwait(false);

        var results = new List<OperationExecutionResult>();
        foreach (var operation in reversible)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new OperationExecutionResult(operation.OperationId, ExecutionOutcome.Cancelled, "Undo was cancelled before this item."));
                await _journal.UpdateOperationAsync(
                    undoId, operation.OperationId, JournalOperationState.Cancelled,
                    "Undo was cancelled before this item.", CancellationToken.None).ConfigureAwait(false);
                break;
            }

            try
            {
                await _journal.UpdateOperationAsync(
                    undoId, operation.OperationId, JournalOperationState.InProgress, null, cancellationToken).ConfigureAwait(false);
                VerifyOwnedRoot();
                UndoOperation(operation);
                results.Add(new OperationExecutionResult(operation.OperationId, ExecutionOutcome.Completed, null));
                await _journal.UpdateOperationAsync(
                    undoId, operation.OperationId, JournalOperationState.Completed, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                results.Add(Failed(operation.OperationId, exception.Message));
                await _journal.UpdateOperationAsync(
                    undoId, operation.OperationId, JournalOperationState.Failed, exception.Message, CancellationToken.None).ConfigureAwait(false);
            }
        }

        var finished = _clock.UtcNow;
        var state = DetermineState(results, reversible.Length);
        await _journal.UpdateTransactionAsync(undoId, state, finished, CancellationToken.None).ConfigureAwait(false);
        if (state == ExecutionTransactionState.Completed)
        {
            await _journal.UpdateTransactionAsync(
                original.Id, ExecutionTransactionState.Undone, original.FinishedAtUtc, CancellationToken.None).ConfigureAwait(false);
        }

        return new UndoResult(undoId, transactionId, results, started, finished);
    }

    public async Task<int> RecoverIncompleteAsync(CancellationToken cancellationToken = default)
    {
        var incomplete = await _journal.ListIncompleteAsync(cancellationToken).ConfigureAwait(false);
        foreach (var transaction in incomplete)
        {
            var storedPlan = await _planRepository.FindAsync(
                transaction.PlanId, transaction.PlanRevision, cancellationToken).ConfigureAwait(false);
            if (storedPlan is not null && storedPlan.RootId != Root.Id)
            {
                await _journal.UpdateTransactionAsync(
                    transaction.Id, ExecutionTransactionState.RecoveryRequired, null, cancellationToken).ConfigureAwait(false);
                continue;
            }

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

        return incomplete.Count;
    }

    private string? ValidateEnvelope(
        OrganizationPlan plan,
        Approval approval,
        PlanOperation[] selected)
    {
        if (!_prepared)
        {
            return "The temporary demo workspace was not prepared.";
        }

        if (plan.RootId != Root.Id || approval.PlanId != plan.Id || approval.PlanRevision != plan.Revision ||
            !string.Equals(approval.PolicyVersion, plan.PolicyVersion, StringComparison.Ordinal))
        {
            return "The approval does not match this exact plan revision and policy.";
        }

        var knownIds = plan.Operations.Select(operation => operation.Id).ToHashSet();
        if (approval.Id == Guid.Empty || !approval.SelectedOperationIds.IsSubsetOf(knownIds))
        {
            return "The approval contains an unknown operation or invalid identity.";
        }

        if (selected.Length == 0)
        {
            return "No operations were selected.";
        }

        var report = _validator.Validate(plan, Root);
        foreach (var operation in selected)
        {
            if (report.Operations.Any(item => item.OperationId == operation.Id && item.Result.Status == ValidationStatus.Blocked))
            {
                return "A selected operation is blocked by the current safety policy.";
            }
        }

        return null;
    }

    /// <summary>Carries out one operation and says what the journal should record for it.</summary>
    private JournalOperationState ExecuteOperation(PlanOperation operation)
    {
        switch (operation)
        {
            case CreateDirectoryOperation create:
                // A folder that was already there is recorded as such, never as created.
                // Recording it as created once let undo delete an empty folder the person
                // had before the run.
                return CreateDirectory(create.DestinationRelativePath)
                    ? JournalOperationState.Completed
                    : JournalOperationState.AlreadyPresent;
            case MoveFileOperation move:
                MoveFile(move.SourceRelativePath, move.DestinationRelativePath);
                return JournalOperationState.Completed;
            case RenameFileOperation rename:
                MoveFile(rename.SourceRelativePath, rename.DestinationRelativePath);
                return JournalOperationState.Completed;
            default:
                throw new InvalidOperationException("The operation type is not supported by the demo executor.");
        }
    }

    private OperationJournalEntry CaptureIntent(int sequence, PlanOperation operation)
    {
        VerifyOwnedRoot();
        var source = operation switch
        {
            MoveFileOperation move => move.SourceRelativePath,
            RenameFileOperation rename => rename.SourceRelativePath,
            _ => null,
        };
        var destination = operation switch
        {
            CreateDirectoryOperation create => create.DestinationRelativePath,
            MoveFileOperation move => move.DestinationRelativePath,
            RenameFileOperation rename => rename.DestinationRelativePath,
            _ => throw new InvalidOperationException("The operation type is not journalable."),
        };
        long? size = null;
        DateTimeOffset? modified = null;
        if (source is not null)
        {
            var sourcePath = Resolve(source);
            RejectReparsePointsInExistingPath(Root.CanonicalPath, sourcePath);
            if (File.Exists(sourcePath))
            {
                var info = new FileInfo(sourcePath);
                size = info.Length;
                modified = info.LastWriteTimeUtc;
            }
        }

        return new OperationJournalEntry(
            sequence, operation.Id, operation.Kind, source, destination,
            size, modified, JournalOperationState.Pending, null);
    }

    private void UndoOperation(OperationJournalEntry operation)
    {
        if (operation.Kind == PlanOperationKind.CreateDirectory)
        {
            var directory = Resolve(operation.DestinationRelativePath);
            RejectReparsePointsInExistingPath(Root.CanonicalPath, directory);
            if (!Directory.Exists(directory))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                throw new IOException("The folder is no longer empty, so DeskAI left it in place.");
            }

            Directory.Delete(directory, recursive: false);
            return;
        }

        if (operation.SourceRelativePath is null)
        {
            throw new InvalidOperationException("The journal is missing the original source path.");
        }

        var originalSource = Resolve(operation.SourceRelativePath);
        var currentDestination = Resolve(operation.DestinationRelativePath);
        RejectReparsePointsInExistingPath(Root.CanonicalPath, currentDestination);
        var sourceParent = Path.GetDirectoryName(originalSource)
            ?? throw new InvalidOperationException("The original source has no parent folder.");
        RejectReparsePointsInExistingPath(Root.CanonicalPath, sourceParent);
        if (File.Exists(originalSource) || Directory.Exists(originalSource))
        {
            throw new IOException("The original location is occupied, so undo was refused.");
        }

        if (!MatchesRecordedFile(currentDestination, operation))
        {
            throw new IOException("The moved file changed after organization, so undo was refused.");
        }

        File.Move(currentDestination, originalSource);
    }

    private bool DidOperationFinish(OperationJournalEntry operation)
    {
        try
        {
            VerifyOwnedRoot();
            if (operation.Kind == PlanOperationKind.CreateDirectory)
            {
                return Directory.Exists(Resolve(operation.DestinationRelativePath));
            }

            return operation.SourceRelativePath is not null &&
                   !File.Exists(Resolve(operation.SourceRelativePath)) &&
                   MatchesRecordedFile(Resolve(operation.DestinationRelativePath), operation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool MatchesRecordedFile(string path, OperationJournalEntry operation)
    {
        if (!File.Exists(path) || Directory.Exists(path) || operation.BeforeSizeBytes is null || operation.BeforeModifiedAtUtc is null)
        {
            return false;
        }

        var info = new FileInfo(path);
        return info.Length == operation.BeforeSizeBytes &&
               info.LastWriteTimeUtc == operation.BeforeModifiedAtUtc.Value.UtcDateTime;
    }

    private static ExecutionTransactionState DetermineState(
        IReadOnlyCollection<OperationExecutionResult> results,
        int expectedCount)
    {
        var completed = results.Count(item => item.Outcome == ExecutionOutcome.Completed);
        if (completed == expectedCount)
        {
            return ExecutionTransactionState.Completed;
        }

        if (completed > 0)
        {
            return ExecutionTransactionState.PartiallyCompleted;
        }

        return results.Any(item => item.Outcome == ExecutionOutcome.Cancelled)
            ? ExecutionTransactionState.Cancelled
            : ExecutionTransactionState.Failed;
    }

    /// <returns>True if this call created the folder; false if it already existed.</returns>
    private bool CreateDirectory(string relativePath)
    {
        var destination = Resolve(relativePath);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("The destination has no parent.");
        RejectReparsePointsInExistingPath(Root.CanonicalPath, parent);
        if (!Directory.Exists(parent))
        {
            throw new InvalidOperationException("The parent folder was not selected for creation first.");
        }

        if (File.Exists(destination))
        {
            throw new IOException("A file already occupies the requested folder path.");
        }

        if (Directory.Exists(destination))
        {
            return false;
        }

        Directory.CreateDirectory(destination);
        return true;
    }

    private void MoveFile(string sourceRelativePath, string destinationRelativePath)
    {
        var source = Resolve(sourceRelativePath);
        var destination = Resolve(destinationRelativePath);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("The destination has no parent.");
        RejectReparsePointsInExistingPath(Root.CanonicalPath, source);
        RejectReparsePointsInExistingPath(Root.CanonicalPath, parent);

        if (!File.Exists(source) || Directory.Exists(source))
        {
            throw new IOException("The source file is missing or is not a regular file.");
        }

        if (!Directory.Exists(parent))
        {
            throw new IOException("The destination folder does not exist.");
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("The destination already exists; overwrite was refused.");
        }

        File.Move(source, destination);
    }

    private string Resolve(string relativePath)
    {
        var policyResult = new WindowsPathPolicy().ValidateRelativePath(Root, relativePath);
        if (policyResult.Status == ValidationStatus.Blocked)
        {
            throw new InvalidOperationException(policyResult.Explanation);
        }

        var resolved = Path.GetFullPath(relativePath, Root.CanonicalPath);
        EnsureContained(Root.CanonicalPath, resolved, "The operation escaped the temporary demo root.");
        return resolved;
    }

    private async Task WriteDummyAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        VerifyOwnedRoot();
        var destination = Resolve(relativePath);
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
        var relative = Path.GetRelativePath(Normalize(ancestor), Normalize(candidate));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static OperationExecutionResult Failed(Guid operationId, string error) =>
        new(operationId, ExecutionOutcome.Failed, error);

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
