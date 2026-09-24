using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Tidy;

namespace DeskAI.Core.Studio;

/// <summary>A card's preview, or the plain reason there is none.</summary>
public sealed record DesktopMovePreviewResult(DesktopMovePreview? Preview, string Message);

/// <summary>What pressing Move or Put back did.</summary>
/// <param name="NeedsPermission">Nothing was tried because moving things on the Desktop is not allowed; the page asks, then tries again.</param>
public sealed record DesktopMoveResult(
    bool NeedsPermission, int Moved, int Tried, IReadOnlyList<DesktopLeftAlone> NotMoved, string Summary);

/// <summary>The latest change a card made on the Desktop, which its Put back can undo.</summary>
/// <param name="Moved">Each moved thing's original place by operation ID, so Put back can name it.</param>
public sealed record DesktopLastChange(
    DesktopMoveCard Card, Guid TransactionId, DateTimeOffset FinishedAtUtc, IReadOnlyDictionary<Guid, string> Moved);

/// <summary>
/// Clear old stuff and Folder by group (ADR 0044): the preview, Move, Put back, the separate yes,
/// and a change that stopped part-way.
/// </summary>
/// <remarks>
/// <para>
/// Only the connected Desktop is served; any other folder is refused. The approval covers exactly
/// the ticked rows plus the folders they go into, and each moved thing carries how it looked in
/// the preview, which the executor checks again right before it moves.
/// </para>
/// <para>
/// No AI is involved: Folder by group reads the board the person already saw and could change.
/// Put back is offered only for the latest change on the Desktop, and only by the card that made
/// it; undoing out of order is not what Put back means.
/// </para>
/// </remarks>
public sealed class DesktopMoveService(
    DesktopGroupingService grouping,
    IReadOnlyFolderService folders,
    IFolderMovePermissions permissions,
    DesktopInventoryService inventory,
    IDesktopGroupRepository boards,
    IFolderTidyExecutor executor,
    IOperationJournal journal,
    TidyRunService runs,
    IPlanSafetyCheck safety,
    IClock clock)
{
    public const string FindGroupsFirst = "Find groups first, then DeskAI can put each group into its own folder.";
    public const string PermissionNeeded = "DeskAI needs your permission before it moves anything on your Desktop.";
    public const string PutBackPermissionNeeded = "Putting things back moves them too, so DeskAI needs your permission to move things on your Desktop again.";

    public async Task<bool> CanMoveAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is { } root && RootCapabilities.CanMoveFolders(root);

    /// <summary>Records the yes. Called only after the page's dialog was accepted.</summary>
    public async Task<string> AllowAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return DesktopGroupingService.NotConnected;
        }

        if (!RootCapabilities.CanReadMetadata(root))
        {
            return "Your Desktop was not connected in a way that lets DeskAI move things.";
        }

        // Checked now, not trusted from when it was connected: it may have become a link since.
        if (await folders.CheckStillSafeAsync(root, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        await permissions.AllowAsync(rootId, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return "DeskAI may now move the things you tick on your Desktop. It never deletes anything.";
    }

    /// <summary>Taking the yes back needs no confirmation; that is never the dangerous direction.</summary>
    public async Task<string> StopAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await permissions.StopAsync(rootId, cancellationToken).ConfigureAwait(false);
        return "DeskAI can no longer move things on your Desktop. Nothing was moved.";
    }

    public async Task<DesktopMovePreviewResult> PreviewAsync(Guid rootId, DesktopMoveCard card, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(null, DesktopGroupingService.NotConnected);
        }

        DesktopGroupBoard? board = null;
        if (card == DesktopMoveCard.FolderByGroup)
        {
            board = await boards.LoadAsync(rootId, cancellationToken).ConfigureAwait(false);
            if (board is null || board.Groups.All(group => group.Items.Count == 0))
            {
                return new(null, FindGroupsFirst);
            }
        }

        var seen = await inventory.LookAsync(root, cancellationToken).ConfigureAwait(false);
        if (seen.Problem is not null)
        {
            return new(null, seen.Problem);
        }

        bool IsProtected(string path) => safety.IsProtected(root, path);
        var preview = card == DesktopMoveCard.ClearOldStuff
            ? DesktopMovePlanner.ClearOldStuff(root, seen, clock.UtcNow, safety.PolicyVersion, IsProtected)
            : DesktopMovePlanner.FolderByGroup(root, seen, board!, clock.UtcNow, safety.PolicyVersion, IsProtected);
        var message = preview.Items.Count > 0
            ? string.Empty
            : card == DesktopMoveCard.ClearOldStuff
                ? "Nothing on your Desktop has been left unchanged for 6 months."
                : "Nothing in your groups can go into folders right now.";
        return new(preview, message);
    }

    public async Task<DesktopMoveResult> ApplyAsync(
        DesktopMovePreview preview, IReadOnlyCollection<Guid> ticked, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(ticked);
        if (await DesktopAsync(preview.Root.Id, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(false, 0, 0, [], DesktopGroupingService.NotConnected);
        }

        if (!RootCapabilities.CanMoveFolders(root))
        {
            return new(true, 0, 0, [], PermissionNeeded);
        }

        var items = preview.Items.Where(item => ticked.Contains(item.OperationId)).ToList();
        if (items.Count == 0)
        {
            return new(false, 0, 0, [], "Nothing was ticked, so nothing moved.");
        }

        var destinations = items.Select(item => item.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var creates = preview.Plan.Operations
            .OfType<CreateDirectoryOperation>()
            .Where(create => destinations.Contains(create.DestinationRelativePath))
            .Select(create => create.Id);
        var approval = Approval.Create(Guid.NewGuid(), preview.Plan, creates.Concat(items.Select(item => item.OperationId)), clock.UtcNow);
        var expected = items.ToDictionary(item => item.OperationId, item => preview.Facts[item.OperationId]);

        var result = await executor.ExecuteAsync(preview.Plan, approval, expected, cancellationToken).ConfigureAwait(false);

        var outcomes = result.Operations.ToDictionary(outcome => outcome.OperationId);
        var notMoved = items
            .Where(item => !(outcomes.TryGetValue(item.OperationId, out var outcome) && outcome.Outcome == ExecutionOutcome.Completed))
            .Select(item => new DesktopLeftAlone(item.Name, outcomes.GetValueOrDefault(item.OperationId)?.Error ?? "It was not moved."))
            .ToList();
        var moved = items.Count - notMoved.Count;
        var into = preview.Card == DesktopMoveCard.ClearOldStuff
            ? DesktopMovePlanner.OldStuffFolder
            : destinations.Count == 1 ? $"the {destinations.Single()} folder" : $"{destinations.Count} folders";
        var summary = notMoved.Count == 0
            ? $"Done. {DesktopMoveText.Things(moved)} moved into {into}."
            : moved == 0
                ? "Nothing was moved."
                : $"{moved} of {items.Count} things moved. The rest stayed where they were.";
        return new(false, moved, items.Count, notMoved, summary);
    }

    /// <summary>The card's own latest change, only if it is the latest change on the Desktop and not undone.</summary>
    public async Task<DesktopLastChange?> FindLastAsync(Guid rootId, DesktopMoveCard card, CancellationToken cancellationToken = default)
    {
        var records = await journal.ListForRootAsync(rootId, TidyRunService.LastTidyLookBack, cancellationToken).ConfigureAwait(false);
        var undone = records
            .Where(record => record.Kind == ExecutionTransactionKind.Undo &&
                             record.OriginalTransactionId is not null &&
                             record.State is not (ExecutionTransactionState.Failed or ExecutionTransactionState.Cancelled))
            .Select(record => record.OriginalTransactionId!.Value)
            .ToHashSet();
        foreach (var record in records.Where(record => record.Kind == ExecutionTransactionKind.Execute))
        {
            if (record.State is ExecutionTransactionState.Prepared or ExecutionTransactionState.Executing
                or ExecutionTransactionState.RecoveryRequired)
            {
                return null;
            }

            var moved = record.Operations
                .Where(operation => operation.Kind is PlanOperationKind.MoveFile or PlanOperationKind.MoveFolder &&
                                    operation.State == JournalOperationState.Completed &&
                                    operation.SourceRelativePath is not null)
                .ToDictionary(operation => operation.OperationId, operation => operation.SourceRelativePath!);
            if (moved.Count == 0)
            {
                continue;
            }

            return record.Purpose != PurposeOf(card) || record.State == ExecutionTransactionState.Undone || undone.Contains(record.Id)
                ? null
                : new DesktopLastChange(card, record.Id, record.FinishedAtUtc ?? record.StartedAtUtc, moved);
        }

        return null;
    }

    public async Task<DesktopMoveResult> PutBackAsync(Guid rootId, DesktopMoveCard card, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(false, 0, 0, [], DesktopGroupingService.NotConnected);
        }

        if (!RootCapabilities.CanMoveFolders(root))
        {
            return new(true, 0, 0, [], PutBackPermissionNeeded);
        }

        return await FindLastAsync(rootId, card, cancellationToken).ConfigureAwait(false) is { } last
            ? await UndoAsync(last.TransactionId, last.Moved, cancellationToken).ConfigureAwait(false)
            : new(false, 0, 0, [], "There is nothing to put back.");
    }

    /// <summary>A change on the Desktop that stopped part-way, checked against the disk; asked about first.</summary>
    public Task<InterruptedTidy?> FindInterruptedAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        runs.FindInterruptedAsync(rootId, cancellationToken);

    /// <summary>"Keep them where they are": closes the record as a change of what moved. Moves nothing.</summary>
    public Task<string?> KeepInterruptedAsync(Guid rootId, InterruptedTidy interrupted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interrupted);
        return runs.KeepInterruptedAsync(rootId, interrupted.TransactionId, cancellationToken);
    }

    /// <summary>"Put them back": closes the record, then puts back what it proved had moved, with Put back's own checks.</summary>
    public async Task<DesktopMoveResult> PutBackInterruptedAsync(
        Guid rootId, InterruptedTidy interrupted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interrupted);
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(false, 0, 0, [], DesktopGroupingService.NotConnected);
        }

        // Asked before closing, so refusing leaves the question open exactly as it was.
        if (!RootCapabilities.CanMoveFolders(root))
        {
            return new(true, 0, 0, [], PutBackPermissionNeeded);
        }

        if (!interrupted.CanUndo)
        {
            return new(false, 0, 0, [], "Nothing had moved, so there is nothing to put back.");
        }

        if (await runs.KeepInterruptedAsync(rootId, interrupted.TransactionId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return new(false, 0, 0, [], problem);
        }

        return await UndoAsync(interrupted.TransactionId, interrupted.MovedFiles, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DesktopMoveResult> UndoAsync(Guid transactionId, IReadOnlyDictionary<Guid, string> moved, CancellationToken cancellationToken)
    {
        UndoResult result;
        try
        {
            result = await executor.UndoAsync(transactionId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return new(false, 0, 0, [], exception.Message);
        }

        var named = result.Operations.Where(outcome => moved.ContainsKey(outcome.OperationId)).ToList();
        var back = named.Count(outcome => outcome.Outcome == ExecutionOutcome.Completed);
        var notBack = named
            .Where(outcome => outcome.Outcome != ExecutionOutcome.Completed)
            .Select(outcome => new DesktopLeftAlone(Path.GetFileName(moved[outcome.OperationId]), outcome.Error ?? "It was not moved back."))
            .ToList();
        var summary = notBack.Count == 0
            ? $"Put back. {DesktopMoveText.Things(back)} {(back == 1 ? "is where it was" : "are where they were")}."
            : $"{back} of {named.Count} things went back. The rest stayed where they are now.";
        return new(false, back, named.Count, notBack, summary);
    }

    private static PlanPurpose PurposeOf(DesktopMoveCard card) => card switch
    {
        DesktopMoveCard.ClearOldStuff => PlanPurpose.ClearOldStuff,
        DesktopMoveCard.FolderByGroup => PlanPurpose.FolderByGroup,
        _ => throw new ArgumentOutOfRangeException(nameof(card)),
    };

    /// <summary>The root, only when it is the connected Desktop. Any other folder is refused.</summary>
    private async Task<AuthorizedRoot?> DesktopAsync(Guid rootId, CancellationToken cancellationToken) =>
        await grouping.FindDesktopAsync(cancellationToken).ConfigureAwait(false) is { } desktop && desktop.Id == rootId ? desktop : null;
}
