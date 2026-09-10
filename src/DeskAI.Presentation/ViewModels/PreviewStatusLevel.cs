namespace DeskAI.App.ViewModels;

/// <summary>
/// How a preview row should be presented to the user.
/// </summary>
/// <remarks>
/// This is presentation severity, never authorization. Safety decides what is blocked;
/// this only decides which icon, colour, and word describe that decision. Colour is
/// always paired with an icon and text so meaning never depends on colour alone.
/// </remarks>
public enum PreviewStatusLevel
{
    Ready,
    Attention,
    Blocked,
}
