# ADR 0006: Preview-Only UI and Dependency-Injected Navigation

- Status: Accepted
- Date: 2026-09-07

## Context

V0.2 needs a real preview before DeskAI introduces any file mutation. The preview must exercise Core planning and Safety validation while guaranteeing that no personal or test folder is read. The original WinUI navigation created page types directly, which prevented constructor injection into `OrganizePage`.

## Decision

Register WinUI pages and view models with the application dependency-injection container. `NavigationService` resolves a page for a known allow-listed route and assigns it to the shell frame. `OrganizePage` receives `OrganizeViewModel` by constructor injection.

Use an App-layer `DemoOrganizationPlanFactory` to create fixed `FileItem` metadata and a synthetic root label entirely in memory. Pass those records through the real classifier, planner, and Safety validator. Render operations and typed issues, allow selection only for non-blocked operations, expose a plan-revision command, and keep execution visibly disabled with no command or executor attached.

## Alternatives Considered

- Add a folder picker now: rejected because user-root authorization and real-folder scanning belong after sandbox demonstrations.
- Hand-build fake preview rows: rejected because they would not prove that the real planner and Safety results can drive the UI.
- Resolve services globally from page code-behind: rejected because it hides dependencies and acts as a service locator.
- Add a temporary executor behind a disabled button: rejected because mutation requires the next dedicated security gate.

## Consequences

The owner can inspect selection, conflicts, explanations, and revision behavior without exposing any personal path. Navigation remains restricted to a fixed route table while pages can use constructor injection. The demonstration root is a label, not an authorization record persisted or used for I/O. UI selection is deliberately not approval, and no selected operation can execute.
