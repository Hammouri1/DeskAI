using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tidy;

/// <summary>A file tidying or undo did not move, and why, in plain words.</summary>
public sealed record TidyFileOutcome(string FileName, string Reason);

/// <summary>What pressing Tidy did.</summary>
/// <param name="TransactionId">The journal record to undo, or null when nothing moved.</param>
/// <param name="MovedFiles">Each moved file by its move operation ID, so undo can name them.</param>
public sealed record TidyRunResult(
    Guid? TransactionId,
    int Moved,
    int Ticked,
    int FoldersUsed,
    IReadOnlyList<TidyFileOutcome> Skipped,
    IReadOnlyDictionary<Guid, string> MovedFiles,
    string Summary)
{
    public bool CanUndo => TransactionId is not null && Moved > 0;
}

/// <summary>What pressing Undo did.</summary>
/// <param name="NeedsPermission">
/// True when nothing was tried because the folder may no longer be tidied; the page asks to
/// allow tidying again, because undo moves files too.
/// </param>
public sealed record TidyUndoResult(
    bool NeedsPermission,
    bool Finished,
    int Restored,
    IReadOnlyList<TidyFileOutcome> NotRestored,
    string Summary);

/// <summary>A folder's last tidy, found again from the journal after DeskAI was reopened.</summary>
/// <param name="MovedFiles">Each moved file by its move operation ID, so undo can name them.</param>
public sealed record LastTidy(
    Guid TransactionId,
    DateTimeOffset FinishedAtUtc,
    IReadOnlyDictionary<Guid, string> MovedFiles,
    int FoldersUsed)
{
    public int Moved => MovedFiles.Count;
}

/// <summary>A tidy or undo that stopped part-way, after DeskAI checked it against the disk.</summary>
/// <param name="IsUndo">An undo was interrupted, rather than a tidy.</param>
/// <param name="Moved">Files it proved had moved (for an undo, had gone back).</param>
/// <param name="Total">Files it set out to move.</param>
/// <param name="NeedsReview">Files DeskAI could not tell about, with where to look. Never moved.</param>
/// <param name="MovedFiles">The proved moves by operation ID, so undoing them can name them.</param>
/// <param name="MadeFolders">Folders the run is recorded as having made, by operation ID.</param>
/// <param name="TotalFolders">Folders it set out to make.</param>
public sealed record InterruptedTidy(
    Guid TransactionId,
    bool IsUndo,
    int Moved,
    int Total,
    IReadOnlyList<TidyFileOutcome> NeedsReview,
    IReadOnlyDictionary<Guid, string> MovedFiles,
    IReadOnlyDictionary<Guid, string> MadeFolders,
    int TotalFolders)
{
    /// <summary>
    /// The run set out only to make folders (a folder template), so it is described and undone
    /// by folders rather than files.
    /// </summary>
    public bool IsFolders => Total == 0 && TotalFolders > 0;

    /// <summary>Only a tidy that moved something, or a folder run that made something, offers undo.</summary>
    public bool CanUndo => !IsUndo && (IsFolders ? MadeFolders.Count > 0 : Moved > 0);

    /// <summary>
    /// Which feature made the change (ADR 0044). The question is answered only on that feature's
    /// page, which asks for that feature's own permission; the other page only points there.
    /// </summary>
    public PlanPurpose Purpose { get; init; }
}

/// <summary>
/// Turns what the person ticked into an exact approval, runs it, and says what happened.
/// </summary>
/// <remarks>
/// <para>
/// The approval covers exactly the ticked moves plus the folders those moves need, bound to
/// the plan, revision, and policy version on screen. Nothing unticked can move, and no folder
/// is created that no ticked file goes into. Each moved file carries how it looked when the
/// list was made, and the executor checks it again right before moving it.
/// </para>
/// <para>
/// Results never become a plain "done" when anything was skipped: the summary says how many of
/// how many, and each skipped file is listed with its reason.
/// </para>
/// <para>
/// After DeskAI is reopened, the journal is the memory: the folder's last tidy is found there
/// and can still be undone, and a tidy that stopped part-way is checked against the disk and
/// put to the person before anything else happens in that folder.
/// </para>
/// </remarks>
public sealed class TidyRunService(
    IFolderTidyExecutor executor,
    IAuthorizedRootRepository roots,
    IOperationJournal journal,
    IClock clock)
{
    /// <summary>How many of a folder's newest records are read to find its last tidy.</summary>
    public const int LastTidyLookBack = 20;

    /// <summary>
    /// The folder's latest tidy that moved at least one file, if it has not been undone.
    /// </summary>
    /// <remarks>
    /// Only the latest is offered, never an older tidy behind it: undoing one out of order is
    /// not what "undo" means to anyone. While a record of the folder is unfinished there is no
    /// last tidy — that question comes first.
    /// </remarks>
    public async Task<LastTidy?> FindLastAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var records = await journal.ListForRootAsync(rootId, LastTidyLookBack, cancellationToken).ConfigureAwait(false);
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

            var moved = CompletedMoves(record);
            if (moved.Count == 0)
            {
                continue;
            }

            // Only the latest change in the folder can be undone, and Organize undoes only its own
            // tidies; a Desktop Studio change is put back from Desktop Studio (ADR 0044).
            if (record.Purpose != PlanPurpose.Tidy)
            {
                return null;
            }

            if (record.State == ExecutionTransactionState.Undone || undone.Contains(record.Id))
            {
                return null;
            }

            var folders = record.Operations
                .Where(operation => moved.ContainsKey(operation.OperationId))
                .Select(operation => Path.GetDirectoryName(operation.DestinationRelativePath) ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return new LastTidy(record.Id, record.FinishedAtUtc ?? record.StartedAtUtc, moved, folders);
        }

        return null;
    }

    /// <summary>
    /// Checks the folder's interrupted tidy or undo against the disk, and describes it.
    /// </summary>
    /// <returns>The newest one, or null when there is none or it cannot safely be checked now.</returns>
    public async Task<InterruptedTidy?> FindInterruptedAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var records = await executor.CheckInterruptedAsync(rootId, cancellationToken).ConfigureAwait(false);
        var record = records.OrderByDescending(item => item.StartedAtUtc).FirstOrDefault();
        if (record is null)
        {
            return null;
        }

        var moves = record.Operations.Where(operation => operation.Kind != PlanOperationKind.CreateDirectory).ToArray();
        var folders = record.Operations.Where(operation => operation.Kind == PlanOperationKind.CreateDirectory).ToArray();
        var isUndo = record.Kind == ExecutionTransactionKind.Undo;
        var review = moves
            .Where(operation => operation.State == JournalOperationState.NeedsReview)
            .Select(operation => new TidyFileOutcome(
                operation.SourceRelativePath ?? operation.DestinationRelativePath,
                WhereToLook(operation)))
            .ToArray();
        var made = folders
            .Where(operation => operation.State == JournalOperationState.Completed)
            .ToDictionary(operation => operation.OperationId, operation => operation.DestinationRelativePath);
        return new InterruptedTidy(
            record.Id, isUndo, CompletedMoves(record).Count, moves.Length, review, CompletedMoves(record), made, folders.Length)
        {
            Purpose = record.Purpose,
        };
    }

    /// <summary>"Keep them": the interrupted record becomes an ordinary tidy of what moved.</summary>
    /// <returns>Null when done; otherwise why not, in plain words.</returns>
    public async Task<string?> KeepInterruptedAsync(Guid rootId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await executor.CloseInterruptedAsync(rootId, transactionId, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// "Undo those": closes the interrupted tidy, then undoes the files it proved had moved,
    /// each with the ordinary undo's own checks. Needs the tidy permission, like any undo.
    /// </summary>
    public async Task<TidyUndoResult> UndoInterruptedAsync(
        Guid rootId,
        InterruptedTidy interrupted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interrupted);
        if (interrupted.Purpose != PlanPurpose.Tidy)
        {
            return new(false, false, 0, [], "This change was made in Desktop Studio, so answer it there.");
        }

        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return new(false, false, 0, [], "That folder is no longer connected, so nothing was moved back.");
        }

        // Asked before closing, so refusing leaves the question open exactly as it was.
        if (!RootCapabilities.CanTidy(root))
        {
            return new(true, false, 0, [], interrupted.IsFolders
                ? "Undo removes the folders DeskAI made, so DeskAI needs your permission to tidy this folder again."
                : "Undo moves files too, so DeskAI needs your permission to tidy this folder again.");
        }

        if (!interrupted.CanUndo)
        {
            return new(false, false, 0, [], interrupted.IsFolders
                ? "No folder had been made, so there is nothing to remove."
                : "Nothing had moved, so there is nothing to put back.");
        }

        if (await KeepInterruptedAsync(rootId, interrupted.TransactionId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return new(false, false, 0, [], problem);
        }

        return await UndoAsync(
            rootId,
            interrupted.TransactionId,
            interrupted.MovedFiles,
            interrupted.IsFolders ? interrupted.MadeFolders : null,
            cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<Guid, string> CompletedMoves(ExecutionJournalEntry record) =>
        record.Operations
            .Where(operation => operation.Kind != PlanOperationKind.CreateDirectory &&
                                operation.State == JournalOperationState.Completed &&
                                operation.SourceRelativePath is not null)
            .ToDictionary(operation => operation.OperationId, operation => operation.SourceRelativePath!);

    private static string WhereToLook(OperationJournalEntry operation)
    {
        var folder = Path.GetDirectoryName(operation.DestinationRelativePath);
        return string.IsNullOrEmpty(folder)
            ? "DeskAI couldn't tell whether it moved, so it left it alone. Please check it."
            : $"DeskAI couldn't tell whether it moved, so it left it alone. Look for it in {folder.Replace("\\", " › ", StringComparison.Ordinal)} or where it was.";
    }

    public async Task<TidyRunResult> TidyAsync(
        TidyPreview preview,
        IReadOnlyCollection<Guid> tickedMoveIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(tickedMoveIds);
        var moves = preview.Plan.Operations
            .OfType<MoveFileOperation>()
            .Where(move => tickedMoveIds.Contains(move.Id) && preview.MoveSources.ContainsKey(move.Id))
            .ToArray();
        if (moves.Length == 0)
        {
            return new TidyRunResult(null, 0, 0, 0, [], new Dictionary<Guid, string>(), "Nothing was ticked, so nothing moved.");
        }

        var neededFolders = moves
            .Select(move => Path.GetDirectoryName(move.DestinationRelativePath) ?? string.Empty)
            .Where(folder => folder.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folderIds = preview.Plan.Operations
            .OfType<CreateDirectoryOperation>()
            .Where(create => neededFolders.Any(folder => IsSameOrInside(create.DestinationRelativePath, folder)))
            .Select(create => create.Id);
        var approval = Approval.Create(Guid.NewGuid(), preview.Plan, folderIds.Concat(moves.Select(move => move.Id)), clock.UtcNow);
        var expected = moves.ToDictionary(
            move => move.Id,
            move => new ExpectedFile(preview.MoveSources[move.Id].SizeBytes, preview.MoveSources[move.Id].ModifiedAtUtc));

        var result = await executor.ExecuteAsync(preview.Plan, approval, expected, cancellationToken).ConfigureAwait(false);

        var outcomes = result.Operations.ToDictionary(item => item.OperationId);
        var moved = new Dictionary<Guid, string>();
        var skipped = new List<TidyFileOutcome>();
        foreach (var move in moves)
        {
            if (outcomes.TryGetValue(move.Id, out var outcome) && outcome.Outcome == ExecutionOutcome.Completed)
            {
                moved[move.Id] = move.SourceRelativePath;
            }
            else
            {
                skipped.Add(new(move.SourceRelativePath, outcome?.Error ?? "It was not moved."));
            }
        }

        var foldersUsed = moves
            .Where(move => moved.ContainsKey(move.Id))
            .Select(move => Path.GetDirectoryName(move.DestinationRelativePath) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var name = preview.Root.DisplayName;
        var summary = moved.Count == moves.Length
            ? $"Done. {name}: {Files(moved.Count)} tidied into {Folders(foldersUsed)}."
            : moved.Count == 0
                ? $"Nothing was moved in {name}."
                : $"{name}: {moved.Count} of {moves.Length} files tidied into {Folders(foldersUsed)}. The rest stayed where they were.";
        return new TidyRunResult(
            moved.Count > 0 ? result.TransactionId : null,
            moved.Count,
            moves.Length,
            foldersUsed,
            skipped,
            moved,
            summary);
    }

    /// <param name="movedFiles">The files the tidy moved, from its <see cref="TidyRunResult"/>.</param>
    public Task<TidyUndoResult> UndoAsync(
        Guid rootId,
        Guid transactionId,
        IReadOnlyDictionary<Guid, string> movedFiles,
        CancellationToken cancellationToken = default) =>
        UndoAsync(rootId, transactionId, movedFiles, null, cancellationToken);

    /// <param name="madeFolders">
    /// Set for a run that only made folders: the folders it made, by operation ID. The result is
    /// then counted and worded in folders removed rather than files put back.
    /// </param>
    public async Task<TidyUndoResult> UndoAsync(
        Guid rootId,
        Guid transactionId,
        IReadOnlyDictionary<Guid, string> movedFiles,
        IReadOnlyDictionary<Guid, string>? madeFolders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movedFiles);
        var byFolders = madeFolders is not null;
        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return new(false, false, 0, [], byFolders
                ? "That folder is no longer connected, so nothing was removed."
                : "That folder is no longer connected, so nothing was moved back.");
        }

        if (!RootCapabilities.CanTidy(root))
        {
            return new(true, false, 0, [], byFolders
                ? "Undo removes the folders DeskAI made, so DeskAI needs your permission to tidy this folder again."
                : "Undo moves files too, so DeskAI needs your permission to tidy this folder again.");
        }

        UndoResult result;
        try
        {
            result = await executor.UndoAsync(transactionId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return new(false, false, 0, [], exception.Message);
        }

        var named = madeFolders ?? movedFiles;
        var restored = 0;
        var notRestored = new List<TidyFileOutcome>();
        foreach (var item in result.Operations.Where(item => named.ContainsKey(item.OperationId)))
        {
            if (item.Outcome == ExecutionOutcome.Completed)
            {
                restored++;
            }
            else
            {
                notRestored.Add(new(named[item.OperationId], item.Error ?? (byFolders ? "It was left in place." : "It was not moved back.")));
            }
        }

        var summary = byFolders
            ? notRestored.Count == 0
                ? $"Undone. {Folders(restored)} removed."
                : $"{restored} of {Folders(restored + notRestored.Count)} removed. The rest were left in place."
            : notRestored.Count == 0
                ? $"Undone. {Files(restored)} went back where {(restored == 1 ? "it was" : "they were")}."
                : $"{restored} of {restored + notRestored.Count} files went back. The rest stayed where they are now.";
        return new(false, true, restored, notRestored, summary);
    }

    private static bool IsSameOrInside(string folder, string destinationFolder) =>
        string.Equals(folder, destinationFolder, StringComparison.OrdinalIgnoreCase) ||
        destinationFolder.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private static string Folders(int count) => count == 1 ? "1 folder" : $"{count} folders";
}
