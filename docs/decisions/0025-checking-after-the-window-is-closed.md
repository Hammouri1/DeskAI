# ADR 0025: Checking After the Window Is Closed

- Status: Accepted
- Date: 2026-09-12
- Amended by: ADR 0047 (quick search also keeps DeskAI near the clock after the window closes)

## Context

ADR 0017 decided that whether DeskAI keeps checking after its window is closed is the
person's choice, and deliberately left the choice unbuildable: `AutomaticCheckMode.InBackground`
exists in the domain, no code produces it, and it is absent from the UI. `docs/SECURITY.md`
requires a focused review before it ships, because a process that runs while nobody is present
is a different threat case from one that stops when the window does.

This ADR is that design. The review it is examined by is
`docs/security/2026-09-12-background-checking-review.md`.

Nothing about what a check may do changes here. A check still refreshes remembered metadata,
evaluates rules in memory, and produces a count. It holds no executor, and
`AutomaticCheckServiceTests.Constructor_CannotReachAnythingThatChangesAFile` fails if one is
added. What this ADR decides is only *when* that may happen and *what is visible* while it
does.

## Decision

**The same DeskAI keeps running, hidden, with a visible icon near the clock.** Closing the
window in this mode cancels the close and hides the window; the host, and therefore the check
timer, are untouched. Reopening shows the same running state rather than a fresh start. The
two alternatives were rejected: destroying the window and keeping only the host ends the
process in WinUI once the last window goes, so it needs a surviving hidden window anyway and
buys nothing but a slower reopen; and a separate background process means two things to trust
and contradicts the promise that it is the same DeskAI still running.

**DeskAI never registers itself with Windows to start on its own.** No Run key, no Startup
folder, no scheduled task, no `StartupTask`. After a restart or a sign-out, DeskAI runs again
only when someone opens it. This is stated in the UI, so it is enforced by a test that scans
the solution for those APIs rather than left to intent.

**The icon appears when the setting is turned on, not when the window is closed.** Turning the
setting on is the moment something changed, so that is the moment the evidence appears — while
the person is still looking at the switch that caused it. An icon that arrives after the
window closes arrives exactly when attention has moved elsewhere.

**The hidden window is an ordinary top-level window that is never shown, not `HWND_MESSAGE`.**
`Shell_NotifyIcon` needs a window handle for its callback, and when Explorer restarts Windows
broadcasts `TaskbarCreated` so applications can add their icon back. Broadcasts do not reach
message-only windows. A message-only window would therefore produce a DeskAI that is running,
checking, and invisible after an Explorer crash, which is the worst outcome this feature has.

**The menu stops things and opens things. It starts nothing.** Three items: open DeskAI, pause
checking, quit DeskAI. Starting a check from the menu was considered and rejected: a check is
harmless in what it may do, but it would mean folder metadata being read with no window on
screen to say so and no page reporting the result, which is the precise shape this review
exists to be careful about. Every control that begins something stays on a page someone opened.

Pause is allowed there because stopping is always safe and is the one control a person may
want in a hurry. It writes through the same repository as the page, shows a checkmark for the
true current state, and cancels a check already running, as the page's switch does.

**A second launch reveals the first and exits.** A `Local\`-scoped named mutex — per sign-in
session, so another Windows user gets their own DeskAI — decides which instance is the real
one. A second launch finds the first one's hidden window and posts a single registered message
that means "show yourself", then exits successfully with no dialog. The reason is not
tidiness: two instances would mean two SQLite writers and two timers producing two counts for
one state. The message carries no payload, so the most another program can do with that channel
is make a window appear.

**The tray tooltip is held to the same rule as a notification.** A count and a state, never a
file name, folder name, or path. A tooltip appears on hover to whoever is at the machine, which
is not somewhere a person chose to show anyone their filenames. Like the scope label and the
check summary, it is derived from the stored settings rather than written as a fixed string:
the one label that promises what DeskAI is doing must not be able to lie.

**A notification reveals the window when clicked.** With the window hidden, a notification that
does nothing when clicked is a dead end.

**The honest limit is stated in the dialog, not only in the documentation.** A check produces a
count. It cannot move, rename, or delete anything. Leaving DeskAI running therefore keeps that
number up to date; it does not tidy while someone is away. Tidying unattended is V0.9 and is
not started.

## Consequences

DeskAI gains its first state in which it is running with nothing on screen. Every promise the
app makes about that state is derived from stored settings and asserted by a page test, because
a promise about invisible behaviour is the one a person cannot check for themselves.

The mode remains off by default, and turning it on is a dialog rather than a switch, because it
changes what closing the window means.

`DeskAI.App` gains the first code in the project that cannot be unit-tested: the tray adapter's
P/Invoke and window procedure. It is therefore kept to the part that genuinely cannot be —
window, icon, menu, and four events. Every decision it might have made stays in Core and
Presentation behind `IBackgroundPresence`, which page tests fake exactly as they fake
`IFindingNotifier`.

## Amended 2026-09-16: the words say where the icon really is

The owner closed DeskAI, looked near the clock, opened the hidden-icons arrow, and found
nothing. Nothing was broken — the switch was off, so no icon had ever been registered — but two
things about the wording made that indistinguishable from a fault, and both are now fixed.

**Nothing on screen connected the switch to the icon.** The switch reads "Keep checking after I
close the window"; its caption talked about startup and asking first, never about the icon. The
icon therefore looked like something DeskAI should always have. The caption now leads with the
fact that turning the switch on is what puts DeskAI near the clock.

**"Near the clock" is not where Windows 11 puts a new icon.** It goes into the hidden-icons
flyout behind the arrow until someone drags it out. Three places said "near the clock" and sent
a person to a place the icon is not. One sentence, `BackgroundCheckingChoice.WhereToLook`, is
now appended to every one of them — the dialog, the switch caption, the still-running notice,
and the help topic — so a fourth place cannot be written without it.

The still-running notice moved from a literal in `MainWindow` into
`BackgroundCheckingChoice.WhereItWent`, for the reason the rest of this feature's words already
live there: a window cannot be built in a test, so words typed into it cannot be asserted.
