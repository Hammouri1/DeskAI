# Quick Search Polish — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Quick search gets a shortcut the person picks from three (Ctrl + Alt + D by default), a
"Let my buddy move" switch that wins over Windows' Animation effects, a buddy chooser with a big
stage and round faces, and a glowing bar with the buddy perched on top and an opening animation.

**Architecture:** Core gets a closed `QuickSearchShortcut` enum and two more remembered values
(`quicksearch.shortcut`, `quicksearch.motion`) in `QuickSearchSettingsService`. Presentation gets
the fixed key table (`QuickSearchHotKeys`), a `BuddyMotion` singleton that the switch sets, and
new card properties (shortcut, motion, chosen buddy). `QuickSearchSwitch` stays the one place that
applies what is stored. The App reads the key table in `GlobalHotKey`, lets `BuddyControl` follow
`BuddyMotion` instead of Windows, draws the stage and faces on My workspace, and redraws
`QuickSearchWindow` — see-through or the backup layout, whichever the probe in Task 4 allows.

**Tech Stack:** C# / .NET 10, WinUI 3 (Windows App SDK 2.4), CommunityToolkit.Mvvm, SQLite
key/value store (`app_settings`), xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-25-quick-search-polish-design.md` (approved by the owner
2026-09-25, including its two rulings). Mockups: `…-quick-search-polish-mockups/chooser.html`
(option B) and `bar-look.html` (option C). The first quick search plan and spec are background:
`docs/superpowers/plans/2026-09-25-quick-search.md`, ADR 0047.

## Global Constraints

- Shortcut list, exactly and in this order: **Ctrl + Alt + D** (default), **Ctrl + Alt + Space**, **Ctrl + Shift + Space**. Stored as `quicksearch.shortcut` with the enum's name (`CtrlAltD`, `CtrlAltSpace`, `CtrlShiftSpace`); anything else or an unreadable store = Ctrl + Alt + D.
- Still `RegisterHotKey` with `MOD_NOREPEAT` for **one** fixed combination at a time. No keyboard hook, no key recorder. The old combination is unregistered before a new one is asked for.
- A refused shortcut: "Another program already uses {shortcut}. Pick another shortcut above." Nothing listens until the person picks one Windows accepts or turns the switch off and on (no fallback to the old one).
- Motion switch: header "Let my buddy move", line "Turn this off to keep your buddy and the search bar still.", on by default, stored as `quicksearch.motion` ("yes"/"no"; missing or unreadable = on). It wins over Windows' Animation effects: `BuddyControl` no longer reads `UISettings.AnimationsEnabled`.
- Start fresh forgets `quicksearch.shortcut` and `quicksearch.motion` (both in `QuickSearchSettingsService.Keys`), and the live shortcut and motion go back to the defaults at once.
- Nothing changes in what quick search reads, opens, remembers, or sends (ADR 0047): no AI, no network, nothing typed or found is stored or logged.
- Plain, friendly words in the UI; no technical terms. Every visible behaviour gets a page test in `DeskAI.Presentation.Tests` and a Feature Coverage Map row in `docs/TESTING.md`.
- Tests use generated temp data only (`TestApp`); no real shortcut is registered and no real window is opened in any test.
- Each task ends with: full Release build, all tests, `dotnet format --verify-no-changes`, then one commit on `quick-search`. Nothing is pushed, tagged, or released.
- The owner often has DeskAI running. Before building, run `Get-Process DeskAI.App -ErrorAction SilentlyContinue`. If it is running, never clean or rebuild the solution: build the App into a scratch `OutDir` (see "Build commands") and ask the owner to close DeskAI for the final build.

### Build commands

```powershell
# Is the owner's DeskAI open?
Get-Process DeskAI.App -ErrorAction SilentlyContinue

# DeskAI closed: the normal three
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
dotnet format DeskAI.sln --no-restore --verify-no-changes

# DeskAI open: prove the App compiles into a scratch folder, and build and test everything else in place
dotnet build src/DeskAI.App/DeskAI.App.csproj -c Release --no-restore -p:OutDir=C:/Users/Hammouri/.claude/jobs/e1233c1d/tmp/appout/
dotnet build tests/DeskAI.Presentation.Tests/DeskAI.Presentation.Tests.csproj -c Release --no-restore
dotnet build tests/DeskAI.Core.Tests/DeskAI.Core.Tests.csproj -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore

# A single test class while working
dotnet test tests/DeskAI.Presentation.Tests/DeskAI.Presentation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QuickSearchSettingsPageTests"

# The UI preview (generated folders only), into a scratch folder
dotnet build src/DeskAI.App/DeskAI.App.csproj -c Release --no-restore -p:DeskAiUiPreview=true -p:OutDir=C:/Users/Hammouri/.claude/jobs/e1233c1d/tmp/preview/
```

## Review Focus

1. **Picking the shortcut DeskAI already listens for** — nothing changes, it keeps listening, no message. (Task 1 test `Picking_the_shortcut_already_in_use_changes_nothing`.)
2. **A refused shortcut, then the switch off and on** — off clears the message; on asks Windows again for the chosen shortcut and says so again if it is still taken. (Task 1 test `A_refused_shortcut_is_asked_for_again_when_the_switch_is_turned_back_on`.)
3. **Start fresh after changing both** — DeskAI listens for Ctrl + Alt + D again and buddies move again straight away, without a restart. (Task 1 and Task 2 Start fresh tests.)
4. **The welcome reopened after changing the shortcut** — it names the chosen shortcut, not the old one. (Task 1 test `The_welcome_names_the_chosen_shortcut`.)
5. **Moving through the faces one after another** (arrow keys choose as they move) — each choice is saved, the stage follows, and exactly one face is chosen at every step. (Task 3 test `Choosing_faces_one_after_another_keeps_exactly_one_chosen`.)

---

## File map

| File | Task | What changes |
|---|---|---|
| `src/DeskAI.Core/QuickSearch/QuickSearchShortcut.cs` (new) | 1 | The closed list and its words |
| `src/DeskAI.Core/QuickSearch/QuickSearchSettings.cs` | 1, 2 | `Shortcut`, `LetsBuddyMove`, two keys, two setters |
| `src/DeskAI.Core/QuickSearch/QuickSearchWords.cs` | 1 | Tooltip names the chosen shortcut |
| `src/DeskAI.Presentation/Services/QuickSearchHotKeys.cs` (new) | 1 | Fixed modifier and key values per choice |
| `src/DeskAI.Presentation/Services/IQuickSearchHotKey.cs` | 1 | `Listen(bool, QuickSearchShortcut)` |
| `src/DeskAI.Presentation/Services/QuickSearchSwitch.cs` | 1, 2 | `SetShortcutAsync`, `SetMotionAsync`, applies motion |
| `src/DeskAI.Presentation/Services/BackgroundPresenceController.cs` | 1 | `SetQuickSearch(bool, QuickSearchShortcut)` |
| `src/DeskAI.Presentation/Services/BuddyMotion.cs` (new) | 2 | Whether buddies move, with a Changed event |
| `src/DeskAI.Presentation/ViewModels/QuickSearchCardViewModel.cs` | 1, 2, 3 | Shortcut, motion, chosen buddy |
| `src/DeskAI.Presentation/ViewModels/WelcomeViewModel.cs` | 1, 2 | Pages name the chosen shortcut; exposes `Motion` |
| `src/DeskAI.Presentation/ViewModels/QuickSearchViewModel.cs` | 5 | Row kind label, colour key, Enter hint |
| `src/DeskAI.Presentation/Help/HelpCatalog.cs` | 1 | Help names the default and where to change it |
| `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs` | 2 | Registers `BuddyMotion` |
| `src/DeskAI.App/Services/GlobalHotKey.cs`, `HotKeyInterop.cs` | 1 | Uses the key table; unregisters before re-registering |
| `src/DeskAI.App/App.xaml.cs` | 1, 5 | `Listen(false, …)`; the bar gets `BuddyMotion` |
| `src/DeskAI.App/Views/WorkspacePage.xaml` + `.cs` | 1, 2, 3 | Shortcut drop-down, motion switch, stage and faces |
| `src/DeskAI.App/Views/Buddies/BuddyControl.cs`, `BuddyFactory.cs` | 2 | Follow `BuddyMotion` |
| `src/DeskAI.App/Views/Buddies/BuddyStageBrushes.cs` (new) | 3 | The seven stage backgrounds |
| `src/DeskAI.App/Views/Buddies/BuddyAnimations.cs` (new) | 3, 5 | Pop-in and drop-in moves |
| `src/DeskAI.App/Views/WelcomeDialog.cs` | 2 | Sparky follows the switch |
| `src/DeskAI.App/Views/SeeThroughBackdrop.cs` (new, only if the probe works) | 4, 5 | Transparent window background |
| `src/DeskAI.App/Views/QuickSearchWindow.xaml` + `.cs` | 5 | The glowing bar |
| Tests: `DeskAI.Core.Tests/QuickSearchSettingsServiceTests.cs`, `DeskAI.Presentation.Tests/QuickSearchSettingsPageTests.cs`, `QuickSearchWelcomePageTests.cs`, `QuickSearchLayoutTests.cs`, `QuickSearchPageTests.cs`, `FreshStartPageTests.cs`, `TestDoubles.cs`, `QuickSearchHotKeysTests.cs` (new) | 1–5 | |
| Docs: `TESTING.md`, `UI-UX.md`, `USER-GUIDE.md`, `README.md`, `PRODUCT.md`, `ROADMAP.md`, `RELEASE-NOTES.md`, `MANUAL-TESTING.md`, ADR 0047, the quick search security review, the spec's rulings, `HANDOFF.md` | 1–6 | |

---

### Task 1: Pick the shortcut

**Files:**
- Create: `src/DeskAI.Core/QuickSearch/QuickSearchShortcut.cs`
- Create: `src/DeskAI.Presentation/Services/QuickSearchHotKeys.cs`
- Create: `tests/DeskAI.Presentation.Tests/QuickSearchHotKeysTests.cs`
- Modify: `src/DeskAI.Core/QuickSearch/QuickSearchSettings.cs`, `QuickSearchWords.cs`
- Modify: `src/DeskAI.Presentation/Services/IQuickSearchHotKey.cs`, `QuickSearchSwitch.cs`, `BackgroundPresenceController.cs`
- Modify: `src/DeskAI.Presentation/ViewModels/QuickSearchCardViewModel.cs`, `WelcomeViewModel.cs`
- Modify: `src/DeskAI.Presentation/Help/HelpCatalog.cs` (the `workspace.quicksearch` entry)
- Modify: `src/DeskAI.App/Services/GlobalHotKey.cs`, `HotKeyInterop.cs`, `src/DeskAI.App/App.xaml.cs`
- Modify: `src/DeskAI.App/Views/WorkspacePage.xaml`, `WorkspacePage.xaml.cs`
- Test: `tests/DeskAI.Core.Tests/QuickSearchSettingsServiceTests.cs`, `tests/DeskAI.Presentation.Tests/QuickSearchSettingsPageTests.cs`, `QuickSearchWelcomePageTests.cs`, `QuickSearchLayoutTests.cs`, `FreshStartPageTests.cs`, `TestDoubles.cs`
- Docs: `docs/TESTING.md`, `docs/UI-UX.md`, `docs/USER-GUIDE.md`, `README.md`, `docs/PRODUCT.md`

**Interfaces:**
- Produces: `enum QuickSearchShortcut { CtrlAltD = 0, CtrlAltSpace = 1, CtrlShiftSpace = 2 }`; `QuickSearchShortcuts.Default`, `.All`, `.Text(QuickSearchShortcut)`; `QuickSearchSettings(bool IsOn, SearchBuddy Buddy, QuickSearchShortcut Shortcut = CtrlAltD)`; `QuickSearchSettingsService.ShortcutKey`, `SetShortcutAsync(QuickSearchShortcut, CancellationToken)`; `QuickSearchWords.Tooltip(QuickSearchShortcut)`, `WithChecking(string, QuickSearchShortcut)`; `QuickSearchHotKeys.For(QuickSearchShortcut) → (uint Modifiers, uint Key)`; `IQuickSearchHotKey.Listen(bool isOn, QuickSearchShortcut shortcut)`; `QuickSearchSwitch.SetShortcutAsync(QuickSearchShortcut) → Task<HotKeyState>`; `BackgroundPresenceController.SetQuickSearch(bool, QuickSearchShortcut)`; card: `ShortcutChoices`, `Shortcut`, `ShortcutIndex`, `SwitchHeader`, `SetShortcutAsync`; `RecordingHotKey.ListeningFor`, `.TakenByOthers`.

- [ ] **Step 1: Write the failing Core tests**

In `tests/DeskAI.Core.Tests/QuickSearchSettingsServiceTests.cs`, change `Choices_are_remembered` and add two tests:

```csharp
    [Fact]
    public async Task Choices_are_remembered()
    {
        var store = new FakeStore();
        var service = new QuickSearchSettingsService(store);
        var token = TestContext.Current.CancellationToken;

        await service.SetOnAsync(false, token);
        await service.SetBuddyAsync(SearchBuddy.Inky, token);
        await service.SetShortcutAsync(QuickSearchShortcut.CtrlShiftSpace, token);

        Assert.Equal(new QuickSearchSettings(false, SearchBuddy.Inky, QuickSearchShortcut.CtrlShiftSpace), await service.LoadAsync(token));
        Assert.Equal(["quicksearch.buddy", "quicksearch.on", "quicksearch.shortcut"], store.Values.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("CtrlShiftSpace", store.Values[QuickSearchSettingsService.ShortcutKey]);
        Assert.Subset(QuickSearchSettingsService.Keys.ToHashSet(StringComparer.Ordinal), store.Values.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.Contains("quicksearch.tip.dismissed", QuickSearchSettingsService.Keys);
    }

    [Fact]
    public void Nothing_remembered_means_Ctrl_Alt_D()
    {
        Assert.Equal(QuickSearchShortcut.CtrlAltD, QuickSearchSettings.Default.Shortcut);
        Assert.Equal(QuickSearchShortcut.CtrlAltD, QuickSearchShortcuts.Default);
        Assert.Equal(["Ctrl + Alt + D", "Ctrl + Alt + Space", "Ctrl + Shift + Space"], QuickSearchShortcuts.All.Select(QuickSearchShortcuts.Text));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("ctrlaltspace")]
    [InlineData("CtrlAltSpace ")]
    [InlineData("Win+R")]
    public async Task A_shortcut_DeskAI_does_not_know_falls_back_to_Ctrl_Alt_D(string stored)
    {
        var store = new FakeStore();
        store.Values[QuickSearchSettingsService.ShortcutKey] = stored;

        Assert.Equal(QuickSearchShortcut.CtrlAltD, (await new QuickSearchSettingsService(store).LoadAsync(TestContext.Current.CancellationToken)).Shortcut);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/DeskAI.Core.Tests/DeskAI.Core.Tests.csproj -c Release --no-restore`
Expected: build errors — `QuickSearchShortcut`, `SetShortcutAsync`, `ShortcutKey` do not exist.

- [ ] **Step 3: Write the Core code**

Create `src/DeskAI.Core/QuickSearch/QuickSearchShortcut.cs`:

```csharp
namespace DeskAI.Core.QuickSearch;

/// <summary>
/// The shortcuts quick search can listen for (the owner's choice, 2026-09-25). A closed list, so a
/// stored value can never name any other key. The order is the order of the drop-down.
/// </summary>
public enum QuickSearchShortcut { CtrlAltD = 0, CtrlAltSpace = 1, CtrlShiftSpace = 2 }

/// <summary>The words for each shortcut, and which one a fresh DeskAI uses.</summary>
/// <remarks>Ctrl + Alt + D is the default because Ctrl + Alt + Space clashed with the Claude desktop app on the owner's PC.</remarks>
public static class QuickSearchShortcuts
{
    public static QuickSearchShortcut Default => QuickSearchShortcut.CtrlAltD;

    public static IReadOnlyList<QuickSearchShortcut> All { get; } = Enum.GetValues<QuickSearchShortcut>();

    /// <summary>What a person reads, for example "Ctrl + Alt + D".</summary>
    public static string Text(QuickSearchShortcut shortcut) => shortcut switch
    {
        QuickSearchShortcut.CtrlAltSpace => "Ctrl + Alt + Space",
        QuickSearchShortcut.CtrlShiftSpace => "Ctrl + Shift + Space",
        _ => "Ctrl + Alt + D",
    };
}
```

In `QuickSearchSettings.cs`: the record becomes
`public sealed record QuickSearchSettings(bool IsOn, SearchBuddy Buddy, QuickSearchShortcut Shortcut = QuickSearchShortcut.CtrlAltD)`
(the summary: "Whether quick search listens, for which shortcut, and which buddy shows."); `Default` is unchanged
(`new(true, SearchBuddy.Sparky)`). In the service add

```csharp
    public const string ShortcutKey = "quicksearch.shortcut";
```

make `Keys` `[OnKey, BuddyKey, ShortcutKey, RetiredTipKey]`, read it in `LoadAsync`

```csharp
            var on = await _store.ReadAsync(OnKey, cancellationToken).ConfigureAwait(false);
            var buddy = await _store.ReadAsync(BuddyKey, cancellationToken).ConfigureAwait(false);
            var shortcut = await _store.ReadAsync(ShortcutKey, cancellationToken).ConfigureAwait(false);
            return new QuickSearchSettings(on != "no", ReadBuddy(buddy), ReadShortcut(shortcut));
```

and add

```csharp
    public Task SetShortcutAsync(QuickSearchShortcut shortcut, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(ShortcutKey, shortcut.ToString(), cancellationToken);

    /// <summary>Only an exact name from the list counts; anything else, numbers included, is Ctrl + Alt + D.</summary>
    private static QuickSearchShortcut ReadShortcut(string? stored) =>
        QuickSearchShortcuts.All.FirstOrDefault(shortcut => string.Equals(shortcut.ToString(), stored, StringComparison.Ordinal));
```

(`FirstOrDefault` on the enum gives `CtrlAltD` = 0 when nothing matches, the default.) Update the class summary to "quick search's small values".

Replace `QuickSearchWords.cs`'s body:

```csharp
/// <summary>What DeskAI says about quick search outside the bar: the icon's tooltip, naming the chosen shortcut.</summary>
/// <remarks>The tooltip field holds 128 characters; the longest checking tooltip plus the longest suffix stays well under.</remarks>
public static class QuickSearchWords
{
    public static string Tooltip(QuickSearchShortcut shortcut) => $"DeskAI — press {QuickSearchShortcuts.Text(shortcut)} to find a file";

    public static string WithChecking(string checkingTooltip, QuickSearchShortcut shortcut) =>
        $"{checkingTooltip} · {QuickSearchShortcuts.Text(shortcut)} finds a file";
}
```

- [ ] **Step 4: Run the Core tests**

Run: `dotnet test tests/DeskAI.Core.Tests/DeskAI.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QuickSearchSettingsServiceTests"`
Expected: PASS. (The solution will not build yet: Presentation still uses `QuickSearchWords.Tooltip` as a constant. The next steps fix that.)

- [ ] **Step 5: Write the failing page and key-table tests**

In `tests/DeskAI.Presentation.Tests/TestDoubles.cs`, replace `RecordingHotKey` (add `using DeskAI.Core.QuickSearch;` if missing):

```csharp
/// <summary>The shortcut, as a test can see it. Never registers anything with Windows.</summary>
internal sealed class RecordingHotKey : IQuickSearchHotKey
{
    public bool IsListening => ListeningFor is not null;

    /// <summary>The shortcut being listened for, or null when nothing listens.</summary>
    public QuickSearchShortcut? ListeningFor { get; private set; }

    /// <summary>The next Listen(true, …) answers as if another program had the shortcut.</summary>
    public bool RefuseNext { get; set; }

    /// <summary>Shortcuts that always answer as if another program had them.</summary>
    public HashSet<QuickSearchShortcut> TakenByOthers { get; } = [];

    public HotKeyState State { get; private set; } = HotKeyState.Off;

    public HotKeyState Listen(bool isOn, QuickSearchShortcut shortcut)
    {
        // Like GlobalHotKey: whatever was held is given back first.
        ListeningFor = null;
        if (!isOn)
        {
            return State = HotKeyState.Off;
        }

        if (RefuseNext || TakenByOthers.Contains(shortcut))
        {
            RefuseNext = false;
            return State = HotKeyState.TakenByAnotherProgram;
        }

        ListeningFor = shortcut;
        return State = HotKeyState.Listening;
    }

    public event EventHandler? Pressed;

    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);
}
```

Create `tests/DeskAI.Presentation.Tests/QuickSearchHotKeysTests.cs`:

```csharp
using DeskAI.App.Services;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>Each choice is one fixed combination, held down reports once, and no other key can be named.</summary>
public sealed class QuickSearchHotKeysTests
{
    [Theory]
    [InlineData(QuickSearchShortcut.CtrlAltD, 0x4003u, 0x44u)]
    [InlineData(QuickSearchShortcut.CtrlAltSpace, 0x4003u, 0x20u)]
    [InlineData(QuickSearchShortcut.CtrlShiftSpace, 0x4006u, 0x20u)]
    public void Each_choice_is_one_fixed_combination_without_repeat(QuickSearchShortcut shortcut, uint modifiers, uint key) =>
        Assert.Equal((modifiers, key), QuickSearchHotKeys.For(shortcut));

    [Fact]
    public void The_list_holds_exactly_three_shortcuts() =>
        Assert.Equal(3, Enum.GetValues<QuickSearchShortcut>().Length);
}
```

In `QuickSearchSettingsPageTests.cs`:
- In `Another_program_using_the_shortcut_is_said_plainly`, expect `"Another program already uses Ctrl + Alt + D. Pick another shortcut above."`.
- In `With_quick_search_on_and_checking_off_…`, expect `QuickSearchWords.Tooltip(QuickSearchShortcut.CtrlAltD)`.
- In `With_checking_on_…`, expect `QuickSearchWords.WithChecking(BackgroundCheckingChoice.Tooltip(checking, null), QuickSearchShortcut.CtrlAltD)`.
- In `The_longest_tooltip_fits_what_Windows_allows`, loop over every shortcut:

```csharp
    [Fact]
    public void The_longest_tooltip_fits_what_Windows_allows()
    {
        var paused = AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground, IsPaused = true };
        var running = AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground };

        foreach (var shortcut in QuickSearchShortcuts.All)
        {
            foreach (var tooltip in new[]
            {
                QuickSearchWords.Tooltip(shortcut),
                QuickSearchWords.WithChecking(BackgroundCheckingChoice.Tooltip(paused, 999), shortcut),
                QuickSearchWords.WithChecking(BackgroundCheckingChoice.Tooltip(running, 999), shortcut),
            })
            {
                Assert.True(tooltip.Length < 128, tooltip);
            }
        }
    }
```

- Extend `Start_fresh_turns_quick_search_back_on`: before Start fresh add
  `await (await OpenCardAsync(app)).SetShortcutAsync(QuickSearchShortcut.CtrlShiftSpace);`; after it add
  `Assert.Equal(QuickSearchShortcut.CtrlAltD, app.HotKey.ListeningFor);` and `Assert.Equal(QuickSearchShortcut.CtrlAltD, card.Shortcut);`.
- Add these tests:

```csharp
    [Fact]
    public async Task A_fresh_DeskAI_listens_for_Ctrl_Alt_D_and_offers_three_shortcuts()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        var card = await OpenCardAsync(app);

        Assert.Equal(QuickSearchShortcut.CtrlAltD, app.HotKey.ListeningFor);
        Assert.Equal(["Ctrl + Alt + D", "Ctrl + Alt + Space", "Ctrl + Shift + Space"], QuickSearchCardViewModel.ShortcutChoices);
        Assert.Equal(0, card.ShortcutIndex);
        Assert.Equal("Press Ctrl + Alt + D to find a file", card.SwitchHeader);
        Assert.Equal("DeskAI — press Ctrl + Alt + D to find a file", app.Presence.Tooltips[^1]);
    }

    [Fact]
    public async Task Choosing_a_shortcut_listens_for_it_names_it_and_is_kept()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        var card = await OpenCardAsync(app);

        await card.SetShortcutAsync(QuickSearchShortcut.CtrlShiftSpace);

        Assert.Equal(QuickSearchShortcut.CtrlShiftSpace, app.HotKey.ListeningFor);
        Assert.Equal(2, card.ShortcutIndex);
        Assert.Equal("Press Ctrl + Shift + Space to find a file", card.SwitchHeader);
        Assert.Equal("DeskAI — press Ctrl + Shift + Space to find a file", app.Presence.Tooltips[^1]);
        Assert.False(card.HasShortcutProblem);

        await using var reopened = await app.ReopenAsync();
        await reopened.Get<QuickSearchSwitch>().ApplyStoredAsync();
        Assert.Equal(QuickSearchShortcut.CtrlShiftSpace, reopened.HotKey.ListeningFor);
        Assert.Equal(QuickSearchShortcut.CtrlShiftSpace, (await OpenCardAsync(reopened)).Shortcut);
    }

    [Fact]
    public async Task A_shortcut_another_program_uses_is_named_and_nothing_listens_until_another_is_picked()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        app.HotKey.TakenByOthers.Add(QuickSearchShortcut.CtrlAltSpace);
        var card = await OpenCardAsync(app);

        await card.SetShortcutAsync(QuickSearchShortcut.CtrlAltSpace);

        Assert.Equal("Another program already uses Ctrl + Alt + Space. Pick another shortcut above.", card.ShortcutProblem);
        Assert.False(app.HotKey.IsListening);

        await card.SetShortcutAsync(QuickSearchShortcut.CtrlShiftSpace);

        Assert.False(card.HasShortcutProblem);
        Assert.Equal(QuickSearchShortcut.CtrlShiftSpace, app.HotKey.ListeningFor);
    }

    [Fact]
    public async Task A_refused_shortcut_is_asked_for_again_when_the_switch_is_turned_back_on()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        app.HotKey.TakenByOthers.Add(QuickSearchShortcut.CtrlAltSpace);
        var card = await OpenCardAsync(app);
        await card.SetShortcutAsync(QuickSearchShortcut.CtrlAltSpace);

        await card.SetOnAsync(false);
        Assert.False(card.HasShortcutProblem);

        await card.SetOnAsync(true);
        Assert.Equal("Another program already uses Ctrl + Alt + Space. Pick another shortcut above.", card.ShortcutProblem);

        app.HotKey.TakenByOthers.Clear();
        await card.SetOnAsync(false);
        await card.SetOnAsync(true);
        Assert.Equal(QuickSearchShortcut.CtrlAltSpace, app.HotKey.ListeningFor);
    }

    [Fact]
    public async Task Choosing_a_shortcut_while_off_only_remembers_it()
    {
        await using var app = await TestApp.StartAsync();
        var card = await OpenCardAsync(app);
        await card.SetOnAsync(false);

        await card.SetShortcutAsync(QuickSearchShortcut.CtrlAltSpace);
        Assert.False(app.HotKey.IsListening);
        Assert.False(app.Presence.IsShowing);

        await card.SetOnAsync(true);
        Assert.Equal(QuickSearchShortcut.CtrlAltSpace, app.HotKey.ListeningFor);
    }

    [Fact]
    public async Task Picking_the_shortcut_already_in_use_changes_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        var card = await OpenCardAsync(app);

        await card.SetShortcutAsync(QuickSearchShortcut.CtrlAltD);

        Assert.Equal(QuickSearchShortcut.CtrlAltD, app.HotKey.ListeningFor);
        Assert.False(card.HasShortcutProblem);
    }
```

In `QuickSearchWelcomePageTests.cs`, change the body assertion to
`"Press Ctrl + Alt + D in any app. Type what you're looking for, and press Enter to open it."` and add:

```csharp
    [Fact]
    public async Task The_welcome_names_the_chosen_shortcut()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSettingsService>().SetShortcutAsync(QuickSearchShortcut.CtrlShiftSpace, TestContext.Current.CancellationToken);
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();

        welcome.Next();
        welcome.Next();

        Assert.Equal("Press Ctrl + Shift + Space in any app. Type what you're looking for, and press Enter to open it.", welcome.Current.Body);
    }
```

(add `using DeskAI.Core.QuickSearch;`). In `FreshStartPageTests.cs` line 67 use `QuickSearchWords.Tooltip(QuickSearchShortcut.CtrlAltD)`; in the test around line 110 also write
`await quick.SetShortcutAsync(QuickSearchShortcut.CtrlAltSpace, TestContext.Current.CancellationToken);` before Start fresh and assert
`Assert.Null(await store.ReadAsync(QuickSearchSettingsService.ShortcutKey, TestContext.Current.CancellationToken));` after.

In `QuickSearchLayoutTests.cs`, replace the first test and adjust two others:

```csharp
    [Fact]
    public void The_shortcut_comes_from_the_fixed_list_one_at_a_time_without_a_keyboard_hook()
    {
        var hotKey = Read("src", "DeskAI.App", "Services", "GlobalHotKey.cs") + Read("src", "DeskAI.App", "Services", "HotKeyInterop.cs");

        Assert.Contains("RegisterHotKey", hotKey, StringComparison.Ordinal);
        Assert.Contains("QuickSearchHotKeys.For(shortcut)", hotKey, StringComparison.Ordinal);
        Assert.Contains("UnregisterHotKey(_window, HotKeyId)", hotKey, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowsHookEx", hotKey, StringComparison.Ordinal);
        Assert.DoesNotContain("WH_KEYBOARD", hotKey, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAsyncKeyState", hotKey, StringComparison.Ordinal);
    }
```

In `Closing_the_main_window_for_real_…` expect `"Listen(false, "` instead of `"Listen(false)"`. In
`The_Quick_search_card_and_the_welcome_picture_are_placed` expect
`Header="{x:Bind ViewModel.QuickSearch.SwitchHeader, Mode=OneWay}"` and `Header="Shortcut"` instead of the fixed header.
In `Home_and_Search_carry_no_quick_search_tip` also assert `DoesNotContain("Ctrl + Alt + D", …)`.

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet build tests/DeskAI.Presentation.Tests/DeskAI.Presentation.Tests.csproj -c Release --no-restore`
Expected: build errors — `QuickSearchHotKeys`, `SetShortcutAsync`, `ShortcutChoices`, `Listen(bool, QuickSearchShortcut)` do not exist.

- [ ] **Step 7: Write the Presentation code**

Create `src/DeskAI.Presentation/Services/QuickSearchHotKeys.cs`:

```csharp
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Services;

/// <summary>
/// The fixed Windows key values for each shortcut in the closed list (ADR 0047). Here rather than
/// in the App so a test can check the table; the App only passes these values to RegisterHotKey.
/// </summary>
public static class QuickSearchHotKeys
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;

    /// <summary>Holding the keys down reports once, not over and over.</summary>
    public const uint ModNoRepeat = 0x4000;

    public const uint KeySpace = 0x20;
    public const uint KeyD = 0x44;

    public static (uint Modifiers, uint Key) For(QuickSearchShortcut shortcut) => shortcut switch
    {
        QuickSearchShortcut.CtrlAltSpace => (ModControl | ModAlt | ModNoRepeat, KeySpace),
        QuickSearchShortcut.CtrlShiftSpace => (ModControl | ModShift | ModNoRepeat, KeySpace),
        _ => (ModControl | ModAlt | ModNoRepeat, KeyD),
    };
}
```

In `IQuickSearchHotKey.cs`: the summary becomes "The quick search shortcut the person picked, anywhere in Windows (ADR 0047)."; the method becomes

```csharp
    /// <summary>
    /// Starts listening for this shortcut, or stops. Whatever was held before is given back first,
    /// so DeskAI never holds two. Returns what happened, including Windows' refusal.
    /// </summary>
    HotKeyState Listen(bool isOn, QuickSearchShortcut shortcut);
```

(add `using DeskAI.Core.QuickSearch;`) and `NoQuickSearchHotKey.Listen(bool isOn, QuickSearchShortcut shortcut) => HotKeyState.Unavailable;`.

In `BackgroundPresenceController.cs`: add a field `private QuickSearchShortcut _shortcut = QuickSearchShortcuts.Default;`, change `SetQuickSearch` to

```csharp
    /// <summary>Quick search was switched on or off, or its shortcut changed (ADR 0047). It too keeps DeskAI near the clock.</summary>
    public void SetQuickSearch(bool isOn, QuickSearchShortcut shortcut)
    {
        _quickSearchOn = isOn;
        _shortcut = shortcut;
        Apply();
    }
```

and in `Apply` use `QuickSearchWords.Tooltip(_shortcut)` and `QuickSearchWords.WithChecking(tooltip, _shortcut)`.

Replace `QuickSearchSwitch`'s body (keep the class summary; add "and which shortcut it listens for" to it):

```csharp
    /// <summary>At startup and after Start fresh: do what is stored.</summary>
    public async Task<HotKeyState> ApplyStoredAsync()
    {
        var stored = await _settings.LoadAsync().ConfigureAwait(true);
        return Apply(stored.IsOn, stored.Shortcut);
    }

    public async Task<HotKeyState> SetOnAsync(bool on)
    {
        await _settings.SetOnAsync(on).ConfigureAwait(true);
        return Apply(on, (await _settings.LoadAsync().ConfigureAwait(true)).Shortcut);
    }

    /// <summary>
    /// Remembers the shortcut and, while quick search is on, listens for it at once. A refused
    /// shortcut is not replaced by the old one: nothing listens until another is picked.
    /// </summary>
    public async Task<HotKeyState> SetShortcutAsync(QuickSearchShortcut shortcut)
    {
        await _settings.SetShortcutAsync(shortcut).ConfigureAwait(true);
        return Apply((await _settings.LoadAsync().ConfigureAwait(true)).IsOn, shortcut);
    }

    private HotKeyState Apply(bool on, QuickSearchShortcut shortcut)
    {
        var state = _hotKey.Listen(on, shortcut);
        _presence.SetQuickSearch(on, shortcut);
        return state;
    }
```

In `QuickSearchCardViewModel`: update the summary to "the switch, the shortcut, the seven buddies, and a shortcut problem"; add

```csharp
    private QuickSearchShortcut _shortcut = QuickSearchShortcuts.Default;

    /// <summary>The drop-down's choices, in the list's order.</summary>
    public static IReadOnlyList<string> ShortcutChoices { get; } = [.. QuickSearchShortcuts.All.Select(QuickSearchShortcuts.Text)];

    public QuickSearchShortcut Shortcut
    {
        get => _shortcut;
        private set
        {
            if (SetProperty(ref _shortcut, value))
            {
                OnPropertyChanged(nameof(ShortcutIndex));
                OnPropertyChanged(nameof(SwitchHeader));
            }
        }
    }

    /// <summary>The drop-down's selected row: the enum's values are 0, 1, 2 in the list's order.</summary>
    public int ShortcutIndex => (int)_shortcut;

    public string SwitchHeader => $"Press {QuickSearchShortcuts.Text(_shortcut)} to find a file";

    public async Task SetShortcutAsync(QuickSearchShortcut shortcut)
    {
        Shortcut = shortcut;
        Report(await _switch.SetShortcutAsync(shortcut).ConfigureAwait(true));
    }
```

set `Shortcut = stored.Shortcut;` in `InitializeAsync` (before `Report`), and change `Report` to

```csharp
    private void Report(HotKeyState state) => ShortcutProblem = state == HotKeyState.TakenByAnotherProgram
        ? $"Another program already uses {QuickSearchShortcuts.Text(_shortcut)}. Pick another shortcut above."
        : string.Empty;
```

In `WelcomeViewModel`: add a constructor parameter `QuickSearchSettingsService quickSearch` (after `aiSettings`; add `using DeskAI.Core.QuickSearch;`), and replace the static `Pages` with

```csharp
    private readonly QuickSearchSettingsService _quickSearch = quickSearch;
    private IReadOnlyList<WelcomePage> _pages = PagesFor(QuickSearchShortcuts.Default);

    /// <summary>The four pages; the third names the shortcut the person picked.</summary>
    public IReadOnlyList<WelcomePage> Pages => _pages;

    public static IReadOnlyList<WelcomePage> PagesFor(QuickSearchShortcut shortcut) =>
    [
        new("Welcome to DeskAI", "Find your files and keep them tidy.", []),
        new("You stay in charge", string.Empty,
        [
            "DeskAI sees nothing until you connect a folder.",
            "It only works in your Desktop, Downloads, Documents, and Pictures.",
            "Nothing moves until you see it and say yes.",
            "You can put things back.",
        ]),
        new("Find any file, from anywhere",
            $"Press {QuickSearchShortcuts.Text(shortcut)} in any app. Type what you're looking for, and press Enter to open it.", [], ShowsBuddy: true),
        new("Let's start", "Connect a folder to begin. DeskAI asks once more before connecting.", []),
    ];
```

and at the start of `OpenAsync`:

```csharp
        // LoadAsync never throws: a store that cannot be read gives the default shortcut.
        _pages = PagesFor((await _quickSearch.LoadAsync().ConfigureAwait(true)).Shortcut);
        PageIndex = 0;
        OnPropertyChanged(nameof(Current));
```

(replacing the existing `PageIndex = 0;`). Update the class remarks: "It holds no settings store it writes to".

In `HelpCatalog.cs`, the `workspace.quicksearch` first line becomes
`"A small search bar you open from any app with a shortcut: Ctrl + Alt + D, or the one you pick on this card.",`.

- [ ] **Step 8: Write the App code**

In `HotKeyInterop.cs` remove `MOD_ALT`, `MOD_CONTROL`, `MOD_NOREPEAT`, `VK_SPACE` (the values now live in `QuickSearchHotKeys`); keep `WM_HOTKEY`, `ERROR_HOTKEY_ALREADY_REGISTERED`, `HWND_MESSAGE` and the two imports.

In `GlobalHotKey.cs`: the summary becomes "The quick search shortcut the person picked, anywhere in Windows (ADR 0047)."; add `using DeskAI.Core.QuickSearch;`, a field `private QuickSearchShortcut _registeredShortcut;`, and replace `Listen`:

```csharp
    public HotKeyState Listen(bool isOn, QuickSearchShortcut shortcut)
    {
        if (_registered && (!isOn || _registeredShortcut != shortcut))
        {
            // Never two at once: the old combination is given back before a new one is asked for.
            HotKeyInterop.UnregisterHotKey(_window, HotKeyId);
            _registered = false;
        }

        if (!isOn)
        {
            return State = HotKeyState.Off;
        }

        if (_registered)
        {
            return State = HotKeyState.Listening;
        }

        if (!EnsureMessageWindow())
        {
            return State = HotKeyState.Unavailable;
        }

        var (modifiers, key) = QuickSearchHotKeys.For(shortcut);
        if (HotKeyInterop.RegisterHotKey(_window, HotKeyId, modifiers, key))
        {
            _registered = true;
            _registeredShortcut = shortcut;
            return State = HotKeyState.Listening;
        }

        var error = Marshal.GetLastWin32Error();
        LogShortcutRefused(_logger, error);
        return State = error == HotKeyInterop.ERROR_HOTKEY_ALREADY_REGISTERED
            ? HotKeyState.TakenByAnotherProgram
            : HotKeyState.Unavailable;
    }
```

In `App.xaml.cs`, the two `Listen(false)` calls become `Listen(false, QuickSearchShortcuts.Default)` (add `using DeskAI.Core.QuickSearch;`), and the comments that say "Ctrl + Alt + Space" say "the quick search shortcut".

In `WorkspacePage.xaml`, the switch and a new drop-down under it:

```xml
                    <ToggleSwitch x:Name="QuickSearchSwitch"
                                  Header="{x:Bind ViewModel.QuickSearch.SwitchHeader, Mode=OneWay}"
                                  IsOn="{x:Bind ViewModel.QuickSearch.IsOn, Mode=OneWay}"
                                  Toggled="OnQuickSearchToggled" />
                    <ComboBox x:Name="ShortcutChoice"
                              Header="Shortcut"
                              MinWidth="220"
                              ItemsSource="{x:Bind viewmodels:QuickSearchCardViewModel.ShortcutChoices}"
                              SelectedIndex="{x:Bind ViewModel.QuickSearch.ShortcutIndex, Mode=OneWay}"
                              SelectionChanged="OnShortcutChosen" />
```

Move the existing shortcut-problem `TextBlock` so it sits right under the drop-down (above the "Works while…" caption), so "Pick another shortcut above" points at it. In `WorkspacePage.xaml.cs` (add `using DeskAI.Core.QuickSearch;`):

```csharp
    /// <summary>Only a choice the person made reaches the switch, never the page setting its own value.</summary>
    private async void OnShortcutChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: >= 0 } box && box.SelectedIndex != ViewModel.QuickSearch.ShortcutIndex)
        {
            await ViewModel.QuickSearch.SetShortcutAsync((QuickSearchShortcut)box.SelectedIndex);
        }
    }
```

- [ ] **Step 9: Build and run everything**

Run the build commands (see "Build commands"; check whether DeskAI is open first).
Expected: 0 warnings, all tests PASS, formatting clean. Fix any other place the compiler names (search: `grep -rn "Listen(\|QuickSearchWords.Tooltip\|SetQuickSearch(" src tests --include=*.cs`).

- [ ] **Step 10: Update the docs**

- `docs/TESTING.md`, the "My workspace | Quick search card" row: add "the shortcut drop-down (Ctrl + Alt + D by default, Ctrl + Alt + Space, Ctrl + Shift + Space) listens for the choice at once, names it on the switch and the icon, is kept, only remembered while off; a shortcut another program uses is named with 'Pick another shortcut above' and nothing listens; the welcome names the chosen shortcut; Start fresh goes back to Ctrl + Alt + D", and add `QuickSearchHotKeysTests`, `QuickSearchWelcomePageTests` to its test list. In the Home welcome row say "the shortcut the person picked".
- `docs/UI-UX.md` (the quick search section, lines ~337 and ~351): the bar opens with "the shortcut picked on My workspace (Ctrl + Alt + D to start)"; the card has the switch "Press {shortcut} to find a file" and a **Shortcut** drop-down under it with the three choices and the refusal line.
- `docs/USER-GUIDE.md` (lines ~122 and ~146): "Press **Ctrl + Alt + D** in any app…" and "You can pick another shortcut (Ctrl + Alt + Space or Ctrl + Shift + Space) on **My workspace → Quick search**. If another program already uses the one you picked, DeskAI says so; pick another."
- `README.md` line ~68 and `docs/PRODUCT.md` line ~77: Ctrl + Alt + D, "(you can pick another on My workspace)".

- [ ] **Step 11: Review the diff and commit**

Check `git diff` for personal paths, secrets, and wording. Then:

```bash
git add -A src tests docs README.md
git commit -m "Let people pick the quick search shortcut, Ctrl + Alt + D by default

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: "Let my buddy move"

**Files:**
- Create: `src/DeskAI.Presentation/Services/BuddyMotion.cs`
- Modify: `src/DeskAI.Core/QuickSearch/QuickSearchSettings.cs`
- Modify: `src/DeskAI.Presentation/Services/QuickSearchSwitch.cs`, `Composition/DeskAiApplicationServices.cs`
- Modify: `src/DeskAI.Presentation/ViewModels/QuickSearchCardViewModel.cs`, `WelcomeViewModel.cs`
- Modify: `src/DeskAI.App/Views/Buddies/BuddyControl.cs`, `BuddyFactory.cs`, `src/DeskAI.App/Views/WelcomeDialog.cs`, `QuickSearchWindow.xaml.cs`, `App.xaml.cs`, `WorkspacePage.xaml` + `.cs`
- Test: `tests/DeskAI.Core.Tests/QuickSearchSettingsServiceTests.cs`, `tests/DeskAI.Presentation.Tests/QuickSearchSettingsPageTests.cs`, `QuickSearchLayoutTests.cs`, `FreshStartPageTests.cs`
- Docs: `docs/TESTING.md`, `docs/UI-UX.md`, `docs/USER-GUIDE.md`

**Interfaces:**
- Consumes: Task 1's `QuickSearchSettings(bool, SearchBuddy, QuickSearchShortcut)`, `QuickSearchSwitch`.
- Produces: `QuickSearchSettings(…, bool LetsBuddyMove = true)`; `QuickSearchSettingsService.MotionKey`, `SetMotionAsync(bool, CancellationToken)`; `BuddyMotion { bool IsOn; event EventHandler? Changed; void Set(bool) }` (namespace `DeskAI.App.Services`, singleton); `QuickSearchSwitch.SetMotionAsync(bool)`; card `LetsBuddyMove`, `MotionLine`, `Motion`, `SetMotionAsync(bool)`; `WelcomeViewModel.Motion`; `BuddyControl.Motion` (`BuddyMotion?`, null = still); `BuddyFactory.Create(SearchBuddy, BuddyMotion?)`; `QuickSearchWindow(QuickSearchViewModel, BuddyMotion)`.

- [ ] **Step 1: Write the failing tests**

Core (`QuickSearchSettingsServiceTests`): in `Choices_are_remembered` also call `await service.SetMotionAsync(false, token);`, expect
`new QuickSearchSettings(false, SearchBuddy.Inky, QuickSearchShortcut.CtrlShiftSpace, LetsBuddyMove: false)` and the keys
`["quicksearch.buddy", "quicksearch.motion", "quicksearch.on", "quicksearch.shortcut"]`. Add:

```csharp
    [Theory]
    [InlineData(null, true)]
    [InlineData("no", false)]
    [InlineData("yes", true)]
    [InlineData("NO", true)]
    public async Task Buddies_move_unless_no_is_remembered(string? stored, bool moves)
    {
        var store = new FakeStore();
        if (stored is not null)
        {
            store.Values[QuickSearchSettingsService.MotionKey] = stored;
        }

        Assert.Equal(moves, (await new QuickSearchSettingsService(store).LoadAsync(TestContext.Current.CancellationToken)).LetsBuddyMove);
    }
```

Page tests (`QuickSearchSettingsPageTests`):

```csharp
    [Fact]
    public async Task Buddies_move_until_the_switch_is_turned_off_and_the_choice_is_kept()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        var card = await OpenCardAsync(app);

        Assert.True(card.LetsBuddyMove);
        Assert.True(app.Get<BuddyMotion>().IsOn);
        Assert.Equal("Turn this off to keep your buddy and the search bar still.", QuickSearchCardViewModel.MotionLine);

        var changes = 0;
        app.Get<BuddyMotion>().Changed += (_, _) => changes++;
        await card.SetMotionAsync(false);

        Assert.False(card.LetsBuddyMove);
        Assert.False(app.Get<BuddyMotion>().IsOn);
        Assert.Equal(1, changes);
        Assert.Same(app.Get<BuddyMotion>(), card.Motion);

        await using var reopened = await app.ReopenAsync();
        await reopened.Get<QuickSearchSwitch>().ApplyStoredAsync();
        Assert.False(reopened.Get<BuddyMotion>().IsOn);
        Assert.False((await OpenCardAsync(reopened)).LetsBuddyMove);
    }

    [Fact]
    public async Task The_welcome_buddy_follows_the_same_switch()
    {
        await using var app = await TestApp.StartAsync();

        Assert.Same(app.Get<BuddyMotion>(), app.Get<WelcomeViewModel>().Motion);
    }
```

Extend `Start_fresh_turns_quick_search_back_on`: before Start fresh add `await (await OpenCardAsync(app)).SetMotionAsync(false);`; after it
`Assert.True(app.Get<BuddyMotion>().IsOn);` and `Assert.True(card.LetsBuddyMove);`. In `FreshStartPageTests` (the test near line 110) also
write `await quick.SetMotionAsync(false, …)` and assert `MotionKey` reads back `null`.

Layout tests: replace `Moves_stop_when_Windows_animation_effects_are_off` with

```csharp
    /// <summary>
    /// Owner-found 2026-09-25: Windows' Animation effects was off on their PC, so no buddy ever
    /// moved. DeskAI's own "Let my buddy move" switch decides instead (the owner's choice).
    /// </summary>
    [Fact]
    public void Moves_follow_DeskAIs_own_switch_not_Windows()
    {
        var control = Read("src", "DeskAI.App", "Views", "Buddies", "BuddyControl.cs");

        Assert.DoesNotContain("AnimationsEnabled", control, StringComparison.Ordinal);
        Assert.DoesNotContain("UISettings", control, StringComparison.Ordinal);
        Assert.Contains("Motion is { IsOn: true }", control, StringComparison.Ordinal);
        Assert.Contains("\"Still\"", control, StringComparison.Ordinal);
        Assert.Contains("Header=\"Let my buddy move\"", Read("src", "DeskAI.App", "Views", "WorkspacePage.xaml"), StringComparison.Ordinal);
        Assert.Contains("Motion = welcome.Motion", Read("src", "DeskAI.App", "Views", "WelcomeDialog.cs"), StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/DeskAI.Presentation.Tests/DeskAI.Presentation.Tests.csproj -c Release --no-restore`
Expected: build errors — `BuddyMotion`, `SetMotionAsync`, `LetsBuddyMove`, `MotionKey` do not exist.

- [ ] **Step 3: Write the Core and Presentation code**

`QuickSearchSettings`: the record becomes
`(bool IsOn, SearchBuddy Buddy, QuickSearchShortcut Shortcut = QuickSearchShortcut.CtrlAltD, bool LetsBuddyMove = true)`.
The service adds `public const string MotionKey = "quicksearch.motion";`, adds it to `Keys`, reads it in `LoadAsync`
(`var motion = await _store.ReadAsync(MotionKey, …)`; `LetsBuddyMove: motion != "no"`), and adds

```csharp
    public Task SetMotionAsync(bool moves, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(MotionKey, moves ? "yes" : "no", cancellationToken);
```

Create `src/DeskAI.Presentation/Services/BuddyMotion.cs`:

```csharp
namespace DeskAI.App.Services;

/// <summary>
/// Whether buddies move: DeskAI's own "Let my buddy move" switch, which wins over Windows'
/// Animation effects (the owner's choice, 2026-09-25).
/// </summary>
/// <remarks>
/// One singleton, so the bar, the chooser's stage, and the welcome's Sparky follow the same
/// switch, and a buddy already on screen stops or starts the moment the switch changes. Only
/// <see cref="QuickSearchSwitch"/> sets it.
/// </remarks>
public sealed class BuddyMotion
{
    public bool IsOn { get; private set; } = true;

    /// <summary>Raised on the thread that changed it; a buddy moves back to its own thread before redrawing.</summary>
    public event EventHandler? Changed;

    public void Set(bool isOn)
    {
        if (IsOn == isOn)
        {
            return;
        }

        IsOn = isOn;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

Register it in `DeskAiApplicationServices` next to `QuickSearchSwitch`:

```csharp
        // Whether buddies move (DeskAI's own switch, not Windows'). One value everyone reads.
        services.AddSingleton<BuddyMotion>();
```

`QuickSearchSwitch` gets a fourth constructor parameter `BuddyMotion motion`; `ApplyStoredAsync` becomes

```csharp
    public async Task<HotKeyState> ApplyStoredAsync()
    {
        var stored = await _settings.LoadAsync().ConfigureAwait(true);
        _motion.Set(stored.LetsBuddyMove);
        return Apply(stored.IsOn, stored.Shortcut);
    }
```

and it adds

```csharp
    /// <summary>"Let my buddy move": remembered, and every buddy on screen follows at once.</summary>
    public async Task SetMotionAsync(bool moves)
    {
        await _settings.SetMotionAsync(moves).ConfigureAwait(true);
        _motion.Set(moves);
    }
```

`QuickSearchCardViewModel` gets a fourth constructor parameter `BuddyMotion motion` and:

```csharp
    public const string MotionLine = "Turn this off to keep your buddy and the search bar still.";

    private bool _letsBuddyMove = true;

    /// <summary>The same line, for the page to bind to.</summary>
    public static string MotionText => MotionLine;

    /// <summary>What the page's buddies follow.</summary>
    public BuddyMotion Motion { get; } = motion;

    public bool LetsBuddyMove { get => _letsBuddyMove; private set => SetProperty(ref _letsBuddyMove, value); }

    public async Task SetMotionAsync(bool moves)
    {
        LetsBuddyMove = moves;
        await _switch.SetMotionAsync(moves).ConfigureAwait(true);
    }
```

and `LetsBuddyMove = stored.LetsBuddyMove;` in `InitializeAsync`. `WelcomeViewModel` gets a constructor parameter `BuddyMotion motion`
and `public BuddyMotion Motion { get; } = motion;` with the summary "What the welcome's Sparky follows."

- [ ] **Step 4: Write the App code**

`BuddyControl.cs`: update the class summary ("holds it still when DeskAI's 'Let my buddy move' switch is off"), remove the `UISettings` field, and:

```csharp
    public BuddyControl()
    {
        Loaded += (_, _) =>
        {
            if (Motion is not null)
            {
                Motion.Changed += OnMotionChanged;
            }

            GoToMood();
        };
        Unloaded += (_, _) =>
        {
            if (Motion is not null)
            {
                Motion.Changed -= OnMotionChanged;
            }
        };
        IsTabStop = false;
    }

    /// <summary>The switch this buddy follows. Null means a still picture.</summary>
    public BuddyMotion? Motion { get; set; }

    private void OnMotionChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(GoToMood);

    private void GoToMood()
    {
        var mood = Mood.ToString();
        VisualStateManager.GoToState(this, mood, useTransitions: false);
        VisualStateManager.GoToState(this, Motion is { IsOn: true } && !HoldsStill ? mood + "Moving" : "Still", useTransitions: false);
    }
```

(add `using DeskAI.App.Services;`). `BuddyFactory`:

```csharp
    public static BuddyControl Create(SearchBuddy buddy, BuddyMotion? motion)
    {
        BuddyControl control = buddy switch
        {
            SearchBuddy.Archie => new ArchieBuddy(),
            SearchBuddy.Pip => new PipBuddy(),
            SearchBuddy.Fetch => new FetchBuddy(),
            SearchBuddy.Inky => new InkyBuddy(),
            SearchBuddy.Mochi => new MochiBuddy(),
            SearchBuddy.Paige => new PaigeBuddy(),
            _ => new SparkyBuddy(),
        };
        control.Motion = motion;
        return control;
    }
```

`QuickSearchWindow`: the constructor becomes `QuickSearchWindow(QuickSearchViewModel viewModel, BuddyMotion motion)`, stores
`private readonly BuddyMotion _motion = …;` (assign before `ShowBuddy()`), and `ShowBuddy` calls `BuddyFactory.Create(ViewModel.Buddy, _motion)`.
In `App.xaml.cs` `ConnectQuickSearchAsync`:
`new QuickSearchWindow(_host.Services.GetRequiredService<QuickSearchViewModel>(), _host.Services.GetRequiredService<BuddyMotion>())`.
`WelcomeDialog`: `new Buddies.SparkyBuddy { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left, Motion = welcome.Motion }`.
`WorkspacePage.xaml.cs` `OnBuddyPictureLoaded`: `BuddyFactory.Create(tile.Buddy, motion: null)` (the tiles stay still pictures; Task 3 replaces them).

`WorkspacePage.xaml`, after the buddy tiles (still inside the Quick search `StackPanel`):

```xml
                    <ToggleSwitch x:Name="BuddyMotionSwitch"
                                  Header="Let my buddy move"
                                  IsOn="{x:Bind ViewModel.QuickSearch.LetsBuddyMove, Mode=OneWay}"
                                  Toggled="OnBuddyMotionToggled" />
                    <TextBlock Style="{StaticResource CaptionStyle}" MaxWidth="620" HorizontalAlignment="Left" TextWrapping="Wrap"
                               Text="{x:Bind viewmodels:QuickSearchCardViewModel.MotionText}" />
```

`WorkspacePage.xaml.cs`:

```csharp
    /// <summary>Only a change the person made reaches the switch, never the page setting its own value.</summary>
    private async void OnBuddyMotionToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.QuickSearch.LetsBuddyMove)
        {
            await ViewModel.QuickSearch.SetMotionAsync(toggle.IsOn);
        }
    }
```

- [ ] **Step 5: Build and run everything**

Run the build commands. Expected: 0 warnings, all tests PASS, formatting clean.

- [ ] **Step 6: Update the docs**

- `docs/TESTING.md`, the Quick search card row: add "'Let my buddy move' is on by default, turns every buddy still at once, is kept, wins over Windows' Animation effects; Start fresh turns it back on".
- `docs/UI-UX.md` quick search section: replace any "still when Windows' Animation effects are off" with "still when **Let my buddy move** is off (on the Quick search card; DeskAI's own switch wins over Windows' Animation effects, the owner's choice)".
- `docs/USER-GUIDE.md` quick search section: "Your buddy moves. To keep it still, turn off **Let my buddy move** on My workspace."

- [ ] **Step 7: Review the diff and commit**

```bash
git add -A src tests docs
git commit -m "Add a Let my buddy move switch that wins over Windows' animation setting

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The buddy stage and faces

**Files:**
- Create: `src/DeskAI.App/Views/Buddies/BuddyStageBrushes.cs`, `src/DeskAI.App/Views/Buddies/BuddyAnimations.cs`
- Modify: `src/DeskAI.Presentation/ViewModels/QuickSearchCardViewModel.cs`
- Modify: `src/DeskAI.App/Views/WorkspacePage.xaml`, `WorkspacePage.xaml.cs`
- Test: `tests/DeskAI.Presentation.Tests/QuickSearchSettingsPageTests.cs`, `QuickSearchLayoutTests.cs`
- Docs: `docs/TESTING.md`, `docs/UI-UX.md`, `docs/USER-GUIDE.md`

**Interfaces:**
- Consumes: Task 2's `BuddyFactory.Create(SearchBuddy, BuddyMotion?)`, card `Motion`.
- Produces: `BuddyTileViewModel.HelloLine`, `.Stage` (`"night"`, `"study"`, `"lab"`, `"forest"`, `"sea"`, `"meadow"`, `"dusk"`); card `Chosen` (`BuddyTileViewModel`), `ChosenIndex` (`int`); `BuddyStageBrushes.For(string) → Brush`; `BuddyAnimations.PopIn(UIElement element, TimeSpan delay)` (used again by Task 5).

- [ ] **Step 1: Write the failing tests**

`QuickSearchSettingsPageTests`:

```csharp
    [Fact]
    public async Task The_stage_starts_with_Sparky_at_night()
    {
        await using var app = await TestApp.StartAsync();
        var card = await OpenCardAsync(app);

        Assert.Equal("Sparky", card.Chosen.Name);
        Assert.Equal("Hi! What are we looking for?", card.Chosen.HelloLine);
        Assert.Equal("night", card.Chosen.Stage);
        Assert.Equal(0, card.ChosenIndex);
    }

    [Fact]
    public async Task Choosing_a_face_saves_the_buddy_and_the_stage_shows_its_name_and_hello()
    {
        await using var app = await TestApp.StartAsync();
        var card = await OpenCardAsync(app);
        var changed = new List<string?>();
        card.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await card.ChooseBuddyAsync(SearchBuddy.Paige);

        Assert.Equal("Paige the paper ghost", card.Chosen.Name);
        Assert.Equal("Boo! Looking for something?", card.Chosen.HelloLine);
        Assert.Equal("dusk", card.Chosen.Stage);
        Assert.Equal(6, card.ChosenIndex);
        Assert.Contains(nameof(QuickSearchCardViewModel.Chosen), changed);

        await using var reopened = await app.ReopenAsync();
        Assert.Equal("Paige the paper ghost", (await OpenCardAsync(reopened)).Chosen.Name);
    }

    [Fact]
    public async Task Choosing_faces_one_after_another_keeps_exactly_one_chosen()
    {
        await using var app = await TestApp.StartAsync();
        var card = await OpenCardAsync(app);

        foreach (var buddy in new[] { SearchBuddy.Archie, SearchBuddy.Pip, SearchBuddy.Fetch, SearchBuddy.Pip })
        {
            await card.ChooseBuddyAsync(buddy);
            Assert.Equal(buddy, Assert.Single(card.Buddies, tile => tile.IsChosen).Buddy);
            Assert.Equal(buddy, card.Chosen.Buddy);
        }
    }

    [Fact]
    public void Every_buddy_has_its_own_stage()
    {
        var stages = Enum.GetValues<SearchBuddy>().Select(buddy => new BuddyTileViewModel(buddy).Stage).ToArray();

        Assert.Equal(["night", "study", "lab", "forest", "sea", "meadow", "dusk"], stages);
    }
```

`QuickSearchLayoutTests`:

```csharp
    /// <summary>
    /// Owner-found 2026-09-25: the tiles clipped their Choose button and Paige's name pushed hers
    /// out of the card. The faces replace them: a named radio group, each face named and reporting
    /// whether it is chosen, the stage decorative, the name and line announced.
    /// </summary>
    [Fact]
    public void The_buddy_faces_are_a_named_radio_group_beside_a_decorative_stage()
    {
        var workspace = Read("src", "DeskAI.App", "Views", "WorkspacePage.xaml");

        Assert.Contains("<RadioButtons", workspace, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Search buddies\"", workspace, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind ChooseName}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BuddyStage\"", workspace, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("VariableSizedWrapGrid", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Choose\"", workspace, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/DeskAI.Presentation.Tests/DeskAI.Presentation.Tests.csproj -c Release --no-restore`
Expected: build errors — `Chosen`, `ChosenIndex`, `HelloLine`, `Stage` do not exist.

- [ ] **Step 3: Write the view model code**

In `BuddyTileViewModel` (summary: "One buddy on the Quick search card: its face, and what the stage shows when it is chosen."):

```csharp
    /// <summary>What the buddy says on the stage: its hello.</summary>
    public string HelloLine { get; } = SearchBuddyLines.Line(buddy, BuddyMood.Idle);

    /// <summary>The stage's colours, named as in the mockup: night, study, lab, forest, sea, meadow, dusk.</summary>
    public string Stage { get; } = buddy switch
    {
        SearchBuddy.Archie => "study",
        SearchBuddy.Pip => "lab",
        SearchBuddy.Fetch => "forest",
        SearchBuddy.Inky => "sea",
        SearchBuddy.Mochi => "meadow",
        SearchBuddy.Paige => "dusk",
        _ => "night",
    };
```

In `QuickSearchCardViewModel` (summary: "…the switch, the shortcut, the buddy stage and faces, and a shortcut problem"):

```csharp
    private BuddyTileViewModel? _chosen;

    /// <summary>The buddy on the stage. Sparky until the stored choice is read.</summary>
    public BuddyTileViewModel Chosen => _chosen ?? Buddies[0];

    /// <summary>The chosen face's place in the row: the buddies are listed in the enum's order.</summary>
    public int ChosenIndex => (int)Chosen.Buddy;
```

and `Choose` becomes

```csharp
    private void Choose(SearchBuddy buddy)
    {
        foreach (var tile in Buddies)
        {
            tile.IsChosen = tile.Buddy == buddy;
        }

        _chosen = Buddies.First(tile => tile.IsChosen);
        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(ChosenIndex));
    }
```

- [ ] **Step 4: Run the page tests**

Run: `dotnet test tests/DeskAI.Presentation.Tests/DeskAI.Presentation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QuickSearchSettingsPageTests"`
Expected: PASS (the layout test still fails until Step 5).

- [ ] **Step 5: Write the App code**

Create `src/DeskAI.App/Views/Buddies/BuddyStageBrushes.cs`:

```csharp
using DeskAI.App.Services;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DeskAI.App.Views.Buddies;

/// <summary>Each buddy's own background on the stage and its face, from the chosen mockup (chooser B).</summary>
internal static class BuddyStageBrushes
{
    public static Brush For(string stage)
    {
        var (centre, edge) = stage switch
        {
            "study" => ("#4A3524", "#1C130C"),
            "lab" => ("#1D2C46", "#0B111D"),
            "forest" => ("#2D4A2A", "#101C10"),
            "sea" => ("#1B3F6E", "#081A33"),
            "meadow" => ("#1D5A4B", "#0A2019"),
            "dusk" => ("#3A2D5C", "#140F24"),
            _ => ("#16405C", "#0A1726"),
        };

        return new RadialGradientBrush
        {
            Center = new Point(0.5, 0.55),
            GradientOrigin = new Point(0.5, 0.55),
            RadiusX = 0.75,
            RadiusY = 0.75,
            GradientStops =
            {
                new GradientStop { Color = WindowsAppearanceApplier.ToColor(centre), Offset = 0 },
                new GradientStop { Color = WindowsAppearanceApplier.ToColor(edge), Offset = 0.75 },
            },
        };
    }
}
```

Create `src/DeskAI.App/Views/Buddies/BuddyAnimations.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace DeskAI.App.Views.Buddies;

/// <summary>
/// The buddy's entrance: it grows up from below with a little overshoot (about half a second).
/// Callers skip it when "Let my buddy move" is off.
/// </summary>
internal static class BuddyAnimations
{
    public static void PopIn(UIElement element, TimeSpan delay)
    {
        var shape = new CompositeTransform { ScaleX = 0.5, ScaleY = 0.5, TranslateY = 30 };
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = shape;
        var overshoot = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        var story = new Storyboard { BeginTime = delay };
        story.Children.Add(To(shape, "ScaleX", 0.5, 1, overshoot));
        story.Children.Add(To(shape, "ScaleY", 0.5, 1, overshoot));
        story.Children.Add(To(shape, "TranslateY", 30, 0, overshoot));
        story.Children.Add(To(element, "Opacity", 0, 1, null));
        story.Begin();
    }

    internal static DoubleAnimation To(DependencyObject target, string property, double from, double to, EasingFunctionBase? easing, double seconds = 0.5)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromSeconds(seconds)),
            EasingFunction = easing,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}
```

In `WorkspacePage.xaml`, the page has no resources yet: add `<Page.Resources>` … `</Page.Resources>` right after the opening `<Page …>` tag, holding the face style:

```xml
        <!-- A buddy's round face on the Quick search card: a mint ring when chosen, a little bigger under the pointer. -->
        <Style x:Key="BuddyFaceStyle" TargetType="RadioButton">
            <Setter Property="Padding" Value="0" />
            <Setter Property="MinWidth" Value="0" />
            <Setter Property="UseSystemFocusVisuals" Value="True" />
            <Setter Property="FocusVisualMargin" Value="-4" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="RadioButton">
                        <Grid Width="60" Height="60" Background="Transparent" RenderTransformOrigin="0.5,0.5">
                            <Grid.RenderTransform>
                                <ScaleTransform x:Name="FaceScale" />
                            </Grid.RenderTransform>
                            <VisualStateManager.VisualStateGroups>
                                <VisualStateGroup x:Name="CommonStates">
                                    <VisualState x:Name="Normal" />
                                    <VisualState x:Name="PointerOver">
                                        <VisualState.Setters>
                                            <Setter Target="FaceScale.ScaleX" Value="1.08" />
                                            <Setter Target="FaceScale.ScaleY" Value="1.08" />
                                        </VisualState.Setters>
                                    </VisualState>
                                    <VisualState x:Name="Pressed" />
                                    <VisualState x:Name="Disabled" />
                                </VisualStateGroup>
                                <VisualStateGroup x:Name="CheckStates">
                                    <VisualState x:Name="Checked">
                                        <VisualState.Setters>
                                            <Setter Target="Ring.Stroke" Value="#3FD9A8" />
                                            <Setter Target="Halo.Opacity" Value="1" />
                                        </VisualState.Setters>
                                    </VisualState>
                                    <VisualState x:Name="Unchecked" />
                                    <VisualState x:Name="Indeterminate" />
                                </VisualStateGroup>
                            </VisualStateManager.VisualStateGroups>
                            <Ellipse x:Name="Halo" Margin="-5" Stroke="#403FD9A8" StrokeThickness="3" Opacity="0" />
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                                              Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" />
                            <Ellipse x:Name="Ring" Stroke="#22384F" StrokeThickness="2" />
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
```

Replace the "Your search buddy" label and the tiles `ItemsControl` with:

```xml
                    <TextBlock Style="{StaticResource GroupLabelStyle}" Text="Your search buddy" />
                    <TextBlock Style="{StaticResource CaptionStyle}" Text="Click a face to pick your buddy." />
                    <Grid ColumnDefinitions="Auto,*" ColumnSpacing="18">
                        <!-- The stage: the chosen buddy, large. Decorative; the name and line beside it say who it is. -->
                        <Border x:Name="BuddyStage"
                                Width="240" Height="230" CornerRadius="18"
                                BorderBrush="#803FD9A8" BorderThickness="1.5"
                                AutomationProperties.AccessibilityView="Raw">
                            <ContentControl x:Name="BuddyStageHost" Width="170" Height="170" IsTabStop="False"
                                            HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <StackPanel Grid.Column="1" Spacing="10" VerticalAlignment="Center">
                            <StackPanel Spacing="10" AutomationProperties.LiveSetting="Polite">
                                <TextBlock FontSize="20" FontWeight="Bold" TextWrapping="Wrap"
                                           Foreground="{ThemeResource DeskTextPrimaryBrush}"
                                           Text="{x:Bind ViewModel.QuickSearch.Chosen.Name, Mode=OneWay}" />
                                <Border HorizontalAlignment="Left" Padding="12,7" CornerRadius="14,14,14,4">
                                    <Border.Background>
                                        <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                                            <GradientStop Offset="0" Color="#3FD9A8" />
                                            <GradientStop Offset="1" Color="#6A8CFF" />
                                        </LinearGradientBrush>
                                    </Border.Background>
                                    <TextBlock FontSize="13" FontWeight="Bold" Foreground="#04121C" TextWrapping="Wrap"
                                               Text="{x:Bind ViewModel.QuickSearch.Chosen.HelloLine, Mode=OneWay}" />
                                </Border>
                            </StackPanel>
                            <RadioButtons x:Name="BuddyFaces"
                                          AutomationProperties.Name="Search buddies"
                                          MaxColumns="7"
                                          ItemsSource="{x:Bind ViewModel.QuickSearch.Buddies}"
                                          SelectedIndex="{x:Bind ViewModel.QuickSearch.ChosenIndex, Mode=OneWay}"
                                          SelectionChanged="OnBuddyFaceChosen">
                                <RadioButtons.ItemTemplate>
                                    <DataTemplate x:DataType="viewmodels:BuddyTileViewModel">
                                        <RadioButton Style="{StaticResource BuddyFaceStyle}"
                                                     AutomationProperties.Name="{x:Bind ChooseName}"
                                                     ToolTipService.ToolTip="{x:Bind Name}">
                                            <Grid Width="56" Height="56">
                                                <Ellipse Tag="{x:Bind Stage}" Loaded="OnFaceBackgroundLoaded" />
                                                <!-- The buddy's still picture; drawn by BuddyFactory when the face loads. -->
                                                <ContentControl Width="46" Height="46" IsTabStop="False"
                                                                Tag="{x:Bind}" Loaded="OnBuddyPictureLoaded" />
                                            </Grid>
                                        </RadioButton>
                                    </DataTemplate>
                                </RadioButtons.ItemTemplate>
                            </RadioButtons>
                        </StackPanel>
                    </Grid>
```

`RadioButtons` uses a `RadioButton` returned by the item template as the item itself (it does not wrap it again), so the face style applies and arrow keys move between faces.

In `WorkspacePage.xaml.cs`: remove `OnChooseBuddyClicked`; keep `OnBuddyPictureLoaded` (still pictures, `motion: null`, `HoldsStill = true`); add (with `using Microsoft.UI.Xaml.Shapes;`, `using System.ComponentModel;`, `using DeskAI.Core.QuickSearch;`):

```csharp
    /// <summary>Clicking a face, or moving to it with the arrow keys, chooses that buddy at once.</summary>
    private async void OnBuddyFaceChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons { SelectedIndex: >= 0 } faces && faces.SelectedIndex != ViewModel.QuickSearch.ChosenIndex)
        {
            await ViewModel.QuickSearch.ChooseBuddyAsync((SearchBuddy)faces.SelectedIndex);
        }
    }

    private void OnFaceBackgroundLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Ellipse { Tag: string stage } face)
        {
            face.Fill = BuddyStageBrushes.For(stage);
        }
    }

    private void OnQuickSearchCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSearchCardViewModel.Chosen))
        {
            ShowStage(popIn: true);
        }
    }

    /// <summary>The chosen buddy, large, on its own background; it pops in when it changes and buddies may move.</summary>
    private void ShowStage(bool popIn)
    {
        var chosen = ViewModel.QuickSearch.Chosen;
        if (BuddyStageHost.Content is BuddyControl shown && shown.Tag is SearchBuddy current && current == chosen.Buddy)
        {
            return;
        }

        BuddyStage.Background = BuddyStageBrushes.For(chosen.Stage);
        var buddy = BuddyFactory.Create(chosen.Buddy, ViewModel.QuickSearch.Motion);
        buddy.Tag = chosen.Buddy;
        BuddyStageHost.Content = buddy;
        if (popIn && ViewModel.QuickSearch.Motion.IsOn)
        {
            BuddyAnimations.PopIn(BuddyStageHost, TimeSpan.Zero);
        }
    }
```

In the page's existing `OnLoaded` (it runs once: it removes itself, then awaits `ViewModel.InitializeAsync()`), add after the await:

```csharp
        ShowStage(popIn: false);
        ViewModel.QuickSearch.PropertyChanged += OnQuickSearchCardChanged;
        Unloaded += (_, _) => ViewModel.QuickSearch.PropertyChanged -= OnQuickSearchCardChanged;
```

(The page and its view model are made fresh for each visit, so one handler per page is all there ever is.)

- [ ] **Step 6: Build and run everything**

Run the build commands. Expected: 0 warnings, all tests PASS, formatting clean.

- [ ] **Step 7: Look at it in the UI preview**

Build the preview into the scratch folder (see "Build commands"), start
`C:/Users/Hammouri/.claude/jobs/e1233c1d/tmp/preview/DeskAI.App.exe`, open **My workspace → Looks**, and check: the stage shows Sparky moving on the night background; each face is round with its own background and the chosen one has the mint ring; clicking Paige swaps the stage with a pop and her name fits; arrow keys move between faces; turning **Let my buddy move** off stops the stage buddy. Close the preview. If you cannot look, say so plainly in the task report.

- [ ] **Step 8: Update the docs**

- `docs/TESTING.md`, the Quick search card row: replace "seven buddies, the choice kept" with "a stage with the chosen buddy's name and hello line on its own background, seven round faces as a radio group; clicking a face chooses it at once and is kept; exactly one chosen".
- `docs/UI-UX.md`: describe the stage (about 240 × 230, per-buddy background, mint edge), the name and hello line in a gradient bubble beside it, the seven faces (60 px, mint ring on the chosen one), arrow keys, and what a screen reader hears ("Search buddies", "Choose Archie the owl", the name and line announced politely).
- `docs/USER-GUIDE.md`: "Click a face under **Your search buddy** to pick your buddy."

- [ ] **Step 9: Review the diff and commit**

```bash
git add -A src tests docs
git commit -m "Show the chosen buddy on a stage and pick buddies by their faces

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: See-through window probe (throwaway)

**Goal:** find out, on the owner's Windows 11, whether the bar's window can be see-through around the card. Nothing from this task is committed except the result written into the spec.

**Files:**
- Create (throwaway, not committed): `src/DeskAI.App/Views/SeeThroughBackdrop.cs`
- Modify (throwaway, not committed): `src/DeskAI.App/Views/QuickSearchWindow.xaml`, `QuickSearchWindow.xaml.cs`
- Modify (committed): `docs/superpowers/specs/2026-09-25-quick-search-polish-design.md` ("Rulings made while building")

- [ ] **Step 1: Write the backdrop**

Create `src/DeskAI.App/Views/SeeThroughBackdrop.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DeskAI.App.Views;

/// <summary>
/// A window background with nothing in it, so only what the page draws is seen (the approach
/// WinUIEx's TransparentTintBackdrop uses). It draws; it gives the window no other ability.
/// </summary>
internal sealed partial class SeeThroughBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? _compositor;
    private static nint _queueController;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        connectedTarget.SystemBackdrop = Compositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    /// <summary>The system compositor needs a Windows (not WinUI) dispatcher queue on this thread; one is made if missing.</summary>
    private static Windows.UI.Composition.Compositor Compositor()
    {
        if (_compositor is not null)
        {
            return _compositor;
        }

        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 };
            _ = CreateDispatcherQueueController(options, out _queueController);
        }

        return _compositor = new Windows.UI.Composition.Compositor();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
```

- [ ] **Step 2: Make the bar see-through for the probe**

In `QuickSearchWindow.xaml`: replace `<DesktopAcrylicBackdrop />` with `<views:SeeThroughBackdrop />` (add `xmlns:views="using:DeskAI.App.Views"`); give `Root` `Background="Transparent"` and `Padding="24,0,24,24"`; wrap the existing content (both rows) in a new `Border` with `Margin="0,44,0,0"`, `CornerRadius="20"`, `Background="#F20B1624"`, `BorderBrush="#3FD9A8"`, `BorderThickness="1.5"`; put three glow `Border`s behind it:

```xml
        <Border Margin="-12,32,-12,-12" CornerRadius="32" BorderBrush="#0D3FD9A8" BorderThickness="6" IsHitTestVisible="False" />
        <Border Margin="-7,37,-7,-7" CornerRadius="27" BorderBrush="#1A3FD9A8" BorderThickness="5" IsHitTestVisible="False" />
        <Border Margin="-3,41,-3,-3" CornerRadius="23" BorderBrush="#333FD9A8" BorderThickness="3" IsHitTestVisible="False" />
```

and move the buddy's `StackPanel` so it sits on top of the card's top edge (`VerticalAlignment="Top"`, declared after the card). In `QuickSearchWindow.xaml.cs`: the window width becomes `BarWidth + 48`; `RoundTheCorners` sets the corner preference to 1 (do not round) and also sets `DWMWA_BORDER_COLOR` (34) to `0xFFFFFFFE` (no border); add `Root.PointerPressed += (_, args) => { if (args.OriginalSource == Root) { HideBar(); } };` in the constructor.

- [ ] **Step 3: Build the preview and look**

Build the UI preview into the scratch folder, start it, press **Ctrl + Alt + D** over another window. Check yourself first that the window starts and the area around the card is not black or grey. If it is black or grey, try once more with `DwmExtendFrameIntoClientArea(hwnd, ref margins)` where all four margins are `-1`, called from the constructor, and rebuild.

- [ ] **Step 4: Ask the owner to check it**

Tell the owner the path of the preview exe and ask them (in plain words, per memory "Ask the owner in plain words") to press Ctrl + Alt + D and answer with AskUserQuestion:
1. Can you see your other window through the space around the search box?
2. Is there a soft mint glow around the box, with no square frame?
3. Does clicking outside the box (including just next to it) hide it?
4. Optional: with Windows' high contrast on, can you still read the box? On a screen set to 150%, does it look sharp and the right size?

- [ ] **Step 5: Throw the probe away and write down the result**

```bash
git restore src/DeskAI.App/Views/QuickSearchWindow.xaml src/DeskAI.App/Views/QuickSearchWindow.xaml.cs
```

Keep `SeeThroughBackdrop.cs` aside **only if it worked** (Task 5 commits it; move it to `C:/Users/Hammouri/.claude/jobs/e1233c1d/tmp/SeeThroughBackdrop.cs` now so the tree is clean), otherwise delete it. Confirm `git status` shows only the spec change. In the spec's "Rulings made while building", write the date, what was tried (the backdrop alone, or with the extended frame), what the owner saw for each of the four checks, and the verdict: **see-through** or **backup layout**. Tell the owner which one Task 5 builds before starting it.

- [ ] **Step 6: Commit the result**

```bash
git add docs/superpowers/specs/2026-09-25-quick-search-polish-design.md
git commit -m "Record the see-through window probe result for the quick search bar

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The glowing bar

**Files:**
- Create (see-through verdict only): `src/DeskAI.App/Views/SeeThroughBackdrop.cs` (from the probe, unchanged)
- Modify: `src/DeskAI.Presentation/ViewModels/QuickSearchViewModel.cs` (`QuickSearchRowViewModel`)
- Modify: `src/DeskAI.App/Views/QuickSearchWindow.xaml`, `QuickSearchWindow.xaml.cs`
- Test: `tests/DeskAI.Presentation.Tests/QuickSearchPageTests.cs`, `QuickSearchLayoutTests.cs`
- Docs: `docs/TESTING.md`, `docs/UI-UX.md`

**Interfaces:**
- Consumes: Task 2's `BuddyMotion`, `QuickSearchWindow(QuickSearchViewModel, BuddyMotion)`; Task 3's `BuddyAnimations.PopIn(UIElement, TimeSpan)` and `BuddyAnimations.To(...)`; Task 4's verdict.
- Produces: `QuickSearchRowViewModel.KindLabel` ("DOC", "PDF", "IMG", "VID", "FILE"), `.KindKey` ("doc", "pdf", "img", "vid", "file"), `.EnterHint` ("Open ↵" / "Show in folder ↵"); `QuickSearchWindow.KindBrush(string) → Brush`.

- [ ] **Step 1: Write the failing tests**

`QuickSearchPageTests`:

```csharp
    [Theory]
    [InlineData("Notes.docx", FileCategory.Documents, OpenChoice.Open, "DOC", "doc", "Open ↵")]
    [InlineData("Slides.pptx", FileCategory.Presentations, OpenChoice.Open, "DOC", "doc", "Open ↵")]
    [InlineData("Budget.xlsx", FileCategory.Spreadsheets, OpenChoice.Open, "DOC", "doc", "Open ↵")]
    [InlineData("Lesson handout.PDF", FileCategory.Documents, OpenChoice.Open, "PDF", "pdf", "Open ↵")]
    [InlineData("Beach.jpg", FileCategory.Images, OpenChoice.Open, "IMG", "img", "Open ↵")]
    [InlineData("Screen 1.png", FileCategory.Screenshots, OpenChoice.Open, "IMG", "img", "Open ↵")]
    [InlineData("Trip.mp4", FileCategory.Videos, OpenChoice.Open, "VID", "vid", "Open ↵")]
    [InlineData("setup.exe", FileCategory.Installers, OpenChoice.ShowInFolder, "FILE", "file", "Show in folder ↵")]
    public void Each_row_shows_what_kind_of_file_it_is_and_what_Enter_does(
        string name, FileCategory category, OpenChoice choice, string label, string key, string hint)
    {
        var row = new QuickSearchRowViewModel(new QuickSearchRow(Guid.NewGuid(), name, name, "Downloads", category, choice));

        Assert.Equal(label, row.KindLabel);
        Assert.Equal(key, row.KindKey);
        Assert.Equal(hint, row.EnterHint);
    }
```

(add `using DeskAI.Core.Classification;` if missing; check `OpenChoice`'s namespace with `grep -rn "enum OpenChoice" src`). `QuickSearchLayoutTests`:

```csharp
    [Fact]
    public void The_bar_has_a_search_icon_an_Esc_hint_and_an_edge_that_turns_only_while_buddies_may_move()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");
        var code = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml.cs");

        Assert.Contains("Glyph=\"&#xE721;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Esc\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EdgeAngle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"20\"", xaml, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"if \(_motion\.IsOn\)\s*\{\s*_edgeTurn\.Begin\(\);", RegexOptions.None, TimeSpan.FromSeconds(1)), code);
        Assert.Contains("_edgeTurn.Stop();", code, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/DeskAI.Presentation.Tests/DeskAI.Presentation.Tests.csproj -c Release --no-restore`
Expected: build errors — `KindLabel`, `KindKey`, `EnterHint` do not exist.

- [ ] **Step 3: Write the row code**

In `QuickSearchRowViewModel`:

```csharp
    /// <summary>The coloured tile's key: document blue, PDF red, picture orange, video violet, anything else grey.</summary>
    public string KindKey { get; } = KindOf(row);

    /// <summary>The short word on the tile.</summary>
    public string KindLabel => KindKey == "file" ? "FILE" : KindKey.ToUpperInvariant();

    /// <summary>What Enter does on the selected row.</summary>
    public string EnterHint => ActionText + " ↵";

    private static string KindOf(QuickSearchRow row) => row.Category switch
    {
        _ when row.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) => "pdf",
        FileCategory.Images or FileCategory.Screenshots => "img",
        FileCategory.Videos => "vid",
        FileCategory.Documents or FileCategory.Presentations or FileCategory.Spreadsheets => "doc",
        _ => "file",
    };
```

Remove the `Glyph` property if nothing else uses it after Step 4 (`grep -rn "Glyph" src/DeskAI.App/Views/QuickSearchWindow.xaml`).

- [ ] **Step 4: Write the window**

Replace `QuickSearchWindow.xaml` with the version for the probe's verdict.

**See-through verdict:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<!--
  The quick search bar (ADR 0047), look C "Glowing edge" (2026-09-25): the buddy perched on the
  card's top edge, a slowly turning mint-to-pink edge, a soft glow, dark glass inside. The window
  is see-through around the card (SeeThroughBackdrop); a click on the see-through part hides it.
-->
<Window
    x:Class="DeskAI.App.Views.QuickSearchWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:views="using:DeskAI.App.Views"
    xmlns:viewmodels="using:DeskAI.App.ViewModels"
    Title="DeskAI quick search">

    <Window.SystemBackdrop>
        <views:SeeThroughBackdrop />
    </Window.SystemBackdrop>

    <Grid
        x:Name="Root"
        Padding="24,0,24,24"
        VerticalAlignment="Top"
        Background="Transparent"
        PreviewKeyDown="OnKeyDown"
        RequestedTheme="Dark">
        <Grid.Resources>
            <!-- The edge turns once every six seconds, only while the bar shows and buddies may move. -->
            <Storyboard x:Key="EdgeTurn" RepeatBehavior="Forever">
                <DoubleAnimation EnableDependentAnimation="True" Storyboard.TargetName="EdgeAngle" Storyboard.TargetProperty="Angle" From="0" To="360" Duration="0:0:6" />
            </Storyboard>

            <DataTemplate x:Key="QuickRowTemplate" x:DataType="viewmodels:QuickSearchRowViewModel">
                <Grid Padding="10,7" ColumnDefinitions="Auto,*,Auto" ColumnSpacing="10" CornerRadius="10">
                    <!-- The selected row: a mint edge and tint, and what Enter will do. -->
                    <Border
                        Grid.ColumnSpan="3"
                        Margin="-10,-7"
                        Background="#296A8CFF"
                        BorderBrush="#3FD9A8"
                        BorderThickness="3,0,0,0"
                        CornerRadius="10"
                        Visibility="{x:Bind IsSelected, Mode=OneWay}" />
                    <Border Width="30" Height="30" VerticalAlignment="Center" Background="{x:Bind views:QuickSearchWindow.KindBrush(KindKey)}" CornerRadius="7">
                        <TextBlock HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="9" FontWeight="Bold" Foreground="White" Text="{x:Bind KindLabel}" />
                    </Border>
                    <StackPanel Grid.Column="1" VerticalAlignment="Center">
                        <TextBlock FontWeight="SemiBold" Text="{x:Bind Name}" TextTrimming="CharacterEllipsis" />
                        <TextBlock Style="{StaticResource CaptionStyle}" Text="{x:Bind Where}" TextTrimming="CharacterEllipsis" />
                        <TextBlock Style="{StaticResource CaptionStyle}" Text="{x:Bind Snippet}" TextTrimming="CharacterEllipsis" Visibility="{x:Bind HasSnippet}" />
                    </StackPanel>
                    <StackPanel Grid.Column="2" VerticalAlignment="Center" Orientation="Horizontal" Spacing="8">
                        <TextBlock
                            VerticalAlignment="Center"
                            AutomationProperties.AccessibilityView="Raw"
                            FontSize="11"
                            Foreground="#8FE9CC"
                            Text="{x:Bind EnterHint}"
                            Visibility="{x:Bind IsSelected, Mode=OneWay}" />
                        <Button Click="OnRowButtonClicked" Content="{x:Bind ActionText}" CornerRadius="8" Tag="{x:Bind}" />
                    </StackPanel>
                </Grid>
            </DataTemplate>
        </Grid.Resources>

        <!-- The glow: soft mint rings behind the card. Only drawing; clicks pass to Root, which hides the bar. -->
        <Border Margin="-12,32,-12,-12" BorderBrush="#0D3FD9A8" BorderThickness="6" CornerRadius="32" IsHitTestVisible="False" />
        <Border Margin="-7,37,-7,-7" BorderBrush="#1A3FD9A8" BorderThickness="5" CornerRadius="27" IsHitTestVisible="False" />
        <Border Margin="-3,41,-3,-3" BorderBrush="#333FD9A8" BorderThickness="3" CornerRadius="23" IsHitTestVisible="False" />

        <!-- The card: the turning edge is the outer border's background showing through a 1.5 px gap. -->
        <Border x:Name="Card" Margin="0,44,0,0" Padding="1.5" CornerRadius="20">
            <Border.RenderTransform>
                <TranslateTransform x:Name="CardShift" />
            </Border.RenderTransform>
            <Border.Background>
                <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                    <LinearGradientBrush.RelativeTransform>
                        <RotateTransform x:Name="EdgeAngle" CenterX="0.5" CenterY="0.5" />
                    </LinearGradientBrush.RelativeTransform>
                    <GradientStop Offset="0" Color="#3FD9A8" />
                    <GradientStop Offset="0.25" Color="#6A8CFF" />
                    <GradientStop Offset="0.5" Color="#C86BFF" />
                    <GradientStop Offset="0.75" Color="#FF8FB8" />
                    <GradientStop Offset="1" Color="#3FD9A8" />
                </LinearGradientBrush>
            </Border.Background>

            <Border Background="#F20B1624" CornerRadius="19">
                <StackPanel Padding="16,40,16,14" Spacing="10">
                    <!-- The search box: a search icon, the words, and the Esc key hint. -->
                    <Grid Padding="12,4" Background="#0DFFFFFF" ColumnDefinitions="Auto,*,Auto" ColumnSpacing="10" CornerRadius="12">
                        <FontIcon VerticalAlignment="Center" AutomationProperties.AccessibilityView="Raw" FontSize="18" Foreground="#8FA3B8" Glyph="&#xE721;" />
                        <TextBox
                            x:Name="Box"
                            Grid.Column="1"
                            AutomationProperties.Name="Find a file"
                            BorderThickness="0,0,0,2"
                            FontSize="19"
                            PlaceholderText="Find a file"
                            Text="{x:Bind ViewModel.Phrase, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}">
                            <!-- No frame of its own: a mint line under the words only while typing. -->
                            <TextBox.Resources>
                                <SolidColorBrush x:Key="TextControlBackground" Color="Transparent" />
                                <SolidColorBrush x:Key="TextControlBackgroundPointerOver" Color="Transparent" />
                                <SolidColorBrush x:Key="TextControlBackgroundFocused" Color="Transparent" />
                                <SolidColorBrush x:Key="TextControlBorderBrush" Color="Transparent" />
                                <SolidColorBrush x:Key="TextControlBorderBrushPointerOver" Color="Transparent" />
                                <SolidColorBrush x:Key="TextControlBorderBrushFocused" Color="#3FD9A8" />
                            </TextBox.Resources>
                        </TextBox>
                        <Border
                            Grid.Column="2"
                            Padding="5,1"
                            VerticalAlignment="Center"
                            AutomationProperties.AccessibilityView="Raw"
                            BorderBrush="#2EFFFFFF"
                            BorderThickness="1,1,1,2"
                            CornerRadius="5">
                            <TextBlock FontSize="10" FontWeight="SemiBold" Foreground="#9FB3C7" Text="Esc" />
                        </Border>
                    </Grid>

                    <ItemsControl ItemsSource="{x:Bind viewmodels:QuickSearchViewModel.Examples}" Visibility="{x:Bind ViewModel.ShowsExamples, Mode=OneWay}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <StackPanel Orientation="Horizontal" Spacing="8" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="x:String">
                                <Button Padding="12,4" Click="OnExampleClicked" Content="{x:Bind}" CornerRadius="999" Tag="{x:Bind}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <TextBlock x:Name="NameHeader" Style="{StaticResource CaptionStyle}" Text="By name" Visibility="Collapsed" />
                    <ItemsControl x:Name="NameRowsList" ItemTemplate="{StaticResource QuickRowTemplate}" ItemsSource="{x:Bind ViewModel.NameRows}" />
                    <TextBlock Style="{StaticResource BodySecondaryStyle}" Text="{x:Bind ViewModel.NameFact, Mode=OneWay}" TextWrapping="Wrap" Visibility="{x:Bind ViewModel.HasNameFact, Mode=OneWay}" />

                    <TextBlock Style="{StaticResource CaptionStyle}" Text="Words inside" Visibility="{x:Bind ViewModel.ShowsInsideGroup, Mode=OneWay}" />
                    <ItemsControl x:Name="InsideRowsList" ItemTemplate="{StaticResource QuickRowTemplate}" ItemsSource="{x:Bind ViewModel.InsideRows}" />
                    <TextBlock Style="{StaticResource BodySecondaryStyle}" Text="{x:Bind ViewModel.InsideFact, Mode=OneWay}" TextWrapping="Wrap" Visibility="{x:Bind ViewModel.HasInsideFact, Mode=OneWay}" />

                    <TextBlock Foreground="{ThemeResource DeskCautionBrush}" Text="{x:Bind ViewModel.OpenMessage, Mode=OneWay}" TextWrapping="Wrap" Visibility="{x:Bind ViewModel.HasOpenMessage, Mode=OneWay}" />

                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button Click="OnSeeMoreClicked" Content="See more in DeskAI" CornerRadius="8" Visibility="{x:Bind ViewModel.ShowsSeeMore, Mode=OneWay}" />
                        <Button Click="OnOpenDeskAiClicked" Content="Open DeskAI" CornerRadius="8" Style="{StaticResource AccentButtonStyle}" Visibility="{x:Bind ViewModel.ShowsOpenDeskAi, Mode=OneWay}" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Border>

        <!-- The buddy, perched on the card's top edge, centred, its line to its right. Decorative: its line is announced instead. -->
        <Grid x:Name="Perch" VerticalAlignment="Top" ColumnDefinitions="*,Auto,*" ColumnSpacing="8" Margin="0,4,0,0">
            <ContentControl
                x:Name="BuddyHost"
                Grid.Column="1"
                Width="72"
                Height="72"
                AutomationProperties.AccessibilityView="Raw"
                IsTabStop="False"
                Tapped="OnBuddyTapped" />
            <Border Grid.Column="2" MaxWidth="300" Padding="11,6" HorizontalAlignment="Left" VerticalAlignment="Center" CornerRadius="14,14,14,4">
                <Border.Background>
                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                        <GradientStop Offset="0" Color="#3FD9A8" />
                        <GradientStop Offset="1" Color="#6A8CFF" />
                    </LinearGradientBrush>
                </Border.Background>
                <TextBlock
                    AutomationProperties.LiveSetting="Polite"
                    FontSize="12.5"
                    FontWeight="Bold"
                    Foreground="#04121C"
                    Text="{x:Bind ViewModel.BuddyLine, Mode=OneWay}"
                    TextWrapping="Wrap" />
            </Border>
        </Grid>
    </Grid>
</Window>
```

**Backup verdict** — the same file with these differences: keep `<DesktopAcrylicBackdrop />` as the backdrop (no `views:SeeThroughBackdrop`); `Root` has no padding and `Background="#E60B1726"`; no glow borders; `Card` has `Margin="0"`; the inner `StackPanel` becomes a two-column grid with the buddy inside on the left and no `Perch` grid:

```xml
            <Border Background="#F20B1624" CornerRadius="19">
                <Grid Padding="14" ColumnDefinitions="96,*" ColumnSpacing="12">
                    <!-- The buddy inside the card on the left, its line under it. Decorative: the line is announced. -->
                    <StackPanel x:Name="Perch" Spacing="6" HorizontalAlignment="Center">
                        <ContentControl x:Name="BuddyHost" Width="72" Height="72" AutomationProperties.AccessibilityView="Raw" IsTabStop="False" Tapped="OnBuddyTapped" />
                        <TextBlock AutomationProperties.LiveSetting="Polite" FontSize="11" FontWeight="SemiBold" Foreground="#8FE9CC"
                                   Text="{x:Bind ViewModel.BuddyLine, Mode=OneWay}" TextAlignment="Center" TextWrapping="Wrap" />
                    </StackPanel>
                    <StackPanel Grid.Column="1" Spacing="10">
                        <!-- the search box grid, examples, rows, facts, open message and buttons: exactly as in the see-through version -->
                    </StackPanel>
                </Grid>
            </Border>
```

(That comment marks where the see-through version's children go unchanged; copy them in, it is not a placeholder to leave.)

Update `QuickSearchWindow.xaml.cs`:

- The class remarks: add "Its look (2026-09-25) is look C, Glowing edge; the opening and the turning edge play only while 'Let my buddy move' is on."
- Constants: `private const double GlowMargin = 24;` (see-through; `0` for the backup) and the window width everywhere is `BarWidth + (2 * GlowMargin)`: in `ShowNearPointer` (`var width = (int)Math.Ceiling((BarWidth + (2 * GlowMargin)) * Scale());`) and in `FitToContent` (`Root.Measure(new Size(BarWidth + (2 * GlowMargin), double.PositiveInfinity));` and the `ResizeClient` width).
- Fields: `private readonly Storyboard _edgeTurn;` set in the constructor after `InitializeComponent()`: `_edgeTurn = (Storyboard)Root.Resources["EdgeTurn"];`.
- See-through only: `RoundTheCorners` becomes `ClearTheFrame`:

```csharp
    private const int DwmBorderColor = 34;
    private const int DwmDoNotRound = 1;
    private const uint DwmColorNone = 0xFFFFFFFE;

    /// <summary>No rounded frame and no border from Windows: the card draws its own corners and edge.</summary>
    private void ClearTheFrame()
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var corners = DwmDoNotRound;
        _ = DwmSetWindowAttribute(window, DwmWindowCornerPreference, ref corners, sizeof(int));
        var none = unchecked((int)DwmColorNone);
        _ = DwmSetWindowAttribute(window, DwmBorderColor, ref none, sizeof(int));
    }
```

  and in the constructor `Root.PointerPressed += OnRootPressed;` with

```csharp
    /// <summary>A click on the see-through part around the card is a click elsewhere: the bar hides.</summary>
    private void OnRootPressed(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, Root))
        {
            HideBar();
        }
    }
```

  (If the probe needed `DwmExtendFrameIntoClientArea`, add that call here too, exactly as the probe did.) Backup: keep `RoundTheCorners` as it is.
- `ShowNearPointer`: after `FitToContent();` and before `AppWindow.Show(activateWindow: true);` call `PlayOpening();`.
- `HideBar`: add `_edgeTurn.Stop();` before `AppWindow.Hide();`.
- New methods:

```csharp
    /// <summary>
    /// With "Let my buddy move" on: the card fades and drops in, the buddy pops up a moment later,
    /// the rows slide in one after another, and the edge starts turning. Off: shown at once, still.
    /// </summary>
    private void PlayOpening()
    {
        UseRowMotion();
        if (_motion.IsOn)
        {
            _edgeTurn.Begin();
            var drop = new Storyboard();
            drop.Children.Add(BuddyAnimations.To(Card, "Opacity", 0, 1, null, 0.32));
            drop.Children.Add(BuddyAnimations.To(CardShift, "Y", -14, 0, new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }, 0.32));
            drop.Begin();
            BuddyAnimations.PopIn(Perch, TimeSpan.FromSeconds(0.12));
        }
        else
        {
            Card.Opacity = 1;
            CardShift.Y = 0;
            Perch.Opacity = 1;
            Perch.RenderTransform = null;
        }
    }

    /// <summary>Rows slide in one after another only while buddies may move.</summary>
    private void UseRowMotion()
    {
        foreach (var list in new[] { NameRowsList, InsideRowsList })
        {
            list.ItemContainerTransitions = _motion.IsOn
                ? [new EntranceThemeTransition { IsStaggeringEnabled = true, FromHorizontalOffset = 0, FromVerticalOffset = -6 }]
                : null;
        }
    }

    /// <summary>Each kind of file's tile colour, as in the mockup: document blue, PDF red, picture orange, video violet, anything else grey.</summary>
    public static Brush KindBrush(string kind)
    {
        var (from, to) = kind switch
        {
            "doc" => ("#4F8CFF", "#2A5BD7"),
            "pdf" => ("#FF6B6B", "#D63D3D"),
            "img" => ("#FFB347", "#F07B2E"),
            "vid" => ("#A77BFF", "#7042D9"),
            _ => ("#7D8A99", "#56616E"),
        };

        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = WindowsAppearanceApplier.ToColor(from), Offset = 0 },
                new GradientStop { Color = WindowsAppearanceApplier.ToColor(to), Offset = 1 },
            },
        };
    }
```

  (Usings: `Microsoft.UI.Xaml.Media`, `Microsoft.UI.Xaml.Media.Animation`. The collection expression `[new EntranceThemeTransition …]` targets `TransitionCollection`; if the compiler refuses it, write `new TransitionCollection { new EntranceThemeTransition { … } }`.)

- [ ] **Step 5: Build and run everything**

Run the build commands. Expected: 0 warnings, all tests PASS (including the existing `QuickSearchLayoutTests` for the placement, the polite line, the raw buddy, the plain snippets), formatting clean.

- [ ] **Step 6: Look at it in the UI preview**

Build the preview into the scratch folder and start it. Press **Ctrl + Alt + D**: the card drops in, the buddy pops up on its edge, the edge turns slowly; type `lesson`: the rows slide in with a red PDF tile, the selected row has the mint edge and "Open ↵"; Down moves it; Esc hides; click outside hides. Turn **Let my buddy move** off on My workspace and press the shortcut again: the bar appears at once and nothing moves. Close the preview. If you cannot look, say so plainly in the task report.

- [ ] **Step 7: Update the docs**

- `docs/TESTING.md`, the Quick search bar row: add "each row's kind tile (DOC, PDF, IMG, VID, FILE) and the selected row's Enter hint; the search icon, Esc hint, and turning edge (only while buddies may move)" and the new test names.
- `docs/UI-UX.md` "The bar": replace the fallback-shape description with look C (or the backup, per the verdict): 20 px corners, the turning mint → blue → violet → pink edge, the glow (see-through only), the perched buddy with its gradient bubble, the large search box with its icon and Esc hint and mint underline, the row tiles, pill examples, the opening, and "all still when Let my buddy move is off".

- [ ] **Step 8: Review the diff and commit**

```bash
git add -A src tests docs
git commit -m "Give the quick search bar its glowing edge, perched buddy, and opening

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Docs, manual check, and handoff

**Files:**
- Modify: `docs/decisions/0047-quick-search-opens-files.md`, `docs/security/2026-09-25-quick-search-review.md`, `docs/MANUAL-TESTING.md`, `docs/RELEASE-NOTES.md`, `docs/ROADMAP.md`, `docs/superpowers/specs/2026-09-25-quick-search-polish-design.md`, `docs/HANDOFF.md`

- [ ] **Step 1: ADR and security review**

- ADR 0047: add a dated "Update 2026-09-25: shortcut choice and motion" section: the shortcut is one of three fixed combinations (Ctrl + Alt + D default, Ctrl + Alt + Space, Ctrl + Shift + Space), still `RegisterHotKey` + `MOD_NOREPEAT`, one at a time, the old one given back first, a closed list so a stored value cannot name another key, a refused one is not replaced by the old one; buddies follow DeskAI's "Let my buddy move" switch, not Windows' Animation effects (the owner's choice; why: the switch is on the card with the buddies in plain words). Leave the original decision text as history.
- The security review: add a dated "Polish (2026-09-25)" section with the spec's four threat cases and how each is covered (closed enum and its test; `RegisterHotKey` only and the source test; unregister-before-register in `GlobalHotKey` and the recording fake; the motion switch's place and page test; the see-through window's click and focus behaviour from the probe, or "not used" for the backup), and that nothing in what quick search reads, opens, keeps, or sends changed.

- [ ] **Step 2: Manual check**

Add a "2026-09-25 Quick search polish" section to `docs/MANUAL-TESTING.md` (use the UI preview build): press **Ctrl + Alt + D** over another app and watch the opening; check the glow and the see-through area (or the backup layout); type `lesson`, check the PDF tile and "Open ↵", Down, Enter; Esc; click outside; on My workspace pick **Ctrl + Shift + Space**, check the switch's words and the icon's tooltip near the clock, and that the old shortcut no longer opens the bar; pick a shortcut another program uses (if one is known) and read the message; click each face and watch the stage; use the arrow keys on the faces; turn **Let my buddy move** off and check the stage, the bar, and the welcome's Sparky are still (Privacy and AI → Show the welcome again); turn it back on; Start fresh and check Ctrl + Alt + D and moving buddies.

- [ ] **Step 3: Release notes and roadmap**

- `docs/RELEASE-NOTES.md`: under an "Unreleased" heading at the top (1.3.0 is not decided): quick search's shortcut is now **Ctrl + Alt + D** for everyone (it was Ctrl + Alt + Space, which another popular app also uses); you can pick Ctrl + Alt + Space or Ctrl + Shift + Space on My workspace; new "Let my buddy move" switch; the new buddy chooser; the new bar look.
- `docs/ROADMAP.md`: under the 2026-09-25 quick search line, add "✅ 2026-09-25: quick search polish (shortcut choice, motion switch, buddy stage, glowing bar), on `quick-search`, not released."

- [ ] **Step 4: Spec rulings**

In the spec's "Rulings made while building", add any ruling made in Tasks 1–5 that is not there yet, including: the help topic names Ctrl + Alt + D "or the one you pick on this card" because the help text is fixed; the refusal message is also used when turning the switch on; the motion switch sits under the faces; the faces are a `RadioButtons` group, so moving with the arrow keys chooses as it moves.

- [ ] **Step 5: Handoff**

Rewrite `docs/HANDOFF.md` "Start here" following its "How to update this file": the polish is built on `quick-search` (the last commit), what was verified (build warnings, test count, formatting, what was seen in the preview), the probe verdict, the manual check to do, the launchable exe path, and the next step: ask the owner whether to check by hand, then whether to push `quick-search` and release it as 1.3.0 (do not push, tag, or release without asking). Move the old "Start here" text under a dated "Earlier state" heading. Update the starter prompt.

- [ ] **Step 6: Build, test, format, commit**

Run the build commands (ask the owner to close DeskAI if it is open, so the final Release build writes the launchable exe). Then:

```bash
git add -A docs
git commit -m "Document the quick search polish, its manual check, and the handoff

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Report**

Tell the owner, in plain words: what changed, how it fits together, the new C#/.NET ideas (a closed enum as a safety list, a singleton with an event, `RadioButtons`, storyboards), the build and test evidence, the proof that the AI still cannot reach private folders (no change to the AI code or its capabilities; `QuickSearchContainmentTests` still pass), what to try by hand, the exe path, and the next step.
