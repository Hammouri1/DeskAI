# ADR 0031: Tidy while I'm away — a standing approval with a hard ceiling

Date: 2026-09-16. Status: accepted (owner's choice of 2026-09-16: "Move a few, then wait").
Review: `docs/security/2026-09-16-tidy-while-away-review.md`.

## Context

Everything before V0.9 stops at telling a person something. V0.9 lets a rule that has already
been approved carry itself out while nobody is watching. It is the largest single increase in
what DeskAI is trusted to do, so its shape was decided with the owner in plain words and
reviewed before code.

## Decision

1. **Per folder, on Organize, after a dialog.** A switch "Tidy this folder while I'm away" on a
   folder that may already be tidied. The dialog names the folder, the rules as worded, the
   ceiling, and what stops it. The yes records `AwayTidyApproval`: the folder and every enabled
   rule at its version.
2. **Rule-placed files only.** Never a file placed by type, never an AI idea, never a file with a
   same-name clash, never a subfolder's file, never a move out of the folder.
3. **At most 25 files per run**, one run per automatic check, in the same transaction, through
   the same `TidyRunService` and `FolderTidyExecutor` as a hand tidy. No new executor command.
4. **Anything unexpected stops it.** A rule change turns it off before the next run. A clash
   stops the run before anything moves. A file the executor refuses stays, and the mode turns
   off after that run. The reason is kept and shown on Organize until the person turns it on
   again, which records a fresh approval.
5. **Undo first.** The run is the folder's last tidy; Organize shows a "While you were away" card
   with Undo, the notice offers Review, and the notification carries a count. Undo needs the tidy
   permission as always.
6. **Wording follows the truth.** Every "nothing moves by itself" promise becomes "only in the N
   folders where you turned this on, and only what your rules match" while any folder has it on.
7. **Nothing permanently deleted, unattended or otherwise.** Unchanged.

## Alternatives rejected

- Moving everything the rules match with no cap: rejected by the owner; a wrong rule could move
  hundreds of files before anyone looked.
- "Keep both" for clashes unattended: a numbered copy is a choice a person makes.
- A separate executor or timer for away runs: rejected; reusing the check timer keeps one clock
  and one executor, and the mode inherits "runs only while DeskAI runs, never registered with
  Windows".
- Continuing the mode after a refusal: rejected; "stops and waits for a person" was the choice.

## Consequences

- `AutomaticCheckCoordinator` gains `AwayTidyService`, the one type it can reach that moves a
  file; `AutomaticCheckService` still holds no executor. The containment tests name this.
- Schema 14 adds `away_tidy` and `away_tidy_runs`, both cascading on the folder.
- Home, Automatic tasks, the keep-running dialog, the help text, and the tooltip change their
  wording with the mode.
