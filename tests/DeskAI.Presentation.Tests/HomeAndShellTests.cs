using DeskAI.App.ViewModels;
using DeskAI.Core.Rules;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Home page and the reminder at the bottom of the side menu, which must always say
/// what DeskAI can actually see.
/// </summary>
public sealed class HomeAndShellTests
{
    [Fact]
    public async Task With_nothing_connected_Home_and_the_side_menu_say_so()
    {
        await using var app = await TestApp.StartAsync();
        var home = app.Get<DashboardViewModel>();
        var shell = app.Get<ShellViewModel>();

        await home.InitializeAsync();
        await shell.RefreshAsync();

        Assert.Equal("Practice mode", home.HeroState);
        Assert.False(home.HasStorage);
        Assert.False(home.HasHealth);
        Assert.Equal("Practice mode", shell.ScopeTitle);
        Assert.Contains("No folders connected", shell.ScopeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_connecting_Home_shows_totals_categories_and_largest_files()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "big.pdf"), new string('x', 5000));
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "small.jpg"), "tiny");
        await ConnectAsync(app, folder);
        var home = app.Get<DashboardViewModel>();

        await home.InitializeAsync();

        Assert.Equal("1 folder connected", home.HeroState);
        Assert.Equal("2", home.TotalFiles);
        Assert.True(home.HasStorage);
        Assert.Equal("big.pdf", home.LargestFiles[0].Name);
        Assert.Contains(home.Categories, row => row.Category == "Documents");
        Assert.True(home.HasLastChecked);
    }

    [Fact]
    public async Task Home_never_suggests_a_connected_folder_can_be_changed()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, app.MakeFolder("Coursework", "notes.txt"));
        var home = app.Get<DashboardViewModel>();

        await home.InitializeAsync();

        Assert.DoesNotContain("without showing you first", home.HeroMessage, StringComparison.Ordinal);
        Assert.Contains("cannot move, rename, or delete", home.HeroMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_of_the_same_size_are_shown_as_possible_copies_never_as_confirmed()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework");
        // Above the 4 KB floor below which sizes collide too often to mean anything.
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "report.pdf"), new string('a', 6000));
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "report copy.pdf"), new string('b', 6000));
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "tiny-1.txt"), "same");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "tiny-2.txt"), "size");
        await ConnectAsync(app, folder);
        var home = app.Get<DashboardViewModel>();

        await home.InitializeAsync();

        Assert.True(home.HasDuplicates);
        Assert.Single(home.DuplicateGroups);
        Assert.Contains("not confirmed", home.DuplicateDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_health_score_is_shown_with_the_parts_that_made_it()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, app.MakeFolder("Coursework", "notes.txt", "essay.docx"));
        var home = app.Get<DashboardViewModel>();

        await home.InitializeAsync();

        Assert.True(home.HasHealth);
        Assert.True(int.TryParse(home.HealthScore, out var score));
        Assert.InRange(score, 0, 100);
        Assert.Equal(2, home.HealthComponents.Count);
    }

    [Fact]
    public async Task The_side_menu_counts_connected_folders_and_says_when_reading_inside_is_allowed()
    {
        await using var app = await TestApp.StartAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(app.MakeFolder("Coursework", "notes.txt", "essay.docx"));
        var shell = app.Get<ShellViewModel>();

        await shell.RefreshAsync();
        Assert.Equal("1 folder connected", shell.ScopeTitle);
        Assert.Contains("2 file(s)", shell.ScopeMessage, StringComparison.Ordinal);
        Assert.Contains("has not opened any", shell.ScopeMessage, StringComparison.Ordinal);

        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        await shell.RefreshAsync();
        Assert.Contains("let it read inside 1 folder", shell.ScopeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_that_finds_something_shows_a_notice_and_notifies_only_when_asked()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, app.MakeFolder("Inbox", "invoice-march.pdf"));
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        automation.NewRuleName = "Tidy invoices";
        automation.NewRuleNameContains = "invoice";
        automation.NewRuleDestination = "Sorted";
        await automation.AddRuleCommand.ExecuteAsync(null);
        var shell = app.Get<ShellViewModel>();

        await automation.CheckNowCommand.ExecuteAsync(null);

        Assert.True(shell.HasFinding);
        Assert.Contains("1 file matches your rules", shell.FindingMessage, StringComparison.Ordinal);
        Assert.Contains("Nothing has moved", shell.FindingMessage, StringComparison.Ordinal);
        Assert.Empty(app.Notifier.Messages);

        automation.NotifyWhenSomethingIsFound = true;
        await WaitUntilAsync(async () =>
            (await app.Get<DeskAI.Core.Abstractions.IAutomaticCheckSettingsRepository>()
                .LoadAsync(TestContext.Current.CancellationToken)).NotifyWhenSomethingIsFound);
        await automation.CheckNowCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => Task.FromResult(app.Notifier.Messages.Count > 0));

        var notification = Assert.Single(app.Notifier.Messages);
        Assert.DoesNotContain("invoice", notification, StringComparison.OrdinalIgnoreCase);

        shell.DismissFindingCommand.Execute(null);
        Assert.False(shell.HasFinding);
        shell.Dispose();
    }

    private static async Task ConnectAsync(TestApp app, string folder)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        Assert.Single(search.Folders);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The expected state was never reached.");
    }
}
