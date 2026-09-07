# Testing Strategy

## Objectives

Tests must prove both useful behavior and refusal of unsafe behavior. The highest-risk failures are unauthorized scope expansion, path escape, silent overwrite, stale approval, incorrect recovery, secret disclosure, and claims of success after partial execution.

## Test Layers

### Unit tests

Fast, deterministic tests for Core planning/rules and pure Safety policies. Use tables/theories for path and operation cases. No real provider, database, clock, or personal filesystem.

### Component/integration tests

Exercise SQLite repositories/migrations, scanner adapters, and eventually the executor against a unique generated temporary directory. Use fake AI/network and fake credential storage unless a specifically isolated platform contract test is intended.

### UI tests

Test view-model state/commands without UI where possible. Add a small number of Windows UI automation tests for navigation, folder permission communication, plan review, warning/blocked states, keyboard access, approval, partial failure, and undo. Keep selectors stable and accessibility-driven.

### End-to-end safety scenarios

On a controlled test machine/root only: scan → plan → validate → preview → approve → execute → journal → undo. Capture exact before/after trees and verify no path outside the sandbox changed.

## Filesystem Sandbox Rules

Every mutation test must:

1. create a uniquely named directory through the test framework/system temporary facility;
2. resolve and verify its absolute canonical path is under the intended temporary root;
3. create only dummy files with non-sensitive content;
4. inject that root instead of discovering Desktop/Downloads/Documents/Pictures;
5. record a sentinel outside the sandbox where safe and verify it remains unchanged;
6. clean only the exact owned directory in `finally`/fixture disposal after re-verifying the target;
7. preserve the sandbox and print its safe path when debugging a failure if configured.

Tests must never enumerate or mutate personal known folders. Do not depend on developer usernames, drive letters, locale, clock, network, or installed cloud-sync clients.

Native picker behavior is verified manually only with a newly generated Windows Temp folder containing dummy data. Automated read-only authorization tests use the same owned-temp helper, lock a dummy file to prove contents are not opened, verify protected-root refusal and scan bounds, and confirm revocation changes only persisted permission.

## Required Safety Matrix

- Allowed source and destination inside the same authorized root.
- Source or destination outside root.
- Prefix confusion (`Root` versus `RootOther`) and `..` traversal.
- Mixed separators, trailing separators/dots/spaces, relative paths, case differences, reserved names, long paths, device/UNC/ADS forms according to support policy.
- File/directory mismatch, missing source, locked/access-denied source, read-only item.
- Existing destination and case-only rename collision.
- Protected system or user-configured item.
- Reparse point/symlink/junction in source, destination, or intermediate component.
- Plan changed after approval; policy/root authorization revoked; source metadata changed before execution.
- Duplicate/reordered operation IDs and operations whose combined effects conflict.
- Cancellation before/during scan and between execution operations.
- Partial execution, restart/recovery, repeated execution attempt, and journal write failure.
- Undo when destination changed, old path is occupied, created folder is nonempty, or only part of a plan can reverse.
- No permanent-delete command exists; blocked operation types remain blocked.

Platform-specific cases may require Windows and privileges. Skip only with an explicit reason and cover policy logic with a platform-neutral fake as well.

## AI Contract Tests

Cover valid structured responses plus malformed/truncated/oversized JSON, unknown schema/enums, invented or duplicate file IDs, absolute/escaping paths, raw commands, prompt injection in names/content, misleading confidence, timeouts, cancellation, authentication/rate limits, and a provider attempting to return more operations than allowed. Verify disclosure filtering before the fake transport and verify logs contain no secret or payload.

## Database Tests

Test fresh schema, every supported migration path, foreign keys, transaction rollback, concurrent access policy, enum/version compatibility, retention deletion, interrupted execution records, and that credentials are never stored in tables. Each test uses an isolated database.

## Quality Gates

For each milestone:

- solution builds with no unexplained warnings;
- focused and full relevant tests pass;
- new safety branch has positive and negative coverage;
- no test touches real personal data;
- manual exploratory checklist covers new user-visible workflow;
- accessibility checks cover keyboard/focus/name/contrast for affected UI;
- documentation and status match actual behavior.

Do not optimize for a coverage percentage alone. Branches that authorize or mutate deserve exhaustive examples and, later, property-based/fuzz testing. Track performance separately with representative synthetic trees so benchmarks do not make normal tests flaky.

## Manual Release Checklist

- Clean install and first launch with no account/network.
- Rule-only organizer demo in a controlled folder.
- Permissions and protected items are understandable.
- Cloud remains off until configured; provider/disclosure indicator is accurate.
- Preview matches exact execution; collision and failure UX are honest.
- History and undo work after restart.
- Logs/database contain no API key or unintended content.
- Update/uninstall preserves or removes local data according to stated user choice.
