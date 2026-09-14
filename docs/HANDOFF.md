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

- Updated: 2026-09-14, branch `v0.7-workspace-profiles`, which was started from
  `v0.5-background-checking`. **Neither branch is merged to `main` yet.**
- V0.1–V0.6 complete in code and automated tests. V0.5's last piece, checking after the window
  is closed, landed 2026-09-13 (ADR 0025), with one follow-up fix `ab01694` (turning checking
  off and closing at once could leave DeskAI running with no icon).
- **V0.7 has started and is not finished.** It was split with the owner into five pieces by
  risk (design `docs/superpowers/specs/2026-09-14-my-workspace-starter-packs-design.md`, plan
  `docs/superpowers/plans/2026-09-14-my-workspace-starter-packs.md`):
  - **A + B done 2026-09-14 (ADR 0026): My workspace** — starter packs and pinned searches.
    Built while the owner was away, at their request; they have not yet seen it running.
  - C folder templates, D DeskAI's look, E desktop and wallpaper — not started.
- V0.9 "Tidy While I'm Away" remains Future, not started, and needs its own security review
  before any code.
- Verification at the end of A + B: Release build zero warnings, 1006 tests pass, none skipped,
  `dotnet format` clean.

## What is left

1. **The owner's look at My workspace**: the "My workspace" list in `docs/MANUAL-TESTING.md`,
   and reading the design document. The dialog, keyboard, Narrator, and theme checks there are
   not covered by any automated test.
2. **The owner's manual sign-off lists still outstanding**: "V0.6 sign-off", and the parts of
   "Checking after the window is closed" not yet reported. On 2026-09-13 the owner reported the
   tray icon and menu work ("eventually" — which step was slow is not pinned down) and that a
   check ran while the window was closed; steps 2, 9, and 19–24 are unconfirmed.
3. **The next V0.7 piece**, only when the owner picks one: C folder templates (creates folders —
   needs its own design and security review first), D DeskAI's own look, or E desktop and
   wallpaper (a `SECURITY.md` review gate). The recommended order was C, then D, then E.
4. Merging `v0.5-background-checking` and `v0.7-workspace-profiles` into `main`, if the owner
   wants that as its own step.

## Decisions made in conversation, not yet recorded elsewhere

- From V0.5: the tray icon's menu holds **exactly three items** (Open DeskAI, Pause checking,
  Quit DeskAI); "Check now" there was rejected. The icon appears **when the setting is turned
  on**, not when the window is later closed. `Shell_NotifyIcon` through a small adapter was
  approved over a NuGet package.
- From V0.7 (also in the design and ADR 0026, repeated here because they shape the next pieces):
  V0.7 is built one piece at a time, safest first; My workspace is the page later V0.7 pieces are
  expected to join; there is no Custom pack card.

## What a new chat must know

- `AGENTS.md` is the main instruction file; `CLAUDE.md` adds the workflow. If documents
  conflict, `docs/SECURITY.md` wins.
- The permanent rule: AI decides what it recommends, deterministic code decides what is
  allowed to happen. An automatic check holds no executor, and neither Workspace service may
  hold one; reflection tests fail if one is added.
- `FolderTidyExecutor` is the only code in DeskAI that moves a file.
- Anything a person can see or do needs a page test in `DeskAI.Presentation.Tests` and a row
  in the Feature Coverage Map in `docs/TESTING.md`. A bug found by hand gets a test that fails
  before the fix.
- Tests and development never touch real personal folders. Generated files in temporary
  directories only.
- The app is unpackaged WinUI 3 (`WindowsPackageType=None`), self-contained Windows App SDK,
  x64, `net10.0-windows10.0.26100.0`; libraries target `net10.0`. SQLite schema version is 13.
- Verification:

  ```powershell
  dotnet build DeskAI.sln -c Release --no-restore
  dotnet test --solution DeskAI.sln -c Release --no-build --no-restore
  dotnet format DeskAI.sln --no-restore --verify-no-changes
  ```

- Launchable app after a Release build:
  `src\DeskAI.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`

## Known limits worth repeating

- Nothing moves a file without a preview and an approval, and there is no path from a rule, an
  automatic check, or a starter pack straight to a move. Pack rules arrive switched off.
- AI never sees locations, folder names, file contents, or DeskAI's file IDs — at most type,
  size and date, and name, each only if allowed, and only after a dialog showing the request.
- PDF and Office files are refused before opening; content reading is plain text only, 64 KB.
- No permanent automatic deletion anywhere.
- Background checking only ever produces a count. Unattended tidying is V0.9, not started.
- A starter pack cannot be removed as a unit; its searches and rules are deleted one by one.

---

## How to update this file

At the end of every roadmap version, before the owner clears the chat:

1. Rewrite "Where things stand", "What is left", and "Decisions made in conversation, not yet
   recorded elsewhere" to match reality — including the commit and the date.
2. Move anything finished out of here and into `ROADMAP.md`.
3. Write down every decision the owner made in conversation that is not yet in code, an ADR,
   or a design document. A decision that exists only in the cleared chat is lost.
4. Commit this file with the version's closing change.
