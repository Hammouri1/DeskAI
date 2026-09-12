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

- Updated: 2026-09-12, at commit `4da8d60`, branch `main`, tree clean.
- V0.1–V0.4 complete. V0.4's last piece, confirming duplicates by reading files after the
  person agrees each time, landed 2026-09-11 (ADR 0024).
- V0.5 complete **except** checking after the window is closed.
- V0.6 "Organize Your Own Folders" complete in code and automated tests (2026-09-11;
  ADR 0019–0023, review `docs/security/2026-09-11-v0.6-milestone-review.md`).
- V0.7 is marked Future. Nothing in it is started.

## What is left

1. **Checks after the window is closed** (V0.5, the one open code item). ADR 0017 decided the
   mode and deliberately left it unbuildable: `AutomaticCheckMode.InBackground` exists in
   `DeskAI.Core/Rules/AutomaticCheckSettings.cs`, no code produces it, and it is absent from
   the UI. `docs/SECURITY.md` requires its own focused review before it ships, because a
   process running while nobody is present is a different threat case.
2. **The owner's "V0.6 sign-off" list** in `docs/MANUAL-TESTING.md` — dialogs, keyboard and
   screen-reader use, a real crash, two windows. Only the owner can do these; they are not a
   coding task.
3. **V0.7**, only if the owner asks for it.

## Decisions already agreed but not yet built

Recorded here so a new chat does not re-ask. Agreed with the owner on 2026-09-12 while
designing item 1 above:

- "After the window is closed" means the same DeskAI keeps running with a visible tray icon
  until sign-out or restart. It does **not** add itself to Windows startup.
- The dialog that turns background checking on also shows the notification switch — still off
  unless turned on — and says plainly that with it off a find is only seen on reopening.
- Launching DeskAI again while it is hidden reveals the running one and exits the second
  launch; it never starts a second DeskAI and never silently quits the first.
- Preferred mechanism: `Shell_NotifyIcon` through a small adapter in `DeskAI.App`, no new
  package. Not yet approved as part of a full design.
- The honest limit to state in the design and the UI: a check produces a count and a notice
  and cannot move a file, so background checking only keeps the count current. "Tidy while I
  am away" is not in V0.5, not in V0.6, and not on the roadmap.

No design document or ADR has been written for this yet. The next step for item 1 is the
design and security review, committed before any code.

## What a new chat must know

- `AGENTS.md` is the main instruction file; `CLAUDE.md` adds the workflow. If documents
  conflict, `docs/SECURITY.md` wins.
- The permanent rule: AI decides what it recommends, deterministic code decides what is
  allowed to happen. An automatic check holds no executor and a reflection test fails if one
  is added.
- `FolderTidyExecutor` is the only code in DeskAI that moves a file. The practice page and its
  executor were removed in ADR 0023.
- Anything a person can see or do needs a page test in `DeskAI.Presentation.Tests` and a row
  in the Feature Coverage Map in `docs/TESTING.md`. Engine tests alone do not finish a
  feature. A bug found by hand gets a test that fails before the fix.
- Tests and development never touch real personal folders. Generated files in temporary
  directories only.
- The app is unpackaged WinUI 3 (`WindowsPackageType=None`), self-contained Windows App SDK,
  x64, `net10.0-windows10.0.26100.0`; libraries target `net10.0`.
- Verification:

  ```powershell
  dotnet build DeskAI.sln -c Release --no-restore
  dotnet test DeskAI.sln -c Release --no-build --no-restore
  dotnet format DeskAI.sln --no-restore --verify-no-changes
  ```

- Launchable app after a Release build:
  `src\DeskAI.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`

## Known limits worth repeating

- Nothing moves a file without a preview and an approval, and there is no path from a rule or
  an automatic check straight to a move.
- AI never sees locations, folder names, file contents, or DeskAI's file IDs — at most type,
  size and date, and name, each only if allowed, and only after a dialog showing the request.
- PDF and Office files are refused before opening; content reading is plain text only, 64 KB.
- No permanent automatic deletion anywhere.

---

## How to update this file

At the end of every roadmap version, before the owner clears the chat:

1. Rewrite "Where things stand", "What is left", and "Decisions already agreed but not yet
   built" to match reality — including the commit and the date.
2. Move anything finished out of here and into `ROADMAP.md`.
3. Write down every decision the owner made in conversation that is not yet in code, an ADR,
   or a design document. A decision that exists only in the cleared chat is lost.
4. Commit this file with the version's closing change.
