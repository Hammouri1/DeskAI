using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Tidy;
using DeskAI.Infrastructure.Execution;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Finding the last tidy again after DeskAI is reopened, and recovering a tidy or undo that
/// stopped part-way, with the whole app built as it runs.
/// </summary>
/// <remarks>
/// A crash is simulated by stopping the real journal at one moment of a real run, so the disk
/// and the journal are left exactly as a power cut at that moment would leave them; then DeskAI
/// is "reopened" over the same database. These are the negative tests the recovery review
/// (<c>docs/security/2026-09-11-tidy-recovery-review.md</c>) lists. Each checks a sentinel file
/// beside the folder, which nothing may touch.
/// </remarks>
public sealed class TidyRecoveryTests
{
    [Fact]
    public async Task After_reopening_the_last_tidy_is_found_and_undone_once()
    {
        await using var first = await TestApp.StartAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(first, "invoice.pdf", "holiday.jpg");
        await TidyAllAsync(first, await PreviewAsync(first, rootId));
        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();

        var last = await run.FindLastAsync(rootId, TestContext.Current.CancellationToken);

        Assert.NotNull(last);
        Assert.Equal(2, last.Moved);
        Assert.Equal(2, last.FoldersUsed);
        var undo = await run.UndoAsync(rootId, last.TransactionId, last.MovedFiles, TestContext.Current.CancellationToken);
        Assert.Equal(2, undo.Restored);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "holiday.jpg")));
        Assert.Null(await run.FindLastAsync(rootId, TestContext.Current.CancellationToken));

        await using var third = await app.ReopenAsync();
        Assert.Null(await third.Get<TidyRunService>().FindLastAsync(rootId, TestContext.Current.CancellationToken));
        var again = await third.Get<TidyRunService>().UndoAsync(rootId, last.TransactionId, last.MovedFiles, TestContext.Current.CancellationToken);
        Assert.False(again.Finished);
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task Only_the_latest_tidy_is_offered_never_an_older_one_behind_it()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "invoice.pdf");
        await TidyAllAsync(app, await PreviewAsync(app, rootId));
        app.MakeFile("Downloads", "notes.pdf");
        var second = await TidyAllAsync(app, await PreviewAsync(app, rootId));
        await app.Get<TidyRunService>().UndoAsync(rootId, second.TransactionId!.Value, second.MovedFiles, TestContext.Current.CancellationToken);

        Assert.Null(await app.Get<TidyRunService>().FindLastAsync(rootId, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));
    }

    [Fact]
    public async Task A_tidy_that_moved_nothing_does_not_hide_the_one_before_it()
    {
        await using var app = await TestApp.StartAsync();
        var (folder, rootId, _) = await SetUpAsync(app, "invoice.pdf");
        var first = await TidyAllAsync(app, await PreviewAsync(app, rootId));
        app.MakeFile("Downloads", "notes.pdf");
        var preview = await PreviewAsync(app, rootId);
        File.AppendAllText(Path.Combine(folder, "notes.pdf"), " changed after the list");
        var nothing = await TidyAllAsync(app, preview);
        Assert.Equal(0, nothing.Moved);

        var last = await app.Get<TidyRunService>().FindLastAsync(rootId, TestContext.Current.CancellationToken);

        Assert.Equal(first.TransactionId, last?.TransactionId);
    }

    [Fact]
    public async Task A_move_that_finished_just_before_DeskAI_stopped_is_proved_and_counted()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(first, "a.pdf", "b.pdf", "c.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        await using var app = await first.ReopenAsync();

        var interrupted = await app.Get<TidyRunService>().FindInterruptedAsync(rootId, TestContext.Current.CancellationToken);

        Assert.NotNull(interrupted);
        Assert.False(interrupted.IsUndo);
        Assert.Equal(2, interrupted.Moved);
        Assert.Equal(3, interrupted.Total);
        Assert.Empty(interrupted.NeedsReview);
        Assert.True(interrupted.CanUndo);
        Assert.True(File.Exists(Path.Combine(folder, "c.pdf")));
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task A_move_that_had_not_happened_yet_is_proved_not_moved()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf", "c.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopAfter(MoveId(preview, "b.pdf"), JournalOperationState.InProgress);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        await using var app = await first.ReopenAsync();

        var interrupted = await app.Get<TidyRunService>().FindInterruptedAsync(rootId, TestContext.Current.CancellationToken);

        Assert.Equal(1, interrupted!.Moved);
        Assert.Empty(interrupted.NeedsReview);
        Assert.True(File.Exists(Path.Combine(folder, "b.pdf")));
    }

    [Fact]
    public async Task Undo_those_puts_back_exactly_the_files_that_moved_and_removes_the_folder_it_made()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(first, "a.pdf", "b.pdf", "c.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();
        var interrupted = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;

        var undo = await run.UndoInterruptedAsync(rootId, interrupted, TestContext.Current.CancellationToken);

        Assert.True(undo.Finished);
        Assert.Equal(2, undo.Restored);
        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "b.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "c.pdf")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Documents")));
        Assert.Null(await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken));
        Assert.Null(await run.FindLastAsync(rootId, TestContext.Current.CancellationToken));
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task A_file_changed_after_DeskAI_stopped_needs_review_and_is_never_moved_back()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        File.AppendAllText(Path.Combine(folder, "Documents", "b.pdf"), " edited after the crash");
        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();

        var interrupted = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;

        Assert.Equal(1, interrupted.Moved);
        var review = Assert.Single(interrupted.NeedsReview);
        Assert.Equal("b.pdf", review.FileName);
        Assert.Contains("Look for it in Documents", review.Reason, StringComparison.Ordinal);

        var undo = await run.UndoInterruptedAsync(rootId, interrupted, TestContext.Current.CancellationToken);

        Assert.Equal(1, undo.Restored);
        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "b.pdf")));
        Assert.False(File.Exists(Path.Combine(folder, "b.pdf")));
    }

    [Fact]
    public async Task Keep_them_makes_the_interrupted_tidy_the_last_tidy_which_can_still_be_undone()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf", "c.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();
        var interrupted = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;

        Assert.Null(await run.KeepInterruptedAsync(rootId, interrupted.TransactionId, TestContext.Current.CancellationToken));

        Assert.Null(await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken));
        var last = await run.FindLastAsync(rootId, TestContext.Current.CancellationToken);
        Assert.Equal(interrupted.TransactionId, last?.TransactionId);
        Assert.Equal(2, last!.Moved);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "b.pdf")));
    }

    [Fact]
    public async Task A_tidy_stopped_before_anything_moved_has_nothing_to_undo()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "a.pdf"), JournalOperationState.InProgress);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();

        var interrupted = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;

        Assert.Equal(0, interrupted.Moved);
        Assert.False(interrupted.CanUndo);
        Assert.Null(await run.KeepInterruptedAsync(rootId, interrupted.TransactionId, TestContext.Current.CancellationToken));
        Assert.Null(await run.FindLastAsync(rootId, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
    }

    [Fact]
    public async Task No_new_tidy_or_undo_while_a_question_about_the_last_one_is_open()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf");
        var earlier = await TidyAllAsync(first, await PreviewAsync(first, rootId));
        first.MakeFile("Downloads", "c.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "c.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        first.MakeFile("Downloads", "d.pdf");
        await using var app = await first.ReopenAsync();

        // Not yet checked, and then checked: refused either way.
        var refused = await TidyAllAsync(app, await PreviewAsync(app, rootId));
        await app.Get<TidyRunService>().FindInterruptedAsync(rootId, TestContext.Current.CancellationToken);
        var stillRefused = await TidyAllAsync(app, await PreviewAsync(app, rootId));
        var undo = await app.Get<TidyRunService>().UndoAsync(
            rootId, earlier.TransactionId!.Value, earlier.MovedFiles, TestContext.Current.CancellationToken);

        Assert.Equal(0, refused.Moved);
        Assert.Contains("Answer the question", refused.Skipped[0].Reason, StringComparison.Ordinal);
        Assert.Equal(0, stillRefused.Moved);
        Assert.False(undo.Finished);
        Assert.True(File.Exists(Path.Combine(folder, "d.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "a.pdf")));
    }

    [Fact]
    public async Task Undo_those_without_the_tidy_permission_asks_first_and_leaves_the_question_open()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        await using var app = await first.ReopenAsync();
        await app.Get<TidyPermissionService>().StopAsync(rootId, TestContext.Current.CancellationToken);
        var run = app.Get<TidyRunService>();

        // Checking reads only names, sizes, and dates, so it works without the tidy permission.
        var interrupted = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;
        var refused = await run.UndoInterruptedAsync(rootId, interrupted, TestContext.Current.CancellationToken);

        Assert.True(refused.NeedsPermission);
        Assert.NotNull(await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "b.pdf")));
    }

    [Fact]
    public async Task Checking_twice_changes_nothing()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();
        var once = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;

        // What was checked stays checked; the disk changing afterwards is undo's to judge.
        File.Move(Path.Combine(folder, "Documents", "b.pdf"), Path.Combine(folder, "b.pdf"));
        var twice = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;

        Assert.Equal(once.TransactionId, twice.TransactionId);
        Assert.Equal(once.Moved, twice.Moved);
        Assert.Equal(2, twice.Moved);
    }

    [Fact]
    public async Task A_folder_that_is_not_where_it_was_is_not_checked_and_its_record_is_kept()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        var moved = folder + "-moved";
        Directory.Move(folder, moved);
        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();

        Assert.Null(await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken));
        var record = Assert.Single(await app.Get<IOperationJournal>().ListForRootAsync(rootId, 10, TestContext.Current.CancellationToken));
        Assert.Equal(ExecutionTransactionState.Executing, record.State);

        Directory.Move(moved, folder);
        Assert.NotNull(await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_link_put_on_the_way_after_the_crash_makes_the_file_needs_review_and_nothing_goes_through_it()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, sentinel) = await SetUpAsync(first, "a.pdf", "b.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        var outside = first.Directory.CreateDummyDirectory("outside-target");
        Directory.Move(Path.Combine(folder, "Documents"), Path.Combine(outside, "Documents"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(folder, "Documents"), Path.Combine(outside, "Documents"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();
        var interrupted = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;

        Assert.Equal("b.pdf", Assert.Single(interrupted.NeedsReview).FileName);
        var undo = await run.UndoInterruptedAsync(rootId, interrupted, TestContext.Current.CancellationToken);

        Assert.Equal(0, undo.Restored);
        Assert.True(File.Exists(Path.Combine(outside, "Documents", "a.pdf")));
        Assert.True(File.Exists(Path.Combine(outside, "Documents", "b.pdf")));
        Assert.False(File.Exists(Path.Combine(folder, "a.pdf")));
        AssertSentinel(sentinel);
    }

    [Fact]
    public async Task An_interrupted_undo_is_reported_closed_and_never_offered_again()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf");
        var preview = await PreviewAsync(first, rootId);
        var tidy = await TidyAllAsync(first, preview);
        first.Stopping.StopBefore(MoveId(preview, "a.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() =>
            first.Get<TidyRunService>().UndoAsync(rootId, tidy.TransactionId!.Value, tidy.MovedFiles, TestContext.Current.CancellationToken));
        await using var app = await first.ReopenAsync();
        var run = app.Get<TidyRunService>();

        var interrupted = (await run.FindInterruptedAsync(rootId, TestContext.Current.CancellationToken))!;

        // Undo goes in reverse, so b went back first and a was on its way when DeskAI stopped.
        Assert.True(interrupted.IsUndo);
        Assert.False(interrupted.CanUndo);
        Assert.Equal(2, interrupted.Moved);
        Assert.Null(await run.KeepInterruptedAsync(rootId, interrupted.TransactionId, TestContext.Current.CancellationToken));
        Assert.Null(await run.FindLastAsync(rootId, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(folder, "a.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "b.pdf")));
    }

    [Fact]
    public async Task A_record_cannot_be_checked_or_closed_from_another_folder_or_the_practice_workspace()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var (folder, rootId, _) = await SetUpAsync(first, "a.pdf", "b.pdf");
        var preview = await PreviewAsync(first, rootId);
        first.Stopping.StopBefore(MoveId(preview, "b.pdf"), JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => TidyAllAsync(first, preview));
        await using var app = await first.ReopenAsync();
        var otherId = await TidySuggestionTests.ConnectAndAllowAsync(app, app.MakeFolder("Desktop", "x.pdf"));
        var demo = app.Get<TemporaryDemoPlanExecutor>();
        await demo.PrepareAsync(TestContext.Current.CancellationToken);
        await app.Get<IAuthorizedRootRepository>().SaveAsync(demo.Root, TestContext.Current.CancellationToken);
        var executor = app.Get<IFolderTidyExecutor>();

        Assert.Empty(await executor.CheckInterruptedAsync(otherId, TestContext.Current.CancellationToken));
        Assert.Empty(await executor.CheckInterruptedAsync(demo.Root.Id, TestContext.Current.CancellationToken));
        var record = Assert.Single(await executor.CheckInterruptedAsync(rootId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.CloseInterruptedAsync(otherId, record.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.CloseInterruptedAsync(demo.Root.Id, record.Id, TestContext.Current.CancellationToken));

        // And practice recovery still leaves the checked record exactly as it is.
        await demo.RecoverIncompleteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionTransactionState.RecoveryRequired,
            (await app.Get<IOperationJournal>().FindAsync(record.Id, TestContext.Current.CancellationToken))!.State);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "b.pdf")));
    }

    private static async Task<(string Folder, Guid RootId, string Sentinel)> SetUpAsync(TestApp app, params string[] files)
    {
        var folder = app.MakeFolder("Downloads", files);
        var sentinel = app.MakeFile("Sentinel", "do-not-touch.pdf", "Sentinel content");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        return (folder, rootId, sentinel);
    }

    private static async Task<TidyPreview> PreviewAsync(TestApp app, Guid rootId) =>
        (await app.Get<TidySuggestionService>().PreviewAsync(
            rootId, Guid.NewGuid(), 1, new Dictionary<Guid, SameNameChoice>(), TidySuggestionMode.TypesAndRules,
            new Dictionary<Guid, TidyAiAdvice>(), TestContext.Current.CancellationToken))!;

    private static Guid MoveId(TidyPreview preview, string name) =>
        preview.Suggestions.Single(item => item.FileName == name).MoveOperationId!.Value;

    private static Task<TidyRunResult> TidyAllAsync(TestApp app, TidyPreview preview) =>
        app.Get<TidyRunService>().TidyAsync(
            preview,
            [.. preview.Suggestions.Where(item => item.MoveOperationId is not null).Select(item => item.MoveOperationId!.Value)],
            TestContext.Current.CancellationToken);

    private static void AssertSentinel(string sentinel) =>
        Assert.Equal("Sentinel content", File.ReadAllText(sentinel));
}
