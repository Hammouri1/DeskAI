using DeskAI.App.ViewModels;
using DeskAI.Core.Rules;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Home page and the reminder at the bottom of the side menu, which must always say
/// what DeskAI can actually see.
/// </summary>
public sealed class HomeAndShellTests
{
    private static readonly string[] Greetings = ["Good morning", "Good afternoon", "Good evening"];

    [Fact]
    public async Task With_nothing_connected_Home_and_the_side_menu_say_so()
    {
        await using var app = await TestApp.StartAsync();
        var home = app.Get<DashboardViewModel>();
        var shell = app.Get<ShellViewModel>();

        await home.InitializeAsync();
        await shell.RefreshAsync();

        Assert.Equal("Nothing connected yet", home.HeroState);
        Assert.False(home.HasStorage);
        Assert.False(home.HasHealth);
        Assert.Equal("Nothing connected yet", shell.ScopeTitle);
        Assert.Contains("No folders connected", shell.ScopeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_greets_by_the_time_of_day_and_the_four_tiles_read_the_remembered_numbers()
    {
        Assert.Equal("Good morning", DashboardViewModel.GreetingFor(new DateTimeOffset(2026, 9, 16, 7, 30, 0, TimeSpan.Zero)));
        Assert.Equal("Good afternoon", DashboardViewModel.GreetingFor(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Good evening", DashboardViewModel.GreetingFor(new DateTimeOffset(2026, 9, 16, 18, 0, 0, TimeSpan.Zero)));

        await using var app = await TestApp.StartAsync();
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        Assert.Contains(home.Greeting, Greetings);
        Assert.Equal("0", home.FoldersConnected);
        Assert.Equal("0", home.TotalFiles);
        Assert.Equal("0", home.OldFilesHeadline);
        Assert.Equal("0", home.DuplicateHeadline);
        Assert.Equal("Connect a folder to look", home.DuplicateTileCaption);

        var folder = app.MakeFolder("Coursework", "notes.txt");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "report.pdf"), new string('a', 6000));
        app.Directory.CreateDummyFile(Path.Combine("folders", "Coursework", "report copy.pdf"), new string('b', 6000));
        await ConnectAsync(app, folder);
        await home.InitializeAsync();

        Assert.Equal("1", home.FoldersConnected);
        Assert.Equal("3", home.TotalFiles);
        Assert.Equal("2", home.DuplicateHeadline);
        Assert.Equal("Same size, not compared yet", home.DuplicateTileCaption);
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
    public async Task Home_says_files_move_only_where_tidying_is_allowed_and_only_when_you_press_Tidy()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, app.MakeFolder("Coursework", "notes.txt"));
        var home = app.Get<DashboardViewModel>();

        await home.InitializeAsync();

        Assert.DoesNotContain("without showing you first", home.HeroMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot move", home.HeroMessage, StringComparison.Ordinal);
        Assert.Contains("only in a folder you allowed it to tidy", home.HeroMessage, StringComparison.Ordinal);
        Assert.Contains("only when you press Tidy", home.HeroMessage, StringComparison.Ordinal);
        Assert.Contains("never deletes anything", home.HeroMessage, StringComparison.Ordinal);
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
    public async Task The_side_menu_says_which_folders_DeskAI_may_tidy_and_stops_when_that_is_taken_back()
    {
        await using var app = await TestApp.StartAsync();
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, app.MakeFolder("Downloads", "invoice.pdf"));
        var shell = app.Get<ShellViewModel>();

        await shell.RefreshAsync();
        Assert.Contains("let it tidy 1 folder when you press Tidy", shell.ScopeMessage, StringComparison.Ordinal);

        await app.Get<DeskAI.Core.Tidy.TidyPermissionService>().StopAsync(rootId, TestContext.Current.CancellationToken);
        await shell.RefreshAsync();
        Assert.DoesNotContain("tidy", shell.ScopeMessage, StringComparison.OrdinalIgnoreCase);
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
