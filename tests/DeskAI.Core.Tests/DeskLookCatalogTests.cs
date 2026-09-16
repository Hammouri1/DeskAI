using System.Globalization;
using DeskAI.Core.Appearance;

namespace DeskAI.Core.Tests;

/// <summary>
/// The looks are the one place DeskAI's colours can change at a person's request, so the rule
/// that holds them honest is checked here: a look tints the neutral surfaces only, never the
/// accent, and every look keeps the shared text colours readable.
/// </summary>
public sealed class DeskLookCatalogTests
{
    // The text colours DeskAITheme.xaml uses on every look, dark and light.
    private const string DarkTextPrimary = "#E7EBF0";
    private const string DarkTextSecondary = "#8C97A5";
    private const string LightTextPrimary = "#10161D";
    private const string LightTextSecondary = "#5A6572";

    [Fact]
    public void There_are_four_looks_and_Slate_is_the_default()
    {
        Assert.Equal(["slate", "graphite", "sand", "ocean"], DeskLookCatalog.All.Select(look => look.Id));
        Assert.Equal("Slate", DeskLookCatalog.Default.Name);
        Assert.Equal(DeskLookCatalog.DefaultId, AppearanceSettings.Default.LookId);
        Assert.Equal(ThemeMode.FollowWindows, AppearanceSettings.Default.Mode);
        Assert.Null(DeskLookCatalog.Find("Slate"));
        Assert.Null(DeskLookCatalog.Find("neon"));
    }

    [Fact]
    public void Slate_is_the_palette_DeskAI_has_always_had()
    {
        var slate = DeskLookCatalog.Default;

        Assert.Equal(new LookPalette("#0F1216", "#161B21", "#1D242C", "#272E38", "#38414D"), slate.Dark);
        Assert.Equal(new LookPalette("#F6F7F9", "#FFFFFF", "#FFFFFF", "#E1E5EA", "#C7CED6"), slate.Light);
    }

    [Fact]
    public void A_look_can_change_only_the_neutral_colours_never_the_accent()
    {
        var properties = typeof(LookPalette).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(["Ground", "Surface", "SurfaceRaised", "Line", "LineStrong", "All"], properties);
        Assert.DoesNotContain(properties, name => name.Contains("Accent", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Caution", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Danger", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_colour_is_a_six_digit_hex_colour()
    {
        foreach (var look in DeskLookCatalog.All)
        {
            foreach (var colour in look.Dark.All.Concat(look.Light.All))
            {
                Assert.Matches("^#[0-9A-F]{6}$", colour);
            }
        }
    }

    [Fact]
    public void Every_look_keeps_the_shared_text_readable_on_its_ground_and_surfaces()
    {
        foreach (var look in DeskLookCatalog.All)
        {
            foreach (var background in new[] { look.Dark.Ground, look.Dark.Surface, look.Dark.SurfaceRaised })
            {
                Assert.True(Contrast(DarkTextPrimary, background) >= 7, $"{look.Name} dark: primary text on {background}");
                Assert.True(Contrast(DarkTextSecondary, background) >= 4.5, $"{look.Name} dark: secondary text on {background}");
            }

            foreach (var background in new[] { look.Light.Ground, look.Light.Surface, look.Light.SurfaceRaised })
            {
                Assert.True(Contrast(LightTextPrimary, background) >= 7, $"{look.Name} light: primary text on {background}");
                Assert.True(Contrast(LightTextSecondary, background) >= 4.5, $"{look.Name} light: secondary text on {background}");
            }
        }
    }

    [Fact]
    public void An_unknown_stored_look_falls_back_to_the_default()
    {
        Assert.Equal("Slate", new AppearanceSettings(ThemeMode.Dark, "removed-look").Look.Name);
        Assert.Equal("Ocean", new AppearanceSettings(ThemeMode.Dark, "ocean").Look.Name);
    }

    /// <summary>WCAG contrast ratio between two "#RRGGBB" colours.</summary>
    private static double Contrast(string first, string second)
    {
        var (a, b) = (Luminance(first), Luminance(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
