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
/// </remarks>
public sealed class TidyRunService(IFolderTidyExecutor executor, IAuthorizedRootRepository roots, IClock clock)
{
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
    public async Task<TidyUndoResult> UndoAsync(
        Guid rootId,
        Guid transactionId,
        IReadOnlyDictionary<Guid, string> movedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movedFiles);
        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return new(false, false, 0, [], "That folder is no longer connected, so nothing was moved back.");
        }

        if (!RootCapabilities.CanTidy(root))
        {
            return new(true, false, 0, [], "Undo moves files too, so DeskAI needs your permission to tidy this folder again.");
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

        var restored = 0;
        var notRestored = new List<TidyFileOutcome>();
        foreach (var item in result.Operations.Where(item => movedFiles.ContainsKey(item.OperationId)))
        {
            if (item.Outcome == ExecutionOutcome.Completed)
            {
                restored++;
            }
            else
            {
                notRestored.Add(new(movedFiles[item.OperationId], item.Error ?? "It was not moved back."));
            }
        }

        var summary = notRestored.Count == 0
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
