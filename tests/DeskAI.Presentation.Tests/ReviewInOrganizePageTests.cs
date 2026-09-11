using DeskAI.App.ViewModels;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// "Review in Organize" on an automatic check's notice, pressed the way a person presses it:
/// the check runs, the notice appears, the button is pressed, and Organize opens.
/// </summary>
public sealed class ReviewInOrganizePageTests
{
    [Fact]
    public async Task Review_in_Organize_opens_the_folder_with_the_most_matches_and_moves_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var alpha = app.MakeFolder("Alpha", "invoice-a.pdf");
        var zeta = app.MakeFolder("Zeta", "invoice-b.pdf", "invoice-c.pdf", "holiday.jpg");
        await TidySuggestionTests.ConnectAndAllowAsync(app, alpha);
        await TidySuggestionTests.ConnectAndAllowAsync(app, zeta);
        var shell = await CheckWithRuleAsync(app);

        Assert.True(shell.CanReviewInOrganize);
        shell.ReviewInOrganize();
        var page = await OpenOrganizeAsync(app);

        Assert.False(shell.HasFinding);
        Assert.Equal("Zeta", page.SelectedFolder?.Name);
        Assert.Equal(
            "From your automatic check: your rules place 2 files here, marked \"Your rule\". Nothing moves until you press Tidy.",
            page.ReviewNote);
        var sorted = Assert.Single(page.Groups, group => group.Folder == "Sorted");
        Assert.All(sorted.Items, item => Assert.StartsWith("Your rule", item.Reason, StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(zeta, "invoice-b.pdf")));
        Assert.True(File.Exists(Path.Combine(zeta, "invoice-c.pdf")));
        Assert.False(Directory.Exists(Path.Combine(zeta, "Sorted")));
        shell.Dispose();
    }

    [Fact]
    public async Task A_folder_that_may_not_be_tidied_opens_with_the_permission_card_and_a_line_saying_why()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Inbox", "invoice-march.pdf");
        await ConnectOnlyAsync(app, folder);
        var shell = await CheckWithRuleAsync(app);

        shell.ReviewInOrganize();
        var page = await OpenOrganizeAsync(app);

        Assert.Equal("Inbox", page.SelectedFolder?.Name);
        Assert.True(page.NeedsPermission);
        Assert.Contains("Allow tidying to see which ones", page.ReviewNote, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "invoice-march.pdf")));
        shell.Dispose();
    }

    [Fact]
    public async Task Matches_only_inside_subfolders_are_said_to_be_nothing_to_tidy_here()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Inbox", @"Old\invoice-march.pdf", "holiday.jpg");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var shell = await CheckWithRuleAsync(app);

        shell.ReviewInOrganize();
        var page = await OpenOrganizeAsync(app);

        Assert.Contains("not loose at the top of this folder", page.ReviewNote, StringComparison.Ordinal);
        Assert.DoesNotContain(page.Groups, group => group.Folder == "Sorted");
        shell.Dispose();
    }

    [Fact]
    public async Task The_request_is_used_once_and_a_later_visit_opens_as_usual()
    {
        await using var app = await TestApp.StartAsync();
        await TidySuggestionTests.ConnectAndAllowAsync(app, app.MakeFolder("Alpha", "notes.txt"));
        await TidySuggestionTests.ConnectAndAllowAsync(app, app.MakeFolder("Zeta", "invoice-b.pdf"));
        var shell = await CheckWithRuleAsync(app);
        shell.ReviewInOrganize();
        await OpenOrganizeAsync(app);

        var later = await OpenOrganizeAsync(app);

        Assert.Equal("Alpha", later.SelectedFolder?.Name);
        Assert.False(later.HasReviewNote);
        shell.Dispose();
    }

    [Fact]
    public async Task A_folder_disconnected_since_the_check_opens_the_page_as_usual()
    {
        await using var app = await TestApp.StartAsync();
        await TidySuggestionTests.ConnectAndAllowAsync(app, app.MakeFolder("Alpha", "notes.txt"));
        var zetaId = await TidySuggestionTests.ConnectAndAllowAsync(app, app.MakeFolder("Zeta", "invoice-b.pdf"));
        var shell = await CheckWithRuleAsync(app);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.DisconnectFolderCommand.ExecuteAsync(zetaId);

        shell.ReviewInOrganize();
        var page = await OpenOrganizeAsync(app);

        Assert.Equal("Alpha", page.SelectedFolder?.Name);
        Assert.False(page.HasReviewNote);
        shell.Dispose();
    }

    /// <summary>Writes a rule for invoices, presses Check now, and returns the shell with its notice.</summary>
    private static async Task<ShellViewModel> CheckWithRuleAsync(TestApp app)
    {
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        automation.NewRuleName = "Tidy invoices";
        automation.NewRuleNameContains = "invoice";
        automation.NewRuleDestination = "Sorted";
        await automation.AddRuleCommand.ExecuteAsync(null);
        var shell = app.Get<ShellViewModel>();

        await automation.CheckNowCommand.ExecuteAsync(null);

        Assert.True(shell.HasFinding);
        return shell;
    }

    private static async Task ConnectOnlyAsync(TestApp app, string folder)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
    }

    /// <summary>What the window does after the button: a fresh Organize page.</summary>
    private static async Task<TidyViewModel> OpenOrganizeAsync(TestApp app)
    {
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        return page;
    }
}
