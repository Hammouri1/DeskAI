# Security Review — Confirming Duplicates by Reading Files

- Date: 2026-09-11
- Scope: V0.4 step 6, stage 2 — reading whole files in connected folders to tell whether files
  of the same size are identical.
- Required by: `docs/SECURITY.md` ("a focused review is required before introducing … content
  extraction") and the roadmap, which requires its own consent wording and bounds.
- Status: accepted as built (see Result). Written before the code; result added when built.

## What changes

Until now DeskAI opened files only to read the first 64 KB of plain text files in folders where a
person allowed that. This step lets it read any kind of file from start to end, but only the
files a person has just been shown and agreed to have compared, only to compute a fingerprint in
memory, and only in folders still connected. It adds no way to change a file.

## Threat cases and controls

| Threat | Control | Test |
|---|---|---|
| Files read without the person agreeing | Nothing is read until Compare is pressed on a dialog stating the count, folders, and size; preparing reads nothing | preparing opens no file (a locked file still prepares); cancel reads nothing |
| A check reads more than was shown | Compare reads only files in the prepared question | a file added to the folder afterwards is not read |
| A folder disconnected, or no longer searchable, after the question was shown | Each folder re-checked before its files are read | disconnected folder's files skipped |
| A file outside the connected folder, or a protected one | Path policy and containment before opening | protected file skipped; escaping path refused |
| A link to somewhere else | Link check before opening and after the handle is open | link skipped, its target not read |
| An online-only file | Skipped before opening, because reading would download it | online-only skipped |
| A file changed since DeskAI remembered it, or while being read | Size and last-changed time checked before and after reading | changed file skipped, reported "changed" |
| A file open for writing by another program | Opened with others allowed only to read; sharing violation reported | busy file skipped, others compared |
| Reading huge files or too many | 200 files, 2 GB per file in full, 8 GB per check, all stated; Stop cancels | limits reported, not guessed |
| Two different files reported as copies | Equal size and equal SHA-256 of the whole file | same size, different ending: "not the same" |
| Fingerprints or contents kept or sent | Held in memory for one check; no store, no AI; the fingerprinter is reachable only from this service | services-cannot-reach tests; nothing written to the database |
| The check changes something | The fingerprinter opens read-only and has no write path; the page offers no action | files and times unchanged after a check |
| Anything outside the connected folders is touched | — | a sentinel file outside is unchanged |

## What a person is told

Before: "DeskAI will read 14 files (2.3 GB) in 2 folders from beginning to end, on this computer,
to see which are really the same. Nothing it reads is saved or sent anywhere, and it does not
change, move, or delete anything." with **Compare** and **Cancel**. After: for each group,
"Identical", "Same size, different contents", or "Not checked" with the reason for each file.

## Rollback and recovery

Nothing to roll back: the check writes nothing. Stopping part-way reports what was compared.

## Result

Accepted, 2026-09-11 (ADR 0024). Every row above has a named test:

- `DuplicateCheckTests` (11, whole app, generated files): preparing opens no file even one locked
  by another program; identical and same-size-different files; large files differing only at the
  end read whole; files differing at the start read only at the start; no file time or database
  byte changed by a check; changed, busy, online-only, and link files not compared; a folder
  disconnected after the question, and a file added after it, not read.
- `DuplicateCheckServiceTests` (10, fakes): preparing reads nothing; 200 files, whole groups, the
  rest counted; 2 GB and 8 GB limits; only files in the question read; Stop; a disconnected folder
  not read; only `DuplicateCheckService` can take the reader.
- `FileFingerprinterTests` (8): whole and beginning fingerprints match SHA-256 and change nothing;
  folders not connected for reading, protected files, paths leaving the folder, a subfolder turned
  link, and a missing file refused (and not created).
- `CopyCheckPageTests` (7): the dialog's wording, Compare's results and summary, reasons for files
  not checked, Cancel reads nothing, nothing to check, Stop offered only while running.

Controls removed on purpose to check the tests notice: the folder-permission check, the link check
on the way, the online-only check, the changed-file check, the beginning-first pruning, and the
still-connected check. Each made a test fail and was restored. The link tests ran on this machine.

Full suite: 881 tests pass, none skipped; Release build with zero warnings; formatting clean.

Residual: the dialog itself is checked by hand; Stop's effect on a running check is proved at the
service level, where timing can be controlled; a file changed and changed back to the same size
and time within one check cannot be detected (accepted — the fingerprint still describes the bytes
actually read).

## Not accepted by this review

Deleting, moving, or offering to remove copies; storing fingerprints; sending anything to AI;
reading files other than those in a size group Home showed; changing the health score.
