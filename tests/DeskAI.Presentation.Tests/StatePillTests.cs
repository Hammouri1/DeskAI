using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The state pills the redesign added read from the same values the pages already decide on, so
/// a pill can never say the opposite of the text beside it. Each inverse is checked here because
/// the pages bind the plain pill to it directly.
/// </summary>
public sealed class StatePillTests
{
    [Fact]
    public void A_rule_row_is_On_or_Off_never_both()
    {
        var on = new RuleViewModel(Guid.NewGuid(), "Invoices", "Move invoices to Documents", IsEnabled: true);
        var off = on with { IsEnabled = false };

        Assert.Equal("On", on.State);
        Assert.False(on.IsOff);
        Assert.Equal("Off", off.State);
        Assert.True(off.IsOff);
    }

    [Fact]
    public void A_folder_row_says_names_only_until_reading_inside_is_allowed()
    {
        var namesOnly = new ConnectedFolderViewModel(Guid.NewGuid(), "Coursework", "C:\\generated", "3 files remembered", CanReadContent: false);

        Assert.True(namesOnly.IsNamesOnly);
        Assert.False((namesOnly with { CanReadContent = true }).IsNamesOnly);
    }

    [Fact]
    public async Task The_checking_pill_follows_the_pause_switch()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        Assert.False(page.IsPaused);
        Assert.True(page.IsRunning);
        Assert.Equal("Every 15 minutes", page.SelectedFrequency.Label);

        page.IsPaused = true;
        Assert.False(page.IsRunning);
        page.IsPaused = false;
        Assert.True(page.IsRunning);
    }
}
