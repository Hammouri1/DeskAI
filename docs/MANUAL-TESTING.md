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
   card, and a small "Nervous? Try it on example files first" link at the bottom.
2. Press **Choose another folder**, pick the test folder, and confirm. Expected: the folder is
   selected and a green-edged card asks "Allow DeskAI to tidy …?" with three promises.
3. Press **Allow tidying** and read the dialog. Cancel once and confirm nothing changed; then
   allow. Expected: suggestions grouped by folder (Documents, Pictures, Installers).
4. Open a group. Expected: every file shows why ("PDF file") and where it would go.
5. Untick a group, then one file. Expected: the big number and the button text follow.
6. Find `report.pdf`. Expected: "A file called report.pdf is already there" and a switch set
   to "Skip this file". Turn on "Keep both". Expected: it now goes to `report (2).pdf`.
7. Open **Left alone**. Expected: the `.crdownload` file with "still downloading".
8. Look at the Tidy button. Expected: switched off, with "Tidying arrives in the next update.
   Nothing moves yet". Check in File Explorer that **no file has moved**.
9. Press **Stop tidying this folder**. Expected: the permission card returns and the folder is
   still listed in Search.
10. Press the practice link, then **Back to Tidy a folder**. Expected: both pages work.
11. Press each new "?" on this page and check the explanations read clearly.

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
