namespace DeskAI.App.Services;

/// <summary>A DeskAI with no icon near the clock. Everything else still works.</summary>
/// <remarks>
/// The default, so that a DeskAI composed without Windows-facing services is a working
/// DeskAI rather than a broken one. It never shows, so the Automatic tasks page never offers
/// to keep running with no window — see <c>CanKeepRunning</c> in the view model.
/// </remarks>
public sealed class NoBackgroundPresence : IBackgroundPresence
{
    public bool IsShowing => false;

    public void Show(string tooltip, bool isPaused)
    {
    }

    public void Update(string tooltip, bool isPaused)
    {
    }

    public void Hide()
    {
    }

#pragma warning disable CS0067 // A presence that never appears never raises anything.
    public event EventHandler? OpenRequested;

    public event EventHandler? PauseToggleRequested;

    public event EventHandler? QuitRequested;
#pragma warning restore CS0067
}
