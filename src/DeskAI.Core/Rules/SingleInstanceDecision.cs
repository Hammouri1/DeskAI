namespace DeskAI.Core.Rules;

/// <summary>What a launch of DeskAI should do.</summary>
public enum LaunchAction
{
    /// <summary>No other DeskAI is running in this sign-in session. Start.</summary>
    StartNormally = 0,

    /// <summary>
    /// One is already running, possibly with no window. Show it and exit successfully.
    /// </summary>
    RevealTheRunningOneAndExit = 1,
}

/// <summary>
/// Whether this launch is the real DeskAI or a request to reveal the one already running.
/// </summary>
/// <remarks>
/// <para>
/// Once DeskAI can run with no window, launching it again is the obvious thing a person does
/// when they want it back. Starting a second one would mean two SQLite writers against one
/// database and two timers producing two counts for one state; silently doing nothing would
/// look broken. So the running one is revealed and this launch ends.
/// </para>
/// <para>
/// Kept apart from the mutex that answers the question, so the rule is a unit test rather
/// than something only two real processes could demonstrate. See ADR 0025.
/// </para>
/// </remarks>
public static class SingleInstanceDecision
{
    public static LaunchAction Decide(bool anotherIsAlreadyRunning) => anotherIsAlreadyRunning
        ? LaunchAction.RevealTheRunningOneAndExit
        : LaunchAction.StartNormally;
}
