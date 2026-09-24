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

## V0.4 — Search and Storage Intelligence (**Complete — 2026-09-11**; duplicate confirmation added last)

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
6. ✅ Exact duplicate candidates using staged size/hash checks; possible-duplicate review.
   Stage 1 completed 2026-09-09: files sharing an exact size are grouped and shown on
   Home as possible duplicates, merged across folders, with no file ever opened.
   Stage 2 completed 2026-09-11 (ADR 0024, review
   `docs/security/2026-09-11-duplicate-confirmation-review.md`): **Check if they're really
   copies** on Home. It did not reuse the "read inside files" permission, which promises only the
   beginning of text files. Instead each check asks first, in a dialog naming how many files,
   folders, and bytes would be read; no permission is stored. It reads the first 64 KB of each
   file and to the end only when beginnings match (200 files, 2 GB per file, 8 GB per check), and
   says which files are identical, which only share a size, and which were not checked and why.
   Nothing is kept, sent, changed, or offered for removal.
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
   Stage 2 completed 2026-09-09: `PlainTextExtractor` reads a bounded prefix of plain
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
   search, currently 256 KB each, text formats only — and the results state how many files were opened.
   Sending extracted text to an AI provider, reading PDF or Office documents, and storing
   extracted text each remain unaccepted. See
   `docs/security/2026-09-09-content-consent-and-search-review.md`.
   Local embeddings and semantic search remain future work, not part of this milestone.

Exit criteria: results are scoped to authorized roots, index deletion/privacy controls work, score is explainable, and no cleanup action bypasses preview.

## V0.5 — Rules and Automation (**Complete — 2026-09-13**)

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
- ✅ Folder watchers and/or scheduler selected through an ADR.
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
  Checking after the window is closed, decided and reviewed in ADR 0025
  (`docs/decisions/0025-checking-after-the-window-is-closed.md`, review
  `docs/security/2026-09-12-background-checking-review.md`), is now built too. A switch on
  Automatic tasks asks first — naming that DeskAI will never add itself to Windows startup —
  and, once agreed, the same DeskAI keeps running with a visible icon near the clock after
  the window closes rather than exiting. That icon's tooltip says how often DeskAI is
  looking or that it is paused, and its menu holds exactly three items: open DeskAI, pause
  checking, and quit DeskAI. It starts nothing. Launching DeskAI again while it is hidden
  reveals the running one instead of starting a second, so there is never more than one
  DeskAI and one database writer. A check still only ever produces a count; leaving DeskAI
  running keeps that count current and does not tidy anything while its owner is away.
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

## V0.6 — Organize Your Own Folders (**Complete — 2026-09-11; owner's manual sign-off outstanding**)

Goal: let DeskAI actually move and rename files in a folder someone connected, after they
approve each change in the preview.

Why this milestone exists: until now every connected folder has been look-only, and the
move, approval, journal, and undo loop has worked only inside the generated practice folder.
ADR 0010 and the 2026-09-07 capability audit both required "a later real-folder execution
milestone", but none was ever scheduled, so rules, automatic checks, and AI suggestions could
never lead to a file moving. Added 2026-09-10 after the owner found this while testing.

Design agreed with the owner on 2026-09-10, including a rebuilt Organize page and "?" help
on every page: `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md`.

Progress (build order from the design):
- ✅ Step 1, 2026-09-10: "?" help next to every feature, tested for completeness, length, and
  plain words.
- ✅ Step 2a, 2026-09-10: the separate tidy permission (ADR 0019), a scanner that notices
  hidden, system, and online-only files, the tidy suggestion engine, and the new "Tidy a
  folder" page. The Tidy button is shown switched off; nothing in a connected folder can move.
- ✅ Step 2b, 2026-09-10: AI as a suggestion source on your own folders (ADR 0020, disclosure
  review `docs/security/2026-09-10-real-folder-ai-disclosure-review.md`). AI may be asked only
  about files DeskAI cannot place, or about every file the person's rules do not place.
  Nothing is sent until a dialog has shown exactly what the AI will see and the person pressed
  Send. At most type, size and date, and name leave the computer, each only if allowed; never
  locations, folder names, contents, or DeskAI's file IDs. AI names a category, DeskAI names
  the folder. Nothing moves.
- ✅ Step 3, 2026-09-10: tidying for real (ADR 0021, review
  `docs/security/2026-09-10-real-folder-tidy-review.md`). One shared set of move rules for the
  practice folder and real folders; a real folder is trusted only while it is still connected,
  may be tidied, is at the same place, and passes its safety re-check, asked before every
  file. Each file must match the list the person saw; busy, online-only, hidden, changed, or
  blocked files stay with a reason. The result says what happened, and **Undo** for that tidy
  ships in the same step, needing the same permission.
- ✅ Step 4, 2026-09-11: undo after reopening, and interrupted tidies (ADR 0022, review
  `docs/security/2026-09-11-tidy-recovery-review.md`). The last tidy is found again from the
  journal and can be undone after a restart. A tidy or undo that stopped part-way is checked file
  by file against the disk — moved, not moved, or "please check", never a guess — and put to the
  person as "7 of 12 files moved" with **Undo those 7** / **Keep them**; nothing else runs in
  that folder until they answer. A lock file now keeps two DeskAI windows from running at once.
  Found while planning and fixed first: a tidied folder could not be disconnected; disconnecting
  now forgets its tidy history too.
- ✅ Step 5, 2026-09-11: **Review in Organize** on an automatic check's notice opens Tidy a folder
  on the folder with the most matches and says what the person's rules place there; the check
  still only looks. The practice page was removed at the owner's request (ADR 0023) and replaced
  by a "How tidying works" card on Organize; its executor went with it, so one executor remains.
- ✅ Step 6, 2026-09-11: the milestone's security review record,
  `docs/security/2026-09-11-v0.6-milestone-review.md`. Every row of the design's threat table and
  every Part 2 rule traced to its control and a named test. It found three controls with no test
  — the folder re-check behind Allow tidying, system files, and a file turned into a link after
  the list — and closed them; removing each control makes its new test fail.

Exit criteria met in code and automated tests on 2026-09-11: a page test connects a folder,
allows tidying, tidies, reopens DeskAI over the same database, and undoes. What only a person
can check — the dialogs, keyboard and screen-reader use, a real crash, two windows — is the
"V0.6 sign-off" list in `MANUAL-TESTING.md`, still to be done by the owner.

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

Out of scope here: deleting files and running rules without approval. Sending real file
information to AI was reviewed and built in step 2b, behind a preview of each request.

Exit criteria: a person can connect a folder, allow organizing, approve a preview, see files
move, and undo it after a restart; every refusal case is tested with generated temporary data;
and each of those steps has a page test that performs it through the page, not only through
the executor.

## V0.7 — Workspace Profiles and Design (**Now — started 2026-09-14**)

Goal: turn organization/search into tailored workspaces.

Built one piece at a time, safest first (design:
`docs/superpowers/specs/2026-09-14-my-workspace-starter-packs-design.md`):

- ✅ A + B, 2026-09-14: **My workspace** (ADR 0026). Profiles are one-time starter packs —
  Student, Developer, Gaming, Productivity, Minimal — that preview, then add ordinary saved
  searches and rules; rules always arrive switched off, and nothing a person already has is
  replaced. Saved searches can be pinned (up to eight) as tiles with honest counts. No file or
  Windows setting changes. There is no Custom pack; making your own stays in Search and
  Automatic tasks.
- ✅ C, 2026-09-16: **Folder templates** (ADR 0027, review
  `docs/security/2026-09-14-folder-templates-review.md`). A template — one per pack, or names the
  person types — makes empty folders one level inside a connected folder, after a preview of
  exactly which folders and a yes, with the same "Allow tidying" permission Tidy uses, through
  the one executor, journaled, and undoable (empty folders only) even after reopening. Nothing is
  moved. Typed names pass a plain-language check and then the path policy. A run that stopped
  part-way is asked about on Organize as folders. Adding a pack still changes nothing on disk.
- ✅ D, 2026-09-16: **DeskAI's look** (ADR 0028). Four looks — Slate, Graphite, Sand, Ocean —
  and a light / dark / follow-Windows choice, on My workspace. A look tints only the neutral
  ground, surfaces, and lines; the accent, caution, and danger colours are not a look's to
  change, so "green means safe or confirmed" holds in every look, and a test checks each look
  keeps the shared text readable. Applied to the DeskAI window at once and at startup before it
  shows; stored in the existing key/value settings table; unknown values fall back to Slate.
  Changes nothing in Windows and no file.
- ✅ E, 2026-09-16: **Desktop and wallpaper** (ADR 0029, review
  `docs/security/2026-09-16-desktop-and-wallpaper-review.md`), scoped by the owner that day.
  **Wallpaper:** pick one picture in the Windows file dialog, see it, press "Use as wallpaper";
  the old wallpaper is written down before the change and "Put the old wallpaper back" restores
  it, also after reopening. Only a plain local JPG/PNG/BMP under 50 MB; DeskAI never lists
  folders for pictures and never makes or downloads one. This is DeskAI's first change to a
  Windows setting, and the only one. **Desktop:** "Tidy my Desktop" (replaced by the Your folders card in V1.1, ADR 0032) connected the Desktop folder
  through the known-folder API (names, sizes, dates) and opens it in Organize, where the
  ordinary permission, preview, Tidy, and undo apply; shortcuts are left alone. Tests never touch
  the real wallpaper or Desktop: both are replaced in `TestApp` and asserted to be sandboxed.
- **Deferred beyond V0.7 (owner, 2026-09-16):** shortcut and icon suggestions, desktop layout
  previews, and local image generation. Each would need new executor commands or a new
  source of pictures, and its own review. There is still no desktop-shell mutation beyond the
  wallpaper picture.

**V0.7 is complete as of 2026-09-16.** The original bullet list for it is kept for the record:

- Student, Developer, Gaming, Productivity, Minimal, and Custom profiles. (Built as one-time
  starter packs; no Custom, by ADR 0026.)
- Folder templates, pinned Smart Collections, and workspace setup suggestions. (Templates and
  pinned searches built; "setup suggestions" are the packs.)
- Desktop layout previews and safe shortcut/icon suggestions. (Deferred, see above.)
- Themes and wallpapers; local image generation only after hardware/license/privacy design.
  (Looks and wallpaper built; generation deferred.)
- No direct desktop-shell mutation without a dedicated security/recovery design. (Held: the
  wallpaper picture is the one change, reviewed.)

## V0.8 — Extensibility and Distribution (**Complete — 2026-09-16**)

Design: `docs/superpowers/specs/2026-09-16-v0.8-distribution-design.md`; decisions: ADR 0030;
privacy review: `docs/security/2026-09-16-v0.8-privacy-review.md`. Built the same day as the
command-center redesign (`docs/superpowers/specs/2026-09-16-command-center-redesign-design.md`),
which restyled every page under the same rules and kept the menu names the owner chose.

- ✅ **GitHub builds and tests every push** (`.github/workflows/build.yml`): locked restore,
  Release build, all tests, format check, and a known-vulnerable-dependency check that fails
  the run.
- ✅ **A release zip per tag** (`.github/workflows/release.yml`): self-contained x64 publish with
  the tag as the version, zipped, with a CycloneDX software bill of materials, attached to a
  GitHub Release. No installer and no signing (ADR 0030); `docs/INSTALL.md` explains install,
  update, remove, and the unknown-publisher notice. DeskAI never checks online for updates;
  Privacy and AI shows the version.
- ✅ **Back up and restore** on Privacy and AI: a JSON file of rules and saved searches only,
  written where the person chose; restore previews what would be added and skipped, rebuilds
  every rule through the same checks a typed one passes, skips names already used, and
  **restored rules arrive switched off**.
- ✅ **Start fresh**: after a dialog, DeskAI forgets every folder, rule, search, key, AI choice,
  history, setting, look, and wallpaper memory. No file is touched. The data-retention control
  V1.0 asks for.
- ✅ **Accessibility**: `AccessibilityNameTests` reads the pages so every icon-only button and
  every input has a name for screen readers; keyboard and Narrator steps stay in the manual lists.
- ✅ **Performance**: an opt-in probe over 3,000 generated files, recorded in `docs/PERFORMANCE.md`.
- ✅ **Crash recovery**: already built (ADR 0022's interrupted-tidy recovery, the startup-failure
  window, the contained unhandled-exception handler); recorded, not rebuilt.
- **Deferred, recorded in ADR 0030:** add-ons (plugins) until the system is finished, with the
  boundary they must start from written down; code signing until a certificate exists;
  localization beyond culture-aware formatting until there is a second language.

The original bullet list is kept for the record:

- Capability-scoped plugin contracts and manifest. (Boundary recorded in ADR 0030; not built.)
- Isolation, signing/trust, compatibility, permissions, update, and revocation model before community plugins. (Deferred with the above.)
- Accessibility, localization foundation, performance profiling, crash recovery, import/export, and privacy review. (Done as listed; localization deferred.)
- Packaging, installer/uninstaller behavior, GitHub Actions, GitHub Releases, update policy, SBOM/dependency checks, and qualifying open-source code-signing options. (Zip, workflows, SBOM, no-self-update policy done; signing options recorded.)

## V0.9 — Tidy While I'm Away (**Complete — 2026-09-16**)

Built after its security review (`docs/security/2026-09-16-tidy-while-away-review.md`), ADR 0031,
and design (`docs/superpowers/specs/2026-09-16-tidy-while-away-design.md`), from the owner's
choice "Move a few, then wait":

- ✅ A per-folder switch on Organize, **Tidy this folder while I'm away**, offered only where
  tidying is already allowed and at least one rule is on. Turning it on opens a dialog naming
  the folder, the rules as worded, and the ceiling; its yes records `AwayTidyApproval` (the
  folder and every enabled rule at its version) in its own cascading table (schema 14).
- ✅ After each automatic check — including after the window is closed, if that is on —
  `AwayTidyService` runs once per folder with the yes: only loose files a switched-on rule
  places, never a type-placed or AI-placed file, never a subfolder's file, never a same-name
  clash, at most **25 files per run**, through the same `TidyRunService` and executor as a
  hand tidy, journaled and undoable.
- ✅ Anything unexpected stops it with the reason kept and shown: a rule edited, added,
  removed, or toggled (checked before every run and whenever the folder is shown); tidy
  permission withdrawn; a clash (the run stops before anything moves); a file the executor
  refused (the rest move, then the mode turns off); a folder that cannot be looked at.
- ✅ Undo first: a "While you were away" card above everything on Organize with the runs
  (counts and times, never a file name), **Undo the latest run**, and **Got it**; the notice in
  the window says the count and folder and offers Review in Organize; the notification, if on,
  carries a count only. Runs survive reopening.
- ✅ Every "nothing moves by itself" promise — Home's pill and sentence, Automatic tasks' first
  card and checking summary, the keep-running dialog and More details, the help topics — says
  the narrower truth while any folder has the mode on, and the old sentence when none does.
- ✅ Containment: `AutomaticCheckService` still holds no executor; the coordinator holds only
  `IAwayTidyRunner`, which `AwayTidyService` alone implements; it holds no AI, reader,
  fingerprinter, credential, or file store. Start fresh and disconnecting end the mode.
- Nothing permanently deleted, unattended or otherwise. Unchanged.

The original text is kept for the record:

Goal: let a rule that has already been approved carry itself out while nobody is watching.

This is the largest single increase in what DeskAI is trusted to do, so it is a version of its
own rather than a line inside another one. Everything before it stops at telling a person
something. This is the first thing that would act on its own.

- Standing approval: what it means to approve a rule's *outcome* in advance rather than one
  previewed list of moves, and how that approval is invalidated — by the rule changing, by the
  files changing, by a collision, by anything novel. ADR 0016 already invalidates an approval
  when the same untouched rules want to move different files; this has to answer what is left
  that can still be trusted after that.
- A dedicated security review, which is a precondition rather than a step. Every control
  DeskAI has today assumes a person is looking at a preview at the moment a file moves. That
  assumption ends here, and each control has to be re-argued without it: preview, collision
  handling, reparse points, time-of-check/time-of-use, and what a wrong move costs when it is
  noticed hours later.
- Undo becomes the primary control rather than the safety net, including undoing a run the
  person was not present for and did not see begin.
- A hard ceiling on what may happen unattended, decided before anything is built: which
  actions, how many files, which folders, and what makes a run stop and wait for a person.
- Nothing permanently deleted, unattended or otherwise. That rule does not bend here.

Depends on V0.5's "checks after the window is closed": an unattended tidy needs something
running while nobody is present, and that mode is where running unattended is designed and
reviewed. It does not depend on V0.7 or V0.8.

## V1.0 — Stable Release (**Complete in code and documents — 2026-09-16; the owner's manual sign-off of V0.7–V1.0 is outstanding**)

- ✅ Supported Windows versions and hardware guidance: `docs/INSTALL.md` (Windows 11 24H2 or
  later, x64; an ordinary laptop is enough — `docs/PERFORMANCE.md` records a 3,000-file folder
  connecting in about a tenth of a second).
- ✅ The organizer loop on connected folders, undo and history, protected locations, rule-only
  mode, privacy controls, and the optional AI path: V0.2–V0.9.
- ✅ Security threat review with destructive and escape scenarios traced to tests:
  `docs/security/2026-09-16-v1.0-release-review.md`.
- ✅ Accessibility and keyboard: `AccessibilityNameTests`, pills and badges with icon and word,
  high-contrast mapping, and the Tab and Narrator steps in every manual list.
- ✅ Clean install, update, uninstall, and data retention: unzip and run, replace the folder,
  Start fresh then delete; verified by `FreshStartPageTests` and `NeverStartsWithWindowsTests`,
  with the manual release-zip list.
- ✅ User documentation (`docs/USER-GUIDE.md`, `docs/INSTALL.md`), contribution guide
  (`CONTRIBUTING.md`), license (MIT, `LICENSE`, the owner's choice of 2026-09-16), security
  reporting (`SECURITY.md` at the root), release notes (`docs/RELEASE-NOTES.md`).
- ✅ No placeholder UI presented as complete: `NoPlaceholderUiTests`.
- Version 1.0.0 in `Directory.Build.props`; a `v1.0.0` tag makes the release zip.

Still the owner's to do by hand, recorded in `HANDOFF.md`: walk the manual lists for the
redesign, V0.8, and V0.9 (and the older V0.6 and background-checking sign-offs), and push the
first tag.

## V1.1 — After the Owner's First Look (started 2026-09-16)

The owner's first look at V1.0 raised four things: the Desktop would not connect, there was no
way to connect Downloads or the other personal folders from Home, a tile clipped its words, and
"there is no real use of the AI". Agreed order: the Desktop bug, the tile, the folders card, then
three AI features (A: plain language for Search and rules, B: "Plan this folder", C: "Ask DeskAI").

- ✅ 2026-09-16: **The Desktop connects even when DeskAI itself sits on it.** DeskAI's program
  folder is protected; a folder that merely *contains* a protected place now connects with the
  protected part skipped entry by entry, while a folder *inside* one stays refused. Page test
  first (`YourFoldersPageTests`), every test Desktop now holds a protected "program folder".
- ✅ 2026-09-16: **Pinned tiles keep words out of the number slot** ("No folders connect…" was
  clipped): a number in the big style only when there is one, words on a wrapping caption
  (`ShellLayoutTests` reads the XAML).
- ✅ 2026-09-16: **Only the person's own four folders** (ADR 0032, the owner's decision):
  DeskAI connects Desktop, Downloads, Documents, Pictures, or folders inside them, and nothing
  else, checked at connection and again before tidying. A **Your folders** card on Home and My
  workspace lists the four with one Connect / Tidy button each, replacing "Tidy my Desktop".
- ✅ 2026-09-16: **AI feature A — plain language on Search and Automatic tasks** (ADR 0033,
  review `docs/security/2026-09-16-sentence-ai-review.md`). "Let <service> read this" sends the
  typed words alone after a dialog; the AI answers with a few typed facts; DeskAI writes them as
  a sentence in its own fixed vocabulary and reads it exactly as a typed one, so AI gains no new
  reach and saved searches keep working.
- ✅ 2026-09-16: **AI feature B — Plan this folder** (ADR 0034, review
  `docs/security/2026-09-16-plan-folder-review.md`). The same request as Ask AI about every
  file rules do not place; the AI may also name at most 12 plain folders, each checked by the
  parser, the service, the path policy, and the executor; the plan is the ordinary preview,
  Tidy, and undo.
- ✅ 2026-09-16: **AI feature C — Ask DeskAI** (ADR 0035, review
  `docs/security/2026-09-16-ask-deskai-review.md`). A question box on Home: only the question is
  sent; the AI answers with a kind of question; DeskAI searches its own index, reads its own
  storage summary, or points at Organize, in its own words, with one button that opens a page.

- ✅ 2026-09-16: **Ask DeskAI asks once per service, not before every question** (ADR 0035
  amended, review addendum in `docs/security/2026-09-16-ask-deskai-review.md`), the owner's
  choice from the offered follow-ups. The yes is remembered by service, nothing is sent without
  it, the line under the box says which way it stands, and "Ask me each time" or Start fresh
  takes it back.
- ✅ 2026-09-16: every analyzer warning cleared, including two V1.1 introduced; a second
  performance run recorded after V1.1.

- ✅ 2026-09-17: **the words say where the icon near the clock really is** (ADR 0025 amended),
  after the owner looked for it and found nothing. Nothing was broken — the keep-running switch
  was off, so no icon existed — but the caption never said the switch is what creates the icon,
  and three places sent a person to "near the clock" when Windows 11 hides a new icon behind the
  arrow. One sentence is now appended to all four. The owner confirmed the icon afterwards.
- ✅ 2026-09-17: **the repository describes the product that exists.** The README had called
  DeskAI "a planned application" with V1.0 next and schema 7; both it and `INSTALL.md` also sent
  people to an empty Releases page. Rewritten around what a person can do today, with an honest
  limits section and instructions that work while there is no release.

- ✅ 2026-09-17: **the calm-workspace interface revision.** Every page now starts with the same
  clear introduction and puts its next useful action first. Home's folder choices are roomy
  cards; optional explanations collapse; Search places answers before saved searches; My
  workspace and Privacy and AI use plain task tabs so their unrelated jobs no longer form one
  long page. Lavender and Rose expand the neutral-only look catalog to six. A separate opt-in
  UI-preview build replaces every computer-facing service and uses generated Windows Temp data,
  so the redesign was visually checked without loading personal folders, keys, wallpaper, or
  the owner's DeskAI database.

V1.1 is complete in code, tests, and documents as of 2026-09-17, and the source is on GitHub
privately at https://github.com/Hammouri1/DeskAI. The owner's manual look at all of it, and the
first Actions run, are the next things (`HANDOFF.md`).

## Search Expansion — 2026-09-20

- ✅ First safe slice: fix Search's contradictory connected-folder line and improve the wide
  page layout; understand document-format words and match the remaining term inside local
  content; add separately approved, bounded `.docx`/`.xlsx` reading without expanding any
  older plain-text grant (ADR 0036 and its security review).
- ✅ PDF text: separate opt-in after document reading; a bounded, local parser helper with
  crash and timeout containment. Scanned pages are outside this slice (ADR 0037).
- ✅ Modern PowerPoint `.pptx` slide text: separate permission independent of PDF reading,
  bounded local ZIP/XML reader, nested-folder page test, and slide number in matching
  results (ADR 0039).
- ✅ Bounded visual inspection: a fresh read dialog, then a separate exact-batch cloud Send
  dialog when online AI is selected. JPEG/PNG/WebP, referenced slide pictures, and
  extractable PDF images can be judged by a vision-capable model with page/slide evidence.
  No model download or stored image index (ADR 0038 and its security review).
- ✅ 2026-09-24: **Search finds more of your files** (ADR 0041). A look goes 8 folders deep and
  up to 20,000 items (was 4 and 2,000); a look that stops early keeps what was remembered
  before instead of forgetting it, and the page says the folder was only partly checked; opening
  Search looks again at folders not checked in the last 10 minutes. Schema 15 records the last look.
- Remaining Search work: rasterized whole-page OCR, broader image formats, more complete
  coverage than the first 30 files / 12 images, and ranking across many connected roots.

**Owner's full Search goal, clarified 2026-09-21:** a person describes a forgotten file in
ordinary English and finds it across the subfolders of connected roots. “PowerPoint with
Hammouri on a slide” now has separately approved `.pptx` slide-text extraction. If the word is
only pixels, visual AI may read it but there is no dedicated OCR guarantee. “PDF with a
picture of a dog smelling a flower” now has a visual path for extractable embedded images;
unsupported PDF image encodings or whole-page graphics may still be missed. Results name
the file, page or slide where known, and the AI's short reason. The current pass is limited
to 30 files and 12 pictures per action, on top of the scanner's depth-8 / 20,000-entry cap.
No model is downloaded by DeskAI. Expand coverage only with honest limits and a fresh
security review.

The older **Let AI read this** button interprets only the typed sentence. The separate
picture action reads a bounded batch after an explicit choice and asks AI about its pixels.

## Desktop Studio — 2026-09-24

The owner's core idea: connect the Desktop and make it sorted *and* good-looking, folders and
files. One page, **Desktop Studio**, with one card per feature; each is chosen on its own and
previews first. Design: `docs/superpowers/specs/2026-09-24-desktop-studio-design.md`. Each step
is released and checked by the owner before the next.

- ✅ 2026-09-24: **Step 1 — Find groups** (ADR 0042). AI (after an exact Send window) or DeskAI's
  own guess sorts the Desktop's folders and loose files into at most 8 groups plus Not sure; the
  person renames, merges, and moves. Stored per folder (schema 16), erased with it. Changes
  nothing on disk or in Windows.
- Step 2 — icon-position feasibility probe: **done, no-go** (ADR 0043). Icons could be placed
  but a refresh or an Explorer restart undid it, so **Keep together**, **Make zones**, and
  **Name the zones** are dropped.
- Step 3 — **Clear old stuff** and **Folder by group** (move a folder). Planned.
- Step 4 — **Tag names** (rename a folder). Planned.
- Step 5 — color-icon probe, then **Color groups**. Planned.

## Explicitly Deferred Beyond V1 Unless Reprioritized

Cross-platform clients, team/cloud sync, hosted accounts, model training on user data, broad Windows control, widgets, rich desktop-shell replacement, marketplace-scale plugins, and automatic permanent deletion.

## Recommended Task Rhythm

For each feature: agree on behavior and threat cases → add/update Core model → write Safety/unit tests → implement adapter/use case → build UI → run focused/full tests → update docs → explain concepts and demo evidence. Keep commits small enough to review and undo.
