using DeskAI.IconProbe;

namespace DeskAI.FolderColorProbe;

internal readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>Reads the screen capture (BGRA, top row first) for the probe's colour checks.</summary>
internal static class ColorCount
{
    /// <summary>Pixels inside <paramref name="area"/> whose every channel is within <paramref name="tolerance"/>.</summary>
    internal static int Count(ReadOnlySpan<byte> bgra, int width, int height, Rect area, Rgb color, int tolerance)
    {
        var count = 0;
        for (var y = Math.Max(area.Top, 0); y < Math.Min(area.Bottom, height); y++)
        {
            for (var x = Math.Max(area.Left, 0); x < Math.Min(area.Right, width); x++)
            {
                var i = ((y * width) + x) * 4;
                if (Math.Abs(bgra[i] - color.B) <= tolerance
                    && Math.Abs(bgra[i + 1] - color.G) <= tolerance
                    && Math.Abs(bgra[i + 2] - color.R) <= tolerance)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// False when every pixel is the same colour: a black capture from a minimized Sandbox would
    /// otherwise pass "no colour left" (ADR 0046 review).
    /// </summary>
    internal static bool IsReadable(ReadOnlySpan<byte> bgra)
    {
        for (var i = 4; i + 2 < bgra.Length; i += 4)
        {
            if (bgra[i] != bgra[0] || bgra[i + 1] != bgra[1] || bgra[i + 2] != bgra[2])
            {
                return true;
            }
        }

        return false;
    }
}
