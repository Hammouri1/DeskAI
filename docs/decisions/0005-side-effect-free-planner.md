# ADR 0005: Side-Effect-Free Organization Planner

- Status: Accepted
- Date: 2026-09-07

## Context

Classification says what a file appears to be, and a recipe says where that category is preferred. DeskAI needs to combine those facts into a complete reviewable proposal without inspecting or changing the filesystem. Conflicts must remain explicit and must prevent approval.

## Decision

`IOrganizationPlanner` belongs in Core and accepts an `OrganizationPlanningRequest` containing plan identity/revision, root identity, policy version, creation time, selected recipe, and classified files. `OrganizationPlanner` creates deterministic `CreateDirectoryOperation` and `MoveFileOperation` records with stable IDs derived from the plan and operation facts.

The planner records typed informational and conflict issues. It detects duplicate destinations case-insensitively, destinations occupied by other scanned files, and required directory paths occupied by files. Files that are unknown, have no recipe mapping, or are already organized receive informational issues rather than speculative operations. `PlanValidator` turns any conflict issue into a blocked collision result.

## Alternatives

- Performing filesystem checks during planning was rejected because planning must remain pure and cached metadata can be stale.
- Silently suffixing duplicate names was rejected because collision resolution must be explicit and deterministic.
- Omitting conflicting operations was rejected because preview needs to explain what could not be proposed safely.
- Random operation IDs were rejected because stable IDs improve review, retry detection, and later journal correlation.

## Consequences

Plans are inspectable data and can be reproduced from the same request. A planner conflict cannot pass Safety approval. Existing live filesystem state must still be revalidated by a later Safety/executor stage immediately before mutation. The current implementation remains incapable of executing its proposals.
