# Security Review — Automatic Rule Checks

- Date: 2026-09-10
- Scope: the periodic check introduced by ADR 0017, in `WhileAppIsOpen` mode only.
- Required by: `docs/SECURITY.md` — "a focused review is required before introducing …
  background watchers/schedulers".

## What was added

A timer that, while DeskAI is running, periodically refreshes the metadata index for
connected folders, runs the existing rule practice run over it, and shows a count. Settings
for how often, a pause switch, an opt-in Windows notification, and a "Check now" button.

Background running after the window closes is **not** part of this change. The mode exists in
the domain and is decided in ADR 0017; no code produces it, and it is absent from the UI.

## Threat cases considered

**A check moves a file nobody approved.** The strongest control is structural rather than
procedural: `AutomaticCheckService` has no executor, planner, scanner, or content extractor
in its constructor, so no object graph reachable from a check can mutate the filesystem.
`AutomaticCheckServiceTests.Constructor_CannotReachAnythingThatChangesAFile` asserts this by
reflection and fails if a future change adds one. Rule evaluation already returns proposals
rather than plan operations (ADR 0016), and real-folder execution remains refused.

**A check widens scope.** Folders come from `FileSearchService.IsSearchable`, the same
predicate search, the storage summary, and the practice run use. A check therefore cannot
reach a folder search would not look in, including the generated practice workspace.
Asserted by `RunAsync_IgnoresFoldersSearchWouldNotLookIn`.

**A check reads inside files.** It does not. Refreshing is metadata-only through the existing
bounded scan, and rules test names, endings, categories, sizes, and dates. A folder that was
granted content permission is treated identically. Asserted by `RunAsync_NeverReadsInsideAFile`.

**A notification leaks file names.** Notifications carry a count and nothing else. Names,
folders, and paths never enter one, because a notification is shown on a lock screen and in a
notification centre — places the person did not choose to display their filenames.

**Pausing does not actually stop anything.** `IsPaused` blocks the schedule
(`AutomaticCheckSchedule` returns no due time) and cancels a check already in flight through
`AutomaticCheckCoordinator.StopRunningCheck`. Stopping mid-check is safe precisely because a
check changes nothing.

**Overlapping checks.** Single-flight through a coordinator; a second request while one runs
is dropped, not queued. Asserted by `Coordinator_RunsOneCheckAtATime`.

**A missed period causes a burst of catch-up work.** It cannot: being overdue is a boolean,
not a queue. An app closed for a week owes one check. Asserted by
`IsDue_OwesOneCheckAfterALongClosure`.

**A wrong clock stalls or storms checking.** A last-checked moment in the future — a clock
corrected backwards — is treated as "check now" rather than leaving DeskAI waiting for the
clock to catch up. Asserted by `IsDue_ChecksWhenTheLastCheckIsInTheFuture`.

**A corrupted or future-written settings row grants something.** Unrecognised stored values
fall back to the narrow default rather than being cast into the enum, so a bad row cannot
produce an unhandled mode. Asserted by
`LoadAsync_FallsBackToTheNarrowDefaultForAnUnknownStoredValue`.

**A repeated failure disables checking silently.** One failed check is logged and the next
tick tries again; a folder that has genuinely gone away is simply skipped.

## User-facing disclosure

The Automatic tasks page previously stated that nothing ran on its own. That became false and
was rewritten: DeskAI now says it may look by itself, and that looking is all it can do. The
"More details" section states that checking happens only while the app is open, that DeskAI
does not add itself to Windows startup, and that a check reads names, sizes, and dates rather
than opening files. Every wording of the summary sentence ends by saying nothing is moved.

## Rollback and recovery

A check produces no filesystem change, so there is nothing to roll back and no journal entry
to recover. Stopping DeskAI stops all checking. Setting the frequency to "Only when I ask", or
pausing, stops it without closing the app.

## Not accepted by this review

Running after the window is closed, any form of tray or startup registration, `FileSystemWatcher`,
and any path from a check to an executed file operation. Each needs its own review.
