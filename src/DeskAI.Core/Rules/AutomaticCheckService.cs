using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.Rules;

/// <summary>What one automatic check found.</summary>
/// <remarks>
/// A count, a moment, and which folder to look at first. There is deliberately nothing here
/// that could be carried out — no plan, no operation, no path. Reviewing is a separate thing a
/// person starts, and tidying a separate thing they press.
/// </remarks>
/// <param name="FolderToReview">The connected folder with the most matches, if any matched.</param>
public sealed record AutomaticCheckResult(
    int FoldersChecked,
    int ProposalCount,
    int ConflictCount,
    DateTimeOffset CheckedAtUtc,
    bool WasCatchUp = false,
    Guid? FolderToReview = null)
{
    public static AutomaticCheckResult Nothing(DateTimeOffset checkedAtUtc) => new(0, 0, 0, checkedAtUtc);

    /// <summary>Whether there is anything worth putting in front of someone.</summary>
    public bool HasSomethingToReview => ProposalCount > 0;
}

/// <summary>
/// Looks for rule matches in connected folders, and says what it found.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what an automatic check does: refresh what DeskAI remembers about
/// each connected folder, run the existing practice run over it, and return a count. It ends
/// at a number on a screen.
/// </para>
/// <para>
/// It holds no executor and no planner, and could not carry a proposal out even if asked to
/// — the objects that would be needed are not in the constructor. That is the point rather
/// than an accident: this is the first thing in DeskAI that acts without a person pressing
/// something at that moment, so what it is capable of is bounded by what it can reach. A
/// test asserts a full check writes nothing to disk.
/// </para>
/// <para>
/// Refreshing reads metadata only, through the same bounded scan connecting uses. A folder
/// allowed to have its contents read is not read here: rules test names, sizes, and dates,
/// and nothing in this path opens a file.
/// </para>
/// <para>
/// A folder that refuses a refresh — disconnected mid-check, or newly blocked by path
/// policy — is skipped rather than failing the check. The remaining folders are still worth
/// reporting, and a refusal is already the correct outcome for that folder.
/// </para>
/// </remarks>
public sealed class AutomaticCheckService(
    ConnectedFolderService folders,
    RuleSimulationService simulation,
    IAutomaticCheckSettingsRepository settings,
    IAuthorizedRootRepository roots,
    IClock clock)
{
    private readonly ConnectedFolderService _folders = folders;
    private readonly RuleSimulationService _simulation = simulation;
    private readonly IAutomaticCheckSettingsRepository _settings = settings;
    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IClock _clock = clock;

    /// <summary>Checks now, whatever the schedule says.</summary>
    /// <remarks>
    /// Used both by the timer once a check is due and by the "Check now" button. Pausing
    /// stops automatic checks; it does not stop a person asking for one.
    /// </remarks>
    public async Task<AutomaticCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var checkedAt = _clock.UtcNow;
        var wasCatchUp = await WasOverdueAsync(checkedAt, cancellationToken).ConfigureAwait(false);

        var searchable = (await _roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(FileSearchService.IsSearchable)
            .ToArray();

        foreach (var root in searchable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _folders.RefreshAsync(root.Id, cancellationToken).ConfigureAwait(false);
        }

        var found = await _simulation.SimulateAsync(checkedAt, cancellationToken).ConfigureAwait(false);
        await _settings.RecordCheckedAtAsync(checkedAt, cancellationToken).ConfigureAwait(false);

        var mostMatches = found.Folders
            .Where(folder => folder.Preview.Proposals.Count > 0)
            .OrderByDescending(folder => folder.Preview.Proposals.Count)
            .FirstOrDefault();
        return new AutomaticCheckResult(
            searchable.Length,
            found.ProposalCount,
            found.ConflictCount,
            checkedAt,
            wasCatchUp,
            mostMatches?.RootId);
    }

    /// <summary>
    /// Whether this check is arriving late — the first one after DeskAI was closed or paused.
    /// </summary>
    /// <remarks>
    /// Missed checks are never replayed, so this is how the gap gets reported instead of
    /// hidden. "Late" means more than two intervals since the last check: one interval is
    /// simply the normal wait, and a little drift is not worth remarking on.
    /// </remarks>
    private async Task<bool> WasOverdueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var chosen = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (AutomaticCheckSchedule.IntervalFor(chosen.Frequency) is not { } interval)
        {
            return false;
        }

        var last = await _settings.ReadLastCheckedAtUtcAsync(cancellationToken).ConfigureAwait(false);
        return last is { } previous && previous <= nowUtc && nowUtc - previous > interval + interval;
    }

    /// <summary>Checks only if the schedule says one is due, and reports whether it did.</summary>
    public async Task<AutomaticCheckResult?> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        var chosen = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var last = await _settings.ReadLastCheckedAtUtcAsync(cancellationToken).ConfigureAwait(false);

        return AutomaticCheckSchedule.IsDue(chosen, last, _clock.UtcNow)
            ? await RunAsync(cancellationToken).ConfigureAwait(false)
            : null;
    }
}
