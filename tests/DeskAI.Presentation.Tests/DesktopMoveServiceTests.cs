using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Core.Studio;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Desktop Studio moving service over the real stack (ADR 0044), on a generated Desktop inside
/// the test's own folder. The page tests use the same service through the page.
/// </summary>
public sealed class DesktopMoveServiceTests
{
    private static readonly TimeSpan SevenMonths = TimeSpan.FromDays(213);

    [Fact]
    public async Task Moving_needs_its_own_yes_and_that_yes_is_not_tidying()
    {
        await using var app = await TestApp.StartAsync();
        MakeOldDesktop(app);
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        var tidying = app.Get<TidyPermissionService>();
        await tidying.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        var preview = (await moves.PreviewAsync(desktop.Id, DesktopMoveCard.ClearOldStuff, TestContext.Current.CancellationToken)).Preview!;
        var all = preview.Items.Select(item => item.OperationId).ToList();

        var refused = await moves.ApplyAsync(preview, all, TestContext.Current.CancellationToken);

        Assert.True(refused.NeedsPermission);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old project")));

        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        await tidying.StopAsync(desktop.Id, TestContext.Current.CancellationToken);
        var moved = await moves.ApplyAsync(preview, all, TestContext.Current.CancellationToken);

        Assert.False(moved.NeedsPermission);
        Assert.Equal(2, moved.Moved);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old stuff", "Old project")));
        var root = await app.Get<IAuthorizedRootRepository>().FindAsync(desktop.Id, TestContext.Current.CancellationToken);
        Assert.False(RootCapabilities.CanTidy(root!));
        Assert.True(RootCapabilities.CanMoveFolders(root!));
    }

    [Fact]
    public async Task Put_back_is_offered_only_for_the_latest_change_on_the_Desktop()
    {
        await using var app = await TestApp.StartAsync();
        MakeOldDesktop(app);
        app.MakeFile("Desktop", "report.docx");
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        await ApplyAllAsync(moves, desktop.Id, DesktopMoveCard.ClearOldStuff);

        Assert.NotNull(await moves.FindLastAsync(desktop.Id, DesktopMoveCard.ClearOldStuff, TestContext.Current.CancellationToken));
        Assert.Null(await moves.FindLastAsync(desktop.Id, DesktopMoveCard.FolderByGroup, TestContext.Current.CancellationToken));

        await app.Get<DesktopGroupingService>().GuessAsync(desktop.Id, TestContext.Current.CancellationToken);
        await ApplyAllAsync(moves, desktop.Id, DesktopMoveCard.FolderByGroup);

        Assert.Null(await moves.FindLastAsync(desktop.Id, DesktopMoveCard.ClearOldStuff, TestContext.Current.CancellationToken));
        Assert.NotNull(await moves.FindLastAsync(desktop.Id, DesktopMoveCard.FolderByGroup, TestContext.Current.CancellationToken));
        var refused = await moves.PutBackAsync(desktop.Id, DesktopMoveCard.ClearOldStuff, TestContext.Current.CancellationToken);
        Assert.Equal("There is nothing to put back.", refused.Summary);
    }

    [Fact]
    public async Task Organize_offers_no_undo_for_a_Desktop_Studio_change()
    {
        await using var app = await TestApp.StartAsync();
        MakeOldDesktop(app);
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);

        await ApplyAllAsync(moves, desktop.Id, DesktopMoveCard.ClearOldStuff);

        Assert.Null(await app.Get<TidyRunService>().FindLastAsync(desktop.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Folder_by_group_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true, shareFolderNames: true);
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        app.MakeFile("Desktop", "report.docx");
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        await app.Get<DesktopGroupingService>().GuessAsync(desktop.Id, TestContext.Current.CancellationToken);

        var result = await ApplyAllAsync(moves, desktop.Id, DesktopMoveCard.FolderByGroup);

        Assert.Equal(2, result.Moved);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Tag_names_needs_the_same_yes_and_Put_back_restores_names()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        await app.Get<DesktopGroupingService>().GuessAsync(desktop.Id, TestContext.Current.CancellationToken);
        var preview = (await moves.PreviewAsync(desktop.Id, DesktopMoveCard.TagNames, TestContext.Current.CancellationToken)).Preview!;
        var all = preview.Items.Select(item => item.OperationId).ToList();

        Assert.True((await moves.ApplyAsync(preview, all, TestContext.Current.CancellationToken)).NeedsPermission);
        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        var done = await moves.ApplyAsync(preview, all, TestContext.Current.CancellationToken);

        Assert.Equal("Done. 1 folder renamed.", done.Summary);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Coding – Python stuff")));
        var back = await moves.PutBackAsync(desktop.Id, DesktopMoveCard.TagNames, TestContext.Current.CancellationToken);
        Assert.Equal(1, back.Moved);
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Python stuff", "main.py")));
    }

    [Fact]
    public async Task Tag_names_before_Find_groups_asks_for_groups_in_its_own_words()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        var desktop = await ConnectDesktopAsync(app);

        var result = await app.Get<DesktopMoveService>().PreviewAsync(desktop.Id, DesktopMoveCard.TagNames, TestContext.Current.CancellationToken);

        Assert.Null(result.Preview);
        Assert.Equal("Find groups first, then DeskAI can add each group's name to its folders.", result.Message);
    }

    internal static void MakeOldDesktop(TestApp app)
    {
        app.MakeFile("Desktop", Path.Combine("Old project", "main.py"), age: SevenMonths);
        app.MakeFile("Desktop", Path.Combine("Old project", "notes.md"), age: SevenMonths);
        Age(app, "Old project");
        app.MakeFile("Desktop", "old notes.txt", age: SevenMonths);
        app.MakeFile("Desktop", Path.Combine("Current work", "draft.docx"));
        Age(app, "Current work");
        app.MakeFile("Desktop", "this week.pdf");
    }

    /// <summary>Makes a generated folder's own last-changed time old, as it is for a folder nobody touched.</summary>
    internal static void Age(TestApp app, string folder) =>
        Directory.SetLastWriteTimeUtc(Path.Combine(app.DesktopPath, folder), DateTime.UtcNow - SevenMonths);

    internal static async Task<AuthorizedRoot> ConnectDesktopAsync(TestApp app)
    {
        var folders = app.Get<PersonalFoldersViewModel>();
        await folders.ReloadAsync();
        Assert.NotNull(await folders.ConnectAsync(PersonalFolderKind.Desktop));
        return (await app.Get<DesktopGroupingService>().FindDesktopAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<DesktopMoveResult> ApplyAllAsync(DesktopMoveService moves, Guid desktopId, DesktopMoveCard card)
    {
        var preview = (await moves.PreviewAsync(desktopId, card, TestContext.Current.CancellationToken)).Preview!;
        return await moves.ApplyAsync(preview, preview.Items.Select(item => item.OperationId).ToList(), TestContext.Current.CancellationToken);
    }
}
