using DeskAI.App.ViewModels;
using DeskAI.Core.Rules;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Automatic tasks page as a person uses it: write a rule, try a practice run, check
/// now, look at the history, and change how often DeskAI looks.
/// </summary>
public sealed class AutomationPageTests
{
    [Fact]
    public async Task Opening_the_page_with_a_saved_rule_does_not_say_there_are_no_rules()
    {
        await using var app = await TestApp.StartAsync();
        var first = app.Get<AutomationViewModel>();
        await first.InitializeAsync();
        await SaveInvoiceRuleAsync(first);

        var reopened = app.Get<AutomationViewModel>();
        await reopened.InitializeAsync();

        Assert.Single(reopened.Rules);
        Assert.DoesNotContain("No rules yet", reopened.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_the_page_with_no_rules_says_so()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();

        await page.InitializeAsync();

        Assert.Empty(page.Rules);
        Assert.Contains("No rules yet", page.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_a_rule_lists_it_in_words_and_clears_the_form()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await SaveInvoiceRuleAsync(page);

        var rule = Assert.Single(page.Rules);
        Assert.Equal("Tidy invoices", rule.Name);
        Assert.Contains("invoice", rule.Sentence, StringComparison.Ordinal);
        Assert.Contains("Sorted", rule.Sentence, StringComparison.Ordinal);
        Assert.Equal("On", rule.State);
        Assert.Empty(page.NewRuleName);
        Assert.StartsWith("Saved:", page.FormMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rule_with_nothing_to_look_for_is_refused_next_to_the_Save_button()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        page.NewRuleName = "Everything";
        page.NewRuleDestination = "Sorted";

        await page.AddRuleCommand.ExecuteAsync(null);

        Assert.Empty(page.Rules);
        Assert.Contains("match every file", page.FormMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C:\\Elsewhere")]
    [InlineData("..\\outside")]
    public async Task A_destination_outside_the_folder_is_refused(string destination)
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        page.NewRuleName = "Escape";
        page.NewRuleNameContains = "invoice";
        page.NewRuleDestination = destination;

        await page.AddRuleCommand.ExecuteAsync(null);

        Assert.Empty(page.Rules);
        Assert.False(string.IsNullOrWhiteSpace(page.FormMessage));
    }

    [Fact]
    public async Task A_typed_sentence_fills_the_form_without_saving_anything()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        page.Sentence = "move invoices to Documents";

        page.DraftFromSentenceCommand.Execute(null);

        Assert.Equal("invoice", page.NewRuleNameContains);
        Assert.Equal("Documents", page.NewRuleDestination);
        Assert.Empty(page.Rules);
        Assert.StartsWith("DeskAI read that as:", page.FormMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_practice_run_shows_the_files_a_rule_would_move_and_moves_none_of_them()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Inbox", "invoice-march.pdf", "invoice-april.pdf", "holiday.jpg");
        await ConnectAsync(app, folder);
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await SaveInvoiceRuleAsync(page);

        await page.PractiseCommand.ExecuteAsync(null);

        Assert.Equal("2 files would move", page.PracticeHeadline);
        Assert.Equal(2, page.Proposals.Count);
        Assert.All(page.Proposals, proposal => Assert.Equal("Sorted", proposal.Destination));
        Assert.True(File.Exists(Path.Combine(folder, "invoice-march.pdf")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Sorted")));
    }

    [Fact]
    public async Task Turning_a_rule_off_leaves_it_out_of_the_practice_run()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Inbox", "invoice-march.pdf");
        await ConnectAsync(app, folder);
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await SaveInvoiceRuleAsync(page);

        await page.ToggleRuleCommand.ExecuteAsync(Assert.Single(page.Rules).Id);
        await page.PractiseCommand.ExecuteAsync(null);

        Assert.Equal("Off", Assert.Single(page.Rules).State);
        Assert.Equal("No rules are on", page.PracticeHeadline);
        Assert.Empty(page.Proposals);
    }

    [Fact]
    public async Task Deleting_a_rule_removes_it_and_touches_no_file()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Inbox", "invoice-march.pdf");
        await ConnectAsync(app, folder);
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await SaveInvoiceRuleAsync(page);

        await page.DeleteRuleCommand.ExecuteAsync(Assert.Single(page.Rules).Id);

        Assert.Empty(page.Rules);
        Assert.True(File.Exists(Path.Combine(folder, "invoice-march.pdf")));
    }

    [Fact]
    public async Task Check_now_counts_matches_records_the_check_and_moves_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Inbox", "invoice-march.pdf", "holiday.jpg");
        await ConnectAsync(app, folder);
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await SaveInvoiceRuleAsync(page);

        await page.CheckNowCommand.ExecuteAsync(null);

        Assert.Contains("match 1 file(s)", page.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing has moved", page.Message, StringComparison.Ordinal);
        Assert.Equal("Last looked: just now.", page.LastCheckedDescription);
        Assert.Single(page.RecentChecks);
        Assert.True(File.Exists(Path.Combine(folder, "invoice-march.pdf")));
    }

    [Fact]
    public async Task Check_now_notices_a_file_that_arrived_after_connecting()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Inbox", "holiday.jpg");
        await ConnectAsync(app, folder);
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await SaveInvoiceRuleAsync(page);
        app.Directory.CreateDummyFile(Path.Combine("folders", "Inbox", "invoice-new.pdf"));

        await page.CheckNowCommand.ExecuteAsync(null);

        Assert.Contains("match 1 file(s)", page.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_the_history_empties_it()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.CheckNowCommand.ExecuteAsync(null);
        Assert.Single(page.RecentChecks);

        await page.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Empty(page.RecentChecks);
    }

    [Fact]
    public async Task How_often_pause_and_notification_choices_are_remembered()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        page.SelectedFrequency = page.FrequencyOptions.Single(option => option.Value == AutomaticCheckFrequency.EveryHour);
        page.IsPaused = true;
        page.NotifyWhenSomethingIsFound = true;
        await WaitForSaveAsync(app, settings => settings.IsPaused && settings.NotifyWhenSomethingIsFound
            && settings.Frequency == AutomaticCheckFrequency.EveryHour);

        var reopened = app.Get<AutomationViewModel>();
        await reopened.InitializeAsync();

        Assert.Equal(AutomaticCheckFrequency.EveryHour, reopened.SelectedFrequency.Value);
        Assert.True(reopened.IsPaused);
        Assert.True(reopened.NotifyWhenSomethingIsFound);
        Assert.Contains("paused", reopened.AutomaticCheckSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notifications_are_off_until_someone_turns_them_on()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();

        await page.InitializeAsync();

        Assert.False(page.NotifyWhenSomethingIsFound);
    }

    private static async Task SaveInvoiceRuleAsync(AutomationViewModel page)
    {
        page.NewRuleName = "Tidy invoices";
        page.NewRuleNameContains = "invoice";
        page.NewRuleDestination = "Sorted";
        await page.AddRuleCommand.ExecuteAsync(null);
        Assert.StartsWith("Saved:", page.FormMessage, StringComparison.Ordinal);
    }

    private static async Task ConnectAsync(TestApp app, string folder)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        Assert.Single(search.Folders);
    }

    /// <summary>
    /// Settings are saved as soon as they change, without being awaited by the setter, so
    /// the test waits for the stored values rather than for an arbitrary delay.
    /// </summary>
    private static async Task WaitForSaveAsync(TestApp app, Func<AutomaticCheckSettings, bool> saved)
    {
        var repository = app.Get<DeskAI.Core.Abstractions.IAutomaticCheckSettingsRepository>();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (saved(await repository.LoadAsync(TestContext.Current.CancellationToken)))
            {
                return;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The page did not save the choice.");
    }
}
