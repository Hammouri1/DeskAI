namespace DeskAI.App.Services;

/// <summary>
/// Whether buddies move: DeskAI's own "Let my buddy move" switch, which wins over Windows'
/// Animation effects (the owner's choice, 2026-09-25).
/// </summary>
/// <remarks>
/// One singleton, so the bar, the chooser's stage, and the welcome's Sparky follow the same
/// switch, and a buddy already on screen stops or starts the moment the switch changes. Only
/// <see cref="QuickSearchSwitch"/> sets it.
/// </remarks>
public sealed class BuddyMotion
{
    public bool IsOn { get; private set; } = true;

    /// <summary>Raised on the thread that changed it; a buddy moves back to its own thread before redrawing.</summary>
    public event EventHandler? Changed;

    public void Set(bool isOn)
    {
        if (IsOn == isOn)
        {
            return;
        }

        IsOn = isOn;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
