# Security Review — Checking After the Window Is Closed

- Date: 2026-09-12
- Scope: `AutomaticCheckMode.InBackground` and the notification-area presence it needs,
  designed in ADR 0025.
- Required by: `docs/SECURITY.md` — a focused review before background watchers/schedulers,
  and before desktop-shell surfaces. Explicitly listed as "not accepted" by
  `docs/security/2026-09-10-automatic-check-review.md`, which reviewed the same feature in
  `WhileAppIsOpen` mode only.
- Status: design review, written before implementation. Where a control is already asserted by
  a test, that test is named; where it is not yet, the control describes what must assert it.
  The review is not satisfied until every one of those tests exists and passes.

## What is being added

A person may choose to have DeskAI keep running, with no window, after they close it. It shows
an icon near the clock, keeps running the periodic check already reviewed on 2026-09-10, and
ends when they quit it, sign out, or restart.

What a check may do does not change. This review is about a new *state* — DeskAI running with
nothing on screen — and about a new *surface*, the notification area.

## Threat cases considered

**A check moves a file while nobody is present.** The control is structural and already exists:
`AutomaticCheckService` takes no executor, planner, scanner, or content extractor, so no object
graph reachable from a check can mutate the filesystem, and a reflection test fails if one is
added. Background mode changes when a check runs, never what it may do. The same reflection
test is extended to the background lifetime controller and the tray adapter, so neither can
acquire one either.

**AI reaches something while nobody is watching.** It cannot. Nothing on the background path
holds an AI provider or the credential store; DeskAI's AI is reachable only from a page a
person opened, after a dialog that shows what would be sent. A hidden DeskAI has no code path
that sends anything anywhere. Asserted by the same constructor-graph test.

**Background checking widens scope.** It does not. Folders still come from
`FileSearchService.IsSearchable`, the predicate search and the storage summary use, so a check
with the window closed cannot look anywhere the search box could not look with it open. Already
asserted by `RunAsync_IgnoresFoldersSearchWouldNotLookIn`; unchanged by this slice.

**DeskAI silently survives a restart or sign-out.** It does not. No Run key, no Startup folder,
no scheduled task, no `StartupTask`. Enforced by a solution-wide test that scans for those APIs
rather than by intent, because this is a promise the UI makes in words. On session end the icon
is removed and the host is stopped.

**DeskAI keeps running when the person meant to close it.** The mode is off by default. Turning
it on is a dialog, not a switch, because it changes what closing the window means. The icon
appears at the moment the setting is turned on — while they are looking at the switch — rather
than at the moment they close the window and look away. The first hidden close shows a one-time
notice saying where DeskAI went and how to quit it. With the mode off, the close button still
really exits.

**DeskAI is running but invisible.** The failure mode is an Explorer restart: Windows announces
it by broadcasting `TaskbarCreated`, and broadcasts do not reach message-only windows. The
hidden window is therefore an ordinary top-level window that is never shown, and the icon is
re-added when that broadcast arrives. A crash leaves no live process, so it leaves no live
checking; a stale icon from a killed process disappears on hover, which is Windows' own
behaviour and not something DeskAI can pre-empt.

**Two DeskAIs.** A `Local\`-scoped named mutex — per sign-in session, so a second Windows user
gets their own — decides which instance is real. A second launch reveals the first and exits
successfully. This is a data-integrity control, not a convenience: two instances would mean two
SQLite writers against one database and two timers producing two counts for one state.

**The reveal channel becomes a way to drive DeskAI.** The hidden window's procedure accepts
exactly two messages: the registered "show yourself" message and the tray callback. The show
message carries no payload — no path, no command, no argument. The most another program on the
machine can achieve through it is causing a window to appear.

**Something leaks with no screen present.** Notifications already carry a count and never a
name, because they appear on a lock screen and in a notification centre. The tray tooltip is
held to the identical rule for the identical reason: it is shown on hover to whoever is at the
machine. A count and a state; never a file name, folder name, or path.

**The icon claims something untrue.** The tooltip is derived from the stored settings, so
paused reads as paused and "only when I ask" reads as itself. A fixed string would eventually
promise activity that is switched off, which is the failure the scope label in the navigation
pane was rewritten to prevent.

**A control with no page around it to explain it.** The menu holds three items: open, pause,
quit. Starting a check from the menu was considered and rejected — a check is harmless in what
it may do, but it would read folder metadata with no window on screen and no page reporting the
result. Nothing in the menu begins work. Pause is allowed because stopping is always safe, it
is the control someone may want in a hurry, and it writes through the same repository the page
uses, so the two cannot disagree; the page re-reads its settings when it opens.

**A notification that leads nowhere.** With the window hidden, clicking a notification reveals
it. A notification about a finding that cannot be acted on is a dead end, not a feature.

**A longer-lived database handle.** The same single-writer SQLite connection is held for longer
than before — hours or days rather than one session at the desk. Accepted and recorded rather
than mitigated: it is the same handle with the same access, and the mutex prevents a second
writer.

## User-facing disclosure

The Automatic tasks page currently states in "More details" that checking happens only while
DeskAI is open. That sentence becomes false in this mode, so it is derived from the stored mode
rather than written fixed — the same treatment the summary sentence and the scope label already
have. The sentence that DeskAI does not add itself to Windows startup stays in both modes,
because it is true in both.

The dialog that turns the mode on says what will happen, carries the notification switch so
both decisions are made in one place, says plainly that with notifications off a find is only
seen on reopening, and states the honest limit: a check can tell you a number, it cannot move,
rename, or delete anything, so leaving DeskAI running keeps that number current rather than
tidying while you are away.

## Rollback and recovery

A check produces no filesystem change, so there is nothing to roll back and no journal entry to
recover. Quitting from the menu ends everything. Turning the setting off returns DeskAI to
stopping when its window closes and removes the icon at once. Signing out or restarting ends it
and does not bring it back. Pausing stops checking without closing anything, and cancels a check
already under way.

## Not accepted by this review

Carrying out a rule, moving a file, or tidying anything while nobody is present — that is V0.9
and needs its own design and its own review, in which every control that assumes a person is
watching a preview has to be re-argued without that assumption. Also not accepted: any startup
registration, `FileSystemWatcher`, AI on the background path, and any control in the tray menu
that begins work rather than stopping it.
