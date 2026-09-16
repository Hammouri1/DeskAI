# Security review: Tidy while I'm away (V0.9) — 2026-09-16

This review is a precondition for V0.9, not a step in it (`ROADMAP.md`). Every control DeskAI
has today assumes a person is looking at a preview at the moment a file moves. V0.9 is the
first thing that acts on its own, so each control is re-argued here without that assumption,
and the ceiling on what may happen unattended is decided before any code.

The owner's choice (2026-09-16, asked in plain words): **"Move a few, then wait"** — only files
matched by rules they switched on, only inside folders where they allowed tidying and turned
this on, at most 25 files per run, never a delete; anything unexpected stops the run; undo is
always offered when they come back.

## 1. What "standing approval" means here

Today an approval is bound to one plan revision and the exact operations on screen (ADR 0016
for rules; `Approval` for a tidy). A standing approval cannot name files that do not exist
yet, so it is an approval of an **outcome class**, not a list:

> "Move a loose file at the top of *this folder* into the folder inside it that *one of these
> rules, exactly as worded now*, names — and nothing else."

Recorded as `AwayTidyApproval(RootId, ApprovedRuleVersions, ApprovedAtUtc)`: the folder and
every enabled rule at its version at the moment of the yes, the same shape ADR 0016 already
uses. It is stored in its own table (`away_tidy`), cascade-erased when the folder is
disconnected, never written by saving a folder's scope, and granted only through a dialog on
Organize that names the folder and states the ceiling.

## 2. What invalidates it

Any of these switches it off before a file moves, and the folder shows why:

| Change | Detected by | Result |
| --- | --- | --- |
| A rule edited, added, removed, or turned off/on | `AwayTidyApproval.Covers(rules)` — the same version and set comparison as `RuleApproval.Covers`, minus the outcome fingerprint (an outcome that changes with new files is the point of V0.9) | Off, "A rule changed since you agreed." |
| Tidy permission withdrawn, folder disconnected, folder moved or unsafe | `RootCapabilities.CanTidy` and `CheckStillSafeAsync` (the executor's live checks), and the cascade on disconnect | Off, or gone with the folder |
| A same-name clash for a file a rule would move | `TidySuggestion.HasSameName` in the preview | The run stops before moving anything: "A file called X is already in Documents; DeskAI needs you to decide." Never "keep both" unattended, because a numbered copy is a choice. |
| A file DeskAI cannot recognise as safe (changed since the list, busy, link, hidden, online-only, protected) | The executor's per-file re-check, unchanged | That file stays; the run's other files move; then the switch turns off: "2 files couldn't be moved. DeskAI stopped tidying while you're away until you look." |
| The scan did not complete, or the folder cannot be looked at | `TidyPreview.ScanWasIncomplete` / `FolderProblem` | Nothing moves; off with the reason |
| An unfinished record in the folder (an interrupted tidy) | The executor refuses a new tidy there | Nothing moves; off, and Organize shows the interrupted card as today |
| DeskAI is quit, paused, or checks are set to "only when I ask" | No check runs, so no away tidy runs | Nothing; the switch stays as it was |

"Anything novel" is defined narrowly and structurally: **only suggestions whose source is a
rule** are ever moved unattended. A file placed by type, and any AI idea, is never moved
without a person: `AwayTidyService` filters on `TidySuggestionSource.Rule` and never asks
`TidyAiService` (a test asserts it holds no AI type).

## 3. The unattended ceiling

- **Which actions:** move a file, and make the destination folder if missing. The same two
  executor commands a tidy uses; nothing new is added to the executor.
- **How many:** at most **25 files per run** (`AwayTidyLimits.MaxFilesPerRun`). A run happens
  at most once per automatic check, so the most that can move in an hour is bounded by the
  check frequency times 25.
- **Which folders:** only a connected folder that may be tidied *and* has the switch on. Never
  a subfolder's files, never anything out of the folder: the preview only ever offers loose
  top-level files and destinations inside the folder.
- **Which files:** loose files a switched-on rule places, that are not still downloading, not
  changed in the last two minutes, not hidden, system, online-only, or a link, and have no
  same-name clash. The same left-alone rules as a hand tidy.
- **What stops it:** everything in section 2. A stop is per folder and persists (the row keeps
  a `stopped_reason`) until the person turns it on again from Organize, which records a fresh
  approval of the rules as they are then.
- **Never:** a delete, a rename, a move out of the folder, a numbered copy, an AI idea, a file
  placed by type, a folder without the switch.

## 4. Each control, re-argued with nobody watching

| Control | With a person (today) | Unattended (V0.9) |
| --- | --- | --- |
| Preview | The person reads the list and unticks | There is no reader, so the list is narrowed until reading is unnecessary: rule-placed only, no clashes, ≤25, and every rule was read and approved as worded. What the person "sees" is the ceiling and the rules, stated in the dialog. |
| Collision | Skip by default, or Keep both by choice | Any clash stops the run before anything moves. Nothing is ever overwritten: `overwrite: false` in the executor is unchanged. |
| Reparse points | Refused per path at execution | Unchanged; a refusal also stops the mode so it is noticed. |
| Time-of-check/time-of-use | Each file re-checked against the size and date the person saw | The preview and the run are seconds apart in the same call, and the executor still compares each file to the preview's size and date and refuses a changed one. The "recently changed" two-minute rule keeps files still being written out of the run. |
| Approval binding | Plan revision + operation IDs | The run still builds an ordinary `Approval` over the exact operations it will execute, so the executor sees nothing new; the standing approval only decides *whether* a run may be built. |
| Journal | Every run journaled | Unchanged: the run goes through `TidyRunService.TidyAsync`, so it is journaled and recoverable like any tidy. Additionally an `away_tidy_runs` row records the run as unattended so it can be shown as such. |
| Undo | Offered on the result card | **Primary.** The run is the folder's last tidy, so Organize's "Last tidy… Undo" finds it after any restart; the "While you were away" card sits above it with its own Undo, the notice in the window says how many files moved and offers Review, and the Windows notification (if on) carries the count. Undo needs the tidy permission, as always. |
| The cost of a wrong move noticed hours later | A person notices at once | Bounded by the ceiling (25 per run, rule-placed, inside the folder) and reversible by the same undo as any tidy, which refuses a file changed since it moved rather than guessing. |
| Executor reach | One executor, `FolderTidyExecutor` | Unchanged. `AutomaticCheckService` still holds no executor; `AwayTidyService` holds `TidyRunService` and is the one type reachable from the coordinator that can move a file. A test names it as the only such type. |
| Windows startup | Never | Unchanged: nothing registers with Windows; the mode runs only while DeskAI runs. |
| No window on screen | Tooltip carries a count | Unchanged: the tooltip and notification carry counts only, never a file or folder name. |

## 5. Negative tests required before the feature is done

- Away tidy never moves a type-placed file, an AI-placed file, a file with a clash, a file in a
  subfolder, or more than 25 files in one run.
- A rule edit, add, remove, or toggle after the yes turns the mode off before the next run, and
  the folder says so.
- Withdrawing tidy permission, or disconnecting, ends the mode; a fresh yes is needed after
  turning permission back on.
- A file changed between the preview and the move stays, and the mode turns off with the reason.
- A run appears as the last tidy and can be undone after reopening; the away card names the run.
- The check service and coordinator still hold no executor or journal; `AwayTidyService` is the
  only unattended path and holds no AI, scanner-of-contents, reader, or credential.
- With the mode on in one folder, Home, Automatic tasks, the keep-running dialog, and the help
  text no longer say nothing moves by itself; with it off everywhere they still do.
- Start fresh removes every away-tidy row.

## 6. Disclosures

- The Organize dialog before the yes names the folder, the rules as worded, the 25-file ceiling,
  what stops it, that it never deletes, and that undo is always there.
- Every page that promised "nothing moves by itself" states the narrower truth while any folder
  has the mode on: "DeskAI moves a file on its own only in the N folders where you turned on
  Tidy while I'm away, and only what your rules match."
- The notice and notification after a run: "While you were away, DeskAI tidied 12 files in
  Downloads. Nothing was deleted." — a count and a folder *name* in the window; a count only
  in the notification and the tooltip.

## 7. Recovery

An away run that stops part-way is an interrupted tidy like any other: the next time the folder
is shown, Organize asks "Undo those N / Keep them", and nothing runs in the folder until it is
answered. The away mode turns off on any refusal so a person is asked before the next run.
