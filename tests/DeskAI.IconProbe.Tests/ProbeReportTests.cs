using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class ProbeReportTests
{
    [Fact]
    public void Reliable_when_place_refresh_and_put_back_pass_even_if_restart_loses_positions()
    {
        var report = Passing();
        report.Add(new Stage("explorer restart", StageOutcome.Failed, "3 icons moved"));

        Assert.True(report.IsReliable);
        Assert.Contains("VERDICT: reliable", report.Render(), StringComparison.Ordinal);
        Assert.Contains("explorer restart: Failed - 3 icons moved", report.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_put_back_makes_the_verdict_not_reliable()
    {
        var report = new ProbeReport();
        report.Add(new Stage("place", StageOutcome.Passed, ""));
        report.Add(new Stage("refresh", StageOutcome.Passed, ""));
        report.Add(new Stage("put back", StageOutcome.Failed, "auto arrange still off"));

        Assert.False(report.IsReliable);
        Assert.Contains("VERDICT: not reliable", report.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_stage_is_not_reliable()
    {
        var report = new ProbeReport();
        report.Add(new Stage("place", StageOutcome.Passed, ""));

        Assert.False(report.IsReliable);
    }

    [Fact]
    public void Notes_appear_in_the_report()
    {
        var report = Passing();
        report.Note("dpi: 144");

        Assert.Contains("dpi: 144", report.Render(), StringComparison.Ordinal);
    }

    private static ProbeReport Passing()
    {
        var report = new ProbeReport();
        report.Add(new Stage("place", StageOutcome.Passed, ""));
        report.Add(new Stage("refresh", StageOutcome.Passed, ""));
        report.Add(new Stage("put back", StageOutcome.Passed, ""));
        return report;
    }
}
