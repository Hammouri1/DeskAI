# Security Review — Undo After Reopening, and Recovering an Interrupted Tidy

- Date: 2026-09-11
- Scope: V0.6 step 4 — finding a real folder's last tidy after DeskAI is reopened, and checking
  and answering a tidy or undo that stopped part-way.
- Required by: `docs/SECURITY.md` (undo and recovery are part of the file-change design) and
  ADR 0021, which left interrupted real-folder records untouched until this review.
- Threat design: Part 2 §7 of `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md`.
- Status: accepted as built (see Result). Written before the code; result added when built.

## What changes

Until now Undo was offered only in the visit that tidied, and a record left unfinished by a
crash stayed as the journal left it. After this step DeskAI reads the folder's journal records
when the Organize page shows that folder: it offers Undo for the last tidy, and it checks an
unfinished record against the disk and asks the person what to do. No new kind of file change
is added: checking reads names, sizes, and dates only, and every move back goes through the
existing undo with its existing per-file checks.

## Threat cases and controls

| Threat | Control | Test |
|---|---|---|
| A record is "recovered" while its run is still going (a second DeskAI window, or the same window) | A lock file beside the database is held for every tidy, undo, check, and answer; unfinished records are looked at only while holding it | a record is not checked while another window holds the lock, and is once it is free; tidy and undo refuse while it is held |
| The lock is held by a process that died | Windows releases a file lock when its process ends; DeskAI never leaves it held after a run | the lock is free again once a run is over (a real process death is checked by hand) |
| The lock is never freed (a hung window) | Bounded wait; refuse with a plain message rather than hang | busy lock refuses |
| A move is guessed as done, or as not done | Done only if gone from the source and the destination has the recorded size and last-changed time; not done only if still at the source with those facts; anything else is needs review | each case, including a file changed after the crash |
| A needs-review file is moved by Undo | Undo moves only operations recorded as completed; needs review is a separate state | needs-review file stays |
| A folder that was there before is removed by Undo after a crash | An interrupted folder creation is recorded as already there, never as made | folder kept after undo |
| A link placed in the path after the crash | Checking refuses links in source and destination paths, making the file needs review; undo refuses links anyway | link makes needs review; nothing moves through it |
| The folder was replaced, moved, protected, or disconnected since | Checking requires the folder connected, at the same canonical path, passing its safety re-check | nothing checked, nothing changed |
| Undo those without the tidy permission | Same rule as Undo: asks first; the record stays open | refused, record untouched |
| One folder's record checked, kept, or undone from another folder | Every call names the folder, and the record's plan must belong to it | refused |
| A practice record reached through the real-folder executor, or the reverse | Records whose plan is not for a tidy-permitted connected folder are refused; practice recovery skips real records | both directions |
| A new tidy piling a second unfinished record on the first | Tidy refused while any unfinished record exists for the folder; the page says to answer first | refused |
| Undo of an older tidy behind the last one | Only the latest tidy that moved something is offered | older tidy not offered |
| Undo twice, after reopening | Existing once-only rule reads the journal, so it holds across restarts | second undo refused |
| Anything outside the folder changes | — | a sentinel file beside the folder is unchanged after every test |

## What a person is told

"Last tidy: 3 files, at 10:40 on 11/09/2026." with **Undo**. For an interrupted tidy: "Your last
tidy was interrupted: 7 of 12 files moved." with **Undo those 7** and **Keep them**; before
anything moved, only **OK**. For an interrupted undo: "Your last undo was interrupted: 3 of 5
files went back." with **OK**. Files that need a look are listed with where to look. While a
question is open, the Tidy button is off and says why.

## Rollback and recovery

The journal keeps every state it had; checking writes each file's verified state and leaves the
record waiting for an answer, which only then closes it. Checking twice is harmless. Undo is the
existing validated undo. A disconnected folder's records are erased with it (fixed 2026-09-11),
so there is nothing left to recover for it.

## Result

Accepted, 2026-09-11, with every control in the table built and tested (ADR 0022):

- `RunLockFile` and `FolderTidyExecutor.CheckInterruptedAsync` / `CloseInterruptedAsync` in
  Infrastructure; `FileOperationRunner.CheckInterrupted` decides per file; `TidyRunService`
  finds the last tidy and describes and answers an interrupted one; the Organize page shows both.
- A crash is simulated by stopping the real journal just before or after one of its writes in a
  real run (`StoppingJournal`), then reopening DeskAI over the same database (`TestApp.ReopenAsync`).
- Tests: `TidyRecoveryTests` (16, through the whole app), `TidyRecoveryPageTests` (8),
  `FolderTidyExecutorTests` (3 new: busy lock refuses tidy and undo, check waits for the lock,
  lock free after a run), and `SqliteAuthorizedRootRepositoryTests` (2 new, for disconnecting).
  The link test ran on this machine rather than being skipped.
- Controls removed on purpose to check the tests notice: the "still at the source" proof, the
  destination match in the "moved" proof, the link checks while checking, the refusal of a new
  tidy while a question is open, the lock around checking, and the page's Tidy guard. Each made
  a test fail and was restored.
- Full suite: 846 tests pass, none skipped; Release build with no warnings; formatting clean.

Found while planning and fixed first: a folder that had been tidied could not be disconnected —
the plans and journal blocked it with a raw database error after its search memory was cleared.

## Not accepted by this review

Undoing any tidy other than the last one; finishing an interrupted undo automatically (its
remaining files stay where the tidy put them, and the person is told); a history list of past
tidies; recovery while DeskAI's window is closed.
