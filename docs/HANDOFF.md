# DeskAI — Coding Handoff

## Start here (updated 2026-09-24, after Desktop Studio step 1)

**State:** Desktop Studio step 1, **Find groups**, is built on the branch
`desktop-studio-find-groups` (from `2153fdd` on `main`), one commit per plan task. Not merged,
pushed, tagged, or released until the owner says so. After the whole-branch review and its
fixes, the Release build had 0 warnings, all
1,513 tests passed, and formatting verification passed. The owner chose to build it in one
session (Native) with one review of the whole branch at the end.

**What was built** (ADR 0042, review `docs/security/2026-09-24-desktop-grouping-review.md`):
a **Desktop Studio** page in the menu after Automatic tasks. Its one card, Find groups, sorts
the connected Desktop's folders and loose files into at most 8 groups plus Not sure, either by
AI after a Send window listing exactly what goes (per folder: name, kinds of files, up to 5 file
names; per file: its name) or by DeskAI's own guess from the kinds of files. The person can
rename, merge, and move. The board is stored per folder (schema 16) and erased with it. Nothing
changes on disk or in Windows. Details in the dated section below.

**Icon-position probe (2026-09-24): no-go.** ADR 0043 is Rejected. In two Windows Sandbox runs
(`tools/IconPositionProbe`, plan `docs/superpowers/plans/2026-09-24-icon-position-probe.md`),
icons could be placed, but a Desktop refresh (F5) or an Explorer restart put them back on the
grid, even with the layout saved. So **Keep together, Make zones, and Name the zones are
dropped**, as the design said. The other routes (Explorer's stored layout in the registry, or
driving the Desktop's list control from another process) are outside what `docs/SECURITY.md`
allows this feature and need a new decision by the owner. The probe stays in `tools/` as a
record; it is not part of the app and refuses to run outside the Sandbox.

**Next task:** carry out `docs/superpowers/plans/2026-09-24-desktop-studio-moves.md` (Clear old
stuff and Folder by group, ADR 0044) once the owner has reviewed the plan and chosen how it is run.
The plan gives moving things on the Desktop **its own yes**, separate from "Allow tidying", because
the tidy dialog promises DeskAI never touches what is inside a folder.

**Decisions the owner made that are not yet in code** (also in
`docs/superpowers/specs/2026-09-24-desktop-studio-design.md`):
- Every Desktop Studio card is chosen on its own; one never turns on another. Naming style
  "short and friendly": Keep together, Make zones, Name the zones, Clear old stuff, Folder by
  group, Tag names, Color groups.
- Build order after step 1: ~~screen designs (icon positions + labelled wallpaper)~~ — dropped
  after the probe (ADR 0043) → Clear old stuff and Folder by group → Tag names → Color groups
  (after a probe). Each step is released and checked by the owner before the next.
- 2026-09-24: the owner chose to **skip the hand check of Find groups for now** and look at
  Desktop Studio as a whole later, going straight on to Clear old stuff and Folder by group.

**Open with the owner:**
- Merge and push: the owner decided (2026-09-24) to keep committing each task locally on this
  branch and push to GitHub only once the whole Desktop Studio feature is finished. Ask again
  then; do not push before.
- The GSD skill was installed on 2026-09-24. It is **not set up** in this repository (no
  `.planning/`). The recommendation given: keep `docs/ROADMAP.md`, this file, and
  `docs/superpowers/plans/` as the only plan, and use GSD only for reviews, debugging, and small
  fixes, so there are never two roadmaps. The owner has not answered yet.
- Their Search screenshots were shared as paths in their real Pictures folder and were not
  opened (CLAUDE.md). Ask them to paste the images into the chat.
- Other improvement ideas not yet picked, in the suggested order: code signing, an opt-in
  "newer version?" button, re-enabling picture search after a fresh review, Recycle Bin for
  proven copies, a first-run guide, architecture guard tests.

## 2026-09-24 Desktop Studio step 1: Find groups

Built from `docs/superpowers/plans/2026-09-24-desktop-studio-find-groups.md` in 7 tasks
(ADR and review first, then scanner, reader, AI connection, storage and service, page, docs).

- **Data flow.** The scanner now also reports each folder it passes (`FolderDiscovered`).
  `DesktopLookService` makes one bounded, read-only look at the connected Desktop (4 levels,
  5,000 entries; at most 60 folders and 200 files kept, the rest guessed locally and never
  sent). `DesktopGroupingService.PrepareAsync` numbers the items and builds the exact lines the
  Send window shows; `SendAsync` re-checks the Desktop, the AI choice, and the sharing choices,
  then calls `IOrganizationSuggestionProvider.GroupItemsAsync`, the one AI connection's third
  method, which returns text only. `DesktopGroupReading` accepts one exact JSON shape or refuses
  the whole answer. `desktop_group_boards` (schema 16) keeps the board, cascading with the
  folder. `DesktopStudioViewModel` and `DesktopStudioPage` show it.
- **Safety.** Online AI is used only if the sharing choices allow file types, file names, and
  folder names (checked when preparing, at Send, and in the AI connection). Names travel as data
  between markers. Hidden, system, link, and protected items, and any top-level folder holding
  one (DeskAI's own program folder), never appear. The service holds no executor, journal,
  writer, or setting changer (a test checks). The page reads no disk: the board records which
  items are folders.
- **Owner's manual checks:** open Desktop Studio; connect the Desktop if asked; press **Use
  DeskAI's guess** and check the groups; if AI is on, press **Find groups with …** and check the
  window lists only names and kinds of files, then Send or Cancel; rename, merge, and move;
  close and reopen DeskAI and check the board is kept; check that nothing on the Desktop moved.
- **Whole-branch review** (a fresh reviewer): 1 critical and 3 important findings, all fixed
  with a test that failed first. (1) A big folder on the Desktop (over 5,000 items within 4
  levels, such as a code project) made the whole card fail with a wrong safety message; now the
  Desktop is still sorted and the page says some folders were too full to look all the way
  inside. (2) Hidden files inside folders (`.git`, `desktop.ini`) were counted and named; now
  skipped. (3) Long non-English names could make Send refuse an approved list; Prepare now keeps
  the list within the size limit. (4) "Grouped by …" named today's AI; the board now remembers
  who made it.
- **Deferred small points from the review** (the owner decides): menu items in Move to… and
  Merge into… skip the busy check, and buttons look enabled while busy; Connect clears the busy
  state early; an AI group with no items is
  kept as an empty box; a very long board could hit the storage size check and show technical
  text; the hidden-only Desktop case is tested below the page level only; a rename that only
  changes letter case keeps the old casing on the board.
- **Board layout fix (owner-found, 2026-09-24):** on a real Desktop, Not sure ran down the page in
  one long column and the Rename and Merge buttons squeezed names ("Document s"). Group cards are
  now one size with an item count and a **⋯** menu (Rename…, Merge into — left out when there is
  no other group), long groups scroll inside their card, and Not sure spans the width in columns
  with its own scroll. The owner pointed to two screenshots by their path in Pictures and they
  were opened at the owner's request; next time, ask them to paste images into the chat.
- **Known limit:** a Desktop with more than 60 folders or 200 loose files, or with very long
  names, is only partly sorted by AI; DeskAI's guess sorts the rest.

## 2026-09-24 Search finds more of your files

The owner asked for improvement ideas and chose this one first (the other ideas, in order:
code signing, an opt-in "newer version?" button, re-enabling picture search after a fresh review,
Recycle Bin for proven copies, a first-run guide, and architecture guard tests). Three problems were
fixed together (ADR 0041). The look at a connected folder was 4 levels and 2,000 items and is now 8
and 20,000. A look that stopped at the item limit used to make the index forget every file it had
not reached; it now forgets nothing, and only a complete look removes files that are gone. Schema
15 adds `index_looks` (last look time, stopped early, folders too deep), cascading with the folder,
and the Search folder row, the Connect/Refresh message, and "Nothing matched" say when a folder
was only partly checked. Opening Search looks again at folders not checked in the last 10 minutes,
through the same Refresh path; leaving the page stops it.

Page tests (generated data only) cover a photo six folders down, an early stop that keeps an
earlier file (confirmed failing with the fix switched off), the too-deep note, and looking again
on open (a movable test clock). The performance probe now accepts `DESKAI_PERF_FILES`: 20,000
generated files connect in about 1 s and refresh in about 0.3 s. The Release build had 0 warnings,
all 1,439 tests passed, and formatting verification passed. Not pushed, tagged, or released.

## 2026-09-22 folder connection and first-download follow-up

A first-time user could not connect the folders they tried and could not quickly tell how to
download DeskAI from the README. The four-folder security boundary remains unchanged. The picker
now begins in Documents rather than at This PC, both folder pages name valid choices before the
person opens or completes the picker, and an outside-folder refusal points directly to Home's
**Your folders** card. `WindowsKnownFolders` now calls `SHGetKnownFolderPath` for Desktop,
Downloads, Documents, and Pictures instead of mixing two lookup mechanisms, which keeps Windows
redirected-folder handling consistent. The README starts with a direct 1.1.2 zip link and five
plain steps; the install and user guides now include the first connection and troubleshooting.

The security rule did not broaden: only the four Windows-reported personal folders or their
descendants can connect; protected paths and reparse points are still rejected. The dated folder
connection review records the threat check. The Release solution build passed with zero warnings,
all 1,428 tests passed with no skips, and `dotnet format --verify-no-changes` passed. The owner then
explicitly requested the fix be pushed; version 1.1.2 and its direct release link were prepared for
the tag-driven release workflow.

## 2026-09-21 public 1.1.0 release preparation

An original transparent DeskAI logo was generated for this project: an abstract D/folder with
an inner intelligence spark in the product's mint and deep-navy palette. The source PNG is in
`src/DeskAI.App/Assets/DeskAI.Logo.png`; a multi-size ICO is embedded as the executable icon,
and the navigation pane plus README use the PNG. A source/asset regression pins all three uses;
the full suite now contains 1,423 tests.

Ask DeskAI now carries a visible and accessible **BETA** pill. Its existing boundary is
unchanged: natural-language file-search, storage, and organize questions only, not general
chat. The README now accurately covers PDF/PowerPoint text search, bounded local PDF OCR,
the disabled image-search action, current safety limits, and 1,422 passing tests. The default
displayed version is 1.1.0.

The release workflow was exercised locally. Locked solution restore originally disagreed with
stale runtime sections in library lock files, and publishing could omit the PDF worker's runtime
assets. Library locks now match their projects, `DeskAI.PdfWorker` declares `win-x64`, and the
worker now declares that runtime so the locked solution restore covers it. Release build, test,
format, and self-contained publish all pass. The workflow now also attaches a SHA-256 checksum beside the zip and SBOM.
No push, tag, or GitHub Release was made in this task; those remain explicit owner actions.

## 2026-09-21 flattened-PDF OCR fix

An owner-supplied PDF test case was valid and unencrypted, but most pages had no extractable
text and the requested visible words were flattened into pixels. The normal PDF reader therefore
correctly reported no text match. Search now has a separate
**Search scanned PDF words** action. It requires the existing content and PDF permissions
plus a fresh confirmation for each run, then uses Windows on-device OCR on at most 10 PDFs,
8 MB each, first 20 pages, and 256 KB recognized text per file. Nothing is uploaded,
persisted, or allowed to change a file; the UI labels OCR approximate and puts verification
responsibility on the user. AI picture reading remains disabled.

The owner-found page-flow regression proves normal search and Cancel do not call OCR, an
approved run finds the requested words on Page 8, and no AI transport is used. A one-off probe
through the production `WindowsPdfOcrReader` against that locally supplied case recovered the
words on Page 8. The temporary probe was deleted. ADR 0040 and the dated
security review record the boundary. Final Release build/format/commit details follow in
the current task history.

## 2026-09-21 launch search reliability follow-up

The owner's screenshots showed the concrete failure: PowerPoints of about 8.3 MB and
13.5 MB were skipped by the old 8 MB quick-search bound, so only the smaller matching deck
appeared. The launch fix raises local PDF/PPTX containers to 32 MB, PDF text to the first
100 pages, PowerPoint text to the first 200 slides, and extracted text to 256 KB. PDF and
Office layout whitespace inside a word is ignored for matching, so text extracted as
`Ham mour i` can match `hammouri`; snippets still use the original text. All work remains
local, read-only, permission-gated, bounded, and unpersisted. Image-only/scanned words still
cannot match without OCR.

The owner asked to disable image reading for today's launch. The Search page no longer
offers **Find pictures with AI**, its view-model gate is fixed off, and ordinary no-result
wording no longer directs a person to it. The reviewed visual-search implementation remains
dormant in the codebase for later reconsideration. Generated page regressions cover two
matching PPTX files including a 9 MB deck, a PDF match on page 21, and the disabled picture
gate. This work still needs the final full build/test/format pass and commit recorded below.

Final verification completed: the full Release UI-preview build passed with 0 warnings and
0 errors; all 1,419 tests passed with no skips; `dotnet format --verify-no-changes` passed.
No personal PDF, PowerPoint, folder, or API key was opened during development or testing.

The first local 1.1.0 publish then exposed a window-startup bug: the process stayed healthy
in Task Manager but no window appeared. Published startup had called WinUI `Activate()` only.
It now calls `MainWindow.Reveal()`, which explicitly shows the AppWindow, activates it, and
requests the foreground. A source-level presentation regression test pins this launch path.
After this correction the preview build again passed with 0 warnings/errors, all 1,420 tests
passed, and formatting verification passed. Republish 1.1.0 before asking the owner to retry.

## 2026-09-21 visual Search update

After the slide-text commit `991a275`, the owner clarified that DeskAI must **not**
download a vision model. A configured local AI may inspect pictures; otherwise a chosen
cloud AI may receive an exact selected batch only after a fresh Send dialog for that
search. The visual action uses a first fresh **Read pictures** dialog; candidate files
come from the connected folder's metadata index and can be nested. It can inspect bounded
JPEG/PNG/WebP files, pictures referenced by modern PowerPoint slides, and extractable
images on the first 20 PDF pages. Results identify page/slide where known and show short
AI evidence. Selected picture bytes are sent only after the appropriate choice; no
derived image index is persisted, and no file name or path is sent to the model.
Limits are 30 files, 12 pictures, 4 MB total, 30 seconds preparation, and the existing
scanner depth-8 / 20,000-entry cap (raised 2026-09-24, ADR 0041). An unsupported or text-only vision model may refuse;
there is no provider fallback or model download. See ADR 0038 and its security review.
Generated-data page tests use fake transport and keys. The full preview configuration
build passed with 0 warnings/errors, all 1,416 tests passed, and `dotnet format` reported
no changes. It has not been pushed or released; do not claim the owner is running it until
they launch a new build.

Updated 2026-09-20 after the PDF search follow-up. This is a map, not a replacement for
`AGENTS.md` or `docs/SECURITY.md`. Read those before changing code; security rules win if
documents conflict. The owner wants one coherent milestone at a time, plain UI wording,
generated-data tests, a beginner-friendly explanation, and a commit for each completed task.

## Checkout and release state

- Repository: the local DeskAI checkout, branch `main`. The PDF search follow-up
  began with `66a8e3b` (`Explain per-file PDF search outcomes`). Later local commits added a
  generated PDF one subfolder down for safe manual checking and clarified partly-read PDF
  wording. Verify Git state before work; these commits remain local until the owner chooses
  to push.
- The PDF implementation is `dce859a` (`Add consent-gated local PDF text search`), built on
  the requested starting commit `e7436f3`. The follow-up is `66a8e3b`.
- The next Search slice adds separately approved `.pptx` slide-text reading (ADR 0039).
  A generated page test finds `Hammouri` on slide 2 of a presentation two folders down.
  It does not inspect pictures or perform OCR; the owner's image-search goal remains open.
- Neither PDF commit was pushed. No version tag or GitHub release was created for this work.
  A **local** self-contained publish was checked after `dce859a`; it included
  `PdfWorker/DeskAI.PdfWorker.exe`. That local publish is not a release and predates the
  follow-up UI change. Ask the owner before any push, tag, or release. If asked to push, push
  `main` only, never `--all` or `--mirror`; older local refs have included the owner's email.
- Last follow-up verification: the full solution built with `-p:DeskAiUiPreview=true` and
  0 warnings/errors; all **1,412/1,412** tests passed with no skips; `dotnet format
  DeskAI.sln --no-restore --verify-no-changes` passed. A normal Release build could not copy
  over the app's DLLs while the owner had that build running; the separate preview output
  avoided the file lock. The generated-data preview window was **not visually inspected**
  in this session. No owner file or real API key was opened, scanned, or used by the agent.

## What Search does now

- A person selects all connected folders or one folder under **Look in**. Search first uses
  remembered names, sizes, dates, and other metadata. A newly added file appears after
  **Refresh**, or on its own when Search is opened more than 10 minutes after the last look (ADR 0041). The scanner enters subfolders
  within its depth and entry limits (depth 8, 20,000 entries since ADR 0041).
- Separate grants permit bounded local plain-text reading, modern `.docx`/`.xlsx` reading,
  PDF text reading, and modern `.pptx` slide-text reading. Old grants were not broadened.
  PDF and slide grants have their own confirmations and Stop actions. Search never moves or
  changes a file. Slide pictures still need the separate visual-search design.
- `pdf` alone lists remembered PDF **names**. `pdf hammouri` means `.pdf` files whose
  **searchable text** contains `hammouri`; it does not promise every PDF will appear.
  Search reads at most 50 eligible files per request. Each PDF is limited to 32 MB, the first
  100 pages, 256 KB of extracted text, and a 10-second worker deadline; the search checks a
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
- On 2026-09-21 the owner showed **Files checked** with one PDF in a nested connected folder:
  it was found, partly read, and had no requested-word match in the part read. Two other PDFs
  were listed as unreadable. The exact reason for the partial read is unknown without opening
  the owner's PDF, which the agent did not do. A generated 21-page
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

After that manual sign-off, the next milestone toward the owner's Search goal needs to be
scoped. The owner chose a connected local AI when available, otherwise a fresh per-search
cloud Send choice for selected images. A connected folder or OpenRouter key alone never
permits image upload. Broader language understanding is unfinished.
On 2026-09-21 the owner clarified the intended destination: ordinary-English search across
nested subfolders of connected roots, including slide text such as “PowerPoint with Hammouri
on a slide” and visual subjects such as “PDF with a picture of a dog smelling a flower.”
`docs/PRODUCT.md` and `docs/ROADMAP.md` now record this as planned work, with page/slide
evidence and honest limits. The existing AI sentence translator and PDF text search do not
meet that goal. The owner does not want DeskAI to download a vision model: use a connected
local AI if compatible, or ask separately before sending selected images to cloud AI
(ADR 0038). The local-model and cloud-capability checks, indexing policy, and permission
need a separate reviewed design; this choice does not implement visual search.
Earlier V0.7–V1.1 manual sign-offs and release decisions remain the owner's. Architecture
guard tests are separate hardening work; do not add unrelated features to the PDF follow-up.

## V1.1 release pipeline correction

- The first clean GitHub Actions run for `v1.1.0` exposed a stale-output bug that local
  builds had hidden. `DeskAI.PdfWorker` builds for `win-x64`, while the app and PDF page-test
  projects were still copying from its older non-RID output folder.
- All three copy targets now follow `bin/<configuration>/net10.0/win-x64`, and a source-level
  page regression test keeps the worker project and its consumers aligned.
- A clean local Release build now has zero warnings/errors, all 1,424 tests pass, formatting
  is clean, and a self-contained package includes both the app and PDF-worker executables.
  Replace the failed, unreleased `v1.1.0` tag and verify the hosted workflow creates the ZIP,
  checksum, and SBOM.

## Post-release shell polish and public-repository review

- The owner's 2026-09-21 screenshot showed WinUI's generic title-bar icon even though the new
  logo appeared in the navigation pane. The app now copies `DeskAI.ico` into its output and calls
  `AppWindow.SetIcon` for both normal and startup-failure windows.
- Home's bounded grid now centers in a stretched scroll viewport instead of appearing shifted
  right on a wide window. `ShellLayoutTests` protects both owner-found regressions.
- Before the next public tag, re-run the full Release build/tests/format checks, inspect tracked
  files and Git history for secrets and personal artifacts, and record any remaining distribution
  risks honestly. The existing V1.1 package is unsigned.
- GitHub secret scanning, push protection, and private vulnerability reporting are enabled. The
  root `SECURITY.md` tells reporters to use a private advisory and not attach personal data.
- The owner's downloaded V1.1.0 ZIP was reproduced locally: its checksum and required runtime
  files were correct, but `DeskAI.App` stayed alive with no window. The isolated preview and
  ordinary Release output opened. Comparing them proved `dotnet publish` had omitted
  `DeskAI.App.pri`, `App.xbf`, `MainWindow.xbf`, and all page XBF files (Windows App SDK issue
  #6720). The project now copies those generated resources after publish, and the release
  workflow refuses an incomplete interface. Startup recovery also used bare `Activate()`; it
  now calls `Reveal()`, with regression tests for both paths. Publish this as V1.1.1; do not
  direct users back to V1.1.0.

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

> Continue DeskAI in the repository checkout. Read `AGENTS.md`, `docs/HANDOFF.md` ("Start
> here" first), and especially `docs/SECURITY.md`; inspect Git status and whether the
> `desktop-studio-find-groups` branch (Desktop Studio step 1, Find groups, ADR 0042) is merged.
> Next, after my manual check of step 1, is Desktop Studio step 2, starting with the
> icon-position feasibility probe: write its plan first and ask me before building. I will
> paste my Search screenshots into the chat. Do not open or scan my personal folders or use my
> API key. Test with generated files, update docs, and commit each task. Ask before pushing,
> tagging, or releasing.

## How to update this file

- Rewrite **Start here** at the end of every roadmap version or whenever the chat is about to
  be cleared: the commit `main` is on, what was verified, the exact next task, and the owner's
  open questions.
- Write down every decision the owner made in conversation that is not yet in code, an ADR, or
  a design document. The chat is cleared after each version, so an unrecorded decision is lost.
- Keep older dated sections below as history; correct them only where they are now wrong.
- Update the starter prompt so it names the current commit and the next task.
