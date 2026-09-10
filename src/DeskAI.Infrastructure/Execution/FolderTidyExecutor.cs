using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Execution;

/// <summary>
/// Moves files in a folder the person connected and allowed DeskAI to tidy.
/// </summary>
/// <remarks>
/// <para>
/// The rules for moving are <see cref="FileOperationRunner"/>'s, shared with the practice
/// workspace. What is particular here is trust: before the run and again before every file,
/// the folder must still be connected, still allowed to be tidied, at the same place it was
/// when the run started, and still pass the same checks as when tidying was allowed — present,
/// not a network or whole-drive location, no link in its path, not protected. If any of that
/// stops being true part-way, the remaining files are refused.
/// </para>
/// <para>
/// One run at a time, tidy or undo, so two runs can never race over the same files.
/// </para>
/// </remarks>
public sealed class FolderTidyExecutor : IFolderTidyExecutor, IDisposable
{
    private readonly IAuthorizedRootRepository _roots;
    private readonly IReadOnlyFolderService _folders;
    private readonly IOperationJournal _journal;
    private readonly IPlanRepository _plans;
    private readonly IClock _clock;
    private readonly FileOperationRunner _runner;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public FolderTidyExecutor(
        IAuthorizedRootRepository roots,
        IReadOnlyFolderService folders,
        PlanValidator validator,
        IPathPolicy pathPolicy,
        IClock clock,
        IOperationJournal journal,
        IPlanRepository plans)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _runner = new FileOperationRunner(validator, pathPolicy, clock, journal, plans);
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
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await _roots.FindAsync(plan.RootId, cancellationToken).ConfigureAwait(false);
            if (root is null || !RootCapabilities.CanTidy(root))
            {
                var now = _clock.UtcNow;
                var reason = root is null
                    ? "That folder is no longer connected, so nothing was moved."
                    : "DeskAI may not tidy this folder, so nothing was moved.";
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

            return await _runner.ExecuteAsync(new FolderTrust(root, _roots, _folders), plan, approval, expected, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public async Task<UndoResult> UndoAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var original = await _journal.FindAsync(transactionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("DeskAI could not find that tidy in its history.");
            var plan = await _plans.FindAsync(original.PlanId, original.PlanRevision, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("DeskAI could not find what that tidy did.");
            var root = await _roots.FindAsync(plan.RootId, cancellationToken).ConfigureAwait(false);

            // Undo moves files too, so it needs the same permission as tidying (spec §1). The
            // practice workspace never holds it, so a practice record cannot be undone here.
            if (root is null || !RootCapabilities.CanTidy(root))
            {
                throw new InvalidOperationException(root is null
                    ? "That folder is no longer connected, so nothing was moved back."
                    : "Undo moves files too, so DeskAI needs your permission to tidy this folder again.");
            }

            return await _runner.UndoAsync(new FolderTrust(root, _roots, _folders), transactionId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public void Dispose() => _oneAtATime.Dispose();

    /// <summary>A connected folder is trusted only while its tidy permission and path still hold.</summary>
    private sealed class FolderTrust(
        AuthorizedRoot root,
        IAuthorizedRootRepository roots,
        IReadOnlyFolderService folders) : IRootTrust
    {
        public AuthorizedRoot Root => root;

        public async Task VerifyAsync(CancellationToken cancellationToken)
        {
            var current = await roots.FindAsync(root.Id, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                throw new InvalidOperationException("That folder is no longer connected, so DeskAI stopped.");
            }

            if (!RootCapabilities.CanTidy(current))
            {
                throw new InvalidOperationException("DeskAI may no longer tidy this folder, so it stopped.");
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
