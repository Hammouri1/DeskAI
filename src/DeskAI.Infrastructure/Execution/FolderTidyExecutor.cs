using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Safety;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Execution;

/// <summary>
/// Moves files in a folder the person connected and allowed DeskAI to tidy.
/// </summary>
/// <remarks>
/// <para>
/// The rules for moving are <see cref="FileOperationRunner"/>'s. What is particular here is
/// trust: before the run and again before every file,
/// the folder must still be connected, still allowed to be tidied, at the same place it was
/// when the run started, and still pass the same checks as when tidying was allowed — present,
/// not a network or whole-drive location, no link in its path, not protected. If any of that
/// stops being true part-way, the remaining files are refused.
/// </para>
/// <para>
/// One run at a time, tidy, undo, or check, across every DeskAI window: a lock inside the
/// process and a lock file beside the database (<see cref="RunLockFile"/>). While both are held,
/// an unfinished record for a folder belongs to a run that is no longer running, which is what
/// makes checking it against the disk safe.
/// </para>
/// <para>
/// A folder with an unfinished record accepts no new tidy or undo until the person has
/// answered about it, so there is never more than one question open per folder.
/// </para>
/// </remarks>
public sealed class FolderTidyExecutor : IFolderTidyExecutor, IDisposable
{
    /// <summary>How long a check done when a page opens waits for another window.</summary>
    private static readonly TimeSpan CheckWait = TimeSpan.FromSeconds(2);

    private const string BusyMessage = "DeskAI is busy tidying in another window. Try again in a moment.";
    private const string OpenQuestionMessage =
        "Your last tidy of this folder was interrupted. Answer the question about it first.";

    private readonly IAuthorizedRootRepository _roots;
    private readonly IReadOnlyFolderService _folders;
    private readonly IOperationJournal _journal;
    private readonly IPlanRepository _plans;
    private readonly IClock _clock;
    private readonly FileOperationRunner _runner;
    private readonly string _lockPath;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public FolderTidyExecutor(
        IAuthorizedRootRepository roots,
        IReadOnlyFolderService folders,
        PlanValidator validator,
        IPathPolicy pathPolicy,
        IClock clock,
        IOperationJournal journal,
        IPlanRepository plans,
        IOptions<DatabaseOptions> database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _runner = new FileOperationRunner(validator, pathPolicy, clock, journal, plans);
        _lockPath = LockPathFor(database.Value.DatabasePath);
    }

    /// <summary>
    /// A tidy needs the tidy yes; a Desktop Studio run needs its own yes (ADR 0044). Neither one
    /// stands in for the other.
    /// </summary>
    internal static bool MayRun(AuthorizedRoot root, PlanPurpose purpose) =>
        purpose == PlanPurpose.Tidy ? RootCapabilities.CanTidy(root) : RootCapabilities.CanMoveFolders(root);

    /// <summary>How long a tidy or undo waits for another DeskAI window before refusing.</summary>
    public TimeSpan BusyWait { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The lock file for the DeskAI that keeps its journal in <paramref name="databasePath"/>.</summary>
    public static string LockPathFor(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return databasePath + ".tidy-lock";
    }

    public async Task<ExecutionResult> ExecuteAsync(
        OrganizationPlan plan,
        Approval approval,
        IReadOnlyDictionary<Guid, ExpectedFile> expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(expected);
        using var held = await TryEnterAsync(BusyWait, cancellationToken).ConfigureAwait(false);
        if (held is null)
        {
            return Refuse(plan, approval, BusyMessage);
        }

        var root = await _roots.FindAsync(plan.RootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !MayRun(root, plan.Purpose))
        {
            return Refuse(plan, approval, root is null
                ? "That folder is no longer connected, so nothing was moved."
                : plan.Purpose == PlanPurpose.Tidy
                    ? "DeskAI may not tidy this folder, so nothing was moved."
                    : "DeskAI may not move things here, so nothing was moved.");
        }

        if ((await FindUnfinishedAsync(root.Id, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return Refuse(plan, approval, OpenQuestionMessage);
        }

        return await _runner.ExecuteAsync(new FolderTrust(root, _roots, _folders, plan.Purpose), plan, approval, expected, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UndoResult> UndoAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        using var held = await TryEnterAsync(BusyWait, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(BusyMessage);
        var original = await _journal.FindAsync(transactionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("DeskAI could not find that tidy in its history.");
        var plan = await _plans.FindAsync(original.PlanId, original.PlanRevision, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("DeskAI could not find what that tidy did.");
        var root = await _roots.FindAsync(plan.RootId, cancellationToken).ConfigureAwait(false);

        // Undo moves files too, so it needs the same permission the run needed (spec §1, ADR
        // 0044). The retired practice workspace never held one, so an old practice record cannot
        // be undone.
        if (root is null || !MayRun(root, plan.Purpose))
        {
            throw new InvalidOperationException(root is null
                ? "That folder is no longer connected, so nothing was moved back."
                : plan.Purpose == PlanPurpose.Tidy
                    ? "Undo moves files too, so DeskAI needs your permission to tidy this folder again."
                    : "Putting things back moves them too, so DeskAI needs your permission to move things on your Desktop again.");
        }

        if ((await FindUnfinishedAsync(root.Id, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            throw new InvalidOperationException(OpenQuestionMessage);
        }

        return await _runner.UndoAsync(new FolderTrust(root, _roots, _folders, plan.Purpose), transactionId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Checks every unfinished record for the folder against the disk, writes what each file
    /// turned out to have done, and leaves the record waiting for the person's answer.
    /// </summary>
    /// <remarks>
    /// Checking reads only names, sizes, and dates, so it needs the folder connected and passing
    /// its safety re-check, not the tidy permission. If another window holds the lock, or the
    /// folder is not safe to look at, nothing is checked and nothing is returned; the record
    /// stays exactly as it was for the next look. Checking a record twice changes nothing.
    /// </remarks>
    public async Task<IReadOnlyList<ExecutionJournalEntry>> CheckInterruptedAsync(
        Guid rootId,
        CancellationToken cancellationToken = default)
    {
        using var held = await TryEnterAsync(CheckWait < BusyWait ? CheckWait : BusyWait, cancellationToken).ConfigureAwait(false);
        if (held is null)
        {
            return [];
        }

        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadMetadata(root) ||
            await _folders.CheckStillSafeAsync(root, cancellationToken).ConfigureAwait(false) is not null)
        {
            return [];
        }

        var checkedRecords = new List<ExecutionJournalEntry>();
        foreach (var record in await FindUnfinishedAsync(rootId, cancellationToken).ConfigureAwait(false))
        {
            if (record.State != ExecutionTransactionState.RecoveryRequired)
            {
                foreach (var operation in record.Operations)
                {
                    var (state, note) = operation.State switch
                    {
                        // Written before the file is touched, so a Pending operation never started.
                        JournalOperationState.Pending => (JournalOperationState.Cancelled, "It had not started when DeskAI stopped."),
                        JournalOperationState.InProgress => _runner.CheckInterrupted(root, record.Kind, operation),
                        _ => (operation.State, operation.Error),
                    };
                    if (state != operation.State)
                    {
                        await _journal.UpdateOperationAsync(record.Id, operation.OperationId, state, note, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                await _journal.UpdateTransactionAsync(record.Id, ExecutionTransactionState.RecoveryRequired, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            checkedRecords.Add(await _journal.FindAsync(record.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("DeskAI could not find that tidy in its history."));
        }

        return checkedRecords;
    }

    /// <summary>
    /// Closes a checked record as an ordinary tidy or undo of what it did. Changes no file.
    /// </summary>
    public async Task<ExecutionTransactionState> CloseInterruptedAsync(
        Guid rootId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        using var held = await TryEnterAsync(BusyWait, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(BusyMessage);
        var record = await _journal.FindAsync(transactionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("DeskAI could not find that tidy in its history.");
        var plan = await _plans.FindAsync(record.PlanId, record.PlanRevision, cancellationToken).ConfigureAwait(false);
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (plan is null || root is null || plan.RootId != rootId || !RootCapabilities.CanReadMetadata(root))
        {
            throw new InvalidOperationException("That record belongs to a different folder, so DeskAI left it alone.");
        }

        if (record.State != ExecutionTransactionState.RecoveryRequired)
        {
            throw new InvalidOperationException("That tidy is not waiting for an answer.");
        }

        var state = Settle(record);
        await _journal.UpdateTransactionAsync(transactionId, state, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (record.Kind == ExecutionTransactionKind.Undo && state == ExecutionTransactionState.Completed &&
            record.OriginalTransactionId is { } originalId &&
            await _journal.FindAsync(originalId, cancellationToken).ConfigureAwait(false) is { } original)
        {
            await _journal.UpdateTransactionAsync(originalId, ExecutionTransactionState.Undone, original.FinishedAtUtc, cancellationToken)
                .ConfigureAwait(false);
        }

        return state;
    }

    public void Dispose() => _oneAtATime.Dispose();

    /// <summary>What a checked record did: nothing, some of it, or all of it.</summary>
    /// <remarks>
    /// A tidy is measured by the files it moved: a folder made for a file that never arrived is
    /// nothing worth undoing. A run that set out only to make folders (a folder template) has no
    /// moves to measure, so it is measured by the folders it made instead. Without that, such a
    /// record settled as Failed and the folders it had made could never be undone.
    /// </remarks>
    private static ExecutionTransactionState Settle(ExecutionJournalEntry record)
    {
        var onlyFolders = record.Operations.All(operation => operation.Kind == PlanOperationKind.CreateDirectory);
        var done = record.Operations.Count(operation =>
            (onlyFolders || operation.Kind != PlanOperationKind.CreateDirectory) &&
            operation.State == JournalOperationState.Completed);
        if (done == 0)
        {
            return ExecutionTransactionState.Failed;
        }

        return record.Operations.All(operation =>
                operation.State is JournalOperationState.Completed or JournalOperationState.AlreadyPresent)
            ? ExecutionTransactionState.Completed
            : ExecutionTransactionState.PartiallyCompleted;
    }

    /// <summary>Records for this folder that no run finished: interrupted, or waiting for an answer.</summary>
    private async Task<IReadOnlyList<ExecutionJournalEntry>> FindUnfinishedAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var unfinished = new List<ExecutionJournalEntry>();
        foreach (var record in await _journal.ListIncompleteAsync(cancellationToken).ConfigureAwait(false))
        {
            var plan = await _plans.FindAsync(record.PlanId, record.PlanRevision, cancellationToken).ConfigureAwait(false);
            if (plan?.RootId == rootId)
            {
                unfinished.Add(record);
            }
        }

        return unfinished;
    }

    /// <summary>Both locks, or null when another window kept the lock file busy.</summary>
    private async Task<IDisposable?> TryEnterAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await RunLockFile.TryEnterAsync(_lockPath, wait, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                _oneAtATime.Release();
                return null;
            }

            return new Held(file, _oneAtATime);
        }
        catch
        {
            _oneAtATime.Release();
            throw;
        }
    }

    private ExecutionResult Refuse(OrganizationPlan plan, Approval approval, string reason)
    {
        var now = _clock.UtcNow;
        return new ExecutionResult(
            Guid.NewGuid(),
            plan.Id,
            plan.Operations
                .Where(operation => approval.SelectedOperationIds.Contains(operation.Id))
                .Select(operation => new OperationExecutionResult(operation.Id, ExecutionOutcome.Failed, reason))
                .ToArray(),
            now,
            now);
    }

    private sealed class Held(IDisposable file, SemaphoreSlim oneAtATime) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            file.Dispose();
            oneAtATime.Release();
        }
    }

    /// <summary>A connected folder is trusted only while its tidy permission and path still hold.</summary>
    private sealed class FolderTrust(
        AuthorizedRoot root,
        IAuthorizedRootRepository roots,
        IReadOnlyFolderService folders,
        PlanPurpose purpose) : IRootTrust
    {
        public AuthorizedRoot Root => root;

        public async Task VerifyAsync(CancellationToken cancellationToken)
        {
            var current = await roots.FindAsync(root.Id, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                throw new InvalidOperationException("That folder is no longer connected, so DeskAI stopped.");
            }

            if (!MayRun(current, purpose))
            {
                throw new InvalidOperationException(purpose == PlanPurpose.Tidy
                    ? "DeskAI may no longer tidy this folder, so it stopped."
                    : "DeskAI may no longer move things here, so it stopped.");
            }

            if (!string.Equals(
                    FileOperationRunner.Normalize(current.CanonicalPath),
                    FileOperationRunner.Normalize(root.CanonicalPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The folder is not where it was, so DeskAI stopped.");
            }

            if (await folders.CheckStillSafeAsync(current, cancellationToken).ConfigureAwait(false) is { } problem)
            {
                throw new InvalidOperationException(problem);
            }
        }

        // A connected folder is already recorded; saving it again would rewrite its row.
        public Task PersistRootAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
