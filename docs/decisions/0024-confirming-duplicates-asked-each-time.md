# ADR 0024: Confirming Duplicates by Reading Files, Asked Each Time

- Status: Accepted
- Date: 2026-09-11

## Context

Home lists possible copies: files of the exact same size (V0.4 step 6, stage 1). Telling real
copies apart means reading their bytes. The one permission to read inside files that exists
(ADR 0014, 0015) says DeskAI reads only the beginning of plain text files, so the roadmap required
confirming duplicates to have its own consent wording and bounds rather than quietly reuse it.

## Decision

- **No stored permission; a question each time.** `DuplicateCheckService.PrepareAsync` lists the
  files in Home's size groups (at most 200, whole groups) and opens none. The page shows how many
  files, in how many folders, and how much would be read, and that nothing read is saved, sent, or
  changed. Only **Compare** calls `CompareAsync`, which reads only files in that question, after
  checking each folder is still connected. The same two-step shape as asking AI (ADR 0020).
- **Read as little as possible.** The first 64 KB of each file; to the end only when two
  beginnings match. Files over 2 GB are not read in full, and at most 8 GB is read in full per
  check. What was not compared is listed with its reason.
- **A second file reader, as narrow as the first.** `IFileFingerprinter` (Core) and
  `FileFingerprinter` (Infrastructure) take the folder as a separate argument, refuse folders not
  connected for reading, protected paths, paths leaving the folder, links on the way or at the file
  (before and after opening), online-only files, and files changed since DeskAI remembered them or
  while being read; they open read-only, letting others only read. SHA-256 in memory; kept nowhere.
  Only `DuplicateCheckService` can take the reader; a test fails otherwise.
- **Describes, never acts.** No remove or "keep one" action. The health score is unchanged.

## Alternatives

- Reuse the "read inside files" permission: rejected — people agreed to something smaller.
- A new stored per-folder permission, like tidying: rejected for now. It would let later checks
  read without asking, adds a permission to find and take back, and buys only skipping a dialog
  for an occasional action.
- Compare whole files byte by byte instead of fingerprints: no safer, and slower across folders.

## Consequences

Home can say which possible copies are identical. There are now two places that open files, each
refusing on its own. A future "tidy the copies away" would go through Tidy a folder's list, Tidy
button, and Undo — never from this card.
