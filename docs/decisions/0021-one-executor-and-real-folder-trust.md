# ADR 0021: One Executor, and How a Real Folder Is Trusted

- Status: Accepted
- Date: 2026-09-10

## Context

V0.6 step 3 is the first time DeskAI moves files in a folder someone connected. Until now the
only executor, `TemporaryDemoPlanExecutor`, worked inside a generated folder under Windows
Temp and proved it owned that folder with a marker holding a secret only the running process
knew. ADR 0007 said it should be "replaced or generalized only after journaling, recovery,
undo, and a separate security review"; ADR 0019 said real tidying needs its own executor and
review. The design (Part 2 §8) chose one executor for both, so there is one set of move,
collision, link, and journal rules to test rather than two that can drift.

## Decision

- **Shared rules, separate trust.** `FileOperationRunner` (internal to Infrastructure) holds the
  journal-before-change sequence, per-operation checks, moves, folder creation, undo, and
  recovery proof. Each executor supplies an `IRootTrust` that is checked before the run and
  again before every operation:
  - practice: the workspace's shape under its dedicated temp base and its secret marker;
  - real folder (`FolderTidyExecutor`): still connected, `RootCapabilities.CanTidy`, the same
    canonical path as at the start of the run, and `IReadOnlyFolderService.CheckStillSafeAsync`
    (present, not network or whole drive, no link in its path, not protected).
- **Per-file checks right before each move:** source still a regular file with the size and
  last-changed time the person saw (supplied as `ExpectedFile` from the list), not online-only,
  hidden, or system; no link in any existing path component of source or destination; the
  destination folder exists; the destination name is free; and the move is made with
  `overwrite: false`, so a name taken in the last instant still fails. A sharing violation is
  reported as "open in another program". Each refusal is that file's own outcome.
- **Undo ships with tidying.** `SECURITY.md` treats undo as part of the design, so real moves do
  not arrive without it. Undo needs the tidy permission; the page asks for it again if it was
  taken back.
- **Each executor refuses the other's records.** Undo checks that the record's plan belongs to
  the executor's trusted folder. Practice recovery no longer marks a connected folder's
  interrupted record; it leaves it for that folder's own recovery (step 4).
- **One run at a time** in the real-folder executor, for tidy and undo.
- **Plain messages.** DeskAI's own refusals carry messages written for the person; anything
  Windows raises on its own is described as "Windows could not do that" rather than passing a
  technical message through.

## Alternatives

- A second, copied executor for real folders: rejected. Two copies of the move rules would
  drift, and the practice page would stop being a faithful rehearsal of the real thing.
- Trusting a real folder once at the start of the run: rejected. Permission can be withdrawn,
  the folder disconnected, or its path replaced by a link while a run is in progress.
- Trusting the list's facts without re-reading the disk: rejected. The list can be minutes old.
- Shipping tidy first and undo later: rejected for the reason above.

## Consequences

The practice page now exercises the same code as real tidying, so its tests protect both.
A person can tidy and undo in the same visit. Finding the last tidy again after reopening
DeskAI, and handling a tidy interrupted by a crash, are step 4; until then an interrupted real
record stays exactly as the journal left it, never guessed at.
