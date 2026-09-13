using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

/// <summary>
/// What launching DeskAI a second time does. Never a second DeskAI, and never a silent
/// nothing: the one already running is revealed.
/// </summary>
public sealed class SingleInstanceDecisionTests
{
    [Fact]
    public void The_first_launch_starts_normally()
    {
        Assert.Equal(
            LaunchAction.StartNormally,
            SingleInstanceDecision.Decide(anotherIsAlreadyRunning: false));
    }

    [Fact]
    public void A_second_launch_reveals_the_one_already_running_and_exits()
    {
        Assert.Equal(
            LaunchAction.RevealTheRunningOneAndExit,
            SingleInstanceDecision.Decide(anotherIsAlreadyRunning: true));
    }

    [Fact]
    public void There_is_no_outcome_that_starts_a_second_DeskAI_or_quits_the_first()
    {
        // Two DeskAIs would mean two SQLite writers and two timers producing two counts for
        // one state. A launch that silently quit the first would lose what someone was doing.
        Assert.Equal(2, Enum.GetValues<LaunchAction>().Length);
    }
}
