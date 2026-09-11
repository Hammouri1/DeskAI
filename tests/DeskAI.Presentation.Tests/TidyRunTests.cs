using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Tidying a real (generated) folder and undoing it, with the whole app built as it runs.
/// </summary>
/// <remarks>
/// These are the negative tests the tidy review (<c>docs/security/2026-09-10-real-folder-tidy-review.md</c>)
/// lists. Every test also checks a sentinel file beside the folder, which no tidy may touch.
/// </remarks>
public sealed class TidyRunTests
{
    private static readonly IReadOnlyDictionary<Guid, SameNameChoice> NoChoices = new Dictionary<Guid, SameNameChoice>();

    [Fact]
    public async Task Tidy_moves_exactly_the_ticked_files_into_folders_inside_the_folder()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(app, "invoice.pdf", "notes.pdf", "holiday.jpg", "setup.exe", @"Uni\essay.docx");
        var preview = await PreviewAsync(app, rootId);
        var ticked = MoveIds(preview, "invoice.pdf", "holiday.jpg");

        var result = await app.Get<TidyRunService>().TidyAsync(preview, ticked, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Moved);
        Assert.Empty(result.Skipped);
        Assert.True(result.CanUndo);
        Assert.StartsWith("Done. Downloads: 2 files tidied into 2 folders.", result.Summary, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Pictures", "holiday.jpg")));
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "setup.exe")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Installers")));
        Assert.True(File.Exists(Path.Combine(folder, "Uni", "essay.docx")));
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task Keep_both_moves_the_file_under_a_new_name_and_never_replaces_the_one_there()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(app, "report.pdf", @"Documents\report.pdf");
        var first = await PreviewAsync(app, rootId);
        var fileId = Assert.Single(first.Suggestions).FileId;
        var preview = await PreviewAsync(app, rootId, new Dictionary<Guid, SameNameChoice> { [fileId] = SameNameChoice.KeepBoth });

        var result = await app.Get<TidyRunService>().TidyAsync(preview, MoveIds(preview, "report.pdf"), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "report (2).pdf")));
        Assert.Equal("Generated DeskAI test data", File.ReadAllText(Path.Combine(folder, "Documents", "report.pdf")));
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task A_file_changed_after_the_list_was_made_is_left_where_it_is_and_the_rest_move()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(app, "invoice.pdf", "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        File.AppendAllText(Path.Combine(folder, "notes.pdf"), " edited after the list");

        var result = await TidyAllAsync(app, preview);

        Assert.Equal(1, result.Moved);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("notes.pdf", skipped.FileName);
        Assert.Contains("changed after the list", skipped.Reason, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
        Assert.DoesNotContain("Done", result.Summary, StringComparison.Ordinal);
        Assert.Contains("1 of 2", result.Summary, StringComparison.Ordinal);
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task A_file_replaced_by_another_with_the_same_name_is_left_where_it_is()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        var path = Path.Combine(folder, "notes.pdf");
        File.Delete(path);
        File.WriteAllText(path, "A different file that happens to share the name");

        var result = await TidyAllAsync(app, preview);

        Assert.Equal(0, result.Moved);
        Assert.False(result.CanUndo);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task A_file_that_disappeared_after_the_list_was_made_is_reported()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "invoice.pdf", "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        File.Delete(Path.Combine(folder, "notes.pdf"));

        var result = await TidyAllAsync(app, preview);

        Assert.Equal(1, result.Moved);
        Assert.Equal("It is no longer there.", Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public async Task A_name_taken_after_the_list_was_made_is_never_replaced()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        Directory.CreateDirectory(Path.Combine(folder, "Documents"));
        File.WriteAllText(Path.Combine(folder, "Documents", "notes.pdf"), "Someone else's notes");

        var result = await TidyAllAsync(app, preview);

        Assert.Equal(0, result.Moved);
        Assert.Contains("already there", Assert.Single(result.Skipped).Reason, StringComparison.Ordinal);
        Assert.Equal("Someone else's notes", File.ReadAllText(Path.Combine(folder, "Documents", "notes.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
    }

    [Fact]
    public async Task A_file_open_in_another_program_stays_and_the_others_move()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "invoice.pdf", "notes.pdf");
        var preview = await PreviewAsync(app, rootId);

        TidyRunResult result;
        using (new FileStream(Path.Combine(folder, "notes.pdf"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = await TidyAllAsync(app, preview);
        }

        Assert.Equal(1, result.Moved);
        Assert.Contains("open in another program", Assert.Single(result.Skipped).Reason, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));
    }

    [Fact]
    public async Task A_file_that_became_online_only_is_left_where_it_is()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        var path = Path.Combine(folder, "notes.pdf");
        File.SetAttributes(path, FileAttributes.Offline);
        try
        {
            var result = await TidyAllAsync(app, preview);

            Assert.Equal(0, result.Moved);
            Assert.Contains("online only", Assert.Single(result.Skipped).Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Taking_back_permission_after_the_list_was_made_moves_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(app, "invoice.pdf", "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        await app.Get<TidyPermissionService>().StopAsync(rootId, TestContext.Current.CancellationToken);

        var result = await TidyAllAsync(app, preview);

        Assert.Equal(0, result.Moved);
        Assert.Equal(2, result.Skipped.Count);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Documents")));
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task Disconnecting_the_folder_after_the_list_was_made_moves_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "invoice.pdf");
        var preview = await PreviewAsync(app, rootId);
        var search = app.Get<DeskAI.App.ViewModels.SearchViewModel>();
        await search.InitializeAsync();
        await search.DisconnectFolderCommand.ExecuteAsync(rootId);

        var result = await TidyAllAsync(app, preview);

        Assert.Equal(0, result.Moved);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task A_link_put_where_a_destination_folder_goes_is_refused_and_nothing_goes_through_it()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "notes.pdf");
        var outside = app.Directory.CreateDummyDirectory("outside-target");
        var preview = await PreviewAsync(app, rootId);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(folder, "Documents"), outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        var result = await TidyAllAsync(app, preview);

        Assert.Equal(0, result.Moved);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
    }

    [Fact]
    public async Task A_plan_with_a_destination_outside_the_folder_moves_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(app, "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        var escape = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"..\escaped.pdf", "Hostile", OperationProvenance.CloudAi);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), rootId, 1, DateTimeOffset.UtcNow, preview.Plan.PolicyVersion, [escape]);
        var file = preview.MoveSources.Values.Single();

        var result = await app.Get<IFolderTidyExecutor>().ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [escape.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [escape.Id] = new(file.SizeBytes, file.ModifiedAtUtc) },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
        Assert.False(File.Exists(Path.Combine(app.Sandbox, "escaped.pdf")));
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task An_approval_for_a_different_revision_moves_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        var stale = OrganizationPlan.CreateDraft(
            preview.Plan.Id, rootId, preview.Plan.Revision + 1, DateTimeOffset.UtcNow, preview.Plan.PolicyVersion, preview.Plan.Operations);
        var approval = Approval.Create(Guid.NewGuid(), stale, preview.Plan.Operations.Select(item => item.Id), DateTimeOffset.UtcNow);

        var result = await app.Get<IFolderTidyExecutor>().ExecuteAsync(
            preview.Plan, approval, new Dictionary<Guid, ExpectedFile>(), TestContext.Current.CancellationToken);

        Assert.All(result.Operations, item => Assert.Equal(ExecutionOutcome.Failed, item.Outcome));
        Assert.True(File.Exists(Path.Combine(folder, "notes.pdf")));
    }

    [Fact]
    public async Task An_old_practice_folder_left_in_the_database_can_never_be_tidied()
    {
        await using var app = await TestApp.StartAsync();
        var practice = await OldPracticeFolderAsync(app);
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sheets\semester-budget.xlsx", "Test", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), practice.Id, 1, DateTimeOffset.UtcNow, "v1", [move]);

        var result = await app.Get<IFolderTidyExecutor>().ExecuteAsync(
            plan, Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile>(), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(Path.Combine(practice.CanonicalPath, "semester-budget.xlsx")));
    }

    [Fact]
    public async Task Undo_puts_every_file_back_and_removes_only_the_folders_DeskAI_made()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(app, "invoice.pdf", "holiday.jpg", @"Documents\old.txt");
        var run = await TidyAllAsync(app, await PreviewAsync(app, rootId));

        var undo = await UndoAsync(app, rootId, run);

        Assert.True(undo.Finished);
        Assert.Equal(2, undo.Restored);
        Assert.Empty(undo.NotRestored);
        Assert.StartsWith("Undone.", undo.Summary, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Pictures")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "old.txt")));
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task Undo_keeps_a_folder_that_was_there_before_even_when_it_is_empty_again()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "invoice.pdf");
        Directory.CreateDirectory(Path.Combine(folder, "Documents"));
        var run = await TidyAllAsync(app, await PreviewAsync(app, rootId));

        await UndoAsync(app, rootId, run);

        Assert.True(Directory.Exists(Path.Combine(folder, "Documents")));
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task Undo_leaves_a_file_that_changed_since_and_one_whose_old_spot_is_taken()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "invoice.pdf", "notes.pdf", "holiday.jpg");
        var run = await TidyAllAsync(app, await PreviewAsync(app, rootId));
        File.AppendAllText(Path.Combine(folder, "Documents", "notes.pdf"), " edited after tidying");
        File.WriteAllText(Path.Combine(folder, "invoice.pdf"), "A new file where the old one was");

        var undo = await UndoAsync(app, rootId, run);

        Assert.Equal(1, undo.Restored);
        Assert.Equal(2, undo.NotRestored.Count);
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "notes.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));
        Assert.Equal("A new file where the old one was", File.ReadAllText(Path.Combine(folder, "invoice.pdf")));
        Assert.True(Directory.Exists(Path.Combine(folder, "Documents")));
    }

    [Fact]
    public async Task Undo_needs_the_tidy_permission_and_works_once_it_is_given_again()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "invoice.pdf");
        var run = await TidyAllAsync(app, await PreviewAsync(app, rootId));
        await app.Get<TidyPermissionService>().StopAsync(rootId, TestContext.Current.CancellationToken);

        var refused = await UndoAsync(app, rootId, run);

        Assert.True(refused.NeedsPermission);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));

        await app.Get<TidyPermissionService>().AllowAsync(rootId, TestContext.Current.CancellationToken);
        var undone = await UndoAsync(app, rootId, run);

        Assert.Equal(1, undone.Restored);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task Undo_cannot_run_twice()
    {
        await using var app = await TestApp.StartAsync();
        var (_, rootId, _) = await SetUpAsync(app, "invoice.pdf");
        var run = await TidyAllAsync(app, await PreviewAsync(app, rootId));
        await UndoAsync(app, rootId, run);

        var second = await UndoAsync(app, rootId, run);

        Assert.False(second.Finished);
        Assert.Equal(0, second.Restored);
    }

    [Fact]
    public async Task A_record_from_an_old_practice_folder_is_never_undone()
    {
        await using var app = await TestApp.StartAsync();
        var practice = await OldPracticeFolderAsync(app);
        Directory.CreateDirectory(Path.Combine(practice.CanonicalPath, "Sheets"));
        File.Move(
            Path.Combine(practice.CanonicalPath, "semester-budget.xlsx"),
            Path.Combine(practice.CanonicalPath, "Sheets", "semester-budget.xlsx"));
        var move = new MoveFileOperation(Guid.NewGuid(), "semester-budget.xlsx", @"Sheets\semester-budget.xlsx", "Test", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), practice.Id, 1, DateTimeOffset.UtcNow, "v1", [move]);
        await app.Get<IPlanRepository>().SaveAsync(plan, TestContext.Current.CancellationToken);
        var info = new FileInfo(Path.Combine(practice.CanonicalPath, "Sheets", "semester-budget.xlsx"));
        var record = new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Completed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new OperationJournalEntry(0, move.Id, PlanOperationKind.MoveFile, move.SourceRelativePath, move.DestinationRelativePath,
                info.Length, info.LastWriteTimeUtc, JournalOperationState.Completed, null)]);
        await app.Get<IOperationJournal>().CreateAsync(record, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.Get<IFolderTidyExecutor>().UndoAsync(record.Id, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(info.FullName));
    }

    /// <summary>
    /// A practice folder as a database from before 2026-09-11 can still hold one. The practice
    /// page is gone, but its scope stays, and must stay meaning "never tidied".
    /// </summary>
    internal static async Task<AuthorizedRoot> OldPracticeFolderAsync(TestApp app)
    {
        var path = app.Directory.CreateDummyDirectory("DeskAI.Demo.old");
        app.Directory.CreateDummyFile(@"DeskAI.Demo.old\semester-budget.xlsx");
        var practice = AuthorizedRoot.Create(
            Guid.NewGuid(), path, "Safe temporary demo", RootAccessLevel.Allowed, RootAuthorizationScope.ControlledDemo);
        await app.Get<IAuthorizedRootRepository>().SaveAsync(practice, TestContext.Current.CancellationToken);
        await app.Get<IAuthorizedRootRepository>().AllowTidyAsync(practice.Id, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        return practice;
    }

    private static async Task<(string Folder, Guid RootId, string Sentinel)> SetUpAsync(TestApp app, params string[] files)
    {
        var folder = app.MakeFolder("Downloads", files);
        var sentinel = app.MakeFile("Sentinel", "do-not-touch.pdf", "Sentinel content");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        return (folder, rootId, sentinel);
    }

    private static async Task<TidyPreview> PreviewAsync(
        TestApp app,
        Guid rootId,
        IReadOnlyDictionary<Guid, SameNameChoice>? choices = null) =>
        (await app.Get<TidySuggestionService>().PreviewAsync(
            rootId, Guid.NewGuid(), 1, choices ?? NoChoices, TidySuggestionMode.TypesAndRules,
            new Dictionary<Guid, TidyAiAdvice>(), TestContext.Current.CancellationToken))!;

    private static Guid[] MoveIds(TidyPreview preview, params string[] names) =>
        [.. preview.Suggestions.Where(item => names.Contains(item.FileName)).Select(item => item.MoveOperationId!.Value)];

    private static Task<TidyRunResult> TidyAllAsync(TestApp app, TidyPreview preview) =>
        app.Get<TidyRunService>().TidyAsync(
            preview,
            [.. preview.Suggestions.Where(item => item.MoveOperationId is not null).Select(item => item.MoveOperationId!.Value)],
            TestContext.Current.CancellationToken);

    private static Task<TidyUndoResult> UndoAsync(TestApp app, Guid rootId, TidyRunResult run) =>
        app.Get<TidyRunService>().UndoAsync(rootId, run.TransactionId!.Value, run.MovedFiles, TestContext.Current.CancellationToken);

    private static void AssertSentinel(string sentinel) =>
        Assert.Equal("Sentinel content", File.ReadAllText(sentinel));
}
