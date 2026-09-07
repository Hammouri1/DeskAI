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

## V0.2 — Scanner, Planner, Preview, Executor, Undo (**Now**)

Goal: the complete local, preview-first organizer loop for one explicitly selected folder.

1. ✅ Read-only, cancellable, bounded metadata scanner with access-error handling and reparse-point protection. Completed 2026-09-07; backend only and tested with generated temporary data.
2. ✅ Deterministic classification by extension/filename metadata and typed configurable folder recipes. Completed 2026-09-07; rule-only backend with no AI or filesystem side effects.
3. ✅ Side-effect-free organization planner with stable operation IDs, explanations, typed issues, combined-operation conflict detection, and Safety blocking for conflicted plans. Completed 2026-09-07; backend only.
4. ✅ Preview UI with per-operation selection, issues, blocked reasons, and plan revisioning. Completed 2026-09-07 using generated in-memory sample data; execution is visibly disabled.
5. ✅ Safety review gate and narrow move/rename/create-folder executor restricted to a generated, marker-protected temporary demo root. Completed 2026-09-07 with explicit plan-revision approval, live containment/link/collision checks, and dummy data only.
6. ✅ SQLite schema v2 migrations for settings, authorized roots, versioned plans/operations, and execution transactions. Completed 2026-09-07 with fresh-schema, v1-upgrade, and foreign-key tests; repositories/journaling remain step 7.
7. Operation journal, partial-failure reporting, recovery state, and validated undo.
8. User folder picker and real-folder use only after sandbox demonstrations and explicit confirmation.

Exit criteria: a user-controlled demo can scan, preview, approve, execute, inspect history, and undo safely; collision/traversal/link/stale-plan tests pass; rule-only mode works.

## V0.3 — AI Integration and Privacy (**Planned**)

Goal: optional AI improves ambiguous suggestions without changing the safety model.

- Provider-neutral contracts and deterministic fake provider.
- Privacy dashboard and per-data-category disclosure policy.
- Strict structured-output schema, validation, and prompt-injection test corpus.
- Windows-protected credential storage.
- One local runtime adapter or documented local-compatible endpoint.
- One BYO cloud adapter behind clear consent; no silent fallback.
- AI provenance/confidence/explanations in preview.
- Cost, timeout, cancellation, offline, malformed-response, and rate-limit UX.

Exit criteria: disabling AI preserves the organizer; protected/unapproved data never enters requests; malformed/malicious output cannot cause execution; credentials do not appear in database/logs.

## V0.4 — Search and Storage Intelligence (**Planned**)

Goal: find and understand files without needing to move them.

- Incremental local metadata index and structured search filters.
- Natural-language query translation into a constrained local query model.
- Smart Collections as saved virtual queries.
- Storage summaries, large/old downloads, archive and installer views.
- Exact duplicate candidates using staged size/hash checks; possible-duplicate review.
- Organization Health score with transparent components.
- Optional permission-gated content extraction, followed later by local embeddings/semantic search.

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
