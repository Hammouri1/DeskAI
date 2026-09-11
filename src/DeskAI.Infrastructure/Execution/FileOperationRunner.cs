using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Execution;

/// <summary>
/// How an executor proves, right now, that it may still change files in its folder.
/// </summary>
/// <remarks>
/// The practice workspace proves it with its ownership marker; a real folder with a live check
/// of its tidy permission and its path. Everything else about moving files is shared in
/// <see cref="FileOperationRunner"/>, so there is one set of move rules rather than two that
/// can drift apart.
/// </remarks>
internal interface IRootTrust
{
    AuthorizedRoot Root { get; }

    /// <summary>Throws <see cref="InvalidOperationException"/> with a plain reason if not.</summary>
    Task VerifyAsync(CancellationToken cancellationToken);

    /// <summary>Makes sure the folder is recorded before a plan for it is saved.</summary>
    Task PersistRootAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The only code in DeskAI that moves a file or creates or removes a folder: the shared rules
/// for running an approved plan, recording it, and undoing it.
/// </summary>
/// <remarks>
/// <para>
/// Before anything changes, every intended operation is written to the journal with the facts
/// of the file it moves. Before each operation the folder's trust is checked again, and before
/// each move the file is checked again: still there, still a plain file, not a link, the same
/// size and last-changed time that was recorded, not online-only, hidden, or system, and the
/// destination still free. The move itself refuses to overwrite, so a name taken in the last
/// instant still fails safely. Each refusal is that file's own outcome; the rest continue.
/// </para>
/// <para>
/// Messages are written for the person reading the result, not for a developer.
/// </para>
/// </remarks>
internal sealed class FileOperationRunner(
    PlanValidator validator,
    IPathPolicy pathPolicy,
    IClock clock,
    IOperationJournal journal,
    IPlanRepository planRepository)
{
    // Not named in the FileAttributes enum, but set by Windows on cloud placeholders.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    public async Task<ExecutionResult> ExecuteAsync(
        IRootTrust trust,
        OrganizationPlan plan,
        Approval approval,
        IReadOnlyDictionary<Guid, ExpectedFile> expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(expected);
        var root = trust.Root;
        var started = clock.UtcNow;
        var results = new List<OperationExecutionResult>();
        var selected = plan.Operations.Where(operation => approval.SelectedOperationIds.Contains(operation.Id)).ToArray();

        var refusal = ValidateEnvelope(root, plan, approval, selected);
        if (refusal is null)
        {
            try
            {
                await trust.VerifyAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                refusal = exception.Message;
            }
        }

        if (refusal is not null)
        {
            results.AddRange(selected.Select(operation => Failed(operation.Id, refusal)));
            return new ExecutionResult(Guid.NewGuid(), plan.Id, results, started, clock.UtcNow);
        }

        await trust.PersistRootAsync(cancellationToken).ConfigureAwait(false);
        await planRepository.SaveAsync(plan, cancellationToken).ConfigureAwait(false);
        var transactionId = Guid.NewGuid();
        var intents = selected
            .Select((operation, index) => CaptureIntent(root, index, operation, expected))
            .ToArray();
        await journal.CreateAsync(new ExecutionJournalEntry(
            transactionId,
            plan.Id,
            plan.Revision,
            approval.Id,
            ExecutionTransactionKind.Execute,
            null,
            ExecutionTransactionState.Prepared,
            started,
            null,
            intents), cancellationToken).ConfigureAwait(false);
        await journal.UpdateTransactionAsync(
            transactionId, ExecutionTransactionState.Executing, null, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < selected.Length; index++)
        {
            var operation = selected[index];
            if (cancellationToken.IsCancellationRequested)
            {
                const string stopped = "Stopped before this file.";
                results.Add(new OperationExecutionResult(operation.Id, ExecutionOutcome.Cancelled, stopped));
                await journal.UpdateOperationAsync(
                    transactionId, operation.Id, JournalOperationState.Cancelled, stopped, CancellationToken.None)
                    .ConfigureAwait(false);
                break;
            }

            try
            {
                await journal.UpdateOperationAsync(
                    transactionId, operation.Id, JournalOperationState.InProgress, null, cancellationToken).ConfigureAwait(false);
                await trust.VerifyAsync(cancellationToken).ConfigureAwait(false);
                var finishedState = ExecuteOperation(root, operation, intents[index]);
                results.Add(new OperationExecutionResult(operation.Id, ExecutionOutcome.Completed, null));
                await journal.UpdateOperationAsync(
                    transactionId, operation.Id, finishedState, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                var reason = Plain(exception);
                results.Add(Failed(operation.Id, reason));
                await journal.UpdateOperationAsync(
                    transactionId, operation.Id, JournalOperationState.Failed, reason, CancellationToken.None).ConfigureAwait(false);
            }

            await Task.Yield();
        }

        var finished = clock.UtcNow;
        await journal.UpdateTransactionAsync(
            transactionId, DetermineState(results, selected.Length), finished, CancellationToken.None).ConfigureAwait(false);
        return new ExecutionResult(transactionId, plan.Id, results, started, finished);
    }

    public async Task<UndoResult> UndoAsync(IRootTrust trust, Guid transactionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trust);
        var original = await journal.FindAsync(transactionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("DeskAI could not find that tidy in its history.");
        if (original.Kind != ExecutionTransactionKind.Execute ||
            original.State is not (ExecutionTransactionState.Completed or ExecutionTransactionState.PartiallyCompleted))
        {
            throw new InvalidOperationException("This cannot be undone.");
        }

        // A record is undone only by the executor that trusts the folder it was made in, so
        // neither executor can be pointed at the other's files.
        var plan = await planRepository.FindAsync(original.PlanId, original.PlanRevision, cancellationToken).ConfigureAwait(false);
        if (plan is null || plan.RootId != trust.Root.Id)
        {
            throw new InvalidOperationException("That record belongs to a different folder, so DeskAI will not undo it here.");
        }

        var recent = await journal.ListRecentAsync(100, cancellationToken).ConfigureAwait(false);
        if (recent.Any(item => item.Kind == ExecutionTransactionKind.Undo &&
                               item.OriginalTransactionId == transactionId &&
                               item.State is ExecutionTransactionState.Completed or ExecutionTransactionState.PartiallyCompleted))
        {
            throw new InvalidOperationException("This has already been undone.");
        }

        await trust.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var root = trust.Root;
        var started = clock.UtcNow;
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
        await journal.CreateAsync(new ExecutionJournalEntry(
            undoId, original.PlanId, original.PlanRevision, Guid.Empty,
            ExecutionTransactionKind.Undo, transactionId,
            ExecutionTransactionState.Prepared, started, null, reversible), cancellationToken).ConfigureAwait(false);
        await journal.UpdateTransactionAsync(
            undoId, ExecutionTransactionState.Executing, null, cancellationToken).ConfigureAwait(false);

        var results = new List<OperationExecutionResult>();
        foreach (var operation in reversible)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                const string stopped = "Undo stopped before this file.";
                results.Add(new OperationExecutionResult(operation.OperationId, ExecutionOutcome.Cancelled, stopped));
                await journal.UpdateOperationAsync(
                    undoId, operation.OperationId, JournalOperationState.Cancelled, stopped, CancellationToken.None)
                    .ConfigureAwait(false);
                break;
            }

            try
            {
                await journal.UpdateOperationAsync(
                    undoId, operation.OperationId, JournalOperationState.InProgress, null, cancellationToken).ConfigureAwait(false);
                await trust.VerifyAsync(cancellationToken).ConfigureAwait(false);
                UndoOperation(root, operation);
                results.Add(new OperationExecutionResult(operation.OperationId, ExecutionOutcome.Completed, null));
                await journal.UpdateOperationAsync(
                    undoId, operation.OperationId, JournalOperationState.Completed, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                var reason = Plain(exception);
                results.Add(Failed(operation.OperationId, reason));
                await journal.UpdateOperationAsync(
                    undoId, operation.OperationId, JournalOperationState.Failed, reason, CancellationToken.None).ConfigureAwait(false);
            }
        }

        var finished = clock.UtcNow;
        var state = DetermineState(results, reversible.Length);
        await journal.UpdateTransactionAsync(undoId, state, finished, CancellationToken.None).ConfigureAwait(false);
        if (state == ExecutionTransactionState.Completed)
        {
            await journal.UpdateTransactionAsync(
                original.Id, ExecutionTransactionState.Undone, original.FinishedAtUtc, CancellationToken.None).ConfigureAwait(false);
        }

        return new UndoResult(undoId, transactionId, results, started, finished);
    }

    /// <summary>Whether an interrupted operation can be proved to have finished, from the disk.</summary>
    public bool DidOperationFinish(AuthorizedRoot root, OperationJournalEntry operation)
    {
        try
        {
            if (operation.Kind == PlanOperationKind.CreateDirectory)
            {
                return Directory.Exists(Resolve(root, operation.DestinationRelativePath));
            }

            return operation.SourceRelativePath is not null &&
                   !File.Exists(Resolve(root, operation.SourceRelativePath)) &&
                   MatchesRecordedFile(Resolve(root, operation.DestinationRelativePath), operation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// What an operation under way when DeskAI stopped actually did, read from the disk.
    /// </summary>
    /// <remarks>
    /// Only names, sizes, and dates are read, and nothing is changed. A move counts as done only
    /// when the file is gone from where it started and the other end holds a file with the
    /// recorded size and last-changed time; as not done only when it is still where it started
    /// with those facts. Anything else — changed since, missing from both ends, a link on the
    /// way — is <see cref="JournalOperationState.NeedsReview"/>, never a guess. A folder being
    /// made that exists counts as already there, because nothing proves DeskAI made it, so undo
    /// will never remove it.
    /// </remarks>
    /// <param name="kind">A tidy moves a file to its destination; an undo moves it back.</param>
    public (JournalOperationState State, string Note) CheckInterrupted(
        AuthorizedRoot root,
        ExecutionTransactionKind kind,
        OperationJournalEntry operation)
    {
        const string couldNotTell = "DeskAI could not tell whether this file moved, so it left it alone.";
        try
        {
            if (operation.Kind == PlanOperationKind.CreateDirectory)
            {
                var directory = Resolve(root, operation.DestinationRelativePath);
                RejectLinks(root, directory);
                var exists = Directory.Exists(directory);
                return kind == ExecutionTransactionKind.Execute
                    ? exists
                        ? (JournalOperationState.AlreadyPresent, "The folder is there, so undo will leave it in place.")
                        : (JournalOperationState.Failed, "The folder had not been made.")
                    : exists
                        ? (JournalOperationState.Failed, "The folder was left in place.")
                        : (JournalOperationState.Completed, "The folder had been removed.");
            }

            if (operation.SourceRelativePath is null)
            {
                return (JournalOperationState.NeedsReview, couldNotTell);
            }

            var source = Resolve(root, operation.SourceRelativePath);
            var destination = Resolve(root, operation.DestinationRelativePath);
            RejectLinks(root, source);
            RejectLinks(root, destination);
            var (from, to) = kind == ExecutionTransactionKind.Execute ? (source, destination) : (destination, source);
            var goneFromStart = !File.Exists(from) && !Directory.Exists(from);
            if (goneFromStart && MatchesRecordedFile(to, operation))
            {
                return (JournalOperationState.Completed, kind == ExecutionTransactionKind.Execute
                    ? "Checked after DeskAI stopped: it had moved."
                    : "Checked after DeskAI stopped: it had gone back.");
            }

            // A move renames, so a file still where it started never left, whatever is at the
            // other end.
            if (MatchesRecordedFile(from, operation))
            {
                return (JournalOperationState.Failed, kind == ExecutionTransactionKind.Execute
                    ? "It had not moved yet, so it is where it was."
                    : "It had not gone back yet, so it is where the tidy put it.");
            }

            return (JournalOperationState.NeedsReview, couldNotTell);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (JournalOperationState.NeedsReview, couldNotTell);
        }
    }

    public static ExecutionTransactionState DetermineState(
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

    /// <summary>Everything that must hold before a single intent is written.</summary>
    private string? ValidateEnvelope(AuthorizedRoot root, OrganizationPlan plan, Approval approval, PlanOperation[] selected)
    {
        if (plan.RootId != root.Id || approval.PlanId != plan.Id || approval.PlanRevision != plan.Revision ||
            !string.Equals(approval.PolicyVersion, plan.PolicyVersion, StringComparison.Ordinal))
        {
            return "What was approved is not this exact list, so nothing was moved. Look again and try once more.";
        }

        var knownIds = plan.Operations.Select(operation => operation.Id).ToHashSet();
        if (approval.Id == Guid.Empty || !approval.SelectedOperationIds.IsSubsetOf(knownIds))
        {
            return "What was approved includes something that is not in this list, so nothing was moved.";
        }

        if (selected.Length == 0)
        {
            return "Nothing was ticked.";
        }

        var report = validator.Validate(plan, root);
        return selected.Any(operation => report.Operations.Any(item =>
                item.OperationId == operation.Id && item.Result.Status == ValidationStatus.Blocked))
            ? "DeskAI's safety rules refuse something in this list, so nothing was moved."
            : null;
    }

    /// <summary>
    /// Records what is about to happen. For a move, the facts recorded are the ones the person
    /// saw when a list supplies them, so a file changed since fails its own check and the
    /// journal never records a move of a file nobody reviewed.
    /// </summary>
    private OperationJournalEntry CaptureIntent(
        AuthorizedRoot root,
        int sequence,
        PlanOperation operation,
        IReadOnlyDictionary<Guid, ExpectedFile> expected)
    {
        var (source, destination) = operation switch
        {
            CreateDirectoryOperation create => ((string?)null, create.DestinationRelativePath),
            MoveFileOperation move => (move.SourceRelativePath, move.DestinationRelativePath),
            RenameFileOperation rename => (rename.SourceRelativePath, rename.DestinationRelativePath),
            _ => throw new InvalidOperationException("DeskAI does not know how to do that."),
        };

        long? size = null;
        DateTimeOffset? modified = null;
        if (expected.TryGetValue(operation.Id, out var seen))
        {
            size = seen.SizeBytes;
            modified = seen.ModifiedAtUtc;
        }
        else if (source is not null)
        {
            var sourcePath = Resolve(root, source);
            RejectLinks(root, sourcePath);
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

    private JournalOperationState ExecuteOperation(AuthorizedRoot root, PlanOperation operation, OperationJournalEntry intent)
    {
        switch (operation)
        {
            case CreateDirectoryOperation create:
                // A folder that was already there is recorded as such, never as created.
                // Recording it as created once let undo delete an empty folder the person
                // had before the run.
                return CreateDirectory(root, create.DestinationRelativePath)
                    ? JournalOperationState.Completed
                    : JournalOperationState.AlreadyPresent;
            case MoveFileOperation move:
                MoveFile(root, move.SourceRelativePath, move.DestinationRelativePath, intent);
                return JournalOperationState.Completed;
            case RenameFileOperation rename:
                MoveFile(root, rename.SourceRelativePath, rename.DestinationRelativePath, intent);
                return JournalOperationState.Completed;
            default:
                throw new InvalidOperationException("DeskAI does not know how to do that.");
        }
    }

    private void UndoOperation(AuthorizedRoot root, OperationJournalEntry operation)
    {
        if (operation.Kind == PlanOperationKind.CreateDirectory)
        {
            var directory = Resolve(root, operation.DestinationRelativePath);
            RejectLinks(root, directory);
            if (!Directory.Exists(directory))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                throw new FileOperationRefusal("The folder is no longer empty, so DeskAI left it in place.");
            }

            Directory.Delete(directory, recursive: false);
            return;
        }

        if (operation.SourceRelativePath is null)
        {
            throw new InvalidOperationException("The history is missing where this file came from.");
        }

        var originalSource = Resolve(root, operation.SourceRelativePath);
        var currentDestination = Resolve(root, operation.DestinationRelativePath);
        RejectLinks(root, currentDestination);
        var sourceParent = Path.GetDirectoryName(originalSource)
            ?? throw new InvalidOperationException("The history is missing where this file came from.");
        RejectLinks(root, sourceParent);
        if (File.Exists(originalSource) || Directory.Exists(originalSource))
        {
            throw new FileOperationRefusal("Something else is now where this file was, so it was not moved back.");
        }

        if (!MatchesRecordedFile(currentDestination, operation))
        {
            throw new FileOperationRefusal("The file changed after it was tidied, so it was not moved back.");
        }

        MoveWithoutOverwrite(currentDestination, originalSource);
    }

    /// <returns>True if this call created the folder; false if it already existed.</returns>
    private bool CreateDirectory(AuthorizedRoot root, string relativePath)
    {
        var destination = Resolve(root, relativePath);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("That location is outside the folder.");
        RejectLinks(root, parent);
        if (!Directory.Exists(parent))
        {
            throw new InvalidOperationException("The folder above this one was not made first.");
        }

        if (File.Exists(destination))
        {
            throw new FileOperationRefusal("A file is where this folder would go.");
        }

        if (Directory.Exists(destination))
        {
            RejectLinks(root, destination);
            return false;
        }

        Directory.CreateDirectory(destination);
        return true;
    }

    private void MoveFile(AuthorizedRoot root, string sourceRelativePath, string destinationRelativePath, OperationJournalEntry intent)
    {
        var source = Resolve(root, sourceRelativePath);
        var destination = Resolve(root, destinationRelativePath);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("That location is outside the folder.");
        RejectLinks(root, source);
        RejectLinks(root, parent);

        if (!File.Exists(source) || Directory.Exists(source))
        {
            throw new FileOperationRefusal("It is no longer there.");
        }

        var attributes = File.GetAttributes(source);
        if ((attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0)
        {
            throw new FileOperationRefusal("It is stored online only now. Moving it would download it first.");
        }

        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            throw new FileOperationRefusal("It is now a hidden or system file.");
        }

        if (!MatchesRecordedFile(source, intent))
        {
            throw new FileOperationRefusal("It changed after the list was made, so it was left where it is.");
        }

        if (!Directory.Exists(parent))
        {
            throw new FileOperationRefusal("The folder it was going into is missing.");
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new FileOperationRefusal("A file with that name is already there, so nothing was replaced.");
        }

        MoveWithoutOverwrite(source, destination);
    }

    /// <summary>
    /// Moves without ever replacing: if the destination appeared a moment ago, Windows refuses.
    /// A file held open by another program is reported as such instead of as an error code.
    /// </summary>
    private static void MoveWithoutOverwrite(string source, string destination)
    {
        try
        {
            File.Move(source, destination, overwrite: false);
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) is SharingViolation or LockViolation)
        {
            throw new FileOperationRefusal("It's open in another program, so it was left where it is.", exception);
        }
        catch (IOException exception) when (File.Exists(destination))
        {
            throw new FileOperationRefusal("A file with that name is already there, so nothing was replaced.", exception);
        }
    }

    private string Resolve(AuthorizedRoot root, string relativePath)
    {
        if (pathPolicy.ValidateRelativePath(root, relativePath).Status == ValidationStatus.Blocked)
        {
            throw new InvalidOperationException("DeskAI's safety rules do not allow this location.");
        }

        var resolved = Path.GetFullPath(relativePath, root.CanonicalPath);
        EnsureContained(root.CanonicalPath, resolved);
        return resolved;
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

    /// <summary>Refuses if any existing part of the path below the folder is a link or junction.</summary>
    private static void RejectLinks(AuthorizedRoot root, string candidate)
    {
        EnsureContained(root.CanonicalPath, candidate);
        var current = root.CanonicalPath;
        foreach (var segment in Path.GetRelativePath(root.CanonicalPath, candidate)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current)) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException("Part of the way there is a link or shortcut, so DeskAI stopped.");
            }
        }
    }

    internal static void EnsureContained(string ancestor, string candidate)
    {
        var relative = Path.GetRelativePath(Normalize(ancestor), Normalize(candidate));
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("That location is outside the folder.");
        }
    }

    internal static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    /// DeskAI's own refusals are already written for a person. Anything Windows raised on its
    /// own is described in plain words instead of passing its technical message through.
    /// </summary>
    private static string Plain(Exception exception) => exception switch
    {
        FileOperationRefusal or InvalidOperationException => exception.Message,
        UnauthorizedAccessException => "Windows did not let DeskAI change it.",
        _ => "Windows could not do that, so it was left where it is.",
    };

    private static OperationExecutionResult Failed(Guid operationId, string error) =>
        new(operationId, ExecutionOutcome.Failed, error);
}

/// <summary>A refusal DeskAI made on purpose, with a message written for the person.</summary>
internal sealed class FileOperationRefusal : IOException
{
    public FileOperationRefusal(string message)
        : base(message)
    {
    }

    public FileOperationRefusal(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}