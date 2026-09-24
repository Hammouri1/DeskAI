using DeskAI.FolderColorProbe;
using DeskAI.IconProbe;

namespace DeskAI.FolderColorProbe.Tests;

public sealed class ColorCountTests
{
    private static readonly Rgb Magenta = new(230, 0, 230);

    [Fact]
    public void Counts_pixels_within_the_tolerance()
    {
        var screen = new Screen(4, 1);
        screen.Set(0, 0, new Rgb(230, 0, 230));
        screen.Set(1, 0, new Rgb(200, 30, 250));
        screen.Set(2, 0, new Rgb(180, 0, 230));
        screen.Set(3, 0, new Rgb(0, 0, 0));

        Assert.Equal(2, ColorCount.Count(screen.Bgra, 4, 1, new Rect(0, 0, 4, 1), Magenta, tolerance: 40));
    }

    [Fact]
    public void Counts_only_inside_the_rectangle()
    {
        var screen = new Screen(3, 3);
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                screen.Set(x, y, Magenta);
            }
        }

        Assert.Equal(4, ColorCount.Count(screen.Bgra, 3, 3, new Rect(1, 1, 3, 3), Magenta, 40));
    }

    [Fact]
    public void A_rectangle_partly_off_the_screen_is_clipped()
    {
        var screen = new Screen(2, 2);
        screen.Set(1, 1, Magenta);

        Assert.Equal(1, ColorCount.Count(screen.Bgra, 2, 2, new Rect(-5, -5, 10, 10), Magenta, 40));
    }

    [Fact]
    public void An_all_one_colour_capture_is_unreadable()
    {
        Assert.False(ColorCount.IsReadable(new Screen(3, 3).Bgra));

        var screen = new Screen(3, 3);
        screen.Set(2, 2, Magenta);
        Assert.True(ColorCount.IsReadable(screen.Bgra));
    }

    [Fact]
    public void The_same_area_has_no_difference()
    {
        var screen = new Screen(2, 2);
        screen.Set(0, 0, Magenta);

        Assert.Equal(0, ColorCount.MeanDifference(screen.Bgra, new Point(0, 0), screen.Bgra, new Point(0, 0), 2, 2, new Point(2, 2)));
    }

    [Fact]
    public void A_covered_icon_differs_from_the_start()
    {
        var start = new Screen(2, 1);
        start.Set(0, 0, new Rgb(255, 200, 80));
        start.Set(1, 0, new Rgb(255, 200, 80));
        var covered = new Screen(2, 1);
        covered.Set(0, 0, new Rgb(255, 255, 255));
        covered.Set(1, 0, new Rgb(255, 255, 255));

        // Per pixel: (0 + 55 + 175) / 3; the same for both pixels.
        Assert.Equal(230 / 3.0, ColorCount.MeanDifference(start.Bgra, new Point(0, 0), covered.Bgra, new Point(0, 0), 2, 1, new Point(2, 1)), 3);
    }

    [Fact]
    public void Compares_each_area_at_its_own_position()
    {
        var start = new Screen(3, 1);
        start.Set(0, 0, Magenta);
        var later = new Screen(3, 1);
        later.Set(2, 0, Magenta);

        Assert.Equal(0, ColorCount.MeanDifference(start.Bgra, new Point(0, 0), later.Bgra, new Point(2, 0), 3, 1, new Point(1, 1)));
    }

    private sealed class Screen(int width, int height)
    {
        internal byte[] Bgra { get; } = new byte[width * height * 4];

        internal void Set(int x, int y, Rgb color)
        {
            var i = ((y * width) + x) * 4;
            Bgra[i] = color.B;
            Bgra[i + 1] = color.G;
            Bgra[i + 2] = color.R;
            Bgra[i + 3] = 255;
        }
    }
}
