using DeskAI.App.ViewModels;
using DeskAI.Core.Search;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Confirming copies by reading generated files, with the whole app built as it runs. These are
/// the negative tests the review (<c>docs/security/2026-09-11-duplicate-confirmation-review.md</c>)
/// lists. Every test checks a sentinel file beside the folders, which nothing may touch.
/// </summary>
public sealed class DuplicateCheckTests
{
    private static readonly string Five = new('a', 5000);

    [Fact]
    public async Task Preparing_opens_no_file_even_one_locked_by_another_program()
    {
        await using var app = await TestApp.StartAsync();
        var folder = await ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));

        DuplicateCheckQuestion question;
        using (new FileStream(Path.Combine(folder, "a.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            question = await app.Get<DuplicateCheckService>().PrepareAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, question.Files.Count);
        Assert.Equal(10_000, question.TotalBytes);
        Assert.Equal(1, question.FolderCount);
    }

    [Fact]
    public async Task Identical_files_are_found_and_a_same_size_different_file_is_not_a_copy()
    {
        await using var app = await TestApp.StartAsync();
        var sentinel = app.MakeFile("Sentinel", "do-not-touch.txt", "Sentinel content");
        await ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five), ("c.txt", new string('a', 4999) + "b"));
        await ConnectAsync(app, "Backup", ("a-copy.txt", Five));

        var result = await CompareAsync(app);

        var group = Assert.Single(result.Groups);
        Assert.Equal(["Backup / a-copy.txt", "Study / a.txt", "Study / b.txt"],
            Assert.Single(group.IdenticalSets).Select(file => file.DisplayName).Order());
        Assert.Equal("Study / c.txt", Assert.Single(group.Different).DisplayName);
        Assert.Equal(10_000, result.ReclaimableBytes);
        Assert.Equal("Sentinel content", File.ReadAllText(sentinel));
    }

    [Fact]
    public async Task Large_files_that_differ_only_at_the_end_are_read_whole_and_told_apart()
    {
        await using var app = await TestApp.StartAsync();
        var big = new string('x', 200_000);
        await ConnectAsync(app, "Study", ("one.bin", big), ("two.bin", big), ("three.bin", big[..^1] + "y"));

        var result = await CompareAsync(app);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.IdenticalCount);
        Assert.Equal("Study / three.bin", Assert.Single(group.Different).DisplayName);
        Assert.Equal(3 * 200_000, result.BytesRead - (3 * DuplicateCheckService.BeginningBytes));
    }

    [Fact]
    public async Task Files_that_differ_at_the_start_are_only_read_at_the_start()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Study", ("one.bin", "1" + new string('x', 199_999)), ("two.bin", "2" + new string('x', 199_999)));

        var result = await CompareAsync(app);

        Assert.Equal(2, Assert.Single(result.Groups).Different.Count);
        Assert.Equal(2 * DuplicateCheckService.BeginningBytes, result.BytesRead);
    }

    [Fact]
    public async Task Checking_changes_no_file_and_keeps_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var folder = await ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));
        var before = Directory.GetFiles(folder).ToDictionary(path => path, File.GetLastWriteTimeUtc);
        var databaseBefore = File.ReadAllBytes(Path.Combine(app.Directory.Path, "deskai.db"));

        await CompareAsync(app);

        Assert.All(before, pair => Assert.Equal(pair.Value, File.GetLastWriteTimeUtc(pair.Key)));
        Assert.Equal(Five, File.ReadAllText(Path.Combine(folder, "a.txt")));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.Equal(databaseBefore, File.ReadAllBytes(Path.Combine(app.Directory.Path, "deskai.db")));
    }

    [Fact]
    public async Task A_file_changed_since_DeskAI_last_looked_is_not_compared()
    {
        await using var app = await TestApp.StartAsync();
        var folder = await ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));
        var question = await app.Get<DuplicateCheckService>().PrepareAsync(TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(folder, "b.txt"), new string('z', 5000));

        var result = await app.Get<DuplicateCheckService>().CompareAsync(question, TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Empty(group.IdenticalSets);
        Assert.Contains("changed since DeskAI last looked", Assert.Single(group.NotCompared).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_open_for_writing_elsewhere_is_not_compared_and_the_rest_are()
    {
        await using var app = await TestApp.StartAsync();
        var folder = await ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five), ("c.txt", Five));
        var question = await app.Get<DuplicateCheckService>().PrepareAsync(TestContext.Current.CancellationToken);

        DuplicateCheckResult result;
        using (new FileStream(Path.Combine(folder, "c.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            result = await app.Get<DuplicateCheckService>().CompareAsync(question, TestContext.Current.CancellationToken);
        }

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.IdenticalCount);
        Assert.Contains("open in another program", Assert.Single(group.NotCompared).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_online_only_file_is_not_read()
    {
        await using var app = await TestApp.StartAsync();
        var folder = await ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));
        var path = Path.Combine(folder, "b.txt");
        File.SetAttributes(path, FileAttributes.Offline);
        try
        {
            var result = await CompareAsync(app);

            Assert.Contains("online only", Assert.Single(Assert.Single(result.Groups).NotCompared).Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task A_file_replaced_by_a_link_is_not_followed()
    {
        await using var app = await TestApp.StartAsync();
        var folder = await ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));
        var outside = app.Directory.CreateDummyFile(@"outside\b.txt", Five);
        var question = await app.Get<DuplicateCheckService>().PrepareAsync(TestContext.Current.CancellationToken);
        var path = Path.Combine(folder, "b.txt");
        File.Delete(path);
        try
        {
            File.CreateSymbolicLink(path, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        var result = await app.Get<DuplicateCheckService>().CompareAsync(question, TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Empty(group.IdenticalSets);
        Assert.Contains(group.NotCompared, item => item.File.RelativePath == "b.txt");
    }

    [Fact]
    public async Task A_folder_disconnected_after_the_question_is_not_read()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Study", ("a.txt", Five));
        await ConnectAsync(app, "Backup", ("a.txt", Five));
        var question = await app.Get<DuplicateCheckService>().PrepareAsync(TestContext.Current.CancellationToken);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.DisconnectFolderCommand.ExecuteAsync(search.Folders.Single(folder => folder.Name == "Backup").Id);

        var result = await app.Get<DuplicateCheckService>().CompareAsync(question, TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Equal("Backup / a.txt", Assert.Single(group.NotCompared).File.DisplayName);
        Assert.Contains("no longer connected", group.NotCompared[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_added_after_the_question_is_not_read()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Study", ("a.txt", Five), ("b.txt", Five));
        var question = await app.Get<DuplicateCheckService>().PrepareAsync(TestContext.Current.CancellationToken);
        app.MakeFile("Study", "c.txt", Five);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.RefreshFolderCommand.ExecuteAsync(search.Folders.Single().Id);

        var result = await app.Get<DuplicateCheckService>().CompareAsync(question, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result.Groups.SelectMany(group => group.IdenticalSets.SelectMany(set => set)), file => file.RelativePath == "c.txt");
        Assert.Equal(2, result.FilesOpened);
    }

    internal static async Task<string> ConnectAsync(TestApp app, string name, params (string Name, string Content)[] files)
    {
        var folder = app.MakeFolder(name);
        foreach (var (file, content) in files)
        {
            app.MakeFile(name, file, content);
        }

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        return folder;
    }

    private static async Task<DuplicateCheckResult> CompareAsync(TestApp app)
    {
        var service = app.Get<DuplicateCheckService>();
        return await service.CompareAsync(await service.PrepareAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
    }
}
