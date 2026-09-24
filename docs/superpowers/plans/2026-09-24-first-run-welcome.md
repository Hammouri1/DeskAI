# First-Run Welcome Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A three-page welcome pop-up that greets brand-new people once, says what DeskAI will
never do, and hands a Connect press to Home's existing connect question.

**Architecture:** A Core `WelcomeService` decides "brand new" (never shown, no folder remembered)
and remembers the showing in the existing `app_settings` store before the pop-up opens. A
WinUI-free `WelcomeViewModel` holds the pages, the Back/Next/Done state, the folder rows (reusing
`PersonalFoldersViewModel`), and the chosen folder. The App draws it as a code-built
`ContentDialog`; `MainWindow` opens it at startup and from Privacy and AI, and on Connect runs the
same `PersonalFolderDialogs.ConfirmConnectAsync` → `PersonalFoldersViewModel.ConnectAsync` →
Organize path Home uses.

**Tech Stack:** C# / .NET 10, WinUI 3, CommunityToolkit.Mvvm, SQLite key/value store, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-24-first-run-welcome-design.md`

## Global Constraints

- The welcome cannot connect, read, or move anything by itself; only the existing connect question connects.
- No AI, no network, nothing read from any folder. The only stored value is `welcome.shown` in `app_settings`. No schema change.
- Shown at startup only when never shown **and** no folder is remembered; marked shown the moment it opens.
- Start fresh forgets `welcome.shown`. Privacy and AI's **Show the welcome again** opens it without changing `welcome.shown`.
- Visible words, exactly: "Welcome to DeskAI" / "Find your files and keep them tidy."; "You stay in charge" with the four lines "DeskAI sees nothing until you connect a folder.", "It only works in your Desktop, Downloads, Documents, and Pictures.", "Nothing moves until you see it and say yes.", "You can put things back."; "Let's start" and "AI is off. You can turn it on later in Privacy and AI."; buttons **Back**, **Next**, **Done**, **Skip**, **Show the welcome again**.
- Tests use only generated temp data (`TestApp`); every person-visible behaviour gets a page test and a Feature Coverage Map row.

## Review Focus

1. Someone updating from an older DeskAI with folders already connected → never greeted. (Task 1 and Task 2 tests.)
2. The settings store cannot be read or written at startup → no welcome and no crash, and never a welcome that returns every start. (Task 1 tests.)
3. The welcome reopened from Privacy and AI after folders are connected and AI is on → rows say "Tidy …" for connected folders, the AI line does not claim AI is off, and it starts on page 1. (Task 2 tests.)
4. Connect refused (the folder Windows reported is gone) → nothing connected, and the reason is shown rather than silence. (Task 2 test; Task 3 shows the message.)
5. Windows reports none of the four folders → page 3 says so instead of an empty page. (Task 2 test.)

---

### Task 1: Remember the welcome (Core), and forget it on Start fresh

**Files:**
- Create: `src/DeskAI.Core/Welcome/WelcomeService.cs`
- Modify: `src/DeskAI.Core/Backup/FreshStartService.cs` (one `RemoveAsync` line)
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs` (register `WelcomeService`)
- Test: `tests/DeskAI.Core.Tests/WelcomeServiceTests.cs`
- Test: `tests/DeskAI.Presentation.Tests/FreshStartPageTests.cs` (one new test)

**Interfaces:**
- Produces: `DeskAI.Core.Welcome.WelcomeService(IAppSettingsStore, IAuthorizedRootRepository)` with
  `const string ShownKey = "welcome.shown"`, `Task<bool> ShouldShowAsync(CancellationToken = default)`,
  `Task MarkShownAsync(CancellationToken = default)`, `Task<bool> ClaimFirstShowAsync(CancellationToken = default)`.

- [ ] **Step 1: Write the failing Core tests**

```csharp
using System.Data.Common;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Core.Welcome;

namespace DeskAI.Core.Tests;

/// <summary>
/// The first-run welcome opens only for someone brand new, is remembered before it opens so it
/// never nags, and stays shut when DeskAI cannot remember having shown it.
/// </summary>
public sealed class WelcomeServiceTests
{
    [Fact]
    public async Task A_brand_new_DeskAI_shows_it_once_and_remembers_before_it_opens()
    {
        var store = new FakeStore();
        var service = new WelcomeService(store, new FakeRoots());

        Assert.True(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(store.Values.GetValueOrDefault(WelcomeService.ShownKey));
        Assert.False(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Someone_with_a_folder_already_remembered_is_not_greeted_and_nothing_is_written()
    {
        var store = new FakeStore();
        var roots = new FakeRoots();
        roots.Roots.Add(AuthorizedRoot.Create(
            Guid.NewGuid(), @"C:\deskai-tests\Downloads", "Downloads", RootAccessLevel.ReadMetadata, RootAuthorizationScope.UserFolder));
        var service = new WelcomeService(store, roots);

        Assert.False(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
        Assert.Empty(store.Values);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_means_no_welcome()
    {
        var service = new WelcomeService(new FakeStore { FailReads = true }, new FakeRoots());

        Assert.False(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_welcome_that_cannot_be_remembered_is_not_shown_so_it_can_never_return_every_start()
    {
        var service = new WelcomeService(new FakeStore { FailWrites = true }, new FakeRoots());

        Assert.False(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FakeStore : IAppSettingsStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public bool FailReads { get; init; }

        public bool FailWrites { get; init; }

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            FailReads ? throw new FakeDbException() : Task.FromResult(Values.GetValueOrDefault(key));

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new FakeDbException();
            }

            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDbException : DbException;

    private sealed class FakeRoots : IAuthorizedRootRepository
    {
        public List<AuthorizedRoot> Roots { get; } = [];

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>(Roots);

        // Every other member of IAuthorizedRootRepository: `=> throw new NotSupportedException();`
        // (the welcome only lists). Copy the member list from IAuthorizedRootRepository.cs.
    }
}
```

Check the real enum member names in `src/DeskAI.Core/Roots/AuthorizedRoot.cs` for
`RootAccessLevel` / `RootAuthorizationScope` and use a connected-folder scope; fill `FakeRoots`'s
remaining members from `IAuthorizedRootRepository` with `throw new NotSupportedException()`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DeskAI.Core.Tests -c Release --filter "FullyQualifiedName~WelcomeServiceTests"`
Expected: build error, `WelcomeService` does not exist.

- [ ] **Step 3: Write `WelcomeService`**

```csharp
using System.Data.Common;
using DeskAI.Core.Abstractions;

namespace DeskAI.Core.Welcome;

/// <summary>Decides whether the first-run welcome opens, and remembers that it did.</summary>
/// <remarks>
/// <para>
/// Only someone brand new is greeted: DeskAI has never shown the welcome, and it remembers no
/// folder at all. Any remembered folder counts, so a person already using DeskAI is not greeted
/// after an update.
/// </para>
/// <para>
/// It holds the settings store and the folder list, and nothing that can reach a file.
/// </para>
/// </remarks>
public sealed class WelcomeService(IAppSettingsStore settings, IAuthorizedRootRepository roots)
{
    /// <summary>Written the moment the welcome opens. Start fresh removes it.</summary>
    public const string ShownKey = "welcome.shown";

    private readonly IAppSettingsStore _settings = settings;
    private readonly IAuthorizedRootRepository _roots = roots;

    public async Task<bool> ShouldShowAsync(CancellationToken cancellationToken = default)
    {
        if (await _settings.ReadAsync(ShownKey, cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        return (await _roots.ListAsync(cancellationToken).ConfigureAwait(false)).Count == 0;
    }

    public Task MarkShownAsync(CancellationToken cancellationToken = default) =>
        _settings.WriteAsync(ShownKey, "yes", cancellationToken);

    /// <summary>True when the welcome should open now; it is then already remembered as shown.</summary>
    /// <remarks>
    /// Remembered before it opens, so Skip, the X, closing DeskAI, or a crash all count. A welcome
    /// whose showing cannot be written down is not shown at all: it would otherwise open again on
    /// every start, which is worse than never greeting.
    /// </remarks>
    public async Task<bool> ClaimFirstShowAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await ShouldShowAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            await MarkShownAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or DbException
            or IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Run the Core tests; expect PASS.**

- [ ] **Step 5: Write the failing Start fresh page test** (in `FreshStartPageTests`)

```csharp
[Fact]
public async Task Start_fresh_brings_the_welcome_back_for_the_next_start()
{
    await using var app = await TestApp.StartAsync();
    var welcome = app.Get<WelcomeService>();
    Assert.True(await welcome.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
    var settings = app.Get<SettingsViewModel>();
    await settings.InitializeAsync();

    await settings.StartFreshAsync();

    Assert.True(await welcome.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
}
```

Run it: fails to resolve `WelcomeService` (not registered).

- [ ] **Step 6: Register and forget**

In `DeskAiApplicationServices`, beside `FreshStartService`:

```csharp
        // The first-run welcome (2026-09-24): one remembered value and the folder list; nothing
        // that can reach a file.
        services.AddSingleton<WelcomeService>();
```

(add `using DeskAI.Core.Welcome;`). In `FreshStartService.StartFreshAsync`, after the
`AskDeskAiService.AgreedKey` removal:

```csharp
        await _appSettings.RemoveAsync(Welcome.WelcomeService.ShownKey, cancellationToken).ConfigureAwait(false);
```

and add "the welcome was shown" to the remarks' list of what is reset.

- [ ] **Step 7: Run `FreshStartPageTests` and `WelcomeServiceTests`; expect PASS.**

- [ ] **Step 8: Commit** — `git commit -m "Remember the first-run welcome once, and forget it on Start fresh"`

---

### Task 2: The welcome's pages and folder rows (Presentation)

**Files:**
- Create: `src/DeskAI.Presentation/ViewModels/WelcomeViewModel.cs`
- Modify: `src/DeskAI.Presentation/ViewModels/ShellViewModel.cs` (constructor + `ClaimFirstWelcomeAsync`)
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs` (`AddTransient<WelcomeViewModel>()`)
- Test: `tests/DeskAI.Presentation.Tests/WelcomePageTests.cs`

**Interfaces:**
- Consumes: `WelcomeService.ClaimFirstShowAsync`, `WelcomeService.ShownKey` (Task 1);
  `PersonalFoldersViewModel` (`Rows`, `Find`, `HasNoRows`, `NoneKnownText`, `Message`, `HasMessage`, `ConnectAsync`, `ReloadAsync`);
  `ShellViewModel.DescribeAi(AiSettings)`.
- Produces: `WelcomePage(string Title, string Body, IReadOnlyList<string> Promises)`;
  `WelcomeViewModel(PersonalFoldersViewModel, IAiSettingsRepository)` with `static Pages`,
  `const AiOffLine`, `Folders`, `PageIndex`, `Current`, `CanGoBack`, `IsLastPage`, `NextText`,
  `PageNumberText`, `Dots` (`IReadOnlyList<bool>`), `AiLine`, `ChosenFolder` (`PersonalFolderKind?`),
  `Task OpenAsync()`, `bool Next()` (true = Done pressed), `void Back()`, `void Choose(PersonalFolderKind)`,
  `Task<Guid?> ConnectChosenAsync()`; `ShellViewModel.ClaimFirstWelcomeAsync()` → `Task<bool>`.

- [ ] **Step 1: Write the failing page tests**

```csharp
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
        await using var app = await TestApp.StartAsync();
        var shell = app.Get<ShellViewModel>();

        Assert.True(await shell.ClaimFirstWelcomeAsync());
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
    public async Task Next_Back_and_Done_walk_the_three_pages()
    {
        await using var app = await TestApp.StartAsync();
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();

        Assert.Equal("Welcome to DeskAI", welcome.Current.Title);
        Assert.Equal("Find your files and keep them tidy.", welcome.Current.Body);
        Assert.False(welcome.CanGoBack);
        Assert.Equal("Next", welcome.NextText);
        Assert.Equal([true, false, false], welcome.Dots);
        Assert.Equal("Page 1 of 3", welcome.PageNumberText);

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

        welcome.Back();
        Assert.Equal(0, welcome.PageIndex);
        welcome.Back();
        Assert.Equal(0, welcome.PageIndex);

        Assert.False(welcome.Next());
        Assert.False(welcome.Next());
        Assert.Equal("Let's start", welcome.Current.Title);
        Assert.True(welcome.IsLastPage);
        Assert.Equal("Done", welcome.NextText);
        Assert.Equal(WelcomeViewModel.AiOffLine, welcome.AiLine);
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

        // Choosing only closes the welcome; the window then asks "Connect your Downloads?". Cancel there
        // means ConnectChosenAsync is never called, so nothing is connected.
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
        await welcome.ConnectChosenAsync();
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
```

Check before running: the provider display name `CloudProviderCatalog` gives for `openrouter` (the
`DescribeAi` tests show "AI: OpenRouter"); `TidyViewModel.SelectedFolder`/`NeedsPermission` as used in
`YourFoldersPageTests`. For the refused-connect test, confirm `PersonalFolderPolicy.List()` still lists
a known folder whose directory does not exist; if it filters missing folders, create Downloads, then
delete it after `OpenAsync` and before `ConnectChosenAsync`.

- [ ] **Step 2: Run to verify they fail** — `dotnet test tests/DeskAI.Presentation.Tests -c Release --filter "FullyQualifiedName~WelcomePageTests"`; expected: build errors (`WelcomeViewModel`, `ClaimFirstWelcomeAsync` missing).

- [ ] **Step 3: Write `WelcomeViewModel`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Ai;
using DeskAI.Core.Roots;

namespace DeskAI.App.ViewModels;

/// <summary>One page of the welcome: a title, a line under it, and ticked promises (page 2 only).</summary>
public sealed record WelcomePage(string Title, string Body, IReadOnlyList<string> Promises);

/// <summary>
/// The first-run welcome's three pages, and the folder a person chose to connect from it.
/// </summary>
/// <remarks>
/// It cannot connect anything by itself. Choosing a folder only records it; the window then asks
/// Home's "Connect your …?" question, and only that question's yes calls
/// <see cref="ConnectChosenAsync"/>, which is Home's own connect. It holds no settings store: opening
/// it again from Privacy and AI never changes whether DeskAI remembers greeting.
/// </remarks>
public sealed class WelcomeViewModel(PersonalFoldersViewModel folders, IAiSettingsRepository aiSettings) : ObservableObject
{
    public const string AiOffLine = "AI is off. You can turn it on later in Privacy and AI.";

    private readonly IAiSettingsRepository _aiSettings = aiSettings;
    private int _pageIndex;
    private string _aiLine = AiOffLine;

    public static IReadOnlyList<WelcomePage> Pages { get; } =
    [
        new("Welcome to DeskAI", "Find your files and keep them tidy.", []),
        new("You stay in charge", string.Empty,
        [
            "DeskAI sees nothing until you connect a folder.",
            "It only works in your Desktop, Downloads, Documents, and Pictures.",
            "Nothing moves until you see it and say yes.",
            "You can put things back.",
        ]),
        new("Let's start", "Connect a folder to begin. DeskAI asks once more before connecting.", []),
    ];

    public PersonalFoldersViewModel Folders { get; } = folders;

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(Current));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(IsLastPage));
                OnPropertyChanged(nameof(NextText));
                OnPropertyChanged(nameof(PageNumberText));
                OnPropertyChanged(nameof(Dots));
            }
        }
    }

    public WelcomePage Current => Pages[_pageIndex];

    public bool CanGoBack => _pageIndex > 0;

    public bool IsLastPage => _pageIndex == Pages.Count - 1;

    public string NextText => IsLastPage ? "Done" : "Next";

    /// <summary>What a screen reader says for the row of dots.</summary>
    public string PageNumberText => $"Page {_pageIndex + 1} of {Pages.Count}";

    /// <summary>One dot per page; true for the page on screen.</summary>
    public IReadOnlyList<bool> Dots => [.. Enumerable.Range(0, Pages.Count).Select(index => index == _pageIndex)];

    /// <summary>The AI line on the last page. Read from the saved choice, so a reopened welcome never claims AI is off while it is on.</summary>
    public string AiLine
    {
        get => _aiLine;
        private set => SetProperty(ref _aiLine, value);
    }

    public PersonalFolderKind? ChosenFolder { get; private set; }

    /// <summary>Starts on page one with nothing chosen, and reads the folder rows and the AI choice afresh.</summary>
    public async Task OpenAsync()
    {
        PageIndex = 0;
        ChosenFolder = null;
        await Folders.ReloadAsync().ConfigureAwait(true);
        try
        {
            var state = ShellViewModel.DescribeAi(await _aiSettings.LoadAsync().ConfigureAwait(true));
            AiLine = state == "AI off" ? AiOffLine : $"{state}. You can change this in Privacy and AI.";
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // Never guess "off": say where the choice lives instead.
            AiLine = "You can choose whether to use AI in Privacy and AI.";
        }
    }

    /// <returns>True when Done was pressed on the last page, so the pop-up closes.</returns>
    public bool Next()
    {
        if (IsLastPage)
        {
            return true;
        }

        PageIndex++;
        return false;
    }

    public void Back()
    {
        if (CanGoBack)
        {
            PageIndex--;
        }
    }

    /// <summary>Records the Connect press. Nothing is connected here.</summary>
    public void Choose(PersonalFolderKind kind) => ChosenFolder = kind;

    /// <summary>Home's own connect, called only after the "Connect your …?" question's yes.</summary>
    public async Task<Guid?> ConnectChosenAsync() =>
        ChosenFolder is { } kind ? await Folders.ConnectAsync(kind).ConfigureAwait(true) : null;
}
```

- [ ] **Step 4: Add to `ShellViewModel`** — a `WelcomeService welcome` constructor parameter (last),
  stored in `_welcome`, and:

```csharp
    /// <summary>
    /// True once, at the first start of a brand-new DeskAI; the welcome is then already
    /// remembered as shown, so skipping or closing it never brings it back.
    /// </summary>
    public Task<bool> ClaimFirstWelcomeAsync() => _welcome.ClaimFirstShowAsync();
```

  Register `services.AddTransient<WelcomeViewModel>();` beside `PersonalFoldersViewModel`.

- [ ] **Step 5: Run `WelcomePageTests`; expect PASS.** Then run the whole Presentation test project (the shell's new constructor parameter must still resolve everywhere).

- [ ] **Step 6: Commit** — `git commit -m "Add the welcome's three pages and folder rows, shown once to brand-new people"`

---

### Task 3: Show the welcome (App), and "Show the welcome again"

**Files:**
- Create: `src/DeskAI.App/Views/WelcomeDialog.cs`
- Modify: `src/DeskAI.App/MainWindow.xaml.cs` (constructor param, startup, `ShowWelcomeAsync`)
- Modify: `src/DeskAI.App/Views/SettingsPage.xaml` (button in the hero) and `SettingsPage.xaml.cs` (handler)
- Test: `tests/DeskAI.Presentation.Tests/WelcomeLayoutTests.cs` (source-level)

**Interfaces:**
- Consumes: everything `WelcomeViewModel` produces (Task 2); `ShellViewModel.ClaimFirstWelcomeAsync`;
  `PersonalFolderDialogs.ConfirmConnectAsync(XamlRoot, string)`; `MainWindow.GoTo(string, bool)`.
- Produces: `WelcomeDialog.ShowAsync(XamlRoot, WelcomeViewModel)` → `Task<bool>` (true = a Connect
  button was pressed); `MainWindow.ShowWelcomeAsync()` (internal).

- [ ] **Step 1: Write the failing source-level test** (the App project cannot be loaded by tests; these
  pin the wiring the view model tests cannot see)

```csharp
namespace DeskAI.Presentation.Tests;

/// <summary>
/// The welcome's wiring in the window: it opens only when the shell claims the first showing,
/// a Connect press goes through Home's own "Connect your …?" question before anything connects,
/// and Privacy and AI can open it again.
/// </summary>
public sealed class WelcomeLayoutTests
{
    [Fact]
    public void The_window_opens_the_welcome_only_after_claiming_it_and_connects_only_after_Homes_question()
    {
        var window = Read("src", "DeskAI.App", "MainWindow.xaml.cs");

        Assert.Contains("ClaimFirstWelcomeAsync()", window, StringComparison.Ordinal);
        var ask = window.IndexOf("PersonalFolderDialogs.ConfirmConnectAsync", StringComparison.Ordinal);
        var connect = window.IndexOf("ConnectChosenAsync()", StringComparison.Ordinal);
        Assert.True(ask > 0 && connect > ask, "The welcome must ask Home's question before connecting.");
        Assert.DoesNotContain("MarkShownAsync", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Privacy_and_AI_has_a_button_that_opens_the_welcome_again()
    {
        Assert.Contains("Content=\"Show the welcome again\"", Read("src", "DeskAI.App", "Views", "SettingsPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("ShowWelcomeAsync()", Read("src", "DeskAI.App", "Views", "SettingsPage.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pop_up_is_closed_with_Skip_and_moves_with_Back_and_the_view_models_Next()
    {
        var dialog = Read("src", "DeskAI.App", "Views", "WelcomeDialog.cs");

        Assert.Contains("CloseButtonText = \"Skip\"", dialog, StringComparison.Ordinal);
        Assert.Contains("SecondaryButtonText = \"Back\"", dialog, StringComparison.Ordinal);
        Assert.Contains("welcome.Next()", dialog, StringComparison.Ordinal);
        Assert.Contains("welcome.Choose(", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAsync", dialog, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
```

- [ ] **Step 2: Run; expect FAIL** (file not found / strings missing).

- [ ] **Step 3: Write `WelcomeDialog`**

```csharp
using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace DeskAI.App.Views;

/// <summary>
/// The first-run welcome pop-up. Next and Back stay inside it; Skip, the X, Esc, and Done close it.
/// A Connect button only records the folder and closes it; the window then asks Home's question.
/// </summary>
internal static class WelcomeDialog
{
    /// <returns>True when a Connect button was pressed; <see cref="WelcomeViewModel.ChosenFolder"/> names it.</returns>
    public static async Task<bool> ShowAsync(XamlRoot xamlRoot, WelcomeViewModel welcome)
    {
        var chose = false;
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            CloseButtonText = "Skip",
            SecondaryButtonText = "Back",
            DefaultButton = ContentDialogButton.Primary,
        };

        void Render()
        {
            dialog.Title = welcome.Current.Title;
            dialog.PrimaryButtonText = welcome.NextText;
            dialog.IsSecondaryButtonEnabled = welcome.CanGoBack;
            dialog.Content = BuildPage(welcome, kind =>
            {
                welcome.Choose(kind);
                chose = true;
                dialog.Hide();
            });
        }

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!welcome.Next())
            {
                args.Cancel = true;
                Render();
            }
        };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            welcome.Back();
            Render();
        };

        Render();
        await dialog.ShowAsync();
        return chose;
    }

    private static StackPanel BuildPage(WelcomeViewModel welcome, Action<Core.Roots.PersonalFolderKind> connect)
    {
        var page = new StackPanel { Spacing = 14, MaxWidth = 440, MinWidth = 360 };
        page.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/DeskAI.Logo.png")),
            Width = 56,
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        AutomationProperties.SetName(dots, welcome.PageNumberText);
        foreach (var isCurrent in welcome.Dots)
        {
            dots.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                Opacity = isCurrent ? 1 : 0.3,
            });
        }

        page.Children.Add(dots);
        if (welcome.Current.Body.Length > 0)
        {
            page.Children.Add(new TextBlock { Text = welcome.Current.Body, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var promise in welcome.Current.Promises)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 14 });
            row.Children.Add(new TextBlock { Text = promise, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
            page.Children.Add(row);
        }

        if (welcome.IsLastPage)
        {
            if (welcome.Folders.HasNoRows)
            {
                page.Children.Add(new TextBlock { Text = PersonalFoldersViewModel.NoneKnownText, TextWrapping = TextWrapping.Wrap });
            }

            foreach (var folder in welcome.Folders.Rows)
            {
                var button = new Button { Content = folder.ButtonName, HorizontalAlignment = HorizontalAlignment.Stretch };
                button.Click += (_, _) => connect(folder.Kind);
                page.Children.Add(button);
            }

            page.Children.Add(new TextBlock
            {
                Text = welcome.AiLine,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionStyle"],
            });
        }

        return page;
    }
}
```

Check `CaptionStyle` exists in `src/DeskAI.App/Themes` (the Settings page uses it); if the accent brush
key differs in DeskAI's theme, use the brush the nav pane uses for its selected item.

- [ ] **Step 4: Wire `MainWindow`** — add `WelcomeViewModel welcome` as the constructor's last
  parameter, keep it in `private readonly WelcomeViewModel? _welcome;`, add
  `private bool _welcomeOpen;`, and at the end of the constructor:

```csharp
        RootNavigation.Loaded += OnFirstLoaded;
```

```csharp
    /// <summary>
    /// Greets a brand-new person once. The shell remembers the showing before the pop-up opens,
    /// so skipping it, closing DeskAI, or a crash never brings it back.
    /// </summary>
    private async void OnFirstLoaded(object sender, RoutedEventArgs args)
    {
        RootNavigation.Loaded -= OnFirstLoaded;
        if (_shell is not null && await _shell.ClaimFirstWelcomeAsync())
        {
            await ShowWelcomeAsync();
        }
    }

    /// <summary>
    /// Opens the welcome, at the first start or from Privacy and AI. A Connect press closes it and
    /// asks Home's own "Connect your …?" question; only that question's yes connects, and then
    /// Organize opens on the folder, exactly as from Home.
    /// </summary>
    internal async Task ShowWelcomeAsync()
    {
        if (_welcome is null || _welcomeOpen || Content?.XamlRoot is not { } root)
        {
            return;
        }

        _welcomeOpen = true;
        try
        {
            await _welcome.OpenAsync();
            if (!await Views.WelcomeDialog.ShowAsync(root, _welcome)
                || _welcome.ChosenFolder is not { } kind
                || _welcome.Folders.Find(kind) is not { } row)
            {
                return;
            }

            if (!row.IsConnected && !await Views.PersonalFolderDialogs.ConfirmConnectAsync(root, row.Name))
            {
                return;
            }

            if (await _welcome.ConnectChosenAsync() is not null)
            {
                GoTo("organize", fresh: true);
            }
            else if (_welcome.Folders.HasMessage)
            {
                await new ContentDialog
                {
                    XamlRoot = root,
                    Title = $"DeskAI could not connect your {row.Name}",
                    Content = new TextBlock { Text = _welcome.Folders.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
                    CloseButtonText = "OK",
                }.ShowAsync();
            }
        }
        finally
        {
            _welcomeOpen = false;
        }
    }
```

- [ ] **Step 5: Settings button** — in `SettingsPage.xaml`'s hero, right after the
  "AI is optional. Choose what you use and what you share." `TextBlock`:

```xml
                        <Button Content="Show the welcome again" Click="OnShowWelcomeClick" />
```

and in `SettingsPage.xaml.cs`:

```csharp
    /// <summary>Opens the first-run welcome again. It never changes whether DeskAI remembers greeting.</summary>
    private async void OnShowWelcomeClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainAppWindow is MainWindow window)
        {
            await window.ShowWelcomeAsync();
        }
    }
```

- [ ] **Step 6: Run `WelcomeLayoutTests`, `AccessibilityNameTests`, `NoPlaceholderUiTests`; expect PASS.**
  Build the App project into a scratch OutDir (the owner may have DeskAI open):
  `dotnet build src/DeskAI.App -c Release --no-restore -p:OutDir=<scratchpad>\app\` — expect 0 warnings.

- [ ] **Step 7: Commit** — `git commit -m "Show the welcome at the first start and from Privacy and AI"`

---

### Task 4: Docs, coverage map, full verification

**Files:**
- Modify: `docs/TESTING.md` (Feature Coverage Map row), `docs/UI-UX.md` (First-Run Experience),
  `docs/USER-GUIDE.md`, `docs/MANUAL-TESTING.md`, `README.md` ("What to expect on first run"),
  `docs/HANDOFF.md` (Start here: welcome built; next is the Desktop Studio end-to-end check and suggestions)

- [ ] **Step 1: Coverage map row** (Home section):

```markdown
| Home | First-run welcome: shown once to a brand-new DeskAI and remembered before it opens; not shown to someone with a folder already connected; Next, Back, and Done walk three pages; the last page lists only the folders Windows reports (or says none were found) and says whether AI is on; Connect connects only after Home's question and opens Organize; a refused connect says why; Start fresh brings it back; Privacy and AI reopens it on page one without changing what DeskAI remembers | `WelcomePageTests`, `WelcomeLayoutTests`, `FreshStartPageTests` |
```

- [ ] **Step 2: Other docs.** UI-UX: describe the three pages and who sees them. USER-GUIDE and README:
  "The first time DeskAI opens it shows a short welcome; skip it any time, and find it again under
  Privacy and AI → Show the welcome again." MANUAL-TESTING: a check using the UI preview build (fresh
  temp data): the welcome opens; Next/Back/Skip; reopen DeskAI and it does not come back; Connect
  Downloads → the question → Cancel connects nothing; Connect → Organize opens on Downloads; Privacy
  and AI → Show the welcome again.

- [ ] **Step 3: Full verification** (ask the owner to close DeskAI first):

```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
dotnet format DeskAI.sln --no-restore --verify-no-changes
```

Expected: 0 warnings, all tests pass, no format changes.

- [ ] **Step 4: Review the diff** for secrets, personal paths, misleading text. Then one fresh
  whole-change review (owner's usual choice), fix findings with a failing test first.

- [ ] **Step 5: Commit** — `git commit -m "Document the first-run welcome and record it in the handoff"`
