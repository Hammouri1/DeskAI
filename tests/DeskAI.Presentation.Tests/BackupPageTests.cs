using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Backup;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Back up and restore on Privacy and AI, used the way a person uses it: save a file, look at
/// what restoring it would do, restore, and find the rules switched off. Every file is generated
/// inside the test's own folder.
/// </summary>
public sealed class BackupPageTests
{
    [Fact]
    public async Task A_backup_holds_rules_and_saved_searches_and_nothing_about_folders_keys_or_locations()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Coursework", "invoice-march.pdf");
        await ConnectAsync(app, folder);
        await AddRuleAsync(app, "Tidy invoices", "invoice", "Sorted");
        await SaveSearchAsync(app, "Photos", "photos");
        await app.Vault.SaveAsync("DeskAI/OpenRouter", "sk-or-generated-not-a-real-key", TestContext.Current.CancellationToken);
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        var path = Path.Combine(app.Directory.Path, "backup.json");

        await settings.ExportBackupAsync(path);

        Assert.Equal("Saved 1 rule and 1 saved search to backup.json. The file holds no folders, keys, or locations.", settings.BackupStatus);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("Tidy invoices", text, StringComparison.Ordinal);
        Assert.Contains("\"name-contains\"", text, StringComparison.Ordinal);
        Assert.Contains("Photos", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Coursework", text, StringComparison.Ordinal);
        Assert.DoesNotContain(app.Directory.Path, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-or-", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenRouter", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restoring_shows_what_would_be_added_first_adds_it_once_and_rules_arrive_switched_off()
    {
        await using var source = await TestApp.StartAsync();
        await AddRuleAsync(source, "Tidy invoices", "invoice", "Sorted");
        await SaveSearchAsync(source, "Photos", "photos");
        var sourceSettings = source.Get<SettingsViewModel>();
        await sourceSettings.InitializeAsync();
        var path = Path.Combine(source.Directory.Path, "backup.json");
        await sourceSettings.ExportBackupAsync(path);

        await using var app = await TestApp.StartAsync();
        await SaveSearchAsync(app, "photos", "pictures");
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        var preview = await settings.PreviewRestoreAsync(path);
        Assert.NotNull(preview);
        Assert.Equal(1, preview.RulesToAdd);
        Assert.Equal(0, preview.SearchesToAdd);
        Assert.Equal("You already have a search called Photos.", Assert.Single(preview.Searches).SkipReason);
        Assert.Contains("invoice", Assert.Single(preview.Rules).Description, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await app.Get<IRuleRepository>().ListAsync(TestContext.Current.CancellationToken));

        await settings.RestoreBackupAsync(path);

        Assert.Equal("Restored 1 rule and 0 saved searches. Skipped 1 you already had or DeskAI can't use: Photos. Restored rules are switched off — turn them on in Automatic tasks.", settings.BackupStatus);
        var rule = Assert.Single(await app.Get<IRuleRepository>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Tidy invoices", rule.Name);
        Assert.False(rule.IsEnabled);

        await settings.RestoreBackupAsync(path);
        Assert.StartsWith("Restored 0 rules and 0 saved searches.", settings.BackupStatus, StringComparison.Ordinal);
        Assert.Single(await app.Get<IRuleRepository>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_is_refused_in_plain_words_and_nothing_is_added()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();

        var junk = Path.Combine(app.Directory.Path, "junk.json");
        await File.WriteAllTextAsync(junk, "{ \"hello\": 1 }", TestContext.Current.CancellationToken);
        Assert.Null(await settings.PreviewRestoreAsync(junk));
        Assert.Equal("That file is not a DeskAI backup.", settings.BackupStatus);

        var newer = Path.Combine(app.Directory.Path, "newer.json");
        await File.WriteAllTextAsync(newer, "{\"Version\":9,\"MadeAtUtc\":\"2026-09-16T00:00:00+00:00\",\"Rules\":[],\"SavedSearches\":[]}", TestContext.Current.CancellationToken);
        Assert.Null(await settings.PreviewRestoreAsync(newer));
        Assert.Contains("newer DeskAI", settings.BackupStatus, StringComparison.Ordinal);

        var notJson = Path.Combine(app.Directory.Path, "notes.txt");
        await File.WriteAllTextAsync(notJson, "{}", TestContext.Current.CancellationToken);
        Assert.Null(await settings.PreviewRestoreAsync(notJson));
        Assert.Contains(".json", settings.BackupStatus, StringComparison.Ordinal);

        var huge = Path.Combine(app.Directory.Path, "huge.json");
        await File.WriteAllTextAsync(huge, new string(' ', DeskAiBackup.MaxBytes + 1), TestContext.Current.CancellationToken);
        Assert.Null(await settings.PreviewRestoreAsync(huge));
        Assert.Contains("too big", settings.BackupStatus, StringComparison.Ordinal);

        Assert.Empty(await app.Get<IRuleRepository>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await app.Get<ISavedSearchRepository>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_rule_in_the_file_that_DeskAI_cannot_use_is_skipped_with_a_reason_and_the_rest_restored()
    {
        await using var app = await TestApp.StartAsync();
        var settings = app.Get<SettingsViewModel>();
        await settings.InitializeAsync();
        var path = Path.Combine(app.Directory.Path, "edited.json");
        await File.WriteAllTextAsync(path, """
            {
              "Version": 1,
              "MadeAtUtc": "2026-09-16T00:00:00+00:00",
              "Rules": [
                { "Name": "Escape", "Conditions": [ { "Kind": "name-contains", "Value": "x" } ], "Action": { "Kind": "move-to-folder", "Value": "..\\Windows" } },
                { "Name": "Mystery", "Conditions": [ { "Kind": "run-program", "Value": "cmd" } ], "Action": { "Kind": "move-to-folder", "Value": "Sorted" } },
                { "Name": "Everything", "Conditions": [ ], "Action": { "Kind": "move-to-folder", "Value": "Sorted" } },
                { "Name": "Fine", "Conditions": [ { "Kind": "extension-is", "Value": ".pdf" } ], "Action": { "Kind": "move-to-folder", "Value": "Documents" } }
              ],
              "SavedSearches": [ { "Name": "Big", "Phrase": "big files", "IsPinned": true } ]
            }
            """, TestContext.Current.CancellationToken);

        var preview = await settings.PreviewRestoreAsync(path);
        Assert.NotNull(preview);
        Assert.Equal(1, preview.RulesToAdd);
        Assert.All(preview.Rules.Where(line => line.Name != "Fine"), line => Assert.StartsWith("DeskAI can't use this rule:", line.SkipReason, StringComparison.Ordinal));

        await settings.RestoreBackupAsync(path);

        var rule = Assert.Single(await app.Get<IRuleRepository>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Fine", rule.Name);
        Assert.False(rule.IsEnabled);
        var search = Assert.Single(await app.Get<ISavedSearchRepository>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Big", search.Name);
        Assert.True(search.IsPinned);
    }

    [Fact]
    public void The_backup_services_hold_nothing_that_can_reach_a_file_on_disk()
    {
        foreach (var type in new[] { typeof(BackupService), typeof(FreshStartService) })
        {
            var parameters = type.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.Name).ToArray();
            foreach (var forbidden in new[] { "IFolderTidyExecutor", "IOperationJournal", "IFileScanner", "IContentTextExtractor", "IFileFingerprinter", "IWallpaperSetter", "IUserFileStore" })
            {
                Assert.DoesNotContain(forbidden, parameters);
            }
        }
    }

    internal static async Task ConnectAsync(TestApp app, string folder)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
    }

    internal static async Task AddRuleAsync(TestApp app, string name, string contains, string destination)
    {
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        automation.NewRuleName = name;
        automation.NewRuleNameContains = contains;
        automation.NewRuleDestination = destination;
        await automation.AddRuleCommand.ExecuteAsync(null);
        Assert.Contains(automation.Rules, rule => rule.Name == name);
    }

    internal static async Task SaveSearchAsync(TestApp app, string name, string phrase)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        search.Phrase = phrase;
        await search.SaveCurrentSearchAsync(name);
        Assert.Contains(search.SavedSearches, saved => saved.Name == name);
    }
}
