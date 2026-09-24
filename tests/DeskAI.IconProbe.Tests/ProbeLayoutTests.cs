using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class ProbeLayoutTests
{
    [Fact]
    public void Twelve_targets_form_three_columns_of_four_one_spacing_apart()
    {
        var targets = ProbeLayout.Targets(12, new Point(75, 100), new Rect(0, 0, 1920, 1040));

        Assert.Equal(12, targets.Count);
        Assert.Equal(12, targets.Distinct().Count());
        Assert.Equal(3, targets.Select(t => t.X).Distinct().Count());
        Assert.Equal(4, targets.Select(t => t.Y).Distinct().Count());
        Assert.All(targets.Select(t => t.Y).Distinct().Order().Zip(targets.Select(t => t.Y).Distinct().Order().Skip(1)),
            pair => Assert.Equal(100, pair.Second - pair.First));
    }

    [Fact]
    public void Targets_sit_away_from_the_top_left_where_Windows_puts_new_icons()
    {
        var targets = ProbeLayout.Targets(12, new Point(75, 100), new Rect(0, 0, 1920, 1040));

        Assert.All(targets, t => Assert.True(t.X >= 1920 / 2, $"{t} is on the left half"));
    }

    [Fact]
    public void Targets_stay_inside_a_small_work_area()
    {
        // A 1280 x 720 Sandbox window at 150 % scaling: big spacing, little room.
        var area = new Rect(0, 0, 1280, 720);
        var targets = ProbeLayout.Targets(12, new Point(110, 130), area);

        Assert.All(targets, t =>
        {
            Assert.InRange(t.X, area.Left, area.Right - 110);
            Assert.InRange(t.Y, area.Top, area.Bottom - 130);
        });
    }

    [Fact]
    public void Refuses_when_the_icons_cannot_fit() =>
        Assert.Throws<InvalidOperationException>(() => ProbeLayout.Targets(12, new Point(300, 300), new Rect(0, 0, 600, 600)));
}
