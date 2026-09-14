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

## Multi-Provider Cloud AI Learning Log — 2026-09-09

```text
What became usable: Online AI now works with whichever supported service the user already has a key for — OpenRouter, OpenAI, Groq, Mistral, DeepSeek, or Together AI — instead of only OpenRouter. Every label, agreement, and message renames itself to the chosen service.
Main data flow: Settings picks a CloudProvider from a fixed catalog → key saved under that provider's own Windows Credential Manager reference → settings store the provider ID and reference only → ConfiguredSuggestionProvider resolves the ID back through the catalog → CloudChatCompletionsSuggestionProvider posts to that provider's fixed HTTPS address.
Classes/interfaces I can explain: CloudProvider, CloudProviderCatalog, CloudChatCompletionsSuggestionProvider, ConfiguredSuggestionProvider, and ICredentialVault.
New concept and my own explanation: An allow-list is safer than validation. Rather than checking whether a user-typed address looks acceptable, DeskAI only knows a fixed set of destinations. There is no input to get wrong, so there is no parsing bug to exploit.
New concept and my own explanation (2): Separate credential references are a blast radius decision. Because each service stores its key under its own name, sending one company's key to another is not one bug away — the adapter physically cannot read a reference it was not given.
Tension I had to resolve: the owner wanted "any API key", but SECURITY.md forbids arbitrary cloud addresses because a typo would send approved data to the wrong host. The catalog satisfies the intent (use the service you already pay for) without a free-text endpoint. A reviewed custom-endpoint path is recorded as an open question in ADR 0013, not shipped quietly.
Security cases tested: every catalog entry is plain HTTPS with a default port and no credentials/query/fragment; IDs, display names, hosts, and credential references are all unique; every reference stays under the DeskAI namespace; Find refuses null, blank, whitespace-padded, wrong-case, and unknown IDs; each provider routes only to its own address; the adapter reads only its own credential entry; an unknown saved provider ID is refused before any network call; a missing key stops before the network.
Build/test evidence: Release build with zero warnings/errors; 202 tests passed with none skipped (171 before this change). dotnet format reported no changes.
AI containment check: DeskAI.AI still references DeskAI.Core only. A search for index, scanner, path, executor, journal, process, and registry use across the project returns nothing.
Trade-off/ADR: docs/decisions/0013-vetted-multi-provider-cloud-ai.md.
What is deliberately not built: services with a different API shape (Anthropic Messages, Google Gemini), a user-typed custom endpoint, provider fallback, retries, and price estimation.
Next small task: make the interface look and feel better without weakening any safety wording.
```

## Interface Refresh Learning Log — 2026-09-09

```text
What became usable: A consistent visual system across all five pages — accent headers, bordered cards, one type ramp, icon-and-word status badges, and a permanent practice-mode reminder in the navigation footer.
Main data flow: App.xaml holds shared styles and converter instances → pages reference them by key → PreviewStatusLevel from a view model is converted to a brush, a glyph, and a badge background at bind time.
Classes/interfaces I can explain: PreviewStatusLevel, StatusLevelToBrushConverter, StatusLevelToGlyphConverter, StatusLevelToBackgroundConverter, and the styles in App.xaml.
New concept and my own explanation: An IValueConverter turns view-model data into something a view can draw, without the view model referencing WinUI. The view model says "this row is Blocked"; the converter decides that means a red brush and a cross icon. That keeps Brush and FontIcon types out of the view model, so it stays testable.
New concept and my own explanation (2): Theme resources instead of chosen colours. Naming SystemFillColorCriticalBrush rather than a hex red means Windows supplies the right value for light, dark, and high contrast. Inventing a palette would have looked fine on my machine and failed on someone else's.
Hardest thing to get right: proving it actually works. XAML resource keys resolve at runtime, not compile time, so a missing key builds cleanly and then crashes on navigation. I first tried searching the WinUI DLL for key names, which reported even known-good keys as missing because they live in compiled XBF. The verification that counted was temporary scaffolding that constructed all five pages in a real run and wrote the result to a file — then removing it and diffing against the backup to prove it was gone.
Safety cases checked: blocked rows still unselectable; colour never the sole carrier of meaning; Search and Automatic tasks state plainly that they are unfinished and their controls are disabled; no safety wording was shortened to fit a nicer layout.
Build/test evidence: Release build with zero warnings/errors; 202 tests still pass; dotnet format clean; all five pages verified to construct in a real run.
AI containment check: this slice changed presentation only. No view gained a filesystem, index, or provider capability, and DeskAI.AI still references DeskAI.Core alone.
What is deliberately not built: animations, custom title bar, per-file-type icons beyond two well-established glyphs, and any visual for a feature that does not exist.
Next small task: V0.4 step 2 — the typed structured search query executed as parameterized SQL over the index.
```

## V0.6 Steps 2b and 3 Learning Log — 2026-09-10

```text
What became usable: On "Tidy a folder", AI can suggest where files go (only after the person sees exactly what it will be told and presses Send), and the Tidy button really moves the ticked files in a folder the person allowed DeskAI to tidy, with a result line, every skipped file and why, and Undo.
Main data flow (AI): TidyPreview.AskableFiles → TidyAiService.PrepareAsync (random stand-in IDs, sharing capped, protected dropped) → dialog → AskAsync (re-check) → IOrganizationSuggestionProvider → strict parser → TidyAiAdvice → TidySuggestionService (rules > AI > type) → list.
Main data flow (tidy): ticked moves → TidyRunService (exact approval + ExpectedFile facts) → FolderTidyExecutor (live folder trust) → FileOperationRunner (journal first, per-file re-check, move without overwrite) → result → Undo through the same runner.
Classes/interfaces I can explain: TidyAiService, TidyAiQuestion, TidyAiAdvice, IPlanSafetyCheck.IsProtected, IFolderTidyExecutor, FolderTidyExecutor, FileOperationRunner, IRootTrust, ExpectedFile, TidyRunService.
New concept and my own explanation: Two-step confirmation. Building a request and sending it are separate calls with the person in between, and the second re-checks that nothing changed since the first. What was shown is exactly what is sent.
New concept and my own explanation (2): Strategy by composition. One runner holds the move rules; each executor plugs in only "how do I know I may still touch this folder?". The practice page therefore rehearses the exact code real tidying uses.
New concept and my own explanation (3): Time-of-check versus time-of-use, per file. The list can be minutes old, so each file's size and last-changed time are compared again right before it moves, and File.Move(overwrite: false) makes the last check atomic.
Hardest thing to get right: Proving the tests test something. Several controls were removed on purpose (sending real file IDs, full paths, skipping the re-check before Send, the changed-file check, the per-file trust check, link checks, online-only check) and each made a test fail. Without the link checks a file genuinely moved through a link out of the folder.
Security cases tested: see docs/security/2026-09-10-real-folder-ai-disclosure-review.md and 2026-09-10-real-folder-tidy-review.md — every row has a named test, plus a sentinel file outside the folder that must never change.
Build/test evidence: Release build with zero warnings; 813 tests passed, none skipped; dotnet format clean.
AI containment check: DeskAI.AI still references Core only and receives only a request record. TidyAiService holds no scanner, reader, index, journal, or executor, and AutomaticCheckService and TidyAiService tests fail if either is given the real-folder executor.
Trade-off/ADR: ADR 0020 (preview-then-send; no full paths from real folders), ADR 0021 (one executor, live trust, undo ships with tidy).
What is deliberately not built: finding the last tidy after reopening DeskAI, recovery of an interrupted real tidy (step 4), review from automatic checks (step 5), delete or Recycle Bin, moving out of the folder.
Next small task: V0.6 step 4 — "Last tidy" with Undo after restart, and the interrupted-tidy prompt.
```

## V0.6 Step 4 Learning Log — 2026-09-11

```text
What became usable: Undo for the last tidy after DeskAI is closed and reopened, and a clear question when a tidy was interrupted by a crash ("7 of 12 files moved" — Undo those 7 / Keep them). A tidied folder can be disconnected again.
Main data flow (reopen): Organize shows a folder → TidyRunService.FindInterruptedAsync → FolderTidyExecutor.CheckInterruptedAsync (lock, folder re-check, FileOperationRunner.CheckInterrupted per file, record → RecoveryRequired) → card; otherwise FindLastAsync → IOperationJournal.ListForRootAsync → "Last tidy" with Undo.
Main data flow (answer): Keep → CloseInterruptedAsync (journal only) → it becomes the last tidy. Undo those → permission check → CloseInterruptedAsync → the ordinary UndoAsync with its per-file checks.
Classes/interfaces I can explain: RunLockFile, FolderTidyExecutor.CheckInterruptedAsync/CloseInterruptedAsync, FileOperationRunner.CheckInterrupted, JournalOperationState.NeedsReview, LastTidy, InterruptedTidy, StoppingJournal, TestApp.ReopenAsync.
New concept and my own explanation: Crash recovery is proof, not memory. The journal says what DeskAI meant to do; only the disk says what happened. A move counts as done only if the file left its old spot and the new spot holds a file with the recorded size and date; anything in between is handed to the person.
New concept and my own explanation (2): An invariant a lock creates. Because every run holds the lock for its whole life, any unfinished record seen while holding it must belong to a run that is dead — which is what makes it safe to judge. A lock inside one process could not promise that across two windows; a file opened with no sharing can, and Windows frees it if the process dies.
New concept and my own explanation (3): Simulating a crash honestly. The test journal lets the real code run and simply stops at one chosen write, so disk and journal are exactly what a power cut would leave. Then a second TestApp opens the same database, with nothing carried over in memory.
Hardest thing to get right: Deciding what counts as proof. "The file is gone from where it was" feels like proof it moved, but it is not — someone may have moved or edited it since. Removing the destination check made the changed-file test fail, which is the point of having it.
Bug found by testing: a tidied folder could not be disconnected; the plans and journal referred to it, SQLite refused, and the page showed a raw database error after the folder's search memory was already cleared.
Security cases tested: see docs/security/2026-09-11-tidy-recovery-review.md — every row has a named test, plus a sentinel file outside the folder.
Build/test evidence: Release build with zero warnings; 846 tests passed, none skipped; dotnet format clean.
AI containment check: nothing in this step touches DeskAI.AI; it still references Core only. TidyAiService still holds no journal, executor, scanner, or reader, and its test fails if it is given one.
Trade-off/ADR: ADR 0022 — the journal as the only memory, a cross-window lock file, check then ask, only the latest tidy.
What is deliberately not built: undoing older tidies, finishing an interrupted undo automatically, a tidy history list, recovery while DeskAI is closed.
Next small task: V0.6 step 5 — "Review in Organize" from an automatic check's notice, and the practice link.
```

## V0.6 Step 5 Learning Log — 2026-09-11

```text
What became usable: "Review in Organize" on an automatic check's notice opens Tidy a folder on the folder with the most rule matches, with a line saying what the rules place there. A "How tidying works" card on Organize replaced the practice page, which was removed at the owner's request.
Main data flow: AutomaticCheckService → AutomaticCheckResult.FolderToReview (an ID) → ShellViewModel notice → ReviewInOrganize() → OrganizeRequest.Ask → window opens a fresh Organize page → TidyViewModel.InitializeAsync takes the ID → selects that folder → preview → ReviewNote.
Classes/interfaces I can explain: OrganizeRequest, AutomaticCheckResult.FolderToReview, INavigationService.Navigate(route, fresh), TidyViewModel.ReviewNote and HowItWorksSteps.
New concept and my own explanation: Passing intent, not power. The check hands over a folder ID, the same thing a person picks from a list; everything that can move a file still sits behind a permission and a button press. The notice got more useful without the check getting more reach.
New concept and my own explanation (2): Deleting code is a safety change. An executor nothing can reach still exists, still compiles, and can be wired back by accident. Removing it shrinks what can move files to one class — but only after moving every test of shared rules that the remaining executor did not already have, so no protection is lost on the way out.
Hardest thing to get right: Not repeating a number that would be wrong on the next page. A check looks at every remembered file, including ones in subfolders; tidying never touches those. So the Organize line counts what the rules place in this list, and says so when that is nothing.
Security cases tested: the check still holds no executor or journal (a constructor test); Review in Organize moves nothing; without permission the page only asks; a disconnected folder falls back to the usual first folder; the request is used once; an old practice folder in the database is never tidied, undone, checked, or closed.
Build/test evidence: Release build with zero warnings; 830 tests pass, none skipped (fewer than before because the practice executor's and practice page's own tests were deleted with them; the shared-rule ones were ported first); dotnet format clean.
AI containment check: DeskAI.AI is unchanged and references Core only. AI is now asked from one place — Ask AI on a tidy-permitted folder, after a preview of exactly what is sent.
Trade-off/ADR: ADR 0023 — retire the practice page and its executor; keep the ControlledDemo scope for old databases.
What is deliberately not built: a practice or sample mode of any kind; opening more than one folder from a notice.
Next small task: V0.6 step 6 — the final security review record, checking the built system against the design's threat table.
```

## V0.6 Milestone Learning Log — 2026-09-11

```text
Milestone / date: V0.6 Organize Your Own Folders, completed 2026-09-11.
What became usable: A person can connect a folder, allow tidying, see where each loose file would go (by type, by their rules, or by AI after a preview of what is sent), untick anything, press Tidy, and undo — even after closing DeskAI or after a crash part-way. An automatic check can open the folder it found matches in. Nothing moves without a press of Tidy.
Main data flow: picker → connect (names, sizes, dates) → Allow tidying (re-check) → fresh scan → suggestions (rules > AI > type) → plan → Safety → list on the page → exact approval → FolderTidyExecutor (lock, live trust, journal first, per-file re-check, move without overwrite) → result → Undo from the journal.
Classes/interfaces I can explain: TidyPermissionService, TidySuggestionService, TidyAiService, TidyRunService, IFolderTidyExecutor, FolderTidyExecutor, FileOperationRunner, RunLockFile, IOperationJournal, OrganizeRequest.
New concept and my own explanation: A review is a search for missing proof, not a reading of the code. Walking each threat to a named test found three controls that looked right in code but that nothing proved — including the one check every tidy relies on, which tests had always replaced with a fake.
Hardest bug and root cause: A tidied folder could not be disconnected. The saved plans referred to the folder with a RESTRICT foreign key, and the disconnect had already cleared the search index before the delete failed — a half-done revoke with a raw database error on screen.
Security cases tested: the threat table in docs/security/2026-09-11-v0.6-milestone-review.md, every row with a named test and a sentinel outside the folder.
Build/test evidence: Release build with zero warnings; 841 tests pass, none skipped; dotnet format clean.
AI containment check: DeskAI.AI references Core only and has no file, journal, executor, process, or registry code; the AI and automatic-check services fail a test if handed an executor or the journal.
Trade-off/ADR: ADR 0019 (separate permission), 0020 (preview before AI), 0021 (one executor, live trust), 0022 (recovery by proof), 0023 (practice page retired).
What is deliberately not built: delete or Recycle Bin, moving out of the folder or out of subfolders, automatic tidying, undoing older tidies, running while closed.
Next small task: the owner's manual V0.6 sign-off, then choose between confirming duplicates by content (V0.4) and checks after the window is closed (V0.5).
```

## V0.4 Duplicate Confirmation Learning Log — 2026-09-11

```text
What became usable: "Check if they're really copies" on Home. After a dialog naming how many files, folders, and bytes would be read, DeskAI reads them and says which possible copies are identical, which only share a size, and which it could not check and why.
Main data flow: DuplicateFinderService size groups → DuplicateCheckService.PrepareAsync (question, opens nothing) → dialog → Compare → CompareAsync (folder re-check, first 64 KB, whole file only when beginnings match) → IFileFingerprinter / FileFingerprinter (checks, read-only, SHA-256 in memory) → groups → Home.
Classes/interfaces I can explain: DuplicateCheckService, DuplicateCheckQuestion, DuplicateCheckResult, IFileFingerprinter, FileFingerprinter, FileFingerprint.
New concept and my own explanation: Consent sized to the action. A lasting permission fits something done often; an occasional, heavy read fits a question asked each time, naming exactly what will be read. Nothing stays behind to be forgotten about.
New concept and my own explanation (2): Staged work. Reading the beginning first rules out most files cheaply; only files that still look alike are read to the end, and what could not be read inside the limits is reported, never guessed.
Hardest thing to get right: Making the limits testable without gigabyte files. The limits live in the service, which a fake reader can drive with pretend sizes; the real reader is tested separately on small generated files.
Security cases tested: see docs/security/2026-09-11-duplicate-confirmation-review.md — each row has a named test; six controls were removed on purpose and each made a test fail.
Build/test evidence: Release build with zero warnings; 881 tests pass, none skipped; dotnet format clean.
AI containment check: DeskAI.AI is unchanged and references Core only. The new reader is reachable only from DuplicateCheckService, and a test fails if any other Core type takes it; nothing read goes to AI.
Trade-off/ADR: ADR 0024 — asked each time rather than a stored permission.
What is deliberately not built: removing copies, storing fingerprints, using confirmed copies in the health score.
Next small task: the owner's manual checks for V0.6 and this feature, then decide on V0.5's checks after the window is closed or V0.7.
```

## V0.7 My Workspace Learning Log — 2026-09-14

```text
Milestone / date: V0.7 first slice (pieces A + B), 2026-09-14.
What became usable: A My workspace page. Starter packs (Student, Developer, Gaming, Productivity, Minimal) preview and then add saved searches and switched-off rules; saved searches can be pinned as tiles with honest counts and opened in Search.
Main data flow: pack card → StarterPackService.PreviewAsync (reads searches and rules, marks skips, saves nothing) → dialog → Add → AddAsync (reads again, saves searches with pins, rules via StarterPackRule.ToRule switched off) → outcome line on the card. Tile: saved search (is_pinned) → PinnedSearchService.CountAsync → FileSearchService → PinnedCount → wording.
Classes/interfaces I can explain: StarterPack, StarterPackCatalog, StarterPackRule.ToRule, StarterPackService, StarterPackPreview/Outcome, PinnedSearchService, PinnedCount, SearchRequest, WorkspaceViewModel, WorkspacePage.
New concept and my own explanation: A preview is not a promise. The dialog can stay open while something changes, so Add rebuilds the plan from what is stored now; anything new since is skipped, never overwritten.
New concept and my own explanation (2): Safe by default at the one choke point. Every pack rule becomes real in ToRule, which switches it off — so no caller can forget to, and a mutation test (switching it on) makes a page test fail.
New concept and my own explanation (3): Additive schema migration. Adding a column with a default leaves old rows valid; checking pragma_table_info first makes the migration safe to run twice.
Hardest thing to get right: Wording a number honestly. "0 files" looks like a measurement even when nothing was searched, so a count, "no folders", "not understood", and "stopped at the limit" are four different results rather than one integer.
Security cases tested: preview saves nothing; clashes skipped whatever the capitals; re-read before add; limits of 50 searches and 8 pins; rules Off end to end (Tidy and check unchanged until turned on, no file moved); neither service can be given an executor, journal, planner, scanner, reader, credential vault, or AI provider.
Build/test evidence: Release build with zero warnings; 1006 tests pass, none skipped; dotnet format clean.
AI containment check: nothing in this slice touches DeskAI.AI or sends anything; the Workspace services take no AI or credential type, and a test fails if they do.
Trade-off/ADR: ADR 0026 — one-time starter packs, not a remembered profile; fixed catalog in code; no Custom card.
What is deliberately not built: folder templates, DeskAI themes, desktop and wallpaper changes, removing a pack as a unit, editing packs, pins on Home.
Next small task: the owner's manual check of My workspace (MANUAL-TESTING.md), then choose the next V0.7 piece.
```

## Portfolio Evidence to Collect

Keep a clean architecture diagram, safe preview screenshots using dummy data, a short undo demonstration, representative Safety tests, an ADR showing a real trade-off, performance measurements on synthetic folders, and release notes. In interviews, discuss constraints and verification rather than raw line count or “AI built it.”
