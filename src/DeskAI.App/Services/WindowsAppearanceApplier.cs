using System.Globalization;
using DeskAI.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DeskAI.App.Services;

/// <summary>
/// Paints DeskAI's window in a look, and sets whether it follows Windows, is light, or is dark.
/// </summary>
/// <remarks>
/// <para>
/// The theme dictionaries in <c>DeskAITheme.xaml</c> hold one brush per token per theme. A look
/// is applied by changing the colour of those brush objects in place, for both the dark and the
/// light dictionary, so every page that uses <c>{ThemeResource DeskGroundBrush}</c> repaints
/// without being told and the next theme switch finds the look already there. High contrast maps
/// every token to a Windows system colour and is left alone.
/// </para>
/// <para>
/// Only the neutral tokens are touched. The accent, caution, and danger brushes are never
/// changed by a look, because the accent means "safe or confirmed" on every screen.
/// </para>
/// </remarks>
public sealed class WindowsAppearanceApplier : IAppearanceApplier
{
    public void Apply(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var look = settings.Look;
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            Paint(dictionary, "Default", look.Dark);
            Paint(dictionary, "Light", look.Light);
        }

        if (((App)Application.Current).MainAppWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = settings.Mode switch
            {
                ThemeMode.Light => ElementTheme.Light,
                ThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    private static void Paint(ResourceDictionary dictionary, string themeKey, LookPalette palette)
    {
        if (!dictionary.ThemeDictionaries.TryGetValue(themeKey, out var found) || found is not ResourceDictionary theme)
        {
            return;
        }

        Set(theme, "DeskGroundBrush", palette.Ground);
        Set(theme, "DeskSurfaceBrush", palette.Surface);
        Set(theme, "DeskSurfaceRaisedBrush", palette.SurfaceRaised);
        Set(theme, "DeskLineBrush", palette.Line);
        Set(theme, "DeskLineStrongBrush", palette.LineStrong);
        Set(theme, "CardBackgroundFillColorDefaultBrush", palette.Surface);
        Set(theme, "CardBackgroundFillColorSecondaryBrush", palette.Ground);
        Set(theme, "CardStrokeColorDefaultBrush", palette.Line);
        Set(theme, "AccentButtonBackgroundDisabled", palette.Line);
    }

    private static void Set(ResourceDictionary theme, string key, string hex)
    {
        if (theme.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = ToColor(hex);
        }
    }

    /// <summary>"#RRGGBB" to a colour. The catalog test guarantees the form.</summary>
    internal static Color ToColor(string hex)
    {
        var digits = hex.TrimStart('#');
        return Color.FromArgb(
            255,
            byte.Parse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
