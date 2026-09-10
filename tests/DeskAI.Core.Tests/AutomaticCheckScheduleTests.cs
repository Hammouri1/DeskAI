using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

/// <summary>
/// Deciding when to check is arithmetic, and this is where that arithmetic is pinned down.
/// The awkward cases — never checked, closed for a week, the clock moved backwards — are
/// answerable here rather than by waiting for a timer.
/// </summary>
public sealed class AutomaticCheckScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsDue_ChecksSoonAfterTheFirstEverStart()
    {
        var settings = AutomaticCheckSettings.Default with { Frequency = AutomaticCheckFrequency.EveryHour };

        Assert.True(AutomaticCheckSchedule.IsDue(settings, lastCheckedUtc: null, Now));
    }

    [Fact]
    public void IsDue_WaitsWhileTheIntervalHasNotElapsed()
    {
        var settings = AutomaticCheckSettings.Default with { Frequency = AutomaticCheckFrequency.EveryHour };

        Assert.False(AutomaticCheckSchedule.IsDue(settings, Now.AddMinutes(-59), Now));
    }

    [Fact]
    public void IsDue_ChecksOnceTheIntervalHasElapsed()
    {
        var settings = AutomaticCheckSettings.Default with { Frequency = AutomaticCheckFrequency.EveryHour };

        Assert.True(AutomaticCheckSchedule.IsDue(settings, Now.AddHours(-1), Now));
    }

    /// <summary>
    /// A check re-reads current state, so it has no per-occurrence meaning. An app closed
    /// for a week owes one check, not a week of them — which is why being overdue is a
    /// boolean here and never a backlog.
    /// </summary>
    [Fact]
    public void IsDue_OwesOneCheckAfterALongClosure()
    {
        var settings = AutomaticCheckSettings.Default with { Frequency = AutomaticCheckFrequency.EveryHour };

        Assert.True(AutomaticCheckSchedule.IsDue(settings, Now.AddDays(-7), Now));
        Assert.Equal(Now, AutomaticCheckSchedule.NextDueAt(settings, Now.AddDays(-7), Now));
    }

    /// <summary>
    /// A clock moved backwards — a timezone fix, a wrong system clock corrected — would
    /// otherwise leave the last check sitting in the future and stall checking until the
    /// clock caught up. Being unable to explain the gap is treated as a reason to check.
    /// </summary>
    [Fact]
    public void IsDue_ChecksWhenTheLastCheckIsInTheFuture()
    {
        var settings = AutomaticCheckSettings.Default with { Frequency = AutomaticCheckFrequency.EveryHour };

        Assert.True(AutomaticCheckSchedule.IsDue(settings, Now.AddHours(3), Now));
    }

    [Fact]
    public void IsDue_RefusesWhilePaused()
    {
        var settings = AutomaticCheckSettings.Default with
        {
            Frequency = AutomaticCheckFrequency.EveryHour,
            IsPaused = true,
        };

        Assert.False(AutomaticCheckSchedule.IsDue(settings, Now.AddDays(-7), Now));
        Assert.Null(AutomaticCheckSchedule.NextDueAt(settings, Now.AddDays(-7), Now));
    }

    /// <summary>
    /// "Only when I ask" has to mean it. Someone who chose it should never find that DeskAI
    /// checked anyway because enough time had passed.
    /// </summary>
    [Fact]
    public void IsDue_NeverChecksOnItsOwnWhenAskedNotTo()
    {
        var settings = AutomaticCheckSettings.Default with { Frequency = AutomaticCheckFrequency.OnlyWhenIAsk };

        Assert.False(AutomaticCheckSchedule.IsDue(settings, lastCheckedUtc: null, Now));
        Assert.False(AutomaticCheckSchedule.IsDue(settings, Now.AddYears(-1), Now));
        Assert.Null(AutomaticCheckSchedule.NextDueAt(settings, Now.AddYears(-1), Now));
    }

    [Fact]
    public void NextDueAt_ReportsWhenTheNextCheckFalls()
    {
        var settings = AutomaticCheckSettings.Default with { Frequency = AutomaticCheckFrequency.EveryFifteenMinutes };

        Assert.Equal(Now.AddMinutes(10), AutomaticCheckSchedule.NextDueAt(settings, Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void Frequencies_AreOrderedAndPositive()
    {
        var intervals = new[]
        {
            AutomaticCheckFrequency.EveryFifteenMinutes,
            AutomaticCheckFrequency.EveryHour,
            AutomaticCheckFrequency.ACoupleOfTimesADay,
        }.Select(AutomaticCheckSchedule.IntervalFor).ToArray();

        Assert.All(intervals, interval => Assert.True(interval > TimeSpan.Zero));
        Assert.Equal(intervals.OrderBy(interval => interval), intervals);
    }

    [Fact]
    public void IntervalFor_HasNoIntervalWhenNothingIsAutomatic()
    {
        Assert.Null(AutomaticCheckSchedule.IntervalFor(AutomaticCheckFrequency.OnlyWhenIAsk));
    }
}
