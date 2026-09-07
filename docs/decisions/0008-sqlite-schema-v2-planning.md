# ADR 0008: SQLite Schema Version 2 for Planning

- Status: Accepted
- Date: 2026-09-07

## Context

Step 6 requires durable schema boundaries for local settings, roots, plans, operations, and transaction headers while step 7 journaling is not yet implemented. Existing installations may already have schema version 1.

## Decision

Keep direct `Microsoft.Data.Sqlite` migrations. Apply version 1 idempotently, then version 2 in its own transaction. Represent plan identity with the composite key `(id, revision)`, make operations children of that exact revision, and bind execution-transaction headers to it with foreign keys. Add indexes for root/time and plan transaction lookups.

Do not add generic repositories or pretend transaction headers are a completed journal. Step 7 will introduce focused persistence adapters and per-operation before/after outcomes.

## Consequences

Fresh and version-1 databases converge on schema version 2. Foreign keys reject transactions or plans that reference missing parents. SQLite remains local state, never authority that live paths are safe, and stores no provider credential.
