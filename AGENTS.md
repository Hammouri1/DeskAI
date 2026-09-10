# DeskAI Repository Instructions

## Mission

Build DeskAI as a trustworthy, Windows-first, local-first AI workspace and file organizer. The product helps people understand, search, organize, and improve their files without surrendering control of their computer.

The permanent rule is:

> AI decides what it recommends. Deterministic application code decides what is allowed to happen.

## Read Before Changing Code

Use these documents as the source of truth:

- `docs/PRODUCT.md` — users, scope, product principles, and feature definitions.
- `docs/ARCHITECTURE.md` — projects, dependencies, data flow, and domain model.
- `docs/SECURITY.md` — non-negotiable trust and filesystem boundaries.
- `docs/ROADMAP.md` — milestones and current scope.
- `docs/AI-PROVIDERS.md` — local, cloud, and no-AI modes.
- `docs/DEVELOPMENT.md` — setup, workflow, conventions, and first-build prompt.
- `docs/TESTING.md` — test strategy and filesystem-test rules.
- `docs/UI-UX.md` — navigation, preview, safety language, and visual direction.
- `docs/INTERVIEW-NOTES.md` — learning goals and concepts that must be explainable.

If documents conflict, security rules win. Update documentation in the same change when an architectural or product decision changes.

## Non-Negotiable Safety Rules

- An LLM never receives direct filesystem, shell, PowerShell, CMD, registry, installer, executable-launch, or permission-changing access.
- AI output is untrusted input. Parse it into a narrow structured proposal, validate it, and reject anything invalid.
- All file changes go through: proposal → `OrganizationPlan` → safety validation → preview → approval → deterministic executor → journal.
- No permanent automatic deletion. Cleanup may suggest review, ignore, quarantine where designed, or Recycle Bin after explicit approval.
- Never overwrite a destination silently. Detect collisions and require a deterministic resolution.
- Operate only inside explicitly authorized roots. Normalize and resolve paths before authorization checks; protect against traversal, links/reparse points, case differences, and time-of-check/time-of-use changes.
- System, application, credential, and configured protected locations remain blocked. The model cannot override policy.
- Early development and automated tests must never scan or modify real Desktop, Downloads, Documents, Pictures, cloud-sync, or other personal folders. Use temporary directories containing generated dummy files.
- API keys must not be stored in plaintext, committed, logged, included in telemetry, or sent to a different provider.
- Cloud processing is opt-in and data-minimized. No account or DeskAI-hosted backend is required for core features.
- Undo and an append-only operation journal are part of the design, not later polish.

## Technical Direction

- C#, modern supported .NET, WinUI 3 / Windows App SDK, XAML, SQLite.
- MVVM in the UI; dependency injection at the composition root; async APIs and cancellation for I/O.
- Nullable reference types and warnings enabled.
- Prefer BCL and official Windows APIs; justify significant dependencies.
- Use official known-folder APIs rather than hardcoded user paths.
- Keep provider SDK types, SQLite types, and Windows APIs behind adapters.
- No Electron, Node.js, Python runtime, hosted backend, or unrelated service unless an explicit architectural decision approves it.

## Intended Solution Boundaries

```text
src/
  DeskAI.App              WinUI views, dialogs, navigation, Windows adapters, composition root
  DeskAI.Presentation     View models and the shared service registration, free of WinUI
  DeskAI.Core             Domain types, use cases, rules, provider-neutral contracts
  DeskAI.Safety           Policy evaluation and plan validation
  DeskAI.Infrastructure   Filesystem, SQLite, Windows integration, indexing
  DeskAI.AI               Provider adapters and structured AI translation
tests/
  DeskAI.Core.Tests
  DeskAI.Safety.Tests
  DeskAI.Infrastructure.Tests
  DeskAI.AI.Tests
  DeskAI.Presentation.Tests   Page tests: each feature used the way a person uses it
```

`Core` must not reference App, Infrastructure, AI-provider SDKs, WinUI, or SQLite. `Safety` may depend on Core. Infrastructure and AI implement Core contracts. Presentation must not reference WinUI. App composes the system and must not contain filesystem business logic.

## Engineering Rules

- Make the smallest coherent change for the active milestone. Do not build future-roadmap features speculatively.
- Before editing, inspect the relevant files and current Git state. Preserve unrelated user changes.
- Use clear names, focused classes, immutable domain values where useful, and interfaces only at meaningful boundaries.
- Comments explain why, invariants, and hazards—not obvious syntax. Avoid giant services, service locators, static mutable state, and premature frameworks.
- Never hardcode personal paths, secrets, provider models, or unexplained thresholds.
- Treat plans as data. Planning must have no filesystem side effects. Execution consumes only validated, approved plans.
- Design every operation with a stable ID, explanation, safety result, and enough before/after state for audit and undo.
- Compile after meaningful changes. Run focused tests, then the full relevant suite. Fix warnings or explain them.
- Do not claim a feature works unless it is implemented and verified. Clearly label placeholders and planned work.

## Working With the Owner

This repository is both a real product and a learning project. After every meaningful task, explain in beginner-friendly language:

1. what changed and why;
2. how data flows through it;
3. important concepts introduced (for example MVVM, interfaces, dependency injection, records, async/await, repositories, DTOs, transactions, or tests);
4. build/test evidence;
5. risks, incomplete work, and the exact recommended next step.

Do not hide complexity, but introduce it step by step. When a new concept is required, give a short explanation and point to its concrete use in DeskAI.

## Definition of Done

A change is done only when it is scoped to the milestone, respects all security boundaries, builds where the environment supports it, has proportionate tests, updates affected docs, introduces no real-user-folder test access, and has an honest completion summary.

Proportionate tests always include a page test in `DeskAI.Presentation.Tests` for anything a person can see or do, listed in the Feature Coverage Map in `docs/TESTING.md`. Engine tests alone do not make a feature done. A bug the owner finds by hand gets a page test that fails before the fix.

## First Build Instruction

If this repository contains documentation but no solution yet, follow **First Build** in `docs/DEVELOPMENT.md`. Build only the foundation milestone. Do not organize the owner's computer and do not implement broad AI, automation, search, wallpaper, or plugin features yet.
