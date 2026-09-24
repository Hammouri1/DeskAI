# ADR 0045: Tag Names Renames Folders on the Desktop

- Status: Accepted
- Date: 2026-09-24
- Builds on: ADR 0044
- Review: `docs/security/2026-09-24-tag-names-review.md`

## Context

Tag names puts each group's name in front of the names of the folders in that group ("Coding –
Python stuff"). The design called for a new executor action, "rename a folder".

## Decision

- A rename is carried out as ADR 0044's `MoveFolderOperation` with the destination in the same
  folder: one Windows rename, with every existing check (same folder as in the list, never
  overwriting, journal first, check after a stop, Put back by made-at time). No new executor
  action and no schema change; the plan's purpose is the new `PlanPurpose.TagNames`.
- Only folders are renamed; files and Not sure keep their names. The separator is " – ".
- A folder already starting with "Group – " is left alone. A new name that fails
  `FolderNameCheck`, or that is already used by anything on the Desktop (including hidden or
  left-out items), is left alone with the reason.
- The same separate yes as ADR 0044 applies; its dialog says "move or rename".

## Consequences

Put back of a rename moves the folder back to its old name only if that name is free and the
folder is still the same one. Project and program folders start unticked, because renaming them
can break shortcuts or programs that remember the old name.
