using DeskAI.FolderColorProbe;

namespace DeskAI.FolderColorProbe.Tests;

public sealed class ColorProbeReportTests
{
    [Fact]
    public void Reliable_when_every_required_stage_passed()
    {
        var report = Passing();
        report.Add(new Stage("icon file missing", StageOutcome.Failed, "shows a blank icon"));

        Assert.True(report.IsReliable);
        Assert.Contains("VERDICT: reliable", report.Render(), StringComparison.Ordinal);
        Assert.Contains("icon file missing: Failed - shows a blank icon", report.Render(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("color")]
    [InlineData("refresh")]
    [InlineData("explorer restart")]
    [InlineData("put back")]
    [InlineData("put back after refresh")]
    public void Any_failed_required_stage_is_not_reliable(string stage)
    {
        var report = Passing(except: stage);
        report.Add(new Stage(stage, StageOutcome.Failed, "no"));

        Assert.False(report.IsReliable);
        Assert.Contains("VERDICT: not reliable", report.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_skipped_pixel_stage_is_not_reliable()
    {
        var report = Passing(except: "put back after refresh");
        report.Add(new Stage("put back after refresh", StageOutcome.Skipped, "the screen could not be read"));

        Assert.False(report.IsReliable);
    }

    [Fact]
    public void A_missing_stage_is_not_reliable() =>
        Assert.False(Passing(except: "explorer restart").IsReliable);

    [Fact]
    public void Notes_appear_in_the_report()
    {
        var report = Passing();
        report.Note("dpi: 144");

        Assert.Contains("dpi: 144", report.Render(), StringComparison.Ordinal);
    }

    private static ColorProbeReport Passing(string? except = null)
    {
        var report = new ColorProbeReport();
        foreach (var name in new[] { "color", "refresh", "explorer restart", "put back", "put back after refresh" })
        {
            if (name != except)
            {
                report.Add(new Stage(name, StageOutcome.Passed, ""));
            }
        }

        return report;
    }
}
