namespace DeskAI.Core.Rules;

/// <summary>What someone is asked before DeskAI may keep running with no window.</summary>
/// <remarks>
/// A record rather than a dialog, so the words are testable without a window and the page
/// test can assert what a person was actually told. See ADR 0025.
/// </remarks>
public sealed record BackgroundCheckingQuestion(
    string Title,
    string Body,
    string LimitLine,
    string NotifyLabel,
    string NotifyCaption,
    bool NotifyWhenSomethingIsFound,
    string Confirm,
    string Decline);

/// <summary>
/// The words DeskAI uses about running with no window, derived from what is stored.
/// </summary>
/// <remarks>
/// <para>
/// Every sentence here is a promise about behaviour a person cannot see happening. It is
/// computed from the settings rather than written as a fixed string for the same reason the
/// navigation pane's scope label is: the one label that says what DeskAI is doing is the
/// label that must never be able to lie.
/// </para>
/// <para>
/// Pure by design. No I/O, no clock, no UI — so every promise in this feature can be
/// asserted in a unit test rather than read off a screen.
/// </para>
/// </remarks>
public static class BackgroundCheckingChoice
{
    /// <summary>The dialog shown before the mode is turned on. Asking, not announcing.</summary>
    public static BackgroundCheckingQuestion Ask(AutomaticCheckSettings settings) => new(
        Title: "Keep DeskAI running after you close the window?",
        Body: "DeskAI will stay near the clock and keep looking at the folders you connected. "
            + "It will not add itself to Windows startup — after you restart or sign out, it only "
            + "runs again when you open it.",
        LimitLine: "A check can tell you how many files your rules match. It cannot move, rename, "
            + "or delete anything. So leaving DeskAI on keeps that number up to date; it does not "
            + "tidy while you are away.",
        NotifyLabel: "Tell me with a Windows notification when something is found",
        NotifyCaption: "With this off, you'll see what it found the next time you open DeskAI.",
        NotifyWhenSomethingIsFound: settings.NotifyWhenSomethingIsFound,
        Confirm: "Keep running",
        Decline: "No thanks");

    /// <summary>
    /// What the icon near the clock says on hover.
    /// </summary>
    /// <remarks>
    /// A count and a state, never a file name, folder name, or path — the rule notifications
    /// already follow, for the same reason: this is shown to whoever is at the machine, which
    /// is not somewhere a person chose to show anyone their filenames.
    /// </remarks>
    public static string Tooltip(AutomaticCheckSettings settings, int? filesToReview)
    {
        if (settings.IsPaused)
        {
            // Deliberately before the count. A paused DeskAI is no longer keeping that
            // number current, so showing it would be showing something stale as if it were
            // being watched.
            return "DeskAI — checks paused";
        }

        if (settings.Frequency == AutomaticCheckFrequency.OnlyWhenIAsk)
        {
            return "DeskAI — only looks when you ask";
        }

        if (filesToReview is > 0 and var count)
        {
            return count == 1 ? "DeskAI — 1 file to review" : $"DeskAI — {count} files to review";
        }

        return settings.Frequency switch
        {
            AutomaticCheckFrequency.EveryFifteenMinutes => "DeskAI — looking every 15 minutes",
            AutomaticCheckFrequency.EveryHour => "DeskAI — looking every hour",
            _ => "DeskAI — looking a few times a day",
        };
    }

    /// <summary>
    /// The "More details" paragraph on the Automatic tasks page.
    /// </summary>
    /// <remarks>
    /// It used to be a fixed string opening "Checking happens only while DeskAI is open".
    /// That sentence is false in <see cref="AutomaticCheckMode.InBackground"/>, so the clause
    /// is derived. The startup sentence and the moves-nothing sentence stay in both, because
    /// they are true in both.
    /// </remarks>
    public static string MoreDetails(AutomaticCheckMode mode)
    {
        var opening = mode == AutomaticCheckMode.InBackground
            ? "Checking carries on after you close the window, until you quit DeskAI from the icon "
                + "near the clock, sign out, or restart."
            : "Checking happens only while DeskAI is open. Closing it stops everything.";

        return opening
            + " DeskAI does not add itself to Windows startup. A check re-reads the names, sizes, "
            + "and dates of files in the folders you connected — it does not open them, and it "
            + "does not move anything.";
    }
}
