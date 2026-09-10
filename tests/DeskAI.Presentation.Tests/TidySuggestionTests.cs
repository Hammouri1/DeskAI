using DeskAI.App.ViewModels;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>What DeskAI would suggest for a real (generated) folder. Nothing here moves a file.</summary>
public sealed class TidySuggestionTests
{
    private static readonly IReadOnlyDictionary<Guid, SameNameChoice> NoChoices = new Dictionary<Guid, SameNameChoice>();

    [Fact]
    public async Task Loose_files_are_suggested_by_type_and_files_in_subfolders_are_left_where_they_are()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "holiday.jpg", "setup.exe", @"Uni\essay.docx");
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        var byName = preview.Suggestions.ToDictionary(item => item.FileName, item => item.DestinationFolder);
        Assert.Equal("Documents", byName["invoice.pdf"]);
        Assert.Equal("Pictures", byName["holiday.jpg"]);
        Assert.Equal("Installers", byName["setup.exe"]);
        Assert.DoesNotContain(preview.Suggestions, item => item.FileName.Contains("essay", StringComparison.Ordinal));
        Assert.All(preview.Suggestions, item => Assert.NotNull(item.MoveOperationId));
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task Busy_recent_hidden_and_unknown_files_are_left_alone_with_a_reason()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        app.MakeFile("Downloads", "movie.mp4.crdownload");
        app.MakeFile("Downloads", "just-saved.pdf", age: TimeSpan.FromSeconds(10));
        var hidden = app.MakeFile("Downloads", "secret.pdf");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        app.MakeFile("Downloads", "mystery.zzz");
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        File.SetAttributes(hidden, FileAttributes.Normal);
        Assert.Empty(preview.Suggestions);
        var reasons = preview.LeftAlone.ToDictionary(item => item.FileName, item => item.Reason);
        Assert.Equal(LeftAloneReason.StillDownloading, reasons["movie.mp4.crdownload"]);
        Assert.Equal(LeftAloneReason.ChangedRecently, reasons["just-saved.pdf"]);
        Assert.Equal(LeftAloneReason.HiddenOrSystem, reasons["secret.pdf"]);
        Assert.Equal(LeftAloneReason.UnknownType, reasons["mystery.zzz"]);
        Assert.All(preview.LeftAlone, item => Assert.False(string.IsNullOrWhiteSpace(item.Explanation)));
    }

    [Fact]
    public async Task An_online_only_file_is_left_alone()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        var online = app.MakeFile("Downloads", "cloud.pdf");
        File.SetAttributes(online, FileAttributes.Offline);
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        File.SetAttributes(online, FileAttributes.Normal);
        Assert.Equal(LeftAloneReason.OnlineOnly, Assert.Single(preview.LeftAlone).Reason);
    }

    [Fact]
    public async Task Your_rule_wins_over_the_file_type()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "bank-invoice.pdf", "notes.pdf");
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        automation.NewRuleName = "Tidy invoices";
        automation.NewRuleNameContains = "invoice";
        automation.NewRuleDestination = @"Documents\Invoices";
        await automation.AddRuleCommand.ExecuteAsync(null);
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        var invoice = preview.Suggestions.Single(item => item.FileName == "bank-invoice.pdf");
        Assert.Equal(@"Documents\Invoices", invoice.DestinationFolder);
        Assert.Equal(TidySuggestionSource.Rule, invoice.Source);
        Assert.Contains("Tidy invoices", invoice.Reason, StringComparison.Ordinal);
        Assert.Equal("Documents", preview.Suggestions.Single(item => item.FileName == "notes.pdf").DestinationFolder);
    }

    [Fact]
    public async Task A_same_name_already_there_is_skipped_unless_keep_both_is_chosen()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "report.pdf", @"Documents\report.pdf");
        var rootId = await ConnectAndAllowAsync(app, folder);

        var skipped = Assert.Single((await PreviewAsync(app, rootId)).Suggestions);
        Assert.True(skipped.HasSameName);
        Assert.Equal(SameNameChoice.Skip, skipped.Choice);
        Assert.Null(skipped.MoveOperationId);

        var kept = Assert.Single((await PreviewAsync(app, rootId,
            new Dictionary<Guid, SameNameChoice> { [skipped.FileId] = SameNameChoice.KeepBoth })).Suggestions);
        Assert.Equal(@"Documents\report (2).pdf", kept.DestinationRelativePath);
        Assert.NotNull(kept.MoveOperationId);
    }

    [Fact]
    public async Task At_most_500_files_are_suggested_at_once()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        for (var index = 0; index < TidySuggestionService.MaxFilesPerTidy + 10; index++)
        {
            app.MakeFile("Downloads", $"note-{index:D4}.pdf");
        }

        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        Assert.Equal(TidySuggestionService.MaxFilesPerTidy, preview.Suggestions.Count);
        Assert.True(preview.ReachedLimit);
    }

    [Fact]
    public async Task Without_tidy_permission_the_preview_says_it_cannot_tidy()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);

        var preview = await PreviewAsync(app, Assert.Single(search.Folders).Id);

        Assert.False(preview.CanTidy);
    }

    [Fact]
    public async Task Every_suggested_move_stays_inside_the_folder_and_passes_the_safety_check()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "holiday.jpg", "setup.exe");
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        Assert.Empty(app.Get<DeskAI.Core.Abstractions.IPlanSafetyCheck>().FindBlocked(preview.Plan, preview.Root));
        Assert.All(preview.Plan.Operations.OfType<DeskAI.Core.Plans.MoveFileOperation>(), move =>
            Assert.StartsWith(folder, Path.GetFullPath(move.DestinationRelativePath, folder), StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<Guid> ConnectAndAllowAsync(TestApp app, string folder)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = search.Folders.Single(item => item.Path == folder).Id;
        Assert.True((await app.Get<TidyPermissionService>().AllowAsync(rootId, TestContext.Current.CancellationToken)).IsAllowed);
        return rootId;
    }

    private static async Task<TidyPreview> PreviewAsync(
        TestApp app,
        Guid rootId,
        IReadOnlyDictionary<Guid, SameNameChoice>? choices = null) =>
        (await app.Get<TidySuggestionService>().PreviewAsync(
            rootId, Guid.NewGuid(), 1, choices ?? NoChoices, TidySuggestionMode.TypesAndRules,
            new Dictionary<Guid, TidyAiAdvice>(), TestContext.Current.CancellationToken))!;
}
