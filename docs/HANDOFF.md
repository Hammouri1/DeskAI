# Handoff

This file exists so a brand-new chat can pick DeskAI up cold. The owner clears the
conversation after each roadmap version to keep it cheap, so nothing important may live only
in a chat. If it matters after this version, it is written down here or in `docs/`.

**If you are a new session: read this file first, then `AGENTS.md`, then the documents it
names. Do not start a milestone unasked — ask the owner which of "What is left" comes next.**

Keep this file short. It records state and open threads, not history: finished work belongs in
`ROADMAP.md`, decisions in `docs/decisions/`, reviews in `docs/security/`, and manual checks in
`docs/MANUAL-TESTING.md`. Rewrite the sections below at the end of every version rather than
appending to them.

---

## Where things stand

- Updated: 2026-09-16, after commit `6eab611` plus the tile-width fix committed with this file,
  tree clean,
  branch `v0.7-workspace-profiles`, started from `v0.5-background-checking`. **Neither branch
  is merged to `main` yet.**
- **V0.1–V0.7 complete in code and automated tests.** V0.7 closed on 2026-09-16 with five
  commits in one session: the recovery fix for folder-only journal records (`b81e803`), folder
  templates (`86bc7b3`, `70a04b5`, ADR 0027), DeskAI's look (`c5d69e2`, ADR 0028), and desktop
  and wallpaper (`6eab611`, ADR 0029). Each piece has a page test class, Feature Coverage Map
  rows, a manual list, and docs, and C and E each have a security review in `docs/security/`.
- **The owner has not yet seen V0.7 running.** A and B were built while they were away; C, D,
  and E were built while they answered scope questions only. Every V0.7 manual list is
  therefore unreported.
- Verification at the end of V0.7: Release build zero warnings, **1144 tests pass, none
  skipped**, `dotnet format` clean.
- V0.8 "Extensibility and Distribution" and V0.9 "Tidy While I'm Away" remain **Future, not
  started**. The owner said on 2026-09-16 they want to finish the whole roadmap; they were told
  V0.8 and V0.9 are each multi-day versions and V0.9 needs its own security review before any
  code. V1.0 "Stable Release" is Planned.

## What is left

0. **NEXT, chosen by the owner on 2026-09-16 after seeing My workspace running: a visual
   redesign and a naming pass across the whole app.** The owner showed a reference (a dark,
   professional "command center" dashboard: a left navigation pane with small grouped section
   labels, a top bar with a search box, a welcoming hero card, a row of soft pastel stat tiles
   each with an icon and one big number, and two-column panels of rounded cards with date
   captions and small status pills) and said, in their words: "i want my system to look
   professional like this, i am not asking you to just copy and paste the design but be creative
   and also make sure that all the screens and features are named in a simple way so normal
   users feel that they can use the system easily." How to do it:
   - Start with `superpowers:brainstorming` and a design document under
     `docs/superpowers/specs/` **before any XAML**, and get the owner's yes on it. Show the
     owner what the changes look like in plain words per page (what they will see), not in
     style-token names. Ask questions in plain words (see "How to ask the owner things" below).
   - Keep every rule in `docs/UI-UX.md`: the accent means "safe or confirmed" and is never
     decoration; colour never carries state alone; the scope reminder in the pane stays truthful;
     no page implies a capability that does not exist; the four looks keep working (a redesign
     must go through the same theme brushes, and `DeskLookCatalogTests` must still pass).
     Pastel stat tiles like the reference's are fine for *counts* (files, folders, searches) but
     must not use the accent green for anything that is not a permission or a confirmation.
   - The naming pass covers the side menu ("Home", "Organize", "Search", "Automatic tasks", "My
     workspace", "Privacy and AI"), every section title, every button, and every dialog title.
     Candidates to test on the owner: "Organize" → "Tidy up", "Automatic tasks" → "Checks that
     run for you", "Privacy and AI" → "Privacy", "Starter packs" → "Ready-made setups", "Folder
     templates" → "Make folders". Whatever is chosen, `HelpCatalog` titles, `MANUAL-TESTING.md`,
     `UI-UX.md`, the Feature Coverage Map, and every page test that asserts a label must change
     in the same commit. `HelpCatalog.BannedWords` still applies.
   - Suggested order, one commit each with page tests where a test can see it: (1) the shell —
     pane sections, top bar, one type ramp; (2) Home as the hero-plus-stat-tiles page; (3) the
     other pages one at a time, My workspace last since it was just built. Every page has a
     manual list; add "looks the way the design says" steps to each.
   - **Fixed on 2026-09-16 while looking at the owner's screenshot:** pinned-search and look
     tiles were 216 wide and cut "Unpin" off as "Unpir". Widened to 268 (`WorkspacePage.xaml`).
     A page test cannot see a layout clip, so the check is a manual step under "My workspace".
     Expect more of these when the owner walks the V0.7 lists.
1. **The owner's look at V0.7**, in `docs/MANUAL-TESTING.md`: the lists "My workspace", "Folder
   templates", "DeskAI's look", and "Desktop and wallpaper". The dialog, keyboard, Narrator,
   theme, and high-contrast checks there are not covered by any automated test, and neither is
   the real `SystemParametersInfo` wallpaper call (tests use a recording setter by design) nor
   the real repaint of theme brushes in place. **Those two are the riskiest untested spots:** if
   choosing a look does not repaint the window, or the wallpaper does not change, that is a bug
   to fix first, with a page test where one is possible.
2. **The owner's older sign-off lists still outstanding**: "V0.6 sign-off", and steps 2, 9, and
   19–24 of "Checking after the window is closed".
3. **Merging `v0.5-background-checking` and `v0.7-workspace-profiles` into `main`**, if the owner
   wants that as its own step. Nothing has been merged since V0.4.
4. **The next version, only when the owner picks it.** The recommended order is V0.8 before
   V0.9, because V0.9 is the largest increase in what DeskAI is trusted to do and its security
   review is a precondition rather than a step. If the owner wants V0.9 first, write the review
   (standing approval, invalidation, the unattended ceiling, undo-first) before any code, as
   `ROADMAP.md` says.
5. **Deferred inside V0.7, recorded in `ROADMAP.md`:** shortcut and icon suggestions, desktop
   layout previews, and local image generation. Each needs new executor commands or a new
   picture source and its own review. Do not start them under V0.7's name.

## Decisions made in conversation, not yet recorded elsewhere

- From V0.5: the tray icon's menu holds **exactly three items** (Open DeskAI, Pause checking,
  Quit DeskAI); "Check now" there was rejected. The icon appears **when the setting is turned
  on**, not when the window is later closed. `Shell_NotifyIcon` through a small adapter was
  approved over a NuGet package.
- From V0.7, all now in ADRs 0026–0029 but repeated because they shape what comes next: V0.7 is
  built one piece at a time, safest first; My workspace is the page later pieces join; there is
  no Custom pack; templates reuse Allow tidying, make one level only, and accept typed names;
  looks never change the accent; the wallpaper picture is the one Windows setting DeskAI
  changes.
- **How to ask the owner things:** on 2026-09-16 a scope question written in project terms
  ("piece E", "security gate", "executor commands") got the answer "didn't understand the
  question"; the same question rewritten as what DeskAI would be allowed to do on their computer
  was answered at once. Ask in plain words about what the person will see and what DeskAI may
  touch, not in roadmap or architecture vocabulary.

## What a new chat must know

- `AGENTS.md` is the main instruction file; `CLAUDE.md` adds the workflow. If documents
  conflict, `docs/SECURITY.md` wins.
- The permanent rule: AI decides what it recommends, deterministic code decides what is
  allowed to happen. An automatic check holds no executor, neither Workspace service holds one,
  and the wallpaper setter is held only by `WallpaperService`; reflection tests fail if that
  changes.
- `FolderTidyExecutor` is the only code in DeskAI that moves a file or makes or removes a folder.
  Folder templates go through it too.
- The wallpaper picture is the only Windows setting DeskAI can change, only from the My
  workspace button, only to a picture the person picked in the Windows file dialog.
- Anything a person can see or do needs a page test in `DeskAI.Presentation.Tests` and a row
  in the Feature Coverage Map in `docs/TESTING.md`. A bug found by hand gets a test that fails
  before the fix.
- Tests and development never touch real personal folders, the real wallpaper, or the real
  Desktop. `TestApp` replaces the credential vault, the internet, notifications, the tray, the
  window painter, the wallpaper setter, and the known Desktop folder, and asserts the test
  Desktop is inside its own temp folder.
- The app is unpackaged WinUI 3 (`WindowsPackageType=None`), self-contained Windows App SDK,
  x64, `net10.0-windows10.0.26100.0`; libraries target `net10.0`. SQLite schema version is
  still 13: V0.7 pieces D and E store their settings in the `app_settings` key/value table
  that has existed since schema 1.
- Verification:

  ```powershell
  dotnet build DeskAI.sln -c Release --no-restore
  dotnet test DeskAI.sln -c Release --no-build --no-restore
  dotnet format DeskAI.sln --no-restore --verify-no-changes
  ```

- Launchable app after a Release build:
  `src\DeskAI.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`

## Known limits worth repeating

- Nothing moves a file without a preview and an approval, and there is no path from a rule, an
  automatic check, a starter pack, or a template straight to a move. Pack rules arrive
  switched off. A template only makes empty folders.
- AI never sees locations, folder names, file contents, or DeskAI's file IDs — at most type,
  size and date, and name, each only if allowed, and only after a dialog showing the request.
  AI plays no part in templates, looks, wallpaper, or the Desktop shortcut.
- PDF and Office files are refused before opening; content reading is plain text only, 64 KB.
- No permanent automatic deletion anywhere. Template undo removes only empty folders DeskAI
  made.
- Background checking only ever produces a count. Unattended tidying is V0.9, not started.
- A starter pack cannot be removed as a unit; its searches and rules are deleted one by one.
- Put back restores the wallpaper picture only; a Windows slideshow or Spotlight that was on
  does not come back. Tidying the Desktop leaves shortcuts where they are.

---

## How to update this file

At the end of every roadmap version, before the owner clears the chat:

1. Rewrite "Where things stand", "What is left", and "Decisions made in conversation, not yet
   recorded elsewhere" to match reality — including the commit and the date.
2. Move anything finished out of here and into `ROADMAP.md`.
3. Write down every decision the owner made in conversation that is not yet in code, an ADR,
   or a design document. A decision that exists only in the cleared chat is lost.
4. Commit this file with the version's closing change.
