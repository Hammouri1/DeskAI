using DeskAI.App.ViewModels;
using DeskAI.Core.Execution;
using DeskAI.Core.Templates;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Folder templates on My workspace, used the way a person uses them: choose a folder, see the
/// list, make the folders, and undo — including after DeskAI was closed, and after it stopped
/// part-way. Every folder is generated; nothing here ever moves a file.
/// </summary>
public sealed class FolderTemplatePageTests
{
    [Fact]
    public async Task With_nothing_connected_the_section_says_to_connect_a_folder_in_Organize()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<WorkspaceViewModel>();

        await page.InitializeAsync();

        Assert.True(page.HasNoTemplateFolders);
        Assert.Null(page.SelectedTemplateFolder);
        Assert.Equal(
            ["Student", "Developer", "Gaming", "Productivity", "Minimal", "Your own folders"],
            page.Templates.Select(card => card.Name));
        Assert.Equal("Assignments, Slides, Screenshots, Notes", page.Templates[0].FolderList);
        Assert.True(page.Templates[5].IsOwn);
        Assert.Null(await page.PreviewTemplateAsync("student"));
        Assert.Equal("Connect a folder in Organize first.", page.Templates[0].Result);
    }

    [Fact]
    public async Task Without_the_tidy_permission_the_preview_asks_for_it_first_and_looks_at_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "notes.txt");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        var preview = await page.PreviewTemplateAsync("student");

        Assert.Equal("Downloads", page.SelectedTemplateFolder!.Name);
        Assert.False(page.SelectedTemplateFolder.CanTidy);
        Assert.True(preview!.NeedsPermission);
        Assert.Empty(preview.Lines);

        // What the page does after the person accepts the permission dialog.
        await page.AllowTemplateFolderTidyAsync();
        var allowed = await page.PreviewTemplateAsync("student");

        Assert.True(page.SelectedTemplateFolder!.CanTidy);
        Assert.False(allowed!.NeedsPermission);
        Assert.Equal(["Assignments", "Slides", "Screenshots", "Notes"], allowed.Lines.Select(line => line.Name));
        Assert.All(allowed.Lines, line => Assert.Equal(FolderTemplateLineKind.WillMake, line.Kind));
        Assert.Equal(4, allowed.ToMake);
        Assert.Equal(["notes.txt"], Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Making_a_template_makes_exactly_the_listed_folders_and_Undo_removes_only_the_empty_ones_DeskAI_made()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "notes.txt");
        Directory.CreateDirectory(Path.Combine(folder, "slides"));
        app.MakeFile("Downloads", "Notes");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        var preview = await page.PreviewTemplateAsync("student");

        Assert.Equal(
            [
                ("Assignments", FolderTemplateLineKind.WillMake, null),
                ("slides", FolderTemplateLineKind.AlreadyThere, null),
                ("Screenshots", FolderTemplateLineKind.WillMake, null),
                ("Notes", FolderTemplateLineKind.Blocked, "A file called Notes is already there."),
            ],
            preview!.Lines.Select(line => (line.Name, line.Kind, line.Reason)));
        Assert.False(Directory.Exists(Path.Combine(folder, "Assignments")));

        Assert.Null(await page.MakeTemplateAsync(preview));

        var card = page.Templates.Single(item => item.Id == "student");
        Assert.Equal(
            "Made 2 of 3 folders in Downloads. Notes: A file called Notes is already there. Already there: slides.",
            card.Result);
        Assert.True(Directory.Exists(Path.Combine(folder, "Assignments")));
        Assert.True(Directory.Exists(Path.Combine(folder, "Screenshots")));
        Assert.True(File.Exists(Path.Combine(folder, "Notes")));
        Assert.True(page.CanUndoTemplate);
        Assert.EndsWith(": Assignments, Screenshots.", page.LastTemplateSummary, StringComparison.Ordinal);
        Assert.All(page.Templates.Where(item => item.Id != "student"), other => Assert.False(other.HasResult));

        app.MakeFile(@"Downloads\Screenshots", "shot.png");
        await page.UndoTemplateCommand.ExecuteAsync(null);

        Assert.Equal(
            "Removed 1 of 2 folders. Screenshots: The folder is no longer empty, so DeskAI left it in place.",
            page.TemplateMessage);
        Assert.False(Directory.Exists(Path.Combine(folder, "Assignments")));
        Assert.True(Directory.Exists(Path.Combine(folder, "Screenshots")));
        Assert.True(Directory.Exists(Path.Combine(folder, "slides")));
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
        Assert.False(page.CanUndoTemplate);
    }

    [Fact]
    public async Task Typed_names_are_refused_with_a_reason_and_accepted_names_are_made()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        var own = page.Templates.Single(item => item.IsOwn);

        own.TypedNames = @"Tax, ..\Up";
        Assert.Null(await page.PreviewTemplateAsync(own.Id));
        Assert.StartsWith(@"..\Up can't be used.", own.Result, StringComparison.Ordinal);
        own.TypedNames = "CON, Photos";
        Assert.Null(await page.PreviewTemplateAsync(own.Id));
        Assert.Equal("Windows keeps the name CON for itself, so it can't be a folder.", own.Result);
        own.TypedNames = string.Empty;
        Assert.Null(await page.PreviewTemplateAsync(own.Id));
        Assert.Equal("Type at least one folder name.", own.Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));

        own.TypedNames = "Tax 2026, Receipts";
        var preview = await page.PreviewTemplateAsync(own.Id);
        Assert.Equal(["Tax 2026", "Receipts"], preview!.Lines.Select(line => line.Name));
        Assert.Null(await page.MakeTemplateAsync(preview));

        Assert.Equal("Made 2 folders in Downloads.", own.Result);
        Assert.True(Directory.Exists(Path.Combine(folder, "Tax 2026")));
        Assert.True(Directory.Exists(Path.Combine(folder, "Receipts")));
        Assert.Equal(2, Directory.EnumerateFileSystemEntries(folder).Count());
    }

    [Fact]
    public async Task When_every_folder_is_already_there_nothing_can_be_made_and_the_card_says_so()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        Directory.CreateDirectory(Path.Combine(folder, "Screenshots"));
        Directory.CreateDirectory(Path.Combine(folder, "Installers"));
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        var preview = await page.PreviewTemplateAsync("minimal");

        Assert.False(preview!.CanMake);
        Assert.Equal(0, preview.ToMake);
        Assert.Equal("Every folder in this list is already in Downloads.", page.Templates.Single(item => item.Id == "minimal").Result);
    }

    [Fact]
    public async Task After_reopening_DeskAI_the_last_template_run_is_still_offered_and_Undo_removes_its_folders()
    {
        await using var first = await TestApp.StartAsync();
        var folder = first.MakeFolder("Downloads");
        await TidySuggestionTests.ConnectAndAllowAsync(first, folder);
        var before = first.Get<WorkspaceViewModel>();
        await before.InitializeAsync();
        await before.MakeTemplateAsync((await before.PreviewTemplateAsync("minimal"))!);
        await using var app = await first.ReopenAsync();

        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        Assert.True(page.CanUndoTemplate);
        Assert.StartsWith("Made in Downloads at ", page.LastTemplateSummary, StringComparison.Ordinal);
        Assert.EndsWith(": Screenshots, Installers.", page.LastTemplateSummary, StringComparison.Ordinal);

        await page.UndoTemplateCommand.ExecuteAsync(null);

        Assert.Equal("Removed 2 folders.", page.TemplateMessage);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.False(page.CanUndoTemplate);

        // Once undone, it is not offered again next time either.
        await using var again = await app.ReopenAsync();
        var later = again.Get<WorkspaceViewModel>();
        await later.InitializeAsync();
        Assert.False(later.CanUndoTemplate);
    }

    [Fact]
    public async Task A_tidy_after_the_template_takes_the_templates_undo_off_this_page()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.MakeTemplateAsync((await page.PreviewTemplateAsync("minimal"))!);
        Assert.True(page.CanUndoTemplate);

        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        await organize.TidyCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(folder, "Documents", "invoice.pdf")));

        var later = app.Get<WorkspaceViewModel>();
        await later.InitializeAsync();

        Assert.False(later.CanUndoTemplate);
        Assert.True(Directory.Exists(Path.Combine(folder, "Screenshots")));
    }

    [Fact]
    public async Task Undo_after_the_permission_was_taken_back_asks_for_it_first()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        var rootId = await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        await page.MakeTemplateAsync((await page.PreviewTemplateAsync("minimal"))!);
        await app.Get<DeskAI.Core.Tidy.TidyPermissionService>().StopAsync(rootId, TestContext.Current.CancellationToken);
        await page.InitializeAsync();

        await page.UndoTemplateCommand.ExecuteAsync(null);

        Assert.Equal(
            "Undo removes the folders DeskAI made, so DeskAI needs your permission to tidy this folder again.",
            page.TemplateMessage);
        Assert.True(Directory.Exists(Path.Combine(folder, "Screenshots")));
        Assert.True(page.CanUndoTemplate);
    }

    [Fact]
    public async Task A_template_run_that_stopped_part_way_is_asked_about_on_Organize_as_folders_and_refused_here_until_answered()
    {
        await using var first = await TestApp.StartStoppableAsync();
        var folder = first.MakeFolder("Downloads");
        await TidySuggestionTests.ConnectAndAllowAsync(first, folder);
        var before = first.Get<WorkspaceViewModel>();
        await before.InitializeAsync();
        var preview = (await before.PreviewTemplateAsync("minimal"))!;
        var installers = preview.Plan!.Operations.Single(operation => operation is DeskAI.Core.Plans.CreateDirectoryOperation { DestinationRelativePath: "Installers" });
        first.Stopping.StopBefore(installers.Id, JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => first.Get<FolderTemplateService>().MakeAsync(preview, TestContext.Current.CancellationToken));
        await using var app = await first.ReopenAsync();

        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();

        Assert.True(organize.HasInterrupted);
        Assert.Equal("DeskAI stopped while making folders: 1 of 2 folders made.", organize.InterruptedTitle);
        Assert.Equal("DeskAI checked each folder. You can remove the empty folders it made, or keep them.", organize.InterruptedNote);
        Assert.Equal("Remove that folder", organize.UndoInterruptedText);
        Assert.Equal("Keep it", organize.KeepInterruptedText);
        Assert.False(organize.CanPressTidy);

        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();
        Assert.Null(await page.PreviewTemplateAsync("student"));
        Assert.Equal(
            "DeskAI stopped part-way in this folder last time. Open it in Organize and answer the question first.",
            page.Templates[0].Result);
        Assert.False(page.CanUndoTemplate);

        var undone = await organize.UndoInterruptedAsync();

        // The folder under way when DeskAI stopped exists, but nothing proves DeskAI made it, so it stays.
        Assert.Equal("Undone. 1 folder removed.", undone!.Summary);
        Assert.False(Directory.Exists(Path.Combine(folder, "Screenshots")));
        Assert.True(Directory.Exists(Path.Combine(folder, "Installers")));
        Assert.False(organize.HasInterrupted);
        var again = await page.PreviewTemplateAsync("minimal");
        Assert.True(again!.CanMake);
        Assert.Equal(["Screenshots"], again.Lines.Where(line => line.Kind == FolderTemplateLineKind.WillMake).Select(line => line.Name));
    }

    [Fact]
    public async Task Adding_a_starter_pack_still_makes_no_folder_on_disk()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "notes.txt");
        await TidySuggestionTests.ConnectAndAllowAsync(app, folder);
        var page = app.Get<WorkspaceViewModel>();
        await page.InitializeAsync();

        await page.AddPackAsync("student");

        Assert.StartsWith("Added 3 searches and 3 rules.", page.Packs.Single(item => item.Id == "student").Result, StringComparison.Ordinal);
        Assert.Equal(["notes.txt"], Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName));
        Assert.False(page.CanUndoTemplate);
    }
}
