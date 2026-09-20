# DeskAI — Coding Handoff

Updated 2026-09-20 after the PDF search follow-up. This is a map, not a replacement for
`AGENTS.md` or `docs/SECURITY.md`. Read those before changing code; security rules win if
documents conflict. The owner wants one coherent milestone at a time, plain UI wording,
generated-data tests, a beginner-friendly explanation, and a commit for each completed task.

## Checkout and release state

- Repository: `C:\Users\Hammouri\Desktop\DeskAI`, branch `main`. The PDF search follow-up
  began with `66a8e3b` (`Explain per-file PDF search outcomes`). Later local commits added a
  generated PDF one subfolder down for safe manual checking and clarified partly-read PDF
  wording. Verify Git state before work; these commits remain local until the owner chooses
  to push.
- The PDF implementation is `dce859a` (`Add consent-gated local PDF text search`), built on
  the requested starting commit `e7436f3`. The follow-up is `66a8e3b`.
- Neither PDF commit was pushed. No version tag or GitHub release was created for this work.
  A **local** self-contained publish was checked after `dce859a`; it included
  `PdfWorker/DeskAI.PdfWorker.exe`. That local publish is not a release and predates the
  follow-up UI change. Ask the owner before any push, tag, or release. If asked to push, push
  `main` only, never `--all` or `--mirror`; older local refs have included the owner's email.
- Last follow-up verification: the full solution built with `-p:DeskAiUiPreview=true` and
  0 warnings/errors; all **1,401/1,401** tests passed with no skips; `dotnet format
  DeskAI.sln --no-restore --verify-no-changes` passed. A normal Release build could not copy
  over the app's DLLs while the owner had that build running; the separate preview output
  avoided the file lock. The generated-data preview window was **not visually inspected**
  in this session. No owner file or real API key was opened, scanned, or used by the agent.

## What Search does now

- A person selects all connected folders or one folder under **Look in**. Search first uses
  remembered names, sizes, dates, and other metadata. A newly added file needs **Refresh**
  on its connected folder before it appears in that index. The scanner enters subfolders
  within its depth and entry limits (currently depth 4, 2,000 entries).
- Separate grants permit bounded local plain-text reading, modern `.docx`/`.xlsx` reading,
  then PDF text reading. Old grants were not broadened. The PDF grant has its own confirmation
  and **Stop PDF reading** action. Search never moves or changes a file.
- `pdf` alone lists remembered PDF **names**. `pdf hammouri` means `.pdf` files whose
  **searchable text** contains `hammouri`; it does not promise every PDF will appear.
  Search reads at most 50 eligible files per request. Each PDF is limited to 8 MB, the first
  20 pages, 64 KB of extracted text, and a 5-second worker deadline; the search checks a
  20-second overall deadline between files. A match may be missed beyond those limits.
- **Found inside your files** shows short snippets. The follow-up added a collapsed
  **Files checked** list naming each attempted file and distinguishing matched text, read
  without a match, partly read, and could not be read. Its data lives only in the current
  result. The no-eligible-files message now points to Refresh, PDF permission, and the
  name-only `pdf` search.
- PDF parsing runs in a fixed local `DeskAI.PdfWorker` process. The trusted extractor checks
  the connected root, canonical containment, protected paths, and links, opens read-only,
  and sends bounded bytes to the worker over standard input. The worker receives no path.
  A crash, timeout, encrypted/damaged PDF, or no extractable text becomes a skipped file.
  The process contains ordinary parser faults but is **not an OS security sandbox**: it runs
  as the signed-in user. See ADR 0037 and its security review.
- The optional AI button interprets **only the sentence the person typed**, after its own
  disclosure. It never receives file contents, search results, paths, images, an index,
  shell access, or direct filesystem access. File search is deterministic local code, not
  full semantic or multilingual search. Scanned-PDF OCR and photo-subject search are absent.

## Owner report that prompted the follow-up

- The owner put a PDF in a subfolder of a connected folder. Before allowing PDF reading, a
  search showed no eligible files opened. After the separate grant, `pdf hammouri` showed
  one content match among five attempted files, one partial read, and two unreadable files.
  The old UI did not identify which PDF had no matching word versus which could not be read.
- A generated-data page test proved a newly added text PDF one subfolder down is found after
  **Refresh** and PDF consent. Another page test, failing before the follow-up, proved the
  need for per-file outcomes. The new **Files checked** list addresses that ambiguity. It
  does not establish what happened to the owner's other PDF: the agent did not open it, and
  the owner has not yet reported its entry from the new list.
- The owner interrupted a prior chat turn. That did not modify their files. Switching PDF
  reading permission only grants or withdraws a read capability; it does not edit PDFs.
- The owner may still be running an older local build. `66a8e3b` is committed locally but
  not released or pushed, so **Files checked** requires launching an updated build.
- On 2026-09-21 the owner showed **Files checked** with `Fintech Rally Presentation
  Template_EN.pdf` in `shefaa presntation / test pptx`: it was found in the nested folder,
  partly read, and had no `hammouri` match in the part read. `full images.pdf` and
  `shared image.pdf` were listed as unreadable. The exact reason for the partial read is
  unknown without opening the owner's PDF, which the agent did not do. A generated 21-page
  page test reproduced the ambiguity and Search now names the 20-page or 64-KB PDF text
  limit on partly read rows. This wording change is local, not released.

## Key code and decisions

- Permission and search: `src/DeskAI.Core/Roots/RootCapabilities.cs`,
  `src/DeskAI.Core/Search/ConnectedFolderService.cs`,
  `src/DeskAI.Core/Search/ContentSearchService.cs`.
- Path-gated extraction and helper protocol: `src/DeskAI.Infrastructure/Content/PlainTextExtractor.cs`,
  `src/DeskAI.Infrastructure/Content/PdfProcessReader.cs`,
  `src/DeskAI.PdfWorker/Program.cs`.
- UI: `src/DeskAI.Presentation/ViewModels/SearchViewModel.cs`,
  `src/DeskAI.App/Views/SearchPage.xaml` and its code-behind.
- Generated tests: `tests/DeskAI.Presentation.Tests/SearchPageTests.cs`,
  `tests/DeskAI.Infrastructure.Tests/PlainTextExtractorTests.cs`, and
  `tests/DeskAI.Core.Tests/RootCapabilitiesTests.cs`. The feature map is in `docs/TESTING.md`.
- Design records: `docs/decisions/0036-separate-consent-for-local-office-search.md`,
  `docs/decisions/0037-separate-consent-for-local-pdf-text.md`, and
  `docs/security/2026-09-20-pdf-text-search-review.md`.

## Next safe action

The owner should manually check the updated Search page using only the UI preview's generated
temporary Downloads. `tools/UiPreview.cs` generates `Lesson handout.pdf`,
`Presentations/Nested handout.pdf`, and `Broken sample.pdf`; the detailed steps are in
`docs/MANUAL-TESTING.md` under **PDF text
search check**. Check PDF consent, search `pdf` alone, search `pdf nebula`, and expand
**Files checked**. To diagnose their own other PDF without the agent accessing it, the owner
can report what that list says for the file after Refresh. A PDF with no text match is
different from one skipped as unreadable or one absent from the remembered-name list.

Do not inspect the owner's real Desktop, Downloads, Documents, Pictures, cloud-sync folders,
screenshots on disk, PDFs, or API key as part of development or automated tests. Screenshots
the owner attached in chat were viewed only as attachments. `TestApp` and the explicit
`-p:DeskAiUiPreview=true` build replace known folders, network, key vault, wallpaper, tray,
and notifications with generated/fake equivalents. The preview leaves unique Temp data for
inspection; do not recursively delete a path unless its resolved target was verified.

After that manual sign-off, ask which milestone the owner wants. Image-subject search needs
its own privacy decision: local vision or an explicit per-request image-pixel disclosure and
Send action for a verified vision-capable provider. An OpenRouter key and connected folder
never imply image-upload permission. Broader language understanding is also unfinished.
Earlier V0.7–V1.1 manual sign-offs and release decisions remain the owner's. Architecture
guard tests are separate hardening work; do not add unrelated features to the PDF follow-up.

## Working rules for the next coding session

- Read `AGENTS.md`, `docs/SECURITY.md`, and the relevant product, architecture, testing,
  roadmap, provider, development, and UI documents before changing code.
- Keep AI away from filesystem, shell, process launch, registry, permission, credential, and
  file-mutation powers. For any file change, preserve proposal → plan → validation → preview
  → approval → deterministic executor → journal. Search itself is read-only.
- A person-visible behavior needs a page test and a Feature Coverage Map row. A bug found
  by the owner gets a page test that fails before its fix. Use generated files in verified
  Temp folders only. Build the full Release solution, run all tests, verify formatting,
  update docs, and commit a finished change. Do not push or release as a side effect.

## Copy-paste starter prompt

> Continue DeskAI at `C:\Users\Hammouri\Desktop\DeskAI`. Read `AGENTS.md`,
> `docs/HANDOFF.md`, and especially `docs/SECURITY.md`; confirm `66a8e3b` is in the
> checkout and inspect Git status. PDF text search and its per-file **Files checked**
> follow-up are implemented locally but not pushed or released. First help me verify the
> updated Search page using only generated UI-preview files, or use the per-file outcome I
> report to diagnose my other PDF. Do not open or scan my personal folders or use my API
> key. Keep work to one agreed milestone, test with generated files, update docs, and
> commit any code change. Ask before pushing, tagging, or releasing.
