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
DeskAI.App ───────────────→ DeskAI.Core
    │                           ↑
    ├────────→ DeskAI.Safety ───┘
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

### `DeskAI.AI`

Provider-neutral orchestration plus adapters for local or cloud endpoints. It constructs minimal data envelopes, requests structured results, validates syntax/schema, maps output to Core proposals, and returns errors without filesystem side effects. Provider SDK response types never escape this project.

### `DeskAI.App`

WinUI 3 views, reusable controls, view models, commands, navigation, accessibility, and the composition root. View models call application use cases/interfaces; code-behind is limited to view behavior. App must not manipulate files or call provider HTTP APIs directly.

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

Undo reads completed journal operations in reverse order. A moved file returns only if its current size and modification time still match the recorded pre-move facts and its original path is free. A created directory is removed only if it was recorded, remains inside the owned root, is not a link, and is empty after file reversals. Undo creates its own journal transaction. In this demo implementation, undo is offered only during the same application session; durable cross-restart ownership is deliberately unresolved.

## Persistence

SQLite is local application state, not a source of authority over the current filesystem. Suggested logical areas:

- schema migrations and application settings;
- authorized roots and protected entries;
- file index and classifications;
- plans, operations, approvals, execution transactions, and undo records;
- rules and automation runs;
- AI provider settings without secrets.

Use migrations, foreign keys, transactions, indexes, UTC timestamps, and an explicit retention strategy. Repositories are justified when they separate Core use cases from SQLite—not as one generic repository for every table. API keys stay in a Windows-protected credential store and SQLite holds only a credential reference.

Schema version 1 introduced migration tracking, local settings, and authorized roots. Versions 2–3 added plans, operations, journal outcomes, and undo links. Version 4 added authorization scope. Version 5 adds non-secret AI mode, endpoint/model, disclosure flags, limits, consent, and a credential reference. Version 6 adds an atomic per-provider daily request counter. Version 7 adds `indexed_files`, keyed on `(root_id, file_id)` with a foreign key to `authorized_roots` using `ON DELETE CASCADE`, plus indexes on path, name, category, size, and modification time. Version 8 adds `saved_searches`, holding a name, the typed phrase, and a creation time. It deliberately has no root column and no foreign key: a saved search must not be able to outlive or widen an authorization, so scope is resolved from the authorized roots each time one runs. A case-insensitive unique index on the name stops two saved searches differing only by capitalisation. API-key bytes never enter SQLite.

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

**Stage 2, confirming by hash, is deliberately absent.** Hashing reads the bytes of a file. These folders are authorized `MetadataOnly`, which does not permit that, so confirming duplicates belongs with the permission-gated content work in step 8 rather than being slipped in behind a size check. Everything the UI says is therefore hedged: "possible duplicates", "might be duplicated", "up to" a saving. `ReclaimableBytes` is a ceiling on what could be freed if the copies turn out identical, never a promise. A test asserts the service reads no content at all.

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

V0.3 implements this boundary as `AiRequestBuilder → ConfiguredSuggestionProvider → local/OpenRouter adapter → StructuredSuggestionParser`. The builder excludes protected IDs and emits only fields permitted by the saved category set. Providers receive no filesystem, shell, executor, plan, root repository, or credential-enumeration capability. Responses can express only `{fileId, category, confidence, reason}` and remain advisory in the sample preview.

Local addresses must be explicit HTTP(S) loopback URLs without embedded credentials, query strings, or fragments. Cloud routing resolves the saved provider ID through `CloudProviderCatalog`, a closed compile-time allow-list; each entry carries one fixed HTTPS chat-completions address, its own credential reference, and a model hint. `CloudChatCompletionsSuggestionProvider` serves every listed service because they share the OpenAI-style request and response shape. An unrecognized provider ID is refused rather than guessed at. Redirects and automatic retries are disabled, and one provider failure never triggers another provider. Windows Credential Manager stores each key under that provider's own reference; SQLite holds only the reference string. Daily request reservations are atomic, counted per provider, and happen before cloud transport.

## Dependency Injection and Configuration

Register concrete implementations once in the App composition root. Constructor injection is the default. Avoid resolving services from a global container. Configuration has typed options, safe defaults, and validation. Secrets are retrieved just in time from the credential adapter.

WinUI pages are registered as transient dependencies. `NavigationService` resolves the requested page from the application service provider and places that page in the shell frame, allowing pages such as `OrganizePage` to receive their view models by constructor injection. Page code-behind assigns the data context and handles the native picker/confirmation dialog because those require WinUI window handles; authorization, scanning, state, and filesystem policy remain outside the view.

The V0.2 preview uses `DemoOrganizationPlanFactory` in the App project. It creates `FileItem` metadata and a synthetic authorized-root label in memory, then invokes the same Core planner and Safety validator used by later real flows. It does not create, enumerate, or inspect the displayed path. `OrganizeViewModel` adapts immutable plan results into selectable presentation rows; selection has no execution meaning until a future approval model binds chosen IDs to a validated plan revision.

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
