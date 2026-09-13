# Owner Manual Testing

Use this checklist after each delivered slice. Never point development builds or tests at Desktop, Downloads, Documents, Pictures, cloud-sync folders, or other personal data until the roadmap explicitly enables a reviewed picker flow.

The practice page (sample files, "Run … in safe demo", "Get AI ideas") was retired on 2026-09-11 (ADR 0023). Older sections that walk through it — V0.2 steps 4–7, the V0.3 and Bring Your Own Key steps that press "Get AI ideas", and parts of Interface Refresh — are kept as history and can be skipped. Try AI with **Ask AI** on Tidy a folder instead.

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

## V0.5 — Rules and the Practice Run

Use a temporary folder of generated dummy files. Connect it in Search first, so the rules
have something to look at. Never connect a personal folder for this.

1. Build Release and run the complete suite. Expected result: **490 passing tests**, none
   skipped, and zero build warnings/errors.
2. Open **Automatic tasks**. Confirm the page says plainly that nothing runs on its own and
   that DeskAI cannot move a file from that page.
3. Write a rule: name it, set "when the name contains" to a word that appears in some of
   your dummy files, and set a destination such as `Sorted`. Save it. Confirm the rule
   appears written out as one sentence, and that the sentence matches what you typed.
4. Try to save a rule with a name but **no** conditions. Confirm it is refused with a plain
   explanation, not an error code — a rule with no conditions would match every file.
5. Try a destination of `..\..\Windows` or `C:\Windows`. Confirm it is refused.
6. Press **Try a practice run**. Confirm it lists the files that would move and where, and
   that it says nothing has moved. Check the folder on disk: **nothing has changed.**
7. Write a second rule matching the same files but with a different destination. Run the
   practice again. Those files must now appear under **Left alone**, with no proposal — two
   rules disagreeing means DeskAI refuses to guess.
8. Turn one of the two rules off and practise again. The conflict must resolve and the files
   move to the remaining rule's destination. Confirm the rule stays off after you close and
   reopen DeskAI.
9. Confirm there is **no** button anywhere on the page that carries a rule out. There must
   not be one: a rule still has to go through the ordinary preview and approval.
10. Delete a rule. Confirm it disappears and no file was touched.

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

## Automatic checks (V0.5, added 2026-09-10)

These need a real launch, because the timer and the Windows notification cannot be verified
by unit tests. Use a temporary folder with generated files — never a personal folder.

1. Open **Automatic tasks**. Confirm the first card says DeskAI never moves a file on its own,
   and that it no longer claims nothing runs in the background.
2. Confirm the **Checking for you** card shows "Every 15 minutes" and that both switches are
   off: notifications off, checks not paused.
3. Connect a temporary folder from Search, write a rule that matches something in it, then
   press **Check now**. Expected: a count appears and "Nothing has moved".
4. Set the frequency to **Only when I ask**, wait, and confirm the last-looked time does not
   advance on its own.
5. Set it back to every 15 minutes, add a matching file to the temporary folder, and leave
   DeskAI open. Within about 15 minutes the top-right notice should appear. Confirm it names
   a count and no filenames, and that **Review** navigates to Automatic tasks.
6. Turn **Tell me with a Windows notification** on and repeat step 5. Expected: a Windows
   notification carrying a count only. DeskAI runs unpackaged, so notifications may be
   unavailable on some machines — if none appears, the in-app notice must still work.
7. Turn on **Pause all automatic checks** while a check is running. Expected: it stops, and
   the summary sentence changes to say DeskAI is not looking on its own.
8. Close DeskAI entirely, add another matching file, and confirm nothing happens: no
   notification, no process left running, and nothing added to Windows startup.

### Check history

9. Open **Recent checks** under Checking for you. Confirm each past check shows a time and a
   plain sentence, and that one saying files matched also says nothing was moved.
10. Press **Check now**, then pause DeskAI mid-check if you can catch it. Confirm the stopped
    check still appears in the history rather than vanishing.
11. Close DeskAI for a while, reopen it, and wait for the first check. Confirm it is marked
    "First check after DeskAI was closed or paused" and that only **one** catch-up check
    appears, not one per missed period.
12. Press **Clear this history** and confirm the list empties and the button disappears.

## Tidy a folder (V0.6 step 2a, added 2026-09-10)

Use a new folder under Windows Temp filled with made-up files — for example a few `.pdf`,
`.jpg`, and `.exe` files, one `report.pdf` plus a `Documents\report.pdf`, and a file ending
`.crdownload`. Never test on a personal folder.

1. Open **Organize**. Expected: "Tidy a folder", a folder list or a "Pick a folder to tidy"
   card, and (since step 5) an open "How tidying works" card under the title.
2. Press **Choose another folder**, pick the test folder, and confirm. Expected: the folder is
   selected and a green-edged card asks "Allow DeskAI to tidy …?" with three promises.
3. Press **Allow tidying** and read the dialog. Cancel once and confirm nothing changed; then
   allow. Expected: suggestions grouped by folder (Documents, Pictures, Installers).
4. Open a group. Expected: every file shows why ("PDF file") and where it would go.
5. Untick a group, then one file. Expected: the big number and the button text follow.
6. Find `report.pdf`. Expected: "A file called report.pdf is already there" and a switch set
   to "Skip this file". Turn on "Keep both". Expected: it now goes to `report (2).pdf`.
7. Open **Left alone**. Expected: the `.crdownload` file with "still downloading".
8. (Step 2a only; since step 3 the button works — see the checklist below.) Check in File
   Explorer that opening the page and allowing tidying moved **no file**.
9. Press **Stop tidying this folder**. Expected: the permission card returns and the folder is
   still listed in Search.
10. (Retired in step 5: the practice link and page are gone.)
11. Press each new "?" on this page and check the explanations read clearly.

## Tidying for real (V0.6 step 3, added 2026-09-10)

This is the first step that really moves files. Use **only** a new folder under Windows Temp
filled with made-up files — for example `%TEMP%\DeskAI-Tidy-Test` holding `invoice.pdf`,
`notes.pdf`, `holiday.jpg`, `setup.exe`, `mystery.zzz`, and a subfolder `Old\keep.txt`. Put one
more file **next to** that folder (for example `%TEMP%\sentinel.txt`) and note its date. Never
test on Desktop, Downloads, Documents, Pictures, or a cloud-sync folder.

1. Open **Organize**, pick the test folder, and allow tidying. Expected: the Tidy button reads
   "Tidy 4 files" and the line under it says "Nothing moves until you press it. You can undo it."
2. Untick the **Installers** group. Expected: "Tidy 3 files".
3. Press **Tidy**. Expected: a card under the button: "Done. DeskAI-Tidy-Test: 3 files tidied into
   2 folders." and an **Undo** button. In File Explorer: `Documents` holds the two PDFs,
   `Pictures` holds the photo, `setup.exe` and `mystery.zzz` are still loose, `Old\keep.txt` is
   untouched, and `sentinel.txt` beside the folder is unchanged.
4. Look at the side menu. Expected: it says you have let DeskAI tidy 1 folder.
5. Press **Undo**. Expected: "Undone. 3 files went back where they were." In File Explorer:
   every file is back, and the `Documents` and `Pictures` folders DeskAI made are gone.
6. Create a `Documents` folder yourself, then tidy again and undo. Expected: your own
   `Documents` folder is **kept** even though it is empty again.
7. Tidy again, but before pressing Tidy open `notes.pdf` in a program that keeps it locked (or
   edit and save it). Expected: the result says "1 of 2 files …" or similar, and lists
   `notes.pdf` with a plain reason ("open in another program" or "changed after the list was
   made"). It stays where it was.
8. After a tidy, press **Stop tidying this folder**, then **Undo**. Expected: the permission
   dialog appears. Cancel: nothing moves. Press Undo again and allow: the files go back.
9. Put a file named `invoice.pdf` inside `Documents` yourself, then tidy with that file ticked
   and "Keep both" on. Expected: yours is untouched, and the other arrives as
   `invoice (2).pdf`.
10. Press the "?" next to **Undo** and check it reads clearly.
11. Close DeskAI after a tidy and reopen it. Since step 4 the last tidy is shown again with
    **Undo** — see the checklist below. Nothing moves when DeskAI opens.

## Undo after reopening, and interrupted tidies (V0.6 step 4, added 2026-09-11)

Use the same kind of made-up folder under Windows Temp as above, with a sentinel file next to it.
Never test on a personal folder.

1. Tidy the folder, close DeskAI completely, and open it again. Open **Organize**. Expected: the
   card under the Tidy button reads "Last tidy: N files tidied into N folders, at <time> on
   <date>." with **Undo**. Nothing has moved on its own.
2. Press **Undo**. Expected: "Undone. …" and the files back where they were. Close and reopen:
   the Last tidy line is gone.
3. Tidy again, press **Stop tidying this folder**, close and reopen DeskAI. Expected: the Last
   tidy line and the permission card both show. Press **Undo**: the permission dialog appears
   first. Cancel: nothing moves. Press Undo again and allow: the files go back.
4. Tidy twice in a row (add a new PDF between the two), undo the second, then close and reopen.
   Expected: **no** Last tidy line. DeskAI only ever offers the latest tidy.
5. Interrupted tidy — optional and only with made-up files: put about 300 small generated PDFs
   in the test folder, press Tidy, and end DeskAI from Task Manager while it is moving. Reopen
   and open Organize. Expected: a card "Your last tidy was interrupted: X of 300 files moved."
   with **Undo those X** and **Keep them**, and the Tidy button off with "Answer the question
   about your last tidy first." Count the files in `Documents` in File Explorer: it should be X.
   If a file is listed under the card as one to check, look for it where the card says.
6. Press **Keep them**. Expected: the card goes and the Last tidy line appears with Undo.
   Alternatively repeat step 5 and press **Undo those X**: the moved files come back and the card
   goes.
7. With one DeskAI window tidying the 300 files, open a second DeskAI window and press Tidy there
   on the same folder. Expected: the second window waits, and never moves the same files at the
   same time; if the first takes longer than about 30 seconds it says "DeskAI is busy tidying in
   another window."
8. Press the "?" next to the interrupted card's title and check it reads clearly.
9. After a tidy, disconnect the folder in **Search**. Expected: "Disconnected. Everything
   remembered about it has been forgotten." and the files stay exactly where the tidy put them.

## Checking for real copies (V0.4 step 6 stage 2, added 2026-09-11)

Use a new folder under Windows Temp with made-up files only: `a.txt` and `b.txt` with the same
text (at least 5 KB — paste a long line many times), `c.txt` the same length but one letter
different, and a copy of `a.txt` in a second temp folder. Never use personal files.

1. Connect both folders in **Search**, then open **Home**. Expected: "Possible duplicates" lists
   one group of 4 files, and a **Check if they're really copies** button with a "?".
2. Press the button. Expected: a dialog "Compare 4 files?" saying how much DeskAI would read, in
   2 folders, from beginning to end on this computer, and that nothing it reads is saved, sent,
   changed, moved, or deleted. Press **Cancel**. Expected: nothing changes on the page.
3. Press it again and **Compare**. Expected: a line such as "3 files are identical copies…
   DeskAI read 4 files… Nothing was saved, sent, or changed." and the group marked "Some
   identical", naming the three identical files and `c.txt` as "Not a copy of the others".
4. In File Explorer, check every file's "Date modified" is unchanged.
5. Open `c.txt` in a program that keeps it open for writing, then check again. Expected: `c.txt`
   listed as "Not checked" with "It's open in another program".
6. Edit `b.txt` without refreshing the folder in Search, then check again. Expected: `b.txt`
   "Not checked" with "It changed since DeskAI last looked".
7. Confirm there is no delete, remove, or keep-one button anywhere on Home.
8. Press the new "?" and check it reads clearly.

## V0.6 sign-off (added 2026-09-11)

The milestone review (`docs/security/2026-09-11-v0.6-milestone-review.md`) lists what automated
tests cannot prove. Before calling V0.6 done by hand, with made-up files under Windows Temp only:

1. Walk the three checklists above and below: Tidying for real, Undo after reopening and
   interrupted tidies, and Review in Organize.
2. End DeskAI from Task Manager during a tidy of a few hundred generated files, reopen, and answer
   the interrupted-tidy card both ways (on two separate runs).
3. Open two DeskAI windows and press Tidy in both on the same folder.
4. Do one whole tidy and undo using only the keyboard, then again with Narrator on.
5. Switch Windows between light, dark, and high contrast with Organize open, including the
   interrupted card and the How tidying works card.
6. If you use a cloud-sync client, put a generated file there, make it online-only, and check it
   is left alone. Never use real files for this.

## Review in Organize, and How tidying works (V0.6 step 5, added 2026-09-11)

Use two new folders under Windows Temp with made-up files: `Alpha` holding `invoice-a.pdf`, and
`Zeta` holding `invoice-b.pdf`, `invoice-c.pdf`, and `holiday.jpg`. Never use a personal folder.

1. On a fresh start with no folder allowed to be tidied, open **Organize**. Expected: an open
   "How tidying works" card under the title with four steps and a green-shield line saying DeskAI
   never deletes, never touches subfolders, and never moves anything out of the folder. There is
   no "Try it on example files" link, and no practice page anywhere.
2. Look at the side menu and Home with nothing connected. Expected: "Nothing connected yet", not
   "Practice mode". Home's first suggestion reads "Tidy a folder".
3. Connect both folders and allow tidying for both. Reopen Organize. Expected: the card is now
   closed; open it and close it — it stays as you left it while on the page.
4. In **Automatic tasks**, write a rule: name contains `invoice`, destination `Sorted`. Press
   **Check now**. Expected: the top-right notice "3 files match your rules. Nothing has moved."
   with **Review in Organize** and a "See it in Automatic tasks" link.
5. Press **Review in Organize**. Expected: Organize opens on **Zeta** (the folder with more
   matches) with the line "From your automatic check: your rules place 2 files here, marked
   "Your rule". Nothing moves until you press Tidy." and a **Sorted** group. In File Explorer,
   nothing has moved.
6. Press Check now again and, this time, **See it in Automatic tasks**. Expected: the Automatic
   tasks page, and nothing moved.
7. Press **Stop tidying this folder** on Zeta, check again, and press Review in Organize.
   Expected: Zeta with the permission card and a line asking you to allow tidying to see which
   files DeskAI would move.

## Ask AI on Tidy a folder (V0.6 step 2b, added 2026-09-10)

Use a new folder under Windows Temp with made-up files only: a `notes.pdf`, a `photo.jpg`, and
two files DeskAI cannot recognise, such as `mystery.zzz` and `oddity.qqq`. Never use a personal
folder. An online AI request costs a little of your own credit; one or two presses is enough.

1. With AI off in Privacy and AI, open **Organize**, pick the test folder, and allow tidying.
   Expected: an "Ask AI" card saying DeskAI doesn't know where 2 files go, the Ask button
   switched off, "Turn on AI in Privacy and AI first.", and the "Every file my rules don't
   place" choice switched off.
2. In Privacy and AI, turn on your AI service with only **File type** shared. Come back to
   Organize. Expected: the caption reads "<your service> would see: file types."
3. Press **Ask AI about 2 files**. Expected: a dialog titled "Send this to <your service>?",
   naming its web address, with two lines "A .zzz file" and "A .qqq file" — **no file names**
   — and a sentence saying contents, locations, and folder names are never sent.
4. Press **Cancel**. Expected: nothing changes, and no answer line appears.
5. Press it again and **Send**. Expected: a line under the button such as "OpenRouter suggested
   a place for 2 of 2 files. Nothing has moved." Files it placed show "AI idea from
   <service>". If it was unsure about any, they are in an "AI isn't sure" group at the end,
   with a question-mark icon, unticked. No percentages anywhere.
6. Check File Explorer: **no file has moved**.
7. Choose **Every file my rules don't place**. Expected: the button now offers `notes.pdf` and
   `photo.jpg` too. Switch back and confirm the list returns to file types.
8. In Privacy and AI, turn on **Full location on your computer**, ask again, and confirm the
   dialog still shows no location or folder name. Turn that switch off again afterwards.
9. Start a request and press **Stop** if you can catch it. Expected: the answer line says it
   stopped, and nothing else is tried.
10. Press the new "?" next to **Ask AI** and check it reads clearly and truthfully.

## Help pop-ups (V0.6 step 1, added 2026-09-10)

1. On every page, find the small round **?** buttons: Home (score, space, possible
   duplicates — these appear once a folder is connected), Organize (Practice mode), Search
   (title, saved searches, folders, what search can do, found inside files), Automatic tasks
   (checking for you, how often, pause, notifications, your rules, practice run, read my
   sentence), Privacy and AI (what online AI may see, choose how AI works, daily limit under
   More options, where your key is kept), and the reminder at the bottom of the side menu.
2. Press each one. Expected: a pop-up with the title, "What it is", "What it does", and a
   green-edged "What it never does". It should read as plain, friendly English with no
   technical words.
3. Press **Esc** or click elsewhere. Expected: the pop-up closes.
4. Use **Tab** to reach a "?" and press **Enter** or **Space**. Expected: it opens.
5. Hover a "?". Expected: the tooltip "What is this?".
6. With Narrator on, focus a "?". Expected: it reads "Help:" followed by the feature name.
7. Switch Windows between dark and light mode (and high contrast if you can). Expected: the
   pop-up stays readable in each.
8. Nothing in any pop-up should be untrue about what DeskAI can do today. Report any sentence
   that promises more than the app does.

## Checking after the window is closed (V0.5, added 2026-09-13)

None of this is covered by an automated test — the icon near the clock, the window hiding,
the second launch, and the Explorer restart all depend on Windows itself, and only a person
at the keyboard can see them happen. Use a temporary folder with generated files and a rule
that matches one of them, the same way as "Automatic checks" above. Never use a personal
folder.

### Turning it on and off

1. Open **Automatic tasks** and turn on **Keep checking after I close the window**. Expected:
   a dialog appears, states DeskAI will never add itself to Windows startup, and offers a
   notification checkbox.
2. Turn it on again three separate times and dismiss the dialog a different way each time:
   press **Enter**, press **Esc**, and click the dialog's **X**. Expected: all three behave
   like pressing "No thanks" — the switch goes back to Off and no icon appears.
3. Turn it on and press **Keep running**. Expected: the icon appears near the clock
   **immediately, while the window is still open** — not only once you close it.
4. Look at the dialog itself in both light and dark Windows themes. Expected: the checkbox
   and its caption are readable, match DeskAI's other dialogs, and nothing is cut off.
5. Turn the switch off from the page. Expected: the icon disappears at once, with no need to
   close the window first.

### Closing and reopening the window

6. With the mode on, close the window. Expected: DeskAI keeps running — check Task Manager —
   and a one-time notice tells you it is still near the clock and how to quit it.
7. Close the window again in the same run. Expected: the notice does **not** appear a second
   time.
8. Reopen DeskAI from the icon and close the window once more. Expected: the notice appears
   again, since this is a new run.
9. With the mode on, turn the switch off, then close the window immediately — click the X
   right away, without clicking anything else or opening another page first. Expected: DeskAI
   really exits at once. Check Task Manager: no `DeskAI.App.exe` left running, and no
   notification claims it is still near the clock, because there is no icon to be near it.
10. With the mode off, close the window. Expected: DeskAI really exits — nothing left in Task
    Manager and no icon.

### The icon and its menu

11. Hover the icon. Expected: it says how often DeskAI is looking, or that it is paused, and
    never shows a file or folder name.
12. With the mode on, close the window, wait for one whole checking interval to pass, then
    hover the icon again. Expected: the wording or count has changed, proving checks keep
    happening with no window open.
13. Right-click the icon. Expected: exactly three items, in this order — **Open DeskAI**,
    **Pause checking**, **Quit DeskAI** — with **Open DeskAI** shown bold as the default.
    There is no "Check now" and nothing here starts anything.
14. Left-click the icon, and separately choose **Open DeskAI** from the menu. Expected: both
    bring the window to the front.
15. Open the menu, then click elsewhere on the desktop. Expected: the menu closes without
    choosing anything.
16. Launch DeskAI, choose **Pause checking** from the icon without ever opening **Automatic
    tasks**, then open DeskAI. Expected: the page already shows checking as paused — the icon
    and the page never disagree.
17. Pause from the icon, then unpause from the page (and the reverse). Expected: the tick
    beside **Pause checking** always matches what the page says.
18. Choose **Quit DeskAI**. Expected: the icon disappears and `DeskAI.App.exe` is gone from
    Task Manager — no ghost icon left behind.

### Launching again, Explorer, and signing out

19. With the mode on and DeskAI hidden, launch DeskAI again (Start menu or the `.exe`).
    Expected: the existing window is revealed and brought to the front, and Task Manager
    still shows only one `DeskAI.App.exe`.
20. With the mode off and DeskAI's window already open and visible, launch DeskAI again.
    Expected: the second launch quietly exits and does nothing visible — this is expected,
    since the window is already on screen, not a bug.
21. Turn notifications on, let a check find something while the window is hidden, and click
    the Windows notification. Expected: DeskAI's window is revealed.
22. With DeskAI hidden, restart Explorer (Task Manager → Windows Explorer → Restart).
    Expected: the icon comes back near the clock afterwards.
23. Start a shutdown or sign-out with DeskAI hidden and cancel it before it finishes.
    Expected: the icon is still there afterwards, rather than being permanently gone.
24. Sign out and back in, or restart Windows. Expected: DeskAI does **not** start on its
    own — it only runs again once you open it yourself.
