# ADR 0003: Read-Only Metadata Scanner Boundary

- Status: Accepted
- Date: 2026-09-07

## Context

V0.2 begins with filesystem discovery, which introduces the first read-only contact with an authorized root. The scanner must remain responsive, avoid loading an entire tree, refuse links that could escape scope, and report ordinary per-item failures without disclosing absolute paths.

## Decision

`IFileScanner` streams a closed `ScanEvent` hierarchy. Successful events contain a `FileItem` with a stable path-derived ID, normalized root-relative path, size, and timestamps. Issue events contain only a root-relative path, machine-readable code, and sanitized explanation. Callers supply validated `MetadataScanOptions` with maximum depth and total entries.

The Windows adapter uses iterative, non-recursive enumeration. It checks every component of the authorized root for reparse points and skips reparse entries without following them. Protected paths are evaluated before metadata is collected. File contents are never opened. Since .NET exposes synchronous directory enumeration, the adapter yields control between directories and exposes an asynchronous cancellable stream without hiding enumeration inside speculative `Task.Run` work.

## Alternatives

- Returning one materialized list was rejected because memory use and first-result latency grow with the tree.
- Recursive enumeration was rejected because it makes per-directory error handling, depth limits, and link refusal harder to control.
- Reading a small content prefix was rejected because content extraction has a separate permission and security gate.
- Silently ignoring errors was rejected because the UI must distinguish complete and partial scans.

## Consequences

Consumers must handle both discovered files and issues and must pass scan limits. The scanner can represent partial results honestly and cancellation is cooperative. Individual Windows enumeration calls can still block briefly because the platform API is synchronous; performance measurements will determine whether a dedicated scheduling strategy is justified later.
