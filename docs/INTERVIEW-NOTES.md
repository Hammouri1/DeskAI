# Interview and Learning Notes

## Purpose

DeskAI should become a project you can explain, not just code generated for you. After each milestone, update this file with concrete classes, diagrams, trade-offs, failures, and test results from the actual repository. Never claim planned features as implemented.

## Thirty-Second Project Explanation

“DeskAI is a Windows-first, local-first file and workspace organizer built with C#, .NET, WinUI 3, and SQLite. It uses deterministic rules and optional local or bring-your-own-key AI to propose organization plans. The model never touches the filesystem: typed plans pass through a separate safety validator, the user previews and approves them, and a narrow executor records operations so they can be audited and undone. Core functionality needs no backend or account.”

## Two-Minute Architecture Explanation

The WinUI App owns presentation and composes dependencies. Core contains provider-neutral domain types and use cases. Infrastructure implements Windows filesystem access, SQLite, indexing, credentials, and execution. AI adapters translate minimized data into structured suggestions but have no filesystem dependency. Safety independently validates typed operations against authorized roots, protected locations, collision rules, and live state. Planning is side-effect free; approval is bound to a plan revision; the executor revalidates before mutation and journals per-operation results. This is dependency inversion: high-level policy depends on contracts, while volatile frameworks implement them.

## Concepts to Understand in This Project

### Solution, project, namespace

A solution groups buildable projects. Each project is an assembly and dependency boundary. A namespace organizes names but does not enforce dependency direction. Be ready to point to the `.sln`, each `.csproj`, and its references.

### Interface and dependency inversion

An interface describes a capability such as scanning or storing plans. Core depends on that contract; Infrastructure implements it. This makes policy independent of Windows/SQLite and permits safe fakes. Do not say “interfaces are always better”—they are valuable at meaningful boundaries.

### Dependency injection

The App composition root constructs the object graph and supplies dependencies through constructors. Benefits are explicit dependencies, replaceability, and testability. Avoid the service-locator anti-pattern where classes reach into a global container.

### Domain model versus DTO

A domain model represents valid business concepts and invariants. A DTO represents serialized/provider/database data. Mapping prevents untrusted JSON or storage details from becoming trusted domain behavior.

### Records and immutability

Records work well for plans, operations, and validation results because value semantics and immutable-style construction reduce accidental mutation. Approval binds to a particular revision; changing it creates a new value. Mutable entities can still be appropriate for lifecycle state managed deliberately.

### MVVM

Views/XAML render UI; view models expose observable state and commands; use cases/services perform work. MVVM improves testing and keeps filesystem logic out of code-behind. Explain a real user action from button/command to use case and back to UI state.

### Async/await and cancellation

Scanning, database, and network work must not freeze the UI. `await` represents asynchronous completion; cancellation is cooperative through `CancellationToken`. Async does not automatically mean parallel and should not wrap every method.

### Repository and SQLite migration

A repository isolates meaningful persistence operations from Core. A migration evolves the database schema reproducibly. SQLite stores local application state; it cannot prove that the live filesystem still matches an old plan.

### Transaction and compensating undo

A database transaction can atomically commit database changes, but a multi-file operation is not one atomic transaction across Windows and SQLite. DeskAI journals intent/outcome and uses compensating operations for undo. Undo revalidates because the user may have changed files since execution.

### Canonicalization and TOCTOU

Canonicalization normalizes a path before containment checks; naive string prefixes are unsafe. TOCTOU means state can change between validation and use, so the executor validates again immediately before acting and handles failure safely.

### Structured AI output

The provider returns a versioned, narrow schema using known IDs and enums. Parsing success does not equal authorization. This design contains hallucinations and prompt injection because unknown commands cannot become executable behavior.

### Unit, integration, and UI tests

Unit tests isolate pure policies; integration tests exercise adapters such as SQLite/filesystem in controlled sandboxes; UI tests verify critical interaction/accessibility. Negative safety tests are as important as happy paths.

## Important Design Answers

**Why C#/.NET and WinUI 3?** Native Windows APIs, strong tooling/type safety, async support, and a Windows-native UI fit deep—but constrained—filesystem integration. Windows-first avoids pretending filesystem behavior is portable.

**Why no backend?** Core organization, index, rules, history, and local AI can remain on-device, reducing cost and privacy exposure. BYO providers are direct optional connections.

**Why not let the model call tools?** Prompt instructions are not an authorization mechanism. A narrow typed plan plus deterministic validation limits the model to recommendations.

**Why SQLite?** It is embedded, transactional, widely supported, and sufficient for one-user local metadata/history. Secrets still require protected credential storage.

**Why preview and undo?** Classification is uncertain and filesystem mistakes are costly. Preview establishes informed approval; journaled undo builds recoverability and trust.

**Why deterministic classification first?** Extensions/metadata/rules are faster, cheaper, explainable, and reliable for common cases. AI adds value only for ambiguity.

**What happens during partial failure?** DeskAI reports each operation, preserves the journal, stops/continues according to explicit policy, and offers safe undo/recovery. It never reports the whole plan as successful.

## Security Scenarios to Whiteboard

1. Model proposes `..\\..\\Windows`: schema mapping may parse it, but canonical containment/protected-path checks block it.
2. A folder contains a junction escaping the root: scanner/executor refuse reparse traversal by default.
3. Destination appears after preview: execution-time collision revalidation blocks overwrite.
4. A PDF contains malicious model instructions: it remains untrusted content; the model cannot create new operation types or call services.
5. An API key leaks in an exception: credential retrieval/log redaction design and tests prevent storage/output; rotate the key if exposure occurs.
6. User requests deletion: the initial operation vocabulary has no permanent-delete command; later Recycle Bin is explicit and reviewed.

## Questions You Should Be Able to Answer After Each Milestone

- What user problem does this code solve now?
- Which project owns it and why?
- What inputs are untrusted?
- Where are invariants enforced?
- What could fail midway and how is state represented?
- Which tests prove the happy path and refusal path?
- What trade-off did you choose instead of an alternative?
- What is still planned rather than implemented?

## Milestone Learning Log Template

```text
Milestone / date:
What became usable:
Main data flow:
Classes/interfaces I can explain:
New concept and my own explanation:
Hardest bug and root cause:
Security cases tested:
Build/test evidence:
Trade-off/ADR:
What is deliberately not built:
Next small task:
```

## V0.1 Learning Log — 2026-09-07

```text
What became usable: An x64 WinUI shell with five truthful placeholder pages, startup database creation, DI, and local redacted debug logging.
Main data flow: App startup → composition root → SQLite migration → MainWindow → NavigationService → selected placeholder Page.
Classes/interfaces I can explain: AuthorizedRoot, FileItem, OrganizationPlan, typed PlanOperation records, Approval, WindowsPathPolicy, PlanValidator, SqliteDatabaseInitializer, IFileScanner, IPlanExecutor, and IOrganizationSuggestionProvider.
New concept and my own explanation: A project reference is an enforced assembly boundary; Core has no dependency on UI, SQLite, Windows App SDK, or provider packages.
Hardest bug and root cause: The first temporary-directory helper checked only that a path stayed under the system temp root, not inside the specific test-owned directory. A negative traversal test exposed it and the helper now verifies both boundaries.
Security cases tested: absolute paths, UNC/device-style input, alternate data streams, traversal, prefix confusion, reserved names, protected roots/entries, plan-root mismatch, unknown approval IDs, and duplicate operation IDs.
Build/test evidence: Debug solution build completed with zero warnings/errors; 24 tests passed.
Trade-off/ADR: .NET 10 + Windows App SDK 2.4, x64 unpackaged/self-contained foundation; direct Microsoft.Data.Sqlite rather than an ORM.
What is deliberately not built: folder picker, scanner, filesystem mutation, preview, execution, undo behavior, cloud/local model adapters, search, and automation.
Next small task: V0.2 step 1, a read-only async/cancellable metadata scanner restricted to an injected authorized temporary root with reparse-point refusal and per-item errors.
```

## V0.2 Step 1 Learning Log — 2026-09-07

```text
What became usable: Infrastructure can scan metadata from an explicitly supplied authorized root and stream files/issues to a caller. It is not connected to real-folder UI.
Main data flow: AuthorizedRoot + MetadataScanOptions → IFileScanner → root/reparse/path checks → iterative enumeration → FileDiscovered or ScanIssue events.
Classes/interfaces I can explain: IFileScanner, MetadataScanOptions, ScanEvent, FileDiscovered, ScanIssue, ScanIssueCode, and WindowsMetadataScanner.
New concept and my own explanation: IAsyncEnumerable streams results over time and lets the caller process early results without retaining the whole filesystem tree; cancellation is checked before and during enumeration.
Hardest bug and root cause: C# forbids yield return in catch blocks. Exception-prone filesystem calls were moved into small helpers that return typed success-or-issue outcomes, and the iterator yields those outcomes outside catch regions.
Security cases tested: root-relative metadata, no content opening, protected-entry exclusion, child reparse-point refusal, missing/protected roots, pre-cancellation, stable IDs, maximum depth, and maximum total entries.
Build/test evidence: Release build completed with zero warnings/errors; all 36 tests passed.
What is deliberately not built: folder picker/UI scan command, classification, persistence of scan results, planning, preview, mutation, journaling, and undo.
Next small task: V0.2 step 2, deterministic extension/metadata classification and configurable folder recipes, still using only generated temporary data.
```

## V0.2 Step 2 Learning Log — 2026-09-07

```text
What became usable: Core can deterministically classify common file formats and map classifications through a selected versioned folder recipe without AI or filesystem side effects.
Main data flow: FileItem → IFileClassifier → FileTypeRuleSet/filename heuristic → Classification → FolderRecipe destination lookup. The next planner will turn that data into proposals.
Classes/interfaces I can explain: IFileClassifier, Classification, FileCategory, FileTypeRule, FileTypeRuleSet, DeterministicFileClassifier, FolderRecipe, and FolderRecipeEntry.
New concept and my own explanation: Classification and organization preference are separate concerns. A PDF is a document regardless of whether a Student recipe prefers University/Assignments and another recipe prefers Documents.
Security cases tested: unsafe recipe paths, duplicate categories/extensions, disguised report.pdf.exe names, unknown formats, compound extensions, and proof that scan/classify uses no AI provenance.
Build/test evidence: Release build completed with zero warnings/errors; all 62 tests passed.
What is deliberately not built: content sniffing, AI classification, recipe persistence/editor UI, planner, preview, or file mutation.
Next small task: V0.2 step 3, a side-effect-free organization planner with stable operation IDs, explanations, and combined-operation conflict detection.
```

## V0.2 Step 3 Learning Log — 2026-09-07

```text
What became usable: Core can convert classified files and a versioned recipe into a deterministic, reviewable OrganizationPlan without filesystem access.
Main data flow: classified files + recipe + plan identity → OrganizationPlanner → typed operations + PlanIssue records → PlanValidator blocks any conflicts.
Classes/interfaces I can explain: IOrganizationPlanner, OrganizationPlanningRequest, ClassifiedFile, OrganizationPlanner, PlanIssue, PlanIssueCode, and PlanIssueSeverity.
New concept and my own explanation: A proposal is data, not an action. Creating MoveFileOperation records does not move files; only a future narrow executor can do that after validation and approval.
Security cases tested: duplicate destinations, case-insensitive collisions, an existing destination file, a file occupying a required directory path, duplicate source paths, traversal in FileItem, stable operation IDs, and Safety refusal of conflicted plans.
Build/test evidence: Release build completed with zero warnings/errors; all 74 tests passed.
What is deliberately not built: preview UI, approval workflow, filesystem execution, collision resolution, plan persistence, journal, or undo.
Next small task: V0.2 step 4, a preview-only UI over generated in-memory sample plans with selection, warnings/conflicts, and no execution capability.
```

## V0.2 Step 4 Learning Log — 2026-09-07

```text
What became usable: The Organize page can render a real Core plan over generated metadata, explain operations and issues, disable conflicted selections, track allowed selections, and generate a visible new revision.
Main data flow: generated FileItem records → deterministic classifier → OrganizationPlanner → PlanValidator → OrganizeViewModel presentation rows → XAML bindings.
Classes/interfaces I can explain: DemoOrganizationPlanFactory, DemoPlanSnapshot, OrganizeViewModel, PreviewOperationViewModel, PreviewIssueViewModel, NavigationService, ObservableObject, and RelayCommand.
New concept and my own explanation: MVVM binding connects view-model properties and commands to XAML controls. The view renders state and forwards intent, while the view model adapts domain data without manipulating files.
Security cases demonstrated: no picker/scanner call, virtual root only, blocked operation checkboxes disabled, selection is not approval, plan revision is visible, and execution has no command or implementation.
Build/test evidence: Release build completed with zero warnings/errors; all 74 tests passed.
What is deliberately not built: folder selection, real scanning from UI, approval, execution, persistence, journaling, or undo.
Next small task: V0.2 step 5 security review gate, followed only then by a narrow executor restricted to a verified temporary demo root.
```

## V0.2 Steps 5–6 Learning Log — 2026-09-07

```text
What became usable: Explicitly selected allowed operations can run against generated dummy files in an internally chosen temporary workspace, and SQLite can migrate fresh or version-1 databases to planning schema v2.
Main data flow: selected preview IDs → Approval bound to plan/revision/policy → fresh Safety and owned-root checks → typed executor operations → per-operation result. Startup database → transactional v1 migration → transactional v2 migration.
Classes/interfaces I can explain: IPlanExecutor, TemporaryDemoPlanExecutor, DemoWorkspaceOptions, Approval, ExecutionResult, SqliteDatabaseInitializer, migration table, composite primary key, and foreign key.
New concept and my own explanation: TOCTOU means a safe-looking path can change between checking and using it, so the executor rechecks ownership, links, sources, and destinations immediately around each operation. A database migration is a numbered, repeatable upgrade from an older schema—not a manual database edit.
Security cases tested: temp-boundary enforcement, marker tampering, traversal, stale approval, unknown operation ID, unselected operation, overwrite collision, outside sentinel preservation, schema upgrade, and foreign-key rejection.
Build/test evidence: Release build completed with zero warnings/errors; all 84 tests passed.
What is deliberately not built: real-folder picker/access, automatic demo deletion, write-ahead journal records, restart recovery, undo, or AI.
Next work cycle: V0.2 steps 7–8—journal/recovery/undo first, then a separate real-folder authorization review and picker only if every gate passes.
```

## V0.2 Step 7 Learning Log — 2026-09-07

```text
What became usable: Every demo operation is journaled before mutation; outcomes and recent activity persist; unchanged sample files can be safely undone in the same session.
Main data flow: root/plan persistence → Prepared journal → InProgress operation → filesystem call → exact outcome → transaction summary → reverse validated undo transaction.
Classes/interfaces I can explain: IOperationJournal, ExecutionJournalEntry, OperationJournalEntry, SqliteOperationJournal, SqlitePlanRepository, SqliteAuthorizedRootRepository, IUndoService, and journal state enums.
New concept and my own explanation: Write-ahead journaling records what is about to happen before it happens, so a crash does not erase intent. Undo is a compensating transaction with new validation, not time travel.
Security cases tested: journal failure before mutation, changed destination before undo, empty created-directory removal, outside-root refusal, interrupted move reconciliation, and conservative old-session recovery state.
Build/test evidence: Release build completed with zero warnings/errors; all 90 tests passed.
What is deliberately not built: cross-restart undo, real-folder mutation, protected durable marker storage, or automatic cleanup.
Next small task: V0.2 step 8, a native picker and revocable read-only authorization flow tested only with a generated temporary folder.
```

## V0.2 Step 8 Learning Log — 2026-09-07

```text
What became usable: A user can choose a folder through the native Windows picker, explicitly approve a bounded read-only metadata preview, inspect names/sizes/dates, and revoke that permission.
Main data flow: native picker → disclosure confirmation → IReadOnlyFolderService → canonical/protected/link checks → MetadataOnly root persistence → bounded IFileScanner events → friendly view-model rows.
Classes/interfaces I can explain: RootAuthorizationScope, IReadOnlyFolderService, ReadOnlyFolderService, IFolderPickerService, WindowsFolderPickerService, and ReadOnlyFileItemViewModel.
New concept and my own explanation: Least privilege means a permission grants only what the current feature needs. MetadataOnly is stored as data and enforced by Safety, so UI consent to preview cannot silently become consent to move files.
Security cases tested: locked dummy file proves no content opening, protected-root ancestor refusal, bounded scanning, revocation without file change, metadata-only plan rejection, reparse protection, and schema-v4 migration.
Build/test evidence: Release build completed with zero warnings/errors; all 94 tests passed.
What is deliberately not built: real-folder move/rename/delete, content extraction, cross-restart undo, AI provider calls, API-key storage, search index, or automation.
Next work cycle: V0.3 tasks 1–2—a provider-neutral structured AI contract with deterministic fake, then a privacy/disclosure policy and simple dashboard. Online AI integration remains later and optional.
```

## V0.3 Learning Log — 2026-09-08

```text
What became usable: Optional classification advice for generated samples with AI off, through an explicit local-only address, or through consent-gated OpenRouter with a chosen model, privacy settings, and Windows-protected credentials.
Main data flow: FileItem → disclosure-filtered AiFileCandidate → configured provider → bounded HTTP transport → strict structured parser → advisory preview row. No provider result reaches the plan/executor.
Classes/interfaces I can explain: IOrganizationSuggestionProvider, AiRequestBuilder, AiSettings, IAiSettingsRepository, ICredentialVault, WindowsCredentialVault, IAiHttpTransport, ConfiguredSuggestionProvider, StructuredSuggestionParser, and IAiUsageBudget.
New concept and my own explanation: A DTO is a deliberately small data shape crossing a boundary. By constructing it before provider code and omitting disallowed fields, privacy does not depend on asking the model to ignore data it should never receive.
Security cases tested: prompt injection, unknown/duplicate JSON fields and IDs, invented IDs, invalid enums/confidence, oversized responses, non-loopback endpoints, missing credentials, disclosure expansion, offline/timeout/cancellation/rate limits, secret redaction, and daily request caps.
Build/test evidence: Release build completed with zero warnings/errors; all 129 tests passed without live network or personal credential-store writes.
What is deliberately not built: AI processing of user-selected folders/content/images, AI-generated executable operations, background AI, automatic fallback/retry, exact price calculation, arbitrary cloud destinations, or V0.4 search/index features.
Next work cycle: V0.4 begins with an incremental local metadata index and constrained structured search, still limited to authorized metadata-only roots and generated test directories.
```

## V0.4 Step 1 Learning Log — 2026-09-09

```text
What became usable: A local metadata index that can remember one authorized folder's file names, sizes, dates, and categories between scans, refresh incrementally, summarize itself, and forget everything about a folder the moment that folder is disconnected. Backend only; no UI calls it yet.
Main data flow: AuthorizedRoot → MetadataIndexService (refuses protected roots) → IFileScanner metadata stream → DeterministicFileClassifier → IndexedFile records → IFileIndex.SynchronizeRootAsync → SqliteFileIndex diff/write in one transaction → FileIndexSyncResult counts.
Classes/interfaces I can explain: IndexedFile, FileIndexSyncResult, FileIndexStatistics, IFileIndex, IMetadataIndexService, IndexUpdateResult, MetadataIndexService, and SqliteFileIndex.
New concept and my own explanation: A cache is not an authority. The index says how a file looked the last time DeskAI looked at it. That is enough to search or summarize, but never enough to move a file, because the disk can change afterwards. Anything that mutates must re-check live state.
New concept and my own explanation (2): A foreign key with ON DELETE CASCADE makes deletion a data rule rather than a habit. Disconnecting a folder erases its remembered rows in the same statement, so nobody has to remember to clean up.
Hardest thing to get right: Making the refresh genuinely incremental. Reading the stored rows first and comparing only the facts a rescan can change (path, kind, category, size, timestamps) means an unchanged folder reports "nothing changed" and writes zero rows, instead of rewriting everything and looking busy.
Security cases tested: traversal/rooted/ADS/oversized paths refused at construction, protected root refused, root overlapping a protected location refused, protected child skipped, entry limit honoured, cancellation writes nothing, entries for another root refused, duplicate file IDs refused, unauthorized root refused by the foreign key, roots isolated with identical file names, no stored row contains an absolute path, and disconnecting a root erases its index.
Build/test evidence: Release build completed with zero warnings/errors; 171 tests passed with none skipped (129 before this slice). dotnet format reported no changes.
AI containment check: DeskAI.AI references DeskAI.Core only and contains no filesystem, scanner, index, path, or executor use. The index is not an AI disclosure channel.
Trade-off/ADR: docs/decisions/0012-local-metadata-index.md — root-scoped, cascade-erased, non-authoritative index instead of a global file table with absolute paths.
What is deliberately not built: automatic/background indexing, any indexing UI, content hashes, duplicate detection, content extraction, embeddings, and search queries.
Next small task: a typed structured search query model executed as parameterized SQL, scoped to authorized root IDs.
```

## Portfolio Evidence to Collect

Keep a clean architecture diagram, safe preview screenshots using dummy data, a short undo demonstration, representative Safety tests, an ADR showing a real trade-off, performance measurements on synthetic folders, and release notes. In interviews, discuss constraints and verification rather than raw line count or “AI built it.”
