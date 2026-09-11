# Organize Step 4 — Last Tidy After Reopening, and the Interrupted-Tidy Prompt

**Goal:** Close DeskAI after a tidy, open it again, and the Organize page still says what the
last tidy did and offers **Undo**. If DeskAI or Windows stopped part-way through a tidy, the
page checks each file that was moving against the disk and asks: "Your last tidy was
interrupted: 7 of 12 files moved." with **Undo those 7** and **Keep them**. A move DeskAI
cannot prove is marked for review and never guessed at.

**Spec:** `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md` Part 1 step 6
and Part 2 §7. **Security review:** `docs/security/2026-09-11-tidy-recovery-review.md`, written
before the code.

## Decisions

- **The journal is the memory.** Nothing new is stored. "Last tidy" is read from the journal
  records of the folder's own plans (`IOperationJournal.ListForRootAsync`), newest first: the
  latest tidy that moved at least one file, offered only if it has not been undone. Only the
  last one is offered, never an older one behind it.
- **One run at a time, across windows too.** Step 3's lock was inside one DeskAI process. Two
  DeskAI windows could tidy the same folder at once, and a second window would see the first
  one's live record as "interrupted". The real-folder executor now also holds a lock file next
  to the database for every tidy, undo, check, and answer. Windows releases it when a process
  ends, crash included. While it is held, any unfinished record for a folder belongs to a run
  that is no longer running — that is what makes checking it safe. Waiting is bounded: a busy
  lock refuses with a plain message instead of hanging.
- **Checking an interrupted record reads only names, sizes, and dates** inside the folder, so it
  needs the folder to be connected, at the same place, and to pass its safety re-check — not
  the tidy permission. Moving anything back still needs the tidy permission.
- **What the check decides, per file:**
  - never started (Pending) → not started;
  - a move in progress → moved if the file is gone from where it was and the destination holds
    a file with the recorded size and last-changed time; not moved if it is still where it was
    with those facts; otherwise **needs review** (a new journal state, appended). Links in the
    path make it needs review;
  - a folder being made → already there if it exists (it cannot be proved DeskAI made it, so
    undo will never remove it), otherwise not made;
  - an undo in progress, the same in reverse.
  The record then waits for the person (`RecoveryRequired`). Checking twice changes nothing.
- **Answers.** *Keep them* closes the record as an ordinary tidy of what moved, so it becomes
  the last tidy and can still be undone later. *Undo those N* closes it the same way and then
  runs the ordinary undo, with its own per-file checks; without the tidy permission it asks
  first, exactly like Undo. A tidy interrupted before anything moved, and an interrupted undo,
  get **OK** only. Needs-review files are listed with where to look and are never moved.
- **No new tidy while a question is open** for that folder: the page turns Tidy off with a line
  saying why, and the executor refuses too.
- **Each executor still refuses the other's records**, and practice recovery still leaves a real
  folder's records alone.

## Tasks (one commit each)

1. Plan and review (docs). (A bug found while planning — a tidied folder could not be
   disconnected — was fixed first in its own commit.)
2. **Engine:** `ListForRootAsync`; `JournalOperationState.NeedsReview`; the cross-window lock;
   `FolderTidyExecutor.CheckInterruptedAsync` and `CloseInterruptedAsync`; the refusal while a
   question is open; `TidyRunService.FindLastAsync`, `FindInterruptedAsync`, `KeepInterruptedAsync`,
   `UndoInterruptedAsync`. Negative tests for every row of the review, simulating a crash by
   writing the journal exactly as a stopped run leaves it.
3. **Page:** the last tidy with Undo after reopening (a second `TestApp` over the same folder and
   database), the interrupted card and its answers, Tidy off while a question is open, help.
4. **Documents:** ADR 0022, review result, SECURITY, ARCHITECTURE, UI-UX, TESTING, MANUAL-TESTING,
   ROADMAP, README, INTERVIEW-NOTES.
