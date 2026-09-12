# DeskAI Claude Code Instructions

Read and follow @AGENTS.md completely. It is the main repository instruction file.

Before changing code, read `README.md` and every Markdown file under `docs/`
completely. Treat those documents as the source of truth. If instructions conflict,
`docs/SECURITY.md` wins.

## Current Handoff

**Read `docs/HANDOFF.md` before anything else.** The owner clears the conversation after each
roadmap version, so that file — not the chat — is where the current state, the open items, and
the decisions already agreed but not yet built are recorded. Start every new session there.

- Ask the owner which open item comes next rather than starting one unasked.
- Do not rebuild finished milestones or implement the whole remaining roadmap at once.
- Before starting work, inspect Git history, the current tree, tests, and the roadmap.
- Work in small coherent tasks and commit every completed task with a descriptive message.
- Every page has page tests in `DeskAI.Presentation.Tests`; keep the Feature Coverage Map in
  `docs/TESTING.md` complete.

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
11. When the task closes a roadmap version, rewrite `docs/HANDOFF.md` in the same change,
    following the "How to update this file" section at its end. Also write down there any
    decision the owner made in conversation that is not yet in code, an ADR, or a design
    document: the chat is cleared after each version, and an unrecorded decision is lost.

Use these verification commands unless the repository documentation changes them:

```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
dotnet format DeskAI.sln --no-restore --verify-no-changes
```

Never claim completion when builds or tests have not actually passed. Never perform a
live paid-provider test without the owner's explicit approval.
