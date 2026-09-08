# ADR 0012: Local Metadata Index Boundary

- Status: Accepted
- Date: 2026-09-09

## Context

V0.4 needs to find and summarize files without moving them, which requires remembering
metadata between scans. A remembered path is dangerous in a different way than a live
one: it can be stale, it can outlive the permission that produced it, and it can quietly
become a second, weaker answer to "what is DeskAI allowed to touch?".

## Decision

Keep the index strictly subordinate to authorization and to the live filesystem.

`IndexedFile` stores a root ID plus a normalized root-relative path and rejects rooted
paths, drive/alternate-data-stream colons, traversal segments, and paths beyond a fixed
length. It holds no absolute path and no file content. `Name` and `Extension` are derived
from the stored path rather than supplied, so they cannot disagree with it.

`IFileIndex` has no "read everything" member. Every read, write, and delete names one
root ID, so one authorized folder's entries cannot appear in another folder's results.
`indexed_files` carries a foreign key to `authorized_roots` with `ON DELETE CASCADE`, so
disconnecting a folder erases what DeskAI remembered about it in the same statement.

`MetadataIndexService` is the only path from the filesystem into the index. It refuses a
`Protected` root and any root that `IPathPolicy` blocks before scanning, then reaches the
disk solely through the existing bounded, cancellable `IFileScanner`, which opens no file
content and does not follow reparse points. Refreshes are incremental: existing rows are
compared against the new scan and only genuine differences are written, with removals
reported as forgetting an entry rather than deleting a file.

While correcting migration numbering for this change, an existing defect surfaced: the
version-4 migration recorded `CurrentSchemaVersion` instead of the literal `4`, so no
database ever recorded version 4. It now records `4`, and `INSERT OR IGNORE` backfills it
for existing installations.

## Alternatives

- Storing absolute paths was rejected because it would put personal paths in the database
  and invite code to act on them without an authorization check.
- A single global file table without root scoping was rejected because correct filtering
  would then depend on every caller remembering to add a `WHERE` clause.
- Deleting and re-inserting every row on each refresh was rejected because it is not
  incremental, loses the ability to report "nothing changed", and writes far more than a
  real change requires.
- Letting the index answer "does this file exist" for execution was rejected outright:
  the executor must revalidate live state, per `SECURITY.md`.
- Hashing content or extracting text was rejected here because both belong behind their
  own later permission gate.

## Consequences

Search and storage summaries can be built on remembered metadata without widening
filesystem access, and revoking a folder is a complete erasure rather than a permission
change with leftovers. The index can be stale between refreshes, which is acceptable
because nothing may mutate a file based on it. Nothing indexes automatically yet; the
service must be invoked explicitly, and no UI calls it in this slice. Duplicate detection,
content hashes, and semantic search remain unimplemented and require their own decisions.
