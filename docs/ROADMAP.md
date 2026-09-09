# Roadmap

## How to Use This Roadmap

Build one vertical, verifiable slice at a time. Every milestone ends with a compiling application, passing relevant tests, documentation updates, and an honest demonstration. Future items are not implemented merely because their interfaces are imaginable.

Status legend: **Now** = next work, **Planned** = sequenced, **Future** = direction only.

## V0.1 — Safe Foundation (**Complete — 2026-09-07**)

Goal: a minimal WinUI application and testable domain/safety skeleton with no real file mutations.

- Inspect installed .NET, Visual Studio/Build Tools, Windows SDK, and WinUI support; record selected target versions.
- Initialize Git, `.gitignore`, solution, source projects, test projects, package/version conventions, and CI-friendly build commands.
- Create a minimal WinUI shell with Dashboard, Organize, Search, Automation, and Settings placeholders.
- Establish MVVM, navigation, dependency injection, configuration, local logging/redaction, and error boundaries.
- Define initial Core types for authorized roots, file items, operations, plans, validation, approvals, and results.
- Implement pure Safety checks for canonical root containment and protected/unsupported paths using dummy inputs only.
- Add a generated temporary-filesystem test helper.
- Build and run all tests; keep actual mutation and providers as fakes.
- Add initial ADRs for target versions, packaging assumptions, and major libraries.

Exit criteria: the solution builds in the supported Windows environment; tests run; navigation works; dependency boundaries are checked manually or automatically; Safety negative tests exist; no code accesses personal folders.

## V0.2 — Scanner, Planner, Preview, Executor, Undo (**Complete — 2026-09-07**)

Goal: the complete local, preview-first organizer loop for one explicitly selected folder.

1. ✅ Read-only, cancellable, bounded metadata scanner with access-error handling and reparse-point protection. Completed 2026-09-07; backend only and tested with generated temporary data.
2. ✅ Deterministic classification by extension/filename metadata and typed configurable folder recipes. Completed 2026-09-07; rule-only backend with no AI or filesystem side effects.
3. ✅ Side-effect-free organization planner with stable operation IDs, explanations, typed issues, combined-operation conflict detection, and Safety blocking for conflicted plans. Completed 2026-09-07; backend only.
4. ✅ Preview UI with per-operation selection, issues, blocked reasons, and plan revisioning. Completed 2026-09-07 using generated in-memory sample data; execution is visibly disabled.
5. ✅ Safety review gate and narrow move/rename/create-folder executor restricted to a generated, marker-protected temporary demo root. Completed 2026-09-07 with explicit plan-revision approval, live containment/link/collision checks, and dummy data only.
6. ✅ SQLite schema v2 migrations for settings, authorized roots, versioned plans/operations, and execution transactions. Completed 2026-09-07 with fresh-schema, v1-upgrade, and foreign-key tests; repositories/journaling remain step 7.
7. ✅ SQLite write-ahead operation journal, per-operation/partial outcomes, conservative recovery state, recent activity, and validated same-session undo. Completed 2026-09-07 for the controlled temporary demo only.
8. ✅ Native folder picker with explicit confirmation, revocable metadata-only authorization, bounded read-only preview, and deterministic refusal of every mutation plan for that scope. Completed 2026-09-07; real-folder execution remains disabled.

Exit criteria: a user-controlled demo can scan, preview, approve, execute, inspect history, and undo safely; collision/traversal/link/stale-plan tests pass; rule-only mode works.

## V0.3 — AI Integration and Privacy (**Complete — 2026-09-08**)

Goal: optional AI improves ambiguous suggestions without changing the safety model.

- ✅ Provider-neutral contracts and deterministic fake provider.
- ✅ Privacy dashboard and per-data-category disclosure policy.
- ✅ Strict structured-output schema, validation, and prompt-injection test corpus.
- ✅ Windows Credential Manager storage with SQLite references only.
- ✅ Constrained OpenAI-compatible local adapter for explicit loopback endpoints.
- ✅ OpenRouter BYO-key adapter with a user-selected model, sharing consent, a fixed HTTPS destination, and no silent fallback.
- ✅ AI provider, confidence, category, and explanations in the sample preview.
- ✅ Daily cloud-request cap, timeout, cancellation, bounded payloads, no retries, usage reporting, and distinct failure UX.

Exit criteria: disabling AI preserves the organizer; protected/unapproved data never enters requests; malformed/malicious output cannot cause execution; credentials do not appear in database/logs.

## V0.4 — Search and Storage Intelligence (**Now**)

Goal: find and understand files without needing to move them.

1. ✅ Incremental local metadata index with root-scoped storage and cascade erasure.
   Completed 2026-09-09; backend only, invoked by tests with generated temporary data.
   Nothing is indexed automatically and no UI calls it yet.
2. ✅ Structured search filters over the index.
   Completed 2026-09-09; backend only, invoked by tests with generated temporary data.
   Filters are validated in Core and applied by the store; no UI calls them yet.
3. ✅ Natural-language query translation into a constrained local query model.
   Completed 2026-09-09; deterministic local vocabulary with no AI, backend only.
   Every understood part produces a chip, and understanding nothing is reported as such.
   The Search page now uses steps 1-3: phrase, chips, results, and stated scope.
   Search also connects, refreshes, and disconnects folders, which is what fills
   the index; without it every V0.4 feature was unreachable.
4. ✅ Smart Collections as saved virtual queries.
   Completed 2026-09-09; saved searches store the typed phrase and no folder, so a
   relative phrase stays relative and one can never outlive an authorization.
   Implemented in code as SavedSearch; "Smart Collection" remains the product term.
5. ✅ Storage summaries, large/old downloads, archive and installer views.
   Completed 2026-09-09; Home shows size by category, the largest files, and what has
   not changed in six months, aggregated in SQL and scoped to connected folders only.
   It describes and never proposes: there is no cleanup action that bypasses preview.
6. ◐ Exact duplicate candidates using staged size/hash checks; possible-duplicate review.
   Stage 1 completed 2026-09-09: files sharing an exact size are grouped and shown on
   Home as possible duplicates, merged across folders, with no file ever opened.
   Stage 2, confirming by content hash, is deliberately NOT done here. Hashing reads
   file bytes, which the metadata-only authorization these folders were connected
   under does not permit, so it belongs with step 8 permission-gated content work.
7. ✅ Organization Health score with transparent components.
   Completed 2026-09-09; Home shows a score out of 100 next to the three parts that
   produced it — possible copies, files sitting unused, and types DeskAI cannot recognise —
   each with what it measured and how much it counted for. The calculation is pure
   arithmetic over the readings already gathered, so it reaches nothing new, and a folder
   with nothing remembered is reported as not measured rather than scored.
8. ⬜ Optional permission-gated content extraction, followed later by local embeddings/semantic search.

Exit criteria: results are scoped to authorized roots, index deletion/privacy controls work, score is explainable, and no cleanup action bypasses preview.

## V0.5 — Rules and Automation (**Planned**)

Goal: turn repeated intent into deterministic, auditable behavior.

- Typed conditions/actions, rule simulator, conflict detection, versioning, and manual rule editor.
- Natural-language-to-rule drafting with review.
- Explicit approval scope and invalidation when rules change.
- Folder watchers and/or scheduler selected through an ADR.
- Run history, notifications, pause/disable controls, missed-run behavior, and safe concurrency.
- Safe default: risky or novel outcomes return to preview.

Exit criteria: rules can be explained and simulated; background execution cannot widen scope; each run is recoverable/auditable.

## V0.6 — Workspace Profiles and Design (**Future**)

Goal: turn organization/search into tailored workspaces.

- Student, Developer, Gaming, Productivity, Minimal, and Custom profiles.
- Folder templates, pinned Smart Collections, and workspace setup suggestions.
- Desktop layout previews and safe shortcut/icon suggestions.
- Themes and wallpapers; local image generation only after hardware/license/privacy design.
- No direct desktop-shell mutation without a dedicated security/recovery design.

## V0.7 — Extensibility and Distribution (**Future**)

- Capability-scoped plugin contracts and manifest.
- Isolation, signing/trust, compatibility, permissions, update, and revocation model before community plugins.
- Accessibility, localization foundation, performance profiling, crash recovery, import/export, and privacy review.
- Packaging, installer/uninstaller behavior, GitHub Actions, GitHub Releases, update policy, SBOM/dependency checks, and qualifying open-source code-signing options.

## V1.0 — Stable Release (**Planned target**)

- Supported Windows versions and hardware guidance documented.
- Polished organizer loop, undo/history, protected items, rule-only mode, privacy controls, and at least one well-supported optional AI path.
- Security threat review; destructive/escape scenarios tested.
- Accessibility and keyboard navigation reviewed.
- Clean install/update/uninstall and data-retention behavior verified.
- User documentation, contribution guide, license, security reporting, and release notes present.
- No placeholder UI presented as complete functionality.

## Explicitly Deferred Beyond V1 Unless Reprioritized

Cross-platform clients, team/cloud sync, hosted accounts, model training on user data, broad Windows control, widgets, rich desktop-shell replacement, marketplace-scale plugins, and automatic permanent deletion.

## Recommended Task Rhythm

For each feature: agree on behavior and threat cases → add/update Core model → write Safety/unit tests → implement adapter/use case → build UI → run focused/full tests → update docs → explain concepts and demo evidence. Keep commits small enough to review and undo.
