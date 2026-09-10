# ADR 0019: A Separate, Revocable Permission to Tidy One Folder

- Status: Accepted
- Date: 2026-09-10

## Context

V0.6 lets DeskAI change files in folders a person connected. Until now a connected folder
could only be read: `MetadataOnly` (names, sizes, dates) or `MetadataAndContent` (also the
text inside plain text files). ADR 0010 required that real-folder changes need their own
consent and could not reuse metadata consent.

Two facts shaped the decision. Scopes are exclusive values, so adding "may tidy" as a scope
would force a choice between reading inside files and tidying, and would need new combined
values for each pairing. And the folder row is rewritten whenever its reading scope changes
(`ConnectedFolderService` saves a freshly created root), so anything stored on that row is
one careless save away from being dropped or granted.

A third fact was found while designing: `AuthorizedRoot.Create` defaulted its scope to
`Organize`, the value that may be changed. No production caller relied on it, but a default
is what a future caller gets by forgetting.

## Decision

- Store the tidy permission in its own table, `tidy_permissions(root_id, granted_at_utc)`,
  with a foreign key to `authorized_roots` and `ON DELETE CASCADE`. Disconnecting a folder
  erases it like everything else remembered. Schema version 12.
- Surface it as `AuthorizedRoot.TidyAllowedSinceUtc`, read with a `LEFT JOIN`. `SaveAsync`
  never writes it, so changing what may be read neither drops nor grants tidying.
- `RootCapabilities.CanTidy` is true only for an `Allowed` folder whose scope is one of the two
  reading scopes and that holds a grant. The scope list is explicit, so a scope added later
  cannot inherit tidying. `CanMutate` answers true for such a folder, so the plan validator
  accepts plans for it.
- The grant is written only by `TidyPermissionService.AllowAsync`, which the page calls after a
  dialog naming the folder and what tidying may do. It re-checks the folder at that moment —
  still present, not a network or whole-drive location, no link in its path, not protected —
  rather than trusting how it looked when connected. The repository itself refuses to attach
  a grant to any folder not connected for reading, so the practice workspace cannot get one.
- Withdrawing needs no confirmation and leaves the folder connected.
- `AuthorizedRoot.Create` no longer has a default scope; every caller names one.

## Consequences

Plans for a tidy-permitted folder now pass validation. In this step no executor can act on
them: `TemporaryDemoPlanExecutor` is bound to its own generated workspace and a test proves it
refuses a plan for a tidy-permitted real folder. Real tidying (V0.6 step 3) adds its own
executor, with live per-file rechecks, the journal, and a security review, before any file in
a connected folder can move.
