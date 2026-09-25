# Quick search with a search buddy — design

- Date: 2026-09-25
- Status: agreed with the owner in conversation on 2026-09-25; the owner reviewed and approved
  the written spec on 2026-09-25 with one change (the bar also looks inside files; decisions
  16–19). Implementation plan: `docs/superpowers/plans/2026-09-25-quick-search.md`.
- Mockups (open in a browser; they are the art reference for the redraw):
  `docs/superpowers/specs/2026-09-25-quick-search-mockups/characters.html` (the seven
  characters) and `.../layouts.html` (three layouts; **C** was chosen).

## Goal

Press **Ctrl + Alt + Space** anywhere in Windows. A slim search bar appears near the top of the
screen, with the person's chosen **search buddy** (an animated character) perched on its edge.
They type ("essay from last week"), the buddy reacts, matching files from their connected
folders drop down, and **Enter** opens the chosen file in its usual app. **Esc** hides it all.

It should feel like DeskAI's own thing: friendly and a little playful, while staying as honest
and careful as the rest of DeskAI.

## Decisions the owner made (2026-09-25)

Each one was asked in plain words and answered by the owner. Do not re-ask them.

1. **Feature chosen** from the whole-system ideas list: "quick search from anywhere". Other ideas
   offered and not picked (keep for later): storage map, "your week in DeskAI" timeline, better
   names for messy files, near-copy pictures to the Recycle Bin, clearing old installers and zips,
   screenshot sorter, a Ctrl+K command box. From the Desktop Studio list the owner picked "the
   same cards for Downloads", **for later, not now**.
2. **Enter opens the file in its usual app, but never programs.** Programs, scripts, and
   shortcuts are never opened; they get **Show in folder** instead (File Explorer opens with the
   file highlighted).
3. **When it works:** while DeskAI's window is open, and while DeskAI sits as the icon near the
   clock. If DeskAI is fully quit, the keys do nothing. DeskAI does **not** start with Windows.
4. **Closing the window keeps DeskAI near the clock** so the shortcut still works (owner's answer:
   "Yes, stay near the clock"). Closing still does no checking or tidying unless that was turned on
   separately. Quitting is from the icon's menu.
5. **A character pops up** when the shortcut is pressed (the owner's own idea), and **the person
   types to it**. No voice or microphone.
6. **All seven characters** are included: Sparky, Archie the owl, Pip the robot, Fetch the fox,
   Inky the octopus, Mochi, Paige the paper ghost. The person picks one in settings; **Sparky** is
   the default. The owner may want to **edit a couple of them later**, so each character's art
   lives in its own file and can change without touching the others.
7. **Layout C, "Perched on top":** a slim bar near the top centre of the screen, the buddy sitting
   on its top edge and peeking over, results dropping down below.
8. **Shortcut: Ctrl + Alt + Space.** (Win + Shift + Space was dropped: Windows uses it to switch
   keyboard language, and the owner uses more than one.) The shortcut is fixed; there is no key
   picker in this version.
9. **Empty bar:** a greeting from the buddy plus 2–3 example searches that can be clicked.
   **Nothing is remembered**: no search history and no list of opened files.
10. **Each buddy has its own voice**: short lines in its own style (see "The seven buddies").
11. **No sounds.**
12. **Discovery:** a **new page in the first-run welcome** and a **dismissible tip on Home and on
    Search**. (Not chosen: a Windows notification the first time DeskAI stays near the clock.)
13. **Clicking the buddy** makes it react happily (a small animation and a line). It does nothing
    else.
14. **The buddy inside DeskAI's own window** (a mascot on Home or Search) is **a later, separate
    step**, not part of this feature.
15. **How the art is built: drawn natively in DeskAI** (option 1). Each character is redrawn as
    XAML vector art with XAML animations, one file per character. Not a web view (it would add a
    browser engine and a place for web content inside DeskAI), and not pre-made animation files
    (they need a new tool and a new dependency, and are hard to edit).

Answered when the owner reviewed this spec (2026-09-25):

16. **No AI in the first version** (confirmed). The bar uses DeskAI's own phrase understanding (the
    same as Search without AI), so it is instant and nothing leaves the PC. AI can be a later step.
17. **It also reads inside files** (the owner's change; the draft said names only). Besides
    remembered names, kinds, dates, and sizes, the bar looks for the words inside files, the same
    way Search's "Found inside your files" does.
18. **The inside look starts by itself after a short pause in typing.** Name matches show at once;
    files with the words inside appear underneath a moment later. Typing again stops the look.
    (Not chosen: only on a button or Tab.)
19. **On by default** (confirmed), with a switch in settings to turn it off. Turning it off also
    means closing the window quits DeskAI again, unless background checking is on.

### Rulings made while writing this spec (the owner may overrule)

- **Only familiar file types open** (an allow-list, below). Everything else gets Show in folder.
  Allowing a known-safe list is stricter than trying to block every risky type.
- **Reading inside uses the permissions already given on Search, and nothing more.** The bar looks
  inside only folders where the person allowed "Read inside files", PDFs only where PDF reading is
  allowed, slides only where slide reading is allowed (the same checks as Search, because it is the
  same `ContentSearchService`). The bar never grants a permission itself; that stays on Search with
  its own questions. The **scanned PDF words** reader (Windows OCR) is never used from the bar,
  because it asks a fresh yes on each run.
- **Same limits as Search.** One inside look opens at most 50 files and stops after 20 seconds, and
  nothing read is kept. No new limits and no second reading code.
- **The pause before the inside look is about 600 ms** (names wait about 150 ms). A new keystroke
  cancels a look that is running, so at most one runs at a time.
- **Two short groups of results:** "By name" (at most 5 rows) and "Words inside" (at most 5 rows,
  each with its short piece of text). A file already listed by name is not listed again under
  Words inside.
- The link **See more in DeskAI** (was "Look inside files in DeskAI") opens the full Search page
  with the same words, where "Files checked" and the scanned PDF reader are.
- Feature name in the UI: **Quick search**. The character is the **search buddy**.

### Rulings made while planning (2026-09-25; the owner may overrule)

- `IFileLauncher` takes the folder's **ID**, not the folder object, so the launcher looks the folder
  up at the moment of opening and refuses one that was disconnected in the meantime.
- The "DeskAI is still running" Windows notification stays tied to background checking only. When
  only quick search keeps DeskAI near the clock, no notification is shown (the owner did not pick a
  notification for discovery, decision 12); the Quick search card and help say what closing does.
- "Checking happens only while DeskAI is open. Closing it stops everything." becomes "… Closing it
  stops checking.", and Start fresh's dialog says checking stops, because closing no longer always
  quits. After Start fresh the icon stays near the clock for quick search (back on by default).
- **Open DeskAI** on the "connect a folder first" line opens Home (its Your folders card).
- When a file matched by name also has the words inside, it is not listed twice, and the inside
  line says "Nothing else found inside the *N* files DeskAI checked." (not "Nothing found", which
  would be untrue).
- Quick search keeps DeskAI running after the window closes only while the icon near the clock is
  actually showing; if Windows refuses the icon, closing really quits.
- If a see-through window is not possible, the buddy sits in a strip above the bar painted like the
  bar (the plan's Task 8 probe decides; the owner is told which).

## What the person sees

### The bar (layout C)

- Appears on the screen where the mouse pointer is, horizontally centred, about 12% down from the
  top. About 640 px wide (scaled for the display), rounded, dark translucent navy with a thin mint
  border, like the mockup.
- The buddy (about 72 px) sits on the bar's top edge, centred, peeking over. Its speech bubble
  appears beside it with its current line.
- The box has keyboard focus at once. Results drop down under the box as the person types, in two
  short groups: **By name** (at most 5 rows) and, a moment later, **Words inside** (at most 5
  rows). Each row: file-type icon, file name, where it is ("Documents › School"), and a button:
  **Open** for an allowed type, **Show in folder** otherwise. A Words inside row also shows its
  short piece of text (one line, cut with "…"), and the page or slide when known. The first row is
  selected.
- While the inside look runs, the Words inside group shows "Looking inside files…" and the buddy
  is in its Thinking mood. Name rows can already be chosen and opened.
- Below the rows, when relevant, plain lines of facts (see "Honest lines") and the link
  **See more in DeskAI**.
- Keys: typing searches names after a short pause (about 150 ms) and inside files after a longer
  one (about 600 ms); a new keystroke cancels both; **Up/Down** move the selection across both
  groups; **Enter** does the selected row's button; **Ctrl + Enter** always does Show in folder;
  **Esc** hides the bar and stops any inside look. Clicking outside the bar hides it. Opening a
  file hides it. Pressing the shortcut while the bar is open hides it.
- The bar is not in the taskbar or Alt + Tab, stays on top while open, and never steals focus
  unless the shortcut was pressed.

### Empty bar

The buddy's greeting in its bubble, and 2–3 clickable example searches under the box, for
example **pdf from last week**, **photos from this month**, **big videos**. Clicking one fills the
box and searches. The examples are fixed text, not taken from the person's files.

### Honest lines (the same for every buddy)

The buddy's line is flavour. The facts are always stated plainly under the box in DeskAI's usual
voice, so no character can blur them:

- No folders connected: "Connect a folder in DeskAI first. Quick search looks only in folders you
  connected." with a button **Open DeskAI**.
- Nothing understood: "Try a name, a kind like "pdf", or a time like "last week"."
- Nothing matched by name or inside: "Nothing matched in your connected folders."
- More than 5 by name: "Showing the first 5 by name. **See more in DeskAI** for everything."
- No connected folder allows reading inside: "To find words inside files too, allow it for a folder
  on the Search page." (with **See more in DeskAI**). Shown once, under By name, instead of the
  Words inside group.
- Words fewer than 3 letters: no inside look and no line (as on Search).
- Nothing found inside: "Nothing found inside the *N* files DeskAI checked." When the 50-file or
  20-second limit was reached: "Nothing found in the first *N* files DeskAI checked." / "There may
  be more. **See more in DeskAI**."
- A file that could only be partly read, or not read at all, is not listed as a row; the full
  "Files checked" list stays on the Search page.
- A result refused at open time (see Safety): "DeskAI couldn't open it: *reason*." The bar stays.

### Settings

On **My workspace**, next to **DeskAI's look**, a new card **Quick search**:

- A switch **Press Ctrl + Alt + Space to find a file** (on by default) and one line saying it works
  while DeskAI is open or near the clock.
- **Your search buddy:** seven tiles, each with the buddy's still picture and name; exactly one is
  chosen. Choosing one saves at once.
- If Windows refuses the shortcut because another program already uses it: "Another program
  already uses Ctrl + Alt + Space, so quick search can't listen for it." (No automatic fallback.)
- A "?" help topic, like every feature.

### Discovery

- **Welcome:** a new page, placed before the last page: title "Find any file, from anywhere", the
  Sparky picture, and "Press Ctrl + Alt + Space in any app. Type what you're looking for, and press
  Enter to open it." The page dots and "Page N of M" follow automatically.
- **Tip on Home and on Search:** a small line with the buddy's face: "Tip: press Ctrl + Alt + Space
  anywhere to find a file." with a close button. Closing it on either page hides it on both, for
  good (`quicksearch.tip.dismissed` in `app_settings`). Not shown while quick search is off.

### The icon near the clock

- Shown whenever quick search is on **or** background checking is on.
- Tooltip when only quick search keeps it: "DeskAI — press Ctrl + Alt + Space to find a file".
  When checking is on, today's checking tooltip stays, with the shortcut added.
- Menu: **Open DeskAI**, **Find a file** (opens the bar), **Pause checking** (only when background
  checking is on), **Quit DeskAI**.

## The seven buddies

Every buddy has five moods, each a XAML visual state: **Idle** (gentle float and blink, the
greeting), **Thinking** (while a search runs), **Found**, **Nothing**, and **Happy** (when clicked;
returns to the previous mood after about 1.5 s). When Windows' "Animation effects" setting is off,
each mood is a still pose with no looping movement.

Lines are short (a test keeps each under 40 characters) and use no technical words. `{n}` is the
number found; the singular form is used for 1.

| Buddy | Look and moves (see mockup) | Greeting | Found | Nothing | Clicked |
|---|---|---|---|---|---|
| **Sparky** (default) | DeskAI's logo spark with a face; three tiny stars orbit; glows brighter while thinking; waves | "Hi! What are we looking for?" | "Found {n}!" | "Hmm, nothing yet." | "Wheee!" (spins) |
| **Archie the owl** | Librarian owl in round gold glasses; eyes dart while thinking; wings flap; holds a file card | "Which file shall we find?" | "Ah, {n} in the archives." | "Nothing on my shelves." | "Hoo-hoo!" |
| **Pip the robot** | Hover robot, glowing screen face, spark on chest, magnifying glass; jet flickers | "Ready to scan." | "Scan done: {n} found." | "Scan done: no match." | "Beep boop!" |
| **Fetch the fox** | Fox in a mint scarf with a file in its mouth; tail wags, ears twitch | "Want me to fetch something?" | "Fetched {n}!" | "I sniffed everywhere…" | "Wag wag!" |
| **Inky the octopus** | Purple octopus holding a photo and a PDF; arms wave; bubbles; winks | "All arms ready!" | "Grabbed {n}!" | "Nothing in reach." | "Blub!" |
| **Mochi** | Mint jelly blob with a leaf sprout, sparkly eyes, a gold star; squishes | "Hello! What shall we find?" | "Yay, {n} found!" | "Aww, nothing yet." | "Squish!" |
| **Paige the paper ghost** | Ghost of notebook paper with a paperclip bow; floats and sways | "Boo! Looking for something?" | "Boo! Found {n}." | "Not a ghost of a match." | "Hee hee!" |

Thinking line for all: the buddy's own short word ("Looking…", "Searching the shelves…",
"Scanning…", "Sniffing…", "Reaching…", "Hmm hmm…", "Floating through…").

The mockup art is a guide; the XAML redraw should keep each character's colours, shape, props,
and moves. The owner plans to edit a couple of them later.

## How it works

```text
Ctrl+Alt+Space ─► GlobalHotKey (App) ─► QuickSearchWindow shows, focuses the box
typed words ──► QuickSearchViewModel (Presentation) ──► QuickSearchService (Core)
                  (150 ms: names)                          ├► FileSearchService + index (read-only)
                  (600 ms: inside)                         └► ContentSearchService (read-only,
                                                               existing permissions and limits)
rows + mood ◄──────────────────────────────────────────────┘
Enter ─► QuickSearchViewModel ─► IFileLauncher (Core contract)
                                   └► WindowsFileLauncher (Infrastructure):
                                        re-check live path ─► open with its usual app
                                                           or Explorer with the file selected
```

### Core (`DeskAI.Core`, no Windows types)

- `SearchBuddy` enum: Sparky, Archie, Pip, Fetch, Inky, Mochi, Paige.
- `SearchBuddyLines`: pure; the lines above per buddy and mood. Tested for length, singular and
  plural, and banned words.
- `QuickSearchSettings` (IsOn, Buddy, TipDismissed) read and written through the existing
  `IAppSettingsStore` under `quicksearch.on`, `quicksearch.buddy`, `quicksearch.tip.dismissed`.
  Missing keys mean on, Sparky, not dismissed. **Start fresh** removes them.
- `FileOpenRule`: decides **Open** versus **Show in folder only** from the file's extension. The
  allow-list: `.txt .md .rtf .csv .pdf .docx .xlsx .pptx .odt .ods .odp .jpg .jpeg .png .gif
  .webp .bmp .heic .mp3 .wav .m4a .flac .mp4 .mov .mkv .avi .zip`. Everything else, including
  macro-enabled and older Office files (`.docm .xlsm .pptm .doc .xls .ppt`), web pages, programs,
  scripts, shortcuts, and files with no extension, is Show in folder only. The **last** extension
  counts ("invoice.pdf.exe" is a program).
- `QuickSearchService`: two read-only calls. `FindByNameAsync` wraps `FileSearchService` (all
  connected, searchable folders) and returns at most 5 rows plus the facts: no folders, nothing
  understood, nothing matched, more than 5, and whether any folder allows reading inside.
  `FindInsideAsync` wraps `ContentSearchService.SearchAsync` (never the OCR pass) and returns at
  most 5 matched rows not already listed by name, plus files read and whether a limit was
  reached. Each row: root ID, relative path, name, where-text, kind, the `FileOpenRule` answer,
  and for inside rows the snippet and section. No filesystem access of its own. It stores
  nothing.
- `ContentHit` gains the folder's `RootId` (today it carries only the folder's name), so an inside
  row can be opened through the same checks as a name row. Search's page ignores it.
- `IFileLauncher` contract: `OpenAsync(Guid rootId, string relativePath)` and
  `ShowInFolderAsync(Guid rootId, string relativePath)`, each returning a plain result
  (done, or refused with a reason).

### Infrastructure (`DeskAI.Infrastructure`)

- `WindowsFileLauncher : IFileLauncher`. Before either action it checks the **live** file, not the
  index: the root is still connected and allowed; the path is resolved and normalised and is still
  inside the root; no part of the path is a link or reparse point; the path is not protected; the
  file exists and is a regular file; and for Open, `FileOpenRule` says Open on the **live** name.
  Any failure → refused with a plain reason, nothing launched.
- Open: the Windows shell's "open" action on that one validated file path (its usual app). Show in
  folder: File Explorer with `/select,` and the quoted validated path. Both go through a small
  internal process-starting seam so tests never start a real process.

### Presentation (`DeskAI.Presentation`, no WinUI)

- `QuickSearchViewModel`: phrase, name rows, inside rows, selected row, buddy, mood, buddy line,
  fact lines, examples; `OpenSelectedAsync`, `ShowInFolderAsync`, `PetBuddy`, `SeeMoreInDeskAI`
  (asks the main window to open Search with the phrase). Two debounces (names, inside) sharing one
  cancellation per keystroke; hiding the bar cancels too.
- The Quick search card's view model on My workspace (switch, buddy tiles, shortcut problem line).
- Tip state for Home and Search.
- `WelcomeViewModel.Pages` gains the new page.

### App (`DeskAI.App`)

- `GlobalHotKey`: registers Ctrl + Alt + Space with `RegisterHotKey` (with no-repeat) on a hidden
  message window while quick search is on, and unregisters when it is turned off or DeskAI quits.
  Reports a refusal to the settings card. This is not a keyboard hook: Windows tells DeskAI only
  about this one combination and nothing else that is typed.
- `QuickSearchWindow`: borderless WinUI window, no title bar, not resizable, always on top while
  shown, tool-window style (not in the taskbar or Alt + Tab), placed as described above.
- Buddies: `Views/Buddies/SparkyBuddy.xaml`, `ArchieBuddy.xaml`, `PipBuddy.xaml`,
  `FetchBuddy.xaml`, `InkyBuddy.xaml`, `MochiBuddy.xaml`, `PaigeBuddy.xaml`. Each is a
  `UserControl` with a `Mood` property that moves it between its visual states. They are
  decorative for screen readers; the bubble's line is a polite live region.
- The icon near the clock and closing the window follow "The icon near the clock" above:
  `BackgroundPresenceController` learns about quick search, and `KeepsRunningWhenClosed` becomes
  "checking in background **or** quick search on".

## Safety

A new ADR (next free number, 0047) and a dated security review record this before code. It amends
ADR 0025 (what closing the window means) and adds DeskAI's first "open a file" action.

- **Opening a file is new.** DeskAI never opened files before. Only the person's own press opens
  one, only a file in a connected folder, only an allow-listed type, and only after the live
  checks above. Programs, scripts, and shortcuts are never started. Explorer is started only to
  show a validated file.
- **AI containment.** No AI is used by quick search, and nothing is sent anywhere. The AI code
  never receives `IFileLauncher`, the hotkey, or the window; a reflection test in the same style
  as the existing containment tests checks that `IFileLauncher` is held only by
  `QuickSearchViewModel`.
- **Threats and answers:**
  - A program disguised as a document ("report.pdf.exe") → the last extension decides; Show in
    folder only.
  - The file changed since the index saw it (swapped for a link, moved outside, renamed to .exe) →
    the live checks run at the moment of opening; refused with the reason.
  - A path outside the connected folder through `..`, a link, or a junction → normalisation and
    the reparse-point check refuse it.
  - A protected location → refused.
  - Time between the check and the open → accepted and written down: the file is the person's
    own, in a folder they connected, opened by their own press, with an allow-listed type.
  - Windows' own choice of app for a type is the person's Windows setting and out of scope.
  - The shortcut is not a key logger (see GlobalHotKey).
- **Reading inside files from a pop-up.** It adds no new reading power: the bar calls the same
  `ContentSearchService` Search uses, with the same per-folder, PDF, and slide permissions, the same
  path checks in the text extractors, the same PDF helper process, and the same 50-file and
  20-second limits. New risks and answers:
  - Many looks while typing → only after a 600 ms pause, one at a time, cancelled by the next
    keystroke or by hiding the bar.
  - Words from inside a file shown over another app (someone looking over the shoulder) → only
    after the person's own shortcut press, only a short piece per row, and gone when the bar hides.
    This is the same text Search shows.
  - A snippet is untrusted text → shown as plain text only, like a file name, never as markup or a
    link.
  - The bar granting reading without the person's knowledge → it cannot; it has no permission
    setter, and a test checks that `QuickSearchViewModel` holds no permission-changing service.
- **Privacy.** Typed words, snippets, and results are not saved, not logged, and not sent. Logs
  never contain the phrase, file names, or snippets.
- **Staying near the clock.** DeskAI staying after its window closes is now normal while quick
  search is on. It does no checking or tidying unless that was separately turned on. Every text
  that says what closing DeskAI does must stay true: the "Keep running after you close it" help,
  the Automatic tasks lines, `BackgroundCheckingChoice`'s dialog, and the Settings message about
  stopping. Each needs checking and updating in the same change.

## Testing

Generated data only; no real folders, no real hotkey, no real process started.

- **Page tests** (`DeskAI.Presentation.Tests`, new `QuickSearchPageTests`), each used the way a
  person uses it: the empty bar shows the buddy's greeting and the examples; clicking an example
  searches; typing finds a generated file in a connected temp folder; more than 5 by name → the
  "first 5" line; no folders → the connect line; gibberish → the "try a name" line; a word written
  inside a generated text file in a folder that allows reading inside appears under Words inside
  with its piece of text, and can be opened; the same folder without that permission → the "allow
  it on the Search page" line and no file is read; a PDF in a folder without PDF reading is not
  read; a file already listed by name is not listed again; typing again cancels a running inside
  look (a slow fake reader); hiding the bar cancels it; 2-letter words start no inside look; the
  limit line when 50 files were read; Enter on a `.pdf` calls
  the fake launcher's Open; Enter on a `.exe` and on "x.pdf.exe" calls Show in folder; a refusal
  shows its reason and keeps the bar; Ctrl + Enter shows in folder; each of the seven buddies gives
  its own greeting, found, and nothing lines; clicking the buddy gives Happy then returns; nothing
  is saved after a search (no new `app_settings` rows); Look inside files opens Search with the
  phrase.
- **Settings page tests:** the switch turns the fake hotkey off and on; the buddy choice is saved
  and survives reopening; the "another program uses it" line appears when the fake hotkey refuses;
  Start fresh resets quick search.
- **Welcome and tip tests:** the new welcome page, page count and dots; the tip shows on Home and
  Search, closing it hides it on both and after reopening, and it is hidden while quick search is
  off.
- **Icon near the clock tests:** with quick search on and checking off, closing hides the window
  and the icon shows the quick search tooltip and no Pause checking; with both off, closing quits;
  with checking on, today's behaviour stays.
- **Layout tests** reading the XAML: the bar is top-centre, the buddy is on its top edge, the
  bubble is a live region, every buddy file has the five moods, and reduced motion is honoured.
- **Core tests:** `FileOpenRule` (allow-list, last extension, no extension, upper case),
  `SearchBuddyLines` (length, singular, banned words), `QuickSearchService` (limit, facts).
- **Infrastructure tests:** `WindowsFileLauncher` refuses a link, a junction, a path outside the
  root, a missing file, a protected path, and a live name that became `.exe`, and asks the fake
  process starter for exactly one validated path otherwise.
- **Containment test:** only `QuickSearchViewModel` holds `IFileLauncher`; no AI type does;
  `QuickSearchViewModel` and `QuickSearchService` hold no permission-changing service, AI
  connection, executor, or writer.
- Rows in the Feature Coverage Map in `docs/TESTING.md`; a manual check in
  `docs/MANUAL-TESTING.md` using the UI preview build's generated folders.

## Suggested build order (for the plan)

1. ADR 0047 and the security review.
2. Core: `FileOpenRule`, `SearchBuddy` and lines, settings, `ContentHit.RootId`,
   `QuickSearchService` (by name, then inside).
3. Infrastructure: `WindowsFileLauncher` and its checks.
4. Presentation: `QuickSearchViewModel`, the settings card, the tip, the welcome page.
5. App: the hotkey, the window, the icon near the clock and closing; Sparky's art first.
6. The other six buddies' art (can be one task each, so the owner can check them one by one).
7. Docs (README, user guide, UI-UX, SECURITY, TESTING, MANUAL-TESTING, release notes, handoff) and
   a fresh whole-change review.

## Not in this feature

AI in the bar; giving reading permissions from the bar; the scanned PDF words reader (OCR) and
picture reading from the bar; the "Files checked" list in the bar; a choice of shortcut keys;
sounds; voice; the
buddy inside DeskAI's own window; starting DeskAI with Windows; search history or recent files;
opening folders (the index holds files only).
