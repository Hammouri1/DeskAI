using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class PositionCheckTests
{
    private static readonly Point Spacing = new(76, 100);

    [Fact]
    public void Positions_within_half_a_step_match()
    {
        var expected = new Dictionary<string, Point> { ["probe-01.txt"] = new(1000, 200) };
        var actual = new Dictionary<string, Point> { ["probe-01.txt"] = new(1037, 249) };

        Assert.Empty(PositionCheck.Differences(expected, actual, Spacing));
    }

    [Fact]
    public void A_moved_or_missing_icon_is_named()
    {
        var expected = new Dictionary<string, Point> { ["probe-01.txt"] = new(1000, 200), ["probe-02.txt"] = new(1000, 300) };
        var actual = new Dictionary<string, Point> { ["probe-01.txt"] = new(20, 20) };

        var differences = PositionCheck.Differences(expected, actual, Spacing);

        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.Contains("probe-01.txt", StringComparison.Ordinal) && d.Contains("(20, 20)", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.Contains("probe-02.txt", StringComparison.Ordinal) && d.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Matches_by_full_name_not_by_display_name()
    {
        var expected = new Dictionary<string, Point> { ["probe-01.txt"] = new(1000, 200) };

        // Windows ignores case, so a different case is the same icon...
        Assert.Empty(PositionCheck.Differences(expected, new Dictionary<string, Point> { ["PROBE-01.TXT"] = new(1000, 200) }, Spacing));
        // ...but a name without its extension is a different icon, reported as missing.
        Assert.Single(PositionCheck.Differences(expected, new Dictionary<string, Point> { ["probe-01"] = new(1000, 200) }, Spacing));
    }
}
