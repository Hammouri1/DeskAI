namespace DeskAI.App.Services;

/// <summary>
/// Tells someone, outside the app, that a check found something.
/// </summary>
/// <remarks>
/// <para>
/// Behind an interface because a Windows notification is a platform detail and because a
/// notifier that quietly does nothing is a legitimate implementation. DeskAI runs
/// unpackaged, and notification support is something to be asked for rather than assumed:
/// if it is unavailable, the in-app notice is the whole behaviour and nothing pretends
/// otherwise.
/// </para>
/// <para>
/// A notification carries a count and never a file name. What matched is on a screen the
/// person chooses to open, not on their lock screen or in a notification centre that other
/// people can see over their shoulder.
/// </para>
/// </remarks>
public interface IFindingNotifier
{
    /// <summary>Whether notifications can actually be shown on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Shows a notification. Does nothing when unavailable.</summary>
    void Notify(string title, string message);
}
