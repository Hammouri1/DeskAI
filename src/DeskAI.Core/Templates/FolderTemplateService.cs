using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Templates;

public enum FolderTemplateLineKind
{
    WillMake,
    AlreadyThere,
    Blocked,
}

/// <summary>One folder in a template preview: made, already there, or not possible and why.</summary>
public sealed record FolderTemplateLine(string Name, FolderTemplateLineKind Kind, string? Reason);

/// <summary>A folder the run did not make, or undo did not remove, and why in plain words.</summary>
public sealed record FolderOutcome(string Name, string Reason);

/// <summary>
/// Exactly what making a template in a folder would do right now. Changes nothing.
/// </summary>
/// <param name="NeedsPermission">The folder may not be tidied yet, so nothing was looked at.</param>
/// <param name="Problem">Why no list could be made, in plain words.</param>
/// <param name="Plan">The folders to make, as the plan the executor will be given.</param>
public sealed record FolderTemplatePreview(
    FolderTemplate Template,
    Guid RootId,
    string FolderName,
    IReadOnlyList<FolderTemplateLine> Lines,
    bool NeedsPermission,
    string? Problem,
    OrganizationPlan? Plan)
{
    public int ToMake => Lines.Count(line => line.Kind == FolderTemplateLineKind.WillMake);

    public bool CanMake => !NeedsPermission && Problem is null && Plan is not null && ToMake > 0;

    /// <summary>The names that would be made, for telling one preview from a fresh one.</summary>
    internal IReadOnlySet<string> NamesToMake => Lines
        .Where(line => line.Kind == FolderTemplateLineKind.WillMake)
        .Select(line => line.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>What pressing Make did.</summary>
/// <param name="TransactionId">The record to undo, or null when nothing was made.</param>
/// <param name="MadeById">Each made folder by its operation ID, so undo can name it.</param>
/// <param name="LookAgain">
/// Set when the folder changed between the preview and pressing Make: the fresh list, shown
/// instead of guessing.
/// </param>
public sealed record FolderTemplateOutcome(
    FolderTemplate Template,
    string FolderName,
    Guid? TransactionId,
    IReadOnlyList<string> Made,
    IReadOnlyList<string> AlreadyThere,
    IReadOnlyList<FolderOutcome> NotMade,
    IReadOnlyDictionary<Guid, string> MadeById,
    FolderTemplatePreview? LookAgain,
    string? Problem)
{
    public bool CanUndo => TransactionId is not null && Made.Count > 0;
}

/// <summary>What pressing Undo did.</summary>
/// <param name="NeedsPermission">Nothing was tried because the folder may no longer be tidied.</param>
public sealed record FolderTemplateUndoOutcome(
    bool NeedsPermission,
    int Removed,
    IReadOnlyList<FolderOutcome> LeftInPlace,
    string? Problem)
{
    public string Summary => Problem ?? (LeftInPlace.Count == 0
        ? $"Removed {Folders(Removed)}."
        : $"Removed {Removed} of {Folders(Removed + LeftInPlace.Count)}. {string.Join(" ", LeftInPlace.Select(item => $"{item.Name}: {item.Reason}"))}");

    private static string Folders(int count) => count == 1 ? "1 folder" : $"{count} folders";
}

/// <summary>A folder's last template run, found again from the journal after DeskAI was reopened.</summary>
public sealed record LastFolderTemplate(
    Guid TransactionId,
    DateTimeOffset FinishedAtUtc,
    IReadOnlyDictionary<Guid, string> MadeById)
{
    public IReadOnlyList<string> Made => MadeById.Values.ToArray();
}

/// <summary>
/// Previews, makes, and undoes folder templates in a connected folder.
/// </summary>
/// <remarks>
/// <para>
/// This is the one My workspace service that can change a folder, and it can do exactly one
/// thing: make empty folders directly inside a connected folder the person allowed DeskAI to
/// tidy. It builds an <see cref="OrganizationPlan"/> of nothing but
/// <see cref="CreateDirectoryOperation"/>s, asks the safety policy, and hands the plan to the
/// same executor Tidy uses, which journals it, re-checks the folder before each folder, and is
/// the only code that touches the disk. There is no second way to make a folder.
/// </para>
/// <para>
/// It holds no scanner, no reader, no AI, no credential, and no rule evaluator; a test fails if
/// one is added. The names it makes come from a compiled catalog or from names the person typed
/// and <see cref="FolderNameCheck"/> accepted, and the policy checks each again.
/// </para>
/// </remarks>
public sealed class FolderTemplateService(
    IAuthorizedRootRepository roots,
    IReadOnlyFolderService folders,
    IFolderNameLookup lookup,
    IPlanSafetyCheck safety,
    IFolderTidyExecutor executor,
    IOperationJournal journal,
    IClock clock)
{
    /// <summary>How many of a folder's newest records are read to find its last template run.</summary>
    public const int LookBack = 20;

    private const string ChangedMessage = "The folder changed while the list was open. Look again.";
    private const string OpenQuestionMessage =
        "DeskAI stopped part-way in this folder last time. Open it in Organize and answer the question first.";

    /// <summary>What making a built-in template would do right now. Changes nothing.</summary>
    public Task<FolderTemplatePreview> PreviewAsync(Guid rootId, string templateId, CancellationToken cancellationToken = default)
    {
        var template = FolderTemplateCatalog.Find(templateId)
            ?? throw new ArgumentException($"There is no folder template called \"{templateId}\".", nameof(templateId));
        return PlanAsync(rootId, template, cancellationToken);
    }

    /// <summary>What making the folders a person typed would do right now. Changes nothing.</summary>
    public Task<FolderTemplatePreview> PreviewOwnAsync(Guid rootId, string? typedNames, CancellationToken cancellationToken = default)
    {
        var names = FolderNameCheck.Parse(typedNames, out var problem);
        return problem is not null
            ? Task.FromResult(new FolderTemplatePreview(FolderTemplate.Own([]), rootId, string.Empty, [], false, problem, null))
            : PlanAsync(rootId, FolderTemplate.Own(names), cancellationToken);
    }

    /// <summary>
    /// Makes the folders, after the person agreed to the preview.
    /// </summary>
    /// <remarks>
    /// The list is worked out again from the folder as it is now rather than trusted. If a
    /// different set of folders would be made, nothing is made and the fresh list comes back
    /// for the person to look at: a dialog left open is not consent to whatever changed behind it.
    /// </remarks>
    public async Task<FolderTemplateOutcome> MakeAsync(FolderTemplatePreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.CanMake)
        {
            return Nothing(preview, "There is nothing to make.");
        }

        var fresh = await PlanAsync(preview.RootId, preview.Template, cancellationToken).ConfigureAwait(false);
        if (fresh.NeedsPermission)
        {
            return Nothing(fresh, "DeskAI may not tidy this folder any more, so no folder was made.");
        }

        if (fresh.Problem is not null || fresh.Plan is null)
        {
            return Nothing(fresh, fresh.Problem ?? "There is nothing to make.");
        }

        if (!fresh.NamesToMake.SetEquals(preview.NamesToMake))
        {
            return Nothing(fresh, ChangedMessage) with { LookAgain = fresh };
        }

        // The fresh look only confirms the list; what runs is the plan the person saw, so the
        // approval and the journal name exactly the operations that were on screen. The
        // executor checks the plan against the policy again before anything is made.
        var plan = preview.Plan!;
        var approval = Approval.Create(Guid.NewGuid(), plan, plan.Operations.Select(operation => operation.Id), clock.UtcNow);
        var result = await executor.ExecuteAsync(plan, approval, new Dictionary<Guid, ExpectedFile>(), cancellationToken)
            .ConfigureAwait(false);

        // The journal knows whether a folder was made or found already there; the result alone
        // calls both "completed". A folder found already there must never be listed as made,
        // or the person would expect undo to remove it.
        var record = await journal.FindAsync(result.TransactionId, cancellationToken).ConfigureAwait(false);
        var states = record?.Operations.ToDictionary(operation => operation.OperationId, operation => operation.State)
            ?? new Dictionary<Guid, JournalOperationState>();
        var outcomes = result.Operations.ToDictionary(item => item.OperationId);
        var made = new Dictionary<Guid, string>();
        var alreadyThere = fresh.Lines
            .Where(line => line.Kind == FolderTemplateLineKind.AlreadyThere)
            .Select(line => line.Name)
            .ToList();
        var notMade = fresh.Lines
            .Where(line => line.Kind == FolderTemplateLineKind.Blocked)
            .Select(line => new FolderOutcome(line.Name, line.Reason ?? "It can't be made here."))
            .ToList();
        foreach (var create in plan.Operations.OfType<CreateDirectoryOperation>())
        {
            var name = create.DestinationRelativePath;
            if (states.TryGetValue(create.Id, out var state) && state == JournalOperationState.AlreadyPresent)
            {
                alreadyThere.Add(name);
            }
            else if (outcomes.TryGetValue(create.Id, out var outcome) && outcome.Outcome == ExecutionOutcome.Completed)
            {
                made[create.Id] = name;
            }
            else
            {
                notMade.Add(new FolderOutcome(name, outcomes.GetValueOrDefault(create.Id)?.Error ?? "It was not made."));
            }
        }

        return new FolderTemplateOutcome(
            fresh.Template,
            fresh.FolderName,
            made.Count > 0 ? result.TransactionId : null,
            made.Values.ToArray(),
            alreadyThere.AsReadOnly(),
            notMade.AsReadOnly(),
            made,
            null,
            null);
    }

    /// <summary>Removes the folders a run made, each only if it is still empty.</summary>
    /// <param name="madeById">The folders the run made, from its outcome or <see cref="LastFolderTemplate"/>.</param>
    public async Task<FolderTemplateUndoOutcome> UndoAsync(
        Guid rootId,
        Guid transactionId,
        IReadOnlyDictionary<Guid, string> madeById,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(madeById);
        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return new(false, 0, [], "That folder is no longer connected, so nothing was removed.");
        }

        if (!RootCapabilities.CanTidy(root))
        {
            return new(true, 0, [], "Undo removes the folders DeskAI made, so DeskAI needs your permission to tidy this folder again.");
        }

        UndoResult result;
        try
        {
            result = await executor.UndoAsync(transactionId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return new(false, 0, [], exception.Message);
        }

        var removed = 0;
        var left = new List<FolderOutcome>();
        foreach (var item in result.Operations.Where(item => madeById.ContainsKey(item.OperationId)))
        {
            if (item.Outcome == ExecutionOutcome.Completed)
            {
                removed++;
            }
            else
            {
                left.Add(new FolderOutcome(madeById[item.OperationId], item.Error ?? "It was left in place."));
            }
        }

        return new(false, removed, left.AsReadOnly(), null);
    }

    /// <summary>
    /// The folder's latest run that only made folders, if nothing has run there since and it
    /// has not been undone.
    /// </summary>
    /// <remarks>
    /// Only the latest run in the folder counts. A tidy after the template means the template's
    /// undo is no longer offered here: undoing out of order is not what undo means. While a
    /// record of the folder is unfinished there is nothing to offer; that question comes first.
    /// </remarks>
    public async Task<LastFolderTemplate?> FindLastAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var records = await journal.ListForRootAsync(rootId, LookBack, cancellationToken).ConfigureAwait(false);
        var undone = records
            .Where(record => record.Kind == ExecutionTransactionKind.Undo &&
                             record.OriginalTransactionId is not null &&
                             record.State is not (ExecutionTransactionState.Failed or ExecutionTransactionState.Cancelled))
            .Select(record => record.OriginalTransactionId!.Value)
            .ToHashSet();
        foreach (var record in records.Where(record => record.Kind == ExecutionTransactionKind.Execute))
        {
            if (IsUnfinished(record))
            {
                return null;
            }

            if (!record.Operations.All(operation => operation.Kind == PlanOperationKind.CreateDirectory))
            {
                return null;
            }

            var made = record.Operations
                .Where(operation => operation.State == JournalOperationState.Completed)
                .ToDictionary(operation => operation.OperationId, operation => operation.DestinationRelativePath);
            if (made.Count == 0)
            {
                continue;
            }

            if (record.State == ExecutionTransactionState.Undone || undone.Contains(record.Id))
            {
                return null;
            }

            return new LastFolderTemplate(record.Id, record.FinishedAtUtc ?? record.StartedAtUtc, made);
        }

        return null;
    }

    /// <summary>True while a record of the folder waits for the person's answer on Organize.</summary>
    public async Task<bool> HasOpenQuestionAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        (await journal.ListForRootAsync(rootId, LookBack, cancellationToken).ConfigureAwait(false)).Any(IsUnfinished);

    private static bool IsUnfinished(ExecutionJournalEntry record) =>
        record.State is ExecutionTransactionState.Prepared
            or ExecutionTransactionState.Executing
            or ExecutionTransactionState.RecoveryRequired;

    private async Task<FolderTemplatePreview> PlanAsync(Guid rootId, FolderTemplate template, CancellationToken cancellationToken)
    {
        if (template.Folders.Count == 0 || template.Folders.Count > FolderTemplateCatalog.MaxFolders)
        {
            throw new ArgumentException($"A template makes between 1 and {FolderTemplateCatalog.MaxFolders} folders.", nameof(template));
        }

        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return Refused(template, rootId, string.Empty, "That folder is no longer connected.");
        }

        if (!RootCapabilities.CanTidy(root))
        {
            return new FolderTemplatePreview(template, rootId, root.DisplayName, [], true, null, null);
        }

        if (await folders.CheckStillSafeAsync(root, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return Refused(template, rootId, root.DisplayName, problem);
        }

        if (await HasOpenQuestionAsync(rootId, cancellationToken).ConfigureAwait(false))
        {
            return Refused(template, rootId, root.DisplayName, OpenQuestionMessage);
        }

        IReadOnlyList<FolderEntryPresence> present;
        try
        {
            present = await lookup.LookAsync(root, template.Folders, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Refused(template, rootId, root.DisplayName, "DeskAI could not look inside that folder right now.");
        }

        var lines = new List<FolderTemplateLine>();
        var candidates = new List<CreateDirectoryOperation>();
        foreach (var name in template.Folders)
        {
            var found = present.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            switch (found?.Kind ?? FolderEntryKind.Missing)
            {
                case FolderEntryKind.Folder:
                    lines.Add(new FolderTemplateLine(found!.Name, FolderTemplateLineKind.AlreadyThere, null));
                    break;
                case FolderEntryKind.File:
                    lines.Add(new FolderTemplateLine(name, FolderTemplateLineKind.Blocked, $"A file called {found!.Name} is already there."));
                    break;
                case FolderEntryKind.Link:
                    lines.Add(new FolderTemplateLine(name, FolderTemplateLineKind.Blocked, $"{found!.Name} is a link or shortcut, so it was left alone."));
                    break;
                default:
                    var create = new CreateDirectoryOperation(
                        Guid.NewGuid(), name, $"Part of the {template.Name} folders.", OperationProvenance.User);
                    candidates.Add(create);
                    lines.Add(new FolderTemplateLine(name, FolderTemplateLineKind.WillMake, null));
                    break;
            }
        }

        if (candidates.Count == 0)
        {
            return new FolderTemplatePreview(template, rootId, root.DisplayName, lines.AsReadOnly(), false, null, null);
        }

        // The policy sees every name, typed or built in, before a plan exists that could be run.
        var candidatePlan = Draft(root, candidates);
        var blocked = safety.FindBlocked(candidatePlan, root);
        if (blocked.ContainsKey(Guid.Empty))
        {
            return Refused(template, rootId, root.DisplayName, "DeskAI's safety rules do not allow changes in this folder.");
        }

        var allowed = candidates.Where(create => !blocked.ContainsKey(create.Id)).ToArray();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Kind == FolderTemplateLineKind.WillMake &&
                candidates.Any(create => blocked.ContainsKey(create.Id) && create.DestinationRelativePath == line.Name))
            {
                lines[index] = line with
                {
                    Kind = FolderTemplateLineKind.Blocked,
                    Reason = "DeskAI's safety rules don't allow a folder with that name here.",
                };
            }
        }

        return new FolderTemplatePreview(
            template,
            rootId,
            root.DisplayName,
            lines.AsReadOnly(),
            false,
            null,
            allowed.Length == 0 ? null : Draft(root, allowed));
    }

    private OrganizationPlan Draft(AuthorizedRoot root, IReadOnlyList<CreateDirectoryOperation> operations) =>
        OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, clock.UtcNow, safety.PolicyVersion, operations);

    private static FolderTemplatePreview Refused(FolderTemplate template, Guid rootId, string folderName, string problem) =>
        new(template, rootId, folderName, [], false, problem, null);

    private static FolderTemplateOutcome Nothing(FolderTemplatePreview preview, string problem) =>
        new(preview.Template, preview.FolderName, null, [], [], [], new Dictionary<Guid, string>(), null, problem);
}
