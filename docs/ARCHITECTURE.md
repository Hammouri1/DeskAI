# Architecture

## Goals

The architecture must make unsafe behavior difficult to express, keep UI and providers replaceable, work offline, and support incremental delivery. It favors clear boundaries over a large speculative framework.

## System Flow

```text
User-selected root
      ↓
Metadata scanner ──→ local index (SQLite)
      ↓
Deterministic classifiers ──→ optional AI classification
      ↓                              │
      └──────── structured facts/proposals ───────┐
                                                   ↓
                                           OrganizationPlan
                                                   ↓
                                            Safety validator
                                                   ↓
                                             Preview / edit
                                                   ↓
                                             User approval
                                                   ↓
                                      Revalidate + deterministic executor
                                                   ↓
                                         Filesystem + operation journal
                                                   ↓
                                                  Undo
```

Planning is side-effect free. Validation cannot execute. Execution cannot accept raw model text.

The V0.2 planner receives already classified `FileItem` values plus one versioned `FolderRecipe`. It deterministically produces typed create-directory/move proposals and typed `PlanIssue` values. It detects duplicate destinations, destinations occupied by scanned files, and required directory paths occupied by files. Conflicts remain visible in the plan, but Safety converts every conflict into a blocked validation result so a conflicted plan cannot be approved. The planner has no filesystem interface and never checks or changes live state.

## Projects and Dependencies

```text
DeskAI.App ───────────────→ DeskAI.Presentation
                                │
                                ├────────→ DeskAI.Core
                                ├────────→ DeskAI.Safety ───→ Core
                                ├────────→ DeskAI.Infrastructure ─→ Core contracts
                                └────────→ DeskAI.AI ─────────────→ Core contracts

DeskAI.Infrastructure ────→ DeskAI.Safety only when an adapter needs policy services
```

### `DeskAI.Core`

Provider- and UI-neutral domain types and use cases: authorized roots, file descriptors, classifications, plan operations, plans, rules, execution/undo results, and contracts for clocks, scanning, persistence, planning, execution, and classification. Core must not reference WinUI, SQLite, provider SDKs, or concrete Windows APIs.

### `DeskAI.Safety`

Pure and deterministic policy logic where practical: canonical path checks, root containment, protected-location policy, operation allow-list, collision policy, link/reparse-point policy, plan validation, and clear reason codes. It depends on Core domain types and exposes results; it does not perform mutations.

### `DeskAI.Infrastructure`

Filesystem adapters, known-folder integration, SQLite migrations/repositories, indexing, content-hash implementation, Recycle Bin integration, credential-store adapter, transaction journal, and the sole concrete file-operation executor. Windows and persistence details stay here.

It also holds `AutomaticCheckTimer`, the one clock in DeskAI that makes something happen without a person pressing anything. It owns no policy: every tick it asks Core whether a check is due. Whether one is due, and what a check may do, are decided in `AutomaticCheckSchedule` and `AutomaticCheckService` and tested there — the timer exists only because a timer cannot be a pure function.

### `DeskAI.AI`

Provider-neutral orchestration plus adapters for local or cloud endpoints. It constructs minimal data envelopes, requests structured results, validates syntax/schema, maps output to Core proposals, and returns errors without filesystem side effects. Provider SDK response types never escape this project.

### Tidying (V0.6)

`DeskAI.Core.Tidy` holds the tidy-a-folder use cases. `TidyPermissionService` grants and
withdraws the per-folder tidy permission (ADR 0019) after asking `IReadOnlyFolderService` to
re-check the folder. `TidySuggestionService` scans the folder fresh through `IFileScanner` —
never the index, which records how a folder looked rather than how it is — keeps loose
top-level files, leaves busy, recent, online-only, hidden, and unknown files alone with
reasons, applies the person's rules ahead of file type (`TidyFolderRecipe`), resolves
same-name destinations, and returns a `TidyPreview` containing an `OrganizationPlan`. It asks
Safety about that plan through `IPlanSafetyCheck`, a Core contract Safety implements
(`PlanSafetyCheck`), so Core still never references Safety. The suggestion and AI services move
nothing. Step 3 adds `TidyRunService`, which turns the ticked moves into an `Approval` covering
exactly them and the folders they need, bound to the plan revision on screen, passes each
file's listed size and last-changed time as `ExpectedFile`, calls `IFolderTidyExecutor`, and
words the result without calling a partial run done; it also undoes a tidy on request. Step 4
gives it the folder's history: `FindLastAsync` for the last tidy after a restart, and
`FindInterruptedAsync`, `KeepInterruptedAsync`, and `UndoInterruptedAsync` for a tidy that
stopped part-way (ADR 0022).

Step 5 connects automatic checks to the page without giving them any reach.
`AutomaticCheckResult.FolderToReview` is the ID of the connected folder with the most rule
matches — an ID, not a plan, operation, or path. "Review in Organize" on the notice leaves it with
`OrganizeRequest` (a one-shot, UI-thread holder in Presentation); the window opens a fresh
Organize page, whose `InitializeAsync` takes it and selects that folder if it is still connected.
Because a check reads every remembered file but tidying moves only loose top-level files, the
page states how many files the person's rules place *there* rather than repeating the check's
count.

Step 2b adds AI as a suggestion source (ADR 0020). `TidySuggestionService.PreviewAsync` takes a
`TidySuggestionMode` and a dictionary of `TidyAiAdvice` by file ID; it still sends nothing.
Authority runs rules → AI (every-file mode) → file type → AI (default mode), and advice whose
file has a different size or last-changed time is ignored. The preview lists `AskableFiles`.
`TidyAiService` is the only code that sends real-folder information to AI, in two calls with
the person between them: `PrepareAsync` builds an `OrganizationSuggestionRequest` through the
existing `AiRequestBuilder` — random stand-in IDs, sharing choices capped at
`RealFolderShareable`, protected paths dropped via `IPlanSafetyCheck.IsProtected` — and
describes it line by line; `AskAsync` re-checks permission and settings, sends that same
request through `IOrganizationSuggestionProvider`, and maps answers back to advice. It holds
no scanner, reader, index, journal, or executor, and a test asserts that.

### `DeskAI.Presentation`

The logic behind every page: view models, commands, and
`AddDeskAiApplication`, the one registration of everything DeskAI is made of apart from the
window. It references no WinUI type, so each page can be tested the way a person uses it.
Its namespaces stay `DeskAI.App.*` because these are the app's view models, compiled
separately.

It was split out on 2026-09-10. Until then view models lived inside the WinUI project, which
tests cannot load, so none of the 545 engine tests covered a single page — and every bug the
owner found by hand was in that untested layer. `DeskAI.Presentation.Tests` builds DeskAI
through the same `AddDeskAiApplication` call the app uses, with a real SQLite database and
real safety checks in a generated temp folder, replacing only the credential store, the
network, and Windows notifications.

### `DeskAI.App`

WinUI 3 views, reusable controls, converters, dialogs, navigation, Windows adapters (folder
picker, notifications), and the composition root, which calls `AddDeskAiApplication` and adds
only the Windows-facing pieces. Code-behind is limited to view behavior and confirmation
dialogs. App must not manipulate files or call provider HTTP APIs directly.

### My workspace (V0.7, ADR 0026)

`DeskAI.Core.Workspace` holds the first V0.7 slice: starter packs and pinned saved searches.
Nothing in it changes a file.

`StarterPackCatalog` is a fixed list of five `StarterPack` values, each a set of
`StarterPackSearch` (name and phrase) and `StarterPackRule` (name, typed conditions, destination).
`StarterPackRule.ToRule` is the one place a pack rule becomes an `AutomationRule`, through the
ordinary factory and `MoveToFolderAction` checks, always with `isEnabled: false`.

`StarterPackService.PreviewAsync` lists what a pack would add and why anything would be skipped —
a name already used, compared case-insensitively, or the 50-search limit — and saves nothing.
`AddAsync` works that plan out again from stored state rather than trusting the preview, saves
through `ISavedSearchRepository` and `IRuleRepository`, pins new searches while fewer than
`SavedSearch.MaxPinned` (8) are pinned, and returns a `StarterPackOutcome`. A failure part-way
leaves what was added and says so. `PinnedSearchService` pins and unpins, and counts a pinned
search through `FileSearchService`, returning `PinnedCount` with a kind that keeps a count apart
from "no folders" and "not understood", and marks a count that reached the search limit. Both
services take only repositories, `FileSearchService`, and the clock; tests fail if either is given
an executor, journal, planner, scanner, reader, credential vault, or AI provider.

In Presentation, `WorkspaceViewModel` drives the page and `SearchRequest` — shaped like
`OrganizeRequest` — hands one saved-search ID to `SearchViewModel`, which takes it once in
`InitializeAsync` and runs that search as pressing Run would. `WorkspacePage` shows the pack
preview in a `ContentDialog` and navigates through `MainWindow.GoTo` so the side menu follows.

### Folder templates (V0.7 piece C, ADR 0027)

`DeskAI.Core.Templates` is deliberately a different namespace from `Workspace`: a reflection test
asserts no `Workspace` type holds the executor or the journal, and `FolderTemplateService` is the
one My workspace service that does. It makes empty folders and nothing else.

`FolderTemplateCatalog` is five fixed `FolderTemplate` values, one per starter pack, each folder
name matching that pack's rule destinations (a test checks both). `FolderTemplate.Own` wraps names
a person typed once `FolderNameCheck.Parse` has accepted them: single plain names, no separators or
drive letters or traversal, none of the characters Windows refuses, no device names, no trailing
dot, up to 8, no duplicates, each refusal a sentence a person can act on.

`FolderTemplateService.PreviewAsync` (or `PreviewOwnAsync`) finds the root, requires
`RootCapabilities.CanTidy`, runs `CheckStillSafeAsync`, refuses while the folder has an unfinished
journal record, then asks `IFolderNameLookup` — a new narrow contract, implemented by
`FolderNameLookup` in Infrastructure, that lists the folder's top level once and reports for each
name whether a folder, a file, a link, or nothing is there, by the name on disk — and builds an
`OrganizationPlan` of `CreateDirectoryOperation`s for the missing names. `IPlanSafetyCheck` sees
every name; a blocked one becomes a "Can't be made" line and is left out of the plan. The preview
carries the plan. `MakeAsync` looks again from fresh state; if a different set of folders would be
made it makes nothing and returns the fresh preview for the page to show. Otherwise it approves
exactly the previewed plan's operations and calls `IFolderTidyExecutor.ExecuteAsync` with no
expected files. It reads the journal record afterwards so a folder the executor found already
there is reported as such and never as made. `UndoAsync` needs the tidy permission and calls the
executor's undo, which removes only recorded, still-empty folders. `FindLastAsync` reads the
folder's newest records and offers the latest run made only of create-folder operations, if it
has not been undone and nothing ran in the folder since.

### Desktop and wallpaper (V0.7 piece E, ADR 0029)

`DeskAI.Core.Desktop.WallpaperService` is the one place DeskAI changes a Windows setting. It
takes three narrow contracts from `Core.Abstractions` and nothing else: `IWallpaperSetter`
(`ReadCurrent`, `Set`), `IPictureInspector` (kind and size of one path, never its contents), and
`IAppSettingsStore` (the `app_settings` key/value table). `PreviewAsync` checks the picked path
— fully qualified, local, not UNC or a URL, jpg/jpeg/png/bmp, an existing plain file that is not
a reparse point, 1 byte to 50 MB — and describes what Windows shows now. `UseAsync` checks again,
writes the current wallpaper to `wallpaper.previous` before calling the setter (unless DeskAI's
own last-set picture is still showing and a previous is already recorded), then records
`wallpaper.set`; if Windows refuses, the recorded previous is rolled back so Put back is not
offered for a change that never happened. `FindRestoreAsync` describes the recorded previous and
whether Windows now shows something else; `PutBackAsync` restores it (an empty string means a
plain colour) if its file still exists, then clears both keys.

Infrastructure implements the three contracts in `Infrastructure/Desktop`:
`WindowsWallpaperSetter` (`SystemParametersInfoW` with `SPI_GETDESKWALLPAPER` /
`SPI_SETDESKWALLPAPER`, `SPIF_UPDATEINIFILE | SPIF_SENDCHANGE`; classic `DllImport`),
`FilePictureInspector`, and `WindowsKnownFolders` (`Environment.GetFolderPath`), plus
`SqliteAppSettingsStore`. The app adds `IPicturePickerService` (`FileOpenPicker`, picture
extensions only) beside the folder picker.

`WorkspaceViewModel.PreviewWallpaperAsync` / `UseWallpaperAsync` / `PutWallpaperBackCommand`
drive the wallpaper card; `ConnectDesktopAsync` asks `IKnownFolders.Desktop`, reuses an already
connected Desktop or calls `ConnectedFolderService.ConnectAsync` (the picker's path: metadata
scope, bounded scan, policy), then leaves the folder ID in `OrganizeRequest` for the page to
navigate to Organize, where `TidyViewModel` takes it and asks permission as usual. `TestApp`
replaces `IWallpaperSetter` with a recording one and `IKnownFolders` with a folder inside its
own temp directory, and asserts the latter, so no test can reach the real wallpaper or Desktop.

### Tidy while I'm away (V0.9, ADR 0031)

`DeskAI.Core.Tidy.AwayTidyApproval` is the standing yes: the folder and every enabled rule at its
version, with `Covers(rules)` returning the same `RuleApprovalCheck` statuses ADR 0016 uses, minus
the outcome fingerprint. `AwayTidyRun` records one unattended run (counts, time, transaction,
stop reason, seen time). `IAwayTidyRepository` / `SqliteAwayTidyRepository` store both in
`away_tidy` and `away_tidy_runs` (schema 14), cascading with the folder; the approval insert
selects through `tidy_permissions`, so a yes for a folder that may not be tidied inserts nothing.

`AwayTidyService` implements `IAwayTidyRunner`, the one parameter `AutomaticCheckCoordinator`
takes that can move a file (`AutomaticCheckService` still takes none). After a check that ran,
the coordinator calls `RunAllAsync`, which for each active approval re-checks the rules, the
folder, and the permission, asks `TidySuggestionService` for the ordinary preview with no AI
advice and no keep-both choices, refuses on a scan problem or any rule-placed clash, takes at
most `AwayTidyLimits.MaxFilesPerRun` rule-placed moves, and runs them through
`TidyRunService.TidyAsync` — the same approval, executor, journal, and per-file re-checks as a
hand tidy. A refused file stops the mode after the run. The coordinator raises `Tidied` with an
`AwayTidySummary` (counts, one folder name, the folder to review), which `ShellViewModel` turns
into the notice and a count-only notification. `AwayTidyWords` holds the sentences every page
uses so no promise can drift from the mode: `TidyViewModel` (switch, line, card),
`DashboardViewModel` (pill, hero sentence), `AutomationViewModel` (first card, summary), and
`BackgroundCheckingChoice.Ask/MoreDetails` (the keep-running dialog) all read
`CountActiveAsync`.

### Back up, restore, and Start fresh (V0.8, ADR 0030)

`DeskAI.Core.Backup` holds `BackupService` and `FreshStartService`; neither takes anything that can
reach a file on disk, and `BackupPageTests` asserts it by reading their constructors.

`BackupService(IRuleRepository, ISavedSearchRepository, IClock)` turns the stored rules and saved
searches into a `DeskAiBackup` (version, time, `BackupRule` = name + `RuleConditionData`s +
`RuleActionData`, `BackupSearch` = name + phrase + pinned) and back. `ToText` serializes with
`System.Text.Json`; `Parse` is strict (1 MB, unknown members refused, newer version refused, at
most 200 items). `PreviewAsync` rebuilds every rule through `RuleCodec` and
`AutomationRule.Create` with `isEnabled: false` and every search through `SavedSearch.Create`,
marks a name already stored (case-insensitive) or a rule that fails those checks as skipped with
a reason, and adds nothing. `RestoreAsync` works the plan out again from stored state and saves
only the accepted lines with new IDs; a pin is kept only while fewer than eight are pinned. The
on/off flag is not in the file, so "restored rules arrive off" is structural.

`FreshStartService` forgets DeskAI's memory in a fixed order: every folder through
`ConnectedFolderService.DisconnectAsync` (cascading to index, permissions, plans, journal), any
root left in the repository, rules, saved searches, every catalog service's credential and the AI
settings, check history and settings, the look, and the two wallpaper keys. It touches no file.

`IUserFileStore` (Core) is the one contract for a file the person chose in a Windows dialog;
`UserFileStore` (Infrastructure, `Files/`) accepts only a fully qualified local `.json` path,
refuses a link and an oversized file before opening, and opens read-only. The app adds
`IBackupFilePickerService` (`FileSavePicker` / `FileOpenPicker`, `.json` only) beside the other
pickers; `SettingsViewModel` drives the card and the page shows the preview dialog.

### DeskAI's look (V0.7 piece D, ADR 0028)

`DeskAI.Core.Appearance` holds `ThemeMode` (follow Windows, light, dark), `LookPalette` (five
neutral "#RRGGBB" colours), `DeskLook` (id, name, line, a dark and a light palette),
`DeskLookCatalog` (Slate, Graphite, Sand, Ocean, Lavender, Rose; Slate is the default), and `AppearanceSettings`
(mode and look id, with `Look` falling back to Slate for an unknown id).
`IAppearanceSettingsRepository` is implemented by `SqliteAppearanceSettingsRepository` on the
`app_settings` key/value table that has existed since schema 1, so no schema change was needed;
unrecognised stored values fall back to the default. Presentation defines `IAppearanceApplier`
with a no-op, the way `IBackgroundPresence` is done, and `WorkspaceViewModel` saves the choice
then calls the applier. The app replaces the no-op with `WindowsAppearanceApplier`, which
recolours the brushes in `DeskAITheme.xaml`'s dark and light theme dictionaries in place and sets
`RequestedTheme` on the window's root element; `App.OnLaunched` applies the stored choice before
the window is activated. Page tests use a recording applier.

The recovery fix that made this safe: `FolderTidyExecutor.Settle` measures a record with no move
operations by the folders it made rather than the files it moved (before, such a record settled
as `Failed` and could never be undone), `InterruptedTidy` carries `MadeFolders` and `TotalFolders`
with `IsFolders`, and `TidyRunService.UndoAsync` words a folder-only undo in folders removed.
Organize shows "DeskAI stopped while making folders: 1 of 2 folders made." with **Remove that
folder** / **Keep it**.

## Initial Domain Model

Names may evolve, but concepts should remain explicit:

- `AuthorizedRoot`: canonical root, display name, permission level, and stable ID.
- `FileItem`: stable scan identity, relative path, kind, size, timestamps, and selected metadata. Avoid spreading absolute paths into UI/provider objects.
- `Classification`: category, source (`Rule`, `Heuristic`, `LocalAI`, `CloudAI`, `User`), confidence, and reason.
- `OrganizationPlan`: ID, root ID, created time, policy version, operations, warnings, and state.
- `PlanOperation`: operation ID, type, relative source/destination, reason, provenance, and safety result.
- Operation types initially: `CreateDirectory`, `MoveFile`, `RenameFile`; later `AddTag` and `SuggestRecycle`. No arbitrary-command operation exists.
- `ValidationResult`: status (`Allowed`, `Warning`, `Blocked`) plus machine-readable reason codes and user-readable explanation.
- `Approval`: the exact plan revision and selected operation IDs the user approved.
- `ExecutionTransaction`: plan/approval IDs, per-operation before/after facts, state, timestamps, errors, and undo state.
- `OrganizationRule`: typed conditions, typed actions, authorized scope, approval metadata, enabled state, and version.

Prefer immutable records/value objects for plans and validation output. Use enums or closed discriminated-style hierarchies for operation kinds; never accept an action name to reflectively invoke code.

## Plan Lifecycle

```text
Draft → Validated → Approved → Executing → Completed
                      │            ├→ PartiallyCompleted
                      │            └→ Failed
                      └→ Expired/Cancelled

Completed/PartiallyCompleted → Undoing → Undone/PartiallyUndone/UndoFailed
```

Plans are versioned. Editing exclusions or destinations creates a new revision and invalidates previous approval. A plan should expire when inputs or policy have materially changed. The executor re-reads relevant facts and revalidates immediately before mutation.

## Execution and Undo

The executor processes a bounded approved set, records intention and outcome, and returns per-operation results. A folder-wide plan is not assumed to be an atomic filesystem transaction. On failure, stop or continue according to an explicit policy and show exact partial state.

Undo is a compensating transaction, not time travel. It verifies that the destination still represents the executed item and that the old path is safe/free. If conditions changed, it blocks or asks for conflict resolution. Directory removal is eligible only when DeskAI created it and it remains empty.

The V0.2 step-5 implementation is intentionally narrower than the future production executor. `TemporaryDemoPlanExecutor` generates its own unique root beneath a configured base that must itself be contained by the Windows temporary directory. It seeds only known dummy files and requires an unpredictable ownership marker. Before every selected operation it rechecks root identity, containment, existing path components for reparse points, current Safety results, approval identity/revision/policy, source existence, destination availability, and parent existence. It never overwrites. No executor API accepts the root chosen by the step-8 picker; that picker feeds only the metadata preview service.

Step 7 places `IOperationJournal.CreateAsync` before the first mutation, after persisting the exact root and plan revision. Each operation transitions from Pending to InProgress and then Completed, Failed, or Cancelled. Transaction summaries distinguish completed, partially completed, failed, and cancelled outcomes. Recovery checks an InProgress operation against live size/timestamp/path facts only when the current executor still owns the same marked demo root. Records from an older process remain `RecoveryRequired`; the new process does not touch an old temporary workspace it cannot authenticate.

**Since V0.6 step 3 (ADR 0021) the rules above live in one place, `FileOperationRunner`,** and two
executors use them. `TemporaryDemoPlanExecutor` trusts its workspace by marker as described;
`FolderTidyExecutor` trusts a connected folder by a live check before the run and before every
operation — still connected, `CanTidy`, same canonical path, and `CheckStillSafeAsync`. Each
move also re-checks the file against the size and last-changed time recorded from the list,
refuses online-only, hidden, and system files and any link in the path, reports a sharing
violation as "open in another program", and moves with `overwrite: false`. Undo checks that the
record's plan belongs to the executor's own folder, so neither executor can undo the other's
work, and practice recovery leaves a connected folder's interrupted record for that folder's own
recovery (V0.6 step 4). The real-folder executor runs one tidy or undo at a time.

**Since 2026-09-11 (ADR 0023) `FolderTidyExecutor` is the only executor.** The practice page and
`TemporaryDemoPlanExecutor` described above were retired, with `IPlanExecutor`, `IUndoService`,
and `DemoWorkspaceOptions`; the paragraphs about them are history. `ControlledDemo` remains as a
stored scope so a database from before then still reads correctly, and such a folder is never
searched, tidied, undone, or checked.

Undo reads completed journal operations in reverse order. A moved file returns only if its current size and modification time still match the recorded pre-move facts and its original path is free. A created directory is removed only if it was recorded, remains inside the owned root, is not a link, and is empty after file reversals. Undo creates its own journal transaction. For the practice workspace, undo is offered only during the same application session, because a new process cannot authenticate an old workspace.

**Since V0.6 step 4 (ADR 0022) a connected folder's undo survives a restart,** because that folder is trusted by a live check rather than a secret held in memory. `IOperationJournal.ListForRootAsync` reads one folder's records (joined through its plans), and `TidyRunService.FindLastAsync` offers the latest tidy that moved a file and was not undone. A record no run finished is handled by `FolderTidyExecutor.CheckInterruptedAsync`: pending operations never started; an in-progress one is judged by `FileOperationRunner.CheckInterrupted` from names, sizes, and dates — moved, not moved, or `NeedsReview` (an appended journal state undo never touches) — and the record waits as `RecoveryRequired` until `CloseInterruptedAsync` closes it as the person answered. A folder with such a record accepts no new tidy or undo. `RunLockFile` — a file beside the database opened with no sharing — is held with the in-process lock for every tidy, undo, check, and answer, so two DeskAI processes never run at once and an unfinished record seen under the lock is always a dead run's. Disconnecting a folder erases its plans and journal in the same transaction.

## Persistence

SQLite is local application state, not a source of authority over the current filesystem. Suggested logical areas:

- schema migrations and application settings;
- authorized roots and protected entries;
- file index and classifications;
- plans, operations, approvals, execution transactions, and undo records;
- rules and automation runs;
- AI provider settings without secrets.

Use migrations, foreign keys, transactions, indexes, UTC timestamps, and an explicit retention strategy. Repositories are justified when they separate Core use cases from SQLite—not as one generic repository for every table. API keys stay in a Windows-protected credential store and SQLite holds only a credential reference.

Schema version 1 introduced migration tracking, local settings, and authorized roots. Versions 2–3 added plans, operations, journal outcomes, and undo links. Version 4 added authorization scope. Version 5 adds non-secret AI mode, endpoint/model, disclosure flags, limits, consent, and a credential reference. Version 6 adds an atomic per-provider daily request counter. Version 7 adds `indexed_files`, keyed on `(root_id, file_id)` with a foreign key to `authorized_roots` using `ON DELETE CASCADE`, plus indexes on path, name, category, size, and modification time. Version 8 adds `saved_searches`, holding a name, the typed phrase, and a creation time. Version 9 adds `automation_rules`, holding a name, version, enabled flag, the conditions as a JSON array of plain kind/value pairs, and the action as a kind/value pair; like saved searches it has no root column, for the same reason, and its name index is case-insensitive so two rules cannot differ only by capitalisation. It deliberately has no root column and no foreign key: a saved search must not be able to outlive or widen an authorization, so scope is resolved from the authorized roots each time one runs. A case-insensitive unique index on the name stops two saved searches differing only by capitalisation. Versions 10–12 add automatic-check settings, check history, and the tidy permission. Version 13 (V0.7) adds `is_pinned` to `saved_searches`, defaulting to 0 so existing searches start unpinned; the migration looks for the column first because SQLite has no "add column if missing". Saving a search again never changes its pin. API-key bytes never enter SQLite.

Version 4 previously recorded the constant `CurrentSchemaVersion` instead of the literal `4`, so no database ever stored that row. The migration now records `4`, and `INSERT OR IGNORE` backfills it on existing installations.

## Scanning and Indexing

Scanning must be asynchronous, cancellable, bounded, and resilient to access-denied, disappearing files, long paths, cycles, and individual metadata failures. Do not follow reparse points by default. Stream results rather than loading unbounded trees into memory. Store normalized root-relative identifiers and refresh incrementally later.

The V0.2 scanner contract streams a closed `ScanEvent` hierarchy: `FileDiscovered` carries metadata-only `FileItem` data, while `ScanIssue` carries a root-relative location, reason code, and sanitized explanation. `MetadataScanOptions` requires explicit depth and entry bounds. The Windows adapter inspects the authorized root's path components for reparse points, uses an iterative directory stack, and never opens file contents. Classification remains a separate stage, so scanned files initially have `FileKind.Unknown`.

Step 8 adds `IReadOnlyFolderService` between the native picker and scanner. Only the picker supplies a path, a second dialog confirms the exact metadata disclosure, and the service persists a `MetadataOnly` root after canonical-path, protected-location, unsupported-root, and reparse-point checks. The UI scan is capped at depth 3 and 250 entries. `PlanValidator` rejects every plan bound to a metadata-only root, so picker consent cannot flow into the executor.

Content extraction is a separate, permission-gated pipeline with file-size/type limits and sandbox considerations. It is not part of the initial metadata scanner.

### Local metadata index (V0.4 step 1)

`MetadataIndexService` is the only route from the filesystem into `IFileIndex`. It refuses a `Protected` root and any root `IPathPolicy` blocks, then consumes the same bounded, content-free `IFileScanner` stream and the same deterministic classifier used by planning. Each discovered file becomes an `IndexedFile` holding a root ID, a normalized root-relative path, derived name/extension, kind, category, size, timestamps, and when it was last confirmed. Absolute paths and file content are never stored.

`IFileIndex` exposes no unscoped read: `SynchronizeRootAsync`, `ListForRootAsync`, `GetStatisticsAsync`, and `ClearRootAsync` each name one root. `SqliteFileIndex` compares the new scan against stored rows and writes only real differences, returning a `FileIndexSyncResult` of added, updated, unchanged, and removed counts so the UI can honestly say nothing changed. "Removed" means an index row was forgotten, never that a file was deleted.

The index is a cache, not authority. It proves only how a file looked when last scanned, so a future executor must still revalidate live state before mutating anything. No code path lets an index row become an approved operation, and nothing indexes automatically in this slice.

### Structured search filters (V0.4 step 2)

`SearchQuery` is a Core value describing what to look for: text matched anywhere in the root-relative path, file endings, categories, kinds, a size range, a modification-date range, and a result limit. It is validated in its constructor, so an invalid query cannot exist. Two rules matter more than the rest:

- **A query carries no root ID.** The root is a separate argument to `IFileIndex.SearchRootAsync`, so a query assembled from untrusted input — a natural-language translation in step 3, for instance — cannot select a folder the caller did not authorize.
- **A contradictory filter refuses rather than widens.** A minimum larger than a maximum, or a range that starts after it ends, throws. Degrading silently into "match everything" would turn a mistake into an unintended full listing.

The limit is mandatory and capped, so there is no unlimited search. Text is bounded in length, and file endings are rejected if they look like a path or a pattern rather than a suffix.

`SqliteFileIndex.SearchRootAsync` assembles its `WHERE` clause from fixed fragments and binds every value as a parameter, so no query string ever contains caller text. `LIKE` wildcards inside search text are escaped, which keeps a file genuinely named `report_final` from matching `reportXfinal`. Date ranges normalize both the stored column and the boundary to UTC through `strftime`, because stored timestamps keep whatever offset the file carried and a raw string comparison would order them wrongly.

Search is a read of remembered metadata. It opens no file, produces no plan, and cannot become an operation.

### Storage summaries (V0.4 step 5)

`StorageSummaryService` builds the storage picture across connected folders: size and count per category, the largest files, and how much has not changed in six months. Scope comes from `FileSearchService.IsSearchable`, so a summary can never describe a folder search would not look in.

Aggregation happens in SQL through `IFileIndex.SummarizeRootAsync` rather than by loading every row, so a folder with a hundred thousand remembered files costs about the same to summarize as one with ten. The age cut-off is compared in UTC through `strftime` for the same reason search ranges are.

The six-month threshold is a named constant, `StorageSummaryService.OldFileAge`, and the UI states it as "not changed in 6 months" rather than implying a judgement DeskAI has not made. The summary carries `LastCheckedUtc` so the page can say when the numbers were true instead of implying they are live, and `FoldersIncluded` so it can state real scope.

A summary describes and never proposes. It produces no plan, and the page deliberately offers no cleanup action, because any cleanup must still go through the ordinary preview and approval path.

### Possible duplicates (V0.4 step 6, stage 1)

`DuplicateFinderService` groups remembered files by exact byte size. This is the cheap first stage: it rules out the overwhelming majority of pairs, costs one grouped query per folder, and opens no file.

Sizes are merged across roots before deciding what repeats. A per-root `HAVING COUNT(*) > 1` would have been cheaper but wrong: a file copied into a second connected folder appears once in each, and filtering per root would hide exactly the case worth finding. `GetSizeCountsAsync` therefore returns single occurrences too, bounded by the number of distinct sizes.

Files below 4 KB are ignored, because small files collide on size constantly and would bury real candidates in noise. Groups are capped at 50, largest possible saving first.

**Stage 2 was deliberately absent from this service, and still is.** Hashing reads the bytes of a file. These folders are authorized `MetadataOnly`, which does not permit that, so confirming duplicates belongs with the permission-gated content work in step 8 rather than being slipped in behind a size check. Everything the UI says is therefore hedged: "possible duplicates", "might be duplicated", "up to" a saving. `ReclaimableBytes` is a ceiling on what could be freed if the copies turn out identical, never a promise. A test asserts the service reads no content at all.

### Confirming duplicates (V0.4 step 6, stage 2 — 2026-09-11, ADR 0024)

`DuplicateCheckService` (Core) confirms copies only when asked, in two calls with the person between them. `PrepareAsync` lists the files in the size groups above — at most 200, whole groups only — and opens nothing; the page shows the count, folders, and size to be read. `CompareAsync` reads only files in that question, after re-checking each folder is connected and searchable. It reads the first 64 KB of each file and reads to the end only files whose beginnings match another's; files over 2 GB and anything past 8 GB per check are reported as not compared. Equal size and equal SHA-256 means identical. Nothing is stored and no permission is kept, so every check asks again.

It reads through `IFileFingerprinter`, implemented by `FileFingerprinter` in Infrastructure — since then the second place DeskAI opens a file, beside `PlainTextExtractor`. It takes the folder as a separate argument and refuses, before opening: a folder not connected for reading, a protected path, a path leaving the folder, a link on the way or at the file, an online-only file, and a file whose size or last-changed time differs from what DeskAI remembered. It opens read-only letting others only read, re-checks for a link after opening and for changes after reading, and returns a fingerprint kept nowhere. A test asserts `DuplicateCheckService` is the only type in Core that can take it.

### Content-access capability gate (V0.4 step 8, stage 1)

`RootCapabilities` is the single place that decides what an authorized folder permits: `CanReadMetadata`, `CanReadContent`, `CanMutate`. Each lists the scopes that grant it and denies everything else, and each checks permission alongside scope so a restricted or protected folder grants nothing whatever it was connected for.

This replaces direct comparisons such as `scope == MetadataOnly`, which decided both "may be searched" and "may not be changed". That pattern fails open: adding a fourth scope would have dropped it out of the mutation block and made it changeable, with no line of the check appearing to change. Capabilities fail closed instead — a scope added later arrives with no rights until it is granted them deliberately, and a test breaks if a scope is added without a row in the capability matrix.

`RootAuthorizationScope.MetadataAndContent` is appended as value 3. Scopes persist as integers, so values are only ever appended; inserting one would re-label folders a person already connected. It grants metadata plus permission to open files, and no permission to change them. The separation runs both ways: a folder connected for organizing may not have its contents read either.

**No extraction code, no UI, and no way to grant the scope exist yet.** The three places that construct an authorized root produce `ControlledDemo` and `MetadataOnly` only. The gate is built and tested before the capability it guards, which is the order `SECURITY.md` asks for. See ADR 0014 and the gate review in `docs/security/`.

### Plain-text extraction (V0.4 step 8, stage 2)

`PlainTextExtractor` was, until 2026-09-11, the only code in DeskAI that opens a file; `FileFingerprinter` (confirming duplicates, ADR 0024) is now the second. `IContentTextExtractor` is deliberately the narrowest interface that can do the job: one file per call, named relative to a root the caller must supply, bounded by options, returning inert text. The root is a separate argument for the same reason a search query carries none — the permission travels with the call and cannot be chosen by whatever assembled the path.

Checks run in order, first refusal wins. `RootCapabilities.CanReadContent` comes first, before any path work, so an unauthorized folder never reaches a path calculation let alone a handle. Then path policy on root and relative path; then the extension, which is what makes an unsupported file never get touched at all; then canonical containment inside the root, with a separator required after the prefix so a sibling folder with a similar name is not mistaken for a child.

Reading is `FileMode.Open` read-only, never `OpenOrCreate`, so asking for a missing file reports it rather than creating one. Links are refused before opening and checked again once the handle is open, because a file can be swapped for a link in between; the handle still refers to what was opened, so a link found afterwards abandons the read. A zero byte marks the file as not text rather than decoding it into convincing nonsense, and invalid UTF-8 is replaced rather than thrown, because the bytes are whatever happened to be in the file.

Reads are bounded to 64 KB from the beginning of the file — enough to tell what a document is about, which is the only reason the capability exists. Truncation is decided by whether bytes remain, not by whether the buffer filled, so a file of exactly the limit is complete. Extracted text is returned to the caller and stored nowhere.

Plain-text formats only; PDF and Office are excluded because parsing them runs a third-party parser over attacker-controlled binary structure (ADR 0015). Extracted text is untrusted input exactly like a file name: a test feeds prompt-injection wording through and asserts it comes back as inert text. **The extractor is registered in no container and called by nothing**, so it is unreachable from the running application until the consent step in stage 3 exists.

### Rule domain (V0.5)

V0.5 is where DeskAI first acts without someone watching, so the domain is built for that from the first commit. `DeskAI.Core.Rules` is pure: no clock, no filesystem, no database, no AI.

`RuleCondition` and `RuleAction` are closed sets of types, not an expression language. Everything a rule can test and do is readable in two files, and a rule assembled from untrusted text — typed today, model-drafted later — can only ever be a combination of them. Moving into a folder is the only action; deleting is absent and stays absent. Rules see a `RuleSubject` (name, ending, category, kind, size, modified time) rather than a `FileItem`, so no condition added later can reach an absolute path or file content.

Several refusals are load-bearing. A rule with no conditions is rejected rather than treated as "match all". Conditions are capped at eight and joined with AND only. Destinations are validated when the rule is written, not at run time, so a stored rule never holds something that would be refused every time it fires. Age conditions take the moment as an argument, so a simulation and the run after it answer identically.

`RuleSetEvaluator` returns `RuleRunPreview` — proposals, conflicts, and counts. **Conflicts are refused rather than resolved:** when two rules want a file in different places, picking the first or the most specific would be a guess about intent, so the file is left alone and the disagreement is reported. Rules agreeing on a destination are agreement, not conflict. The result does not depend on rule order, and a test asserts it. The evaluator returns proposals rather than plan operations on purpose: turning one into something executable goes through the ordinary planner, validation, preview, and approval.

Rules persist through `IRuleRepository` / `SqliteRuleRepository`. Decoding is the direction that matters: a stored row is input like any other, so `RuleCodec` is an explicit switch over known kind strings that throws on anything else — never a reflection- or type-name-driven deserializer. Nothing read from storage can name a type to construct, and a stored value still faces the same constructor checks a typed rule does, so a hand-edited `..\..\Windows` destination is refused coming *out* of the database. The kind strings are stored data and are pinned by a test. A row that cannot be understood throws rather than being skipped, because skipping would mean a rule quietly stops running while still appearing to exist.

`RuleDraftTranslator` reads a typed sentence such as "move invoices to Documents" into a `RuleDraft` for review. Deterministic and local with a fixed vocabulary and no AI — the same choice search made, for the same reasons: ordinary phrasings are handled conventionally, the reading is auditable in a way a model's is not, and the same sentence always drafts the same rule. When AI drafting arrives it must produce this same `RuleDraft` through the same validated path, so it gains no new reach.

A draft is deliberately not an `AutomationRule`. It fills the form and stops, because DeskAI understanding a sentence is not the same as someone agreeing to what it understood. Every part understood becomes a chip that is stated back, and a destination the domain refuses is carried as `DestinationProblem` rather than thrown — "I cannot use that folder name" is something to show someone.

One case is worth naming: a bare "*word* files" is only read as a file ending when the word is a type DeskAI knows. Without that check "invoice files" read as an ending of `.invoice` — a rule that would match nothing and that nobody meant to write. An explicit dot is always believed, so an unusual ending can still be typed directly. A test covers it.

`RuleSimulationService` is the practice run: it reads remembered metadata, evaluates rules in memory, and returns a description. It produces no plan, holds no executor, and touches no file. Each connected folder is simulated separately, because a destination is a folder *inside* the connected folder and two folders' "Documents" are different places. Scope comes from `FileSearchService.IsSearchable`, so rules can never describe a folder search would not look in — including the practice workspace — and a test asserts a simulation reads no file content even in a folder that granted it.

`RuleApproval` scopes permission to rules *as worded* and an outcome *as shown*. It stores each enabled rule at its version plus a SHA-256 fingerprint of the ordered "this file goes there" pairs. Editing, adding, removing, or disabling a rule invalidates it; so does a different folder. The fingerprint catches what version checks miss — nobody edits anything, a new file appears, and an untouched rule now wants to move it. `RuleApprovalStatus` keeps the reasons separate because "you edited a rule" and "there are new files to move" need different words in front of a person. Rule names are excluded from the fingerprint: renaming a rule changes nothing about anyone's files.

### Content consent and inside-file search (V0.4 step 8, stage 3)

`ConnectedFolderService.AllowContentAsync` and `StopContentAsync` move a connected folder between `MetadataOnly` and `MetadataAndContent`. Only those two scopes may be swapped between: the practice workspace and any organize-scoped folder are refused, so a consent belonging to the folder list cannot reach a scope that grants changes. Connecting a folder still grants metadata only, and a test asserts a freshly connected folder has no content permission — reading inside is a separate question, asked with its own dialog naming what is opened, what is not, and that nothing read is saved or sent.

This slice also closes the gap ADR 0014 recorded: `RemoveAsync` filtered on metadata-only alone, so allowing content access would have made a folder impossible to disconnect — a permission you could give and never take back. Both reading scopes are now accepted, the practice workspace still excluded.

`ContentSearchService` is the only consumer of file content. It selects roots with `RootCapabilities.CanReadContent`, chooses candidates from the index by remembered name so a file DeskAI would refuse to read is never offered to the extractor, and reads only through `IContentTextExtractor`, which refuses independently. Bounds: at most 50 files per search, 64 KB each, and phrases under three characters open nothing. The outcome carries how many files were read and whether the limit was reached, and the UI states it, so "nothing matched" is never mistaken for "nothing exists".

`TextFileFormats` holds the supported endings in Core, so the code choosing candidates and the code opening files cannot drift into disagreeing about which files get read. Snippets are file text — whitespace collapsed, control characters dropped — and are displayed and nothing more.

**2026-09-20 extension (ADR 0036).** `MetadataAndDocuments` is appended to the stored scope enum; old `MetadataAndContent` roots retain plain-text-only reach. `ConnectedFolderService.AllowDocumentsAsync` upgrades only after the new page confirmation. `RootCapabilities.CanReadDocuments` and the extractor both check the new scope. `OfficeOpenXmlReader` in Infrastructure reads selected Word/Excel XML parts through bounded BCL ZIP/XML streams; it neither creates files nor accesses AI. `ContentSearchService` now interprets the typed phrase once, uses format/category/size/date to narrow index candidates, and looks for the remaining word inside eligible files. `Let AI read this` may turn the person's own sentence into that query, but the model still receives only that sentence and no extracted file content. A content hit can satisfy a search even when the filename does not contain the word. The output reports file count, search limit, and partial extraction. PDF text was added in ADR 0037; image subjects remain separate.

**2026-09-21 extension (ADR 0039).** Two appended scope values represent an independent
slide-text grant with or without PDF permission. `RootCapabilities.CanReadSlides` gates both
candidate selection and extraction. `SlideOpenXmlReader` selects only numbered `.pptx` slide
XML parts and returns bounded text plus in-memory slide offsets. `ContentSearchService`
maps the matching offset to a slide label, and Presentation shows it beside the relative
folder location. No content or slide offset is stored or sent to AI.

The Search page's **Look in** choice is a connected-root ID selected from the local folder
list, never a path generated by a model or typed into a query. Both metadata and content
services apply that ID *after* their authorization predicate, so selecting a folder cannot
turn a disconnected or insufficiently approved folder into a search target.

### Organization health score (V0.4 step 7)

`OrganizationHealthCalculator.Evaluate` turns the storage summary and the possible-duplicate report into a score out of 100. It is a static, pure calculation: no dependencies, no I/O, no clock, no AI. It reaches no folder, so it can only ever describe what the two readings it is handed were already allowed to see.

The score is a weighted average of two measured parts, each carried as a `HealthComponent` holding the bytes and file count measured, its share of the total, its own part score, and its weight:

| Part | Zero-score limit | Weight |
| --- | --- | --- |
| Possible copies | 20% of space | 80 |
| Sitting unused (unchanged ~6 months) | 100% of space | 20 |

Each part falls in a straight line from 100 at nothing to 0 at its limit. Possible copies carries most of the score because it is the one finding metadata alone genuinely supports; its limit is generous because matching sizes are unconfirmed evidence, and scoring hard on an unproven signal would overstate what DeskAI knows. Age is deliberately weak in both dimensions: a settled archive is nearly all old by definition and is not a mess, so age nudges the score rather than deciding it. The thresholds and weights are named public constants, and a test asserts the total is exactly the weighted average of the parts shown, so a person can add the score up by hand.

**Both were recalibrated after the first run against realistic folders.** The original limits (10% copies, 60% age) scored a 3.4 GB archive at 53 — "Worth a look" — purely because 93% of it had not changed in six months. That is the score saying that moving more files is always better, which `UI-UX.md` forbids. A regression test now asserts that a folder whose only finding is age stays in the `Good` band.

**Unrecognised types are reported beside the score, never inside it.** `OrganizationHealth` carries `RecognisedShare` and `UnrecognisedFileCount`, and `IsRecognitionPartial` is true below `FullRecognitionShare` (95%). A file type DeskAI has not learned is a gap in this app's knowledge, not a mess the person made; charging them points for it would be both unfair and uninformative. The first run made this concrete — a 90-file folder scored 70 because 67 of its files used extensions the classifier did not cover. The fix was in two parts: stop scoring it, and widen `DefaultFileTypeRules` to cover everyday types (`.log`, `.ini`, `.toml`, `.iso`, `.m4v`, `.wma`, `.bat`, `.scss`, and others). Because categories are stored per row at index time, a widened rule set only reaches an existing folder when it is refreshed.

Two further honesty rules shape the result. Nothing connected, or nothing remembered yet, returns `HealthBand.NotMeasured` rather than a score, because scoring no evidence invents a judgement. And a share that would exceed the whole — possible when the two readings are gathered a moment apart — is clamped instead of producing a share above one.

`HealthComponentKind` identifies each part without wording it, so Core stays free of user-facing text and the UI decides how a part is named and explained. Like every other reading in V0.4, the score describes and never proposes: it produces no plan, and the page attaches no fix action to it.

### Natural-language query translation (V0.4 step 3)

`NaturalLanguageQueryTranslator.Translate` reads a short typed phrase such as "big videos from last month" into a `QueryTranslation`: the resulting `SearchQuery`, plus one `QueryChip` for every part it understood.

It is deterministic and local, with a fixed vocabulary and no AI. This follows the project rule that ordinary cases are handled conventionally and AI is reserved for genuinely uncertain ones, and `AI-PROVIDERS.md` sequences natural-language search *after* a typed query model exists. A fixed vocabulary is also auditable in a way a model's reading of the same sentence is not. When AI translation is added later it must produce the same `SearchQuery` type through the existing validated-suggestion path, so it gains no new reach.

Three properties matter:

- **Interpretation is visible.** Every filter the translator sets produces a chip written for a person to read back, so the UI can show its reading and let someone remove a part of it. Guessing silently is worse than guessing visibly.
- **Understanding nothing is reported, not disguised.** `QueryTranslation.UnderstoodAnything` is false when no part matched. The resulting query has no filters, so running it would list the whole folder; callers must say they did not understand rather than presenting that listing as a result.
- **Time is an argument.** `nowUtc` is passed in rather than read from the system clock, so "last month" resolves identically in tests and in the app, and the same phrase always produces the same query.

Vague words state their real meaning: "big" is a named constant and the chip says "Larger than 100 MB" rather than hiding the threshold. A vague word never contradicts an explicit number — "big files under 5 MB" keeps the number and drops the vague half, instead of building an impossible query. Matched phrases are consumed as they are read, so leftover words are exactly those nothing claimed and become the text filter, which keeps "from" in "videos from last month" out of the search term.

### Running a search (V0.4, Search page)

`FileSearchService` in Core joins the pieces: it translates the phrase, lists the authorized roots, searches each one, and merges the hits. It lives in Core so the rules are testable without a window and so the App layer holds no decision about which folders may be read.

Only roots marked `Allowed` are searched. A `Restricted` or `Protected` root is skipped even though its rows may still exist in the index, because permission is a live decision and a cached row cannot grant access. The service reports `FoldersSearched` so the UI can state real scope, and `ReachedLimit` so a truncated list is never shown as complete. A `SearchHit` carries the root's display name and the path relative to it, so no absolute path reaches the screen.

`SearchViewModel` only turns that outcome into text and lists. It keeps three states distinct — not understood, understood but nothing matched, and matched — because collapsing them is how a search screen starts lying. An unrecognised phrase shows "I did not understand that" and lists nothing, rather than falling through to a full listing of every remembered file.

## Deterministic Classification and Recipes

`IFileClassifier` accepts a `FileItem` and returns one immutable `Classification`. `DeterministicFileClassifier` matches case-insensitive, dot-prefixed rules from `FileTypeRuleSet`; longer compound extensions win. A narrow filename heuristic separates common screenshot names from other images. Results always include a closed `FileCategory`, `FileKind`, provenance, bounded confidence, and explanation. Unknown is an explicit valid outcome.

`FolderRecipe` maps categories to validated root-relative destination directories. Recipes are data only: they do not inspect or create directories and cannot execute operations. The default recipe is registered through dependency injection, while callers can construct versioned custom recipes. A later planner consumes classifications and a selected recipe to propose typed operations, which still require Safety validation and approval.

## AI Boundary

The AI layer receives a minimized DTO, not a filesystem service. Its response passes through:

1. transport and size limits;
2. strict schema parsing;
3. allowed-enum and field validation;
4. mapping to Core proposal types;
5. deterministic Safety validation.

Prompt text is usability guidance, never enforcement. See `AI-PROVIDERS.md`.

V0.3 implements this boundary as `AiRequestBuilder → ConfiguredSuggestionProvider → local/OpenRouter adapter → StructuredSuggestionParser`. The builder excludes protected IDs and emits only fields permitted by the saved category set. Providers receive no filesystem, shell, executor, plan, root repository, or credential-enumeration capability. Responses can express only `{fileId, category, confidence, reason}` and remain advisory: on the practice page they are displayed, and on Tidy a folder (V0.6 step 2b) `TidyAiService` turns the category into a folder through DeskAI's own recipe.

Local addresses must be explicit HTTP(S) loopback URLs without embedded credentials, query strings, or fragments. Cloud routing resolves the saved provider ID through `CloudProviderCatalog`, a closed compile-time allow-list; each entry carries one fixed HTTPS chat-completions address, its own credential reference, and a model hint. `CloudChatCompletionsSuggestionProvider` serves every listed service because they share the OpenAI-style request and response shape. An unrecognized provider ID is refused rather than guessed at. Redirects and automatic retries are disabled, and one provider failure never triggers another provider. Windows Credential Manager stores each key under that provider's own reference; SQLite holds only the reference string. Daily request reservations are atomic, counted per provider, and happen before cloud transport.

## Dependency Injection and Configuration

Register concrete implementations once in the App composition root. Constructor injection is the default. Avoid resolving services from a global container. Configuration has typed options, safe defaults, and validation. Secrets are retrieved just in time from the credential adapter.

WinUI pages are registered as transient dependencies. `NavigationService` resolves the requested page from the application service provider and places that page in the shell frame, allowing pages such as `OrganizePage` to receive their view models by constructor injection. Page code-behind assigns the data context and handles the native picker/confirmation dialog because those require WinUI window handles; authorization, scanning, state, and filesystem policy remain outside the view.

(History — retired 2026-09-11, ADR 0023.) The V0.2 preview used `DemoOrganizationPlanFactory` in the App project. It creates `FileItem` metadata and a synthetic authorized-root label in memory, then invokes the same Core planner and Safety validator used by later real flows. It does not create, enumerate, or inspect the displayed path. `OrganizeViewModel` adapts immutable plan results into selectable presentation rows; selection has no execution meaning until a future approval model binds chosen IDs to a validated plan revision.

## Concurrency and Reliability

- Use `async` for real I/O and pass `CancellationToken` through call chains.
- Do not wrap synchronous work in `Task.Run` without a measured UI need.
- Serialize conflicting mutations per authorized root.
- Use stable operation IDs for idempotency/recovery.
- Consider a write-ahead journal state before mutation, then record completion.
- UI updates occur on the proper dispatcher; domain code remains dispatcher-free.

## Observability

Use structured local logs with event IDs. Redact API keys, contents, prompts, and preferably personal absolute paths. User-facing activity history is distinct from developer diagnostics. Diagnostics export requires review and consent.

## Architecture Decision Process

For consequential choices, add a short ADR under `docs/decisions/` describing context, decision, alternatives, and consequences. Candidates include target Windows version, packaging, SQLite library, MVVM toolkit, credential API, local inference runtime, and plugin sandbox.
