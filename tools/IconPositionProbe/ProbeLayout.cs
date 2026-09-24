namespace DeskAI.IconProbe;

/// <summary>Where the probe puts its icons: columns of four on the right half of the work area.</summary>
/// <remarks>Windows drops new icons at the top left, so targets on the right half cannot pass by accident.</remarks>
internal static class ProbeLayout
{
    private const int PerColumn = 4;

    internal static IReadOnlyList<Point> Targets(int count, Point spacing, Rect workArea)
    {
        var columns = (count + PerColumn - 1) / PerColumn;
        var width = columns * spacing.X;
        var height = Math.Min(count, PerColumn) * spacing.Y;
        var left = workArea.Right - width - spacing.X;
        var top = workArea.Top + spacing.Y;
        var middle = workArea.Left + ((workArea.Right - workArea.Left) / 2);
        if (left < middle || top + height > workArea.Bottom)
        {
            throw new InvalidOperationException("The probe's icons do not fit on the right half of this screen.");
        }

        return Enumerable.Range(0, count)
            .Select(i => new Point(left + (i / PerColumn * spacing.X), top + (i % PerColumn * spacing.Y)))
            .ToList();
    }
}
