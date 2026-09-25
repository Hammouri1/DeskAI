namespace DeskAI.App.Services;

/// <summary>
/// DeskAI's icon near the clock, while it is running with no window.
/// </summary>
/// <remarks>
/// <para>
/// Behind an interface because the notification area is a platform detail, and because a
/// presence that quietly does nothing is a legitimate implementation — the same reasoning as
/// <see cref="IFindingNotifier"/>. It also means everything a person can see or do here is a
/// page test rather than something only a screenshot could check.
/// </para>
/// <para>
/// It opens things and stops things. It starts nothing: there is deliberately no
/// "check now" here, because that would read folder metadata with no window on screen and no
/// page reporting the result. Every control that begins work stays on a page someone opened.
/// See ADR 0025.
/// </para>
/// <para>
/// The tooltip is handed in rather than composed here. What DeskAI may claim about itself is
/// decided in <see cref="DeskAI.Core.Rules.BackgroundCheckingChoice"/> and asserted there; an
/// implementation of this interface holds no such decision, and carries a count and a state
/// but never a file name, folder name, or path.
/// </para>
/// </remarks>
public interface IBackgroundPresence
{
    /// <summary>Whether the icon is on screen right now.</summary>
    bool IsShowing { get; }

    /// <summary>Shows the icon. Does nothing when it is already showing.</summary>
    void Show(string tooltip, PresenceMenu menu);

    /// <summary>
    /// Changes what the icon says on hover, whether its menu offers Pause checking and Find a
    /// file, and whether it shows checking as paused.
    /// Does nothing when not showing.
    /// </summary>
    /// <remarks>
    /// The two travel together deliberately. An icon whose tooltip says it is looking every
    /// 15 minutes while its menu shows a tick beside "Pause checking" is the failure this
    /// whole feature is careful about, and separate calls are how that happens.
    /// </remarks>
    void Update(string tooltip, PresenceMenu menu);

    /// <summary>Takes the icon away. Does nothing when it is not showing.</summary>
    void Hide();

    /// <summary>Someone asked for the DeskAI window back.</summary>
    event EventHandler? OpenRequested;

    /// <summary>Someone asked to pause or resume checking.</summary>
    event EventHandler? PauseToggleRequested;

    /// <summary>Someone asked DeskAI to stop altogether.</summary>
    event EventHandler? QuitRequested;

    /// <summary>Someone asked for the quick search bar (ADR 0047).</summary>
    event EventHandler? FindRequested;
}

/// <summary>What the icon's menu offers. Travels with the tooltip so the two never disagree.</summary>
/// <param name="OffersPause">Whether "Pause checking" is in the menu (only while checking in the background).</param>
/// <param name="IsPaused">Whether that item shows checking as paused.</param>
/// <param name="OffersFind">Whether "Find a file" is in the menu (only while quick search is on, ADR 0047).</param>
public sealed record PresenceMenu(bool OffersPause, bool IsPaused, bool OffersFind);
