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

- Updated: 2026-09-16, after commit `a09fc47` on `main`, tree clean. `v0.5-background-checking`
  and `v0.7-workspace-profiles` are **merged into `main`** (merge commit `0530a05`); work
  continues on `main` or a new branch from it. Nothing has been pushed to GitHub from this
  checkout yet.
- **The whole roadmap is complete in code, tests, and documents: V0.1–V1.0.** On 2026-09-16 one
  session did, in order, each with its own commit(s): the command-center redesign of every page
  (`docs/superpowers/specs/2026-09-16-command-center-redesign-design.md`), V0.8 (ADR 0030,
  `docs/security/2026-09-16-v0.8-privacy-review.md`), V0.9 (ADR 0031,
  `docs/security/2026-09-16-tidy-while-away-review.md`, written before code), and V1.0
  (`docs/security/2026-09-16-v1.0-release-review.md`, `LICENSE` (MIT), `CONTRIBUTING.md`, root
  `SECURITY.md`, `docs/USER-GUIDE.md`, `docs/INSTALL.md`, `docs/RELEASE-NOTES.md`). Version is
  `1.0.0` in `Directory.Build.props`.
- Verification at the end, with the owner's DeskAI closed: full Release build of
  `DeskAI.sln`; **1192 tests pass, none skipped**; `dotnet format` clean; the launchable exe is
  at the usual path below (built 2026-09-16 14:02). While the owner's own `DeskAI.App.exe` is
  open, the App's final copy step fails with locked DLLs; compile it into a scratch folder then
  (`dotnet build src/DeskAI.App -c Release -p:OutDir=<somewhere else>`) and ask them to close it
  for the real build. Three analyzer warnings from before this session remain (see item 5).
- **The owner has not yet reported on anything from V0.7 onward.** They were given the exe at
  the end of the session and asked to start with Home, the away switch on Organize, and the
  backup and Start fresh cards; every manual list from "My workspace" to "A release zip" in
  `MANUAL-TESTING.md` is unreported. The riskiest
  untested-by-hand spots: the real theme repaint and the real wallpaper call (V0.7), the away
  switch's dialog and the notice with the window closed (V0.9), and the release workflow, which
  has never run on GitHub (no push or tag has been made from this machine's checkout).
- GitHub Actions: `.github/workflows/build.yml` and `release.yml` exist but are unverified until
  the repository is pushed. The release uses `softprops/action-gh-release@v2` and the
  `CycloneDX` .NET tool; if either is unavailable, the workflow fails visibly rather than
  publishing a partial release.

## What is left

1. **The owner's look at everything since V0.6**, in `docs/MANUAL-TESTING.md`: "The
   command-center look: every page / Home / the other pages", "Back up, restore, and Start
   fresh", "A release zip", "Tidy while I'm away", plus the older "My workspace", "Folder
   templates", "DeskAI's look", "Desktop and wallpaper", "V0.6 sign-off", and steps 2, 9, 19–24
   of "Checking after the window is closed". Any bug found by hand gets a page test that fails
   first. Expect layout clips like the 2026-09-16 "Unpin" one; a page test cannot see those.
2. **Push and tag.** `git push origin main`, watch the build workflow go green, then
   `git tag v1.0.0 && git push origin v1.0.0` and check the Release has the zip and the SBOM.
   Unzip it on a clean account and walk "A release zip".
3. **Code signing** when a certificate exists (ADR 0030 names SignPath and Azure Trusted Signing).
4. **Deferred by decision, not to start unasked:** add-ons (ADR 0030 records the boundary they
   must start from), localization beyond English, shortcut and icon suggestions, desktop layout
   previews, local image generation, "keep both" unattended.
5. **Small things noticed and left:** three pre-existing analyzer warnings (CA1716 on
   `IWallpaperSetter.Set`, CA1870 in `FolderNameCheck`, CA1838 in `WindowsDesktop`) appear on a
   full rebuild; they were not introduced this session and were left alone to keep the diff
   honest. Renaming `Set` touches a reviewed contract (ADR 0029), so do it as its own small
   commit with the wallpaper tests. `docs/PERFORMANCE.md` has one recorded run; add one after
   any change to scanning.

## Decisions made in conversation, not yet recorded elsewhere

All of 2026-09-16's decisions are in ADR 0030, ADR 0031, and the three design documents. The
ones worth repeating because they shape what comes next:

- **Menu names stay** (Home, Organize, Search, Automatic tasks, My workspace, Privacy and AI).
  Simpler names were offered and declined; the naming pass became grouping plus pills.
- **Distribution is a zip on GitHub Releases, unsigned for now, no installer, no self-update.**
- **Add-ons come "after we finish building the system"**, and DeskAI loads no outside code.
- **Tidy while I'm away is "move a few, then wait"**: rule-placed files only, 25 per run, stop on
  anything unexpected, undo first. "Only tell me" and "move everything" were the rejected options.
- **License is MIT**, the owner's "for now yes"; the copyright line names the owner and
  contributors.
- **Merge into main at the end** was agreed as part of the plan.
- **How to ask the owner things:** in plain words about what they will see and what DeskAI may
  touch, never in roadmap or architecture vocabulary. Batched questions worked well this session
  (four at once, each with a recommended option).

## What a new chat must know

- `AGENTS.md` is the main instruction file; `CLAUDE.md` adds the workflow. If documents
  conflict, `docs/SECURITY.md` wins.
- The permanent rule: AI decides what it recommends, deterministic code decides what is
  allowed to happen. `FolderTidyExecutor` is the only code that moves a file or makes or removes
  a folder. An automatic check holds no executor; the coordinator holds `IAwayTidyRunner`, which
  `AwayTidyService` alone implements, and that is the one unattended path (25 rule-placed files
  per run, stop on anything unexpected). The wallpaper setter is held only by `WallpaperService`.
  Reflection tests fail if any of that changes.
- Every "nothing moves by itself" sentence in the app reads `AwayTidyService.CountActiveAsync`
  through `AwayTidyWords`; do not hard-code that promise anywhere again.
- The backup file holds rules and saved searches only; restored rules arrive off. Start fresh
  erases DeskAI's memory and touches no file.
- Anything a person can see or do needs a page test in `DeskAI.Presentation.Tests` and a row
  in the Feature Coverage Map in `docs/TESTING.md`. Tests read the XAML for layout rules
  (`ShellLayoutTests`, `AccessibilityNameTests`, `NoPlaceholderUiTests`, `HelpPlacementTests`).
- Tests and development never touch real personal folders, the real wallpaper, or the real
  Desktop. `TestApp` replaces the credential vault, the internet, notifications, the tray, the
  window painter, the wallpaper setter, and the known Desktop folder.
- The app is unpackaged WinUI 3, self-contained Windows App SDK, x64,
  `net10.0-windows10.0.26100.0`; libraries target `net10.0`. SQLite schema version is **14**
  (V0.9 added `away_tidy` and `away_tidy_runs`).
- Verification:

  ```powershell
  dotnet build DeskAI.sln -c Release --no-restore
  dotnet test --solution DeskAI.sln -c Release --no-build --no-restore
  dotnet format DeskAI.sln --no-restore --verify-no-changes
  ```

- Launchable app after a Release build:
  `src\DeskAI.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`

## Known limits worth repeating

- Nothing moves a file without a preview and an approval, except an unattended run under a
  standing yes that a person gave on Organize for that folder, bounded as above and undoable.
- AI never sees locations, folder names, file contents, or DeskAI's file IDs — at most type,
  size and date, and name, each only if allowed, and only after a dialog showing the request.
  AI plays no part in templates, looks, wallpaper, the Desktop shortcut, backups, or away runs.
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
