using System.Globalization;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Core.Rules;

namespace DeskAI.Core.Tidy;

/// <summary>The sentences that change with the mode, in one place so no page can drift from the truth.</summary>
public static class AwayTidyWords
{
    /// <summary>The headline promise: true with the mode off everywhere, and the narrower truth with it on.</summary>
    public static string Promise(int foldersOn) => foldersOn == 0
        ? "DeskAI never moves a file on its own"
        : $"DeskAI moves files on its own only in {Folders(foldersOn)} where you turned on Tidy while I'm away";

    /// <summary>The sentence that ends a checking summary.</summary>
    public static string Sentence(int foldersOn) => foldersOn == 0
        ? "It never moves anything by itself."
        : $"It moves files on its own only in {Folders(foldersOn)} where you turned on Tidy while I'm away, and only what your rules match.";

    /// <summary>The short pill on Home.</summary>
    public static string Pill(int foldersOn) => foldersOn == 0
        ? "Nothing moves by itself"
        : $"Moves files on its own in {Folders(foldersOn)} you chose";

    public static string Folders(int count) => count == 1 ? "1 folder" : $"{count} folders";

    public static string Files(int count) => count == 1 ? "1 file" : $"{count} files";
}

/// <summary>
/// Tidies a folder while nobody is watching, under a standing yes and a hard ceiling.
/// </summary>
/// <remarks>
/// <para>
/// The one type reachable from an automatic check that can move a file. It holds
/// <see cref="TidyRunService"/>, so a run goes through the same executor, journal, and per-file
/// re-checks as a hand tidy; it holds no AI, no reader, no credential, and a test asserts it.
/// </para>
/// <para>
/// A run moves only loose files a switched-on rule places, never a file placed by type or by
/// AI, never one with a same-name clash, and at most <see cref="AwayTidyLimits.MaxFilesPerRun"/>.
/// Anything unexpected — a rule changed since the yes, a clash, a file the executor refused, a
/// folder that cannot be looked at — turns the mode off with the reason kept for the folder to
/// show. See ADR 0031 and the review of 2026-09-16.
/// </para>
/// </remarks>
public sealed class AwayTidyService(
    IAwayTidyRepository away,
    IAuthorizedRootRepository roots,
    IRuleRepository rules,
    TidySuggestionService suggestions,
    TidyRunService runs,
    IClock clock) : IAwayTidyRunner
{
    private readonly IAwayTidyRepository _away = away;
    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IRuleRepository _rules = rules;
    private readonly TidySuggestionService _suggestions = suggestions;
    private readonly TidyRunService _runs = runs;
    private readonly IClock _clock = clock;

    /// <summary>The rules that are on right now, for the dialog to name before the yes.</summary>
    public async Task<IReadOnlyList<AutomationRule>> EnabledRulesAsync(CancellationToken cancellationToken = default) =>
        (await _rules.ListAsync(cancellationToken).ConfigureAwait(false)).Where(rule => rule.IsEnabled).ToArray();

    /// <summary>How many folders have the mode on. The wording on every page follows this number.</summary>
    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        (await _away.ListAsync(cancellationToken).ConfigureAwait(false)).Count(approval => approval.IsActive);

    /// <summary>
    /// What the switch shows for one folder. Checks the rules against the yes, and turns the
    /// mode off with the reason if they no longer match, so the folder never claims a yes that
    /// has expired.
    /// </summary>
    public async Task<AwayTidyStatus> GetStatusAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        var canTidy = root is not null && RootCapabilities.CanTidy(root);
        var allRules = await _rules.ListAsync(cancellationToken).ConfigureAwait(false);
        var enabled = allRules.Count(rule => rule.IsEnabled);
        var approval = await _away.FindAsync(rootId, cancellationToken).ConfigureAwait(false);

        if (approval is { IsActive: true })
        {
            var check = approval.Covers(allRules);
            if (!check.IsValid)
            {
                approval = await StopAsync(approval, check.Explanation, cancellationToken).ConfigureAwait(false);
            }
            else if (!canTidy)
            {
                approval = await StopAsync(approval, "Tidying is no longer allowed for this folder.", cancellationToken).ConfigureAwait(false);
            }
        }

        var canTurnOn = canTidy && enabled > 0;
        if (approval is { IsActive: true })
        {
            var since = approval.ApprovedAtUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
            return new AwayTidyStatus(
                true,
                canTurnOn,
                $"On since {since}, for {Rules(approval.Rules.Count)}. DeskAI moves at most {AwayTidyLimits.MaxFilesPerRun} files each time it checks, and stops if anything looks different.",
                false,
                approval.Rules.Count);
        }

        if (approval is { StoppedReason: { } reason })
        {
            return new AwayTidyStatus(
                false,
                canTurnOn,
                $"DeskAI stopped tidying while you're away: {reason} Turn it on again when you've looked.",
                true,
                0);
        }

        var line = !canTidy
            ? "Allow tidying first."
            : enabled == 0
                ? "Turn on a rule in Automatic tasks first."
                : "Off. DeskAI moves nothing here on its own.";
        return new AwayTidyStatus(false, canTurnOn, line, false, 0);
    }

    /// <summary>The dialog's yes: records the folder and every rule that is on, as worded now.</summary>
    public async Task<AwayTidyStatus> TurnOnAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        var allRules = await _rules.ListAsync(cancellationToken).ConfigureAwait(false);
        if (root is not null && RootCapabilities.CanTidy(root) && allRules.Any(rule => rule.IsEnabled))
        {
            await _away.SaveAsync(AwayTidyApproval.Record(rootId, allRules, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        }

        return await GetStatusAsync(rootId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The switch off: the person's decision, needing no dialog. The reason, if any, goes too.</summary>
    public async Task<AwayTidyStatus> TurnOffAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await _away.RemoveAsync(rootId, cancellationToken).ConfigureAwait(false);
        return await GetStatusAsync(rootId, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AwayTidyRun>> ListUnseenAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        _away.ListUnseenRunsAsync(rootId, AwayTidyLimits.MaxRunsShown, cancellationToken);

    public Task MarkSeenAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        _away.MarkRunsSeenAsync(rootId, _clock.UtcNow, cancellationToken);

    /// <summary>
    /// One pass over every folder with the mode on, after an automatic check. Each folder is
    /// its own run; a problem in one turns that folder's mode off and never touches another.
    /// </summary>
    public async Task<AwayTidySummary> RunAllAsync(CancellationToken cancellationToken = default)
    {
        var foldersTidied = 0;
        var filesMoved = 0;
        var foldersStopped = 0;
        string? folderName = null;
        Guid? folderToReview = null;
        var mostMoved = 0;

        foreach (var approval in await _away.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!approval.IsActive)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            AwayTidyRun? run;
            try
            {
                run = await RunOneAsync(approval, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or System.Data.Common.DbException)
            {
                await StopAsync(approval, $"DeskAI hit a problem: {exception.Message}", cancellationToken).ConfigureAwait(false);
                foldersStopped++;
                continue;
            }

            if (run is null)
            {
                continue;
            }

            if (run.StoppedReason is not null)
            {
                foldersStopped++;
            }

            if (run.Moved > 0)
            {
                foldersTidied++;
                filesMoved += run.Moved;
            }

            if (run.Moved > mostMoved || (folderToReview is null && run.StoppedReason is not null))
            {
                mostMoved = Math.Max(mostMoved, run.Moved);
                folderToReview = run.RootId;
                folderName = (await _roots.FindAsync(run.RootId, cancellationToken).ConfigureAwait(false))?.DisplayName;
            }
        }

        return new AwayTidySummary(foldersTidied, filesMoved, folderName, folderToReview, foldersStopped);
    }

    private async Task<AwayTidyRun?> RunOneAsync(AwayTidyApproval approval, CancellationToken cancellationToken)
    {
        var allRules = await _rules.ListAsync(cancellationToken).ConfigureAwait(false);
        var check = approval.Covers(allRules);
        if (!check.IsValid)
        {
            return await StopRunAsync(approval, check.Explanation, cancellationToken).ConfigureAwait(false);
        }

        var root = await _roots.FindAsync(approval.RootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return null;
        }

        if (!RootCapabilities.CanTidy(root))
        {
            return await StopRunAsync(approval, "Tidying is no longer allowed for this folder.", cancellationToken).ConfigureAwait(false);
        }

        // The same preview a person would see, with no AI advice and no "keep both" choices.
        var preview = await _suggestions.PreviewAsync(
            approval.RootId,
            Guid.NewGuid(),
            1,
            new Dictionary<Guid, SameNameChoice>(),
            TidySuggestionMode.TypesAndRules,
            new Dictionary<Guid, TidyAiAdvice>(),
            cancellationToken).ConfigureAwait(false);
        if (preview is null)
        {
            return await StopRunAsync(approval, "The folder is no longer connected.", cancellationToken).ConfigureAwait(false);
        }

        if (preview.FolderProblem is { } problem)
        {
            return await StopRunAsync(approval, problem, cancellationToken).ConfigureAwait(false);
        }

        if (preview.ScanWasIncomplete)
        {
            return await StopRunAsync(approval, "DeskAI could not look at every file in this folder.", cancellationToken).ConfigureAwait(false);
        }

        // Only what a rule placed. A file placed by type, or by an AI idea, is never moved
        // without a person, and a same-name clash is a choice a person makes.
        var rulePlaced = preview.Suggestions.Where(suggestion => suggestion.Source == TidySuggestionSource.Rule).ToArray();
        if (rulePlaced.FirstOrDefault(suggestion => suggestion.HasSameName) is { } clash)
        {
            return await StopRunAsync(
                approval,
                $"A file called {clash.FileName} is already in {clash.DestinationFolder}, so DeskAI needs you to decide.",
                cancellationToken).ConfigureAwait(false);
        }

        var ids = rulePlaced
            .Where(suggestion => suggestion.MoveOperationId is not null)
            .Take(AwayTidyLimits.MaxFilesPerRun)
            .Select(suggestion => suggestion.MoveOperationId!.Value)
            .ToArray();
        if (ids.Length == 0)
        {
            return null;
        }

        var result = await _runs.TidyAsync(preview, ids, cancellationToken).ConfigureAwait(false);
        var stoppedReason = result.Skipped.Count == 0
            ? null
            : $"{AwayTidyWords.Files(result.Skipped.Count)} couldn't be moved ({result.Skipped[0].Reason}).";
        var run = new AwayTidyRun(
            Guid.NewGuid(),
            approval.RootId,
            result.TransactionId,
            _clock.UtcNow,
            result.Moved,
            result.FoldersUsed,
            result.Skipped.Count,
            stoppedReason,
            null);
        await _away.AppendRunAsync(run, cancellationToken).ConfigureAwait(false);
        if (stoppedReason is not null)
        {
            await StopAsync(approval, stoppedReason, cancellationToken).ConfigureAwait(false);
        }

        return run;
    }

    /// <summary>Turns the mode off and records a run that moved nothing, so the folder shows both.</summary>
    private async Task<AwayTidyRun> StopRunAsync(AwayTidyApproval approval, string reason, CancellationToken cancellationToken)
    {
        await StopAsync(approval, reason, cancellationToken).ConfigureAwait(false);
        var run = new AwayTidyRun(Guid.NewGuid(), approval.RootId, null, _clock.UtcNow, 0, 0, 0, reason, null);
        await _away.AppendRunAsync(run, cancellationToken).ConfigureAwait(false);
        return run;
    }

    private async Task<AwayTidyApproval> StopAsync(AwayTidyApproval approval, string reason, CancellationToken cancellationToken)
    {
        var at = _clock.UtcNow;
        await _away.StopAsync(approval.RootId, at, reason, cancellationToken).ConfigureAwait(false);
        return approval with { StoppedAtUtc = at, StoppedReason = reason };
    }

    private static string Rules(int count) => count == 1 ? "1 rule" : $"{count} rules";
}
