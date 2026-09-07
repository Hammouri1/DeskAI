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
5. Do not provide a Gemini/OpenRouter key for this step. No network request should occur.

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
