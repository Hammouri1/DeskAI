using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// "Check if they're really copies" on Home, the way a person uses it: read the dialog, press
/// Compare or Cancel, and read the result. The dialog is a WinUI object checked by hand; these
/// drive the two view-model calls on either side of it.
/// </summary>
public sealed class CopyCheckPageTests
{
    private static readonly string Five = new('a', 5000);

    [Fact]
    public async Task The_dialog_says_how_many_files_how_much_and_that_nothing_is_kept_sent_or_changed()
    {
        await using var app = await TestApp.StartAsync();
        await DuplicateCheckTests.ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));
        await DuplicateCheckTests.ConnectAsync(app, "Backup", ("a.txt", Five));
        var home = await OpenHomeAsync(app);

        Assert.True(home.CanCheckCopies);
        var question = await home.PrepareCopyCheckAsync();

        Assert.NotNull(question);
        Assert.Equal("Compare 3 files?", question.Title);
        Assert.Contains("read 3 files (14.6 KB) in 2 folders from beginning to end, on this computer", question.Body, StringComparison.Ordinal);
        Assert.Contains("Nothing it reads is saved or sent anywhere", question.Body, StringComparison.Ordinal);
        Assert.Contains("does not change, move, or delete anything", question.Body, StringComparison.Ordinal);
        Assert.False(home.HasCopyCheckSummary);
    }

    [Fact]
    public async Task Pressing_Compare_shows_which_are_copies_and_what_was_read()
    {
        await using var app = await TestApp.StartAsync();
        await DuplicateCheckTests.ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five), ("c.txt", new string('a', 4999) + "b"));
        var home = await OpenHomeAsync(app);

        await home.CompareCopiesAsync((await home.PrepareCopyCheckAsync())!);

        Assert.False(home.IsCheckingCopies);
        Assert.StartsWith("2 files are identical copies. Keeping one of each would free up to 4.9 KB.", home.CopyCheckSummary, StringComparison.Ordinal);
        Assert.Contains("DeskAI read 3 files", home.CopyCheckSummary, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was saved, sent, or changed.", home.CopyCheckSummary, StringComparison.Ordinal);
        var group = Assert.Single(home.CheckedCopyGroups);
        Assert.Equal("3 files of 4.9 KB", group.Headline);
        Assert.Equal("Some identical", group.Verdict);
        Assert.Contains("Identical: Study / a.txt, Study / b.txt", group.Lines);
        Assert.Contains("Not a copy of the others: Study / c.txt", group.Lines);
    }

    [Fact]
    public async Task Files_that_only_share_a_size_are_said_to_be_different()
    {
        await using var app = await TestApp.StartAsync();
        await DuplicateCheckTests.ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", new string('b', 5000)));
        var home = await OpenHomeAsync(app);

        await home.CompareCopiesAsync((await home.PrepareCopyCheckAsync())!);

        Assert.StartsWith("None of them are identical copies.", home.CopyCheckSummary, StringComparison.Ordinal);
        Assert.Equal("Same size, different contents", Assert.Single(home.CheckedCopyGroups).Verdict);
    }

    [Fact]
    public async Task A_file_that_could_not_be_checked_is_listed_with_its_reason()
    {
        await using var app = await TestApp.StartAsync();
        var folder = await DuplicateCheckTests.ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five), ("c.txt", Five));
        var home = await OpenHomeAsync(app);
        var question = (await home.PrepareCopyCheckAsync())!;

        using (new FileStream(Path.Combine(folder, "c.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            await home.CompareCopiesAsync(question);
        }

        Assert.Contains("1 could not be checked; each says why.", home.CopyCheckSummary, StringComparison.Ordinal);
        Assert.Contains(
            "Not checked: Study / c.txt. It's open in another program, so it was not read.",
            Assert.Single(home.CheckedCopyGroups).Lines);
    }

    [Fact]
    public async Task Cancel_in_the_dialog_reads_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var folder = await DuplicateCheckTests.ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));
        var home = await OpenHomeAsync(app);

        // The dialog is cancelled, so Compare is never called. A lock no reader could get past
        // proves preparing did not open the file either.
        using (new FileStream(Path.Combine(folder, "a.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.NotNull(await home.PrepareCopyCheckAsync());
        }

        Assert.Empty(home.CheckedCopyGroups);
        Assert.False(home.HasCopyCheckSummary);
    }

    [Fact]
    public async Task With_no_possible_copies_there_is_nothing_to_check()
    {
        await using var app = await TestApp.StartAsync();
        await DuplicateCheckTests.ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", new string('b', 6000)));
        var home = await OpenHomeAsync(app);

        Assert.False(home.CanCheckCopies);
        Assert.Null(await home.PrepareCopyCheckAsync());
        Assert.Equal("There are no possible copies to compare right now.", home.CopyCheckSummary);
    }

    /// <summary>
    /// Stop is offered only while a check runs, and the button to start one is off meanwhile.
    /// What Stop does to a running check is proved in <c>DuplicateCheckServiceTests</c>, where
    /// the moment it is pressed can be controlled.
    /// </summary>
    [Fact]
    public async Task Stop_is_offered_only_while_a_check_runs()
    {
        await using var app = await TestApp.StartAsync();
        await DuplicateCheckTests.ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));
        var home = await OpenHomeAsync(app);

        Assert.False(home.StopCopyCheckCommand.CanExecute(null));
        await home.CompareCopiesAsync((await home.PrepareCopyCheckAsync())!);

        Assert.False(home.StopCopyCheckCommand.CanExecute(null));
        Assert.True(home.CanCheckCopies);
    }

    private static async Task<DashboardViewModel> OpenHomeAsync(TestApp app)
    {
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        return home;
    }
}
