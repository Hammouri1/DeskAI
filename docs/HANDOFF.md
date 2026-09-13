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

- Updated: 2026-09-13, at commit `98f436f`, branch `v0.5-background-checking` (not merged to
  `main` yet), tree clean.
- V0.1–V0.4 complete. V0.4's last piece, confirming duplicates by reading files after the
  person agrees each time, landed 2026-09-11 (ADR 0024).
- **V0.5 is now complete**, including checking after the window is closed (ADR 0025, review
  `docs/security/2026-09-12-background-checking-review.md`). The work landed on branch
  `v0.5-background-checking`, not directly on `main`.
- V0.6 "Organize Your Own Folders" complete in code and automated tests (2026-09-11;
  ADR 0019–0023, review `docs/security/2026-09-11-v0.6-milestone-review.md`).
- V0.7 is marked Future, not started. V0.9 "Tidy While I'm Away" is also marked Future, not
  started, and depends on the V0.5 item that just landed — it needs something running while
  nobody is present, which is what background checking builds.

## What is left

1. **The owner's manual sign-off**, in `docs/MANUAL-TESTING.md` — two lists, neither a coding
   task:
   - the outstanding "V0.6 sign-off" list (dialogs, keyboard and screen-reader use, a real
     crash, two windows);
   - the new "Checking after the window is closed" list this version added — the tray icon,
     window hiding, second launch, and Explorer restart are not covered by any automated
     test, only by a person at the keyboard.
2. **V0.7 or V0.9**, only if the owner asks for one of them next. Neither is started, and V0.9
   also needs its own precondition security review before any code, per its roadmap entry.
3. Merging `v0.5-background-checking` into `main`, if the owner wants that done as its own
   step.

## Decisions made in conversation, not yet recorded elsewhere

These came up while designing and building V0.5's last piece and exist nowhere else — not in
code, not in an ADR, not in a design document:

- The tray icon's menu holds **exactly three items**: Open DeskAI, Pause checking, Quit
  DeskAI. Adding a "Check now" to the menu was discussed and deliberately rejected — a check
  is harmless in what it may do, but starting one with no window open and no page to report
  the result is the exact shape the security review was written to be careful about.
- The icon appears **when the setting is turned on**, not when the window is later closed.
  The reasoning: turning the setting on is the moment something changed, so that is when the
  evidence should appear, while the person is still looking at the switch that caused it.
- `Shell_NotifyIcon`, called through a small adapter inside `DeskAI.App`, was approved over
  adding a new NuGet package for tray support.

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
  `src\DeskAI.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`

## Known limits worth repeating

- Nothing moves a file without a preview and an approval, and there is no path from a rule or
  an automatic check straight to a move.
- AI never sees locations, folder names, file contents, or DeskAI's file IDs — at most type,
  size and date, and name, each only if allowed, and only after a dialog showing the request.
- PDF and Office files are refused before opening; content reading is plain text only, 64 KB.
- No permanent automatic deletion anywhere.
- Background checking (V0.5) only ever produces a count. Leaving DeskAI running near the
  clock keeps that count current; it cannot tidy, move, rename, or delete anything while
  nobody is present. That is a much larger trust decision, tracked separately as V0.9 and not
  started.

---

## How to update this file

At the end of every roadmap version, before the owner clears the chat:

1. Rewrite "Where things stand", "What is left", and "Decisions made in conversation, not yet
   recorded elsewhere" to match reality — including the commit and the date.
2. Move anything finished out of here and into `ROADMAP.md`.
3. Write down every decision the owner made in conversation that is not yet in code, an ADR,
   or a design document. A decision that exists only in the cleared chat is lost.
4. Commit this file with the version's closing change.
