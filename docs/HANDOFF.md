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

- Updated: 2026-09-16, after commit `00692f9` on `main` plus this handoff commit, tree clean.
  Nothing has been pushed to GitHub from this checkout yet; no tag exists.
- **V1.1 is complete in code, tests, and documents.** It came out of the owner's first look at
  V1.0 (two screenshots: a clipped tile, "This location is protected" on the Desktop) and their
  four complaints: the Desktop would not connect, no way to connect Downloads and the others,
  the UI needed fixing, and "no real use of the AI". Six commits in the agreed order:
  `f977673` Desktop bug, `4ec785c` tile clip, `275bad4` four-folder rule and Your folders card
  (ADR 0032), `39e6deb` AI reads a sentence (ADR 0033), `f3b8d27` Plan this folder (ADR 0034),
  `00692f9` Ask DeskAI (ADR 0035). `ROADMAP.md` has the V1.1 section; each AI feature has a
  security review in `docs/security/2026-09-16-*.md`.
- Verification at the end, with the owner's DeskAI closed: full Release build of `DeskAI.sln`
  with **no errors and no warnings** (the three pre-existing analyzer warnings were not
  touched: they are in files this version did not change — see item 4); **1344 tests pass, none
  skipped**; `dotnet format` clean. The launchable exe is at the usual path below, built
  2026-09-16 15:19. While the owner's own `DeskAI.App.exe` is open the App's final copy step
  fails with locked DLLs; compile into a scratch folder then
  (`dotnet build src/DeskAI.App -c Release -p:OutDir=<somewhere else>`) and ask them to close it
  for the real build.
- **The owner has not yet looked at anything from V0.7 onward, nor at V1.1.** Every manual list
  from "My workspace" through "Ask DeskAI" in `MANUAL-TESTING.md` is unreported. The riskiest
  spots by hand: the Your folders card connecting the real Desktop (their DeskAI lives on it —
  the whole point of the first fix), the four "Send this to …?" dialogs (Ask AI, Plan, Let AI
  read this, Ask DeskAI) against a real service, the real theme repaint and wallpaper (V0.7),
  the away switch (V0.9), and the release workflow, which has never run on GitHub.

## What is left

1. **The owner's look at V1.1, then V0.7–V1.0**, in `docs/MANUAL-TESTING.md`: start with "Your
   folders" (inside "Desktop and wallpaper", steps 9–14), "Let AI read this", "Plan this
   folder with AI", and "Ask DeskAI"; then the older lists named in the previous handoff
   ("The command-center look", "Back up, restore, and Start fresh", "A release zip", "Tidy while
   I'm away", "My workspace", "Folder templates", "DeskAI's look", "V0.6 sign-off", and
   "Checking after the window is closed" steps 2, 9, 19–24). Any bug found by hand gets a page
   test that fails first. Expect layout clips a page test cannot see (two were found on
   2026-09-16 that way).
2. **Decide the version number, then push and tag.** `Directory.Build.props` still says 1.0.0
   and `RELEASE-NOTES.md` has a "1.1 — not tagged yet" section. Either tag `v1.0.0` at `5b661e1`
   and `v1.1.0` at the head, or bump to 1.1.0 and tag once. Then `git push origin main`, watch
   the build workflow, tag, and check the Release has the zip and the SBOM. Unzip on a clean
   account and walk "A release zip".
3. **Possible follow-ups the owner may ask for after trying V1.1** (not started, each its own
   decision): a first-use-only dialog for Ask DeskAI instead of one per question (ADR 0035
   names this as the natural next step); size and date boxes on the rule form, so an AI-read
   sentence like "older than 90 days" lands in a box instead of being noted as left out; more
   question kinds for Ask DeskAI (duplicates, old files); a narrow-window layout for the Your
   folders rows on Home.
4. **Small things noticed and left:** three pre-existing analyzer warnings (CA1716 on
   `IWallpaperSetter.Set`, CA1870 in `FolderNameCheck`, CA1838 in `WindowsDesktop`) appear only
   on a full rebuild of untouched files and were left alone; renaming `Set` touches a reviewed
   contract (ADR 0029), so do it as its own small commit with the wallpaper tests.
   `docs/PERFORMANCE.md` has one recorded run; add one after any change to scanning. Code
   signing when a certificate exists (ADR 0030). Add-ons, localization, shortcut and icon
   suggestions, desktop layout previews, local image generation, and "keep both" unattended stay
   deferred by decision.

## Decisions made in conversation, not yet recorded elsewhere

All of 2026-09-16's V1.1 decisions are in ADR 0032–0035 and the three reviews. The ones worth
repeating because they shape what comes next:

- **"I don't want the software to touch the C: workspace or the main important data and system
  folders. Just let the user choose Desktop, Downloads, Documents, Pictures."** Built as a hard
  rule (ADR 0032), not a default: the Windows picker on Search and Organize stays, but a pick
  outside the four is refused in plain words. If the owner later wants another folder allowed,
  that is a change to `PersonalFolderPolicy` and its ADR, not a settings switch.
- **All three AI ideas were accepted at once** ("i liked those AI ideas go with them") in the
  order A (sentences), B (plan), C (ask). The shared design rule across them: AI returns a few
  typed facts; DeskAI turns them into its own words and does the work deterministically. Keep
  that shape for any further AI feature; do not add a free-text reply path.
- **A dialog before every AI request**, including every Ask DeskAI question, was kept as the
  safe default without asking the owner; it is the first thing to expect feedback on.
- **The "Ask the owner in plain words" rule** from the previous handoff worked again: one
  message with four numbered questions, each with a recommended option, got one reply that
  settled everything.

## What a new chat must know

- `AGENTS.md` is the main instruction file; `CLAUDE.md` adds the workflow. If documents
  conflict, `docs/SECURITY.md` wins.
- The permanent rule: AI decides what it recommends, deterministic code decides what is
  allowed to happen. `FolderTidyExecutor` is the only code that moves a file or makes or removes
  a folder. An automatic check holds no executor; the coordinator holds `IAwayTidyRunner`, which
  `AwayTidyService` alone implements, and that is the one unattended path (25 rule-placed files
  per run, stop on anything unexpected). The wallpaper setter is held only by `WallpaperService`.
  Reflection tests fail if any of that changes.
- **The AI connection (`IOrganizationSuggestionProvider`) has two calls:** `SuggestAsync` for
  files (Ask AI, and Plan this folder with `AiSuggestionTask.PlanFolder`) and `ReadSentenceAsync`
  for a typed sentence (Search, rules, Ask DeskAI). Every adapter implements both. Three
  services talk to it and each has a reflection test fixing what it may hold: `TidyAiService`,
  `SentenceAiService`, `AskDeskAiService`. AI output is read strictly (`StructuredSuggestionParser`,
  `AiSentenceReading`) and refused whole on anything off-shape; a planned folder name passes
  `FolderNameCheck` in the parser, the service, the path policy, and the executor.
- **Only the person's own four folders can be connected** (`PersonalFolderPolicy`, ADR 0032),
  checked at connection and again before tidying. In page tests the sandbox's folders root
  stands in for Documents, so every generated folder counts as inside a personal folder, and
  every test Desktop contains a protected "program folder", as the owner's does.
- A folder that *contains* a protected place connects with that part skipped; a folder *inside*
  one is refused (`WindowsPathPolicy.ValidateRoot` returns Warning versus Blocked).
- Every "nothing moves by itself" sentence in the app reads `AwayTidyService.CountActiveAsync`
  through `AwayTidyWords`; do not hard-code that promise anywhere again.
- Anything a person can see or do needs a page test in `DeskAI.Presentation.Tests` and a row in
  the Feature Coverage Map in `docs/TESTING.md`. Tests read the XAML for layout rules
  (`ShellLayoutTests`, `AccessibilityNameTests`, `NoPlaceholderUiTests`, `HelpPlacementTests`);
  every help topic has three parts with word limits (`HelpCatalogTests`).
- Tests and development never touch real personal folders, the real wallpaper, or the real
  Desktop. `TestApp` replaces the credential vault, the internet, notifications, the tray, the
  window painter, the wallpaper setter, and the four known folders.
- The app is unpackaged WinUI 3, self-contained Windows App SDK, x64,
  `net10.0-windows10.0.26100.0`; libraries target `net10.0`. SQLite schema version is **14**
  (unchanged by V1.1: nothing new is stored).
- Verification:

  ```powershell
  dotnet build DeskAI.sln -c Release --no-restore
  dotnet test DeskAI.sln -c Release --no-build --no-restore
  dotnet format DeskAI.sln --no-restore --verify-no-changes
  ```

- Launchable app after a Release build:
  `src\DeskAI.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`

## Known limits worth repeating

- Nothing moves a file without a preview and an approval, except an unattended run under a
  standing yes that a person gave on Organize for that folder, bounded as above and undoable.
- AI never sees locations, folder names, file contents, or DeskAI's file IDs — at most type,
  size and date, and name, each only if allowed, and only after a dialog showing the request. A
  sentence or question carries the typed words and today's date and nothing else. AI plays no
  part in templates, looks, wallpaper, backups, or away runs.
- AI never writes anything shown as fact: a category, a checked folder name, or a sentence in
  DeskAI's vocabulary is all it can produce, and every reply on screen is DeskAI's wording.
- PDF and Office files are refused before opening; content reading is plain text only, 64 KB.
- No permanent deletion anywhere. Template undo removes only empty folders DeskAI made.
- DeskAI never registers itself with Windows startup and never checks online for updates.
- Put back restores the wallpaper picture only. Tidying the Desktop leaves shortcuts alone.

---

## How to update this file

At the end of every roadmap version, before the owner clears the chat:

1. Rewrite "Where things stand", "What is left", and "Decisions made in conversation, not yet
   recorded elsewhere" to match reality — including the commit and the date.
2. Move anything finished out of here and into `ROADMAP.md`.
3. Write down every decision the owner made in conversation that is not yet in code, an ADR,
   or a design document. A decision that exists only in the cleared chat is lost.
4. Commit this file with the version's closing change.
