# DeskAI — New-Chat Handoff

Updated 2026-09-20. This is the short state for a new coding chat, not a substitute for
`AGENTS.md` or `docs/SECURITY.md`. Read those before changing code. If documents conflict,
security wins. The owner prefers one coherent milestone at a time, friendly non-technical
UI, an explanation and safe manual test after each task, and a commit for each completed task.

## PDF milestone update (2026-09-20)

The PDF text slice below was the task for this coding session and is now implemented in code
(ADR 0037 and `docs/security/2026-09-20-pdf-text-search-review.md`). It requires a separate
PDF yes after Office reading, uses a local bounded helper process, and never sends extracted
text to AI. Scanned pages and image subjects are still outside scope. The older "Search today"
and "Agreed next milestone" sections below describe the starting point for that task; do not
reuse them as the next prompt. The owner's manual PDF check is in `docs/MANUAL-TESTING.md`.
The subsequent owner report found that the result did not say which PDF had no matching word
and which PDF could not be read. Search now has a **Files checked** list for each attempted
file, and a generated subfolder PDF page test. Search `pdf` alone to check which PDF names
are remembered after a folder refresh.
The follow-up Release build has zero warnings/errors; all 1,400 tests pass with none skipped;
format and the safe UI preview build pass. The follow-up did not access the owner's PDFs.
Release build: zero warnings/errors; all 1,398 tests pass with none skipped; `dotnet format`
reports no changes. The UI preview build and a self-contained publish both succeeded; the
publish includes `PdfWorker/DeskAI.PdfWorker.exe`. The preview window itself was not visually
inspected in this automated session. No personal folder or API key was used. Next, the owner
can perform the generated-data manual PDF check, then decide separately whether to pursue
image-subject search or broader language understanding.

## Current state

- Repository: `C:\Users\Hammouri\Desktop\DeskAI`, branch `main`, clean immediately after
  commit `9c941bd` (`Add consent-gated local document search and improve search UI`). Check
  `git status` again; do not assume it is still clean.
- `main` was **five commits ahead of `origin/main`** on 2026-09-20. The private remote is
  `https://github.com/Hammouri1/DeskAI`. Do not push, tag, publish, or delete old refs as a
  side effect of the search task. No release tag existed at this update. If the owner later
  asks to push, push `main` only, never `--all` or `--mirror`; older local refs have included
  their personal email.
- V1.1 and the calm-workspace redesign are complete in code. Earlier manual sign-offs and
  release/push decisions remain the owner's; see `docs/ROADMAP.md` and the manual checklists.
- Last search-slice verification: `dotnet build DeskAI.sln --no-restore -v quiet` succeeded
  with 0 warnings/errors; `dotnet test --solution DeskAI.sln --no-build --no-restore -v quiet`
  passed **1,392/1,392**. Search, Organize, and Automatic tasks were visually checked in the
  explicit temp-data UI preview. A sample-only search for `generated` found two files by
  their contents. The owner's real folders and API key were not used. Re-run the full Release
  build/tests/format checks for the next code milestone; these numbers describe the prior one.

## Search today — do not overclaim

- A person can choose **Look in**: all connected folders or one connected folder. The app
  fixes the old contradiction that said no folders were connected while showing one.
- Search uses remembered names/details. After a separate folder permission, it can read
  bounded plain text and modern `.docx`/`.xlsx` text locally. The earlier plain-text-only
  grant remains plain-text-only; it was **not** silently broadened. The UI offers an explicit
  Word/Excel upgrade, reports partial reads and the number of files checked, and shows a
  snippet. Text is not persisted or sent to AI. The Office reader uses bounded ZIP/XML
  processing and does not extract entries, run macros, or resolve external entities.
- The existing optional AI action interprets **only the sentence the person typed**, after
  its own disclosure. It never receives file contents, images, paths, an index, shell, or
  direct filesystem access. The file search itself is deterministic; this is not full
  semantic or multilingual search.
- PDF contents, scanned-PDF OCR, and identifying visual subjects in photos are **not
  implemented**. A PDF/photo may match by filename or metadata, not by its contents.
- Key locations: `src/DeskAI.Core/Search/ContentSearchService.cs`,
  `src/DeskAI.Infrastructure/Content/PlainTextExtractor.cs`,
  `src/DeskAI.Infrastructure/Content/OfficeOpenXmlReader.cs`,
  `src/DeskAI.Presentation/ViewModels/SearchViewModel.cs`,
  `src/DeskAI.App/Views/SearchPage.xaml` and its code-behind. The permission decision is
  `docs/decisions/0036-separate-consent-for-local-office-search.md`; review is
  `docs/security/2026-09-20-local-document-search-review.md`.

## Agreed next milestone: PDF text search

The owner asked what comes next and accepted a handoff for a fresh chat. The recommended
next task is one **safe, local PDF-text-search slice**, not image search or all remaining
roadmap work at once:

1. Inspect current code, docs, Git state, and tests. Define a PDF-specific threat review
   before choosing a parser. Malformed PDFs are untrusted input; an in-process package plus
   a file-size check is not automatically safe. Consider Windows' PDF APIs or isolation,
   and document dependency, crash, cancellation, page/time/size, and rollback tradeoffs.
2. Existing reading consent says PDFs stay closed. Add **separate, explicit consent** before
   opening any PDF; do not reinterpret the old text or Office grant. Keep authorization,
   canonical path checks, protected-path and reparse-point refusal, and live rechecks in
   deterministic code. PDF extraction must never give AI direct file access.
3. Search only selected connected roots; bound files/pages/bytes/time, skip encrypted,
   damaged, or unsupported PDFs honestly, and say when results are partial. Nothing read
   should be stored or sent to a provider unless a later, independently approved design
   says so. Scanned/image-only PDF OCR is a later slice unless separately reviewed.
4. Use generated dummy PDFs in verified temporary test folders only, including malformed,
   huge, path/link, old-grant, revocation, and no-result cases. Add a page test for the
   person-visible flow and update the `docs/TESTING.md` Feature Coverage Map. Build/test the
   whole solution, inspect the safe UI preview, update docs, and commit the completed task.

**After PDF text:** image-subject search (for example, a flower photo) needs its own decision.
Choose local vision or an explicit per-request image-pixel disclosure and Send action for a
verified vision-capable provider. An OpenRouter key or connected folder never implies image
upload permission. Broader language understanding is also unfinished. Ask the owner before
making a material cloud/local privacy choice.

## Safety and testing reminders

- `docs/SECURITY.md` is binding. AI recommends; deterministic application code controls
  every allowed action. File mutation has its separate plan → preview → approval → executor
  → journal path. Search does not change files. Never give a model filesystem, shell,
  PowerShell, registry, process-launch, permission-changing, or credential access.
- Never test against the owner's real Desktop, Downloads, Documents, Pictures, cloud-sync,
  or other personal folders. Never ask for, reveal, or use their real OpenRouter key.
- `TestApp` and the UI-preview build replace known folders, network, key vault, wallpaper,
  tray, and notifications with generated/fake equivalents. For a safe visual test, build
  `src/DeskAI.App/DeskAI.App.csproj` with `-p:DeskAiUiPreview=true`, launch the executable
  under `artifacts/ui-preview/.../win-x64/`, and connect only its generated Downloads.
  The preview creates unique Temp data and leaves it for inspection; do not recursively
  delete an unverified path.
- Anything visible or clickable needs a page test in `DeskAI.Presentation.Tests` and a row
  in `docs/TESTING.md`. A bug found by the owner gets a failing page test before the fix.
  Run full Release build/tests, then `dotnet format DeskAI.sln --no-restore
  --verify-no-changes` for a code milestone. Do not run overlapping builds/tests into a
  shared output directory.
- Keep visible UI words calm and simple; put technical details behind an optional section.
  Tell the owner precisely what to try in generated data after each milestone.

## Other open work (do not start as part of PDF search)

- Owner's manual sign-off of earlier V0.7–V1.1/release flows remains outstanding; see
  `docs/MANUAL-TESTING.md`.
- Pushing local `main`, inspecting GitHub Actions, deciding version/tag/release, and removing
  old email-bearing local refs require their own explicit request and checks.
- Architecture guard tests for project-reference boundaries and dangerous dependencies
  remain a worthwhile separate hardening task.

## Starter prompt for the next chat

> Continue DeskAI in `C:\Users\Hammouri\Desktop\DeskAI`. First read `docs/HANDOFF.md`,
> `AGENTS.md`, and the relevant documentation, especially `docs/SECURITY.md`, then inspect
> Git status and the current search code. Implement the next **PDF text search** milestone
> only, with separate user consent, a reviewed parser boundary, strict local/authorized-root
> limits, generated temporary-file tests and a Search page test. Do not access my personal
> folders or use my OpenRouter key. Do not implement image upload or OCR silently. Build the
> full solution, run all tests, update docs, commit the task, and tell me what to test myself.
