# ADR 0014: Content-Access Capability Gate

- Status: Accepted
- Date: 2026-09-09

## Context

Everything DeskAI has read so far is metadata: names, sizes, dates, attributes. V0.4 step 8 introduces the first feature that would open a file and read what is inside it, which is a materially different promise to the person who connected the folder. `SECURITY.md` requires a focused review before content extraction, with threat scenarios and negative tests, and ADR 0010 states that a later scope change cannot silently reuse metadata consent.

The existing code decided permissions by comparing the scope directly: `scope == MetadataOnly` allowed searching, and the same comparison blocked mutation. That pattern fails open. Adding a fourth scope would have dropped it out of the mutation check and granted it the right to change files, without a single line of the check appearing to change. Fixing that has to come before the scope exists, not after.

## Decision

Introduce `RootCapabilities` as the single place that answers what a folder permits: `CanReadMetadata`, `CanReadContent`, `CanMutate`. Each lists the scopes that grant it and denies everything else, so a scope added later starts with no rights and must be granted them deliberately, in that file, under test. Permission is checked alongside scope, so a restricted or protected folder grants nothing whatever it was connected for. `PlanValidator`, `FileSearchService.IsSearchable`, and `ReadOnlyFolderService` now ask the capability instead of comparing a value.

Add `RootAuthorizationScope.MetadataAndContent = 3`, appended so stored numbers never move. It grants metadata plus permission to open files, and explicitly no permission to move, rename, or delete. Content access and mutation are separate consents in both directions: a folder connected for organizing may not have its contents read either.

**No extraction code, no UI, and no way to grant the scope ship in this slice.** The only three places that construct an authorized root produce `ControlledDemo` (the generated practice workspace) and `MetadataOnly` (the folder picker). The gate exists and nothing can pass through it yet, which is deliberate: the refusal path is built and tested before the capability it guards.

## Consequences

The permission model now fails closed when extended, and the matrix of what each scope grants is one readable table backed by a test that breaks if a scope is added without a decision. The mutation refusal message changes from naming metadata-only access to naming the actual reason, since more than one scope now cannot mutate.

Two things the next slice must handle, and which are known gaps rather than oversights:

- `SqliteAuthorizedRootRepository.RemoveAsync` deletes only roots stored as `MetadataOnly`. A content-authorized folder could not be disconnected through it. This is harmless while no such folder can exist, and must be fixed when one can.
- Granting the scope needs its own consent step naming exactly what will be read, which formats are supported, and what happens to extracted text. Extracted content is untrusted input and an AI disclosure category of its own; it does not inherit the existing metadata disclosure consent any more than it inherits the metadata authorization.
