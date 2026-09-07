# ADR 0009: Write-Ahead Journal and Validated Demo Undo

- Status: Accepted
- Date: 2026-09-07

## Context

Filesystem operations are not a database transaction. A process can stop after a file moves but before the UI receives success, and blindly reversing a stale path can overwrite later user changes.

## Decision

Persist the authorized root, exact immutable plan revision, execution header, and Pending operation intents before mutation. Mark each operation InProgress immediately before its filesystem call and record its exact outcome afterward. Record source size and modification time for move/rename operations.

Undo is another journaled transaction. Process completed operations in reverse order, rechecking the marker, root containment, link status, destination fingerprint, and original-path availability. Refuse changed files. Remove only empty directories that the original transaction recorded as created, using non-recursive deletion.

An incomplete operation may be reconciled against live state only while the current executor owns the same temporary root and marker. After restart, an older root cannot be authenticated with the new in-memory marker token, so its transaction remains RecoveryRequired for manual review. Cross-restart undo is deferred rather than weakening root ownership.

## Consequences

Journal creation failure prevents all mutation. Partial outcomes are explicit, recent activity survives restart, and safe same-session undo is demonstrable. This is not yet production recovery: durable protected workspace identity and user-root execution remain unresolved and require separate designs.
