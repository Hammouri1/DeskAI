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

The V0.2 step-5 implementation is intentionally narrower than the future production executor. `TemporaryDemoPlanExecutor` generates its own unique root beneath a configured base that must itself be contained by the Windows temporary directory. It seeds only known dummy files and requires an unpredictable ownership marker. Before every selected operation it rechecks root identity, containment, existing path components for reparse points, current Safety results, approval identity/revision/policy, source existence, destination availability, and parent existence. It never overwrites. No API accepts an arbitrary execution root, and the App exposes no folder picker.

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

Schema version 1 introduced migration tracking, local settings, and authorized roots. Schema version 2 added composite-keyed plan revisions, plan operations, and execution-transaction headers. Schema version 3 adds plan issues, per-operation journal intent/outcomes, and undo links. Focused SQLite repositories round-trip roots, exact plan revisions, and journal entries using parameters, transactions, foreign keys, invariant UTC parsing, and bounded recent-history queries.

## Scanning and Indexing

Scanning must be asynchronous, cancellable, bounded, and resilient to access-denied, disappearing files, long paths, cycles, and individual metadata failures. Do not follow reparse points by default. Stream results rather than loading unbounded trees into memory. Store normalized root-relative identifiers and refresh incrementally later.

The V0.2 scanner contract streams a closed `ScanEvent` hierarchy: `FileDiscovered` carries metadata-only `FileItem` data, while `ScanIssue` carries a root-relative location, reason code, and sanitized explanation. `MetadataScanOptions` requires explicit depth and entry bounds. The Windows adapter inspects the authorized root's path components for reparse points, uses an iterative directory stack, and never opens file contents. Classification remains a separate stage, so scanned files initially have `FileKind.Unknown`.

Content extraction is a separate, permission-gated pipeline with file-size/type limits and sandbox considerations. It is not part of the initial metadata scanner.

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

## Dependency Injection and Configuration

Register concrete implementations once in the App composition root. Constructor injection is the default. Avoid resolving services from a global container. Configuration has typed options, safe defaults, and validation. Secrets are retrieved just in time from the credential adapter.

WinUI pages are registered as transient dependencies. `NavigationService` resolves the requested page from the application service provider and places that page in the shell frame, allowing pages such as `OrganizePage` to receive their view models by constructor injection. Page code-behind only assigns the injected view model as its data context.

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
