using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;
using DeskAI.Core.Welcome;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The first-run welcome, used the way a newcomer uses it. The four "Windows folders" are
/// generated inside the test's own temp folder.
/// </summary>
public sealed class WelcomePageTests
{
    [Fact]
    public async Task A_brand_new_DeskAI_greets_once_and_never_again_even_if_it_was_skipped()
    {
        var app = await TestApp.StartAsync();
        Assert.True(await app.Get<ShellViewModel>().ClaimFirstWelcomeAsync());

        // Skipped at once, or DeskAI closed: the next start stays quiet.
        await using var reopened = await app.ReopenAsync();
        Assert.False(await reopened.Get<ShellViewModel>().ClaimFirstWelcomeAsync());
    }

    [Fact]
    public async Task Someone_already_using_DeskAI_is_not_greeted_after_updating()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Downloads", "report.pdf");
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        Assert.NotNull(await home.Folders.ConnectAsync(PersonalFolderKind.Downloads));

        Assert.False(await app.Get<ShellViewModel>().ClaimFirstWelcomeAsync());
    }

    [Fact]
    public async Task Next_Back_and_Done_walk_the_four_pages()
    {
        await using var app = await TestApp.StartAsync();
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();

        Assert.Equal("Welcome to DeskAI", welcome.Current.Title);
        Assert.Equal("Find your files and keep them tidy.", welcome.Current.Body);
        Assert.False(welcome.CanGoBack);
        Assert.Equal("Next", welcome.NextText);
        Assert.Equal([true, false, false, false], welcome.Dots);
        Assert.Equal("Page 1 of 4", welcome.PageNumberText);

        Assert.False(welcome.Next());
        Assert.Equal("You stay in charge", welcome.Current.Title);
        Assert.Equal(
            [
                "DeskAI sees nothing until you connect a folder.",
                "It only works in your Desktop, Downloads, Documents, and Pictures.",
                "Nothing moves until you see it and say yes.",
                "You can put things back.",
            ],
            welcome.Current.Promises);
        Assert.True(welcome.CanGoBack);
        Assert.Equal([false, true, false, false], welcome.Dots);

        welcome.Back();
        Assert.Equal(0, welcome.PageIndex);
        welcome.Back();
        Assert.Equal(0, welcome.PageIndex);

        Assert.False(welcome.Next());
        Assert.False(welcome.Next());
        Assert.Equal("Find any file, from anywhere", welcome.Current.Title);
        Assert.False(welcome.Next());
        Assert.Equal("Let's start", welcome.Current.Title);
        Assert.True(welcome.IsLastPage);
        Assert.Equal("Done", welcome.NextText);
        Assert.Equal(WelcomeViewModel.AiOffLine, welcome.AiLine);
        Assert.Equal("AI is off. You can turn it on later in Privacy and AI.", welcome.AiLine);
        Assert.True(welcome.Next());
        Assert.Null(welcome.ChosenFolder);
    }

    [Fact]
    public async Task The_last_page_offers_the_folders_Windows_reports_and_Cancel_connects_nothing()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Downloads", "setup.exe", "report.pdf");
        app.KnownFolders.Pictures = null;
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();

        Assert.Equal(["Desktop", "Downloads", "Documents"], welcome.Folders.Rows.Select(row => row.Name));
        Assert.All(welcome.Folders.Rows, row => Assert.Equal($"Connect {row.Name}", row.ButtonName));

        welcome.Choose(PersonalFolderKind.Downloads);

        // Choosing only closes the welcome; the window then asks "Connect your Downloads?". Cancel
        // there means ConnectChosenAsync is never called, so nothing is connected.
        Assert.Equal(PersonalFolderKind.Downloads, welcome.ChosenFolder);
        Assert.Empty(await app.Get<ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Connect_after_the_yes_connects_names_only_and_opens_Organize_on_the_folder()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Downloads", "setup.exe", "report.pdf");
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();
        welcome.Choose(PersonalFolderKind.Downloads);

        Assert.NotNull(await welcome.ConnectChosenAsync());

        var connected = Assert.Single(await app.Get<ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Downloads", connected.Name);
        Assert.False(connected.CanReadContent);
        Assert.False(connected.CanTidy);
        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        Assert.Equal("Downloads", organize.SelectedFolder!.Name);
        Assert.True(organize.NeedsPermission);
        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(app.Sandbox, "Downloads")).Count());
    }

    [Fact]
    public async Task A_refused_connect_connects_nothing_and_says_why()
    {
        await using var app = await TestApp.StartAsync();
        // Windows reports a Downloads folder that is not there.
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();
        welcome.Choose(PersonalFolderKind.Downloads);

        Assert.Null(await welcome.ConnectChosenAsync());

        Assert.True(welcome.Folders.HasMessage);
        Assert.Empty(await app.Get<ConnectedFolderService>().ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task No_folders_reported_by_Windows_says_so_on_the_last_page()
    {
        await using var app = await TestApp.StartAsync();
        app.KnownFolders.Desktop = null;
        app.KnownFolders.Downloads = null;
        app.KnownFolders.Documents = null;
        app.KnownFolders.Pictures = null;
        var welcome = app.Get<WelcomeViewModel>();

        await welcome.OpenAsync();

        Assert.True(welcome.Folders.HasNoRows);
        Assert.Equal(PersonalFolderPolicy.NoneKnownReason, PersonalFoldersViewModel.NoneKnownText);
    }

    [Fact]
    public async Task Reopened_from_Privacy_and_AI_it_starts_on_page_one_tells_the_truth_and_changes_nothing()
    {
        await using var app = await TestApp.StartAsync();
        Assert.True(await app.Get<ShellViewModel>().ClaimFirstWelcomeAsync());
        app.MakeFolder("Downloads", "report.pdf");
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();
        welcome.Next();
        welcome.Next();
        welcome.Choose(PersonalFolderKind.Downloads);
        Assert.NotNull(await welcome.ConnectChosenAsync());
        await app.Vault.SaveAsync("DeskAI/OpenRouter", "sk-or-generated-not-a-real-key", TestContext.Current.CancellationToken);
        await app.Get<IAiSettingsRepository>().SaveAsync(AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
        }, TestContext.Current.CancellationToken);
        var store = app.Get<IAppSettingsStore>();
        var shownBefore = await store.ReadAsync(WelcomeService.ShownKey, TestContext.Current.CancellationToken);

        await welcome.OpenAsync();

        Assert.Equal(0, welcome.PageIndex);
        Assert.Null(welcome.ChosenFolder);
        Assert.Equal("Tidy Downloads", welcome.Folders.Find(PersonalFolderKind.Downloads)!.ButtonName);
        Assert.Equal("AI: OpenRouter. You can change this in Privacy and AI.", welcome.AiLine);
        Assert.Equal(shownBefore, await store.ReadAsync(WelcomeService.ShownKey, TestContext.Current.CancellationToken));
    }
}
