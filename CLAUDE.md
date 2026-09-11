# DeskAI Claude Code Instructions

Read and follow @AGENTS.md completely. It is the main repository instruction file.

Before changing code, read `README.md` and every Markdown file under `docs/`
completely. Treat those documents as the source of truth. If instructions conflict,
`docs/SECURITY.md` wins.

## Current Handoff

- Milestones V0.1, V0.2, and V0.3 are complete. V0.4 is complete except confirming
  duplicates by content. V0.5 is complete except checks after the window is closed.
- V0.6 "Organize Your Own Folders" is complete in code and tests (2026-09-11; review
  `docs/security/2026-09-11-v0.6-milestone-review.md`, ADR 0019–0023). The owner's manual
  "V0.6 sign-off" list in `docs/MANUAL-TESTING.md` is still outstanding. The practice page is
  gone; `FolderTidyExecutor` is the only code that moves a file.
- Open items carried by earlier milestones, each needing its own security review first:
  confirming duplicates by content (V0.4, needs its own consent wording and bounds) and checks
  after the window is closed (V0.5). V0.7 is still marked Future. Ask the owner which comes next
  rather than starting one unasked.
- As of 2026-09-10 every page has page tests in `DeskAI.Presentation.Tests`; keep the
  Feature Coverage Map in `docs/TESTING.md` complete.
- Do not rebuild finished milestones or implement the whole remaining roadmap at once.
- Before starting work, inspect Git history, the current tree, tests, and the roadmap.
- Work in small coherent tasks and commit every completed task with a descriptive message.

## Product Safety

The rules about AI access in the project documentation apply to the AI provider code
inside DeskAI. That runtime AI must never receive filesystem, shell, scanner, executor,
credential-enumeration, registry, process-launch, or permission-changing capabilities.

While developing and testing DeskAI:

- Never scan, read, move, rename, organize, or delete files in the owner's real Desktop,
  Downloads, Documents, Pictures, cloud-sync folders, or other personal locations.
- Use only generated dummy files inside controlled temporary test folders.
- Never request, read, print, log, commit, or place a real API key in source or test data.
- The OpenRouter key is entered by the owner through the DeskAI UI and stored by Windows
  Credential Manager. Claude Code does not need the key.
- Never weaken authorization, path normalization, reparse-point, collision, preview,
  approval, journal, or undo protections to make a feature easier to implement.
- AI output remains untrusted advice. Deterministic application code alone decides what
  is allowed, and AI advice must never execute a file operation.

## User Experience

DeskAI is mainly for ordinary, non-technical people. Keep visible labels, descriptions,
errors, and workflows short, calm, friendly, and easy to understand. Keep terms such as
provider, endpoint, DTO, schema, SQLite, deterministic, authorization, and telemetry out
of the main UI unless essential. Put optional implementation information under a simple
label such as “More details.” Do not weaken truthful safety explanations.

## Required Workflow

For each task:

1. Inspect Git status and preserve unrelated changes.
2. Confirm the task belongs to the active roadmap milestone.
3. Identify relevant threat cases before implementation.
4. Add or update focused tests using only generated temporary data and fakes. Anything a
   person can see or do also gets a page test in `DeskAI.Presentation.Tests` and a row in
   the Feature Coverage Map in `docs/TESTING.md`.
5. Implement the smallest complete vertical slice.
6. Build the full Release solution and run all tests.
7. Update affected documentation in the same change.
8. Review the diff for secrets, personal paths, unsafe access, and misleading UI text.
9. Commit the completed task.
10. Explain what changed, data flow, important C#/.NET concepts, verification evidence,
    remaining risks, what the owner should test manually, and the exact next task.

Use these verification commands unless the repository documentation changes them:

```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
dotnet format DeskAI.sln --no-restore --verify-no-changes
```

Never claim completion when builds or tests have not actually passed. Never perform a
live paid-provider test without the owner's explicit approval.
