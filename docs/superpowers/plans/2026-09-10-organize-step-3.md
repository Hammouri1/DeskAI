# Organize Step 3 — Tidying for Real

**Goal:** Pressing "Tidy N files" moves exactly the ticked files in a folder the person allowed
DeskAI to tidy, re-checking each file right before it moves, skipping any file that is no
longer safe to move with a reason, and saying what happened in one line. The tidy just done
can be undone from the same place.

**Spec:** `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md` Part 1 steps
5–6 and Part 2 §§2–5, 7–8 (its threat table is the threat design). **Security review:**
`docs/security/2026-09-10-real-folder-tidy-review.md`, written before the code.

## Decisions

- **One mover, two ways of trusting a folder** (spec §8). The move, folder-creation, link,
  collision, journal, and undo rules move out of `TemporaryDemoPlanExecutor` into an internal
  `FileOperationRunner`. The practice executor keeps its public API and trusts its root by
  the ownership marker, as before. A new `FolderTidyExecutor` trusts a real folder by a live
  check before the run and before every file: still connected, `RootCapabilities.CanTidy`,
  same canonical path as when the run started, and `IReadOnlyFolderService.CheckStillSafeAsync`
  (exists, not network or whole drive, no link in its path, not protected).
- **Checked again right before each move:** the file is where it was, a regular file, not a
  link, with the size and last-changed time it had when the list was made; it is not now
  online-only, hidden, or system; the destination folder exists and is not a link; the
  destination name is free. The move itself refuses to overwrite, so a name taken in the last
  instant still fails safely. The configured protected-path policy is used, not a bare one.
- **Busy files** are found at the moment of moving: a sharing violation becomes "It's open in
  another program" for that file, and the rest continue.
- **Journal:** as before, every intended operation is written before the first change, with the
  file's size and last-changed time *from the list*; a file that changed since fails its own
  re-check, so the journal never records a move of a file nobody reviewed.
- **Undo ships with Tidy.** `SECURITY.md` makes undo part of the design, so real moves do not
  arrive without it. The result line offers Undo for the tidy just done. Undo needs the tidy
  permission (spec §1): if it was withdrawn, pressing Undo asks to allow it again. Undo moves a
  file back only if unchanged and its old spot is free, and removes only folders DeskAI created
  that are empty. Showing the last tidy after DeskAI is reopened, and the interrupted-tidy
  prompt, remain step 4.
- **Each executor refuses the other's work.** Practice undo refuses a record whose plan is not
  for its workspace; tidy undo refuses a record whose plan is not for a tidy-permitted folder.
  Practice recovery no longer touches a real folder's interrupted record — it leaves it for
  step 4 rather than marking it on a real folder's behalf.
- **One run at a time**, tidy or undo, across the app.
- **The approval is exactly what is ticked.** `TidyRunService` (Core) selects the ticked move
  operations plus only the folder-creation operations those moves need, binds the approval to
  the plan ID, revision, and policy version on screen, and passes the list's file facts as the
  expectations the executor checks.
- **No extra confirmation dialog.** The list is the preview and the button states the count, as
  agreed in the spec; the line under it says "Nothing moves until you press it. You can undo it."

## Tasks (one commit each)

1. Plan and review (docs).
2. **Refactor**, no behaviour change for the practice page: `FileOperationRunner`; practice
   executor delegates. All existing executor and practice tests still pass.
3. **Real tidying engine**: `FolderTidyExecutor`, `IFolderTidyExecutor` (Core),
   `TidyRunService`, `TidyPreview.MoveSources`, per-move checks, busy mapping, run lock,
   cross-executor refusals. Negative tests for every threat row.
4. **Page**: Tidy button on, result line, skipped files with reasons, Undo, Undo asking for
   permission again, help text updated. Page tests.
5. **Documents**: ADR 0021, review result, SECURITY, ARCHITECTURE, UI-UX, TESTING, MANUAL-TESTING,
   ROADMAP, INTERVIEW-NOTES learning log.
