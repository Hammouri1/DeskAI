# ADR 0018: A Testable Presentation Layer and Page Tests

- Status: Accepted
- Date: 2026-09-10

## Context

Every view model lived inside `DeskAI.App`, a WinUI executable that a test project cannot
load. The engine had 545 tests, but no test ever pressed a button on a page, and none checked
that the app registered its services the way the pages needed them. Every bug the owner found
by hand was in that untested layer: a rules list that said "No rules yet" above a saved rule,
a Save answer shown far from the Save button, a folder pick that failed silently, and a draft
that kept part of the previous sentence. `TESTING.md` already asked for view-model tests
without UI; the project layout made them impossible.

## Decision

Move the view models, the sample-plan factory, and the `IFindingNotifier` contract into a new
`DeskAI.Presentation` class library that references no WinUI type. `ShellViewModel` captures
`SynchronizationContext.Current` instead of a WinUI `DispatcherQueue`; on the UI thread that
context is the dispatcher's, so behaviour is unchanged.

Move every non-window service registration into `AddDeskAiApplication`. The app calls it and
adds only the Windows-facing pieces: navigation, the folder picker, notifications, pages, and
the window. `DeskAI.Presentation.Tests` calls the same method, so a test builds DeskAI the way
the app does, and a missing or wrong registration fails a test. The container is built with
`ValidateOnBuild`.

Page tests replace exactly three things: the credential store (an in-memory vault, so no key
is written to Windows), the network (a recording transport, so no request leaves the
machine), and notifications. Everything else — SQLite, the scanner, the safety checks, the
planner, the demo executor — is real and runs inside a generated temp folder.

Namespaces stay `DeskAI.App.*`, so the XAML and code-behind did not change.

## Consequences

Each page can now be tested as a person uses it: fill in a form, press the command, read
what the page says. Confirmation dialogs remain in code-behind and are still checked
manually, because they are WinUI objects; the view-model method each dialog calls is tested.
Nothing about what DeskAI may read, change, or send is affected by this move.
