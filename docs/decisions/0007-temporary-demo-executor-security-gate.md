# ADR 0007: Temporary Demo Executor Security Gate

- Status: Accepted
- Date: 2026-09-07

## Context

DeskAI needs to prove deterministic execution before receiving access to any user-selected folder. A normal configurable executor would create unnecessary risk during early development.

## Decision

Implement `TemporaryDemoPlanExecutor` as a deliberately non-production capability. It creates a unique owned directory only beneath Windows Temp, seeds known dummy files, and has no API for supplying another root. It requires a random ownership marker, rejects reparse points in existing path components, checks root containment and approval identity/revision/policy, reuses `PlanValidator`, executes only selected typed operations, requires selected parent-folder operations to have completed, and refuses every destination collision. Selection locks while an approval is prepared and executed.

The product does not automatically delete the demo. This avoids adding a product deletion capability; test fixtures clean only their own separately verified sandboxes.

## Consequences

The UI can demonstrate real create-folder and move behavior without personal data. Temporary dummy folders may remain until normal Windows/user temp cleanup. This executor is not authorized for step-8 real-folder use and should be replaced or generalized only after journaling, recovery, undo, and a separate security review.
