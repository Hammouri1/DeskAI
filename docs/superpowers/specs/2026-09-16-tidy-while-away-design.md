# V0.9 — Tidy while I'm away: design

Date: 2026-09-16. Owner's choice: "Move a few, then wait". Review written first:
`docs/security/2026-09-16-tidy-while-away-review.md`; decision: ADR 0031.

## What a person sees

**Organize**, on a folder that may be tidied: a switch **Tidy this folder while I'm away** under
the folder bar, with a line under it.

- Off, no rule on: the switch is disabled and the line reads "Turn on a rule in Automatic
  tasks first."
- Turning it on opens a dialog: "Tidy Downloads while you're away?" — "Whenever DeskAI checks
  your folders (every 15 minutes, also after the window is closed if you turned that on), it
  will move loose files in Downloads that these rules match, into folders inside Downloads:"
  the rules as sentences; then the promise on the accent rail: "At most 25 files each time. It
  stops and waits for you if a rule changes, a file is in the way, or a file can't be moved.
  It never deletes anything and never moves a file out of Downloads. You can undo every run
  here." Buttons: **Tidy while I'm away** (accent), Cancel; Enter, Esc, X cancel.
- On: the line reads "On since 14:05, for 2 rules. DeskAI moves at most 25 files each time it
  checks." The switch off turns it off at once, no dialog.
- Stopped by DeskAI: the switch is off and the line, in the caution colour, reads "DeskAI stopped
  tidying while you're away: A rule changed since you agreed. Turn it on again when you've
  looked." Turning it on records a fresh approval.

**Organize, the "While you were away" card**, above the suggestions when the folder has runs the
person has not seen: "While you were away, DeskAI tidied 12 files into 3 folders at 14:05."
(one line per run, newest first, at most five), **Undo** when the newest run is the folder's
last tidy, and **Got it**, which marks them seen. The line never names a file.

**The notice** (top right, over any page): "While you were away, DeskAI tidied 12 files in
Downloads. Nothing was deleted." with **Review in Organize**. The Windows notification, if on:
"DeskAI tidied 12 files. Open DeskAI to look." The tooltip is unchanged (counts only).

**Wording that changes with the mode**, everywhere it is on: Home's pill "Nothing moves by
itself" becomes "Moves files on its own in 1 folder you chose"; Home's promise sentence says
so; Automatic tasks' first card says "DeskAI moves a file on its own only in the 1 folder where
you turned on Tidy while I'm away, and only what your rules match."; the checking summary and
the keep-running dialog's limit line say the same; the help topics for checking and keep-running
say "unless you turned on Tidy while I'm away for a folder".

## Code

- `DeskAI.Core.Tidy.AwayTidyApproval` (RootId, Rules as `ApprovedRuleVersion`, ApprovedAtUtc,
  StoppedAtUtc?, StoppedReason?; `IsActive`; `Covers(rules)` → `RuleApprovalCheck`).
- `AwayTidyRun` (Id, RootId, TransactionId?, RanAtUtc, Moved, FoldersUsed, Skipped, StoppedReason?,
  SeenAtUtc?).
- `IAwayTidyRepository` (Core.Abstractions): Find/List/Save/Stop/Remove for approvals;
  Append/ListUnseen/MarkSeen for runs. `SqliteAwayTidyRepository`, schema 14:
  `away_tidy(root_id PK FK cascade, approved_at_utc, rules_json, stopped_at_utc, stopped_reason)`
  and `away_tidy_runs(run_id PK, root_id FK cascade, transaction_id, ran_at_utc, moved,
  folders_used, skipped, stopped_reason, seen_at_utc)`.
- `AwayTidyLimits.MaxFilesPerRun = 25`, `MaxRunsShown = 5`.
- `AwayTidyService(IAwayTidyRepository, IAuthorizedRootRepository, IRuleRepository,
  TidySuggestionService, TidyRunService, IClock)`: `TurnOnAsync`, `TurnOffAsync`,
  `GetStatusAsync`, `RunAllAsync` (called by the coordinator after a successful check),
  `ListUnseenAsync`, `MarkSeenAsync`, `CountActiveAsync`, and the wording helpers
  `PromiseFor(activeCount)`.
- `AutomaticCheckCoordinator` takes `AwayTidyService`, runs it after a check that ran, raises
  `Tidied(AwayTidySummary)` when anything moved; `ShellViewModel` shows the notice;
  `BackgroundPresenceController` unchanged.
- `TidyViewModel`: `CanTurnOnAway`, `IsAwayOn`, `AwayLine`, `AwayLineIsCaution`,
  `TurnAwayOnAsync` (after the dialog), `TurnAwayOffAsync`, `AwayRuns`, `HasAwayRuns`,
  `AwaySummaryLines`, `GotItCommand`; `DashboardViewModel` and `AutomationViewModel` read
  `CountActiveAsync` for the wording; `BackgroundCheckingChoice.Ask/MoreDetails` take the count.
- `FreshStartService`: rows go with the folders (cascade); nothing to add.

## Tests

`AwayTidyPageTests` (page): the switch is off and disabled without a rule; the dialog's yes
records the rules; a check then moves only rule-placed loose files, at most 25, into the
folder, and the away card, the notice, and the last-tidy undo appear; undo works after
reopening; a type-placed file, a subfolder file, and a clashing file are never moved and a
clash stops the run with the reason; a rule edit turns the mode off with the reason before the
next run; withdrawing permission ends it; a busy file stays and the mode stops after the run;
wording on Home and Automatic tasks follows the mode; Start fresh clears it.
`AwayTidyServiceTests` (Core, with the real temp folders through TestApp): the ceiling, the
filters, the approval check. Containment: `AutomaticCheckServiceTests` widened to name
`AwayTidyService` as the only type reachable from the coordinator that holds `TidyRunService`,
and `AwayTidyService` holds no AI, credential, reader, or fingerprinter.
