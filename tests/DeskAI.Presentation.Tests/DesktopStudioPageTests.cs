using System.Net;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.App.ViewModels;
using DeskAI.Core.Roots;
using DeskAI.Core.Studio;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Desktop Studio's Find groups card (ADR 0042), used the way a person uses it, on a generated
/// Desktop inside the test's own folder. Nothing on that Desktop may change.
/// </summary>
public sealed class DesktopStudioPageTests
{
    [Fact]
    public async Task Without_a_connected_Desktop_the_page_offers_Connect_and_nothing_else()
    {
        await using var app = await TestApp.StartAsync();
        MakeDesktop(app);
        var studio = app.Get<DesktopStudioViewModel>();
        await studio.InitializeAsync();

        Assert.False(studio.IsDesktopConnected);
        Assert.False(studio.HasBoard);
        Assert.Null(await studio.PrepareAsync());
        await studio.GuessAsync();
        Assert.False(studio.HasBoard);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Connect_Desktop_connects_it_for_names_only_and_shows_the_card()
    {
        await using var app = await TestApp.StartAsync();
        MakeDesktop(app);
        var studio = app.Get<DesktopStudioViewModel>();
        await studio.InitializeAsync();

        await studio.ConnectDesktopAsync();

        Assert.True(studio.IsDesktopConnected);
        AssertDesktopUnchanged(app, Snapshot(app));
    }

    [Fact]
    public async Task With_AI_off_DeskAIs_guess_fills_the_board_and_nothing_is_sent_or_moved()
    {
        await using var app = await TestApp.StartAsync();
        MakeDesktop(app);
        app.MakeFile(Path.Combine("Desktop", "DeskAI", "app"), "DeskAI.App.exe");
        var before = Snapshot(app);
        var studio = await OpenWithDesktopAsync(app);

        Assert.False(studio.HasAi);
        await studio.GuessAsync();

        Assert.True(studio.HasBoard);
        Assert.Contains(studio.Groups, g => g.Name == "Coding" && g.Items.Any(i => i.Name == "Python stuff" && i.IsFolder));
        Assert.Equal("Grouped by DeskAI's own simpler guess.", studio.SourceNote);
        Assert.DoesNotContain(studio.Groups.SelectMany(g => g.Items).Concat(studio.NotSure), i => i.Name == "DeskAI");
        Assert.Empty(app.Internet.Requests);
        AssertDesktopUnchanged(app, before);
    }

    [Fact]
    public async Task Send_sends_exactly_the_lines_shown_and_the_answer_becomes_the_board()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true, shareFolderNames: true);
        MakeDesktop(app);
        var before = Snapshot(app);
        var studio = await OpenWithDesktopAsync(app);
        app.Internet.Reply = _ => Envelope("""{"schemaVersion":"1","groups":[{"name":"Coding","items":[2]},{"name":"School","items":[1]}]}""");

        Assert.True(studio.HasAi);
        Assert.Equal("Find groups with OpenRouter", studio.SendButtonText);
        var question = await studio.PrepareAsync();
        Assert.Empty(app.Internet.Requests);
        Assert.Equal(
            ["Folder \"Essays\": 1 .docx; essay.docx", "Folder \"Python stuff\": 2 .py; main.py, utils.py", "File \"holiday.jpg\"", "File \"report.docx\""],
            question!.Lines);

        await studio.SendAsync(question);

        var body = Assert.Single(app.Internet.Requests).Body;
        foreach (var shown in new[] { "Essays", "essay.docx", "Python stuff", "main.py", "utils.py", "holiday.jpg", "report.docx" })
        {
            Assert.Contains(shown, body, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(app.DesktopPath.Replace("\\", "\\\\", StringComparison.Ordinal), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(question.Request.RequestId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["Coding", "School"], studio.Groups.Select(g => g.Name));
        Assert.Equal(["holiday.jpg", "report.docx"], studio.NotSure.Select(i => i.Name));
        Assert.Equal("Grouped by OpenRouter.", studio.SourceNote);
        AssertDesktopUnchanged(app, before);
    }

    [Fact]
    public async Task Online_AI_without_folder_name_sharing_sends_nothing_and_says_what_to_allow()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true, shareFolderNames: false);
        MakeDesktop(app);
        var studio = await OpenWithDesktopAsync(app);

        Assert.Null(await studio.PrepareAsync());
        Assert.Contains("folder names", studio.Message, StringComparison.Ordinal);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task A_reply_outside_the_shape_is_refused_and_the_board_stays_as_it_was()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true, shareFolderNames: true);
        MakeDesktop(app);
        var before = Snapshot(app);
        var studio = await OpenWithDesktopAsync(app);
        await studio.GuessAsync();
        var groupsBefore = studio.Groups.Select(g => g.Name).ToArray();
        app.Internet.Reply = _ => Envelope("""{"schemaVersion":"1","groups":[{"name":"..\\Windows","items":[1]}]}""");

        await studio.SendAsync((await studio.PrepareAsync())!);

        Assert.Single(app.Internet.Requests);
        Assert.Equal(groupsBefore, studio.Groups.Select(g => g.Name));
        Assert.Equal("Grouped by DeskAI's own simpler guess.", studio.SourceNote);
        Assert.Contains("ignored", studio.Message, StringComparison.Ordinal);
        AssertDesktopUnchanged(app, before);
    }

    [Fact]
    public async Task Rename_merge_and_move_change_the_board_and_survive_reopening()
    {
        await using var first = await TestApp.StartAsync();
        MakeDesktop(first);
        var before = Snapshot(first);
        var studio = await OpenWithDesktopAsync(first);
        await studio.GuessAsync();

        await studio.RenameGroupAsync("Coding", "Programming");
        await studio.MoveItemAsync("report.docx", "Programming");
        await studio.MergeGroupAsync("Pictures", "Programming");
        await studio.RenameGroupAsync("Documents", "a/b");
        Assert.Contains("can't", studio.Message, StringComparison.Ordinal);

        await using var app = await first.ReopenAsync();
        var reopened = app.Get<DesktopStudioViewModel>();
        await reopened.InitializeAsync();

        Assert.Equal(["Documents", "Programming"], reopened.Groups.Select(g => g.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["Python stuff", "report.docx", "holiday.jpg"], reopened.Groups.Single(g => g.Name == "Programming").Items.Select(i => i.Name));
        Assert.Equal(["Documents", "Programming"], reopened.GroupNames.Order(StringComparer.Ordinal));
        AssertDesktopUnchanged(app, before);
    }

    [Fact]
    public async Task A_folder_deleted_since_is_gone_from_the_board_next_time()
    {
        await using var app = await TestApp.StartAsync();
        MakeDesktop(app);
        var studio = await OpenWithDesktopAsync(app);
        await studio.GuessAsync();
        Assert.Contains(studio.Groups.SelectMany(g => g.Items), i => i.Name == "Essays");

        // A generated folder inside the test's own temporary Desktop.
        Directory.Delete(Path.Combine(app.DesktopPath, "Essays"), recursive: true);
        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();

        Assert.DoesNotContain(again.Groups.SelectMany(g => g.Items).Concat(again.NotSure), i => i.Name == "Essays");
        Assert.Contains("no longer on your Desktop", again.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Found in review 2026-09-24: one big folder (a code project) made the whole card fail with
    /// a wrong safety message. The top level is still known, so the card works and says the look
    /// inside was partial.
    /// </summary>
    [Fact]
    public async Task A_folder_too_big_to_look_inside_fully_still_lets_the_Desktop_be_sorted()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true, shareFolderNames: true);
        MakeDesktop(app);
        var big = app.Directory.CreateDummyDirectory(Path.Combine("folders", "Desktop", "Big project"));
        for (var index = 0; index < DesktopLookService.Bounds.MaxEntries + 10; index++)
        {
            File.WriteAllText(Path.Combine(big, $"part{index:D5}.cs"), string.Empty);
        }

        var studio = await OpenWithDesktopAsync(app);

        var question = await studio.PrepareAsync();
        Assert.NotNull(question);
        Assert.Contains(question.Lines, line => line.StartsWith("Folder \"Big project\"", StringComparison.Ordinal));
        await studio.GuessAsync();
        Assert.True(studio.HasBoard);
        Assert.Contains("too full to look all the way inside", studio.Message, StringComparison.Ordinal);
        Assert.Empty(app.Internet.Requests);
    }

    /// <summary>
    /// Found in review 2026-09-24: long non-English names could make the request bigger than its
    /// safety limit, so Send refused after the person had approved the list. Prepare now keeps
    /// the list within the limit and DeskAI's own guess sorts the rest, never sent.
    /// </summary>
    [Fact]
    public async Task Long_names_in_any_language_never_make_Send_refuse_what_was_shown()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true, shareFolderNames: true);
        var longName = string.Concat(Enumerable.Repeat("ملف", 30));
        for (var index = 0; index < DesktopLookService.MaxFiles; index++)
        {
            app.MakeFile("Desktop", $"{longName}{index:D3}.txt");
        }

        var studio = await OpenWithDesktopAsync(app);
        app.Internet.Reply = _ => Envelope("""{"schemaVersion":"1","groups":[{"name":"Notes","items":[1]}]}""");

        var question = await studio.PrepareAsync();
        await studio.SendAsync(question!);

        var sent = Assert.Single(app.Internet.Requests);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(sent.Body) <= question!.Request.Limits.MaximumRequestBytes);
        Assert.Equal(question.Request.Items.Count, question.Lines.Count);
        Assert.True(question.LeftOut > 0);
        Assert.DoesNotContain(question.LeftOutItems[0].Name, sent.Body.Replace("\\\\", "\\", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("more were sorted by DeskAI's own guess", studio.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_Desktop_says_there_is_nothing_to_sort()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true, shareFolderNames: true);
        app.MakeFile(Path.Combine("Desktop", "DeskAI", "app"), "DeskAI.App.exe");
        var studio = await OpenWithDesktopAsync(app);

        Assert.Null(await studio.PrepareAsync());
        Assert.Equal("There is nothing on your Desktop to sort.", studio.Message);
        await studio.GuessAsync();
        Assert.Equal("There is nothing on your Desktop to sort.", studio.Message);
        Assert.False(studio.HasBoard);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Disconnecting_the_Desktop_forgets_the_board()
    {
        await using var app = await TestApp.StartAsync();
        MakeDesktop(app);
        var studio = await OpenWithDesktopAsync(app);
        await studio.GuessAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        await search.DisconnectFolderCommand.ExecuteAsync(Assert.Single(search.Folders).Id);
        var reconnected = await OpenWithDesktopAsync(app);

        Assert.True(reconnected.IsDesktopConnected);
        Assert.False(reconnected.HasBoard);
    }

    private static void MakeDesktop(TestApp app)
    {
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        app.MakeFile("Desktop", Path.Combine("Python stuff", "utils.py"));
        app.MakeFile("Desktop", Path.Combine("Essays", "essay.docx"));
        app.MakeFile("Desktop", "report.docx");
        app.MakeFile("Desktop", "holiday.jpg");
    }

    private static async Task<DesktopStudioViewModel> OpenWithDesktopAsync(TestApp app)
    {
        var folders = app.Get<PersonalFoldersViewModel>();
        await folders.ReloadAsync();
        Assert.NotNull(await folders.ConnectAsync(PersonalFolderKind.Desktop));
        var studio = app.Get<DesktopStudioViewModel>();
        await studio.InitializeAsync();
        Assert.True(studio.IsDesktopConnected);
        return studio;
    }

    private static string[] Snapshot(TestApp app)
    {
        Directory.CreateDirectory(app.DesktopPath);
        return Directory.EnumerateFileSystemEntries(app.DesktopPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(app.DesktopPath, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AssertDesktopUnchanged(TestApp app, string[] before) => Assert.Equal(before, Snapshot(app));

    private static AiHttpResponse Envelope(string content) =>
        new(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 },
        }));
}
