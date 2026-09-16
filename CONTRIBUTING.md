# Contributing to DeskAI

Thank you for looking. DeskAI is a small, careful project with one rule above every other:

> AI decides what it recommends. Deterministic application code decides what is allowed to happen.

A change that weakens that rule is not accepted, however useful the feature.

## Before you start

1. Read `AGENTS.md` (the main instruction file), then `docs/SECURITY.md`, `docs/ARCHITECTURE.md`,
   and `docs/UI-UX.md`. If documents conflict, `docs/SECURITY.md` wins.
2. Look at `docs/ROADMAP.md` for what is planned and what is deliberately deferred. Work on
   something in scope, or open an issue first to talk about it.
3. Set up with `docs/DEVELOPMENT.md`. You need Windows 11, the .NET SDK in `global.json`, and
   nothing else; the pinned Windows App SDK package supplies the build assets.

## The rules that always apply

- **Never test on personal folders.** Every test generates its own files under a temp folder.
  Nothing may scan, read, move, or delete anything in a real Desktop, Downloads, Documents,
  Pictures, or cloud-synced folder, in tests or while developing.
- **Never handle a real API key.** Tests use fake vaults and obvious non-secret tokens. Keys are
  entered by a person through the app and live in Windows Credential Manager.
- **Never widen what AI can reach.** The AI layer receives minimized data and returns a narrow
  structured suggestion. No filesystem, shell, process, registry, settings, or credential access.
- **Never bypass the pipeline.** Every file change goes proposal → plan → safety validation →
  preview → approval → the one executor → journal. No new executor commands without a security
  review; no permanent deletion, ever.
- **Every promise on screen must be true.** If a change makes a sentence in the app untrue, the
  sentence changes in the same commit.

## How a change is made

1. Pick one small, coherent task. Identify the threat cases first.
2. Add or update tests before or with the code, using only generated temporary data and fakes.
   Anything a person can see or do gets a page test in `tests/DeskAI.Presentation.Tests` and a
   row in the Feature Coverage Map in `docs/TESTING.md`.
3. Build Release and run everything:

   ```powershell
   dotnet build DeskAI.sln -c Release --no-restore
   dotnet test --solution DeskAI.sln -c Release --no-build --no-restore
   dotnet format DeskAI.sln --no-restore --verify-no-changes
   ```

4. Update the documentation the change affects, in the same commit. A new decision gets a short
   ADR in `docs/decisions/`; a new capability gets a review in `docs/security/` first.
5. Add the manual steps a person needs to `docs/MANUAL-TESTING.md` for anything a test cannot see
   (dialogs, keyboard, Narrator, themes, the real wallpaper).
6. Open a pull request with what changed, why, how data flows, the test evidence, and what is
   still not done. Small pull requests are reviewed; large ones are asked to split.

## Style

Modern C#, nullable enabled, warnings on. Comments explain why and what must stay true, not what
the syntax does. The interface language is plain: no "provider", "endpoint", "schema", "SQLite",
"deterministic", "authorization", or "telemetry" where a person reads it; the help catalog test
enforces the list.

## Reporting a security problem

See `SECURITY.md` at the repository root. Please do not open a public issue for something that
could expose or destroy someone's files.
