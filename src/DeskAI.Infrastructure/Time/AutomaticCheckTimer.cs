using DeskAI.Core.Rules;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskAI.Infrastructure.Time;

/// <summary>
/// The one clock in DeskAI that makes something happen on its own.
/// </summary>
/// <remarks>
/// <para>
/// It owns no policy. Every tick it asks <see cref="AutomaticCheckCoordinator"/> whether a
/// check is due; whether it is, and what a check may do, are decided in Core and tested
/// there. This class exists only because a timer cannot be a pure function.
/// </para>
/// <para>
/// It ticks more often than any frequency someone can choose, because the tick is not the
/// schedule — the schedule is arithmetic over when the last check happened. A short tick
/// means a check that came due while the app was busy starts soon after rather than at the
/// next hour boundary.
/// </para>
/// <para>
/// This runs while DeskAI runs and stops when DeskAI stops, which is the whole of the
/// <see cref="AutomaticCheckMode.WhileAppIsOpen"/> promise: nothing is registered with
/// Windows, and closing the window ends it. Checking after the window is closed is a
/// separate slice with its own security review, per ADR 0017.
/// </para>
/// </remarks>
public sealed partial class AutomaticCheckTimer(
    AutomaticCheckCoordinator coordinator,
    ILogger<AutomaticCheckTimer> logger) : BackgroundService
{
    /// <summary>
    /// How often the schedule is consulted. Not how often a check happens.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A pause before the first tick so launching DeskAI stays responsive. A check that was
    /// already overdue waits a few more seconds; the window opening does not.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            using var ticks = new PeriodicTimer(TickInterval);
            while (await ticks.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await coordinator.RunIfDueAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // One failed check must not end automatic checking altogether. The next
                    // tick tries again; a folder that has genuinely gone away simply keeps
                    // being skipped.
                    LogCheckFailed(logger, exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // DeskAI is closing. Stopping mid-check is safe: a check changes nothing.
        }
    }

    [LoggerMessage(
        EventId = 4200,
        Level = LogLevel.Warning,
        Message = "An automatic check could not be completed. The next one will try again.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
