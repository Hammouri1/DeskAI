using System.Reflection;
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Tidy while I'm away (V0.9, ADR 0031), used the way a person uses it: turn the switch on
/// after the dialog, let an automatic check run, and come back to the card, the notice, and
/// Undo. Every folder is generated; nothing here can reach a real one.
/// </summary>
public sealed class AwayTidyPageTests
{
    [Fact]
    public async Task The_switch_is_off_and_disabled_until_tidying_is_allowed_and_a_rule_is_on()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice-a.pdf");
        await BackupPageTests.ConnectAsync(app, folder);
        var tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();

        Assert.False(tidy.HasAwaySwitch);
        Assert.Null(await tidy.PrepareAwayQuestionAsync());

        await app.Get<TidyPermissionService>().AllowAsync(Assert.Single(tidy.Folders).Id, TestContext.Current.CancellationToken);
        tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        Assert.True(tidy.HasAwaySwitch);
        Assert.False(tidy.IsAwayOn);
        Assert.False(tidy.CanUseAwaySwitch);
        Assert.Equal("Turn on a rule in Automatic tasks first.", tidy.AwayLine);
        Assert.Null(await tidy.PrepareAwayQuestionAsync());

        await BackupPageTests.AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        Assert.True(tidy.CanUseAwaySwitch);
        Assert.Equal("Off. DeskAI moves nothing here on its own.", tidy.AwayLine);
        var question = Assert.IsType<AwayTidyQuestion>(await tidy.PrepareAwayQuestionAsync());
        Assert.Equal("Tidy Downloads while you're away?", question.Title);
        Assert.Contains("invoice", Assert.Single(question.Rules), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("At most 25 files each time", question.Promise, StringComparison.Ordinal);
        Assert.Contains("never deletes", question.Promise, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_the_yes_a_check_moves_only_rule_placed_loose_files_and_the_card_notice_and_undo_follow()
    {
        await using var first = await TestApp.StartAsync();
        var folder = first.MakeFolder("Downloads", "invoice-a.pdf", "holiday.jpg");
        first.MakeFile("Downloads", Path.Combine("Old", "invoice-old.pdf"));
        await TidySuggestionTests.ConnectAndAllowAsync(first, folder);
        await BackupPageTests.AddRuleAsync(first, "Tidy invoices", "invoice", "Sorted");
        var tidy = first.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        var shell = first.Get<ShellViewModel>();

        await tidy.TurnAwayOnAsync();
        Assert.True(tidy.IsAwayOn);
        Assert.StartsWith("On since", tidy.AwayLine, StringComparison.Ordinal);
        Assert.Contains("for 1 rule", tidy.AwayLine, StringComparison.Ordinal);
        Assert.Equal(1, await first.Get<AwayTidyService>().CountActiveAsync(TestContext.Current.CancellationToken));

        var automation = first.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        await automation.CheckNowCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(folder, "Sorted", "invoice-a.pdf")));
        Assert.False(File.Exists(Path.Combine(folder, "invoice-a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")), "a file placed by type must never move on its own");
        Assert.True(File.Exists(Path.Combine(folder, "Old", "invoice-old.pdf")), "a file in a subfolder must never move");
        Assert.False(Directory.Exists(Path.Combine(folder, "Pictures")));
        Assert.True(shell.HasFinding);
        Assert.Equal("While you were away, DeskAI tidied 1 file in Downloads. Nothing was deleted.", shell.FindingMessage);
        Assert.True(shell.CanReviewInOrganize);
        Assert.Empty(first.Notifier.Messages);

        var later = first.Get<TidyViewModel>();
        await later.InitializeAsync();
        Assert.True(later.HasAwayRuns);
        var line = Assert.Single(later.AwayRuns);
        Assert.StartsWith("While you were away, DeskAI tidied 1 file into 1 folder at ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("invoice", line, StringComparison.OrdinalIgnoreCase);
        Assert.True(later.IsAwayOn);
        Assert.True(later.CanUndo);
        shell.Dispose();

        // Closing and reopening: the run is still there to look at, and Undo still works.
        await using var app = await first.ReopenAsync();
        var reopened = app.Get<TidyViewModel>();
        await reopened.InitializeAsync();
        Assert.True(reopened.HasAwayRuns);
        Assert.True(reopened.CanUndo);

        var undo = await reopened.UndoLastTidyAsync();
        Assert.NotNull(undo);
        Assert.True(File.Exists(Path.Combine(folder, "invoice-a.pdf")));
        Assert.False(File.Exists(Path.Combine(folder, "Sorted", "invoice-a.pdf")));

        await reopened.GotItCommand.ExecuteAsync(null);
        Assert.False(reopened.HasAwayRuns);
        var again = app.Get<TidyViewModel>();
        await again.InitializeAsync();
        Assert.False(again.HasAwayRuns);
    }

    [Fact]
    public async Task At_most_25_files_move_in_one_run_and_the_rest_wait_for_the_next_check()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        for (var i = 0; i < 30; i++)
        {
            app.MakeFile("Downloads", $"invoice-{i:D2}.pdf");
        }

        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        await BackupPageTests.AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        var tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        await tidy.TurnAwayOnAsync();
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();

        await automation.CheckNowCommand.ExecuteAsync(null);
        Assert.Equal(AwayTidyLimits.MaxFilesPerRun, Directory.GetFiles(Path.Combine(folder, "Sorted")).Length);
        Assert.Equal(5, Directory.GetFiles(folder, "*.pdf").Length);

        await automation.CheckNowCommand.ExecuteAsync(null);
        Assert.Equal(30, Directory.GetFiles(Path.Combine(folder, "Sorted")).Length);
        Assert.Empty(Directory.GetFiles(folder, "*.pdf"));
    }

    [Fact]
    public async Task A_same_name_clash_stops_the_run_before_anything_moves_and_says_why()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice-a.pdf", "invoice-b.pdf");
        app.MakeFile("Downloads", Path.Combine("Sorted", "invoice-a.pdf"));
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        await BackupPageTests.AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        var tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        await tidy.TurnAwayOnAsync();
        var shell = app.Get<ShellViewModel>();
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();

        await automation.CheckNowCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(folder, "invoice-a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "invoice-b.pdf")));
        Assert.Equal("DeskAI stopped tidying while you're away in Downloads and needs you to look.", shell.FindingMessage);
        var later = app.Get<TidyViewModel>();
        await later.InitializeAsync();
        Assert.False(later.IsAwayOn);
        Assert.True(later.AwayLineIsCaution);
        Assert.Contains("A file called invoice-a.pdf is already in Sorted", later.AwayLine, StringComparison.Ordinal);
        Assert.Contains("stopped tidying", Assert.Single(later.AwayRuns), StringComparison.Ordinal);
        Assert.True(later.CanUseAwaySwitch);
        shell.Dispose();
    }

    [Fact]
    public async Task A_rule_change_after_the_yes_turns_it_off_before_the_next_run()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice-a.pdf");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        await BackupPageTests.AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        var tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        await tidy.TurnAwayOnAsync();
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();

        await BackupPageTests.AddRuleAsync(app, "Tidy photos", "holiday", "Pictures");
        await automation.CheckNowCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(folder, "invoice-a.pdf")));
        var later = app.Get<TidyViewModel>();
        await later.InitializeAsync();
        Assert.False(later.IsAwayOn);
        Assert.Contains("A rule was added or turned on since you agreed.", later.AwayLine, StringComparison.Ordinal);

        // Turning a rule off is a change too, and so is looking: the status check turns it off at once.
        await later.TurnAwayOnAsync();
        Assert.True(later.IsAwayOn);
        await automation.InitializeAsync();
        await automation.ToggleRuleCommand.ExecuteAsync(automation.Rules.Single(rule => rule.Name == "Tidy photos").Id);
        var looked = app.Get<TidyViewModel>();
        await looked.InitializeAsync();
        Assert.False(looked.IsAwayOn);
        Assert.Contains("removed or turned off", looked.AwayLine, StringComparison.Ordinal);
        Assert.Equal(0, await app.Get<AwayTidyService>().CountActiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Taking_the_tidy_permission_back_ends_it_and_a_busy_file_stops_it_after_the_run()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice-a.pdf", "invoice-b.pdf");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        await BackupPageTests.AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        var tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        await tidy.TurnAwayOnAsync();
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();

        await tidy.StopTidyingCommand.ExecuteAsync(null);
        Assert.False(tidy.HasAwaySwitch);
        await automation.CheckNowCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(folder, "invoice-a.pdf")));

        await tidy.AllowTidyAsync();
        Assert.False(tidy.IsAwayOn);
        Assert.Contains("Tidying is no longer allowed", tidy.AwayLine, StringComparison.Ordinal);
        await tidy.TurnAwayOnAsync();
        Assert.True(tidy.IsAwayOn);

        using (File.Open(Path.Combine(folder, "invoice-b.pdf"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await automation.CheckNowCommand.ExecuteAsync(null);
        }

        Assert.True(File.Exists(Path.Combine(folder, "Sorted", "invoice-a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "invoice-b.pdf")));
        var later = app.Get<TidyViewModel>();
        await later.InitializeAsync();
        Assert.False(later.IsAwayOn);
        Assert.Contains("1 file couldn't be moved", later.AwayLine, StringComparison.Ordinal);
        Assert.Contains("Then it stopped", Assert.Single(later.AwayRuns), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_promise_that_nothing_moves_by_itself_follows_the_mode()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice-a.pdf");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        await BackupPageTests.AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        Assert.Equal("Nothing moves by itself", home.PromisePill);
        Assert.Contains("only when you press Tidy", home.HeroMessage, StringComparison.Ordinal);
        Assert.Equal("DeskAI never moves a file on its own", automation.OwnPromise);
        Assert.EndsWith("It never moves anything by itself.", automation.AutomaticCheckSummary, StringComparison.Ordinal);
        Assert.Contains("does not tidy while you are away", automation.AskAboutKeepingRunning().LimitLine, StringComparison.Ordinal);
        Assert.Contains("does not move anything", automation.MoreDetails, StringComparison.Ordinal);

        var tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        await tidy.TurnAwayOnAsync();
        home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();

        Assert.Equal("Moves files on its own in 1 folder you chose", home.PromisePill);
        Assert.Contains("on its own", home.HeroMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("only when you press Tidy", home.HeroMessage, StringComparison.Ordinal);
        Assert.Equal("DeskAI moves files on its own only in 1 folder where you turned on Tidy while I'm away", automation.OwnPromise);
        Assert.DoesNotContain("never moves anything by itself", automation.AutomaticCheckSummary, StringComparison.Ordinal);
        Assert.Contains("only what your rules match", automation.AutomaticCheckSummary, StringComparison.Ordinal);
        Assert.Contains("moves what your rules match, at most 25 files", automation.AskAboutKeepingRunning().LimitLine, StringComparison.Ordinal);
        Assert.Contains("Tidy while I'm away", automation.MoreDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("does not move anything", automation.MoreDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_fresh_ends_it_everywhere()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice-a.pdf");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        await BackupPageTests.AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        var tidy = app.Get<TidyViewModel>();
        await tidy.InitializeAsync();
        await tidy.TurnAwayOnAsync();
        Assert.Equal(1, await app.Get<AwayTidyService>().CountActiveAsync(TestContext.Current.CancellationToken));

        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        await settings.StartFreshAsync();

        Assert.Equal(0, await app.Get<AwayTidyService>().CountActiveAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await app.Get<IAwayTidyRepository>().ListAsync(TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(folder, "invoice-a.pdf")));
    }

    [Fact]
    public void The_away_service_is_the_only_unattended_path_and_holds_no_AI_reader_or_credential()
    {
        static Type[] Dependencies(Type type) => type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        var away = Dependencies(typeof(AwayTidyService));
        Assert.Contains(typeof(TidyRunService), away);
        Assert.DoesNotContain(away, type => type.Name.Contains("Ai", StringComparison.Ordinal));
        foreach (var forbidden in new[] { typeof(ICredentialVault), typeof(IContentTextExtractor), typeof(IFileFingerprinter), typeof(IWallpaperSetter), typeof(IUserFileStore) })
        {
            Assert.DoesNotContain(forbidden, away);
        }

        // The check itself still holds nothing that moves a file; only the coordinator reaches
        // the away service, and it is the one parameter that can.
        Assert.DoesNotContain(typeof(AwayTidyService), Dependencies(typeof(AutomaticCheckService)));
        Assert.DoesNotContain(typeof(TidyRunService), Dependencies(typeof(AutomaticCheckService)));
        Assert.Contains(typeof(IAwayTidyRunner), Dependencies(typeof(AutomaticCheckCoordinator)));
        Assert.Equal([typeof(AwayTidyService)], typeof(AwayTidyService).Assembly.GetTypes().Where(type => typeof(IAwayTidyRunner).IsAssignableFrom(type) && !type.IsInterface));
        Assert.DoesNotContain(typeof(TidyRunService), Dependencies(typeof(AutomaticCheckCoordinator)));
        Assert.DoesNotContain(typeof(IFolderTidyExecutor), Dependencies(typeof(AutomaticCheckCoordinator)));
    }
}
