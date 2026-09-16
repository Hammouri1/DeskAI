using DeskAI.Core.Appearance;

namespace DeskAI.App.Services;

/// <summary>
/// Repaints DeskAI's own window in a chosen look and theme.
/// </summary>
/// <remarks>
/// It changes DeskAI's window and nothing else: no Windows theme, wallpaper, or setting. The
/// Windows one lives in the app; this project cannot see WinUI, and the page tests get one that
/// only records what it was asked.
/// </remarks>
public interface IAppearanceApplier
{
    void Apply(AppearanceSettings settings);
}

/// <summary>A DeskAI with no window to paint, such as the page tests.</summary>
public sealed class NoAppearanceApplier : IAppearanceApplier
{
    public void Apply(AppearanceSettings settings)
    {
    }
}
