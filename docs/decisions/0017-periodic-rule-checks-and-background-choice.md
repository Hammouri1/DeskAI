# ADR 0017: Periodic Rule Checks and the Background Choice

- Status: Accepted
- Date: 2026-09-10

## Context

V0.5 asks for "folder watchers and/or scheduler selected through an ADR". That wording assumes the open question is *how* DeskAI notices a file. The more important question is what noticing is allowed to cause.

Nothing in DeskAI can currently move a file in a folder someone connected. ADR 0016 made rule evaluation return proposals rather than plan operations, and left no path from "a rule matched" to "a file moved". Real-folder execution is still refused: the executor works only inside the marker-protected temporary demo root. So a trigger in this milestone cannot mean "run the rules". It can only mean "notice there is something worth looking at, and say so".

`docs/SECURITY.md` requires a focused review before background watchers or schedulers ship. This ADR is the design that review examines.

## Decision

**A periodic check, not a folder watcher.** `FileSystemWatcher` was rejected. It holds an open handle on a real personal folder, it has a fixed internal buffer that drops events under load without telling the caller, it storms during a large copy or a cloud-sync pass, and it behaves inconsistently on OneDrive and network paths. Each of those is a case where DeskAI would silently know less than it appears to, and a screen that quietly stops noticing things is worse than one that says it checks every fifteen minutes. Rules read remembered metadata rather than the live disk, so instant reaction buys very little: the honest unit of work is "refresh what we remember, then re-check", which is periodic by nature. A watcher may be added later as an optimisation on top of this, never as a replacement for it.

**A check produces a review, never an execution.** The end of a check is a count and a notice. `PeriodicRuleCheckService` takes no executor and no planner, so there is no object graph in which it could carry a proposal out; a test asserts a full check performs no filesystem writes. Turning a proposal into a move stays where ADR 0016 put it — the ordinary planner, safety validation, preview, and approval.

**Whether DeskAI runs while closed is the person's choice, and the default is no.** Two modes: `WhileAppIsOpen`, where checks stop the moment the window closes and nothing is registered with Windows, and `InBackground`, where DeskAI keeps checking after the window is closed. The choice is asked plainly, is changeable at any time in the same place it was made, and defaults to `WhileAppIsOpen`. `InBackground` is a separate slice with its own security review, because a process that runs while nobody is present is a different threat case from one that does not.

**The mode changes what the app claims about itself.** The navigation pane currently states outright that DeskAI does nothing in the background. That sentence is true only in `WhileAppIsOpen`. It is derived from the stored mode rather than written as a fixed string, for the same reason the scope label was: the one label that promises what DeskAI does is the label that must never lie.

**Notifications are opt-in and default off.** A Windows notification is an interruption arriving without being asked for, so it is a switch someone turns on, not a default someone must discover and turn off. With it off — the shipped default — a check that finds something shows a quiet notice inside the app and nothing else.

**When to check is arithmetic, not a timer.** `AutomaticCheckSchedule.NextDueAt` is pure: last-checked, now, and an interval in, a due time out. The timer lives in Infrastructure and owns no policy. This keeps the decision testable with a fake clock, and keeps clock jumps, a machine waking from sleep, and a long-closed app answerable in unit tests instead of by waiting.

**Missed checks are caught up, never replayed.** A check has no per-occurrence meaning: it re-reads current state. An app closed for a week owes one check on the next launch, not a week of them. Overdue is therefore a boolean, not a queue.

**One check at a time.** Checks are single-flight and cancelled on shutdown. Overlapping passes over the same index would produce two counts for one state and race the notice.

**Pausing stops checks now.** The pause switch cancels a check already running rather than waiting for it to finish, because a person reaching for a stop control means the current activity too.

## Consequences

DeskAI notices a new file in minutes rather than seconds. For a feature whose entire output is "there is something to review", that is an acceptable trade for removing an event source that fails quietly.

The periodic check is dead weight until rules can actually do something in a connected folder, and this ADR does not change that. It is built now because run history, missed-run behaviour, and pause controls — the remaining V0.5 item — all need something that runs on a schedule to have a history of.

`InBackground` is decided here but not built here. Until its own slice and review land, choosing it must be impossible rather than merely discouraged, so the option is absent from the UI rather than present and inert.
