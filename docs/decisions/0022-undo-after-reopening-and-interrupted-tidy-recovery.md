# ADR 0022: Undo After Reopening, and Recovering an Interrupted Tidy

- Status: Accepted
- Date: 2026-09-11

## Context

ADR 0021 shipped tidying a real folder with Undo in the same visit, and left two things for
V0.6 step 4: finding the last tidy again after DeskAI is reopened, and a tidy stopped part-way by
a crash, whose journal record was to stay untouched until then. ADR 0009 had deferred
cross-restart undo for the practice workspace because its folder could not be authenticated
after a restart. A connected folder has no such problem: it is trusted by a live check of its
permission and path, which works just as well in a new process.

Designing this exposed a gap in step 3. Its "one run at a time" lock lived inside one process,
so two DeskAI windows could tidy the same folder at once — and a second window would see the
first one's live record as interrupted.

## Decision

- **The journal is the memory.** Nothing new is stored. `IOperationJournal.ListForRootAsync`
  reads a folder's own records; `TidyRunService.FindLastAsync` offers the latest tidy that moved
  a file, if it has not been undone, and never an older one behind it.
- **A lock file beside the database** (`RunLockFile`) is held for every tidy, undo, check, and
  answer, as well as the in-process lock. Windows releases it when its process ends, so a crash
  frees it. The wait is bounded; a busy lock refuses with a plain message. While it is held, an
  unfinished record belongs to a run that is not running — the invariant that makes checking
  safe.
- **Checking** (`FolderTidyExecutor.CheckInterruptedAsync`) reads names, sizes, and dates only,
  so it needs the folder connected and passing its safety re-check, not the tidy permission. Per
  operation: never started; moved only if gone from the source and the destination matches the
  recorded size and last-changed time; not moved only if still at the source with those facts;
  otherwise `JournalOperationState.NeedsReview` (appended), including when a link is on the way.
  A folder being made that exists is recorded as already there, so undo never removes it. The
  record then waits (`RecoveryRequired`); checking again changes nothing.
- **The person answers.** *Keep them* closes the record (`CloseInterruptedAsync`) as an ordinary
  tidy of what moved, which then becomes the last tidy. *Undo those* closes it and runs the
  ordinary undo; without the tidy permission it asks first and leaves the question open. An
  interrupted undo, or a tidy that moved nothing, gets OK only.
- **One question per folder.** While a record is unfinished, the executor refuses a new tidy or
  undo there, and the page turns Tidy off and says why.
- **Disconnecting erases the folder's tidy history** (fixed the same day: it used to block the
  disconnect). A disconnected folder's tidy can no longer be undone; the practice workspace's
  history is never reached by that path.

## Alternatives

- Store "last tidy" separately: rejected. The journal already holds it, and a second record
  could disagree with it.
- Recover automatically at startup: rejected. What to do with files that did move is the
  person's decision, and the spec asks for it.
- Guess a move from partial evidence (for example, "gone from the source"): rejected. A file the
  person moved or edited after the crash would be treated as DeskAI's, and undo would move it.
- Only check records started before this process started: rejected. It does not stop a second
  window from checking a first window's live run; a lock both windows share does.
- A named semaphore or mutex instead of a file: rejected. A semaphore stays taken if its holder
  crashes, and a mutex must be released by the thread that took it, which `await` does not
  guarantee.
- Finish an interrupted undo automatically: not accepted by the review. Its remaining files stay
  where the tidy put them, and the person is told how many went back.

## Consequences

Undo now survives closing DeskAI, which is the V0.6 exit criterion "undo it after a restart". A
crash mid-tidy ends in a stated question instead of a silent record. Two DeskAI windows can no
longer tidy at once. A tidy that stopped before any file moved, or an interrupted undo, can leave
an empty folder DeskAI made; it is harmless and not removed without being asked.
