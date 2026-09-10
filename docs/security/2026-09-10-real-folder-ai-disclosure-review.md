# Security Review — AI Suggestions for Your Own Folders

- Date: 2026-09-10
- Scope: V0.6 step 2b — sending information about files in a connected, tidy-permitted
  folder to the AI the person set up, to get a suggested category for each.
- Required by: `docs/SECURITY.md` ("a focused review is required before introducing … cloud
  transmission") and the deferred gate in `2026-09-08-v0.3-ai-privacy-review.md`, which said
  sending any user-selected folder metadata needs "a new disclosure review, protected-item
  integration, UI preview of the exact real request, and additional tests".
- Status: accepted for real-folder AI suggestions as built (see Result). Design written before
  the code; result added when the step was built.

## What changes

Until now AI has only ever seen six generated sample records on the practice page. This step
lets it see, with the person's agreement, a little about real files in a folder they connected
and allowed DeskAI to tidy. That is the first time anything about a person's own files can
leave the computer.

## What may be sent

At most, per file: the file type, the size and last-changed date, and the file name — each
only if switched on in Privacy and AI. Never: the full location on the computer (even if that
switch is on), folder names, anything inside the file, DeskAI's own file IDs, or the name of
the folder being tidied. Each file is labelled with a random number made for that one request.

Only files that could still move are candidates, and of those only the ones the person's rules
do not already place. By default that narrows further to files DeskAI cannot place by type.

## Threat cases and controls

**Sent without the person knowing.** Nothing is sent when the page opens or when the list is
worked out. A button prepares a request; a dialog then shows the service and where it goes,
the categories shared, and one line per file describing exactly the fields in the request.
Only pressing Send sends that same prepared request. Test: preparing leaves the recording
internet empty.

**More sent than agreed.** The request is built by the existing `AiRequestBuilder` from the
intersection of the saved sharing choices and the categories real folders may ever send
(type, size and date, name). `ConfiguredSuggestionProvider` independently refuses a cloud
request asking for anything outside the saved choices. Tests: with default sharing no file
name appears; with full locations switched on the folder path still does not appear.

**A stable identifier leaks.** Scanned file IDs are a SHA-256 of the folder's ID and the
file's path, identical on every scan. Sent as-is they would let a service recognise the same
file across requests. Each request instead carries fresh random stand-ins, mapped back
locally. Test: no scanned file ID appears in the request.

**Protected files included.** Protected locations cannot be connected at all, and the scanner
skips protected entries. As a second, independent check each file is tested against the
safety policy when the request is prepared and dropped if blocked, counted but not described.
Test: Safety test for the new check.

**Files DeskAI will not touch are sent anyway.** Downloading, recently changed, online-only,
hidden, and system files are left alone before any AI step and are never candidates. Test:
none of them appears in the request even in "every file" mode.

**Consent or setup changes between the preview and Send.** Send re-reads the settings and
the folder. It refuses, sending nothing, if tidying was withdrawn, the AI choice or its
destination changed, online consent was withdrawn, or the categories to be sent are no longer
all allowed. Tests for the settings and permission cases.

**A hostile or broken answer.** The existing strict parser refuses unknown fields, duplicate
properties, unknown categories, invented or duplicate file numbers, bad confidence, long or
control-character reasons, and oversized answers, and refuses the whole answer rather than
repairing it. The tidy service checks again that every number in the answer was one it sent.
Tests: an answer with an extra "destination" field, and one naming a file not asked about,
are both refused while type and rule suggestions stay as they were.

**AI choosing where a file goes.** The answer can only name a category from a fixed list.
DeskAI turns the category into one of its own folder names, inside the chosen folder, and the
existing safety check runs over the resulting plan. The AI's reason text is not displayed, so
a file name built to make the AI "explain" something alarming cannot put that text on screen.
Test: an answer whose reason names a system folder still lands in DeskAI's folder, and the row
does not show the AI's words.

**AI overruling the person.** Rules win over AI in both modes, and rule-placed files are not
sent. Unsure ideas start unticked and are grouped apart.

**Stale advice.** An idea is remembered with the file's size and last-changed time and applies
only while both still match. Test: changing the file after the answer drops the idea.

**Cost and volume.** One press is one request, at most 100 files, subject to the existing daily
limit, timeout, request and answer size limits, no retries, and no fallback to another
service. Test: at the daily limit nothing is sent.

**AI reaching the filesystem.** `TidyAiService` depends on the folder list (to re-check
permission), the saved AI settings, the suggestion service, and the safety check. It holds no
scanner, reader, index, executor, or journal; a test fails if one is added. The AI project
still references Core only and receives only the request record.

**Moving files.** No step in this change can move a file. The Tidy button stays off until step
3, and every test checks the files are where they were.

## User-facing disclosure

The dialog names the service and its web address (or "this computer"), lists what is shared in
words, shows each file as the AI will see it, and says that what is inside files, full
locations, and folder names are not sent. The page states which service would see what next
to the Ask button before it is pressed. Help topic `organize.askAi` explains the feature, and
`settings.dailyLimit` no longer refers to the practice button alone.

## Result

Accepted, 2026-09-10, with every control above built and tested:

- Engine: `TidyAiService` (Core), `TidySuggestionService` taking mode and advice,
  `IPlanSafetyCheck.IsProtected` (Safety). Tests: `TidyAiTests` (18), `TidyAiServiceTests`
  (reach and the sendable ceiling), `PlanSafetyCheckTests` (protected check).
- Page: the "Ask AI" card and preview dialog on Tidy a folder. Tests: `TidyAiPageTests` (7).
- Three controls were removed on purpose to check the tests notice: sending scanned IDs,
  honouring the full-location switch, and skipping the re-check before Send. Each made its
  test fail, and was restored.
- Full suite: 777 tests pass, Release build with no warnings, formatting clean.

The preview dialog itself is a WinUI object and is checked by hand
(`docs/MANUAL-TESTING.md`). Decision recorded in ADR 0020.

## Still not accepted

Sending text from inside files, images, full locations, or folder names to any AI; asking AI
without the preview; and any path from an AI answer to a moved file other than the ordinary
plan, safety check, preview, and approval that step 3 will add.
