using DeskAI.Core.Abstractions;

namespace DeskAI.Core.Rules;

/// <summary>
/// Runs at most one automatic check at a time, and announces what it found.
/// </summary>
/// <remarks>
/// <para>
/// The timer lives in Infrastructure and the screen lives in the app; both come through
/// here, so "one check at a time" is one rule in one place rather than a convention each
/// caller has to remember. Two passes over the same folders would produce two counts for one
/// state and race each other to the notice.
/// </para>
/// <para>
/// A second request while a check is running is dropped rather than queued. A check re-reads
/// current state, so the one already in flight will report it; queueing would only mean
/// checking again immediately for the same answer.
/// </para>
/// <para>
/// <see cref="StopRunningCheck"/> cancels a check that is already under way. Someone
/// reaching for a stop control means the thing happening now, not the next one.
/// </para>
/// </remarks>
public sealed class AutomaticCheckCoordinator(
    AutomaticCheckService checks,
    IAutomaticCheckHistoryRepository history,
    IClock clock) : IDisposable
{
    private readonly AutomaticCheckService _checks = checks;
    private readonly IAutomaticCheckHistoryRepository _history = history;
    private readonly IClock _clock = clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _running;
    private bool _disposed;

    /// <summary>Raised when a check finishes, with what it found.</summary>
    public event EventHandler<AutomaticCheckResult>? Checked;

    /// <summary>The most recent result, or null when nothing has been checked yet.</summary>
    public AutomaticCheckResult? Latest { get; private set; }

    /// <summary>Whether a check is happening right now.</summary>
    public bool IsChecking => _gate.CurrentCount == 0;

    /// <summary>Checks now because someone asked. Null when one was already running.</summary>
    public Task<AutomaticCheckResult?> RunNowAsync(CancellationToken cancellationToken = default) =>
        RunAsync(onlyWhenDue: false, cancellationToken);

    /// <summary>Checks if the schedule says one is due. Null when it is not, or one is running.</summary>
    public Task<AutomaticCheckResult?> RunIfDueAsync(CancellationToken cancellationToken = default) =>
        RunAsync(onlyWhenDue: true, cancellationToken);

    /// <summary>Stops a check that is under way. Does nothing when none is.</summary>
    public void StopRunningCheck()
    {
        try
        {
            _running?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The check finished between the null check and the cancel. Nothing to stop.
        }
    }

    private async Task<AutomaticCheckResult?> RunAsync(bool onlyWhenDue, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = linked;
        var startedAt = _clock.UtcNow;
        try
        {
            var result = onlyWhenDue
                ? await _checks.RunIfDueAsync(linked.Token).ConfigureAwait(false)
                : await _checks.RunAsync(linked.Token).ConfigureAwait(false);

            // A check that was not due did not happen, so there is nothing to record. Only
            // checks that actually ran belong in the history.
            if (result is not null)
            {
                Latest = result;
                await RecordAsync(startedAt, AutomaticCheckOutcome.Completed, result).ConfigureAwait(false);
                Checked?.Invoke(this, result);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // Stopping a check is a normal outcome, not a failure. Nothing was changed by
            // it, so there is nothing to unwind — but it is still recorded, because a
            // history that quietly omits the interrupted runs is not a history.
            await RecordAsync(startedAt, AutomaticCheckOutcome.Stopped, result: null).ConfigureAwait(false);
            return null;
        }
        catch (Exception)
        {
            await RecordAsync(startedAt, AutomaticCheckOutcome.Failed, result: null).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _running = null;
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes one run into the history.
    /// </summary>
    /// <remarks>
    /// A failure to record is swallowed. The history is a convenience for looking back, and
    /// losing a line of it is not worth turning a harmless check into an error someone has
    /// to deal with — there is no file change here whose record could go missing.
    /// </remarks>
    private async Task RecordAsync(
        DateTimeOffset startedAt,
        AutomaticCheckOutcome outcome,
        AutomaticCheckResult? result)
    {
        try
        {
            await _history.AppendAsync(new AutomaticCheckRun(
                Guid.NewGuid(),
                startedAt,
                _clock.UtcNow,
                outcome,
                result?.FoldersChecked ?? 0,
                result?.ProposalCount ?? 0,
                result?.ConflictCount ?? 0,
                result?.WasCatchUp ?? false)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException
            or OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRunningCheck();
        _gate.Dispose();
    }
}
