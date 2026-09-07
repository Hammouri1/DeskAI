# Current Capability Security Audit

- Date: 2026-09-07
- Scope: V0.1 and V0.2 steps 1–8
- Result: Current application code cannot mutate user files

## Audited Capability Surface

The WinUI shell exposes navigation, a generated practice preview/executor/undo flow, and an optional native picker for read-only metadata preview. The picker requires a second explicit confirmation and produces only a revocable `MetadataOnly` authorization. It exposes no real-folder move, rename, delete, content-read, approval, or execution action.

Source searches found no process, shell, PowerShell, CMD, registry, installer, executable-launch, or network implementation. `DeskAI.AI` contains only the unavailable Rule Engine Only provider and receives no filesystem service.

## Filesystem Touchpoints

1. `SqliteDatabaseInitializer` creates DeskAI's own Local App Data directory and local database. It does not enumerate personal folders.
2. `WindowsMetadataScanner` can read names, size, timestamps, and attributes only when code supplies an `AuthorizedRoot`. It never opens file content, rejects protected/unsupported roots, checks root path components for reparse points, skips protected entries/links, and is bounded/cancellable.
3. `OrganizationPlanner`, classification, and recipes use path strings and immutable records in memory. They have no filesystem dependency.
4. `TemporaryDemoPlanExecutor` is the only product file-mutation implementation. It internally chooses a unique root under Windows Temp, creates known dummy files, requires a per-session ownership marker, rejects reparse points, validates approval and policy again, executes only selected allow-listed operations, and refuses existing destinations. It accepts no path from the UI, model, or user.
5. `DemoOrganizationPlanFactory` builds `FileItem` records matching those known dummy names. It never discovers files or accepts external file metadata.
6. `SqliteOperationJournal` records the transaction and every selected operation before mutation. Undo may call non-recursive directory deletion only for an exact journaled directory inside the currently authenticated demo root after verifying it is empty. There is still no file deletion or recursive product deletion.
7. Recursive deletion exists only in the test-owned temporary-directory cleanup helper, which verifies both the system-temp boundary and its unique owned directory before cleanup.
8. `ReadOnlyFolderService` canonicalizes the selected picker path, rejects network/device/drive-root forms, rejects links in the root chain and permanent protected-location overlap, persists only `MetadataOnly`, and invokes the bounded metadata scanner. `PlanValidator` rejects every mutation proposal for this scope.

## Personal and Sensitive Data Result

The current UI can scan metadata only after the user selects and confirms a folder. It reads names, sizes, timestamps, and attributes within depth/entry bounds; it does not open content. Sensitive configured locations and any ancestor containing them are refused. The selected root cannot reach the executor because its persisted scope is metadata-only and Safety blocks a plan even if one is constructed elsewhere. Starting the separate practice demo allows mutation only of generated dummy files beneath its verified temporary root. Automated filesystem tests use unique test-owned system-temp sandboxes; no test selects or enumerates personal known folders.

This result does not grant permission for real-folder mutation. A later scope upgrade requires a new security review and cannot reuse metadata-only consent. Same-session demo undo is validated; an older interrupted demo is marked for review rather than accessed with a new session identity.
