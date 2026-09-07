# ADR 0010: Metadata-Only Folder Authorization

- Status: Accepted
- Date: 2026-09-07

## Context

A native folder picker proves user intent to select a location, but selection alone does not say whether DeskAI may only display metadata or may change files. Reusing one undifferentiated “allowed” root would make a later wiring mistake dangerous.

## Decision

Persist a closed `RootAuthorizationScope` with every authorized root. A Step 8 picker grant is always `MetadataOnly`; the controlled sample executor uses `ControlledDemo`. The picker is followed by an explicit dialog naming the exact metadata read. The service rejects drive roots, network/device forms, links in the selected root chain, and roots overlapping permanent protected locations before saving permission.

The metadata scanner remains bounded, cancellable, and content-free. `PlanValidator` blocks every mutation plan bound to `MetadataOnly`, independently of the UI. Revocation deletes only the permission record. No real-folder executor is introduced.

## Consequences

Users can safely try a real native selection flow and inspect a small metadata preview without granting organization authority. The database requires schema version 4. A later real-folder execution milestone must introduce a separate scope-upgrade review, stronger live authorization/recovery design, and new negative tests; it cannot silently reuse this consent.
