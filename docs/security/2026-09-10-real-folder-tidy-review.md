# Security Review — Tidying a Real Folder

- Date: 2026-09-10
- Scope: V0.6 step 3 — the first code that moves files in a folder the person connected.
- Required by: `docs/SECURITY.md` ("a focused review is required before introducing file
  mutation"), ADR 0010 and ADR 0019 (real-folder changes need their own executor and review).
- Threat design: Part 2 of `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md`.
  This review states how each threat is controlled and which test proves it.
- Status: written before the code; the result section is completed when the step is built.

## What changes

Until now the only code that moved a file was bound to a generated folder under Windows Temp.
After this step, pressing Tidy moves ticked loose files in a connected folder into folders
inside it. Nothing else changes: no delete, no move out of the folder, no touching subfolder
files, no rename except the " (2)" a person chose with Keep both.

## Operations allowed

Create a folder inside the chosen folder; move a loose top-level file into one; the same move
to a " (2)"-style name. Nothing in the plan can express anything else, the safety validator
refuses anything outside the folder, and the executor's operation switch has no other case.

## Threat cases and controls

| Threat | Control | Test |
|---|---|---|
| A file changed, was replaced, or renamed after the list was shown | Size and last-changed time from the list are checked right before the move; missing source fails | changed file skipped; replaced file skipped; missing file skipped; others move |
| Destination taken after the list was shown | Checked before the move, and the move refuses to overwrite | occupied destination skipped; both files intact |
| A link or junction in the path | Every existing component of source and destination checked; the folder's own path checked by the live trust check | junction destination folder refused; nothing written through it |
| Rule destination `..\x` or `C:\x` | Refused when saved (existing), by the planner's validator, and by the executor's path policy | plan with an escaping destination refused, nothing moves |
| Permission withdrawn between list and Tidy | Live trust check before the run and before each file | nothing moves |
| Folder disconnected mid-tidy | Same check per file | remaining files refused |
| Folder replaced by a different one at the same name, or moved | Canonical path and link check per file | refused |
| Online-only, hidden, or system now | Live attribute check per file | skipped with reason |
| Busy file | Sharing violation mapped to a per-file refusal | that file stays, others move |
| Protected path | Executor uses the configured protected-path policy | protected entry never moved |
| Approval for a different plan, revision, or policy; unknown operation IDs; nothing ticked | Envelope check before any journal write | nothing moves |
| Crash between journal and move | Write-ahead journal with the list's facts; recovery for real folders is step 4, and practice recovery leaves real records alone | practice recovery does not touch a real record |
| Undo when the file changed, the spot is taken, or a folder is not empty | Per-file refusal, never overwrite or delete a non-empty folder; folders that existed before are never removed | each case |
| Undo after the permission was withdrawn | Refused; the page asks to allow tidying again | refused without permission |
| One executor undoing the other's record | Each refuses records whose plan root it does not trust | both directions |
| Two runs at once | One run at a time | second run waits |
| The practice workspace or a folder without tidy permission reaching the real executor | Trust check requires `CanTidy`, which the practice scope never has | practice root refused |
| Anything outside the folder changes | — | a sentinel file outside the sandbox is unchanged after every tidy and undo test |

## What a person is told

The button says how many files will move; the line under it says nothing moves until it is
pressed and that it can be undone. Afterwards one line says what happened, never a green
"success" when anything was skipped, followed by each skipped file and why in plain words, and
an Undo button.

## Rollback and recovery

Undo reverses the tidy from the journal with its own checks. An interrupted tidy leaves a
journal record whose finished moves are provable against the disk; showing it to the person
and offering "Undo those" or "Keep them" is step 4. Until then such a record is left untouched
rather than guessed at.

## Result

To be completed when the step is built.
