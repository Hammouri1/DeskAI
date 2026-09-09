# Content-Access Gate Security Review

- Date: 2026-09-09
- Scope: V0.4 step 8, stage 1 — the authorization gate in front of file content
- Result: gate accepted; content extraction itself remains unbuilt and unreachable

`SECURITY.md` requires a focused review before content extraction is introduced. This review
covers the permission model only. No code in this change opens a file.

## What Changed

`RootCapabilities` becomes the single answer to what an authorized folder permits, replacing
direct comparisons against one scope value. `RootAuthorizationScope.MetadataAndContent` is
appended to the scope enum. `PlanValidator`, `FileSearchService.IsSearchable`, and
`ReadOnlyFolderService.ListAuthorizedAsync` now ask a capability instead of comparing a value.

## Threats and Controls

- **Silent widening when the model is extended.** The previous checks compared against
  `MetadataOnly`, so a new scope would have fallen out of the mutation block and become
  changeable. Capabilities now list the scopes that grant a right and deny everything else,
  so an added scope arrives with nothing. A test fails if a scope is added without a row in
  the capability matrix.
- **Consent reuse.** Metadata consent is not content consent. `CanReadContent` is granted by
  the content scope alone; a metadata-only folder is refused. Tested in both directions.
- **Reading becoming writing.** A content-authorized folder still cannot be changed:
  `CanMutate` does not list the content scope, and `PlanValidator` blocks a plan bound to it.
  A folder connected for organizing likewise cannot have its contents read.
- **Stored-value remap.** Scopes persist as integers. The new value is appended, not
  inserted, and a test pins all four numbers, so an existing metadata-only grant cannot come
  back as something wider.
- **Permission bypass.** Restricted and protected folders grant nothing at any scope, tested
  across the whole enum.
- **Reaching the scope at all.** The three places that construct an authorized root produce
  `ControlledDemo` and `MetadataOnly` only. Existing tests assert the picker stores
  `MetadataOnly`. Nothing in the product can currently produce a content-authorized folder.

## AI Containment

Unchanged and unaffected. `DeskAI.AI` receives no scanner, path, filesystem, or repository
service, and this change adds none. There is no extracted content in the system, so there is
nothing new that could reach a provider. When extraction exists, extracted text is untrusted
input and a disclosure category of its own; it does not inherit the metadata disclosure
consent, and that requires its own review before any content leaves the machine.

## Deferred Gate

Content extraction is **not** accepted by this review. Before any file is opened, the next
review must cover: the consent step and exactly what it names; which formats are parsed and
by what; bounds on file size, count, and time; malformed and hostile document handling;
where extracted text is stored and how it is deleted; and disconnect behaviour for a
content-authorized folder, which `SqliteAuthorizedRootRepository.RemoveAsync` does not yet
support.
