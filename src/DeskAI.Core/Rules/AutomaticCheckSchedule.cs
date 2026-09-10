namespace DeskAI.Core.Rules;

/// <summary>
/// Decides whether an automatic check is due. Pure arithmetic over a moment.
/// </summary>
/// <remarks>
/// <para>
/// The timer belongs to Infrastructure; the policy belongs here. Keeping them apart means
/// every awkward case — never checked before, closed for a week, a clock corrected
/// backwards, paused mid-flight — is settled by a unit test with a fixed moment instead of
/// by waiting for a real timer to fire.
/// </para>
/// <para>
/// Nothing here reads a folder, a rule, or a file. It answers "should something happen now?"
/// and nothing about what that something is.
/// </para>
/// </remarks>
public static class AutomaticCheckSchedule
{
    /// <summary>How long each frequency waits, or null when nothing happens on its own.</summary>
    public static TimeSpan? IntervalFor(AutomaticCheckFrequency frequency) => frequency switch
    {
        AutomaticCheckFrequency.EveryFifteenMinutes => TimeSpan.FromMinutes(15),
        AutomaticCheckFrequency.EveryHour => TimeSpan.FromHours(1),
        AutomaticCheckFrequency.ACoupleOfTimesADay => TimeSpan.FromHours(8),
        _ => null,
    };

    /// <summary>
    /// When the next check falls due, or null when no check will happen on its own.
    /// </summary>
    /// <remarks>
    /// An overdue check is reported as due <em>now</em> rather than at the moment it was
    /// missed. A check re-reads current state, so it carries no per-occurrence meaning:
    /// after a week closed, DeskAI owes one check, not a week of them.
    /// </remarks>
    public static DateTimeOffset? NextDueAt(
        AutomaticCheckSettings settings,
        DateTimeOffset? lastCheckedUtc,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.IsPaused)
        {
            return null;
        }

        if (IntervalFor(settings.Frequency) is not { } interval)
        {
            return null;
        }

        if (lastCheckedUtc is not { } last || last > nowUtc)
        {
            // Never checked, or the clock moved backwards and left the last check in the
            // future. Either way DeskAI cannot account for the gap, so it checks rather than
            // waiting for a clock it does not trust.
            return nowUtc;
        }

        var due = last + interval;
        return due <= nowUtc ? nowUtc : due;
    }

    public static bool IsDue(
        AutomaticCheckSettings settings,
        DateTimeOffset? lastCheckedUtc,
        DateTimeOffset nowUtc)
        => NextDueAt(settings, lastCheckedUtc, nowUtc) is { } due && due <= nowUtc;
}
