# ADR 0023: Retire the Practice Page and Its Executor

- Status: Accepted
- Date: 2026-09-11
- Supersedes: the practice parts of ADR 0007 and ADR 0009; amends ADR 0021 (one executor).

## Context

Since V0.2 DeskAI had a practice run: generated sample files in a marker-protected folder under
Windows Temp, moved by `TemporaryDemoPlanExecutor`, with a "Get AI ideas" card. It let the move,
journal, and undo loop be shown safely before any real folder could change. V0.6 made real
tidying possible, and the design kept practice behind a "Nervous? Try it on example files first"
link, to be simplified in step 5.

Asked how to simplify it, the owner chose to remove the practice page entirely and explain the
Organize page with a small card instead. Real tidying now previews every move, re-checks each
file, never deletes, and undoes after a restart, so a separate rehearsal adds little; and a page
showing made-up files with AI percentages beside a real page without them confused more than it
reassured.

## Decision

- Remove `PracticePage`, `PracticeViewModel`, the sample-plan factory, the preview-row view
  models and status converters, and the practice executor with `IPlanExecutor`, `IUndoService`,
  and `DemoWorkspaceOptions`. An executor that nothing in the app can reach is still code that can
  move files; keeping it would be surface without purpose.
- Add a "How tidying works" card to Organize: four steps and the promise (never deletes, never
  touches subfolders, never moves anything out of the folder). It is open for someone who has not
  allowed tidying anywhere yet, closed otherwise.
- Keep `RootAuthorizationScope.ControlledDemo`. Scopes are stored as numbers and databases from
  before this change can hold a practice folder; it keeps meaning "never searched, never tidied".
  Tests prove such a folder can never be tidied, undone, checked, or closed.
- Port first, then delete: the practice executor's tests of shared move rules not yet covered for
  real folders (an approval naming an unknown operation, a failing journal, intent and outcome
  journaled) now run against `FolderTidyExecutor`. AI journey tests now use Ask AI on Tidy a
  folder.

## Alternatives

- Rebuild the practice page to match Tidy a folder, with or without AI: offered to the owner and
  not chosen.
- Keep the executor unregistered "in case": rejected; dead code that can move files.

## Consequences

`FolderTidyExecutor` is the only code in DeskAI that moves a file. Trying AI now needs a folder the
person allowed DeskAI to tidy, and Ask AI shows exactly what is sent before anything is. Old
practice journal records and temp folders from earlier versions are left as they are: DeskAI never
touches them again. Home and the side menu say "Nothing connected yet" instead of "Practice mode".
