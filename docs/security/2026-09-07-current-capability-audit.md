# Current Capability Security Audit

- Date: 2026-09-07
- Scope: V0.1 and V0.2 steps 1–7
- Result: Current application code cannot mutate user files

## Audited Capability Surface

The WinUI shell exposes navigation, a preview made from generated metadata, per-operation selection, and an explicit controlled-demo run. It has no folder picker, personal-root authorization, UI scanner command, history action, or undo action. The preview invokes the classifier, planner, and Safety validator; it never invokes the filesystem scanner.

Source searches found no process, shell, PowerShell, CMD, registry, installer, executable-launch, or network implementation. `DeskAI.AI` contains only the unavailable Rule Engine Only provider and receives no filesystem service.

## Filesystem Touchpoints

1. `SqliteDatabaseInitializer` creates DeskAI's own Local App Data directory and local database. It does not enumerate personal folders.
2. `WindowsMetadataScanner` can read names, size, timestamps, and attributes only when code supplies an `AuthorizedRoot`. It never opens file content, rejects protected/unsupported roots, checks root path components for reparse points, skips protected entries/links, and is bounded/cancellable.
3. `OrganizationPlanner`, classification, and recipes use path strings and immutable records in memory. They have no filesystem dependency.
4. `TemporaryDemoPlanExecutor` is the only product file-mutation implementation. It internally chooses a unique root under Windows Temp, creates known dummy files, requires a per-session ownership marker, rejects reparse points, validates approval and policy again, executes only selected allow-listed operations, and refuses existing destinations. It accepts no path from the UI, model, or user.
5. `DemoOrganizationPlanFactory` builds `FileItem` records matching those known dummy names. It never discovers files or accepts external file metadata.
6. `SqliteOperationJournal` records the transaction and every selected operation before mutation. Undo may call non-recursive directory deletion only for an exact journaled directory inside the currently authenticated demo root after verifying it is empty. There is still no file deletion or recursive product deletion.
7. Recursive deletion exists only in the test-owned temporary-directory cleanup helper, which verifies both the system-temp boundary and its unique owned directory before cleanup.

## Personal and Sensitive Data Result

The current UI has no route that can scan or mutate Desktop, Downloads, Documents, Pictures, cloud-sync locations, credentials, or another user-selected folder. Starting a demo locks selection, creates a one-time approval bound to the displayed revision, then allows the executor to mutate only generated dummy files beneath its verified temporary root. Launching the app otherwise creates only its SQLite state under Local App Data. Automated mutation tests use unique test-owned system-temp sandboxes and an outside sentinel.

This result describes the current controlled-demo code, not permission for real-folder mutation. Real roots remain unavailable and require the step-8 authorization review. Same-session undo is validated; an older interrupted demo is marked for review rather than accessed with a new session identity.
