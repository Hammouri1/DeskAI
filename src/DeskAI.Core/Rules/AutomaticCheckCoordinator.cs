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
public sealed class AutomaticCheckCoordinator(AutomaticCheckService checks) : IDisposable
{
    private readonly AutomaticCheckService _checks = checks;
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
        try
        {
            var result = onlyWhenDue
                ? await _checks.RunIfDueAsync(linked.Token).ConfigureAwait(false)
                : await _checks.RunAsync(linked.Token).ConfigureAwait(false);

            if (result is not null)
            {
                Latest = result;
                Checked?.Invoke(this, result);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // Stopping a check is a normal outcome, not a failure. Nothing was changed by
            // it, so there is nothing to unwind and nothing to report.
            return null;
        }
        finally
        {
            _running = null;
            _gate.Release();
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
