# ADR 0044: Desktop Studio Moves Things on the Desktop

- Status: Accepted
- Date: 2026-09-24
- Review: `docs/security/2026-09-24-desktop-moves-review.md`
- Design: `docs/superpowers/specs/2026-09-24-desktop-studio-design.md` (step 3)

## Context

Clear old stuff and Folder by group move folders and files on the connected Desktop. Until now
the executor moved only files, and the tidy permission's dialog promises that DeskAI "never
touches files in subfolders". Moving a folder moves everything inside it.

## Decision

- **Move a folder** is a new plan action, `MoveFolderOperation`, carried out as one
  `Directory.Move` inside the same connected folder, so it is never half-moved. It has the file
  move's checks: written to the journal first, the folder's permission and path re-checked before
  each action, no link on the way, not hidden or system, the same folder as in the preview (same
  made-at time and own last-changed time), destination free, never inside itself, never
  overwriting. Windows' refusal while something inside is open is reported in plain words.
- **Put back** moves a folder back only if it is still the same folder (same made-at time) and its
  old place is free. Things added inside since go back with it.
- **After a stop,** a folder move under way is checked against the disk by made-at time: gone from
  the start and found at the end is "moved"; still at the start is "not moved"; anything else is
  "needs review" and is never moved on a guess.
- **A plan records which feature made it** (`PlanPurpose`: Tidy, ClearOldStuff, FolderByGroup).
  Only a non-Tidy plan may contain a folder move, so Organize, folder templates, and Tidy while
  I'm away can never move a folder.
- **A separate yes.** Desktop Studio's moves need `folder_move_permissions`, asked with its own
  dialog and taken back with its own Stop. The tidy permission does not grant it, and it does not
  grant tidying. The executor checks the grant that matches the plan's purpose, before the run and
  before every action.
- **Latest-only Put back.** A card offers Put back only for the latest change on the Desktop, and
  only if that change was its own.
- Nothing is deleted. The only folder ever removed is an empty one DeskAI itself made in that run,
  on Put back, as templates already do.

## Consequences

Schema 17 adds `organization_plans.purpose`, `execution_operation_journal.before_created_at_utc`,
and `folder_move_permissions` (cascading with the folder). An Organize Undo is no longer offered
once a Desktop Studio change ran after it, the same rule Organize already applies to templates.
