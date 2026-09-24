namespace DeskAI.IconProbe;

internal static class PositionCheck
{
    /// <summary>
    /// Every expected icon that is missing or more than half a spacing step away. Names are
    /// parsing names (with extensions), compared ignoring case as Windows does.
    /// </summary>
    internal static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, Point> expected, IReadOnlyDictionary<string, Point> actual, Point spacing)
    {
        var byName = actual.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var differences = new List<string>();
        foreach (var (name, want) in expected)
        {
            if (!byName.TryGetValue(name, out var got))
            {
                differences.Add($"{name}: missing");
            }
            else if (Math.Abs(got.X - want.X) > spacing.X / 2 || Math.Abs(got.Y - want.Y) > spacing.Y / 2)
            {
                differences.Add($"{name}: wanted ({want.X}, {want.Y}), found ({got.X}, {got.Y})");
            }
        }

        return differences;
    }
}
