# Roadmap

## How to Use This Roadmap

Build one vertical, verifiable slice at a time. Every milestone ends with a compiling application, passing relevant tests, documentation updates, and an honest demonstration. Future items are not implemented merely because their interfaces are imaginable.

Every milestone's exit criteria include, whether or not they say so: each feature a person can
reach has a page test that uses it the way a person does (see the Feature Coverage Map in
`TESTING.md`), and every bug found by hand has a test that failed before its fix. A milestone
whose features only have engine tests is not complete. This rule was added on 2026-09-10,
after the first page tests found four bugs that 545 engine tests had missed.

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

## V0.4 — Search and Storage Intelligence (**Complete except duplicate confirmation — 2026-09-09**)

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
   Stage 2, confirming by content hash, is still NOT done, and is the one open item in
   this milestone. Step 8 has since built a content permission, but that is not enough on
   its own: hashing must read a whole file, and the consent dialog people actually agree to
   says DeskAI reads only the beginning of each text file. Confirming duplicates therefore
   needs its own consent wording and its own bounds, not a quiet reuse of this one. It is
   carried forward rather than rushed to close a milestone.
7. ✅ Organization Health score with transparent components.
   Completed 2026-09-09; Home shows a score out of 100 next to the two parts that produced
   it — possible copies and files sitting unused — each with what it measured and how much
   it counted for. The calculation is pure arithmetic over the readings already gathered, so
   it reaches nothing new, and a folder with nothing remembered is reported as not measured
   rather than scored. Recalibrated the same day after running against realistic folders:
   the first thresholds flagged a settled archive as "worth a look" for being old, and
   charged a folder points for file types DeskAI had simply never learned. Age is now weak
   and generous, unrecognised types are a stated limit on the reading instead of a penalty,
   and the classifier covers many more everyday extensions.
8. ✅ Optional permission-gated content extraction, followed later by local embeddings/semantic search.
   Stage 1 completed 2026-09-09: the permission gate only. `RootCapabilities` is now the
   single answer to what a connected folder permits, and it denies any scope not explicitly
   listed, so extending the model fails closed instead of open. A `MetadataAndContent` scope
   exists and grants reading inside files and nothing else — not mutation, and not the
   reverse either. Nothing in the product can produce that scope yet and no file is opened
   anywhere; the refusal path is deliberately built and tested before the capability it
   guards. See `docs/decisions/0014-content-access-capability-gate.md` and
   `docs/security/2026-09-09-content-access-gate-review.md`.
   Stage 2 completed 2026-09-09: `PlainTextExtractor` reads a bounded 64 KB prefix of plain
   text from one file in a content-authorized folder. Plain-text formats only; PDF and
   Office are refused before opening, because parsing them means running a third-party
   parser over attacker-controlled binary and is its own security question. Extracted text
   is returned and stored nowhere. The extractor is registered in no container and called by
   nothing, so it is unreachable from the running app. See
   `docs/decisions/0015-plain-text-only-content-extraction.md` and
   `docs/security/2026-09-09-plain-text-extraction-review.md`.
   Stage 3 completed 2026-09-09: the consent that grants the scope, and its one consumer.
   A connected folder can be allowed to have its text files read, through a separate dialog
   naming exactly what is opened, what is not, and that nothing read is saved or sent. The
   permission is shown in words on the folder row and can be withdrawn without confirmation.
   Disconnect now works for a content-authorized folder, closing the gap ADR 0014 recorded.
   `ContentSearchService` finds files whose words match a typed phrase — at most 50 files per
   search, 64 KB each, text formats only — and the results state how many files were opened.
   Sending extracted text to an AI provider, reading PDF or Office documents, and storing
   extracted text each remain unaccepted. See
   `docs/security/2026-09-09-content-consent-and-search-review.md`.
   Local embeddings and semantic search remain future work, not part of this milestone.

Exit criteria: results are scoped to authorized roots, index deletion/privacy controls work, score is explainable, and no cleanup action bypasses preview.

## V0.5 — Rules and Automation (**Now**)

Goal: turn repeated intent into deterministic, auditable behavior.

- ✅ Typed conditions/actions, rule simulator, conflict detection, versioning, and manual rule editor.
  Completed 2026-09-09: a closed set of typed conditions and one action, evaluation that is
  pure and order-independent, conflict detection that refuses rather than guesses, and
  versioning. Rules are stored in schema version 9, and the Automatic tasks page can write
  one, turn it on and off, delete it, and show a practice run of exactly what the rules would
  do — including the files two rules disagreed about, which are left alone. Evaluation
  returns proposals rather than plan operations, and there is deliberately no button that
  carries a rule out: that would be a way around the preview. Nothing runs in the background
  and the page says so. The editor is basic on purpose — a name, a name-contains, an optional
  file ending, and a destination. See
  `docs/decisions/0016-typed-rule-domain-and-approval-scope.md`.
- ✅ Explicit approval scope and invalidation when rules change.
  Completed 2026-09-09; an approval stores each rule at its version plus a fingerprint of the
  exact moves shown, so editing, adding, removing, or disabling a rule invalidates it — and
  so does the same untouched rules wanting to move different files.
- ✅ Safe default: risky or novel outcomes return to preview.
  Completed 2026-09-09 as part of the approval check above: an outcome that differs from the
  one approved is reported as needing review rather than carried out.
- ✅ Natural-language-to-rule drafting with review.
  Completed 2026-09-09; a typed sentence such as "move invoices to Documents" is read into the
  rule form for review. Deterministic and local with a fixed vocabulary and no AI, the same
  choice search made. It fills the boxes and stops — understanding a sentence is not the same
  as someone agreeing to what was understood — and every part understood is stated back. A
  bare "word files" is only read as a file ending when the word is a type DeskAI knows, so
  "invoice files" becomes a name to look for rather than an ending of ".invoice".
- ◐ Folder watchers and/or scheduler selected through an ADR.
  Decided and half built on 2026-09-10. ADR 0017 chose a periodic check over a folder
  watcher: `FileSystemWatcher` holds a handle on a real personal folder, drops events under
  load without saying so, and storms during a cloud-sync pass, and rules read remembered
  metadata rather than the live disk, so instant reaction buys very little. While DeskAI is
  open it refreshes what it remembers, runs the existing practice run, and shows a count in
  the top corner — it holds no executor and a test asserts one cannot be added without
  failing. How often is chosen in words, there is a pause switch that also stops a check
  already running, and a Windows notification is opt-in and off by default. The Automatic
  tasks page no longer claims nothing runs on its own, because that stopped being true; it
  says instead that DeskAI never moves a file on its own, which is the promise that holds.
  See `docs/decisions/0017-periodic-rule-checks-and-background-choice.md` and
  `docs/security/2026-09-10-automatic-check-review.md`.
  Checking after the window is closed is decided in the same ADR but NOT built: no code
  produces that mode and it is absent from the UI. It is the remaining half, and it needs
  its own security review because a process running while nobody is present is a different
  threat case.
- ✅ Run history, notifications, pause/disable controls, missed-run behavior, and safe concurrency.
  Completed 2026-09-10. Every check that actually ran is recorded — including the ones that
  were stopped part-way and the ones that failed, because a history that omitted those would
  be reassuring rather than accurate. A check that was not due is not recorded, since it did
  not happen. The history is bounded to the last 50 runs and pruned inside the same
  transaction as the insert, so there is no moment where the bound is untrue; it can also be
  cleared outright. It is deliberately not the operation journal: the journal makes file
  changes auditable and undoable, and a check changes nothing.
  Missed runs are reported rather than replayed. DeskAI closed for two days owes one check,
  and that check says "First check after DeskAI was closed or paused" instead of leaving a
  history that looks as though it had been watching all along.
  Notifications, the pause switch, and single-flight concurrency shipped with the previous
  item; pausing also cancels a check already in flight.

Exit criteria: rules can be explained and simulated; background execution cannot widen scope; each run is recoverable/auditable.

## V0.6 — Organize Your Own Folders (**Planned — recommended next**)

Goal: let DeskAI actually move and rename files in a folder someone connected, after they
approve each change in the preview.

Why this milestone exists: until now every connected folder has been look-only, and the
move, approval, journal, and undo loop has worked only inside the generated practice folder.
ADR 0010 and the 2026-09-07 capability audit both required "a later real-folder execution
milestone", but none was ever scheduled, so rules, automatic checks, and AI suggestions could
never lead to a file moving. Added 2026-09-10 after the owner found this while testing.

Design agreed with the owner on 2026-09-10, including a rebuilt Organize page and "?" help
on every page: `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md`.

1. Design and security review before code: a separate "allow DeskAI to organize this folder"
   permission, how it is shown and withdrawn, what happens when a file changes between preview
   and move, locked and cloud-only files, and recovery after a crash. Recorded as an ADR and a
   security review.
2. The organize permission as its own scope, granted only through its own dialog. It never
   silently replaces or widens look-only or read-text consent, and withdrawing it leaves the
   folder look-only.
3. Rule matches and Organize suggestions become an `OrganizationPlan` for that folder and open
   in the existing preview, where each change is chosen and approved. Automatic checks still
   only look.
4. The executor runs approved plans in an organize-permitted folder with live containment,
   link, collision, and stale-plan checks, write-ahead journaling, and new negative tests.
5. Undo that survives closing and reopening DeskAI, refusing safely when a file has changed
   since it was moved.

Out of scope here: deleting files, running rules without approval, and sending real file
names to an AI provider (that needs its own disclosure review).

Exit criteria: a person can connect a folder, allow organizing, approve a preview, see files
move, and undo it after a restart; every refusal case is tested with generated temporary data;
and each of those steps has a page test that performs it through the page, not only through
the executor.

## V0.7 — Workspace Profiles and Design (**Future**)

Goal: turn organization/search into tailored workspaces.

- Student, Developer, Gaming, Productivity, Minimal, and Custom profiles.
- Folder templates, pinned Smart Collections, and workspace setup suggestions.
- Desktop layout previews and safe shortcut/icon suggestions.
- Themes and wallpapers; local image generation only after hardware/license/privacy design.
- No direct desktop-shell mutation without a dedicated security/recovery design.

## V0.8 — Extensibility and Distribution (**Future**)

- Capability-scoped plugin contracts and manifest.
- Isolation, signing/trust, compatibility, permissions, update, and revocation model before community plugins.
- Accessibility, localization foundation, performance profiling, crash recovery, import/export, and privacy review.
- Packaging, installer/uninstaller behavior, GitHub Actions, GitHub Releases, update policy, SBOM/dependency checks, and qualifying open-source code-signing options.

## V1.0 — Stable Release (**Planned target**)

- Supported Windows versions and hardware guidance documented.
- Polished organizer loop that works on folders people connect (V0.6), undo/history, protected items, rule-only mode, privacy controls, and at least one well-supported optional AI path.
- Security threat review; destructive/escape scenarios tested.
- Accessibility and keyboard navigation reviewed.
- Clean install/update/uninstall and data-retention behavior verified.
- User documentation, contribution guide, license, security reporting, and release notes present.
- No placeholder UI presented as complete functionality.

## Explicitly Deferred Beyond V1 Unless Reprioritized

Cross-platform clients, team/cloud sync, hosted accounts, model training on user data, broad Windows control, widgets, rich desktop-shell replacement, marketplace-scale plugins, and automatic permanent deletion.

## Recommended Task Rhythm

For each feature: agree on behavior and threat cases → add/update Core model → write Safety/unit tests → implement adapter/use case → build UI → run focused/full tests → update docs → explain concepts and demo evidence. Keep commits small enough to review and undo.
