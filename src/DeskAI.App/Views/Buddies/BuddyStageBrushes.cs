using DeskAI.App.Services;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DeskAI.App.Views.Buddies;

/// <summary>Each buddy's own background on the stage and its face, from the chosen mockup (chooser B).</summary>
internal static class BuddyStageBrushes
{
    public static Brush For(string stage)
    {
        var (centre, edge) = stage switch
        {
            "study" => ("#4A3524", "#1C130C"),
            "lab" => ("#1D2C46", "#0B111D"),
            "forest" => ("#2D4A2A", "#101C10"),
            "sea" => ("#1B3F6E", "#081A33"),
            "meadow" => ("#1D5A4B", "#0A2019"),
            "dusk" => ("#3A2D5C", "#140F24"),
            _ => ("#16405C", "#0A1726"),
        };

        return new RadialGradientBrush
        {
            Center = new Point(0.5, 0.55),
            GradientOrigin = new Point(0.5, 0.55),
            RadiusX = 0.75,
            RadiusY = 0.75,
            GradientStops =
            {
                new GradientStop { Color = WindowsAppearanceApplier.ToColor(centre), Offset = 0 },
                new GradientStop { Color = WindowsAppearanceApplier.ToColor(edge), Offset = 0.75 },
            },
        };
    }
}
