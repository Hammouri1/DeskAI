# Owner Manual Testing

Use this checklist after each delivered slice. Never point development builds or tests at Desktop, Downloads, Documents, Pictures, cloud-sync folders, or other personal data until the roadmap explicitly enables a reviewed picker flow.

## Common verification

From the repository root:

```powershell
dotnet restore DeskAI.sln --configfile NuGet.Config
dotnet build DeskAI.sln --no-restore --configuration Release
dotnet test --solution DeskAI.sln --no-build --no-restore --configuration Release
```

Expected: the build has zero warnings/errors and every test passes.

## V0.1 Safe Foundation

1. Open `DeskAI.sln` in Visual Studio. If WinUI tooling is unavailable, install the Windows application development workload and Windows 11 SDK 10.0.26100 or later.
2. Launch `DeskAI.App` as x64.
3. Navigate through Dashboard, Organize, Search, Automation, and Settings.
4. Confirm the status says foundation-only and that no folder picker or functional file action is available.
5. Confirm Settings reports Rule Engine Only, no cloud sharing, zero authorized folders, and telemetry off.

## V0.2 Step 1 — Metadata Scanner

There is no scanner UI yet. Run the full test suite and review the `WindowsMetadataScannerTests` results. Confirm no tests are skipped. Do not manually supply a personal folder or add a temporary UI shortcut around authorization.

## V0.2 Step 2 — Deterministic Classification and Recipes

1. Run the Core tests directly:

   ```powershell
   dotnet test --project tests\DeskAI.Core.Tests\DeskAI.Core.Tests.csproj --configuration Release
   ```

2. Run the full suite and confirm the scanner-to-classifier integration test passes.
3. Review the default mappings in `DefaultFileTypeRules.cs` and `DefaultFolderRecipe.cs`; note any formats or destination names you want changed before the planner is built.
4. Launch the app and confirm Organize still states that scanning/classification are not connected to the UI and that it offers no file-changing control.
5. Do not provide an online AI key for this step. No network request should occur.

## V0.2 Step 3 — Side-Effect-Free Planner

1. Run the Release build and complete test suite. Expected result after this slice: 74 tests pass with none skipped.
2. Launch DeskAI and open Organize. Confirm it states that backend planning exists but no folder picker, preview, approval, or execution control is available.
3. Do not point the app or tests at a personal folder. The planner tests use in-memory synthetic paths only.
4. Review `OrganizationPlannerTests.cs`, particularly duplicate destination, existing destination, occupied directory, already-organized, and stable-ID scenarios.
5. Confirm that a planner conflict causes `PlanValidator.CanBeApproved` to be false in `PlanValidatorTests.cs`.
6. Confirm launching the app creates no visible file changes outside DeskAI's own Local App Data state.

## V0.2 Step 4 — Preview-Only Organize UI

1. Build Release and run the complete suite. Expected result: zero build warnings/errors and 74 passing tests with none skipped.
2. Close any older DeskAI window, launch the newly built Release executable, and select Organize.
3. Confirm the page says “Organize preview” and identifies `C:\DeskAI-Demo-Sandbox` as a virtual label only. Do not create that folder; the application does not need it.
4. Confirm proposed create-folder and move-file rows show source, destination, reason, provenance, and Allowed/Blocked status.
5. Confirm both moves to `Documents\course-notes.pdf` are Blocked and their checkboxes cannot be selected.
6. Clear the selection, select individual allowed rows, then choose “Select allowed.” Confirm the selected count follows the checkboxes and blocked rows stay unselected.
7. Choose “New revision.” Confirm the revision number increases and safe selections reset.
8. Review the issue list for a duplicate-destination conflict, an already-organized item, and an unclassified item.
9. Confirm the execution control remains disabled and says execution is unavailable. There is no approval or file-changing action.
10. Confirm the app never opens a folder picker and does not request an API key. Do not provide a personal folder for this slice.

## V0.2 Steps 5–6 — Controlled Demo Execution and SQLite v2

1. Close older DeskAI windows, build Release, run the complete test suite, and launch the new Release executable. Expected automated result: 84 tests pass with none skipped and zero build warnings/errors.
2. Open Organize. Confirm the page begins with Review → Choose → Try it and the green Practice mode message clearly says only sample files are used.
3. Leave the allowed operations selected. Confirm the duplicate `course-notes.pdf` rows remain blocked and unselected.
4. Select the button labeled with the exact number of actions, such as “Run 6 selected actions in safe demo.” While it runs, confirm plan controls and checkboxes lock.
5. Confirm the result says how many sample files were organized. Expand Technical details to see the generated temporary path; it must be under your Windows Temp directory and its final folder name must start with `DeskAI.Demo.`.
6. Do not move, rename, add, or replace the `.deskai-demo-root` marker. It is the executor's proof that it owns this dummy workspace.
7. Confirm the button cannot run the plan a second time and “New revision” is disabled after execution.
8. Inspect the generated folder only if you want: dummy spreadsheet and screenshot placeholders should have moved into recipe folders; both conflicting course-note files should remain at their original dummy locations.
9. Confirm Desktop, Downloads, Documents, Pictures, and cloud-sync folders were never selected and remain unchanged. Do not copy personal files into the demo.
10. Run `TemporaryDemoPlanExecutorTests` and review the overwrite, traversal, stale-approval, unknown-ID, marker-tampering, selection-scope, and temp-boundary refusal cases.
11. Run `SqliteDatabaseInitializerTests` and confirm fresh schema creation, version-1 upgrade, and root foreign-key enforcement pass. Schema v2 tables exist, but the app does not yet save plan/journal records; that belongs to step 7.

## V0.2 Step 7 — Activity Journal, Recovery, and Undo

1. Build Release and run the complete suite. Expected result: 90 passing tests, none skipped, and zero build warnings/errors.
2. Launch the new build and open Organize. Confirm Recent activity starts with a calm empty state or a truthful previous journal summary.
3. Run the selected sample-file organization. Confirm Recent activity says Sample organization and shows the completed file count.
4. Choose Undo demo. Confirm the result says the sample files returned to their original places, Recent activity changes to Undo, and the undo button cannot run twice.
5. In a fresh app session, run the demo again. Before Undo, edit the moved dummy spreadsheet inside the displayed temporary demo folder by appending harmless dummy text. Choose Undo and confirm DeskAI leaves the changed file where it is and reports that it could not safely restore it.
6. Do not place personal data in the demo folder. Do not modify or remove the ownership marker except in automated negative tests.
7. Run `TemporaryDemoPlanExecutorTests`; review write-ahead failure, journal outcome, successful undo, changed-file refusal, recovery, traversal, overwrite, and ownership tests.
8. Run `SqlitePlanningPersistenceTests`; confirm roots, plan operations/issues, and journal outcomes round-trip through schema version 3.
9. Restart recovery is conservative: an old interrupted session may show Needs review, but the new process does not access that old demo folder and cannot offer undo for it yet.

## V0.2 Step 8 — Read-Only Folder Preview

1. Build Release and run the complete suite. Expected result: 94 passing tests, none skipped, and zero build warnings/errors.
2. Create a new dummy folder under Windows Temp. Do not select Desktop, Downloads, Documents, Pictures, a cloud-sync folder, or any folder containing personal data.
3. Put two harmless dummy files in that temporary folder, including one inside a subfolder.
4. Launch DeskAI, open Organize, scroll to Preview a folder, and choose the dummy folder with the native Windows picker.
5. Read the confirmation. It must say DeskAI reads names, sizes, and dates only and cannot move, rename, delete, or read contents. Choose Allow read-only preview.
6. Expand the file count and confirm both dummy names appear with root-relative folder labels, sizes, and local dates. The UI is capped at depth 3 and 250 entries.
7. Confirm the practice organizer above still uses only its own generated sample files. The real selected folder has no organize/rename/delete button.
8. Choose Disconnect. Confirm the preview clears and both dummy files remain unchanged on disk.
9. Restart DeskAI. The disconnected folder must not appear as connected. If you leave it connected instead, startup may remember the permission label but does not rescan until you choose the folder again.
10. Run `ReadOnlyFolderServiceTests` and `PlanValidatorTests`; review the locked-file metadata test, protected-root refusal, non-mutating revocation, and metadata-only plan refusal.

## V0.3 — Optional AI and Privacy

1. Build Release and run the full suite. Expected: 129 tests pass, none skipped, with zero warnings/errors. Automated tests make no network call and do not write Windows Credential Manager.
2. Launch DeskAI and open Settings. Confirm “Privacy and AI” starts with AI off, Internet Off, nothing shared with online AI, and usage tracking Off.
3. Enable File name, choose Save privacy choices, and confirm the expansion dialog names that category. Disable it again if you do not want it available to cloud requests.
4. Open Organize and choose Get AI ideas while “Don't use AI” is active. Confirm it says AI is off and the practice organizer remains usable.
5. Optional local test: run a trusted OpenAI-compatible service yourself, select “AI running on this computer,” enter its local address and exact model name, then save. DeskAI must reject a non-local address. Do not use personal files; the AI card sends generated sample records only.
6. Optional OpenRouter test: in Settings choose “Online AI through OpenRouter,” enter an exact model name available to your OpenRouter account and your key, enable the agreement, and save. Read the second confirmation carefully. The key is stored in Windows Credential Manager, not the repository or database.
7. In Organize, read what may be shared, choose Get AI ideas once, and verify each result shows a category, confidence, explanation, and OpenRouter. It must not change selected moves or enable skipped items.
8. Test Stop by starting a request and selecting Stop. Lower the waiting limit under More options to exercise the timeout message. Do not repeatedly call a paid model; the daily request limit counts attempted online calls.
9. Return to Settings and choose Remove saved OpenRouter key. Confirm AI becomes off. Never paste the key into logs, screenshots, issues, chat, source files, or test configuration.
10. Review OpenRouter billing directly for exact cost. DeskAI reports token usage when OpenRouter supplies it but deliberately does not guess pricing.

## V0.4 Step 1 — Local Metadata Index

There is no index UI yet, and DeskAI does not index anything on its own. This step is
verified by tests and by confirming the app's behavior did not change.

1. Build Release and run the complete suite. Expected result: **171 passing tests**, none
   skipped, and zero build warnings/errors.
2. Launch the new Release build. Confirm Organize, Search, and Settings behave exactly as
   they did in V0.3 and that Search still honestly says no search index exists yet.
3. Confirm nothing new appears asking to scan a personal folder. Do not connect Desktop,
   Downloads, Documents, Pictures, or a cloud-sync folder.
4. Review `MetadataIndexServiceTests`, in particular the locked-file case (proving file
   contents are never opened), the protected-root and protected-child refusals, the
   entry-limit and cancellation cases, and the "forgets files that are no longer there"
   case.
5. Review `SqliteFileIndexTests`, in particular root isolation with identical file names,
   refusal of entries belonging to another root, refusal of an unauthorized root, and the
   assertion that no stored row contains an absolute path.
6. Review `SqliteDatabaseInitializerTests`. Confirm fresh creation reaches schema version
   7, that versions 1–7 are each recorded, that an existing database gains `indexed_files`
   without losing its authorized roots, and that deleting a root removes its indexed rows.
8. Optional database check: after launching the app once, open
   `%LocalAppData%\DeskAI\deskai.db` with a read-only SQLite viewer and confirm
   `indexed_files` exists and is **empty**. Nothing should be indexed until a later slice
   adds an explicit user action.

## V0.4 Step 7 — Organization Health Score

Use a folder of generated dummy files only. Never connect Desktop, Downloads, Documents,
Pictures, or a cloud-sync folder for this check.

1. Build Release and run the complete suite. Expected result: **360 passing tests**, none
   skipped, and zero build warnings/errors.
2. Launch the new Release build with nothing connected. Home must **not** show a health
   score at all — no zero, no full marks. A score from no evidence would be invented.
3. In Search, connect a temporary folder holding a few dummy files, then return to Home.
   Confirm a score out of 100 appears with a plain-language state such as "Looking tidy".
4. Confirm the two parts are listed under it — possible copies and sitting unused — each
   with what it measured, its own score out of 100, and how much it counts for. Check the
   total by hand: multiply each part by its weight, add them, and divide by 100. The number
   on the page must match.
5. Confirm there is **no** button offering to improve, fix, or clean up the score anywhere
   on the page. The score describes only.
6. Add two identical dummy files of the same size to the folder, refresh it in Search, and
   return to Home. Confirm the possible-copies part drops and the wording still says
   "possible" or "may", never that copies are confirmed.
7. Set a few dummy files to a date older than six months and refresh. Confirm the
   sitting-unused part moves only a little and the folder is **not** flagged for age alone.
   A settled archive is not a mess, and the score must never say otherwise.
8. Put a few files with unusual extensions DeskAI cannot know (`.qqq`, `.zzz`) in the
   folder and refresh. Confirm the score does **not** fall, and that a line beside it says
   how much of the folder DeskAI could recognise. An unrecognised type is this app's gap,
   never a charge against the person.
9. Disconnect the folder. Confirm the score disappears rather than lingering on remembered
   numbers from a folder DeskAI can no longer see.

Note: file categories are stored when a folder is indexed, so a widened rule set only
reaches a folder that is refreshed in Search afterwards.

## V0.4 Step 8, Stage 1 — Content-Access Gate

This slice is a permission gate with no UI. Nothing in the app can grant content access and
no file is opened, so it is verified by tests and by confirming nothing changed on screen.

1. Build Release and run the complete suite. Expected result: **373 passing tests**, none
   skipped, and zero build warnings/errors.
2. Launch the new Release build. Confirm Home, Organize, Search, Automatic tasks, and
   Privacy and AI all behave exactly as before. Nothing should offer to read inside files.
3. Confirm no new prompt asks for content permission. There must not be one yet.
4. Review `RootCapabilitiesTests`, in particular the whole scope matrix, that metadata
   consent is not content consent, that content access never becomes permission to change
   files, that a restricted or protected folder grants nothing at any scope, and that the
   stored scope numbers are pinned.
5. Review `PlanValidatorTests.Validate_BlocksEveryMutationForAScopeThatWasNotConnectedForChanges`.
   A folder connected for reading contents must still be refused every file change.
6. Read `docs/security/2026-09-09-content-access-gate-review.md`. Extraction itself is
   explicitly **not** accepted yet; that needs its own review before any file is opened.

## V0.4 Step 8, Stage 2 — Plain-Text Extraction

The first code in DeskAI that opens a file. It is registered nowhere and called by nothing,
so it cannot run in the app yet; this is verified by tests and by confirming nothing changed.

1. Build Release and run the complete suite. Expected result: **391 passing tests**, none
   skipped, and zero build warnings/errors.
2. Launch the new Release build. Confirm every page behaves exactly as before and that
   nothing anywhere offers to read inside files or asks for content permission.
3. Review `PlainTextExtractorTests`, in particular: the refusal for all three non-content
   scopes; that unsupported formats are refused without opening; that a long file is cut
   short and says so; that a file which is not really text is refused rather than decoded;
   that a path leaving the folder is refused; that a missing file stays missing; and that
   prompt-injection wording comes back as inert text.
4. Read `docs/security/2026-09-09-plain-text-extraction-review.md`. Confirm for yourself
   that sending extracted text to an AI provider, reading PDF or Office files, and storing
   extracted text are each listed as still **not** accepted.

## V0.4 Step 8, Stage 3 — Content Consent and Inside-File Search

Use a temporary folder of generated dummy files. Put a few `.txt` and `.md` files in it with
a distinctive word inside one of them, and give that file a name that does **not** contain
the word, so a match can only come from reading the contents.

1. Build Release and run the complete suite. Expected result: **413 passing tests**, none
   skipped, and zero build warnings/errors.
2. Connect the folder in Search. Confirm the connect dialog says DeskAI will not read what
   is inside your files unless you allow that separately afterwards.
3. Confirm the folder row says "Names, sizes, and dates only." and offers "Read inside
   files". Search for the distinctive word: there must be **no** "Found inside your files"
   section at all.
4. Choose "Read inside files". Confirm the dialog names the folder, says which kinds of file
   are opened, says PDFs, Word documents, spreadsheets, photos, and programs are not, says
   nothing read is saved or sent, and says files still cannot be moved, renamed, or deleted.
   Cancel it. Confirm the permission did **not** change.
5. Choose it again and allow. Confirm the row now says DeskAI can read inside the text files
   here, and that the navigation pane reminder now mentions the folder you let it read.
6. Search for the distinctive word again. Confirm the file appears under "Found inside your
   files" with a readable snippet, and that the message states how many files were opened.
7. Put the word in a `.pdf` or `.jpg` file as well and search again. It must **not** appear:
   those formats are never opened.
8. Choose "Stop reading inside". Confirm it happens immediately with no confirmation, the
   row returns to names, sizes, and dates, and searching finds nothing inside files.
9. Allow reading again, then choose "Disconnect". Confirm the folder disconnects properly —
   a permission you can give must stay one you can take back.

## Bring Your Own Key — Any Supported Service

1. Build Release and run the full suite. Expected: **202 passing tests**, none skipped,
   zero warnings/errors. No automated test contacts a real AI service or writes your
   Windows Credential Manager.
2. Open Settings. Confirm the main choice now reads "Online AI with my own key" rather
   than naming one company.
3. Select that option and expand "Online AI with your own key". Confirm a service list
   appears with OpenRouter, OpenAI, Groq, Mistral, DeepSeek, and Together AI.
4. Change the selected service without saving. Confirm the model example, the key box
   label, the agreement sentence, the pricing note, and the remove-key button **all**
   rename themselves to the service you picked.
5. Confirm there is no box anywhere to type a web address for online AI. That is
   deliberate — DeskAI only sends to the fixed address of the service you selected.
6. Optional live test with a service you already pay for: enter an exact model name and
   your key, turn on the agreement, and save. Read the confirmation dialog. It must name
   the service, list exactly what may be shared, and name the host that will receive it.
7. Open Organize and confirm the AI card names your chosen service, not "OpenRouter" and
   not a vague "the cloud".
8. Choose "Get AI ideas" once. Verify each result shows a category, confidence,
   explanation, and your service's name, and that it does not change or enable any move.
9. Return to Settings and choose the remove-key button. Confirm it names only the
   currently selected service and that AI becomes off afterwards.
10. If you use more than one service, confirm saving a key for a second service does not
    disturb the first: each is stored under its own Windows Credential Manager entry.
11. Never paste a key into logs, screenshots, issues, chat, source files, or test data.
    Check your provider's own billing page for exact cost; DeskAI reports token counts
    when the service supplies them and deliberately does not guess prices.

## Interface Refresh

1. Build Release and launch the new executable. Expected: **202 passing tests** and zero
   build warnings/errors.
2. Confirm the window has a Mica background and that the navigation pane footer shows a
   permanent green "Practice mode" note on every page.
3. Visit **all five pages** — Home, Organize, Search, Automatic tasks, Privacy and AI.
   None should show an empty box where an icon belongs, and none should fail to open.
4. On Organize, confirm each suggested move shows a rounded status badge with an icon
   **and** a word ("Ready" / "Not included"), never colour alone.
5. Confirm the two conflicting `course-notes.pdf` rows still show "Not included" with a
   red cross icon and that their checkboxes are still **not selectable**. Polish must not
   have made a blocked row look available.
6. Switch Windows between light and dark mode with DeskAI open. Confirm text stays
   readable on every card and on the coloured headers.
7. Turn on Windows high-contrast mode and revisit Organize and Privacy and AI. Confirm
   status badges and headers remain legible.
8. Tab through Organize and Privacy and AI using only the keyboard. Confirm focus is
   visible and reaches every checkbox, button, and expander.
9. On Search and Automatic tasks, confirm the pages plainly say the feature is not
   finished. The Search filter buttons must appear **disabled**, not clickable.
10. Confirm no page claims a capability that does not exist yet.
