# Quick Search with a Search Buddy — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ctrl + Alt + Space anywhere shows a slim bar near the top of the screen with an animated
search buddy perched on it; typing finds files in connected folders by name at once and by the
words inside them a moment later; Enter opens familiar file types in their usual app and shows
everything else in File Explorer.

**Architecture:** Core gets a pure `DeskAI.Core.QuickSearch` namespace (open rule, buddy lines,
settings, `QuickSearchService` wrapping the existing `FileSearchService` and `ContentSearchService`,
and the `IFileLauncher` contract). Infrastructure's `WindowsFileLauncher` re-checks the live file
and hands one validated path to an `IShellStarter` seam (a do-nothing one by default; the real one
only in the app). Presentation gets `QuickSearchViewModel`, the My workspace card, the Home/Search
tip, a welcome page, and teaches `BackgroundPresenceController` that quick search also keeps DeskAI
near the clock. The App adds `GlobalHotKey` (`RegisterHotKey`, not a keyboard hook),
`QuickSearchWindow`, and one XAML file per buddy.

**Tech Stack:** C# / .NET 10, WinUI 3 (Windows App SDK 2.4), CommunityToolkit.Mvvm, SQLite
key/value store (`app_settings`), xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-25-quick-search-design.md` (read it fully; its
"Decisions the owner made" 1–19 are settled). Art reference:
`docs/superpowers/specs/2026-09-25-quick-search-mockups/characters.html` (layout:
`layouts.html`, layout C).

## Global Constraints

- Shortcut is exactly **Ctrl + Alt + Space**, registered with `RegisterHotKey` + `MOD_NOREPEAT`; no keyboard hook, no key picker.
- No AI, no network, nothing sent. Nothing typed, found, or read is saved or logged (no phrase, file name, or snippet in any log).
- Stored values only: `quicksearch.on`, `quicksearch.buddy`, `quicksearch.tip.dismissed` in `app_settings`. Missing = on, Sparky, not dismissed. Start fresh removes all three. No schema change.
- Reading inside files goes only through the existing `ContentSearchService.SearchAsync` (never `SearchPdfOcrAsync`, never visual search), with its existing permissions and limits (50 files, 20 s). The bar grants no permission.
- Open allow-list, exactly: `.txt .md .rtf .csv .pdf .docx .xlsx .pptx .odt .ods .odp .jpg .jpeg .png .gif .webp .bmp .heic .mp3 .wav .m4a .flac .mp4 .mov .mkv .avi .zip`. The **last** extension decides. Everything else is Show in folder only.
- Rows: at most 5 **By name** and 5 **Words inside**; a file listed by name is not listed again. Name pause ≈150 ms, inside pause ≈600 ms; a keystroke or hiding cancels both.
- Buddies: Sparky (default), Archie the owl, Pip the robot, Fetch the fox, Inky the octopus, Mochi, Paige the paper ghost; five moods Idle, Thinking, Found, Nothing, Happy; no sounds; each buddy line under 40 characters; still poses when Windows animation effects are off.
- Visible words are copied verbatim from the spec's "What the person sees", "Honest lines", and "The seven buddies" sections.
- Closing the window keeps DeskAI near the clock while quick search is on **and** the icon is actually showing; quitting is from the icon's menu. DeskAI never adds itself to Windows startup.
- Tests use generated temp data only (`TestApp`); no real hotkey is registered and no real process is started in any test (`IShellStarter` default does nothing; `TestApp` records).
- Every person-visible behaviour gets a page test in `DeskAI.Presentation.Tests` and a row in the Feature Coverage Map in `docs/TESTING.md`.

## Rulings made while planning (the owner may overrule; also added to the spec)

- `IFileLauncher` takes the folder's **ID**, not the folder object, so the launcher itself looks the
  folder up at the moment of opening and refuses one that was disconnected in the meantime.
- The "DeskAI is still running" Windows notification stays tied to background checking only. When
  only quick search keeps DeskAI near the clock, no notification is shown (the owner did not pick a
  notification for discovery, decision 12); the Quick search card and help say what closing does.
- "Checking happens only while DeskAI is open. Closing it stops everything." becomes "… Closing it
  stops checking." and Start fresh's dialog says checking stops, because closing no longer always
  quits.
- **Open DeskAI** on the "connect a folder first" line opens Home (its Your folders card).

## Review Focus

1. A file renamed from `report.pdf` to `report.pdf.exe` (or swapped for a link) after DeskAI remembered it, then chosen in the bar → nothing is started; the bar says why. (Task 4 tests pin the live-name and link checks.)
2. Typing quickly, or pressing Esc while a slow inside look runs → the old look stops, its late results never appear under newer words. (Task 5 test with a blocking reader.)
3. A folder whose reading-inside permission was withdrawn on Search while the bar is hidden → the next inside look reads nothing there. (Task 3 test.)
4. Quick search on, but Windows refuses the icon near the clock (or no icon exists) → closing the window really quits, never leaves an invisible DeskAI with no way to stop it. (Task 6 test.)
5. Start fresh while quick search was switched off → quick search is on again with Sparky, and the tip returns. (Task 2 and Task 6 tests.)

---

### Task 1: ADR 0047 and the security review (no code)

**Files:**
- Create: `docs/decisions/0047-quick-search-opens-files.md`
- Create: `docs/security/2026-09-25-quick-search-review.md`
- Modify: `docs/decisions/0025-*.md` (one "Amended by ADR 0047" line at the top; find the file with `ls docs/decisions/0025-*`)

(The spec already carries the "Rulings made while planning".)

- [ ] **Step 1: Read the templates.** Read `docs/decisions/0045-tag-names-renames-folders.md` and `docs/security/2026-09-24-tag-names-review.md` and copy their headings and tone.

- [ ] **Step 2: Write ADR 0047.** Status: Accepted (2026-09-25, owner). Context: DeskAI has never opened a file or started a process; quick search needs Enter to open a file and a shortcut that works with the window closed. Decision, as numbered points:
  1. Only the person's own press (Enter, click, or the row's button) opens a file; only a file in a connected folder; only a type on the allow-list (copy it from Global Constraints); the last extension decides.
  2. The launcher re-checks the **live** file just before starting anything: folder still connected and searchable; path normalised and inside the folder; no folder or file on the way is a link or junction; not protected by `IPathPolicy`; exists and is a file; for Open, the live name is still allow-listed.
  3. Open uses the Windows shell's "open" action for that one path (the person's own choice of app). Show in folder starts `%WINDIR%\explorer.exe /select,"<path>"`. Nothing else is ever started. Both go through `IShellStarter`; the shared registration holds a do-nothing starter and only the app registers the real one.
  4. The shortcut is `RegisterHotKey(Ctrl+Alt+Space, MOD_NOREPEAT)`: Windows reports only this one combination; DeskAI sees nothing else typed.
  5. Amends ADR 0025: closing the window keeps DeskAI near the clock while quick search is on and the icon is showing, and DeskAI does no checking or tidying in that state unless background checking was separately turned on. The icon menu gains **Find a file**; **Pause checking** appears only when background checking is on.
  6. The bar reads inside files only through `ContentSearchService.SearchAsync` with the permissions already given on Search; it never grants one and never runs the scanned-PDF reader or picture reading.
  7. No AI anywhere in quick search. `IFileLauncher` is held only by `QuickSearchViewModel` (a test checks).
  Consequences: accepted time-of-check to time-of-use gap (the person's own file, own folder, own press, allow-listed type); Windows' choice of app per type is out of scope; words from inside files appear over other apps only after the person's own shortcut press.

- [ ] **Step 3: Write the security review.** Sections: What changes; Assets; Threats and answers (one row each: program disguised as a document; file changed since remembered; `..`/link/junction escape; protected location; time-of-check gap; many looks while typing; words shown over another app; snippet as untrusted text; bar granting permission; AI reaching the launcher; hotkey as a key logger; DeskAI invisible with no way to quit); Tests that pin each answer (name the test classes from Tasks 2–7); Residual risks. Verdict: acceptable with the listed checks.

- [ ] **Step 4: Commit.**

```powershell
git add docs/decisions docs/security
git commit -m "Record why quick search may open files and stay near the clock (ADR 0047)"
```

---

### Task 2: Core — the open rule, the buddies' lines, and the settings

**Files:**
- Create: `src/DeskAI.Core/QuickSearch/FileOpenRule.cs`
- Create: `src/DeskAI.Core/QuickSearch/SearchBuddy.cs`
- Create: `src/DeskAI.Core/QuickSearch/SearchBuddyLines.cs`
- Create: `src/DeskAI.Core/QuickSearch/QuickSearchSettings.cs`
- Modify: `src/DeskAI.Core/Backup/FreshStartService.cs` (after the `WelcomeService.ShownKey` line, ~line 104)
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs` (register `QuickSearchSettingsService`)
- Test: `tests/DeskAI.Core.Tests/FileOpenRuleTests.cs`
- Test: `tests/DeskAI.Core.Tests/QuickSearchSettingsServiceTests.cs`
- Test: `tests/DeskAI.Presentation.Tests/SearchBuddyLinesTests.cs` (uses `HelpCatalog.BannedWords`)
- Test: `tests/DeskAI.Presentation.Tests/FreshStartPageTests.cs` (one new test)

**Interfaces:**
- Produces (namespace `DeskAI.Core.QuickSearch`):
  - `enum OpenChoice { Open, ShowInFolderOnly }`; `static class FileOpenRule { IReadOnlySet<string> OpenableExtensions; OpenChoice For(string fileName); }`
  - `enum SearchBuddy { Sparky, Archie, Pip, Fetch, Inky, Mochi, Paige }`; `enum BuddyMood { Idle, Thinking, Found, Nothing, Happy }`
  - `static class SearchBuddyLines { string Name(SearchBuddy); string Line(SearchBuddy, BuddyMood, int found = 0); const int MaxLength = 39; }`
  - `sealed record QuickSearchSettings(bool IsOn, SearchBuddy Buddy, bool TipDismissed) { static QuickSearchSettings Default; }`
  - `sealed class QuickSearchSettingsService(IAppSettingsStore)` with `const string OnKey = "quicksearch.on"`, `BuddyKey = "quicksearch.buddy"`, `TipKey = "quicksearch.tip.dismissed"`, `static IReadOnlyList<string> Keys`, `Task<QuickSearchSettings> LoadAsync(CancellationToken = default)`, `Task SetOnAsync(bool, CancellationToken = default)`, `Task SetBuddyAsync(SearchBuddy, CancellationToken = default)`, `Task DismissTipAsync(CancellationToken = default)`.

- [ ] **Step 1: Write the failing Core tests.**

`tests/DeskAI.Core.Tests/FileOpenRuleTests.cs`:

```csharp
using DeskAI.Core.QuickSearch;

namespace DeskAI.Core.Tests;

/// <summary>
/// Which files quick search may open in their usual app. Everything not on the known-safe list
/// is only shown in its folder, and a second extension never disguises a program.
/// </summary>
public sealed class FileOpenRuleTests
{
    [Theory]
    [InlineData("essay.pdf")]
    [InlineData("Holiday.JPG")]
    [InlineData("notes.md")]
    [InlineData("budget.xlsx")]
    [InlineData("song.flac")]
    [InlineData("backup.zip")]
    public void Familiar_files_open(string name) => Assert.Equal(OpenChoice.Open, FileOpenRule.For(name));

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("invoice.pdf.exe")]
    [InlineData("run.bat")]
    [InlineData("script.ps1")]
    [InlineData("game.lnk")]
    [InlineData("page.html")]
    [InlineData("macro.docm")]
    [InlineData("old.doc")]
    [InlineData("README")]
    [InlineData("report.pdf.")]
    [InlineData("report.pdf ")]
    [InlineData("")]
    public void Everything_else_is_only_shown_in_its_folder(string name) =>
        Assert.Equal(OpenChoice.ShowInFolderOnly, FileOpenRule.For(name));

    [Fact]
    public void The_list_is_exactly_the_agreed_one()
    {
        Assert.Equal(
            [".avi", ".bmp", ".csv", ".docx", ".flac", ".gif", ".heic", ".jpeg", ".jpg", ".m4a", ".md", ".mkv", ".mov", ".mp3", ".mp4",
             ".odp", ".ods", ".odt", ".pdf", ".png", ".pptx", ".rtf", ".txt", ".wav", ".webp", ".xlsx", ".zip"],
            FileOpenRule.OpenableExtensions.Order(StringComparer.Ordinal));
    }
}
```

`tests/DeskAI.Core.Tests/QuickSearchSettingsServiceTests.cs` (copy the `FakeStore` and `FakeDbException` from `WelcomeServiceTests` into this file as private nested classes):

```csharp
using System.Data.Common;
using DeskAI.Core.Abstractions;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Core.Tests;

/// <summary>Quick search's three remembered values, and their defaults when nothing is remembered.</summary>
public sealed class QuickSearchSettingsServiceTests
{
    [Fact]
    public async Task Nothing_remembered_means_on_with_Sparky_and_the_tip_showing()
    {
        var service = new QuickSearchSettingsService(new FakeStore());

        Assert.Equal(QuickSearchSettings.Default, await service.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new QuickSearchSettings(true, SearchBuddy.Sparky, false), QuickSearchSettings.Default);
    }

    [Fact]
    public async Task Choices_are_remembered()
    {
        var store = new FakeStore();
        var service = new QuickSearchSettingsService(store);
        var token = TestContext.Current.CancellationToken;

        await service.SetOnAsync(false, token);
        await service.SetBuddyAsync(SearchBuddy.Inky, token);
        await service.DismissTipAsync(token);

        Assert.Equal(new QuickSearchSettings(false, SearchBuddy.Inky, true), await service.LoadAsync(token));
        Assert.Equal(["quicksearch.buddy", "quicksearch.on", "quicksearch.tip.dismissed"], store.Values.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_buddy_name_DeskAI_does_not_know_falls_back_to_Sparky()
    {
        var store = new FakeStore();
        store.Values[QuickSearchSettingsService.BuddyKey] = "Godzilla";

        Assert.Equal(SearchBuddy.Sparky, (await new QuickSearchSettingsService(store).LoadAsync(TestContext.Current.CancellationToken)).Buddy);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_gives_the_defaults()
    {
        var service = new QuickSearchSettingsService(new FakeStore { FailReads = true });

        Assert.Equal(QuickSearchSettings.Default, await service.LoadAsync(TestContext.Current.CancellationToken));
    }

    // FakeStore and FakeDbException: copied from WelcomeServiceTests.
}
```

- [ ] **Step 2: Run them and see them fail.**

Run: `dotnet test tests/DeskAI.Core.Tests -c Release --filter "FullyQualifiedName~FileOpenRuleTests|FullyQualifiedName~QuickSearchSettingsServiceTests"`
Expected: build error, `DeskAI.Core.QuickSearch` does not exist.

- [ ] **Step 3: Write `FileOpenRule.cs`.**

```csharp
namespace DeskAI.Core.QuickSearch;

/// <summary>What quick search may do with a file: open it in its usual app, or only show it in its folder.</summary>
public enum OpenChoice { Open, ShowInFolderOnly }

/// <summary>
/// Decides from a file's name whether quick search may open it in its usual app.
/// </summary>
/// <remarks>
/// An allow-list, because listing every risky kind of file is impossible and missing one would
/// start a program. Only the <b>last</b> extension counts, so "invoice.pdf.exe" is a program. A
/// name ending in a dot or a space has no familiar extension, so it is only shown in its folder
/// even though Windows would drop the dot. Asked twice: when a row is shown (from the remembered
/// name) and again on the live name just before opening (see ADR 0047).
/// </remarks>
public static class FileOpenRule
{
    public static IReadOnlySet<string> OpenableExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".rtf", ".csv", ".pdf", ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic",
        ".mp3", ".wav", ".m4a", ".flac", ".mp4", ".mov", ".mkv", ".avi", ".zip",
    };

    public static OpenChoice For(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.EndsWith('.') || fileName.EndsWith(' '))
        {
            return OpenChoice.ShowInFolderOnly;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Length > 1 && OpenableExtensions.Contains(extension)
            ? OpenChoice.Open
            : OpenChoice.ShowInFolderOnly;
    }
}
```

- [ ] **Step 4: Write `SearchBuddy.cs` and `SearchBuddyLines.cs`.**

```csharp
namespace DeskAI.Core.QuickSearch;

/// <summary>The seven search buddies. The order is the order of the tiles on My workspace.</summary>
public enum SearchBuddy { Sparky, Archie, Pip, Fetch, Inky, Mochi, Paige }

/// <summary>How the buddy looks and what it says right now.</summary>
public enum BuddyMood { Idle, Thinking, Found, Nothing, Happy }
```

```csharp
namespace DeskAI.Core.QuickSearch;

/// <summary>
/// What each buddy says, in its own voice. Flavour only: the facts are always stated separately
/// under the box in DeskAI's usual words, so no character can blur them.
/// </summary>
/// <remarks>
/// The lines hold no noun that changes with the number ("Found 1!", "Found 3!"), so one form
/// reads right for one and for many.
/// </remarks>
public static class SearchBuddyLines
{
    /// <summary>Every line stays under 40 characters so the bubble never wraps into a paragraph.</summary>
    public const int MaxLength = 39;

    public static string Name(SearchBuddy buddy) => buddy switch
    {
        SearchBuddy.Archie => "Archie the owl",
        SearchBuddy.Pip => "Pip the robot",
        SearchBuddy.Fetch => "Fetch the fox",
        SearchBuddy.Inky => "Inky the octopus",
        SearchBuddy.Mochi => "Mochi",
        SearchBuddy.Paige => "Paige the paper ghost",
        _ => "Sparky",
    };

    public static string Line(SearchBuddy buddy, BuddyMood mood, int found = 0) => (buddy, mood) switch
    {
        (SearchBuddy.Archie, BuddyMood.Idle) => "Which file shall we find?",
        (SearchBuddy.Archie, BuddyMood.Thinking) => "Searching the shelves…",
        (SearchBuddy.Archie, BuddyMood.Found) => $"Ah, {found} in the archives.",
        (SearchBuddy.Archie, BuddyMood.Nothing) => "Nothing on my shelves.",
        (SearchBuddy.Archie, BuddyMood.Happy) => "Hoo-hoo!",
        (SearchBuddy.Pip, BuddyMood.Idle) => "Ready to scan.",
        (SearchBuddy.Pip, BuddyMood.Thinking) => "Scanning…",
        (SearchBuddy.Pip, BuddyMood.Found) => $"Scan done: {found} found.",
        (SearchBuddy.Pip, BuddyMood.Nothing) => "Scan done: no match.",
        (SearchBuddy.Pip, BuddyMood.Happy) => "Beep boop!",
        (SearchBuddy.Fetch, BuddyMood.Idle) => "Want me to fetch something?",
        (SearchBuddy.Fetch, BuddyMood.Thinking) => "Sniffing…",
        (SearchBuddy.Fetch, BuddyMood.Found) => $"Fetched {found}!",
        (SearchBuddy.Fetch, BuddyMood.Nothing) => "I sniffed everywhere…",
        (SearchBuddy.Fetch, BuddyMood.Happy) => "Wag wag!",
        (SearchBuddy.Inky, BuddyMood.Idle) => "All arms ready!",
        (SearchBuddy.Inky, BuddyMood.Thinking) => "Reaching…",
        (SearchBuddy.Inky, BuddyMood.Found) => $"Grabbed {found}!",
        (SearchBuddy.Inky, BuddyMood.Nothing) => "Nothing in reach.",
        (SearchBuddy.Inky, BuddyMood.Happy) => "Blub!",
        (SearchBuddy.Mochi, BuddyMood.Idle) => "Hello! What shall we find?",
        (SearchBuddy.Mochi, BuddyMood.Thinking) => "Hmm hmm…",
        (SearchBuddy.Mochi, BuddyMood.Found) => $"Yay, {found} found!",
        (SearchBuddy.Mochi, BuddyMood.Nothing) => "Aww, nothing yet.",
        (SearchBuddy.Mochi, BuddyMood.Happy) => "Squish!",
        (SearchBuddy.Paige, BuddyMood.Idle) => "Boo! Looking for something?",
        (SearchBuddy.Paige, BuddyMood.Thinking) => "Floating through…",
        (SearchBuddy.Paige, BuddyMood.Found) => $"Boo! Found {found}.",
        (SearchBuddy.Paige, BuddyMood.Nothing) => "Not a ghost of a match.",
        (SearchBuddy.Paige, BuddyMood.Happy) => "Hee hee!",
        (_, BuddyMood.Thinking) => "Looking…",
        (_, BuddyMood.Found) => $"Found {found}!",
        (_, BuddyMood.Nothing) => "Hmm, nothing yet.",
        (_, BuddyMood.Happy) => "Wheee!",
        _ => "Hi! What are we looking for?",
    };
}
```

- [ ] **Step 5: Write `QuickSearchSettings.cs`.**

```csharp
using System.Data.Common;
using DeskAI.Core.Abstractions;

namespace DeskAI.Core.QuickSearch;

/// <summary>Whether quick search listens for its shortcut, which buddy shows, and whether the tip was closed.</summary>
public sealed record QuickSearchSettings(bool IsOn, SearchBuddy Buddy, bool TipDismissed)
{
    public static QuickSearchSettings Default { get; } = new(true, SearchBuddy.Sparky, false);
}

/// <summary>
/// Reads and writes quick search's three small values in the app settings store.
/// </summary>
/// <remarks>
/// Holds the settings store and nothing else. A store that cannot be read gives the defaults:
/// quick search on is the owner's chosen default (decision 19), so failing towards it changes
/// nothing a person chose, and the switch on My workspace still turns it off.
/// </remarks>
public sealed class QuickSearchSettingsService(IAppSettingsStore store)
{
    public const string OnKey = "quicksearch.on";
    public const string BuddyKey = "quicksearch.buddy";
    public const string TipKey = "quicksearch.tip.dismissed";

    /// <summary>Everything Start fresh must forget.</summary>
    public static IReadOnlyList<string> Keys { get; } = [OnKey, BuddyKey, TipKey];

    private readonly IAppSettingsStore _store = store;

    public async Task<QuickSearchSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var on = await _store.ReadAsync(OnKey, cancellationToken).ConfigureAwait(false);
            var buddy = await _store.ReadAsync(BuddyKey, cancellationToken).ConfigureAwait(false);
            var tip = await _store.ReadAsync(TipKey, cancellationToken).ConfigureAwait(false);
            return new QuickSearchSettings(
                on != "no",
                Enum.TryParse<SearchBuddy>(buddy, ignoreCase: false, out var chosen) && Enum.IsDefined(chosen) ? chosen : SearchBuddy.Sparky,
                tip == "yes");
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbException or IOException or UnauthorizedAccessException)
        {
            return QuickSearchSettings.Default;
        }
    }

    public Task SetOnAsync(bool isOn, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(OnKey, isOn ? "yes" : "no", cancellationToken);

    public Task SetBuddyAsync(SearchBuddy buddy, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(BuddyKey, buddy.ToString(), cancellationToken);

    public Task DismissTipAsync(CancellationToken cancellationToken = default) =>
        _store.WriteAsync(TipKey, "yes", cancellationToken);
}
```

Note: `Enum.TryParse` accepts numbers ("3"); `Enum.IsDefined` keeps "99" out. Add `[InlineData("99")]`-style coverage by also asserting `"99"` gives Sparky in the fallback test if you want belt and braces.

- [ ] **Step 6: Forget the values on Start fresh.** In `FreshStartService.StartFreshAsync`, directly after the `Welcome.WelcomeService.ShownKey` line:

```csharp
        foreach (var key in QuickSearch.QuickSearchSettingsService.Keys)
        {
            await _appSettings.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
```

Register in `DeskAiApplicationServices` next to `WelcomeService`:

```csharp
        // Quick search's three remembered values (ADR 0047). The settings store and nothing else.
        services.AddSingleton<QuickSearchSettingsService>();
```
(add `using DeskAI.Core.QuickSearch;`).

- [ ] **Step 7: Write the Presentation tests** (`SearchBuddyLinesTests.cs`, and one test in `FreshStartPageTests.cs`).

```csharp
using System.Text.RegularExpressions;
using DeskAI.App.Help;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>Each buddy speaks in its own short voice and never in technical words.</summary>
public sealed class SearchBuddyLinesTests
{
    public static TheoryData<SearchBuddy, BuddyMood> Every() =>
        new(Enum.GetValues<SearchBuddy>().SelectMany(buddy => Enum.GetValues<BuddyMood>().Select(mood => (buddy, mood))));

    [Theory]
    [MemberData(nameof(Every))]
    public void Every_line_is_short_and_plain(SearchBuddy buddy, BuddyMood mood)
    {
        var line = SearchBuddyLines.Line(buddy, mood, found: 12);
        Assert.InRange(line.Length, 1, SearchBuddyLines.MaxLength);
        foreach (var word in HelpCatalog.BannedWords)
        {
            Assert.False(Regex.IsMatch(line, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase), $"{buddy} {mood}: '{word}'");
        }
    }

    [Fact]
    public void Each_buddy_has_its_own_greeting_found_and_nothing_lines()
    {
        foreach (var mood in new[] { BuddyMood.Idle, BuddyMood.Found, BuddyMood.Nothing, BuddyMood.Happy })
        {
            var lines = Enum.GetValues<SearchBuddy>().Select(buddy => SearchBuddyLines.Line(buddy, mood, 2)).ToArray();
            Assert.Equal(lines.Length, lines.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void One_found_reads_right()
    {
        Assert.Equal("Found 1!", SearchBuddyLines.Line(SearchBuddy.Sparky, BuddyMood.Found, 1));
        Assert.Equal("Scan done: 1 found.", SearchBuddyLines.Line(SearchBuddy.Pip, BuddyMood.Found, 1));
    }

    [Fact]
    public void Sparky_is_the_first_and_default_buddy() =>
        Assert.Equal(SearchBuddy.Sparky, Enum.GetValues<SearchBuddy>()[0]);
}
```

In `FreshStartPageTests.cs`, add (follow the file's existing pattern for opening Settings and starting fresh — reuse its helper that confirms Start fresh):

```csharp
    [Fact]
    public async Task Start_fresh_turns_quick_search_back_on_with_Sparky_and_the_tip()
    {
        await using var app = await TestApp.StartAsync();
        var quick = app.Get<QuickSearchSettingsService>();
        await quick.SetOnAsync(false);
        await quick.SetBuddyAsync(SearchBuddy.Mochi);
        await quick.DismissTipAsync();

        await app.Get<SettingsViewModel>().StartFreshAsync();

        Assert.Equal(QuickSearchSettings.Default, await quick.LoadAsync());
    }
```

- [ ] **Step 8: Run all the new tests and see them pass.**

Run: `dotnet test DeskAI.sln -c Release --filter "FullyQualifiedName~FileOpenRuleTests|FullyQualifiedName~QuickSearchSettingsServiceTests|FullyQualifiedName~SearchBuddyLinesTests|FullyQualifiedName~FreshStartPageTests"`
Expected: all pass.

- [ ] **Step 9: Commit.**

```powershell
git add src/DeskAI.Core/QuickSearch src/DeskAI.Core/Backup/FreshStartService.cs src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs tests
git commit -m "Add quick search's open rule, the buddies' lines, and its three remembered choices"
```

---

### Task 3: Core — `QuickSearchService` (by name, then inside)

**Files:**
- Modify: `src/DeskAI.Core/Search/ContentSearchService.cs` (`ContentHit` gains `RootId`; set it where hits are made, ~line 272)
- Create: `src/DeskAI.Core/QuickSearch/QuickSearchService.cs`
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs`
- Test: `tests/DeskAI.Presentation.Tests/QuickSearchServiceTests.cs` (uses `TestApp` for a real index and real reading)

**Interfaces:**
- Consumes: `FileSearchService.SearchAsync(string?, DateTimeOffset, Guid?, CancellationToken)`, `ContentSearchService.SearchAsync(string?, DateTimeOffset, Guid?, CancellationToken)`, `RootCapabilities.CanReadContent`, `FileOpenRule.For`.
- Produces (namespace `DeskAI.Core.QuickSearch`):
  - `sealed record QuickSearchRow(Guid RootId, string RelativePath, string Name, string Where, FileCategory Category, OpenChoice Choice) { string Snippet { get; init; } = ""; string? Section { get; init; } string Key => $"{RootId:N}|{RelativePath}"; }`
  - `enum NameFact { None, NoFolders, NotUnderstood, NothingMatched, MoreThanShown }`
  - `sealed record NameResults(IReadOnlyList<QuickSearchRow> Rows, NameFact Fact, bool AnyFolderReadsInside)`
  - `sealed record InsideResults(IReadOnlyList<QuickSearchRow> Rows, bool WasSearched, int FilesRead, bool ReachedLimit)`
  - `sealed class QuickSearchService(FileSearchService names, ContentSearchService inside, IAuthorizedRootRepository roots)` with `const int MaxRows = 5`, `Task<NameResults> FindByNameAsync(string? phrase, DateTimeOffset nowUtc, CancellationToken = default)`, `Task<InsideResults> FindInsideAsync(string? phrase, DateTimeOffset nowUtc, IReadOnlySet<string> alreadyListed, CancellationToken = default)`, `static string WhereText(string rootName, string relativePath)`.
  - `ContentHit.RootId` (`Guid`, init-only).

- [ ] **Step 1: Write the failing tests.** Look at `SearchPageTests.Allowing_reading_inside_finds_words_in_text_files_and_says_how_many_were_opened` for how a folder is connected and reading inside is allowed; do the same through `SearchViewModel` so the permissions are the real ones.

```csharp
using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Quick search's two looks over generated folders: names first, then the words inside files,
/// only where reading inside was allowed on Search, never listing a file twice.
/// </summary>
public sealed class QuickSearchServiceTests
{
    [Fact]
    public async Task No_folder_connected_says_so()
    {
        await using var app = await TestApp.StartAsync();

        var result = await app.Get<QuickSearchService>().FindByNameAsync("essay", DateTimeOffset.UtcNow);

        Assert.Equal(NameFact.NoFolders, result.Fact);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Names_give_at_most_five_rows_each_saying_where_it_is_and_what_Enter_does()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay 1.pdf", "essay 2.pdf", "essay 3.pdf", "essay 4.pdf", "essay 5.pdf", "essay 6.pdf", "essay.exe");

        var result = await app.Get<QuickSearchService>().FindByNameAsync("essay", DateTimeOffset.UtcNow);

        Assert.Equal(QuickSearchService.MaxRows, result.Rows.Count);
        Assert.Equal(NameFact.MoreThanShown, result.Fact);
        Assert.All(result.Rows, row => Assert.Equal("School", row.Where));
        Assert.False(result.AnyFolderReadsInside);
    }

    [Fact]
    public async Task A_program_is_offered_only_in_its_folder()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Stuff", "invoice.pdf.exe");

        var row = Assert.Single((await app.Get<QuickSearchService>().FindByNameAsync("invoice", DateTimeOffset.UtcNow)).Rows);

        Assert.Equal(OpenChoice.ShowInFolderOnly, row.Choice);
    }

    [Fact]
    public async Task A_file_one_folder_down_says_so()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Documents");
        app.MakeFile(Path.Combine("Documents", "School"), "essay.docx");
        await ConnectFolderAsync(app, folder);

        var row = Assert.Single((await app.Get<QuickSearchService>().FindByNameAsync("essay", DateTimeOffset.UtcNow)).Rows);

        Assert.Equal("Documents › School", row.Where);
    }

    [Fact]
    public async Task Words_inside_are_found_only_where_reading_inside_is_allowed_and_never_twice()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Notes");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "shopping.txt"), "buy bananas and bread");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "bananas.txt"), "a list of bananas");
        var search = await ConnectFolderAsync(app, folder);
        var quick = app.Get<QuickSearchService>();

        var before = await quick.FindInsideAsync("bananas", DateTimeOffset.UtcNow, new HashSet<string>());
        Assert.False(before.WasSearched);

        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var byName = await quick.FindByNameAsync("bananas", DateTimeOffset.UtcNow);
        Assert.True(byName.AnyFolderReadsInside);
        var inside = await quick.FindInsideAsync("bananas", DateTimeOffset.UtcNow, byName.Rows.Select(row => row.Key).ToHashSet());

        var row = Assert.Single(inside.Rows);
        Assert.Equal("shopping.txt", row.Name);
        Assert.Contains("bananas", row.Snippet, StringComparison.Ordinal);
        Assert.Equal(2, inside.FilesRead);

        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: false);
        Assert.False((await quick.FindInsideAsync("bananas", DateTimeOffset.UtcNow, new HashSet<string>())).WasSearched);
    }

    [Fact]
    public async Task Two_letters_start_no_inside_look()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Notes");
        app.Directory.CreateDummyFile(Path.Combine("folders", "Notes", "a.txt"), "ok ok ok");
        var search = await ConnectFolderAsync(app, folder);
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);

        var inside = await app.Get<QuickSearchService>().FindInsideAsync("ok", DateTimeOffset.UtcNow, new HashSet<string>());

        Assert.False(inside.WasSearched);
        Assert.Equal(0, inside.FilesRead);
    }

    [Fact]
    public void It_holds_nothing_that_can_open_change_or_send()
    {
        var parameters = typeof(QuickSearchService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name);
        Assert.DoesNotContain(parameters, name =>
            name.Contains("Launcher", StringComparison.Ordinal) || name.Contains("Executor", StringComparison.Ordinal)
            || name.Contains("Journal", StringComparison.Ordinal) || name.Contains("Provider", StringComparison.Ordinal)
            || name.Contains("Writer", StringComparison.Ordinal) || name.Contains("Settings", StringComparison.Ordinal));
    }

    private static async Task<SearchViewModel> ConnectAsync(TestApp app, string name, params string[] files) =>
        await ConnectFolderAsync(app, app.MakeFolder(name, files));

    private static async Task<SearchViewModel> ConnectFolderAsync(TestApp app, string folder)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        return search;
    }
}
```

- [ ] **Step 2: Run and see them fail** (`QuickSearchService` missing).

Run: `dotnet test tests/DeskAI.Presentation.Tests -c Release --filter FullyQualifiedName~QuickSearchServiceTests`

- [ ] **Step 3: Add `RootId` to `ContentHit`.** In `ContentSearchService.cs`:

```csharp
public sealed record ContentHit(string RootName, string RelativePath, string Name, string Snippet)
{
    public string? Section { get; init; }

    /// <summary>The connected folder the file is in, so quick search can open it through the launcher's own checks.</summary>
    public Guid RootId { get; init; }
}
```

and in the `hits.Add(new ContentHit(...) { ... })` initializer add `RootId = root.Id,` beside `Section = …`.

- [ ] **Step 4: Write `QuickSearchService.cs`.**

```csharp
using DeskAI.Core.Abstractions;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.QuickSearch;

/// <summary>One row in the quick search bar.</summary>
/// <remarks>Carries the folder's ID and a path relative to it, never a full path, like <see cref="SearchHit"/>.</remarks>
public sealed record QuickSearchRow(Guid RootId, string RelativePath, string Name, string Where, FileCategory Category, OpenChoice Choice)
{
    /// <summary>For Words inside rows: the short piece of text around the match. Untrusted; shown as plain text only.</summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>For Words inside rows: "Page 3", "Slide 2", when known.</summary>
    public string? Section { get; init; }

    /// <summary>Identifies the file across the two groups, so it is never listed twice.</summary>
    public string Key => $"{RootId:N}|{RelativePath}";
}

public enum NameFact { None, NoFolders, NotUnderstood, NothingMatched, MoreThanShown }

public sealed record NameResults(IReadOnlyList<QuickSearchRow> Rows, NameFact Fact, bool AnyFolderReadsInside);

public sealed record InsideResults(IReadOnlyList<QuickSearchRow> Rows, bool WasSearched, int FilesRead, bool ReachedLimit)
{
    public static InsideResults NotSearched { get; } = new([], false, 0, false);
}

/// <summary>
/// The quick search bar's two looks: remembered names first, then the words inside files.
/// </summary>
/// <remarks>
/// <para>
/// Adds no reading power of its own. Names come from <see cref="FileSearchService"/>; words inside
/// come from <see cref="ContentSearchService"/>, with exactly the per-folder, PDF, and slide
/// permissions and the 50-file and 20-second limits Search has. It never calls the scanned-PDF
/// reader or picture reading (ADR 0047).
/// </para>
/// <para>
/// It holds no launcher, AI connection, setting changer, executor, or writer, and stores nothing:
/// the rows live as long as the bar shows them.
/// </para>
/// </remarks>
public sealed class QuickSearchService(FileSearchService names, ContentSearchService inside, IAuthorizedRootRepository roots)
{
    /// <summary>Rows per group. Small on purpose: this is a pop-up; "See more in DeskAI" shows everything.</summary>
    public const int MaxRows = 5;

    private readonly FileSearchService _names = names;
    private readonly ContentSearchService _inside = inside;
    private readonly IAuthorizedRootRepository _roots = roots;

    public async Task<NameResults> FindByNameAsync(string? phrase, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var connected = await _roots.ListAsync(cancellationToken).ConfigureAwait(false);
        var anyInside = connected.Any(RootCapabilities.CanReadContent);
        if (!connected.Any(FileSearchService.IsSearchable))
        {
            return new NameResults([], NameFact.NoFolders, anyInside);
        }

        var outcome = await _names.SearchAsync(phrase, nowUtc, selectedRootId: null, cancellationToken).ConfigureAwait(false);
        if (outcome.UnderstoodNothing)
        {
            return new NameResults([], NameFact.NotUnderstood, anyInside);
        }

        var rows = outcome.Hits
            .Take(MaxRows)
            .Select(hit => new QuickSearchRow(hit.RootId, hit.File.RelativePath, hit.File.Name,
                WhereText(hit.RootName, hit.File.RelativePath), hit.File.Category, FileOpenRule.For(hit.File.Name)))
            .ToArray();
        var fact = outcome.Hits.Count == 0 ? NameFact.NothingMatched
            : outcome.Hits.Count > MaxRows ? NameFact.MoreThanShown
            : NameFact.None;
        return new NameResults(rows, fact, anyInside);
    }

    public async Task<InsideResults> FindInsideAsync(
        string? phrase,
        DateTimeOffset nowUtc,
        IReadOnlySet<string> alreadyListed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alreadyListed);
        var outcome = await _inside.SearchAsync(phrase, nowUtc, selectedRootId: null, cancellationToken).ConfigureAwait(false);
        if (!outcome.WasSearched)
        {
            return InsideResults.NotSearched;
        }

        var rows = outcome.Hits
            .Select(hit => new QuickSearchRow(hit.RootId, hit.RelativePath, hit.Name, WhereText(hit.RootName, hit.RelativePath),
                FileCategory.Other, FileOpenRule.For(hit.Name))
            {
                Snippet = hit.Snippet,
                Section = hit.Section,
            })
            .Where(row => !alreadyListed.Contains(row.Key))
            .Take(MaxRows)
            .ToArray();
        return new InsideResults(rows, true, outcome.FilesRead, outcome.ReachedLimit);
    }

    /// <summary>"Documents › School": the folder's name and the folders inside it, never a full path.</summary>
    public static string WhereText(string rootName, string relativePath)
    {
        var folder = Path.GetDirectoryName(relativePath);
        var parts = string.IsNullOrEmpty(folder) ? [] : folder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" › ", new[] { rootName }.Concat(parts));
    }
}
```

Check `FileCategory` has an `Other` member (`grep -n "enum FileCategory" -A12 src/DeskAI.Core`); if its "unknown" member has another name, use that name. `FileSearchService.IsSearchable` is `public static` (see its line 65).

- [ ] **Step 5: Register it** in `DeskAiApplicationServices`, after `ContentSearchService`:

```csharp
        // Quick search (ADR 0047): the two read-only searches above, and nothing that can open,
        // change, or send a file. A test asserts it.
        services.AddSingleton<QuickSearchService>();
```

- [ ] **Step 6: Run the tests** (the new ones plus `SearchPageTests` to show Search is unchanged).

Run: `dotnet test tests/DeskAI.Presentation.Tests -c Release --filter "FullyQualifiedName~QuickSearchServiceTests|FullyQualifiedName~SearchPageTests"`
Expected: all pass.

- [ ] **Step 7: Commit.**

```powershell
git add src/DeskAI.Core src/DeskAI.Presentation/Composition tests/DeskAI.Presentation.Tests/QuickSearchServiceTests.cs
git commit -m "Let quick search find files by name, then by the words inside, with Search's own permissions"
```

---

### Task 4: Infrastructure — `WindowsFileLauncher` and the process seam

**Files:**
- Create: `src/DeskAI.Core/QuickSearch/IFileLauncher.cs`
- Create: `src/DeskAI.Infrastructure/Launching/IShellStarter.cs` (interface + `NoShellStarter`)
- Create: `src/DeskAI.Infrastructure/Launching/WindowsShellStarter.cs`
- Create: `src/DeskAI.Infrastructure/Launching/WindowsFileLauncher.cs`
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs` (register `IShellStarter → NoShellStarter`, `IFileLauncher → WindowsFileLauncher`)
- Modify: `src/DeskAI.App/App.xaml.cs` (replace `IShellStarter` with `WindowsShellStarter`, same remove-then-add pattern as `IBackgroundPresence`)
- Modify: `tests/DeskAI.Presentation.Tests/TestApp.cs` (+ `RecordingShellStarter` in `TestDoubles.cs`)
- Test: `tests/DeskAI.Infrastructure.Tests/WindowsFileLauncherTests.cs`

**Interfaces:**
- Produces:
  - Core: `sealed record LaunchResult(bool Done, string Reason) { static LaunchResult Success; static LaunchResult Refused(string reason); }`; `interface IFileLauncher { Task<LaunchResult> OpenAsync(Guid rootId, string relativePath, CancellationToken = default); Task<LaunchResult> ShowInFolderAsync(Guid rootId, string relativePath, CancellationToken = default); }`
  - Infrastructure (`DeskAI.Infrastructure.Launching`): `interface IShellStarter { void OpenWithUsualApp(string fullPath); void ShowInFolder(string fullPath); }`, `NoShellStarter`, `WindowsShellStarter`, `WindowsFileLauncher(IAuthorizedRootRepository, IPathPolicy, IShellStarter)`.
  - Tests: `RecordingShellStarter` with `List<string> Opened`, `List<string> Shown`, `bool Fail`; `TestApp.Shell`.

- [ ] **Step 1: Write the contract** (`src/DeskAI.Core/QuickSearch/IFileLauncher.cs`):

```csharp
namespace DeskAI.Core.QuickSearch;

/// <summary>What happened when quick search was asked to open or show a file.</summary>
public sealed record LaunchResult(bool Done, string Reason)
{
    public static LaunchResult Success { get; } = new(true, string.Empty);

    public static LaunchResult Refused(string reason) => new(false, reason);
}

/// <summary>
/// Opens one file from a connected folder in its usual app, or shows it in File Explorer.
/// </summary>
/// <remarks>
/// DeskAI's only "open a file" action (ADR 0047). The implementation re-checks the live file
/// before starting anything. Only <c>QuickSearchViewModel</c> holds this; no AI type does, and a
/// test asserts both.
/// </remarks>
public interface IFileLauncher
{
    Task<LaunchResult> OpenAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);

    Task<LaunchResult> ShowInFolderAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing Infrastructure tests.** Look at an existing test in `tests/DeskAI.Infrastructure.Tests` that makes a junction or symlink (`grep -rln "CreateSymbolicLink\|junction\|mklink" tests/DeskAI.Infrastructure.Tests`) and reuse its helper and its skip rule for machines that cannot make links. Use `TemporaryDirectory` from that project.

```csharp
using DeskAI.Core.Abstractions;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Launching;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Tests;

/// <summary>
/// The launcher re-checks the live file and starts exactly one validated path, or nothing.
/// No real process is started: the shell starter records.
/// </summary>
public sealed class WindowsFileLauncherTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new();
    private readonly RecordingStarter _shell = new();
    private readonly FakeRoots _roots = new();
    private readonly AuthorizedRoot _root;

    public WindowsFileLauncherTests()
    {
        var folder = _temp.CreateDummyDirectory("School");
        _root = AuthorizedRoot.Create(Guid.NewGuid(), folder, "School", RootAccessLevel.ReadMetadata, RootAuthorizationScope.UserFolder);
        _roots.Roots.Add(_root);
    }

    private WindowsFileLauncher Launcher(params string[] protectedPaths) =>
        new(_roots, new WindowsPathPolicy(protectedPaths), _shell);

    [Fact]
    public async Task A_familiar_file_is_opened_by_its_full_path()
    {
        var path = _temp.CreateDummyFile(Path.Combine("School", "essay.pdf"), "x");

        var result = await Launcher().OpenAsync(_root.Id, "essay.pdf", TestContext.Current.CancellationToken);

        Assert.True(result.Done);
        Assert.Equal([path], _shell.Opened);
        Assert.Empty(_shell.Shown);
    }

    [Fact]
    public async Task Show_in_folder_shows_any_kind_of_file()
    {
        var path = _temp.CreateDummyFile(Path.Combine("School", "setup.exe"), "x");

        Assert.True((await Launcher().ShowInFolderAsync(_root.Id, "setup.exe", TestContext.Current.CancellationToken)).Done);
        Assert.Equal([path], _shell.Shown);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("invoice.pdf.exe")]
    [InlineData("notes.lnk")]
    public async Task A_program_is_never_opened(string name)
    {
        _temp.CreateDummyFile(Path.Combine("School", name), "x");

        var result = await Launcher().OpenAsync(_root.Id, name, TestContext.Current.CancellationToken);

        Assert.False(result.Done);
        Assert.Equal("DeskAI only opens familiar kinds of files. Use Show in folder.", result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Theory]
    [InlineData(@"..\outside.pdf")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("essay.pdf:hidden")]
    public async Task A_path_leading_out_of_the_folder_is_refused(string relative)
    {
        var result = await Launcher().OpenAsync(_root.Id, relative, TestContext.Current.CancellationToken);

        Assert.False(result.Done);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task A_file_that_is_gone_is_refused_with_a_plain_reason()
    {
        var result = await Launcher().OpenAsync(_root.Id, "gone.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("It is no longer there. Press Refresh on Search so DeskAI catches up.", result.Reason);
    }

    [Fact]
    public async Task A_folder_that_was_disconnected_is_refused()
    {
        _temp.CreateDummyFile(Path.Combine("School", "essay.pdf"), "x");
        _roots.Roots.Clear();

        var result = await Launcher().OpenAsync(_root.Id, "essay.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("That folder is no longer connected in DeskAI.", result.Reason);
    }

    [Fact]
    public async Task A_protected_place_is_refused()
    {
        var inside = _temp.CreateDummyDirectory(Path.Combine("School", "Private"));
        _temp.CreateDummyFile(Path.Combine("School", "Private", "essay.pdf"), "x");

        var result = await Launcher(inside).OpenAsync(_root.Id, @"Private\essay.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("This location is protected, so DeskAI did not open it.", result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task A_link_on_the_way_is_never_followed()
    {
        // Make "School\Linked" a junction to a folder outside School (reuse the project's helper;
        // skip the test the same way the other link tests do when links cannot be made).
        // Put essay.pdf in the target, then:
        var result = await Launcher().OpenAsync(_root.Id, @"Linked\essay.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("That name is a shortcut to somewhere else, so DeskAI did not follow it.", result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task When_Windows_refuses_it_says_so_plainly()
    {
        _temp.CreateDummyFile(Path.Combine("School", "essay.pdf"), "x");
        _shell.Fail = true;

        var result = await Launcher().OpenAsync(_root.Id, "essay.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("Windows couldn't open it.", result.Reason);
    }

    public void Dispose() => _temp.Dispose();

    private sealed class RecordingStarter : IShellStarter
    {
        public List<string> Opened { get; } = [];
        public List<string> Shown { get; } = [];
        public bool Fail { get; set; }

        public void OpenWithUsualApp(string fullPath)
        {
            if (Fail) throw new System.ComponentModel.Win32Exception();
            Opened.Add(fullPath);
        }

        public void ShowInFolder(string fullPath)
        {
            if (Fail) throw new System.ComponentModel.Win32Exception();
            Shown.Add(fullPath);
        }
    }

    // FakeRoots: an IAuthorizedRootRepository over a List<AuthorizedRoot> Roots, answering
    // FindAsync and ListAsync from it; every other member throws NotSupportedException.
}
```

Before writing `A_link_on_the_way_is_never_followed` in full, replace its comment with the real junction-making lines from the helper you found; the assertions stay as written.

- [ ] **Step 3: Run and see them fail.**

Run: `dotnet test tests/DeskAI.Infrastructure.Tests -c Release --filter FullyQualifiedName~WindowsFileLauncherTests`

- [ ] **Step 4: Write the seam** (`IShellStarter.cs`, `WindowsShellStarter.cs`).

```csharp
namespace DeskAI.Infrastructure.Launching;

/// <summary>Starts the one thing quick search may start, for one already-validated full path.</summary>
/// <remarks>
/// A seam so that no test ever starts a real process. The shared registration holds
/// <see cref="NoShellStarter"/>; only the app registers <see cref="WindowsShellStarter"/>.
/// </remarks>
public interface IShellStarter
{
    void OpenWithUsualApp(string fullPath);

    void ShowInFolder(string fullPath);
}

/// <summary>Starts nothing. The default, so a DeskAI built without the app's Windows pieces can never start a process.</summary>
public sealed class NoShellStarter : IShellStarter
{
    public void OpenWithUsualApp(string fullPath) => throw new InvalidOperationException("Opening files is not available here.");

    public void ShowInFolder(string fullPath) => throw new InvalidOperationException("Opening files is not available here.");
}
```

```csharp
using System.Diagnostics;

namespace DeskAI.Infrastructure.Launching;

/// <summary>
/// The Windows shell's "open" action for a validated file, or File Explorer with it selected.
/// </summary>
/// <remarks>
/// Called only by <see cref="WindowsFileLauncher"/>, after its checks. Explorer is started by its
/// full path under the Windows folder, never found through PATH.
/// </remarks>
public sealed class WindowsShellStarter : IShellStarter
{
    public void OpenWithUsualApp(string fullPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
            Verb = "open",
        });
    }

    public void ShowInFolder(string fullPath)
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = explorer,
            Arguments = $"/select,\"{fullPath}\"",
            UseShellExecute = false,
        });
    }
}
```

- [ ] **Step 5: Write `WindowsFileLauncher.cs`.**

```csharp
using System.ComponentModel;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Plans;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Launching;

/// <summary>
/// Opens or shows one file from a connected folder, after checking the live file (ADR 0047).
/// </summary>
/// <remarks>
/// Every check runs on the file as it is now, not as the index remembered it, and the first
/// refusal wins: the folder is still connected; the path stays inside it after normalising; the
/// place is not protected; no folder or file on the way is a link or junction; the file exists;
/// and for Open, the live name is still a familiar kind. Nothing is started on any refusal.
/// </remarks>
public sealed class WindowsFileLauncher(IAuthorizedRootRepository roots, IPathPolicy pathPolicy, IShellStarter shell) : IFileLauncher
{
    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IPathPolicy _pathPolicy = pathPolicy;
    private readonly IShellStarter _shell = shell;

    public Task<LaunchResult> OpenAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) =>
        LaunchAsync(rootId, relativePath, open: true, cancellationToken);

    public Task<LaunchResult> ShowInFolderAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) =>
        LaunchAsync(rootId, relativePath, open: false, cancellationToken);

    private async Task<LaunchResult> LaunchAsync(Guid rootId, string relativePath, bool open, CancellationToken cancellationToken)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadMetadata(root))
        {
            return LaunchResult.Refused("That folder is no longer connected in DeskAI.");
        }

        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':', StringComparison.Ordinal))
        {
            return LaunchResult.Refused("That path leads outside the connected folder.");
        }

        if (_pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked
            || _pathPolicy.ValidateRelativePath(root, relativePath).Status == ValidationStatus.Blocked)
        {
            return LaunchResult.Refused("This location is protected, so DeskAI did not open it.");
        }

        var full = ResolveInsideRoot(root.CanonicalPath, relativePath);
        if (full is null)
        {
            return LaunchResult.Refused("That path leads outside the connected folder.");
        }

        try
        {
            if (CrossesLink(root.CanonicalPath, full))
            {
                return LaunchResult.Refused("That name is a shortcut to somewhere else, so DeskAI did not follow it.");
            }

            if (!File.Exists(full))
            {
                return LaunchResult.Refused("It is no longer there. Press Refresh on Search so DeskAI catches up.");
            }

            if (open && FileOpenRule.For(Path.GetFileName(full)) != OpenChoice.Open)
            {
                return LaunchResult.Refused("DeskAI only opens familiar kinds of files. Use Show in folder.");
            }

            if (open)
            {
                _shell.OpenWithUsualApp(full);
            }
            else
            {
                _shell.ShowInFolder(full);
            }

            return LaunchResult.Success;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            return LaunchResult.Refused("Windows couldn't open it.");
        }
    }

    /// <summary>Same containment rule as the text reader: both sides canonical, separator required after the root.</summary>
    private static string? ResolveInsideRoot(string rootPath, string relativePath)
    {
        try
        {
            var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var full = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
            return full.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>True when the folder itself or anything between it and the file is a link, junction, or reparse point.</summary>
    private static bool CrossesLink(string rootPath, string fullPath)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (IsReparse(current))
        {
            return true;
        }

        foreach (var segment in fullPath[(current.Length + 1)..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparse(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReparse(string path)
    {
        var info = new FileInfo(path);
        return (info.Exists || Directory.Exists(path))
            && (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null);
    }
}
```

Check the `ValidationStatus` namespace (`grep -rn "enum ValidationStatus" src`) and fix the `using` if it lives elsewhere than `DeskAI.Core.Plans`.

- [ ] **Step 6: Register.** In `DeskAiApplicationServices`, after `QuickSearchService`:

```csharp
        // DeskAI's only "open a file" action (ADR 0047). It starts nothing here: the shared
        // registration's starter refuses, and only the app registers the real one.
        services.AddSingleton<IShellStarter, NoShellStarter>();
        services.AddSingleton<IFileLauncher, WindowsFileLauncher>();
```

In `App.xaml.cs`, after the `IAppearanceApplier` replacement block, the same pattern:

```csharp
                // The real "open" and "show in folder" (ADR 0047) replace the ones that start nothing.
                foreach (var existing in services
                    .Where(descriptor => descriptor.ServiceType == typeof(IShellStarter))
                    .ToArray())
                {
                    services.Remove(existing);
                }

                services.AddSingleton<IShellStarter, WindowsShellStarter>();
```

In `TestDoubles.cs` add `RecordingShellStarter` (public-in-assembly copy of the test's `RecordingStarter`), in `TestApp.StartAsync` add `Replace<IShellStarter>(services, new RecordingShellStarter());` and the property `public RecordingShellStarter Shell => (RecordingShellStarter)_services.GetRequiredService<IShellStarter>();`.

- [ ] **Step 7: Run the tests and the full Presentation suite** (the registration must still validate on build).

Run: `dotnet test DeskAI.sln -c Release --filter "FullyQualifiedName~WindowsFileLauncherTests|FullyQualifiedName~DeskAI.Presentation.Tests"`
Expected: all pass.

- [ ] **Step 8: Commit.**

```powershell
git add src tests
git commit -m "Open or show a file from a connected folder only after checking the live file"
```

---

### Task 5: Presentation — `QuickSearchViewModel` (the bar, as a person uses it)

**Files:**
- Create: `src/DeskAI.Presentation/ViewModels/QuickSearchViewModel.cs`
- Create: `src/DeskAI.Presentation/ViewModels/QuickSearchTiming.cs`
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs`
- Modify: `tests/DeskAI.Presentation.Tests/TestApp.cs` (short timings)
- Test: `tests/DeskAI.Presentation.Tests/QuickSearchPageTests.cs`
- Test: `tests/DeskAI.Presentation.Tests/QuickSearchContainmentTests.cs`

**Interfaces:**
- Consumes: Task 2 (`SearchBuddyLines`, `QuickSearchSettingsService`), Task 3 (`QuickSearchService`, rows, facts), Task 4 (`IFileLauncher`, `LaunchResult`), `SearchRequest.AskPhrase(string)`, `IClock.UtcNow`.
- Produces (namespace `DeskAI.App.ViewModels`, as the other view models):
  - `sealed record QuickSearchTiming(TimeSpan NameDelay, TimeSpan InsideDelay, TimeSpan HappyFor) { static QuickSearchTiming Default = new(150 ms, 600 ms, 1.5 s); }`
  - `sealed class QuickSearchRowViewModel : ObservableObject` — `QuickSearchRow Row`, `string Name`, `string Where`, `string Snippet`, `bool HasSnippet`, `string ActionText` ("Open" / "Show in folder"), `string Glyph`, `bool IsSelected`.
  - `sealed class QuickSearchViewModel : ObservableObject` — `string Phrase` (setting it schedules the two looks), `Task Pending`, `ObservableCollection<QuickSearchRowViewModel> NameRows`, `InsideRows`, `QuickSearchRowViewModel? Selected`, `SearchBuddy Buddy`, `BuddyMood Mood`, `string BuddyLine`, `string NameFact`, `string InsideFact`, `bool HasNameFact`, `bool HasInsideFact`, `bool IsLookingInside`, `bool ShowsInsideGroup`, `bool ShowsSeeMore`, `bool ShowsOpenDeskAi`, `bool ShowsExamples`, `static IReadOnlyList<string> Examples`, `string OpenMessage`, `bool HasOpenMessage`; methods `Task ShowAsync()`, `void Hide()`, `Task ChooseExampleAsync(string)`, `void MoveSelection(int delta)`, `Task<bool> ActivateAsync(QuickSearchRowViewModel? row, bool showInFolder)` (true = hide the bar), `Task PetBuddyAsync()`, `void SeeMoreInDeskAi()`, `void OpenDeskAi()`; event `EventHandler<string>? OpenDeskAiRequested` (argument = route: `"search"` or `"dashboard"`).

- [ ] **Step 1: Write the failing page tests.** Each test uses the bar the way a person does. Helpers at the bottom connect a generated folder through `SearchViewModel` (as in Task 3).

```csharp
using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The quick search bar as a person uses it: press the shortcut, read the buddy, type, move with
/// the arrows, press Enter. Generated folders only; opening is recorded, never real.
/// </summary>
public sealed class QuickSearchPageTests
{
    [Fact]
    public async Task The_empty_bar_shows_the_buddys_greeting_and_three_examples()
    {
        await using var app = await TestApp.StartAsync();
        var bar = app.Get<QuickSearchViewModel>();

        await bar.ShowAsync();

        Assert.Equal(SearchBuddy.Sparky, bar.Buddy);
        Assert.Equal(BuddyMood.Idle, bar.Mood);
        Assert.Equal("Hi! What are we looking for?", bar.BuddyLine);
        Assert.True(bar.ShowsExamples);
        Assert.Equal(["pdf from last week", "photos from this month", "big videos"], QuickSearchViewModel.Examples);
    }

    [Fact]
    public async Task Clicking_an_example_fills_the_box_and_searches()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Music", "long song.mp4");
        var bar = await OpenBarAsync(app);

        await bar.ChooseExampleAsync("big videos");

        Assert.Equal("big videos", bar.Phrase);
        Assert.False(bar.ShowsExamples);
    }

    [Fact]
    public async Task Typing_finds_a_file_by_name_and_the_buddy_says_so()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf", "notes.txt");
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "essay");

        var row = Assert.Single(bar.NameRows);
        Assert.Equal("essay.pdf", row.Name);
        Assert.Equal("School", row.Where);
        Assert.Equal("Open", row.ActionText);
        Assert.Same(row, bar.Selected);
        Assert.Equal(BuddyMood.Found, bar.Mood);
        Assert.Equal("Found 1!", bar.BuddyLine);
    }

    [Fact]
    public async Task More_than_five_says_only_the_first_five_are_shown()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay 1.pdf", "essay 2.pdf", "essay 3.pdf", "essay 4.pdf", "essay 5.pdf", "essay 6.pdf");
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "essay");

        Assert.Equal(5, bar.NameRows.Count);
        Assert.Equal("Showing the first 5 by name.", bar.NameFact);
        Assert.True(bar.ShowsSeeMore);
    }

    [Fact]
    public async Task No_folder_connected_says_to_connect_one_and_offers_Open_DeskAI()
    {
        await using var app = await TestApp.StartAsync();
        var bar = await OpenBarAsync(app);
        string? route = null;
        bar.OpenDeskAiRequested += (_, asked) => route = asked;

        await TypeAsync(bar, "essay");

        Assert.Equal("Connect a folder in DeskAI first. Quick search looks only in folders you connected.", bar.NameFact);
        Assert.True(bar.ShowsOpenDeskAi);
        bar.OpenDeskAi();
        Assert.Equal("dashboard", route);
    }

    [Fact]
    public async Task Nothing_matched_says_so_and_the_buddy_is_sorry()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "zzqx");

        Assert.Empty(bar.NameRows);
        Assert.Equal("Nothing matched in your connected folders.", bar.NameFact);
        Assert.Equal(BuddyMood.Nothing, bar.Mood);
        Assert.Equal("Hmm, nothing yet.", bar.BuddyLine);
    }

    [Fact]
    public async Task Words_inside_a_file_appear_under_their_own_heading_with_a_piece_of_text()
    {
        await using var app = await TestApp.StartAsync();
        var search = await ConnectWithTextAsync(app, "Notes", ("shopping.txt", "buy bananas\nand bread"));
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "bananas");

        var row = Assert.Single(bar.InsideRows);
        Assert.Equal("shopping.txt", row.Name);
        Assert.Contains("bananas", row.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', row.Snippet);
        Assert.False(bar.IsLookingInside);

        Assert.True(await bar.ActivateAsync(row, showInFolder: false));
        Assert.EndsWith("shopping.txt", Assert.Single(app.Shell.Opened), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_reading_inside_the_bar_says_where_to_allow_it_and_reads_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Notes", "shopping.txt");
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "bananas");

        Assert.Empty(bar.InsideRows);
        Assert.Equal("To find words inside files too, allow it for a folder on the Search page.", bar.InsideFact);
    }

    [Fact]
    public async Task A_file_already_listed_by_name_is_not_listed_again()
    {
        await using var app = await TestApp.StartAsync();
        var search = await ConnectWithTextAsync(app, "Notes", ("bananas.txt", "all about bananas"));
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "bananas");

        Assert.Single(bar.NameRows);
        Assert.Empty(bar.InsideRows);
        Assert.Equal("Nothing else found inside the 1 file DeskAI checked.", bar.InsideFact);
    }

    [Fact]
    public async Task Enter_on_a_program_shows_it_in_its_folder_instead()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "Stuff", "invoice.pdf.exe");
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "invoice");

        Assert.Equal("Show in folder", bar.Selected!.ActionText);
        Assert.True(await bar.ActivateAsync(bar.Selected, showInFolder: false));

        Assert.Empty(app.Shell.Opened);
        Assert.Single(app.Shell.Shown);
    }

    [Fact]
    public async Task Ctrl_Enter_always_shows_in_folder()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "essay");

        await bar.ActivateAsync(bar.Selected, showInFolder: true);

        Assert.Empty(app.Shell.Opened);
        Assert.Single(app.Shell.Shown);
    }

    [Fact]
    public async Task A_file_that_went_away_keeps_the_bar_open_with_the_reason()
    {
        await using var app = await TestApp.StartAsync();
        var folder = (await ConnectAsync(app, "School", "essay.pdf")).Folders[0];
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "essay");
        File.Delete(Path.Combine(app.Sandbox, "School", "essay.pdf"));

        Assert.False(await bar.ActivateAsync(bar.Selected, showInFolder: false));

        Assert.Equal("DeskAI couldn't open it: It is no longer there. Press Refresh on Search so DeskAI catches up.", bar.OpenMessage);
        Assert.Empty(app.Shell.Opened);
    }

    [Fact]
    public async Task Up_and_Down_move_across_both_groups()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay 1.pdf", "essay 2.pdf");
        var bar = await OpenBarAsync(app);
        await TypeAsync(bar, "essay");

        bar.MoveSelection(+1);
        Assert.Same(bar.NameRows[1], bar.Selected);
        bar.MoveSelection(+1);
        Assert.Same(bar.NameRows[1], bar.Selected);
        bar.MoveSelection(-5);
        Assert.Same(bar.NameRows[0], bar.Selected);
        Assert.True(bar.NameRows[0].IsSelected);
        Assert.False(bar.NameRows[1].IsSelected);
    }

    [Fact]
    public async Task Each_buddy_speaks_in_its_own_voice()
    {
        foreach (var buddy in Enum.GetValues<SearchBuddy>())
        {
            await using var app = await TestApp.StartAsync();
            await app.Get<QuickSearchSettingsService>().SetBuddyAsync(buddy);
            await ConnectAsync(app, "School", "essay.pdf");
            var bar = await OpenBarAsync(app);

            Assert.Equal(SearchBuddyLines.Line(buddy, BuddyMood.Idle), bar.BuddyLine);
            await TypeAsync(bar, "essay");
            Assert.Equal(SearchBuddyLines.Line(buddy, BuddyMood.Found, 1), bar.BuddyLine);
            await TypeAsync(bar, "zzqx");
            Assert.Equal(SearchBuddyLines.Line(buddy, BuddyMood.Nothing), bar.BuddyLine);
        }
    }

    [Fact]
    public async Task Clicking_the_buddy_makes_it_happy_and_then_it_goes_back()
    {
        await using var app = await TestApp.StartAsync();
        var bar = await OpenBarAsync(app);

        var petting = bar.PetBuddyAsync();
        Assert.Equal(BuddyMood.Happy, bar.Mood);
        Assert.Equal("Wheee!", bar.BuddyLine);
        await petting;

        Assert.Equal(BuddyMood.Idle, bar.Mood);
    }

    [Fact]
    public async Task Nothing_typed_or_found_is_remembered()
    {
        await using var app = await TestApp.StartAsync();
        await ConnectAsync(app, "School", "essay.pdf");
        var store = app.Get<DeskAI.Core.Abstractions.IAppSettingsStore>();
        var bar = await OpenBarAsync(app);

        await TypeAsync(bar, "essay");
        await bar.ActivateAsync(bar.Selected, showInFolder: false);
        bar.Hide();

        foreach (var key in new[] { "quicksearch.history", "quicksearch.recent", "quicksearch.last" })
        {
            Assert.Null(await store.ReadAsync(key));
        }

        var reopened = await OpenBarAsync(app);
        Assert.Equal(string.Empty, reopened.Phrase);
        Assert.Empty(reopened.NameRows);
    }

    [Fact]
    public async Task See_more_opens_Search_with_the_same_words()
    {
        await using var app = await TestApp.StartAsync();
        var bar = await OpenBarAsync(app);
        string? route = null;
        bar.OpenDeskAiRequested += (_, asked) => route = asked;
        await TypeAsync(bar, "essay");

        bar.SeeMoreInDeskAi();

        Assert.Equal("search", route);
        Assert.Equal("essay", app.Get<SearchRequest>().TakePhrase());
    }

    private static async Task TypeAsync(QuickSearchViewModel bar, string words)
    {
        bar.Phrase = words;
        await bar.Pending;
    }

    private static async Task<QuickSearchViewModel> OpenBarAsync(TestApp app)
    {
        var bar = app.Get<QuickSearchViewModel>();
        await bar.ShowAsync();
        return bar;
    }

    private static async Task<SearchViewModel> ConnectAsync(TestApp app, string name, params string[] files)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(app.MakeFolder(name, files));
        return search;
    }

    /// <summary>Makes text files with the given words before connecting, so no Refresh is needed.</summary>
    private static async Task<SearchViewModel> ConnectWithTextAsync(TestApp app, string name, params (string File, string Text)[] files)
    {
        var folder = app.MakeFolder(name);
        foreach (var (file, text) in files)
        {
            app.Directory.CreateDummyFile(Path.Combine("folders", name, file), text);
        }

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        return search;
    }
}
```

Also add these two tests with a slow reader, built by hand so the reading can be held open (put them in the same file):

```csharp
    [Fact]
    public async Task Typing_again_stops_a_slow_inside_look_and_its_late_rows_never_appear()
    {
        await using var app = await TestApp.StartAsync();
        var search = await ConnectAsync(app, "Notes", "a.txt");
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var reader = new BlockingReader();
        var bar = BarWith(app, reader);
        await bar.ShowAsync();

        bar.Phrase = "bananas";
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(bar.IsLookingInside);
        Assert.Equal(BuddyMood.Thinking, bar.Mood);

        bar.Phrase = "zzqx";
        await bar.Pending;

        Assert.True(reader.WasCancelled);
        Assert.Empty(bar.InsideRows);
    }

    [Fact]
    public async Task Hiding_the_bar_stops_an_inside_look()
    {
        await using var app = await TestApp.StartAsync();
        var search = await ConnectAsync(app, "Notes", "a.txt");
        await search.SetContentPermissionAsync(Assert.Single(search.Folders).Id, allow: true);
        var reader = new BlockingReader();
        var bar = BarWith(app, reader);
        await bar.ShowAsync();
        bar.Phrase = "bananas";
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        bar.Hide();
        await bar.Pending;

        Assert.True(reader.WasCancelled);
    }

    private static QuickSearchViewModel BarWith(TestApp app, IContentTextExtractor reader)
    {
        var roots = app.Get<IAuthorizedRootRepository>();
        var index = app.Get<IFileIndex>();
        var service = new QuickSearchService(app.Get<FileSearchService>(), new ContentSearchService(roots, index, reader), roots);
        return new QuickSearchViewModel(service, app.Get<QuickSearchSettingsService>(), app.Get<IFileLauncher>(),
            app.Get<SearchRequest>(), app.Get<IClock>(), app.Get<QuickSearchTiming>());
    }

    /// <summary>
    /// The first read never finishes until it is cancelled, like a big PDF on a slow disk. Later
    /// reads answer at once with "could not read", so the newer words' look can finish.
    /// </summary>
    private sealed class BlockingReader : IContentTextExtractor
    {
        private int _calls;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }

        public async Task<TextExtraction> ExtractAsync(AuthorizedRoot root, string relativePath, TextExtractionOptions options, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                return TextExtraction.Refused(relativePath, TextExtractionStatus.Unavailable, "Generated test refusal.");
            }

            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }

            throw new InvalidOperationException("unreachable");
        }
    }
```
(usings for these two: `DeskAI.Core.Abstractions`, `DeskAI.Core.Content`, `DeskAI.Core.Roots`, `DeskAI.Core.Search`; check the exact `IContentTextExtractor.ExtractAsync` signature in `src/DeskAI.Core/Content`.)

And `QuickSearchContainmentTests.cs`:

```csharp
using System.Reflection;
using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Opening files stays in the one place built for it: only the quick search bar holds the
/// launcher, nothing from the AI side does, and the bar holds nothing that changes a permission.
/// </summary>
public sealed class QuickSearchContainmentTests
{
    private static readonly Assembly[] DeskAi =
    [
        typeof(IFileLauncher).Assembly,                                   // Core
        typeof(QuickSearchViewModel).Assembly,                            // Presentation
        typeof(DeskAI.AI.ConfiguredSuggestionProvider).Assembly,          // AI
        typeof(DeskAI.Infrastructure.Launching.WindowsFileLauncher).Assembly,
        typeof(DeskAI.Safety.PlanValidator).Assembly,
    ];

    [Fact]
    public void Only_the_quick_search_bar_holds_the_launcher()
    {
        var holders = DeskAi
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IFileLauncher))))
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal([nameof(QuickSearchViewModel)], holders);
    }

    [Fact]
    public void The_bar_holds_nothing_that_changes_a_permission_or_talks_to_AI()
    {
        var parameters = typeof(QuickSearchViewModel).GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.DoesNotContain(parameters, type =>
            type.Name.Contains("ConnectedFolder", StringComparison.Ordinal) || type.Name.Contains("Permission", StringComparison.Ordinal)
            || type.Name.Contains("Provider", StringComparison.Ordinal) || type.Name.Contains("Ai", StringComparison.Ordinal)
            || type.Name.Contains("Executor", StringComparison.Ordinal) || type.Name.Contains("Repository", StringComparison.Ordinal));
    }
}
```
(Check the exact AI assembly type name with `grep -rn "class ConfiguredSuggestionProvider" src`.)

- [ ] **Step 2: Run and see them fail.**

Run: `dotnet test tests/DeskAI.Presentation.Tests -c Release --filter "FullyQualifiedName~QuickSearchPageTests|FullyQualifiedName~QuickSearchContainmentTests"`

- [ ] **Step 3: Write `QuickSearchTiming.cs`.**

```csharp
namespace DeskAI.App.ViewModels;

/// <summary>How long quick search waits after a keystroke, and how long the buddy stays happy.</summary>
/// <remarks>A value, not constants, so page tests can use short waits and still test the order of things.</remarks>
public sealed record QuickSearchTiming(TimeSpan NameDelay, TimeSpan InsideDelay, TimeSpan HappyFor)
{
    public static QuickSearchTiming Default { get; } =
        new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(600), TimeSpan.FromSeconds(1.5));
}
```

- [ ] **Step 4: Write `QuickSearchViewModel.cs`.**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Indexing;
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.ViewModels;

/// <summary>One row in the bar: a file, where it is, and what Enter does with it.</summary>
public sealed class QuickSearchRowViewModel(QuickSearchRow row) : ObservableObject
{
    /// <summary>A Words inside snippet is cut to one short line; the full text stays on Search.</summary>
    private const int MaxSnippet = 110;

    private bool _isSelected;

    public QuickSearchRow Row { get; } = row;

    public string Name => Row.Name;

    public string Where => Row.Section is { Length: > 0 } section ? $"{Row.Where} · {section}" : Row.Where;

    public string Snippet { get; } = OneLine(row.Snippet);

    public bool HasSnippet => Snippet.Length > 0;

    public string ActionText => Row.Choice == OpenChoice.Open ? "Open" : "Show in folder";

    public string Glyph => Row.Category switch
    {
        FileCategory.Images => "\uEB9F",
        FileCategory.Videos => "\uE714",
        FileCategory.Audio => "\uE8D6",
        _ => "\uE8A5",
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static string OneLine(string text)
    {
        var flat = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length <= MaxSnippet ? flat : flat[..(MaxSnippet - 1)].TrimEnd() + "…";
    }
}

/// <summary>
/// The quick search bar: the buddy, the box, two short groups of results, and Enter.
/// </summary>
/// <remarks>
/// <para>
/// Each keystroke cancels whatever the last one started, then waits a moment and looks up names,
/// then waits a little more and looks inside files. Results from an old keystroke are never shown
/// under new words, because every look checks its own cancellation before touching the rows.
/// </para>
/// <para>
/// Stores nothing typed, found, or read. It holds the launcher (ADR 0047), the two read-only
/// searches, the buddy choice, and the Search request; nothing that changes a permission, talks
/// to AI, or changes a file. Tests assert it.
/// </para>
/// </remarks>
public sealed class QuickSearchViewModel : ObservableObject
{
    private readonly QuickSearchService _search;
    private readonly QuickSearchSettingsService _settings;
    private readonly IFileLauncher _launcher;
    private readonly SearchRequest _request;
    private readonly IClock _clock;
    private readonly QuickSearchTiming _timing;
    private CancellationTokenSource _typing = new();
    private string _phrase = string.Empty;
    private QuickSearchRowViewModel? _selected;
    private SearchBuddy _buddy = SearchBuddy.Sparky;
    private BuddyMood _mood = BuddyMood.Idle;
    private BuddyMood _moodBeforePetting = BuddyMood.Idle;
    private int _found;
    private string _nameFact = string.Empty;
    private string _insideFact = string.Empty;
    private string _openMessage = string.Empty;
    private bool _isLookingInside;
    private bool _showsSeeMore;
    private bool _showsOpenDeskAi;
    private bool _showsInsideGroup;

    public QuickSearchViewModel(
        QuickSearchService search,
        QuickSearchSettingsService settings,
        IFileLauncher launcher,
        SearchRequest request,
        IClock clock,
        QuickSearchTiming timing)
    {
        _search = search;
        _settings = settings;
        _launcher = launcher;
        _request = request;
        _clock = clock;
        _timing = timing;
    }

    /// <summary>Asks the window to open DeskAI on a page: "search" or "dashboard".</summary>
    public event EventHandler<string>? OpenDeskAiRequested;

    public static IReadOnlyList<string> Examples { get; } = ["pdf from last week", "photos from this month", "big videos"];

    public ObservableCollection<QuickSearchRowViewModel> NameRows { get; } = [];

    public ObservableCollection<QuickSearchRowViewModel> InsideRows { get; } = [];

    /// <summary>The latest look started by typing. Tests await it; the window never needs to.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    public string Phrase
    {
        get => _phrase;
        set
        {
            if (SetProperty(ref _phrase, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ShowsExamples));
                Pending = LookAsync(Restart());
            }
        }
    }

    public bool ShowsExamples => string.IsNullOrWhiteSpace(_phrase);

    public QuickSearchRowViewModel? Selected
    {
        get => _selected;
        private set
        {
            if (_selected is not null) _selected.IsSelected = false;
            SetProperty(ref _selected, value);
            if (_selected is not null) _selected.IsSelected = true;
        }
    }

    public SearchBuddy Buddy { get => _buddy; private set => SetProperty(ref _buddy, value); }

    public BuddyMood Mood
    {
        get => _mood;
        private set
        {
            if (SetProperty(ref _mood, value))
            {
                OnPropertyChanged(nameof(BuddyLine));
            }
        }
    }

    public string BuddyLine => SearchBuddyLines.Line(_buddy, _mood, _found);

    public string NameFact { get => _nameFact; private set { if (SetProperty(ref _nameFact, value)) OnPropertyChanged(nameof(HasNameFact)); } }

    public bool HasNameFact => _nameFact.Length > 0;

    public string InsideFact { get => _insideFact; private set { if (SetProperty(ref _insideFact, value)) OnPropertyChanged(nameof(HasInsideFact)); } }

    public bool HasInsideFact => _insideFact.Length > 0;

    public string OpenMessage { get => _openMessage; private set { if (SetProperty(ref _openMessage, value)) OnPropertyChanged(nameof(HasOpenMessage)); } }

    public bool HasOpenMessage => _openMessage.Length > 0;

    public bool IsLookingInside { get => _isLookingInside; private set => SetProperty(ref _isLookingInside, value); }

    /// <summary>Whether the Words inside heading shows: while looking, or when it found something.</summary>
    public bool ShowsInsideGroup { get => _showsInsideGroup; private set => SetProperty(ref _showsInsideGroup, value); }

    public bool ShowsSeeMore { get => _showsSeeMore; private set => SetProperty(ref _showsSeeMore, value); }

    public bool ShowsOpenDeskAi { get => _showsOpenDeskAi; private set => SetProperty(ref _showsOpenDeskAi, value); }

    /// <summary>The shortcut was pressed: a clean bar with the chosen buddy greeting.</summary>
    public async Task ShowAsync()
    {
        Buddy = (await _settings.LoadAsync().ConfigureAwait(true)).Buddy;
        Clear();
        _phrase = string.Empty;
        OnPropertyChanged(nameof(Phrase));
        OnPropertyChanged(nameof(ShowsExamples));
        Mood = BuddyMood.Idle;
        OnPropertyChanged(nameof(BuddyLine));
    }

    /// <summary>Esc, a click outside, or opening a file: stops any look and forgets the words.</summary>
    public void Hide()
    {
        _typing.Cancel();
        Clear();
    }

    public Task ChooseExampleAsync(string example)
    {
        Phrase = example;
        return Pending;
    }

    public void MoveSelection(int delta)
    {
        var all = NameRows.Concat(InsideRows).ToArray();
        if (all.Length == 0)
        {
            return;
        }

        var index = _selected is null ? 0 : Array.IndexOf(all, _selected) + delta;
        Selected = all[Math.Clamp(index, 0, all.Length - 1)];
    }

    /// <returns>True when the bar should hide (the file was opened or shown).</returns>
    public async Task<bool> ActivateAsync(QuickSearchRowViewModel? row, bool showInFolder)
    {
        if (row is null)
        {
            return false;
        }

        OpenMessage = string.Empty;
        var result = showInFolder || row.Row.Choice != OpenChoice.Open
            ? await _launcher.ShowInFolderAsync(row.Row.RootId, row.Row.RelativePath).ConfigureAwait(true)
            : await _launcher.OpenAsync(row.Row.RootId, row.Row.RelativePath).ConfigureAwait(true);
        if (!result.Done)
        {
            OpenMessage = $"DeskAI couldn't open it: {result.Reason}";
        }

        return result.Done;
    }

    /// <summary>Clicking the buddy: a happy moment, then back to how it was. Does nothing else.</summary>
    public async Task PetBuddyAsync()
    {
        if (_mood != BuddyMood.Happy)
        {
            _moodBeforePetting = _mood;
        }

        Mood = BuddyMood.Happy;
        await Task.Delay(_timing.HappyFor).ConfigureAwait(true);
        if (_mood == BuddyMood.Happy)
        {
            Mood = _moodBeforePetting;
        }
    }

    public void SeeMoreInDeskAi()
    {
        if (!string.IsNullOrWhiteSpace(_phrase))
        {
            _request.AskPhrase(_phrase.Trim());
        }

        OpenDeskAiRequested?.Invoke(this, "search");
    }

    public void OpenDeskAi() => OpenDeskAiRequested?.Invoke(this, "dashboard");

    private CancellationToken Restart()
    {
        _typing.Cancel();
        _typing.Dispose();
        _typing = new CancellationTokenSource();
        return _typing.Token;
    }

    private async Task LookAsync(CancellationToken token)
    {
        // Runs synchronously up to the first await, so an old "Looking inside files…" never
        // outlives the keystroke that replaced it.
        IsLookingInside = false;
        try
        {
            if (string.IsNullOrWhiteSpace(_phrase))
            {
                Clear();
                Mood = BuddyMood.Idle;
                return;
            }

            await Task.Delay(_timing.NameDelay, token).ConfigureAwait(true);
            Mood = BuddyMood.Thinking;
            var words = _phrase;
            var byName = await _search.FindByNameAsync(words, _clock.UtcNow, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            ShowNames(byName);

            if (!byName.AnyFolderReadsInside || byName.Fact is NameFact.NoFolders)
            {
                InsideFact = byName.Fact is NameFact.NoFolders ? string.Empty
                    : "To find words inside files too, allow it for a folder on the Search page.";
                Settle();
                return;
            }

            await Task.Delay(_timing.InsideDelay - _timing.NameDelay, token).ConfigureAwait(true);
            IsLookingInside = true;
            ShowsInsideGroup = true;
            InsideFact = "Looking inside files…";
            var inside = await _search.FindInsideAsync(words, _clock.UtcNow,
                byName.Rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal), token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            ShowInside(inside, byName.Rows.Count);
            Settle();
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke or Hide took over; it owns the rows now.
        }
    }

    private void ShowNames(NameResults results)
    {
        NameRows.Clear();
        InsideRows.Clear();
        foreach (var row in results.Rows)
        {
            NameRows.Add(new QuickSearchRowViewModel(row));
        }

        Selected = NameRows.FirstOrDefault();
        OpenMessage = string.Empty;
        ShowsOpenDeskAi = results.Fact is NameFact.NoFolders;
        ShowsSeeMore = results.Fact is NameFact.MoreThanShown;
        ShowsInsideGroup = false;
        NameFact = results.Fact switch
        {
            NameFact.NoFolders => "Connect a folder in DeskAI first. Quick search looks only in folders you connected.",
            NameFact.NotUnderstood => "Try a name, a kind like \"pdf\", or a time like \"last week\".",
            NameFact.MoreThanShown => $"Showing the first {QuickSearchService.MaxRows} by name.",
            _ => string.Empty,
        };
        _found = NameRows.Count;
    }

    private void ShowInside(InsideResults results, int nameCount)
    {
        IsLookingInside = false;
        if (!results.WasSearched)
        {
            ShowsInsideGroup = false;
            InsideFact = string.Empty;
            return;
        }

        foreach (var row in results.Rows)
        {
            InsideRows.Add(new QuickSearchRowViewModel(row));
        }

        Selected ??= InsideRows.FirstOrDefault();
        ShowsInsideGroup = InsideRows.Count > 0;
        var read = results.FilesRead == 1 ? "1 file" : $"{results.FilesRead} files";
        var what = nameCount > 0 ? "Nothing else found" : "Nothing found";
        InsideFact = (InsideRows.Count, results.ReachedLimit) switch
        {
            (0, true) => $"{what} in the first {read} DeskAI checked.",
            (0, false) => $"{what} inside the {read} DeskAI checked.",
            (_, true) => "There may be more.",
            _ => string.Empty,
        };
        ShowsSeeMore |= results.ReachedLimit;
        _found = NameRows.Count + InsideRows.Count;
    }

    /// <summary>The buddy's mood and the "nothing matched" line once both looks are done.</summary>
    private void Settle()
    {
        IsLookingInside = false;
        _found = NameRows.Count + InsideRows.Count;
        if (_found == 0 && NameFact.Length == 0)
        {
            NameFact = "Nothing matched in your connected folders.";
        }

        Mood = _found > 0 ? BuddyMood.Found : BuddyMood.Nothing;
        OnPropertyChanged(nameof(BuddyLine));
    }

    private void Clear()
    {
        NameRows.Clear();
        InsideRows.Clear();
        Selected = null;
        NameFact = string.Empty;
        InsideFact = string.Empty;
        OpenMessage = string.Empty;
        IsLookingInside = false;
        ShowsInsideGroup = false;
        ShowsSeeMore = false;
        ShowsOpenDeskAi = false;
        _found = 0;
    }
}
```

Adjust while making the tests pass, keeping the words: check `FileCategory` members for images, videos, and audio (`grep -n "enum FileCategory" -A12 src/DeskAI.Core`); if "Nothing matched" should not appear when names were empty but the "No folders" line is already shown, the `NameFact.Length == 0` guard covers it. When `NotUnderstood`, `Settle` keeps that line.

- [ ] **Step 5: Register.** In `DeskAiApplicationServices` after `DesktopStudioViewModel`:

```csharp
        services.AddSingleton(QuickSearchTiming.Default);
        // The quick search bar (ADR 0047): the one holder of the file launcher. One bar per DeskAI.
        services.AddSingleton<QuickSearchViewModel>();
```

In `TestApp.StartAsync`, next to the other `Replace` calls:

```csharp
        Replace(services, new QuickSearchTiming(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(20)));
```

Note: a singleton bar means `app.Get<QuickSearchViewModel>()` returns the same bar in a test; `ShowAsync` resets it, which is what `Nothing_typed_or_found_is_remembered` relies on.

- [ ] **Step 6: Run the tests until they pass**, then the whole Presentation suite.

Run: `dotnet test tests/DeskAI.Presentation.Tests -c Release`
Expected: all pass.

- [ ] **Step 7: Commit.**

```powershell
git add src/DeskAI.Presentation tests/DeskAI.Presentation.Tests
git commit -m "Add the quick search bar's behaviour: names, words inside, the buddy, and Enter"
```

---

### Task 6: Presentation — the shortcut switch, the icon near the clock, and closing

**Files:**
- Create: `src/DeskAI.Presentation/Services/IQuickSearchHotKey.cs` (contract, `HotKeyState`, `NoQuickSearchHotKey`)
- Create: `src/DeskAI.Presentation/Services/QuickSearchSwitch.cs`
- Create: `src/DeskAI.Core/QuickSearch/QuickSearchWords.cs` (tooltip and card wording)
- Create: `src/DeskAI.Presentation/ViewModels/QuickSearchCardViewModel.cs`
- Modify: `src/DeskAI.Presentation/Services/IBackgroundPresence.cs`, `NoBackgroundPresence.cs` (menu record, `FindRequested`)
- Modify: `src/DeskAI.Presentation/Services/BackgroundPresenceController.cs`
- Modify: `src/DeskAI.Core/Rules/BackgroundCheckingChoice.cs` (line ~156 wording)
- Modify: `src/DeskAI.Presentation/ViewModels/SettingsViewModel.cs` (Start fresh re-applies quick search)
- Modify: `src/DeskAI.Presentation/ViewModels/WorkspaceViewModel.cs` (exposes `QuickSearch` card)
- Modify: `src/DeskAI.Presentation/Help/HelpCatalog.cs` (topic `workspace.quicksearch`)
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs`
- Modify: `tests/DeskAI.Presentation.Tests/TestDoubles.cs` (`RecordingPresence` menu, `RecordingHotKey`), `TestApp.cs`
- Test: `tests/DeskAI.Presentation.Tests/QuickSearchSettingsPageTests.cs`
- Test: existing `BackgroundCheckingPageTests`, `AutomationPageTests` must still pass (update only assertions on the one changed sentence).

**Interfaces:**
- Consumes: Task 2 settings service.
- Produces:
  - `enum HotKeyState { Off, Listening, TakenByAnotherProgram, Unavailable }`; `interface IQuickSearchHotKey { HotKeyState State { get; } HotKeyState Listen(bool on); event EventHandler? Pressed; }`; `NoQuickSearchHotKey` (always `Unavailable`).
  - `sealed record PresenceMenu(bool OffersPause, bool IsPaused, bool OffersFind)`; `IBackgroundPresence.Show(string tooltip, PresenceMenu menu)`, `Update(string tooltip, PresenceMenu menu)`, `event EventHandler? FindRequested`.
  - `BackgroundPresenceController.SetQuickSearch(bool isOn)`; `KeepsRunningWhenClosed` = checking in background, or quick search on while the icon is showing; `bool QuickSearchKeepsItRunning`.
  - `QuickSearchSwitch(QuickSearchSettingsService, IQuickSearchHotKey, BackgroundPresenceController)` with `Task<HotKeyState> ApplyStoredAsync()`, `Task<HotKeyState> SetOnAsync(bool on)`.
  - `QuickSearchCardViewModel(QuickSearchSettingsService, QuickSearchSwitch, IQuickSearchHotKey)` with `bool IsOn`, `IReadOnlyList<BuddyTileViewModel> Buddies`, `string ShortcutProblem`, `bool HasShortcutProblem`, `static string WorksWhenLine`, `Task InitializeAsync()`, `Task SetOnAsync(bool)`, `Task ChooseBuddyAsync(SearchBuddy)`; `sealed class BuddyTileViewModel(SearchBuddy Buddy) : ObservableObject { string Name; bool IsChosen; }`.
  - `QuickSearchWords.Tooltip` = "DeskAI — press Ctrl + Alt + Space to find a file"; `QuickSearchWords.WithChecking(string checkingTooltip)` = `checkingTooltip + " · Ctrl + Alt + Space finds a file"`.

- [ ] **Step 1: Write the failing page tests** (`QuickSearchSettingsPageTests.cs`).

```csharp
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Rules;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Quick search card on My workspace, and what it means for the icon near the clock and for
/// closing the window. The shortcut and the icon are recorded, never real.
/// </summary>
public sealed class QuickSearchSettingsPageTests
{
    [Fact]
    public async Task The_card_starts_on_with_Sparky_chosen_among_seven()
    {
        await using var app = await TestApp.StartAsync();
        var card = await OpenCardAsync(app);

        Assert.True(card.IsOn);
        Assert.Equal(7, card.Buddies.Count);
        Assert.Equal("Sparky", Assert.Single(card.Buddies, tile => tile.IsChosen).Name);
        Assert.Equal("Works while DeskAI is open or near the clock. Closing the window keeps it near the clock; quit from the icon's menu there.", QuickSearchCardViewModel.WorksWhenLine);
    }

    [Fact]
    public async Task The_switch_stops_and_starts_listening_for_the_shortcut()
    {
        await using var app = await TestApp.StartAsync();
        var card = await OpenCardAsync(app);

        await card.SetOnAsync(false);
        Assert.False(app.HotKey.IsListening);
        Assert.False(card.IsOn);

        await card.SetOnAsync(true);
        Assert.True(app.HotKey.IsListening);
    }

    [Fact]
    public async Task The_chosen_buddy_is_kept_after_reopening()
    {
        await using var app = await TestApp.StartAsync();
        await (await OpenCardAsync(app)).ChooseBuddyAsync(SearchBuddy.Fetch);

        await using var reopened = await app.ReopenAsync();
        var card = await OpenCardAsync(reopened);

        Assert.Equal("Fetch the fox", Assert.Single(card.Buddies, tile => tile.IsChosen).Name);
    }

    [Fact]
    public async Task Another_program_using_the_shortcut_is_said_plainly()
    {
        await using var app = await TestApp.StartAsync();
        app.HotKey.RefuseNext = true;
        var card = await OpenCardAsync(app);

        await card.SetOnAsync(true);

        Assert.Equal("Another program already uses Ctrl + Alt + Space, so quick search can't listen for it.", card.ShortcutProblem);
    }

    [Fact]
    public async Task With_quick_search_on_and_checking_off_closing_keeps_DeskAI_near_the_clock_without_Pause()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        var presence = app.Get<BackgroundPresenceController>();

        Assert.True(app.Presence.IsShowing);
        Assert.Equal(QuickSearchWords.Tooltip, app.Presence.Tooltips[^1]);
        Assert.Equal(new PresenceMenu(OffersPause: false, IsPaused: false, OffersFind: true), app.Presence.Menus[^1]);
        Assert.True(presence.KeepsRunningWhenClosed);
    }

    [Fact]
    public async Task With_both_off_closing_quits()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().SetOnAsync(false);

        Assert.False(app.Presence.IsShowing);
        Assert.False(app.Get<BackgroundPresenceController>().KeepsRunningWhenClosed);
    }

    [Fact]
    public async Task When_the_icon_cannot_show_closing_really_quits()
    {
        await using var app = await TestApp.StartAsync();
        app.Presence.RefuseToShow = true;

        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();

        Assert.False(app.Get<BackgroundPresenceController>().KeepsRunningWhenClosed);
    }

    [Fact]
    public async Task With_checking_on_the_checking_words_stay_and_the_shortcut_is_added()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().ApplyStoredAsync();
        var checking = AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground };

        app.Get<BackgroundPresenceController>().Refresh(checking);

        Assert.Equal(QuickSearchWords.WithChecking(BackgroundCheckingChoice.Tooltip(checking, null)), app.Presence.Tooltips[^1]);
        Assert.True(app.Presence.Menus[^1].OffersPause);
        Assert.True(app.Presence.Menus[^1].OffersFind);
    }

    [Fact]
    public async Task Find_a_file_from_the_icon_asks_for_the_bar()
    {
        await using var app = await TestApp.StartAsync();
        var asked = 0;
        app.Get<BackgroundPresenceController>().FindRequested += (_, _) => asked++;

        app.Presence.RaiseFind();

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Start_fresh_turns_quick_search_back_on()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSwitch>().SetOnAsync(false);

        await app.Get<SettingsViewModel>().StartFreshAsync();

        Assert.True(app.HotKey.IsListening);
        Assert.True(app.Presence.IsShowing);
    }

    [Fact]
    public void Closing_no_longer_claims_to_stop_everything() =>
        Assert.Equal(
            "Checking happens only while DeskAI is open. Closing it stops checking.",
            BackgroundCheckingChoice.MoreDetails(AutomaticCheckMode.WhileOpen).Split(" DeskAI does not add", 2)[0]);

    private static async Task<QuickSearchCardViewModel> OpenCardAsync(TestApp app)
    {
        var workspace = app.Get<WorkspaceViewModel>();
        await workspace.InitializeAsync();
        return workspace.QuickSearch;
    }
}
```

Check the non-background mode's enum name (`grep -n "enum AutomaticCheckMode" -A6 src/DeskAI.Core`) and use it in the last test; check how `MoreDetails` joins its sentences and adjust the `Split` to its real text.

- [ ] **Step 2: Run and see them fail.**

Run: `dotnet test tests/DeskAI.Presentation.Tests -c Release --filter FullyQualifiedName~QuickSearchSettingsPageTests`

- [ ] **Step 3: The contracts.** `IQuickSearchHotKey.cs`:

```csharp
namespace DeskAI.App.Services;

public enum HotKeyState { Off, Listening, TakenByAnotherProgram, Unavailable }

/// <summary>
/// Ctrl + Alt + Space, anywhere in Windows (ADR 0047).
/// </summary>
/// <remarks>
/// Windows tells DeskAI only about this one combination. It is not a keyboard hook and sees
/// nothing else anyone types. The Windows one is registered by the app; this project and the
/// tests never register a real shortcut.
/// </remarks>
public interface IQuickSearchHotKey
{
    HotKeyState State { get; }

    /// <summary>Starts or stops listening. Returns what happened, including Windows' refusal.</summary>
    HotKeyState Listen(bool on);

    /// <summary>The shortcut was pressed. Raised on the UI thread.</summary>
    event EventHandler? Pressed;
}

/// <summary>A DeskAI with no shortcut. Everything else still works.</summary>
public sealed class NoQuickSearchHotKey : IQuickSearchHotKey
{
    public HotKeyState State => HotKeyState.Unavailable;

    public HotKeyState Listen(bool on) => HotKeyState.Unavailable;

#pragma warning disable CS0067 // A shortcut that is never registered is never pressed.
    public event EventHandler? Pressed;
#pragma warning restore CS0067
}
```

`IBackgroundPresence`: add above the interface

```csharp
/// <summary>What the icon's menu offers. Travels with the tooltip so the two never disagree.</summary>
public sealed record PresenceMenu(bool OffersPause, bool IsPaused, bool OffersFind);
```

change `Show(string tooltip, bool isPaused)` → `Show(string tooltip, PresenceMenu menu)`, `Update(string tooltip, bool isPaused)` → `Update(string tooltip, PresenceMenu menu)` (update their doc comments: "whether its menu offers Pause checking and Find a file, and shows checking as paused"), and add `/// <summary>Someone asked for the quick search bar.</summary> event EventHandler? FindRequested;`. Update `NoBackgroundPresence` to match (add `FindRequested` inside its pragma block).

In `TestDoubles.RecordingPresence`: signatures take `PresenceMenu menu`; keep `PausedStates.Add(menu.IsPaused)`; add `public List<PresenceMenu> Menus { get; } = [];` filled beside `Tooltips`; add `public bool RefuseToShow { get; set; }` (when true, `Show` records nothing and leaves `IsShowing` false); add `FindRequested` and `public void RaiseFind() => FindRequested?.Invoke(this, EventArgs.Empty);`.

Add `RecordingHotKey`:

```csharp
/// <summary>The shortcut, as a test can see it. Never registers anything with Windows.</summary>
internal sealed class RecordingHotKey : IQuickSearchHotKey
{
    public bool IsListening { get; private set; }

    /// <summary>The next Listen(true) answers as if another program had the shortcut.</summary>
    public bool RefuseNext { get; set; }

    public HotKeyState State { get; private set; } = HotKeyState.Off;

    public HotKeyState Listen(bool on)
    {
        if (on && RefuseNext)
        {
            RefuseNext = false;
            IsListening = false;
            return State = HotKeyState.TakenByAnotherProgram;
        }

        IsListening = on;
        return State = on ? HotKeyState.Listening : HotKeyState.Off;
    }

    public event EventHandler? Pressed;

    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);
}
```

`TestApp`: `Replace<IQuickSearchHotKey>(services, new RecordingHotKey());` and `public RecordingHotKey HotKey => (RecordingHotKey)_services.GetRequiredService<IQuickSearchHotKey>();`.

- [ ] **Step 4: Wording** (`src/DeskAI.Core/QuickSearch/QuickSearchWords.cs`):

```csharp
namespace DeskAI.Core.QuickSearch;

/// <summary>What DeskAI says about quick search outside the bar: the icon's tooltip.</summary>
/// <remarks>The tooltip field holds 128 characters; the longest checking tooltip plus the suffix stays well under.</remarks>
public static class QuickSearchWords
{
    public const string Tooltip = "DeskAI — press Ctrl + Alt + Space to find a file";

    public static string WithChecking(string checkingTooltip) => checkingTooltip + " · Ctrl + Alt + Space finds a file";
}
```

In `BackgroundCheckingChoice.MoreDetails` change `"Checking happens only while DeskAI is open. Closing it stops everything."` to `"Checking happens only while DeskAI is open. Closing it stops checking."`. In `src/DeskAI.App/Views/SettingsPage.xaml.cs` (~line 104) change `"It will stop keeping running after the window is closed.\n\n"` to `"Checking will stop when the window is closed.\n\n"`. Update any existing test asserting the old sentences (`grep -rn "stops everything\|stop keeping running" tests`).

- [ ] **Step 5: The controller.** In `BackgroundPresenceController`, keep the last checking settings and the quick search flag, and compute everything in one place:

```csharp
    private AutomaticCheckSettings _checking = AutomaticCheckSettings.Default;
    private bool _quickSearchOn;

    public event EventHandler? FindRequested;
```

constructor: `_presence.FindRequested += OnFindRequested;` (and `-=` in `Dispose`), with
`private void OnFindRequested(object? sender, EventArgs args) => FindRequested?.Invoke(this, EventArgs.Empty);`

```csharp
    /// <summary>Whether quick search is what keeps DeskAI near the clock right now (no checking in the background).</summary>
    public bool QuickSearchKeepsItRunning => KeepsRunningWhenClosed && _checking.Mode != AutomaticCheckMode.InBackground;

    public void Refresh(AutomaticCheckSettings settings)
    {
        _checking = settings;
        Apply();
    }

    /// <summary>Quick search was switched on or off (ADR 0047). It too keeps DeskAI near the clock.</summary>
    public void SetQuickSearch(bool isOn)
    {
        _quickSearchOn = isOn;
        Apply();
    }

    /// <summary>
    /// Makes the icon and "what closing means" match both reasons to stay: checking in the
    /// background, and quick search.
    /// </summary>
    /// <remarks>
    /// Quick search keeps DeskAI running only while the icon is actually showing. A DeskAI with no
    /// window and no icon could be stopped only by signing out, so when the icon cannot show,
    /// closing stays a real close.
    /// </remarks>
    private void Apply()
    {
        var checking = _checking.Mode == AutomaticCheckMode.InBackground;
        var quick = _quickSearchOn && CanShowAnIcon;
        if (!checking && !quick)
        {
            KeepsRunningWhenClosed = false;
            _presence.Hide();
            return;
        }

        var tooltip = checking
            ? BackgroundCheckingChoice.Tooltip(_checking, _checks.Latest?.ProposalCount)
            : QuickSearchWords.Tooltip;
        if (checking && quick)
        {
            tooltip = QuickSearchWords.WithChecking(tooltip);
        }

        var menu = new PresenceMenu(OffersPause: checking, IsPaused: checking && _checking.IsPaused, OffersFind: quick);
        if (_presence.IsShowing)
        {
            _presence.Update(tooltip, menu);
        }
        else
        {
            _presence.Show(tooltip, menu);
        }

        KeepsRunningWhenClosed = checking || (quick && _presence.IsShowing);
    }
```

Keep the existing XML doc on `Refresh` and `KeepsRunningWhenClosed` but update them to name both reasons. `CanShowAnIcon` currently returns `_presence is not NoBackgroundPresence`; `RecordingPresence` counts as able, which is what the tests need. Also update `MainWindow.ShowWhereItWentOnceThisRun` in Task 7 (it must skip the notice when `QuickSearchKeepsItRunning`).

- [ ] **Step 6: The switch and the card.**

```csharp
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Services;

/// <summary>
/// Turns quick search on or off everywhere at once: the stored choice, the shortcut, and the icon.
/// </summary>
/// <remarks>
/// One singleton, so the card on My workspace, startup, and Start fresh cannot leave the
/// shortcut listening while the icon says otherwise.
/// </remarks>
public sealed class QuickSearchSwitch(QuickSearchSettingsService settings, IQuickSearchHotKey hotKey, BackgroundPresenceController presence)
{
    private readonly QuickSearchSettingsService _settings = settings;
    private readonly IQuickSearchHotKey _hotKey = hotKey;
    private readonly BackgroundPresenceController _presence = presence;

    /// <summary>At startup and after Start fresh: do what is stored.</summary>
    public async Task<HotKeyState> ApplyStoredAsync() =>
        Apply((await _settings.LoadAsync().ConfigureAwait(true)).IsOn);

    public async Task<HotKeyState> SetOnAsync(bool on)
    {
        await _settings.SetOnAsync(on).ConfigureAwait(true);
        return Apply(on);
    }

    private HotKeyState Apply(bool on)
    {
        var state = _hotKey.Listen(on);
        _presence.SetQuickSearch(on);
        return state;
    }
}
```

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.App.Services;
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.ViewModels;

/// <summary>One buddy's tile on the Quick search card.</summary>
public sealed class BuddyTileViewModel(SearchBuddy buddy) : ObservableObject
{
    private bool _isChosen;

    public SearchBuddy Buddy { get; } = buddy;

    public string Name { get; } = SearchBuddyLines.Name(buddy);

    public bool IsChosen { get => _isChosen; set => SetProperty(ref _isChosen, value); }
}

/// <summary>The Quick search card on My workspace: the switch, the seven buddies, and a shortcut problem.</summary>
public sealed class QuickSearchCardViewModel(QuickSearchSettingsService settings, QuickSearchSwitch quickSwitch, IQuickSearchHotKey hotKey) : ObservableObject
{
    public const string WorksWhenLine =
        "Works while DeskAI is open or near the clock. Closing the window keeps it near the clock; quit from the icon's menu there.";

    private readonly QuickSearchSettingsService _settings = settings;
    private readonly QuickSearchSwitch _switch = quickSwitch;
    private readonly IQuickSearchHotKey _hotKey = hotKey;
    private bool _isOn = true;
    private string _shortcutProblem = string.Empty;

    public static string WorksWhen => WorksWhenLine;

    public IReadOnlyList<BuddyTileViewModel> Buddies { get; } =
        Enum.GetValues<SearchBuddy>().Select(buddy => new BuddyTileViewModel(buddy)).ToArray();

    public bool IsOn { get => _isOn; private set => SetProperty(ref _isOn, value); }

    public string ShortcutProblem
    {
        get => _shortcutProblem;
        private set { if (SetProperty(ref _shortcutProblem, value)) OnPropertyChanged(nameof(HasShortcutProblem)); }
    }

    public bool HasShortcutProblem => _shortcutProblem.Length > 0;

    public async Task InitializeAsync()
    {
        var stored = await _settings.LoadAsync().ConfigureAwait(true);
        IsOn = stored.IsOn;
        Choose(stored.Buddy);
        Report(_hotKey.State);
    }

    public async Task SetOnAsync(bool on)
    {
        IsOn = on;
        Report(await _switch.SetOnAsync(on).ConfigureAwait(true));
    }

    public async Task ChooseBuddyAsync(SearchBuddy buddy)
    {
        await _settings.SetBuddyAsync(buddy).ConfigureAwait(true);
        Choose(buddy);
    }

    private void Choose(SearchBuddy buddy)
    {
        foreach (var tile in Buddies)
        {
            tile.IsChosen = tile.Buddy == buddy;
        }
    }

    private void Report(HotKeyState state) => ShortcutProblem = state == HotKeyState.TakenByAnotherProgram
        ? "Another program already uses Ctrl + Alt + Space, so quick search can't listen for it."
        : string.Empty;
}
```

In `WorkspaceViewModel`: add constructor parameter `QuickSearchCardViewModel quickSearch`, property `/// <summary>The Quick search card (ADR 0047).</summary> public QuickSearchCardViewModel QuickSearch { get; }`, and in `InitializeAsync` add `await QuickSearch.InitializeAsync().ConfigureAwait(true);`.

In `SettingsViewModel`: add primary-constructor parameter `QuickSearchSwitch quickSearch`, and in `StartFreshAsync` after `presence.Refresh(AutomaticCheckSettings.Default);` add `await quickSearch.ApplyStoredAsync();`.

In `FreshStartPageTests` (~line 65), the big Start fresh test asserts `Assert.False(app.Presence.IsShowing);`. After Start fresh quick search is on again (its default), so the icon stays, but only for quick search. Replace that line with:

```csharp
        // Checking in the background stopped; the icon stays only because quick search is on again.
        Assert.Equal(QuickSearchWords.Tooltip, app.Presence.Tooltips[^1]);
        Assert.False(app.Presence.Menus[^1].OffersPause);
```

- [ ] **Step 7: Help topic.** In `HelpCatalog.All`, after `workspace.look`:

```csharp
        new("workspace.quicksearch", "Quick search",
            "A small search bar you open from any app with Ctrl + Alt + Space.",
            "Type a name, a kind, a time, or words inside a file. Enter opens familiar files in their usual app; anything else is shown in its folder. It looks only in folders you connected.",
            "It never opens programs, never uses AI, never sends anything, and remembers nothing you type."),
```

Run the help tests; if a part is over its word limit, shorten it without dropping the "never" facts. Add `[InlineData("workspace.quicksearch")]` to `Each_feature_the_design_names_has_help`.

- [ ] **Step 8: Register.** In `DeskAiApplicationServices` next to `IBackgroundPresence`:

```csharp
        // The quick search shortcut. None here; the Windows one is registered by the app.
        services.AddSingleton<IQuickSearchHotKey, NoQuickSearchHotKey>();
        services.AddSingleton<QuickSearchSwitch>();
        services.AddTransient<QuickSearchCardViewModel>();
```

- [ ] **Step 9: Run the full Presentation suite.** Fix every compile error from the `IBackgroundPresence` signature change (only `TrayPresence` in the App remains; it is done in Task 7 — to keep the solution building now, change `TrayPresence.Show/Update` to take `PresenceMenu` and store `menu.IsPaused` into `_isPaused`, plus add `public event EventHandler? FindRequested;`; the menu item itself comes in Task 7).

Run: `dotnet build DeskAI.sln -c Release --no-restore` then `dotnet test tests/DeskAI.Presentation.Tests -c Release --no-build`
Expected: 0 warnings, all pass.

- [ ] **Step 10: Commit.**

```powershell
git add src tests
git commit -m "Let quick search keep DeskAI near the clock, with a switch and a buddy choice on My workspace"
```

---

### Task 7: Presentation — the tip on Home and Search, and the welcome page

**Files:**
- Create: `src/DeskAI.Presentation/ViewModels/QuickSearchTipViewModel.cs`
- Modify: `src/DeskAI.Presentation/ViewModels/DashboardViewModel.cs`, `SearchViewModel.cs` (expose `Tip`)
- Modify: `src/DeskAI.Presentation/ViewModels/WelcomeViewModel.cs` (new page; `WelcomePage` gains `bool ShowsBuddy = false`)
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs`
- Test: `tests/DeskAI.Presentation.Tests/QuickSearchTipPageTests.cs`
- Test: `tests/DeskAI.Presentation.Tests/WelcomePageTests.cs` (update counts: 3 → 4 pages)

**Interfaces:**
- Produces: `QuickSearchTipViewModel(QuickSearchSettingsService)` with `bool IsShown`, `const string Text = "Tip: press Ctrl + Alt + Space anywhere to find a file."`, `Task LoadAsync()`, `Task DismissAsync()`; `DashboardViewModel.Tip`, `SearchViewModel.Tip`; `WelcomePage(string Title, string Body, IReadOnlyList<string> Promises, bool ShowsBuddy = false)`.

- [ ] **Step 1: Failing tests.**

```csharp
using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>The one-line tip about the shortcut on Home and Search, and the welcome's new page.</summary>
public sealed class QuickSearchTipPageTests
{
    [Fact]
    public async Task Home_and_Search_show_the_tip_until_it_is_closed_on_either()
    {
        await using var app = await TestApp.StartAsync();
        var home = app.Get<DashboardViewModel>();
        await home.InitializeAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        Assert.True(home.Tip.IsShown);
        Assert.True(search.Tip.IsShown);
        Assert.Equal("Tip: press Ctrl + Alt + Space anywhere to find a file.", QuickSearchTipViewModel.Text);

        await search.Tip.DismissAsync();

        var homeAgain = app.Get<DashboardViewModel>();
        await homeAgain.InitializeAsync();
        Assert.False(homeAgain.Tip.IsShown);

        await using var reopened = await app.ReopenAsync();
        var searchAfterReopen = reopened.Get<SearchViewModel>();
        await searchAfterReopen.InitializeAsync();
        Assert.False(searchAfterReopen.Tip.IsShown);
    }

    [Fact]
    public async Task The_tip_is_hidden_while_quick_search_is_off()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<QuickSearchSettingsService>().SetOnAsync(false);
        var home = app.Get<DashboardViewModel>();

        await home.InitializeAsync();

        Assert.False(home.Tip.IsShown);
    }

    [Fact]
    public async Task The_welcome_has_a_page_about_quick_search_before_the_last_one()
    {
        await using var app = await TestApp.StartAsync();
        var welcome = app.Get<WelcomeViewModel>();
        await welcome.OpenAsync();

        welcome.Next();
        welcome.Next();

        Assert.Equal("Find any file, from anywhere", welcome.Current.Title);
        Assert.Equal("Press Ctrl + Alt + Space in any app. Type what you're looking for, and press Enter to open it.", welcome.Current.Body);
        Assert.True(welcome.Current.ShowsBuddy);
        Assert.Equal("Page 3 of 4", welcome.PageNumberText);
        Assert.Equal([false, false, true, false], welcome.Dots);
        welcome.Next();
        Assert.Equal("Let's start", welcome.Current.Title);
    }
}
```

In `WelcomePageTests.cs`, update the three-page expectations to four: `"Page 1 of 3"` → `"Page 1 of 4"`, dots arrays gain a fourth `false`, and the walk passes through the new page before "Let's start". Rename `Next_Back_and_Done_walk_the_three_pages` to `..._four_pages`. Do not change what the other pages say.

- [ ] **Step 2: Run and see them fail.**

- [ ] **Step 3: Write the tip view model.**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.ViewModels;

/// <summary>The line on Home and Search that tells people about Ctrl + Alt + Space. Closed once, gone on both.</summary>
public sealed class QuickSearchTipViewModel(QuickSearchSettingsService settings) : ObservableObject
{
    public const string Text = "Tip: press Ctrl + Alt + Space anywhere to find a file.";

    private readonly QuickSearchSettingsService _settings = settings;
    private bool _isShown;

    public bool IsShown { get => _isShown; private set => SetProperty(ref _isShown, value); }

    public async Task LoadAsync()
    {
        var stored = await _settings.LoadAsync().ConfigureAwait(true);
        IsShown = stored.IsOn && !stored.TipDismissed;
    }

    public async Task DismissAsync()
    {
        IsShown = false;
        await _settings.DismissTipAsync().ConfigureAwait(true);
    }
}
```

Add `QuickSearchTipViewModel tip` to `DashboardViewModel`'s primary constructor with `public QuickSearchTipViewModel Tip { get; } = tip;` and call `await Tip.LoadAsync().ConfigureAwait(true);` at the start of `InitializeAsync`. Same for `SearchViewModel`: add the parameter **before** the optional `visualSearch` parameter, store it in `Tip`, and load it in `InitializeAsync`. Register `services.AddTransient<QuickSearchTipViewModel>();`.

- [ ] **Step 4: The welcome page.** In `WelcomeViewModel.cs`:

```csharp
public sealed record WelcomePage(string Title, string Body, IReadOnlyList<string> Promises, bool ShowsBuddy = false);
```

and in `Pages`, between "You stay in charge" and "Let's start":

```csharp
        new("Find any file, from anywhere",
            "Press Ctrl + Alt + Space in any app. Type what you're looking for, and press Enter to open it.", [], ShowsBuddy: true),
```

Check that `ConnectChosenAsync`, `IsLastPage`, and the dialog logic rely only on `Pages.Count` and the last index (not a hard-coded 2); fix any hard-coded index.

- [ ] **Step 5: Run the Presentation suite; all pass.**

- [ ] **Step 6: Commit.**

```powershell
git add src/DeskAI.Presentation tests/DeskAI.Presentation.Tests
git commit -m "Tell people about quick search in the welcome and with a tip on Home and Search"
```

---

### Task 8: App — the shortcut, the bar window, Sparky, and the icon's menu

This is the first task a person can see. It needs a working Windows desktop; build into a scratch
output folder while the owner has DeskAI open (memory: build with `-p:OutDir=` into
`$env:CLAUDE_JOB_DIR\tmp\build\` or similar), and ask the owner to close DeskAI only for the final
Release build.

**Files:**
- Create: `src/DeskAI.App/Services/HotKeyInterop.cs`, `src/DeskAI.App/Services/GlobalHotKey.cs`
- Create: `src/DeskAI.App/Views/QuickSearchWindow.xaml`, `.xaml.cs`
- Create: `src/DeskAI.App/Views/Buddies/BuddyControl.cs`, `BuddyFactory.cs`, `SparkyBuddy.xaml`, `SparkyBuddy.xaml.cs`
- Create (only if the probe in Step 1 shows it works): `src/DeskAI.App/Views/SeeThroughBackdrop.cs`
- Modify: `src/DeskAI.App/Services/TrayPresence.cs` (menu: Open DeskAI, Find a file, Pause checking only when offered, Quit DeskAI)
- Modify: `src/DeskAI.App/App.xaml.cs` (register `GlobalHotKey`; at startup apply the switch; wire `Pressed` and `FindRequested` to the bar; see-more opens the main window)
- Modify: `src/DeskAI.App/MainWindow.xaml.cs` (no "still running" notice when only quick search keeps DeskAI; a method that opens a page from the bar)
- Modify: `src/DeskAI.App/Views/WorkspacePage.xaml` + `.xaml.cs` (the Quick search card), `DashboardPage.xaml` + `SearchPage.xaml` (+ code-behind: the tip), `WelcomeDialog.cs` (Sparky on the new page)
- Modify: `tools/UiPreview.cs` only if it must replace the hotkey (it should **not**: the owner tries the real shortcut in the preview; the preview's folders are generated)
- Test: `tests/DeskAI.Presentation.Tests/QuickSearchLayoutTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–7.
- Produces: `GlobalHotKey : IQuickSearchHotKey, IDisposable` with `internal bool EnsureMessageWindow()`; `QuickSearchWindow(QuickSearchViewModel)` with `void ShowNearPointer()`, `void HideBar()`; `BuddyControl : UserControl` with dependency property `Mood` (`BuddyMood`) and `static BuddyControl BuddyFactory.Create(SearchBuddy)`.

- [ ] **Step 1: Probe the see-through window (15 minutes, then decide).** The buddy should sit on the bar's top edge with the desktop visible around it. Make a throwaway branch-free experiment: a `Window` whose `SystemBackdrop` is a custom `SystemBackdrop` that sets a fully transparent tint on its `ICompositionSupportsSystemBackdrop` target (the approach WinUIEx's `TransparentTintBackdrop` uses), with the presenter's border and title bar off. Launch it over a normal window.
  - If the area outside the bar is see-through and clicks pass visually (clicks may still hit the window; that is fine because clicking there hides the bar), keep it as `SeeThroughBackdrop.cs` with a comment naming this probe.
  - If not, use the **fallback** and write it into the spec's rulings: the window is exactly the bar's size plus a 40 px strip above it painted in the bar's navy, the buddy sits half in that strip and half on the bar, and the strip's corners are rounded to match. Tell the owner in the task report which one was used.
  Delete the experiment code either way; only the kept class goes in.

- [ ] **Step 2: Write the failing layout tests** (`QuickSearchLayoutTests.cs`), in the style of `WelcomeLayoutTests` (read files as text):

```csharp
namespace DeskAI.Presentation.Tests;

/// <summary>
/// The quick search window and buddies, read from their source: where the bar sits, what the
/// screen reader hears, that every buddy has every mood and a still pose, and that the shortcut is
/// the agreed one registered without a keyboard hook.
/// </summary>
public sealed class QuickSearchLayoutTests
{
    private static readonly string[] BuddyFiles =
        ["SparkyBuddy.xaml", "ArchieBuddy.xaml", "PipBuddy.xaml", "FetchBuddy.xaml", "InkyBuddy.xaml", "MochiBuddy.xaml", "PaigeBuddy.xaml"];

    [Fact]
    public void The_shortcut_is_Ctrl_Alt_Space_without_repeat_and_without_a_keyboard_hook()
    {
        var hotKey = Read("src", "DeskAI.App", "Services", "GlobalHotKey.cs") + Read("src", "DeskAI.App", "Services", "HotKeyInterop.cs");

        Assert.Contains("RegisterHotKey", hotKey, StringComparison.Ordinal);
        Assert.Contains("MOD_CONTROL | HotKeyInterop.MOD_ALT | HotKeyInterop.MOD_NOREPEAT", hotKey, StringComparison.Ordinal);
        Assert.Contains("VK_SPACE", hotKey, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowsHookEx", hotKey, StringComparison.Ordinal);
        Assert.DoesNotContain("WH_KEYBOARD", hotKey, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_sits_top_centre_and_stays_out_of_the_taskbar_and_Alt_Tab()
    {
        var window = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml.cs");

        Assert.Contains("TopFraction = 0.12", window, StringComparison.Ordinal);
        Assert.Contains("IsShownInSwitchers = false", window, StringComparison.Ordinal);
        Assert.Contains("IsAlwaysOnTop = true", window, StringComparison.Ordinal);
        Assert.Contains("SetBorderAndTitleBar(false, false)", window, StringComparison.Ordinal);
    }

    [Fact]
    public void The_buddys_line_is_announced_politely_and_the_buddy_itself_is_decorative()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");

        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Find a file\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Snippets_are_plain_text()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");

        Assert.Contains("Text=\"{x:Bind Snippet}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RichTextBlock", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Hyperlink", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Buddies))]
    public void Every_buddy_has_the_five_moods_their_moves_and_a_still_pose(string file)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "DeskAI.App", "Views", "Buddies", file);
        if (!File.Exists(path))
        {
            Assert.Skip($"{file} is drawn in a later task.");
        }

        var xaml = File.ReadAllText(path);
        foreach (var mood in new[] { "Idle", "Thinking", "Found", "Nothing", "Happy" })
        {
            Assert.Contains($"<VisualState x:Name=\"{mood}\"", xaml, StringComparison.Ordinal);
            Assert.Contains($"<VisualState x:Name=\"{mood}Moving\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("<VisualState x:Name=\"Still\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaElement", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaPlayer", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Moves_stop_when_Windows_animation_effects_are_off()
    {
        var control = Read("src", "DeskAI.App", "Views", "Buddies", "BuddyControl.cs");

        Assert.Contains("AnimationsEnabled", control, StringComparison.Ordinal);
        Assert.Contains("\"Still\"", control, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_quick_search_bar_notice_is_skipped_when_quick_search_alone_keeps_DeskAI()
    {
        var window = Read("src", "DeskAI.App", "MainWindow.xaml.cs");

        Assert.Contains("QuickSearchKeepsItRunning", window, StringComparison.Ordinal);
    }

    [Fact]
    public void The_icon_menu_offers_Find_a_file_and_Pause_only_when_offered()
    {
        var tray = Read("src", "DeskAI.App", "Services", "TrayPresence.cs");

        Assert.Contains("\"Find a file\"", tray, StringComparison.Ordinal);
        Assert.Contains("_menu.OffersPause", tray, StringComparison.Ordinal);
        Assert.Contains("_menu.OffersFind", tray, StringComparison.Ordinal);
    }

    public static TheoryData<string> Buddies() => new(BuddyFiles);

    // Read and RepositoryRoot: copied from WelcomeLayoutTests.
}
```

- [ ] **Step 3: Run and see them fail** (files missing).

- [ ] **Step 4: `HotKeyInterop.cs` and `GlobalHotKey.cs`.** Model `GlobalHotKey` on `TrayPresence`: a registered window class `"DeskAI.HotKeyWindow"`, one hidden window made by `EnsureMessageWindow()` on the UI thread (called from `App` after the host starts), a static window procedure that routes `WM_HOTKEY` (0x0312) with id `1` to the instance and raises `Pressed` on the UI thread (the message already arrives there).

```csharp
using System.Runtime.InteropServices;

namespace DeskAI.App.Services;

/// <summary>
/// The two Win32 calls behind <see cref="GlobalHotKey"/>, and nothing else. Windows reports only
/// the one registered combination; there is deliberately no keyboard hook here (ADR 0047).
/// </summary>
internal static class HotKeyInterop
{
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const uint VK_SPACE = 0x20;
    internal const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);
}
```

In `GlobalHotKey.Listen(bool on)`:

```csharp
    public HotKeyState Listen(bool on)
    {
        if (!on)
        {
            if (_registered)
            {
                HotKeyInterop.UnregisterHotKey(_window, HotKeyId);
                _registered = false;
            }

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

        if (HotKeyInterop.RegisterHotKey(_window, HotKeyId,
                HotKeyInterop.MOD_CONTROL | HotKeyInterop.MOD_ALT | HotKeyInterop.MOD_NOREPEAT, HotKeyInterop.VK_SPACE))
        {
            _registered = true;
            return State = HotKeyState.Listening;
        }

        return State = Marshal.GetLastWin32Error() == HotKeyInterop.ERROR_HOTKEY_ALREADY_REGISTERED
            ? HotKeyState.TakenByAnotherProgram
            : HotKeyState.Unavailable;
    }
```

Reuse `TrayInterop`'s `RegisterClassExW`, `CreateWindowExW`, `DefWindowProcW`, `DestroyWindow`, and window-class struct (copy `TrayPresence.EnsureWindowClass`'s shape, with its own class name and its own static `Instances` map). `Dispose` unregisters and destroys the window. Register in `App.xaml.cs` with the same remove-then-add pattern as `IBackgroundPresence`: `services.AddSingleton<IQuickSearchHotKey, GlobalHotKey>();` — **inside `#if !DESKAI_UI_PREVIEW` is not wanted**: the preview should have the real shortcut too, so register it for both builds.

- [ ] **Step 5: `BuddyControl.cs` and `BuddyFactory.cs`.**

```csharp
using DeskAI.Core.QuickSearch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views.Buddies;

/// <summary>
/// The base of every search buddy: moves it between its moods, and holds it still when Windows'
/// "Animation effects" are off.
/// </summary>
/// <remarks>
/// Each buddy's XAML has two groups of visual states. "Moods" (Idle, Thinking, Found, Nothing,
/// Happy) sets the pose — eyes, mouth, glow, props — with setters only. "Motion" holds the looping
/// moves for each mood (IdleMoving, ThinkingMoving, …) and "Still", which runs nothing. Keeping
/// poses and moves apart means a still buddy still shows the right mood.
/// </remarks>
public partial class BuddyControl : UserControl
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(BuddyMood), typeof(BuddyControl), new PropertyMetadata(BuddyMood.Idle, OnMoodChanged));

    private static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    public BuddyControl()
    {
        Loaded += (_, _) => GoToMood();
        IsTabStop = false;
    }

    public BuddyMood Mood
    {
        get => (BuddyMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    private static void OnMoodChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((BuddyControl)sender).GoToMood();

    private void GoToMood()
    {
        var mood = Mood.ToString();
        VisualStateManager.GoToState(this, mood, useTransitions: false);
        VisualStateManager.GoToState(this, Settings.AnimationsEnabled ? mood + "Moving" : "Still", useTransitions: false);
    }
}
```

```csharp
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Views.Buddies;

/// <summary>Makes the chosen buddy. A buddy not drawn yet shows Sparky.</summary>
internal static class BuddyFactory
{
    public static BuddyControl Create(SearchBuddy buddy) => buddy switch
    {
        _ => new SparkyBuddy(),
    };
}
```
(Later buddy tasks add their case.)

- [ ] **Step 6: `SparkyBuddy.xaml`.** Translate the Sparky SVG in `characters.html` (the `data-choice="sparky"` card) one shape at a time, keeping the 200 × 200 view box inside a `Viewbox` so the control scales to any size:
  - `<svg viewBox="0 0 200 200">` → `<Viewbox><Canvas Width="200" Height="200">…</Canvas></Viewbox>`
  - `<path d="…" fill="#x">` → `<Path Data="…" Fill="#x"/>` (the `d` string works unchanged as `Data`)
  - `<circle cx cy r>` → `<Ellipse Width="2r" Height="2r" Canvas.Left="cx-r" Canvas.Top="cy-r"/>`; `<ellipse cx cy rx ry>` → the same with `2rx`, `2ry`
  - `stroke`, `stroke-width`, `stroke-linecap="round"` → `Stroke`, `StrokeThickness`, `StrokeStartLineCap="Round" StrokeEndLineCap="Round"`
  - `radialGradient` / `linearGradient` → `RadialGradientBrush` / `LinearGradientBrush` with the same stops; the `feGaussianBlur` glow → a second copy of the ray shape behind it at `Opacity="0.45"` scaled 1.15 (no blur effect in XAML shapes)
  - `<g class="x">` → `<Canvas x:Name="…">` with a `CompositeTransform` and `RenderTransformOrigin` taken from the CSS `transform-origin`
  - each CSS `@keyframes` → a `Storyboard` with `DoubleAnimationUsingKeyFrames` on that transform (same percentages × duration, `RepeatBehavior="Forever"`, `EasingFunction` `SineEase EaseInOut` for `ease-in-out`)

  Named parts: `Body` (bob), `Orbit` (the three stars, spin 9 s), `Rays` (pulse 2.4 s), `LeftEye`/`RightEye` (blink 4.5 s), `WaveArm` (wave 1.3 s), `Shadow`, `Twinkle1`, `Twinkle2`, `Mouth`, `MouthOpen` (a round "o" `Path` for Thinking, hidden by default), `MouthFlat` (a flat line for Nothing, hidden by default).
  - Moods (setters only): **Idle** — default. **Thinking** — `Rays.Opacity=1`, `Glow.Opacity=0.8`, `Mouth` hidden, `MouthOpen` shown. **Found** — `Glow.Opacity=0.6`, big smile (`Mouth` data `M90 100 Q100 114 110 100`). **Nothing** — `Mouth` hidden, `MouthFlat` shown, `Glow.Opacity=0.2`. **Happy** — eyes as arcs (`LeftEye`/`RightEye` hidden, `HappyEyes` shown).
  - Motion: **IdleMoving** — bob 2.8 s, blink, orbit 9 s, wave, twinkles. **ThinkingMoving** — bob, orbit 3 s (faster), rays pulse 0.8 s. **FoundMoving** — bob, wave 0.8 s, twinkles. **NothingMoving** — bob only, slower (4 s). **HappyMoving** — one full spin of `Body` over 0.9 s (not repeating) plus twinkles. **Still** — no storyboard.

- [ ] **Step 7: `QuickSearchWindow`.** XAML outline (layout C: buddy over the bar, results below):

```xml
<Window x:Class="DeskAI.App.Views.QuickSearchWindow" … xmlns:viewmodels="using:DeskAI.App.ViewModels">
  <Grid x:Name="Root" Width="640" RowDefinitions="Auto,Auto" KeyDown="OnKeyDown">
    <!-- The buddy, perched on the bar's top edge. Decorative for screen readers: its line is announced instead. -->
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Spacing="8" Margin="0,0,0,-24" Canvas.ZIndex="1">
      <ContentControl x:Name="BuddyHost" Width="72" Height="72" Tapped="OnBuddyTapped"
                      AutomationProperties.AccessibilityView="Raw" />
      <Border Style="{StaticResource SpeechBubbleStyle}" VerticalAlignment="Top">
        <TextBlock Text="{x:Bind ViewModel.BuddyLine, Mode=OneWay}" AutomationProperties.LiveSetting="Polite" />
      </Border>
    </StackPanel>
    <Border Grid.Row="1" Style="{StaticResource QuickBarStyle}">
      <StackPanel Spacing="8">
        <TextBox x:Name="Box" PlaceholderText="Find a file" AutomationProperties.Name="Find a file"
                 Text="{x:Bind ViewModel.Phrase, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
        <!-- examples (ItemsControl of Buttons, visible when ShowsExamples) -->
        <!-- "By name" header + ItemsControl NameRows; "Words inside" header + ItemsControl InsideRows;
             each row: FontIcon Glyph, Name (SemiBold), Where (caption), Snippet (caption, one line,
             Text="{x:Bind Snippet}", Visibility HasSnippet), Button Content="{x:Bind ActionText}";
             a selected row gets the accent border via IsSelected. -->
        <!-- NameFact, InsideFact, OpenMessage lines; HyperlinkButton "See more in DeskAI"
             (Visibility ShowsSeeMore); Button "Open DeskAI" (Visibility ShowsOpenDeskAi). -->
      </StackPanel>
    </Border>
  </Grid>
</Window>
```
Put `SpeechBubbleStyle` and `QuickBarStyle` in the window's own resources: dark translucent navy `#E60B1726`, 1 px mint border (`DeskAccentBrush`), corner radius 16, padding 14. Use only existing text styles (`BodySecondaryStyle`, `CaptionStyle`).

Code-behind essentials:

```csharp
    /// <summary>How far down the screen the bar sits, as a share of the work area's height.</summary>
    private const double TopFraction = 0.12;

    public QuickSearchWindow(QuickSearchViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        Activated += OnActivated;
        ViewModel.PropertyChanged += OnViewModelChanged;   // Buddy → swap BuddyHost.Content; Mood → set BuddyControl.Mood
        ViewModel.NameRows.CollectionChanged += (_, _) => FitToContent();
        ViewModel.InsideRows.CollectionChanged += (_, _) => FitToContent();
    }

    public QuickSearchViewModel ViewModel { get; }

    /// <summary>The shortcut: on the screen with the pointer, top centre, focused.</summary>
    public async void ShowNearPointer()
    {
        await ViewModel.ShowAsync();
        TrayInterop.GetCursorPos(out var pointer);
        var area = DisplayArea.GetFromPoint(new PointInt32(pointer.X, pointer.Y), DisplayAreaFallback.Nearest).WorkArea;
        var scale = (Content?.XamlRoot?.RasterizationScale) ?? 1.0;
        var width = (int)(640 * scale);
        AppWindow.Move(new PointInt32(area.X + ((area.Width - width) / 2), area.Y + (int)(area.Height * TopFraction)));
        FitToContent();
        AppWindow.Show(activateWindow: true);
        Activate();
        TrayInterop.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Box.Focus(FocusState.Programmatic);
    }

    public void HideBar()
    {
        ViewModel.Hide();
        AppWindow.Hide();
    }

    /// <summary>Clicking anywhere else hides the bar, as Esc does.</summary>
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            HideBar();
        }
    }

    private async void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Escape: HideBar(); args.Handled = true; break;
            case VirtualKey.Down: ViewModel.MoveSelection(+1); args.Handled = true; break;
            case VirtualKey.Up: ViewModel.MoveSelection(-1); args.Handled = true; break;
            case VirtualKey.Enter:
                args.Handled = true;
                var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
                if (await ViewModel.ActivateAsync(ViewModel.Selected, showInFolder: ctrl)) HideBar();
                break;
        }
    }
```
`FitToContent` measures `Root` with `Measure(new Size(640, double.PositiveInfinity))` and calls `AppWindow.ResizeClient(new SizeInt32(width, (int)(Root.DesiredSize.Height * scale)))`. `OnBuddyTapped` calls `_ = ViewModel.PetBuddyAsync();`. Row buttons call `ActivateAsync(row, false)` and hide on true. The example buttons call `ChooseExampleAsync`.

- [ ] **Step 8: Wire it in `App.xaml.cs`.** After the window reveal (for both builds, outside `#if`):

```csharp
            await ConnectQuickSearchAsync(window);
```

```csharp
    /// <summary>
    /// Starts listening for Ctrl + Alt + Space when quick search is on, and joins the shortcut and
    /// the icon's "Find a file" to the bar (ADR 0047).
    /// </summary>
    private async Task ConnectQuickSearchAsync(MainWindow window)
    {
        var bar = new QuickSearchWindow(_host.Services.GetRequiredService<QuickSearchViewModel>());
        var hotKey = _host.Services.GetRequiredService<IQuickSearchHotKey>();
        if (hotKey is GlobalHotKey global)
        {
            global.EnsureMessageWindow();
        }

        hotKey.Pressed += (_, _) => window.DispatcherQueue.TryEnqueue(() => ToggleBar(bar));
        _host.Services.GetRequiredService<BackgroundPresenceController>().FindRequested +=
            (_, _) => window.DispatcherQueue.TryEnqueue(bar.ShowNearPointer);
        bar.ViewModel.OpenDeskAiRequested += (_, route) => window.DispatcherQueue.TryEnqueue(() =>
        {
            bar.HideBar();
            window.Reveal();
            window.GoTo(route, fresh: true);
        });
        await _host.Services.GetRequiredService<QuickSearchSwitch>().ApplyStoredAsync();
    }

    /// <summary>The shortcut shows the bar, and pressed again while it shows, hides it.</summary>
    private static void ToggleBar(QuickSearchWindow bar)
    {
        if (bar.AppWindow.IsVisible)
        {
            bar.HideBar();
        }
        else
        {
            bar.ShowNearPointer();
        }
    }
```
Order matters for the non-preview build: call `ConnectQuickSearchAsync` **after** `ConnectTheBackgroundPresenceAsync`, so the controller's first `Refresh` has run and `SetQuickSearch` sees the stored checking mode. In `QuitAsync`, add `_host.Services.GetRequiredService<IQuickSearchHotKey>().Listen(false);` before `presence.Hide()`.

- [ ] **Step 9: The icon menu** (`TrayPresence`). Replace `_isPaused` with `private PresenceMenu _menu = new(false, false, false);`, set it in `Show`/`Update`, add `private const uint CommandFind = 4;`, raise `FindRequested` for it, and build the menu:

```csharp
            TrayInterop.AppendMenuW(menu, TrayInterop.MF_STRING, CommandOpen, "Open DeskAI");
            if (_menu.OffersFind)
            {
                TrayInterop.AppendMenuW(menu, TrayInterop.MF_STRING, CommandFind, "Find a file");
            }

            if (_menu.OffersPause)
            {
                TrayInterop.AppendMenuW(
                    menu,
                    TrayInterop.MF_STRING | (_menu.IsPaused ? TrayInterop.MF_CHECKED : 0),
                    CommandPause,
                    "Pause checking");
            }

            TrayInterop.AppendMenuW(menu, TrayInterop.MF_STRING, CommandQuit, "Quit DeskAI");
```
Update the class comment ("Its menu starts nothing — open, find, pause, quit — …; Find a file only shows the search bar").

- [ ] **Step 10: `MainWindow`.** In `ShowWhereItWentOnceThisRun`, add `|| _presence.QuickSearchKeepsItRunning` to the early-return condition, with a comment: "Only checking in the background gets the notice; the owner did not pick a notification for quick search (spec decision 12), and its card says what closing does."

- [ ] **Step 11: The card, the tip, the welcome picture.**
  - `WorkspacePage.xaml`, Looks tab, after the looks `ItemsControl`: a section titled **Quick search** with `controls:HelpButton Topic="workspace.quicksearch"`; a `ToggleSwitch` `Header="Press Ctrl + Alt + Space to find a file"` bound one-way to `ViewModel.QuickSearch.IsOn` with a `Toggled` handler that calls `SetOnAsync` only when the value differs (same guard as `OnDarkModeToggled`); the `WorksWhen` line (`CaptionStyle`); the shortcut problem line (visible when `HasShortcutProblem`); **Your search buddy**: an `ItemsControl` of seven tiles (`RowCardStyle`, width 150) each with a 64 px `ContentControl` whose content is `BuddyFactory.Create(tile.Buddy)` (set in the item's `Loaded` handler, Mood Idle), the name, the "Chosen" pill as on look cards, and a button **Choose** calling `ChooseBuddyAsync`.
  - `DashboardPage.xaml` and `SearchPage.xaml`, directly under the page title block: a slim `Border` (visible when `ViewModel.Tip.IsShown`) with a 28 px Sparky (`BuddyFactory.Create(SearchBuddy.Sparky)`, Mood Idle), the tip text, and a close button (`AutomationProperties.Name="Close the tip"`, glyph `&#xE711;`) calling `Tip.DismissAsync()`. Pages read as steps (memory): the tip is one line and must not push the page's first step below the fold.
  - `WelcomeDialog.cs`: where a page is drawn, when `welcome.Current.ShowsBuddy`, put a 96 px `SparkyBuddy` (Mood Idle) above the body text.

- [ ] **Step 12: Build and run everything.**

```powershell
dotnet build DeskAI.sln -c Release --no-restore -p:OutDir=$env:CLAUDE_JOB_DIR\tmp\qs-build\
dotnet test DeskAI.sln -c Release --no-restore
dotnet format DeskAI.sln --no-restore --verify-no-changes
```
Expected: 0 warnings; all tests pass (the six other buddy files skip their layout test); formatting clean.

- [ ] **Step 13: Look at it yourself in the UI preview** (generated folders only): build with `-p:DeskAiUiPreview=true` into a scratch folder, start it, press Ctrl + Alt + Space, type `lesson`, check Sparky, the rows, Enter on the generated PDF, Esc, and that the bar hides when clicking elsewhere. Take no screenshots of anything but the preview window. Write what you saw in the task report, or say plainly that you could not look.

- [ ] **Step 14: Feature Coverage Map and commit.** Add rows to `docs/TESTING.md`'s table:

```
| Quick search | The bar: greeting, examples, names, words inside, Enter, Show in folder, refusals, buddy voices, nothing remembered, See more | `QuickSearchPageTests`, `QuickSearchServiceTests`, `QuickSearchLayoutTests` |
| Quick search | Opening a file safely | `WindowsFileLauncherTests`, `QuickSearchContainmentTests` |
| My workspace | Quick search card: switch, buddy choice, shortcut taken, icon near the clock, closing | `QuickSearchSettingsPageTests` |
| Home, Search | Quick search tip | `QuickSearchTipPageTests` |
| Welcome | Quick search page | `QuickSearchTipPageTests`, `WelcomePageTests` |
```

```powershell
git add src tools tests docs/TESTING.md
git commit -m "Show the quick search bar with Sparky on Ctrl + Alt + Space, and add Find a file near the clock"
```

---

### Tasks 9–14: the other six buddies (one task each, in this order: Archie, Pip, Fetch, Inky, Mochi, Paige)

Each is the same shape, so the owner can check them one by one. For buddy **X** (file names `XBuddy.xaml` / `XBuddy.xaml.cs`, mockup card `data-choice` in brackets):

| Task | Buddy | Mockup card | Named parts to animate (from the mockup's CSS classes) |
|---|---|---|---|
| 9 | `ArchieBuddy` | `owl` | `Body` (bob), `LeftWing`/`RightWing` (flapL/flapR 1.9 s), `Eyes` (look 5 s), `Head` (tilt 5.5 s), `FileCard` |
| 10 | `PipBuddy` | `robot` | `Body` (bob), `Jet` (flicker 0.18 s alternate), `ChestSpark` (light 1.3 s), `Lens` (swing 2.4 s), `Screen` |
| 11 | `FetchBuddy` | `fox` | `Body` (bob), `Tail` (wag 0.8 s), `LeftEar`/`RightEar` (twitch 3.2 s), `Scarf` (flutter 1.1 s), `FileInMouth` |
| 12 | `InkyBuddy` | `octopus` | `Body` (bob), `Arm1`…`Arm4` (tent 2.2 s, staggered 0.3 s), `Bubble1`…`Bubble3` (rise 3.2 s), `WinkEye` (wink 6 s) |
| 13 | `MochiBuddy` | `mochi` | `Body` (squish 1.5 s), `Sprout` (sway 2.2 s), `Star` (twinkle 1.8 s), `Eyes` (blink 4.5 s) |
| 14 | `PaigeBuddy` | the paper ghost card (the last card; `data-choice` in the file) | `Body` (bob), `Sheet` (floaty 3.4 s), `Clip`, `Eyes` (blink) |

**Files per task:**
- Create: `src/DeskAI.App/Views/Buddies/XBuddy.xaml`, `XBuddy.xaml.cs` (`public sealed partial class XBuddy : BuddyControl { public XBuddy() => InitializeComponent(); }`)
- Modify: `src/DeskAI.App/Views/Buddies/BuddyFactory.cs` (add `SearchBuddy.X => new XBuddy(),`)

- [ ] **Step 1: See the layout test fail.** Temporarily copy `SparkyBuddy.xaml` as the new file's starting point? **No** — start from the mockup. First confirm `QuickSearchLayoutTests.Every_buddy_has_the_five_moods_their_moves_and_a_still_pose` for `XBuddy.xaml` currently **skips**; create an empty `XBuddy.xaml` UserControl (root `buddies:BuddyControl`) and see it **fail** on the missing states.

- [ ] **Step 2: Draw it.** Translate the mockup card's SVG with the same rules as Sparky (Task 8 Step 6), keeping its colours, shape, props, and background-free (the card's `.stage` gradient is not part of the buddy).

- [ ] **Step 3: Moods and moves.** Moods (setters only), for every buddy:
  - **Idle** — the mockup pose.
  - **Thinking** — eyes looking to one side (move the pupils 2–3 units), mouth small "o"; for Archie the eyes dart (ThinkingMoving uses `look` at 1.2 s); for Pip the screen face shows three dots; for Fetch the nose lowers (sniffing); for Inky the arms reach up; for Mochi the sprout droops; for Paige the eyes half-close.
  - **Found** — big smile; the prop raised (file card, magnifier, file in mouth, photo and PDF, star, paperclip bow bright).
  - **Nothing** — a flat or small down mouth; props lowered; colours unchanged (never grey it out — it is not an error).
  - **Happy** — eyes as happy arcs; the "clicked" move from the table: Archie flaps both wings fast (0.3 s ×3), Pip spins its screen once, Fetch wags fast (0.3 s ×4), Inky waves every arm once, Mochi squishes twice, Paige does a loop-the-loop (rotate 360° over 0.9 s).
  - **XMoving** states run the named parts' storyboards from the table (Thinking faster, Nothing slower); **Still** runs nothing.

- [ ] **Step 4: Run** `dotnet test tests/DeskAI.Presentation.Tests -c Release --filter FullyQualifiedName~QuickSearchLayoutTests` — the buddy's row passes.

- [ ] **Step 5: Build with a scratch `OutDir`, open the UI preview, choose X on My workspace, press Ctrl + Alt + Space, and watch each mood** (type a match, type `zzqx`, click the buddy). Note anything that looks off in the task report; turn on Windows' "Animation effects" off and check the still poses.

- [ ] **Step 6: Commit.**

```powershell
git add src/DeskAI.App/Views/Buddies
git commit -m "Draw X as a search buddy"
```

---

### Task 15: Docs, the whole-change review, and the handoff

**Files:**
- Modify: `README.md` (feature list: Quick search in one plain paragraph; test count)
- Modify: `docs/USER-GUIDE.md` or the user guide file present in `docs/` (a "Quick search" section: the shortcut, the buddies, what Enter does, reading inside needs Search's permission, closing keeps DeskAI near the clock, how to quit, how to turn it off)
- Modify: `docs/UI-UX.md` (the bar, the tip, the card, the welcome page, the icon menu)
- Modify: `docs/SECURITY.md` (the "open a file" boundary and the hotkey, pointing to ADR 0047)
- Modify: `docs/ARCHITECTURE.md` (the `QuickSearch` namespace, `IFileLauncher`, `IShellStarter`, `IQuickSearchHotKey`)
- Modify: `docs/PRODUCT.md`, `docs/ROADMAP.md` (quick search built; "kept for later" items listed: buddy inside DeskAI's window, AI in the bar, Downloads cards)
- Modify: `docs/MANUAL-TESTING.md` (a dated "2026-09-25 Quick search" check using the UI preview build)
- Modify: `docs/HANDOFF.md` ("Start here" rewritten per its "How to update this file")

- [ ] **Step 1: Write the docs** in DeskAI's plain voice; no technical words in anything a person reads.

- [ ] **Step 2: The manual check** in `docs/MANUAL-TESTING.md`: in the UI preview build — press Ctrl + Alt + Space over another app; read Sparky's greeting; click an example; type `lesson` and press Enter (the generated PDF opens); type a generated program's name and check the button says Show in folder; on Search allow reading inside for the generated folder, then type a word from inside a generated text file and wait a moment for Words inside; press Esc; click outside; choose each buddy on My workspace and watch its moods; turn Windows' Animation effects off and check the still poses; close the window and find DeskAI near the clock, use **Find a file** and **Quit DeskAI**; turn quick search off and check closing quits; close the tip on Home and check Search.

- [ ] **Step 3: Verify.**

```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
dotnet format DeskAI.sln --no-restore --verify-no-changes
```
Ask the owner to close DeskAI before the normal Release build (it replaces the running exe).

- [ ] **Step 4: Whole-change review.** A fresh reviewer (subagent, most capable model) reads the spec, ADR 0047, the security review, and the full diff since the plan's first commit, looking for: anything that could start something other than the one validated file; any place a phrase, file name, or snippet is logged or stored; late results shown under new words; closing leaving an invisible DeskAI; wording that is technical or untrue. Fix each finding with a test that fails first; list deferred small points in the handoff.

- [ ] **Step 5: Handoff and commit.** Rewrite `docs/HANDOFF.md`'s "Start here": the commit `main`'s branch is on, what was verified, the launchable exe path, the owner's manual checks, deferred review points, the probe outcome from Task 8 Step 1, and the next step (ask the owner: release as 1.3.0, or check by hand first). Update the starter prompt.

```powershell
git add README.md docs
git commit -m "Document quick search, its manual check, and the handoff"
```
Do not push, tag, or release; ask the owner.
