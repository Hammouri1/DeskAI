# Desktop Studio Step 3: Clear old stuff and Folder by group — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two new Desktop Studio cards that move real things on the connected Desktop, each with a
preview of tick boxes, an explicit Move, and a Put back that survives restarts: **Clear old stuff**
(everything unchanged for 6 months goes into one "Old stuff" folder) and **Folder by group** (each
group on the Find groups board goes into its own folder).

**Architecture:** The executor learns one new action, **move a folder** (`MoveFolderOperation`,
one `Directory.Move`, so a folder is never half-moved), with the same write-ahead journal, live
re-checks, interrupted-run check, and undo as file moves. A plan now records which feature made
it (`PlanPurpose`), and only a Desktop Studio plan may move a folder. Moving things on the Desktop
needs its **own yes** (`folder_move_permissions`), never the tidy permission: allowing tidying
promised "never touches files in subfolders", and widening that promise silently would weaken an
authorization people already gave. Core gets a read-only `DesktopInventoryService`, a pure
`DesktopMovePlanner`, and a `DesktopMoveService` that joins them to the executor. The page gets
two cards built on those.

**Tech Stack:** C# / .NET 10, WinUI 3, CommunityToolkit.Mvvm, SQLite (Microsoft.Data.Sqlite),
xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-24-desktop-studio-design.md` ("Disk features: Clear old
stuff, Folder by group, Tag names"; "Build order" step 3; "Safety summary"; "Testing").

## Global Constraints

- "They use the existing Tidy flow: … a preview with one tick box per item, approval, the one
  executor, the write-ahead journal, interrupted-run recovery, and **Put back** (undo) that
  survives restarts. The preview states the total, for example '3 folders holding 1,204 files'."
- "New executor actions, each with live re-checks just before acting: **move a folder** (within
  the same connected Desktop, which is a single rename in Windows, so a folder is never
  half-moved)."
- "**Unticked by default, with a warning:** folders that look like active projects (`.git`,
  `.sln`, `package.json`, a Python environment), folders containing programs (`.exe`), and folders
  with online-only files."
- "**Left alone, with the reason shown:** a same-name clash (never overwritten or merged), a file
  in use, an item changed since the preview, protected or link items."
- "**Clear old stuff** uses the same 6-month age as Home's 'unused' reading
  (`StorageSummaryService.OldFileAge`) and the item's last-changed date."
- "**Tidy while I'm away never moves or renames folders.** It stays limited to loose files."
- "Moving and renaming folders widen the executor. They get an ADR and a security review tracing
  every threat case to a test." "Nothing is deleted, ever."
- "Put back undoes only that card's own last change." "Anything that runs without the person
  pressing Apply" is out of scope.
- AI plays no part in either card. Folder by group reads the board the person already saw and
  could change; no request is sent.
- Plain words on the page (CLAUDE.md "User Experience"); no "journal", "plan", "executor".
- Tests use generated folders only (TestApp's sandbox Desktop, `TemporaryDirectory`). The real
  Desktop is never touched.
- Every completed task is committed locally on `desktop-studio-find-groups`; nothing is pushed
  until the whole Desktop Studio feature is done (owner, 2026-09-24).
- Verification: `dotnet build DeskAI.sln -c Release --no-restore`,
  `dotnet test DeskAI.sln -c Release --no-build --no-restore`,
  `dotnet format DeskAI.sln --no-restore --verify-no-changes`.

## Decisions this plan makes (recorded in ADR 0044, Task 1)

1. **A separate yes for moving things on the Desktop.** The spec says "the 'Allow tidying'
   permission", but that dialog promises DeskAI "never touches files in subfolders" (Organize
   and My workspace). Moving a folder moves everything inside it. Reusing the grant would change
   what people already agreed to, which CLAUDE.md forbids ("Never weaken authorization"). So
   `RootCapabilities.CanMoveFolders` is its own grant, asked with its own dialog, taken back with
   its own Stop, and never implied by tidying (or the other way round).
2. **Put back is offered only for the latest change on the Desktop.** "Only that card's own last
   change" holds: a card never undoes another card's run. When something ran after it, its Put
   back is no longer offered, as Organize's Undo already behaves ("undoing out of order is not
   what undo means").
3. **A folder is "old" only when everything found inside it is old and the look reached all the
   way inside.** A folder the look could not finish is left alone with the reason.
4. **Folder identity is its made-at time.** A move keeps a folder's creation time; a replacement
   folder with the same name has a different one. Before moving, the folder's own last-changed
   time must also match the preview (it changes when something directly inside is added, removed,
   or renamed). Put back needs only the same folder: things added inside later go back with it.
5. **Protected and link items are not listed at all,** as on the Find groups board (ADR 0042),
   rather than listed as "left alone": naming DeskAI's own program folder or a protected entry adds
   nothing for the person. Things the safety rules refuse only at the destination are listed as
   left alone, with the reason.

## Review Focus

1. **A folder holding DeskAI's own program folder, or any protected or link entry** → never
   listed, never moved. Pinned in Task 2 (`PlanValidatorTests.A_folder_holding_a_protected_entry_cannot_be_moved`),
   Task 5 (`DesktopInventoryServiceTests.Hidden_protected_and_linked_things_are_left_out`), Task 7
   (`DesktopStudioMovePageTests.DeskAIs_program_folder_and_hidden_things_never_appear_or_move`).
2. **A file inside the folder is open in another program** → Windows refuses the rename; the
   folder stays and the reason says so. Pinned in Task 4
   (`FolderTidyExecutorTests.A_folder_with_a_file_open_in_another_program_stays_where_it_is`).
3. **DeskAI stops while a folder is moving** (crash, power) → on the next open the page asks,
   having checked the disk; Put them back returns it; nothing is guessed. Pinned in Task 4
   (`An_interrupted_folder_move_is_checked_against_the_disk`) and Task 7
   (`An_interrupted_move_is_asked_about_and_Put_them_back_returns_it`).
4. **The folder was replaced, or its name reused, between Move and Put back** → Put back refuses
   rather than moving the wrong folder. Pinned in Task 4
   (`A_folder_replaced_after_the_move_is_not_moved_back`).
5. **Tidy permission given earlier under the "never touches subfolders" promise** → it does not
   let Desktop Studio move anything. Pinned in Task 4
   (`A_folder_move_needs_its_own_yes_tidying_is_not_enough`) and Task 6
   (`DesktopMoveServiceTests.Moving_needs_its_own_yes_and_that_yes_is_not_tidying`).

---

## File Structure

| File | Responsibility |
|---|---|
| `docs/decisions/0044-desktop-studio-moves-things.md` | The decision: folder moves, the separate yes, plan purpose, latest-only Put back |
| `docs/security/2026-09-24-desktop-moves-review.md` | Threat table, each row traced to a test |
| `src/DeskAI.Core/Plans/PlanOperation.cs` | + `MoveFolderOperation`, `PlanOperationKind.MoveFolder` |
| `src/DeskAI.Core/Plans/PlanPurpose.cs` | Which feature made a plan |
| `src/DeskAI.Core/Plans/OrganizationPlan.cs` | + `Purpose`; only Studio plans may move folders |
| `src/DeskAI.Core/Execution/ExpectedFile.cs`, `ExecutionJournalEntry.cs` | + folder made-at time, record purpose |
| `src/DeskAI.Core/Roots/AuthorizedRoot.cs`, `RootCapabilities.cs` | + `FolderMovesAllowedSinceUtc`, `CanMoveFolders` |
| `src/DeskAI.Core/Files/ScanEvent.cs` | + folder dates on `FolderDiscovered` |
| `src/DeskAI.Core/Abstractions/IFolderMovePermissions.cs` | Grant / take back the separate yes |
| `src/DeskAI.Safety/PlanValidator.cs` | Validates folder moves; a folder never goes inside itself |
| `src/DeskAI.Infrastructure/Persistence/SqliteDatabaseInitializer.cs` | Schema 17 |
| `src/DeskAI.Infrastructure/Persistence/SqlitePlanRepository.cs`, `SqliteOperationJournal.cs`, `SqliteAuthorizedRootRepository.cs` | Store and read the new facts |
| `src/DeskAI.Infrastructure/Persistence/SqliteFolderMovePermissions.cs` | The separate yes in SQLite |
| `src/DeskAI.Infrastructure/Scanning/WindowsMetadataScanner.cs` | Reports folder dates |
| `src/DeskAI.Infrastructure/Execution/FileOperationRunner.cs`, `FolderTidyExecutor.cs` | Move a folder, put it back, check it after a stop; permission by purpose |
| `src/DeskAI.Core/Studio/DesktopInventory.cs` | Read-only look at what is on the Desktop, with dates and warnings |
| `src/DeskAI.Core/Studio/DesktopMovePlanner.cs` | What each card would move; texts |
| `src/DeskAI.Core/Studio/DesktopMoveService.cs` | Preview, Move, Put back, the yes, an interrupted run |
| `src/DeskAI.Core/Tidy/TidyRunService.cs` | Organize's Undo ignores Studio runs |
| `src/DeskAI.Presentation/ViewModels/DesktopMoveCardViewModel.cs` | One card's rows, total, and last change |
| `src/DeskAI.Presentation/ViewModels/DesktopStudioViewModel.cs` | The cards, the yes, the interrupted question |
| `src/DeskAI.App/Views/DesktopStudioPage.xaml(.cs)` | Two cards, the permission dialog |
| `src/DeskAI.Presentation/Help/HelpCatalog.cs` | Two help topics |
| Tests: `tests/DeskAI.Core.Tests/{OrganizationPlanTests,RootCapabilitiesTests,DesktopInventoryServiceTests,DesktopMovePlannerTests}.cs`, `tests/DeskAI.Safety.Tests/PlanValidatorTests.cs`, `tests/DeskAI.Infrastructure.Tests/{SqliteDatabaseInitializerTests,SqlitePlanningPersistenceTests,SqliteAuthorizedRootRepositoryTests,WindowsMetadataScannerTests,FolderTidyExecutorTests}.cs`, `tests/DeskAI.Presentation.Tests/{DesktopMoveServiceTests,DesktopStudioMovePageTests,DesktopStudioLayoutTests}.cs` | |

---

### Task 1: The decision and the security review

**Files:**
- Create: `docs/decisions/0044-desktop-studio-moves-things.md`
- Create: `docs/security/2026-09-24-desktop-moves-review.md`

**Interfaces:** documents only. The test names in the review table are the exact names later
tasks create; keep them identical.

- [ ] **Step 1: Write ADR 0044**

```markdown
# ADR 0044: Desktop Studio Moves Things on the Desktop

- Status: Accepted
- Date: 2026-09-24
- Review: `docs/security/2026-09-24-desktop-moves-review.md`
- Design: `docs/superpowers/specs/2026-09-24-desktop-studio-design.md` (step 3)

## Context

Clear old stuff and Folder by group move folders and files on the connected Desktop. Until now
the executor moved only files, and the tidy permission's dialog promises that DeskAI "never
touches files in subfolders". Moving a folder moves everything inside it.

## Decision

- **Move a folder** is a new plan action, `MoveFolderOperation`, carried out as one
  `Directory.Move` inside the same connected folder, so it is never half-moved. It has the file
  move's checks: written to the journal first, the folder's permission and path re-checked before
  each action, no link on the way, not hidden or system, the same folder as in the preview (same
  made-at time and own last-changed time), destination free, never inside itself, never
  overwriting. Windows' refusal while something inside is open is reported in plain words.
- **Put back** moves a folder back only if it is still the same folder (same made-at time) and its
  old place is free. Things added inside since go back with it.
- **After a stop,** a folder move under way is checked against the disk by made-at time: gone from
  the start and found at the end is "moved"; still at the start is "not moved"; anything else is
  "needs review" and is never moved on a guess.
- **A plan records which feature made it** (`PlanPurpose`: Tidy, ClearOldStuff, FolderByGroup).
  Only a non-Tidy plan may contain a folder move, so Organize, folder templates, and Tidy while
  I'm away can never move a folder.
- **A separate yes.** Desktop Studio's moves need `folder_move_permissions`, asked with its own
  dialog and taken back with its own Stop. The tidy permission does not grant it, and it does not
  grant tidying. The executor checks the grant that matches the plan's purpose, before the run and
  before every action.
- **Latest-only Put back.** A card offers Put back only for the latest change on the Desktop, and
  only if that change was its own.
- Nothing is deleted. The only folder ever removed is an empty one DeskAI itself made in that run,
  on Put back, as templates already do.

## Consequences

Schema 17 adds `organization_plans.purpose`, `execution_operation_journal.before_created_at_utc`,
and `folder_move_permissions` (cascading with the folder). An Organize Undo is no longer offered
once a Desktop Studio change ran after it, the same rule Organize already applies to templates.
```

- [ ] **Step 2: Write the security review**

```markdown
# Desktop Studio Moves Security Review

- Date: 2026-09-24
- Scope: ADR 0044 — move a folder, the separate yes, Clear old stuff, Folder by group
- Result: accepted

| Threat | Control | Test |
|---|---|---|
| An earlier tidy yes (promising "never touches subfolders") is used to move folders | Separate grant; executor checks the grant for the plan's purpose before the run and each action | `FolderTidyExecutorTests.A_folder_move_needs_its_own_yes_tidying_is_not_enough`, `DesktopMoveServiceTests.Moving_needs_its_own_yes_and_that_yes_is_not_tidying` |
| The Studio yes lets Organize tidy | Tidy plans still need the tidy grant | `FolderTidyExecutorTests.A_tidy_plan_on_a_folder_with_only_the_move_yes_moves_nothing` |
| Organize, templates, or Tidy while I'm away move a folder | `OrganizationPlan.CreateDraft` refuses a folder move in a Tidy plan | `OrganizationPlanTests.A_tidy_plan_cannot_move_a_folder` |
| The yes is taken back part-way | Re-checked before every action | `FolderTidyExecutorTests.Taking_back_the_yes_part_way_stops_the_remaining_folder_moves` |
| A folder holding DeskAI's program folder or a protected entry moves | Path policy blocks a path that contains a protected entry; the inventory leaves out any top-level folder holding one | `PlanValidatorTests.A_folder_holding_a_protected_entry_cannot_be_moved`, `DesktopInventoryServiceTests.Hidden_protected_and_linked_things_are_left_out`, `DesktopStudioMovePageTests.DeskAIs_program_folder_and_hidden_things_never_appear_or_move` |
| A folder is moved into itself or out of the Desktop | Validator refuses both | `PlanValidatorTests.A_folder_cannot_be_moved_into_itself`, `PlanValidatorTests.A_folder_move_cannot_leave_the_folder` |
| A folder changed after the preview is moved | Made-at and own last-changed time re-checked | `FolderTidyExecutorTests.A_folder_changed_since_the_list_is_left_where_it_is`, `DesktopStudioMovePageTests.A_folder_changed_after_the_list_stays_where_it_is` |
| Something with the same name is overwritten or merged | Destination checked; `Directory.Move` never overwrites; planner leaves clashes alone | `FolderTidyExecutorTests.A_folder_is_never_moved_onto_something_with_the_same_name`, `DesktopStudioMovePageTests.An_Old_stuff_folder_already_there_is_used_and_kept_and_a_clash_is_left_alone` |
| A folder half-moved, or moved while in use | One rename; Windows' refusal reported | `FolderTidyExecutorTests.A_folder_with_a_file_open_in_another_program_stays_where_it_is` |
| Put back moves a different folder with the same name | Made-at time must match | `FolderTidyExecutorTests.A_folder_replaced_after_the_move_is_not_moved_back` |
| A stop part-way leaves an unknown state | Disk check by made-at time; needs-review never moved | `FolderTidyExecutorTests.An_interrupted_folder_move_is_checked_against_the_disk`, `DesktopStudioMovePageTests.An_interrupted_move_is_asked_about_and_Put_them_back_returns_it` |
| A card undoes another feature's change, or out of order | Latest-only, purpose-matched Put back; Organize ignores Studio runs | `DesktopMoveServiceTests.Put_back_is_offered_only_for_the_latest_change_on_the_Desktop`, `DesktopStudioMovePageTests.Organize_does_not_offer_to_undo_a_Desktop_Studio_change` |
| A project, program, or online-only folder is moved without a thought | Unticked with a warning | `DesktopMovePlannerTests.Warned_folders_start_unticked`, `DesktopStudioMovePageTests.A_project_folder_starts_unticked_with_its_warning` |
| A folder is called old from a partial look | Left alone with the reason | `DesktopMovePlannerTests.Clear_old_stuff_leaves_a_clash_and_an_unfinished_look_alone` |
| AI influences a move | No AI in either card; Folder by group reads the saved board only | `DesktopMoveServiceTests.Folder_by_group_sends_nothing` |
| Something is deleted | Only an empty folder DeskAI made in that run is removed on Put back | `DesktopStudioMovePageTests.An_Old_stuff_folder_already_there_is_used_and_kept_and_a_clash_is_left_alone` |

No new Windows setting, registry, network, or AI capability is added.
```

- [ ] **Step 3: Commit**

```bash
git add docs/decisions/0044-desktop-studio-moves-things.md docs/security/2026-09-24-desktop-moves-review.md
git commit -m "Decide how Desktop Studio moves folders, with its own permission, and review it"
```

---

### Task 2: Folder moves, plan purpose, and the separate yes in Core and Safety

**Files:**
- Modify: `src/DeskAI.Core/Plans/PlanOperation.cs`, `src/DeskAI.Core/Plans/OrganizationPlan.cs`
- Create: `src/DeskAI.Core/Plans/PlanPurpose.cs`, `src/DeskAI.Core/Abstractions/IFolderMovePermissions.cs`
- Modify: `src/DeskAI.Core/Execution/ExpectedFile.cs`, `src/DeskAI.Core/Execution/ExecutionJournalEntry.cs`
- Modify: `src/DeskAI.Core/Roots/AuthorizedRoot.cs`, `src/DeskAI.Core/Roots/RootCapabilities.cs`
- Modify: `src/DeskAI.Core/Files/ScanEvent.cs`
- Modify: `src/DeskAI.Safety/PlanValidator.cs`
- Test: `tests/DeskAI.Core.Tests/OrganizationPlanTests.cs`, `tests/DeskAI.Core.Tests/RootCapabilitiesTests.cs`, `tests/DeskAI.Safety.Tests/PlanValidatorTests.cs`

**Interfaces:**
- Produces (namespace `DeskAI.Core.Plans`): `sealed record MoveFolderOperation(Guid Id, string SourceRelativePath, string DestinationRelativePath, string Reason, OperationProvenance Provenance)`; `PlanOperationKind.MoveFolder` (= 3); `enum PlanPurpose { Tidy, ClearOldStuff, FolderByGroup }`; `OrganizationPlan.Purpose`; `OrganizationPlan.CreateDraft(..., IEnumerable<PlanIssue>? issues = null, PlanPurpose purpose = PlanPurpose.Tidy)`.
- Produces (`DeskAI.Core.Execution`): `ExpectedFile.CreatedAtUtc { get; init; }` (DateTimeOffset?); `OperationJournalEntry.BeforeCreatedAtUtc { get; init; }` (DateTimeOffset?); `ExecutionJournalEntry.Purpose { get; init; }` (PlanPurpose).
- Produces (`DeskAI.Core.Roots`): `AuthorizedRoot.FolderMovesAllowedSinceUtc`, `AuthorizedRoot.WithFolderMovesAllowedSince(DateTimeOffset?)`, `RootCapabilities.CanMoveFolders(AuthorizedRoot)`.
- Produces (`DeskAI.Core.Files`): `FolderDiscovered(string RelativePath, FileTraits Traits, DateTimeOffset? CreatedAtUtc = null, DateTimeOffset? ModifiedAtUtc = null)`.
- Produces (`DeskAI.Core.Abstractions`): `interface IFolderMovePermissions { Task AllowAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default); Task StopAsync(Guid rootId, CancellationToken cancellationToken = default); }`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/DeskAI.Core.Tests/OrganizationPlanTests.cs` (inside the class):

```csharp
    [Fact]
    public void A_tidy_plan_cannot_move_a_folder()
    {
        var move = new MoveFolderOperation(Guid.NewGuid(), "Old project", @"Old stuff\Old project", "Unchanged for 6 months", OperationProvenance.Heuristic);

        Assert.Throws<ArgumentException>(() => OrganizationPlan.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "1", [move]));
    }

    [Fact]
    public void A_desktop_studio_plan_may_move_a_folder_and_keeps_its_purpose()
    {
        var move = new MoveFolderOperation(Guid.NewGuid(), "Old project", @"Old stuff\Old project", "Unchanged for 6 months", OperationProvenance.Heuristic);

        var plan = OrganizationPlan.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "1", [move], purpose: PlanPurpose.ClearOldStuff);

        Assert.Equal(PlanPurpose.ClearOldStuff, plan.Purpose);
        Assert.Equal(PlanOperationKind.MoveFolder, Assert.Single(plan.Operations).Kind);
    }

    [Fact]
    public void A_plan_made_without_a_purpose_is_a_tidy() =>
        Assert.Equal(PlanPurpose.Tidy, OrganizationPlan.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "1", []).Purpose);
```

Append to `tests/DeskAI.Core.Tests/RootCapabilitiesTests.cs` (inside the class):

```csharp
    [Fact]
    public void Moving_folders_is_its_own_yes_apart_from_tidying()
    {
        var reading = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Desktop", "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);
        var tidyOnly = reading.WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
        var movesOnly = reading.WithFolderMovesAllowedSince(DateTimeOffset.UnixEpoch);

        Assert.False(RootCapabilities.CanMoveFolders(reading));
        Assert.False(RootCapabilities.CanMoveFolders(tidyOnly));
        Assert.True(RootCapabilities.CanMoveFolders(movesOnly));
        Assert.False(RootCapabilities.CanTidy(movesOnly));
        Assert.True(RootCapabilities.CanMutate(movesOnly));

        // Changing one yes keeps the other exactly as it was.
        Assert.Equal(DateTimeOffset.UnixEpoch, movesOnly.WithTidyAllowedSince(null).FolderMovesAllowedSinceUtc);
        Assert.Equal(DateTimeOffset.UnixEpoch, tidyOnly.WithFolderMovesAllowedSince(null).TidyAllowedSinceUtc);
    }

    [Theory]
    [InlineData(RootAuthorizationScope.ControlledDemo)]
    [InlineData(RootAuthorizationScope.Organize)]
    public void A_folder_move_yes_means_nothing_outside_a_folder_connected_for_reading(RootAuthorizationScope scope) =>
        Assert.False(RootCapabilities.CanMoveFolders(
            AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\X", "X", RootAccessLevel.Allowed, scope)
                .WithFolderMovesAllowedSince(DateTimeOffset.UnixEpoch)));
```

Append to `tests/DeskAI.Safety.Tests/PlanValidatorTests.cs` (inside the class):

```csharp
    [Fact]
    public void A_folder_move_inside_the_folder_is_allowed()
    {
        var root = FolderMoveRoot();

        Assert.True(new PlanValidator(new WindowsPathPolicy())
            .Validate(FolderMovePlan(root, "Old project", @"Old stuff\Old project"), root).CanBeApproved);
    }

    [Fact]
    public void A_folder_cannot_be_moved_into_itself()
    {
        var root = FolderMoveRoot();

        var report = new PlanValidator(new WindowsPathPolicy())
            .Validate(FolderMovePlan(root, "Projects", @"Projects\Archive\Projects"), root);

        Assert.False(report.CanBeApproved);
        Assert.Contains(report.Operations, item => item.Result.Explanation == "A folder cannot be moved into itself.");
    }

    [Fact]
    public void A_folder_holding_a_protected_entry_cannot_be_moved()
    {
        var root = FolderMoveRoot();
        var validator = new PlanValidator(new WindowsPathPolicy(userProtectedEntries: [@"C:\DeskAITests\Desktop\DeskAI\app"]));

        Assert.False(validator.Validate(FolderMovePlan(root, "DeskAI", @"Old stuff\DeskAI"), root).CanBeApproved);
    }

    [Fact]
    public void A_folder_move_cannot_leave_the_folder()
    {
        var root = FolderMoveRoot();

        Assert.False(new PlanValidator(new WindowsPathPolicy())
            .Validate(FolderMovePlan(root, "Old project", @"..\Elsewhere\Old project"), root).CanBeApproved);
    }

    private static AuthorizedRoot FolderMoveRoot() =>
        AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Desktop", "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly)
            .WithFolderMovesAllowedSince(DateTimeOffset.UnixEpoch);

    private static OrganizationPlan FolderMovePlan(AuthorizedRoot root, string from, string to) =>
        OrganizationPlan.CreateDraft(
            Guid.NewGuid(), root.Id, 1, DateTimeOffset.UnixEpoch, PlanValidator.CurrentPolicyVersion,
            [new MoveFolderOperation(Guid.NewGuid(), from, to, "Unchanged for 6 months", OperationProvenance.Heuristic)],
            purpose: PlanPurpose.ClearOldStuff);
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build DeskAI.sln -c Release --no-restore`
Expected: build errors — `MoveFolderOperation`, `PlanPurpose`, `WithFolderMovesAllowedSince`, `CanMoveFolders`, `FolderMovesAllowedSinceUtc`, and the `purpose:` parameter do not exist.

- [ ] **Step 3: Write the Core types**

In `src/DeskAI.Core/Plans/PlanOperation.cs`, add after `RenameFileOperation`:

```csharp
/// <summary>
/// Moves a whole folder, and everything in it, to another place inside the same connected
/// folder (ADR 0044). Windows does it as one rename, so a folder is never half-moved.
/// </summary>
/// <remarks>Only a Desktop Studio plan may hold one; <see cref="OrganizationPlan.CreateDraft"/> refuses it in any other.</remarks>
public sealed record MoveFolderOperation(
    Guid Id,
    string SourceRelativePath,
    string DestinationRelativePath,
    string Reason,
    OperationProvenance Provenance)
    : PlanOperation(Id, Reason, Provenance)
{
    public override PlanOperationKind Kind => PlanOperationKind.MoveFolder;
}
```

and change the enum to:

```csharp
public enum PlanOperationKind
{
    CreateDirectory,
    MoveFile,
    RenameFile,

    /// <summary>A whole folder (ADR 0044). Stored as a number, so appended last.</summary>
    MoveFolder,
}
```

Create `src/DeskAI.Core/Plans/PlanPurpose.cs`:

```csharp
namespace DeskAI.Core.Plans;

/// <summary>
/// Which feature made a plan, so each feature offers undo only for its own runs and only Desktop
/// Studio may move a folder (ADR 0044).
/// </summary>
/// <remarks>Stored as a number: append only, never reorder.</remarks>
public enum PlanPurpose
{
    Tidy,
    ClearOldStuff,
    FolderByGroup,
}
```

In `src/DeskAI.Core/Plans/OrganizationPlan.cs`: add a `PlanPurpose purpose` parameter at the end of
the private constructor, assign it to a new property placed after `State`:

```csharp
    /// <summary>Which feature made the plan (ADR 0044). Plans made before it existed are tidies.</summary>
    public PlanPurpose Purpose { get; }
```

Change `CreateDraft`'s signature to end with
`IEnumerable<PlanIssue>? issues = null, PlanPurpose purpose = PlanPurpose.Tidy)`, add these checks
right after the duplicate-ID check:

```csharp
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        // Organize, folder templates, and Tidy while I'm away promise never to touch what is
        // inside a folder. Only Desktop Studio's own yes covers moving one (ADR 0044).
        if (purpose == PlanPurpose.Tidy && operationList.Any(operation => operation is MoveFolderOperation))
        {
            throw new ArgumentException("Only Desktop Studio may move a folder.", nameof(operations));
        }
```

and pass `purpose` as the last argument of the `new OrganizationPlan(...)` call.

Replace `src/DeskAI.Core/Execution/ExpectedFile.cs` with:

```csharp
namespace DeskAI.Core.Execution;

/// <summary>
/// How a file or folder looked when the list the person approved was made.
/// </summary>
/// <remarks>
/// Checked again right before it moves. A file whose size or last-changed time differs, or a
/// folder whose made-at or own last-changed time differs, is not what the person reviewed, so it
/// is left where it is.
/// </remarks>
public sealed record ExpectedFile(long SizeBytes, DateTimeOffset ModifiedAtUtc)
{
    /// <summary>For a folder: when it was made. A move keeps it, so it tells the same folder from a replacement (ADR 0044). Null for a file.</summary>
    public DateTimeOffset? CreatedAtUtc { get; init; }
}
```

In `src/DeskAI.Core/Execution/ExecutionJournalEntry.cs`, replace the trailing `;` of
`ExecutionJournalEntry` with:

```csharp
{
    /// <summary>Which feature made the plan behind this record (ADR 0044).</summary>
    public PlanPurpose Purpose { get; init; }
}
```

and the trailing `;` of `OperationJournalEntry` with:

```csharp
{
    /// <summary>For a moved folder: when it was made, which a move keeps (ADR 0044). Null for files.</summary>
    public DateTimeOffset? BeforeCreatedAtUtc { get; init; }
}
```

In `src/DeskAI.Core/Roots/AuthorizedRoot.cs`: add `DateTimeOffset? folderMovesAllowedSinceUtc` as the
last private-constructor parameter, assigned to:

```csharp
    /// <summary>
    /// When the person allowed Desktop Studio to move things here, whole folders included, or null
    /// (ADR 0044).
    /// </summary>
    /// <remarks>
    /// Its own yes, apart from tidying: allowing tidying promised that DeskAI never touches what is
    /// inside a folder, so that yes can never be read as this one. Ask
    /// <see cref="RootCapabilities.CanMoveFolders"/>.
    /// </remarks>
    public DateTimeOffset? FolderMovesAllowedSinceUtc { get; }
```

Replace `WithTidyAllowedSince` with the pair below, and pass `null, null` in `Create`:

```csharp
    public AuthorizedRoot WithTidyAllowedSince(DateTimeOffset? sinceUtc) =>
        new(Id, CanonicalPath, DisplayName, Permission, AuthorizationScope, sinceUtc, FolderMovesAllowedSinceUtc);

    public AuthorizedRoot WithFolderMovesAllowedSince(DateTimeOffset? sinceUtc) =>
        new(Id, CanonicalPath, DisplayName, Permission, AuthorizationScope, TidyAllowedSinceUtc, sinceUtc);
```

In `src/DeskAI.Core/Roots/RootCapabilities.cs`, change `CanMutate`'s last arm to
`_ => CanTidy(root) || CanMoveFolders(root),` and add after `CanTidy`:

```csharp
    /// <summary>May Desktop Studio move things in this folder, whole folders included (ADR 0044)?</summary>
    /// <remarks>
    /// A separate yes from <see cref="CanTidy"/> and never implied by it: allowing tidying
    /// promised that DeskAI never touches what is inside a folder. The scope list is explicit,
    /// like tidying's, so a scope added later cannot inherit it by falling through.
    /// </remarks>
    public static bool CanMoveFolders(AuthorizedRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return IsUsable(root) && root.FolderMovesAllowedSinceUtc is not null && root.AuthorizationScope switch
        {
            RootAuthorizationScope.MetadataOnly => true,
            RootAuthorizationScope.MetadataAndContent => true,
            RootAuthorizationScope.MetadataAndDocuments => true,
            RootAuthorizationScope.MetadataDocumentsAndPdf => true,
            RootAuthorizationScope.MetadataDocumentsAndSlides => true,
            RootAuthorizationScope.MetadataDocumentsPdfAndSlides => true,
            _ => false,
        };
    }
```

In `src/DeskAI.Core/Files/ScanEvent.cs`, replace the `FolderDiscovered` line and its summary with:

```csharp
/// <summary>A folder the scan came across, entered or not. Reported so a caller can list empty folders too.</summary>
/// <param name="CreatedAtUtc">When it was made; moving it keeps this (ADR 0044). Null when not known.</param>
/// <param name="ModifiedAtUtc">Its own last-changed time, which moves when something directly inside is added, removed, or renamed.</param>
public sealed record FolderDiscovered(
    string RelativePath,
    FileTraits Traits,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? ModifiedAtUtc = null) : ScanEvent;
```

Create `src/DeskAI.Core/Abstractions/IFolderMovePermissions.cs`:

```csharp
namespace DeskAI.Core.Abstractions;

/// <summary>
/// The separate yes that lets Desktop Studio move things in one connected folder, whole folders
/// included (ADR 0044). It neither needs nor grants the tidy permission.
/// </summary>
public interface IFolderMovePermissions
{
    /// <summary>
    /// Records the yes. Ignored for any folder not connected for reading, so it can never reach
    /// the practice workspace or a legacy organize folder.
    /// </summary>
    Task AllowAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Takes the yes back. The folder stays connected; tidying is not affected.</summary>
    Task StopAsync(Guid rootId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Teach the validator folder moves**

In `src/DeskAI.Safety/PlanValidator.cs`, add a switch arm before the `_ =>` arm of `ValidateOperation`:

```csharp
            MoveFolderOperation folder => ValidateFolderMove(root, folder),
```

and add below `Combine`:

```csharp
    /// <summary>
    /// Both ends must pass the path policy, which also refuses a folder that holds a protected
    /// entry, and a folder may never go inside itself.
    /// </summary>
    private ValidationResult ValidateFolderMove(AuthorizedRoot root, MoveFolderOperation move)
    {
        var paths = Combine(
            pathPolicy.ValidateRelativePath(root, move.SourceRelativePath),
            pathPolicy.ValidateRelativePath(root, move.DestinationRelativePath));
        if (paths.Status == ValidationStatus.Blocked)
        {
            return paths;
        }

        var source = Trimmed(move.SourceRelativePath);
        var destination = Trimmed(move.DestinationRelativePath);
        return string.Equals(destination, source, StringComparison.OrdinalIgnoreCase) ||
               destination.StartsWith(source + '\\', StringComparison.OrdinalIgnoreCase)
            ? ValidationResult.Blocked(ValidationReasonCode.InvalidOperation, "A folder cannot be moved into itself.")
            : paths;
    }

    private static string Trimmed(string relativePath) => relativePath.Replace('/', '\\').Trim('\\');
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet build DeskAI.sln -c Release --no-restore` then
`dotnet test DeskAI.sln -c Release --no-build --no-restore`
Expected: 0 warnings, all tests pass (the new ones included). If a switch over
`PlanOperationKind` elsewhere now warns about a missing case, it is handled in Tasks 3–4; a build
error there means this step is not done — add the arm named in those tasks now.

- [ ] **Step 6: Commit**

```bash
git add src/DeskAI.Core src/DeskAI.Safety tests/DeskAI.Core.Tests tests/DeskAI.Safety.Tests
git commit -m "Add folder moves, plan purpose, and a separate yes for moving folders"
```

---

### Task 3: Storing the new facts, schema 17, and folder dates from the scanner

**Files:**
- Modify: `src/DeskAI.Infrastructure/Persistence/SqliteDatabaseInitializer.cs`
- Modify: `src/DeskAI.Infrastructure/Persistence/SqlitePlanRepository.cs`, `SqliteOperationJournal.cs`, `SqliteAuthorizedRootRepository.cs`
- Create: `src/DeskAI.Infrastructure/Persistence/SqliteFolderMovePermissions.cs`
- Modify: `src/DeskAI.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: `src/DeskAI.Infrastructure/Scanning/WindowsMetadataScanner.cs`
- Test: `tests/DeskAI.Infrastructure.Tests/SqliteDatabaseInitializerTests.cs`, `SqlitePlanningPersistenceTests.cs`, `SqliteAuthorizedRootRepositoryTests.cs`, `WindowsMetadataScannerTests.cs`

**Interfaces:**
- Consumes: everything Task 2 produces.
- Produces: `SqliteDatabaseInitializer.CurrentSchemaVersion == 17`; `SqliteFolderMovePermissions : IFolderMovePermissions` registered as a singleton; roots read by `FindAsync`/`ListAsync` carry `FolderMovesAllowedSinceUtc`; stored plans keep `Purpose` and `MoveFolder`; journal entries keep `BeforeCreatedAtUtc` and read `Purpose` from their plan; `FolderDiscovered` from the real scanner carries both dates.

- [ ] **Step 1: Write the failing tests**

In `tests/DeskAI.Infrastructure.Tests/SqliteDatabaseInitializerTests.cs`: change the expected
migration list `"1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16"` to
`"1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17"`, change the last test's
`Assert.Equal(16L, ...)` to `Assert.Equal((long)SqliteDatabaseInitializer.CurrentSchemaVersion, ...)`,
and append:

```csharp
    [Fact]
    public async Task Schema_17_adds_the_plan_purpose_the_folder_date_and_the_move_yes_and_can_run_twice()
    {
        using var sandbox = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db");
        var initializer = new SqliteDatabaseInitializer(
            Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
            new SystemClock(),
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('organization_plans') WHERE name = 'purpose';";
        Assert.Equal(1L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('execution_operation_journal') WHERE name = 'before_created_at_utc';";
        Assert.Equal(1L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        // The yes goes with its folder.
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            INSERT INTO authorized_roots(id, canonical_path, display_name, permission, created_at_utc, authorization_scope)
            VALUES ('99999999-9999-9999-9999-999999999999', 'C:\Sandbox\Desktop', 'Desktop', 0, '2026-09-24T00:00:00Z', 0);
            INSERT INTO folder_move_permissions(root_id, granted_at_utc)
            VALUES ('99999999-9999-9999-9999-999999999999', '2026-09-24T00:00:00Z');
            DELETE FROM authorized_roots WHERE id = '99999999-9999-9999-9999-999999999999';
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        command.CommandText = "SELECT COUNT(*) FROM folder_move_permissions;";
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
```

Append to `tests/DeskAI.Infrastructure.Tests/SqlitePlanningPersistenceTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task A_folder_move_its_made_at_time_and_the_plan_purpose_survive_saving()
    {
        using var sandbox = new TemporaryDirectory();
        var options = Options.Create(new DatabaseOptions { DatabasePath = Path.Combine(sandbox.Path, "deskai.db") });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);
        var root = AuthorizedRoot.Create(Guid.NewGuid(), sandbox.Path, "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);
        await new SqliteAuthorizedRootRepository(options, new SystemClock()).SaveAsync(root, TestContext.Current.CancellationToken);
        var move = new MoveFolderOperation(Guid.NewGuid(), "Old project", @"Old stuff\Old project", "Unchanged for 6 months", OperationProvenance.Heuristic);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, "1", [move], purpose: PlanPurpose.ClearOldStuff);
        var plans = new SqlitePlanRepository(options);
        await plans.SaveAsync(plan, TestContext.Current.CancellationToken);
        var madeAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var journal = new SqliteOperationJournal(options);
        var record = new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Prepared, DateTimeOffset.UtcNow, null,
            [new OperationJournalEntry(0, move.Id, PlanOperationKind.MoveFolder, move.SourceRelativePath, move.DestinationRelativePath,
                null, madeAt.AddDays(1), JournalOperationState.Pending, null) { BeforeCreatedAtUtc = madeAt }]);
        await journal.CreateAsync(record, TestContext.Current.CancellationToken);

        var storedPlan = await plans.FindAsync(plan.Id, 1, TestContext.Current.CancellationToken);
        var storedRecord = await journal.FindAsync(record.Id, TestContext.Current.CancellationToken);

        Assert.Equal(PlanPurpose.ClearOldStuff, storedPlan!.Purpose);
        Assert.IsType<MoveFolderOperation>(Assert.Single(storedPlan.Operations));
        Assert.Equal(PlanPurpose.ClearOldStuff, storedRecord!.Purpose);
        Assert.Equal(madeAt, Assert.Single(storedRecord.Operations).BeforeCreatedAtUtc);
    }
```

Append to `tests/DeskAI.Infrastructure.Tests/SqliteAuthorizedRootRepositoryTests.cs` (inside the
class; it already has `using` lines for `Options`, `SqliteDatabaseInitializer`, and `SystemClock`
— add any that are missing):

```csharp
    [Fact]
    public async Task The_move_yes_is_separate_from_tidying_and_only_for_folders_connected_for_reading()
    {
        using var sandbox = new TemporaryDirectory();
        var options = Options.Create(new DatabaseOptions { DatabasePath = Path.Combine(sandbox.Path, "deskai.db") });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);
        var roots = new SqliteAuthorizedRootRepository(options, new SystemClock());
        var moves = new SqliteFolderMovePermissions(options);
        var desktop = AuthorizedRoot.Create(Guid.NewGuid(), Path.Combine(sandbox.Path, "Desktop"), "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);
        var practice = AuthorizedRoot.Create(Guid.NewGuid(), Path.Combine(sandbox.Path, "Practice"), "Practice", RootAccessLevel.Allowed, RootAuthorizationScope.ControlledDemo);
        await roots.SaveAsync(desktop, TestContext.Current.CancellationToken);
        await roots.SaveAsync(practice, TestContext.Current.CancellationToken);

        await moves.AllowAsync(desktop.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        await moves.AllowAsync(practice.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        var allowed = await roots.FindAsync(desktop.Id, TestContext.Current.CancellationToken);
        Assert.Equal(DateTimeOffset.UnixEpoch, allowed!.FolderMovesAllowedSinceUtc);
        Assert.Null(allowed.TidyAllowedSinceUtc);
        Assert.Null((await roots.FindAsync(practice.Id, TestContext.Current.CancellationToken))!.FolderMovesAllowedSinceUtc);
        Assert.Contains(await roots.ListAsync(TestContext.Current.CancellationToken), root => root.Id == desktop.Id && root.FolderMovesAllowedSinceUtc is not null);

        // Stopping tidying leaves the move yes; its own Stop ends it.
        await roots.AllowTidyAsync(desktop.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        await roots.StopTidyAsync(desktop.Id, TestContext.Current.CancellationToken);
        Assert.NotNull((await roots.FindAsync(desktop.Id, TestContext.Current.CancellationToken))!.FolderMovesAllowedSinceUtc);
        await moves.StopAsync(desktop.Id, TestContext.Current.CancellationToken);
        Assert.Null((await roots.FindAsync(desktop.Id, TestContext.Current.CancellationToken))!.FolderMovesAllowedSinceUtc);
    }
```

Append to `tests/DeskAI.Infrastructure.Tests/WindowsMetadataScannerTests.cs` (inside the class;
follow the file's existing way of building a scanner and a root — the calls below use the names
this file already uses for them; if they differ, use the file's own helpers):

```csharp
    [Fact]
    public async Task A_folder_is_reported_with_when_it_was_made_and_last_changed()
    {
        using var sandbox = new TemporaryDirectory();
        var rootPath = sandbox.CreateDummyDirectory("Root");
        var folder = sandbox.CreateDummyDirectory(@"Root\Old project");
        var made = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var changed = new DateTime(2025, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        Directory.SetCreationTimeUtc(folder, made);
        Directory.SetLastWriteTimeUtc(folder, changed);
        var root = AuthorizedRoot.Create(Guid.NewGuid(), rootPath, "Root", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);

        var found = new List<ScanEvent>();
        await foreach (var scanEvent in new WindowsMetadataScanner(new WindowsPathPolicy())
                           .ScanAsync(root, new MetadataScanOptions(2, 100), TestContext.Current.CancellationToken))
        {
            found.Add(scanEvent);
        }

        var reported = Assert.Single(found.OfType<FolderDiscovered>());
        Assert.Equal(new DateTimeOffset(made), reported.CreatedAtUtc);
        Assert.Equal(new DateTimeOffset(changed), reported.ModifiedAtUtc);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build DeskAI.sln -c Release --no-restore`
Expected: build error — `SqliteFolderMovePermissions` does not exist. (After Step 3 adds it, the
tests fail on the missing column, table, and dates.)

- [ ] **Step 3: Schema 17 and the stores**

In `SqliteDatabaseInitializer.cs`: set `CurrentSchemaVersion = 17`, call
`await ApplyDesktopMovesMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);`
after `ApplyDesktopGroupMigrationAsync`, and add:

```csharp
    /// <summary>
    /// Desktop Studio moving things (ADR 0044): which feature made each plan, when a moved folder
    /// was made, and the separate yes to move things, cascading with its folder.
    /// </summary>
    /// <remarks>
    /// Existing plans become tidies (0), which is what they are. SQLite has no "add column if
    /// missing", so each column is looked for first; running this twice changes nothing.
    /// </remarks>
    private static async Task ApplyDesktopMovesMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('organization_plans') WHERE name = 'purpose';";
        var hasPurpose = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L) > 0;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('execution_operation_journal') WHERE name = 'before_created_at_utc';";
        var hasCreated = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L) > 0;

        command.CommandText = (hasPurpose ? string.Empty : "ALTER TABLE organization_plans ADD COLUMN purpose INTEGER NOT NULL DEFAULT 0;\n")
            + (hasCreated ? string.Empty : "ALTER TABLE execution_operation_journal ADD COLUMN before_created_at_utc TEXT NULL;\n")
            + """
            CREATE TABLE IF NOT EXISTS folder_move_permissions (
                root_id        TEXT NOT NULL PRIMARY KEY REFERENCES authorized_roots(id) ON DELETE CASCADE,
                granted_at_utc TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (17, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
```

In `SqlitePlanRepository.cs`:
- the plan `INSERT` becomes `INSERT INTO organization_plans(id, revision, root_id, created_at_utc, policy_version, state, purpose) VALUES ($id, $revision, $root, $created, $policy, $state, $purpose)` with `planCommand.Parameters.AddWithValue("$purpose", (int)plan.Purpose);`
- `FindAsync` selects `root_id, created_at_utc, policy_version, purpose`, reads `var purpose = (PlanPurpose)reader.GetInt32(3);` into a local declared beside `policy`, and returns `OrganizationPlan.CreateDraft(planId, rootId, revision, created, policy, operations, issues, purpose)`;
- `ReadOperationsAsync` gets the arm `PlanOperationKind.MoveFolder => new MoveFolderOperation(id, source!, destination, reason, provenance),`;
- `Paths` gets the arm `MoveFolderOperation folder => (folder.SourceRelativePath, folder.DestinationRelativePath),`.

In `SqliteOperationJournal.cs`:
- `CreateAsync`'s operation `INSERT` adds the column `before_created_at_utc` and value `$created`, with `command.Parameters.AddWithValue("$created", operation.BeforeCreatedAtUtc?.ToString("O") ?? (object)DBNull.Value);`
- `FindAsync`'s transaction query becomes:

```sql
SELECT t.plan_id, t.plan_revision, t.approval_id, t.state,
       t.started_at_utc, t.finished_at_utc, u.original_transaction_id, p.purpose
FROM execution_transactions t
LEFT JOIN undo_transaction_links u ON u.undo_transaction_id = t.id
LEFT JOIN organization_plans p ON p.id = t.plan_id AND p.revision = t.plan_revision
WHERE t.id = $id;
```

  with a local `PlanPurpose purpose;` set by `purpose = reader.IsDBNull(7) ? PlanPurpose.Tidy : (PlanPurpose)reader.GetInt32(7);`
- the operations query adds `before_created_at_utc` as column 9, and each entry is built with
  `{ BeforeCreatedAtUtc = reader.IsDBNull(9) ? null : ParseTimestamp(reader.GetString(9)) }`
  after the constructor call;
- the returned entry gets `{ Purpose = purpose }` after its constructor call.

In `SqliteAuthorizedRootRepository.cs`:
- `FindAsync` query: `SELECT r.canonical_path, r.display_name, r.permission, r.authorization_scope, t.granted_at_utc, f.granted_at_utc FROM authorized_roots r LEFT JOIN tidy_permissions t ON t.root_id = r.id LEFT JOIN folder_move_permissions f ON f.root_id = r.id WHERE r.id = $id;` and the result gets `.WithFolderMovesAllowedSince(ReadGrant(reader, 5))` after `.WithTidyAllowedSince(ReadGrant(reader, 4))`;
- `ListAsync` the same with columns shifted: `t.granted_at_utc` is 5, `f.granted_at_utc` is 6.

Create `src/DeskAI.Infrastructure/Persistence/SqliteFolderMovePermissions.cs`:

```csharp
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>The separate yes to move things in a connected folder (ADR 0044), in its own table.</summary>
public sealed class SqliteFolderMovePermissions(IOptions<DatabaseOptions> options) : IFolderMovePermissions
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task AllowAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Selected through the reading scopes, like the tidy grant: a yes for any other folder
        // simply inserts nothing.
        command.CommandText = """
            INSERT OR IGNORE INTO folder_move_permissions(root_id, granted_at_utc)
            SELECT id, $granted FROM authorized_roots
            WHERE id = $id AND authorization_scope IN ($metadataScope, $contentScope, $documentScope, $pdfScope, $slideScope, $pdfSlideScope);
            """;
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        command.Parameters.AddWithValue("$granted", grantedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$metadataScope", (int)RootAuthorizationScope.MetadataOnly);
        command.Parameters.AddWithValue("$contentScope", (int)RootAuthorizationScope.MetadataAndContent);
        command.Parameters.AddWithValue("$documentScope", (int)RootAuthorizationScope.MetadataAndDocuments);
        command.Parameters.AddWithValue("$pdfScope", (int)RootAuthorizationScope.MetadataDocumentsAndPdf);
        command.Parameters.AddWithValue("$slideScope", (int)RootAuthorizationScope.MetadataDocumentsAndSlides);
        command.Parameters.AddWithValue("$pdfSlideScope", (int)RootAuthorizationScope.MetadataDocumentsPdfAndSlides);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM folder_move_permissions WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

In `ServiceCollectionExtensions.cs`, next to the `IAuthorizedRootRepository` registration:
`services.AddSingleton<IFolderMovePermissions, SqliteFolderMovePermissions>();`

In `WindowsMetadataScanner.cs`, the folder line becomes:

```csharp
                    yield return new FolderDiscovered(
                        NormalizeRelativePath(relativePath),
                        ToTraits(attributes.Value),
                        new DateTimeOffset(entry.CreationTimeUtc),
                        new DateTimeOffset(entry.LastWriteTimeUtc));
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet build DeskAI.sln -c Release --no-restore` then
`dotnet test DeskAI.sln -c Release --no-build --no-restore`
Expected: 0 warnings; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/DeskAI.Infrastructure tests/DeskAI.Infrastructure.Tests
git commit -m "Store folder moves, plan purpose, and the move yes (schema 17); scan folder dates"
```

---

### Task 4: The executor moves a folder whole, puts it back, and checks it after a stop

**Files:**
- Modify: `src/DeskAI.Infrastructure/Execution/FileOperationRunner.cs`
- Modify: `src/DeskAI.Infrastructure/Execution/FolderTidyExecutor.cs`
- Test: `tests/DeskAI.Infrastructure.Tests/FolderTidyExecutorTests.cs`

**Interfaces:**
- Consumes: Task 2's types, Task 3's storage.
- Produces: `IFolderTidyExecutor.ExecuteAsync` runs `MoveFolderOperation`; `UndoAsync` moves a folder back; `CheckInterruptedAsync` settles an interrupted folder move; a plan's purpose decides the grant (`Tidy` → `CanTidy`, anything else → `CanMoveFolders`); refusal texts `"DeskAI may not move things here, so nothing was moved."`, `"DeskAI may no longer move things here, so it stopped."`, `"Putting things back moves them too, so DeskAI needs your permission to move things on your Desktop again."`; journal records carry `Purpose`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/DeskAI.Infrastructure.Tests/FolderTidyExecutorTests.cs` (inside the class, before
the private helpers):

```csharp
    [Fact]
    public async Task A_folder_moves_whole_and_undo_brings_it_back_with_what_was_added()
    {
        using var sandbox = new TemporaryDirectory();
        var desktop = sandbox.CreateDummyDirectory("Desktop");
        sandbox.CreateDummyDirectory(@"Desktop\Old stuff");
        sandbox.CreateDummyFile(@"Desktop\Old project\notes.txt");
        var root = MovesAllowed(desktop);
        var executor = Create(new DisconnectingRootRepository(root, int.MaxValue), new WindowsPathPolicy(), sandbox);
        var move = FolderMove("Old project", @"Old stuff\Old project");
        var plan = StudioPlan(root, move);

        var result = await executor.ExecuteAsync(
            plan, Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = FolderFacts(Path.Combine(desktop, "Old project")) },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(Path.Combine(desktop, "Old stuff", "Old project", "notes.txt")));
        Assert.False(Directory.Exists(Path.Combine(desktop, "Old project")));

        // Something added inside after the move goes back with the folder.
        File.WriteAllText(Path.Combine(desktop, "Old stuff", "Old project", "added later.txt"), "Generated DeskAI test data");
        var undo = await executor.UndoAsync(result.TransactionId, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, Assert.Single(undo.Operations).Outcome);
        Assert.True(File.Exists(Path.Combine(desktop, "Old project", "notes.txt")));
        Assert.True(File.Exists(Path.Combine(desktop, "Old project", "added later.txt")));
    }

    [Fact]
    public async Task A_folder_move_needs_its_own_yes_tidying_is_not_enough()
    {
        using var sandbox = new TemporaryDirectory();
        var desktop = sandbox.CreateDummyDirectory("Desktop");
        sandbox.CreateDummyDirectory(@"Desktop\Old stuff");
        sandbox.CreateDummyFile(@"Desktop\Old project\notes.txt");
        var root = Allowed(desktop);
        var executor = Create(new DisconnectingRootRepository(root, int.MaxValue), new WindowsPathPolicy(), sandbox);
        var move = FolderMove("Old project", @"Old stuff\Old project");
        var plan = StudioPlan(root, move);

        var result = await executor.ExecuteAsync(
            plan, Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = FolderFacts(Path.Combine(desktop, "Old project")) },
            TestContext.Current.CancellationToken);

        var outcome = Assert.Single(result.Operations);
        Assert.Equal(ExecutionOutcome.Failed, outcome.Outcome);
        Assert.Equal("DeskAI may not move things here, so nothing was moved.", outcome.Error);
        Assert.True(Directory.Exists(Path.Combine(desktop, "Old project")));
    }

    [Fact]
    public async Task A_tidy_plan_on_a_folder_with_only_the_move_yes_moves_nothing()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var file = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var root = MovesAllowed(folder);
        var executor = Create(new DisconnectingRootRepository(root, int.MaxValue), new WindowsPathPolicy(), sandbox);
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        var result = await executor.ExecuteAsync(
            plan, Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(file) },
            TestContext.Current.CancellationToken);

        Assert.Equal("DeskAI may not tidy this folder, so nothing was moved.", Assert.Single(result.Operations).Error);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Taking_back_the_yes_part_way_stops_the_remaining_folder_moves()
    {
        using var sandbox = new TemporaryDirectory();
        var desktop = sandbox.CreateDummyDirectory("Desktop");
        sandbox.CreateDummyDirectory(@"Desktop\Old stuff");
        sandbox.CreateDummyFile(@"Desktop\First\a.txt");
        sandbox.CreateDummyFile(@"Desktop\Second\b.txt");
        var root = MovesAllowed(desktop);
        // Looked up at the start, before the run, and before each folder: the yes is gone before the second.
        var roots = new ChangingRootRepository(root, root.WithFolderMovesAllowedSince(null), switchAfter: 3);
        var executor = Create(roots, new WindowsPathPolicy(), sandbox);
        var first = FolderMove("First", @"Old stuff\First");
        var second = FolderMove("Second", @"Old stuff\Second");
        var plan = StudioPlan(root, first, second);

        var result = await executor.ExecuteAsync(
            plan, Approval.Create(Guid.NewGuid(), plan, [first.Id, second.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile>
            {
                [first.Id] = FolderFacts(Path.Combine(desktop, "First")),
                [second.Id] = FolderFacts(Path.Combine(desktop, "Second")),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Operations[0].Outcome);
        Assert.Equal("DeskAI may no longer move things here, so it stopped.", result.Operations[1].Error);
        Assert.True(Directory.Exists(Path.Combine(desktop, "Second")));
    }

    [Fact]
    public async Task A_folder_changed_since_the_list_is_left_where_it_is()
    {
        using var sandbox = new TemporaryDirectory();
        var desktop = sandbox.CreateDummyDirectory("Desktop");
        sandbox.CreateDummyDirectory(@"Desktop\Old stuff");
        var folder = Path.GetDirectoryName(sandbox.CreateDummyFile(@"Desktop\Old project\notes.txt"))!;
        Directory.SetLastWriteTimeUtc(folder, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var facts = FolderFacts(folder);
        File.WriteAllText(Path.Combine(folder, "new.txt"), "Generated DeskAI test data");
        var root = MovesAllowed(desktop);
        var executor = Create(new DisconnectingRootRepository(root, int.MaxValue), new WindowsPathPolicy(), sandbox);
        var move = FolderMove("Old project", @"Old stuff\Old project");
        var plan = StudioPlan(root, move);

        var result = await executor.ExecuteAsync(
            plan, Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = facts }, TestContext.Current.CancellationToken);

        Assert.Contains("changed after the list", Assert.Single(result.Operations).Error, StringComparison.Ordinal);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task A_folder_is_never_moved_onto_something_with_the_same_name()
    {
        using var sandbox = new TemporaryDirectory();
        var desktop = sandbox.CreateDummyDirectory("Desktop");
        var already = sandbox.CreateDummyFile(@"Desktop\Old stuff\Old project\kept.txt");
        sandbox.CreateDummyFile(@"Desktop\Old project\notes.txt");
        var root = MovesAllowed(desktop);
        var executor = Create(new DisconnectingRootRepository(root, int.MaxValue), new WindowsPathPolicy(), sandbox);
        var move = FolderMove("Old project", @"Old stuff\Old project");
        var plan = StudioPlan(root, move);

        var result = await executor.ExecuteAsync(
            plan, Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = FolderFacts(Path.Combine(desktop, "Old project")) },
            TestContext.Current.CancellationToken);

        Assert.Contains("already there", Assert.Single(result.Operations).Error, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(desktop, "Old project", "notes.txt")));
        Assert.True(File.Exists(already));
        Assert.False(File.Exists(Path.Combine(desktop, "Old stuff", "Old project", "notes.txt")));
    }

    [Fact]
    public async Task A_folder_replaced_after_the_move_is_not_moved_back()
    {
        using var sandbox = new TemporaryDirectory();
        var desktop = sandbox.CreateDummyDirectory("Desktop");
        sandbox.CreateDummyDirectory(@"Desktop\Old stuff");
        var folder = Path.GetDirectoryName(sandbox.CreateDummyFile(@"Desktop\Old project\notes.txt"))!;
        Directory.SetCreationTimeUtc(folder, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var root = MovesAllowed(desktop);
        var executor = Create(new DisconnectingRootRepository(root, int.MaxValue), new WindowsPathPolicy(), sandbox);
        var move = FolderMove("Old project", @"Old stuff\Old project");
        var plan = StudioPlan(root, move);
        var result = await executor.ExecuteAsync(
            plan, Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = FolderFacts(folder) }, TestContext.Current.CancellationToken);
        var moved = Path.Combine(desktop, "Old stuff", "Old project");
        Directory.Delete(moved, recursive: true);
        sandbox.CreateDummyFile(@"Desktop\Old stuff\Old project\someone else's.txt");

        var undo = await executor.UndoAsync(result.TransactionId, TestContext.Current.CancellationToken);

        Assert.Contains("can't find the folder it moved", Assert.Single(undo.Operations).Error, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(moved, "someone else's.txt")));
        Assert.False(Directory.Exists(folder));
    }

    /// <summary>
    /// Windows refuses to rename a folder while a file inside is held open without delete sharing.
    /// If a future Windows allowed it, the move would still be whole and the program would keep its
    /// open file; this test then needs a new decision, not a quiet change.
    /// </summary>
    [Fact]
    public async Task A_folder_with_a_file_open_in_another_program_stays_where_it_is()
    {
        using var sandbox = new TemporaryDirectory();
        var desktop = sandbox.CreateDummyDirectory("Desktop");
        sandbox.CreateDummyDirectory(@"Desktop\Old stuff");
        var notes = sandbox.CreateDummyFile(@"Desktop\Old project\notes.txt");
        var root = MovesAllowed(desktop);
        var executor = Create(new DisconnectingRootRepository(root, int.MaxValue), new WindowsPathPolicy(), sandbox);
        var move = FolderMove("Old project", @"Old stuff\Old project");
        var plan = StudioPlan(root, move);
        var facts = FolderFacts(Path.GetDirectoryName(notes)!);

        ExecutionResult result;
        using (new FileStream(notes, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await executor.ExecuteAsync(
                plan, Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
                new Dictionary<Guid, ExpectedFile> { [move.Id] = facts }, TestContext.Current.CancellationToken);
        }

        Assert.Contains("open in another program", Assert.Single(result.Operations).Error, StringComparison.Ordinal);
        Assert.True(File.Exists(notes));
    }

    [Fact]
    public async Task An_interrupted_folder_move_is_checked_against_the_disk()
    {
        using var sandbox = new TemporaryDirectory();
        var desktop = sandbox.CreateDummyDirectory("Desktop");
        sandbox.CreateDummyDirectory(@"Desktop\Old stuff");
        var moved = Path.GetDirectoryName(sandbox.CreateDummyFile(@"Desktop\Old stuff\Moved\a.txt"))!;
        var stayed = Path.GetDirectoryName(sandbox.CreateDummyFile(@"Desktop\Stayed\b.txt"))!;
        var root = MovesAllowed(desktop);
        var plans = new InMemoryPlanRepository();
        var journal = new InMemoryOperationJournal(plans);
        var moveA = FolderMove("Moved", @"Old stuff\Moved");
        var moveB = FolderMove("Stayed", @"Old stuff\Stayed");
        var moveC = FolderMove("Gone", @"Old stuff\Gone");
        var plan = StudioPlan(root, moveA, moveB, moveC);
        await plans.SaveAsync(plan, TestContext.Current.CancellationToken);
        OperationJournalEntry Intent(int sequence, MoveFolderOperation move, DateTimeOffset madeAt) =>
            new(sequence, move.Id, PlanOperationKind.MoveFolder, move.SourceRelativePath, move.DestinationRelativePath,
                null, DateTimeOffset.UnixEpoch, JournalOperationState.InProgress, null) { BeforeCreatedAtUtc = madeAt };
        var record = new ExecutionJournalEntry(
            Guid.NewGuid(), plan.Id, 1, Guid.NewGuid(), ExecutionTransactionKind.Execute, null,
            ExecutionTransactionState.Executing, DateTimeOffset.UtcNow, null,
            [
                Intent(0, moveA, Directory.GetCreationTimeUtc(moved)),
                Intent(1, moveB, Directory.GetCreationTimeUtc(stayed)),
                Intent(2, moveC, DateTimeOffset.UnixEpoch),
            ]) { Purpose = PlanPurpose.ClearOldStuff };
        await journal.CreateAsync(record, TestContext.Current.CancellationToken);
        var executor = new FolderTidyExecutor(
            new DisconnectingRootRepository(root, int.MaxValue), new FixedFolderService(null),
            new PlanValidator(new WindowsPathPolicy()), new WindowsPathPolicy(), new SystemClock(),
            journal, plans, Database(sandbox));

        var checkedRecord = Assert.Single(await executor.CheckInterruptedAsync(root.Id, TestContext.Current.CancellationToken));

        Assert.Equal(JournalOperationState.Completed, checkedRecord.Operations[0].State);
        Assert.Equal(JournalOperationState.Failed, checkedRecord.Operations[1].State);
        Assert.Equal(JournalOperationState.NeedsReview, checkedRecord.Operations[2].State);
    }
```

Add these helpers beside the existing private helpers:

```csharp
    private static AuthorizedRoot MovesAllowed(string folder) =>
        AuthorizedRoot.Create(Guid.NewGuid(), folder, "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly)
            .WithFolderMovesAllowedSince(DateTimeOffset.UnixEpoch);

    private static MoveFolderOperation FolderMove(string from, string to) =>
        new(Guid.NewGuid(), from, to, "Unchanged for 6 months", OperationProvenance.Heuristic);

    private static OrganizationPlan StudioPlan(AuthorizedRoot root, params PlanOperation[] operations) =>
        OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion,
            operations, purpose: PlanPurpose.ClearOldStuff);

    private static ExpectedFile FolderFacts(string path)
    {
        var info = new DirectoryInfo(path);
        return new ExpectedFile(0, info.LastWriteTimeUtc) { CreatedAtUtc = info.CreationTimeUtc };
    }

    /// <summary>Answers with <paramref name="first"/> until it has been asked a set number of times, then with <paramref name="then"/>.</summary>
    private sealed class ChangingRootRepository(AuthorizedRoot first, AuthorizedRoot then, int switchAfter) : IAuthorizedRootRepository
    {
        private int _finds;

        public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthorizedRoot?>(rootId != first.Id ? null : ++_finds <= switchAfter ? first : then);

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>([first]);

        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build DeskAI.sln -c Release --no-restore` then
`dotnet test tests/DeskAI.Infrastructure.Tests -c Release --no-build --no-restore --filter "FullyQualifiedName~FolderTidyExecutorTests"`
Expected: the new tests fail — a folder move is refused with "DeskAI does not know how to do
that." (or the permission tests fail on the tidy-only messages).

- [ ] **Step 3: Permission by purpose in `FolderTidyExecutor`**

Add to `FolderTidyExecutor`:

```csharp
    /// <summary>
    /// A tidy needs the tidy yes; a Desktop Studio run needs its own yes (ADR 0044). Neither one
    /// stands in for the other.
    /// </summary>
    internal static bool MayRun(AuthorizedRoot root, PlanPurpose purpose) =>
        purpose == PlanPurpose.Tidy ? RootCapabilities.CanTidy(root) : RootCapabilities.CanMoveFolders(root);
```

In `ExecuteAsync`, replace the `if (root is null || !RootCapabilities.CanTidy(root))` block with:

```csharp
        if (root is null || !MayRun(root, plan.Purpose))
        {
            return Refuse(plan, approval, root is null
                ? "That folder is no longer connected, so nothing was moved."
                : plan.Purpose == PlanPurpose.Tidy
                    ? "DeskAI may not tidy this folder, so nothing was moved."
                    : "DeskAI may not move things here, so nothing was moved.");
        }
```

and pass `plan.Purpose` as a fourth argument to `new FolderTrust(...)` in both `ExecuteAsync` and
`UndoAsync`. In `UndoAsync`, replace the `if (root is null || !RootCapabilities.CanTidy(root))`
block with:

```csharp
        if (root is null || !MayRun(root, plan.Purpose))
        {
            throw new InvalidOperationException(root is null
                ? "That folder is no longer connected, so nothing was moved back."
                : plan.Purpose == PlanPurpose.Tidy
                    ? "Undo moves files too, so DeskAI needs your permission to tidy this folder again."
                    : "Putting things back moves them too, so DeskAI needs your permission to move things on your Desktop again.");
        }
```

Change `FolderTrust` to take `PlanPurpose purpose` as its last primary-constructor parameter and
replace its `CanTidy` check with:

```csharp
            if (!MayRun(current, purpose))
            {
                throw new InvalidOperationException(purpose == PlanPurpose.Tidy
                    ? "DeskAI may no longer tidy this folder, so it stopped."
                    : "DeskAI may no longer move things here, so it stopped.");
            }
```

- [ ] **Step 4: Move, put back, and check a folder in `FileOperationRunner`**

In `ExecuteAsync`, give the created entry its purpose: `new ExecutionJournalEntry(... intents) { Purpose = plan.Purpose }`.
In `UndoAsync`, likewise: `new ExecutionJournalEntry(... reversible) { Purpose = plan.Purpose }`.

In `CaptureIntent`, add the arm `MoveFolderOperation folder => (folder.SourceRelativePath, folder.DestinationRelativePath),`,
add a local `DateTimeOffset? created = null;`, set `created = seen.CreatedAtUtc;` in the
`expected` branch, and replace the `else if (source is not null)` branch with:

```csharp
        else if (source is not null)
        {
            var sourcePath = Resolve(root, source);
            RejectLinks(root, sourcePath);
            if (operation is MoveFolderOperation && Directory.Exists(sourcePath))
            {
                var folder = new DirectoryInfo(sourcePath);
                modified = folder.LastWriteTimeUtc;
                created = folder.CreationTimeUtc;
            }
            else if (File.Exists(sourcePath))
            {
                var info = new FileInfo(sourcePath);
                size = info.Length;
                modified = info.LastWriteTimeUtc;
            }
        }
```

and return the entry with `{ BeforeCreatedAtUtc = created }`.

In `ExecuteOperation`, add before `default:`:

```csharp
            case MoveFolderOperation folder:
                MoveFolder(root, folder.SourceRelativePath, folder.DestinationRelativePath, intent);
                return JournalOperationState.Completed;
```

In `UndoOperation`, add right after the `CreateDirectory` block:

```csharp
        if (operation.Kind == PlanOperationKind.MoveFolder)
        {
            UndoFolderMove(root, operation);
            return;
        }
```

In `CheckInterrupted`, add right after the `CreateDirectory` block:

```csharp
            if (operation.Kind == PlanOperationKind.MoveFolder && operation.SourceRelativePath is not null)
            {
                var start = Resolve(root, operation.SourceRelativePath);
                var end = Resolve(root, operation.DestinationRelativePath);
                RejectLinks(root, start);
                RejectLinks(root, end);
                var (fromFolder, toFolder) = kind == ExecutionTransactionKind.Execute ? (start, end) : (end, start);
                if (!File.Exists(fromFolder) && !Directory.Exists(fromFolder) && MatchesRecordedFolder(toFolder, operation, sameContents: false))
                {
                    return (JournalOperationState.Completed, kind == ExecutionTransactionKind.Execute
                        ? "Checked after DeskAI stopped: it had moved."
                        : "Checked after DeskAI stopped: it had gone back.");
                }

                return MatchesRecordedFolder(fromFolder, operation, sameContents: false)
                    ? (JournalOperationState.Failed, kind == ExecutionTransactionKind.Execute
                        ? "It had not moved yet, so it is where it was."
                        : "It had not gone back yet, so it is where it was moved to.")
                    : (JournalOperationState.NeedsReview, couldNotTell);
            }
```

Add these members beside `MoveFile`:

```csharp
    /// <summary>
    /// Moves a whole folder with one rename (ADR 0044). Each check the file move makes has a
    /// folder twin; the folder must also not go inside itself.
    /// </summary>
    private void MoveFolder(AuthorizedRoot root, string sourceRelativePath, string destinationRelativePath, OperationJournalEntry intent)
    {
        var source = Resolve(root, sourceRelativePath);
        var destination = Resolve(root, destinationRelativePath);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("That location is outside the folder.");
        RejectLinks(root, source);
        RejectLinks(root, parent);

        if (!Directory.Exists(source))
        {
            throw new FileOperationRefusal("It is no longer there.");
        }

        if (IsSameOrInside(destination, source))
        {
            throw new FileOperationRefusal("A folder cannot go inside itself.");
        }

        if ((File.GetAttributes(source) & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            throw new FileOperationRefusal("It is now a hidden or system folder.");
        }

        if (!MatchesRecordedFolder(source, intent, sameContents: true))
        {
            throw new FileOperationRefusal("It changed after the list was made, so it was left where it is.");
        }

        if (!Directory.Exists(parent))
        {
            throw new FileOperationRefusal("The folder it was going into is missing.");
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new FileOperationRefusal("Something with that name is already there, so nothing was replaced.");
        }

        MoveFolderWithoutOverwrite(source, destination);
    }

    /// <summary>
    /// Moves a folder back only when it is still the same folder (same made-at time) and its old
    /// place is free. Things added inside since go back with it.
    /// </summary>
    private void UndoFolderMove(AuthorizedRoot root, OperationJournalEntry operation)
    {
        if (operation.SourceRelativePath is null)
        {
            throw new InvalidOperationException("The history is missing where this folder came from.");
        }

        var originalPlace = Resolve(root, operation.SourceRelativePath);
        var current = Resolve(root, operation.DestinationRelativePath);
        RejectLinks(root, current);
        RejectLinks(root, Path.GetDirectoryName(originalPlace)
            ?? throw new InvalidOperationException("The history is missing where this folder came from."));
        if (File.Exists(originalPlace) || Directory.Exists(originalPlace))
        {
            throw new FileOperationRefusal("Something else is now where this folder was, so it was not moved back.");
        }

        if (!MatchesRecordedFolder(current, operation, sameContents: false))
        {
            throw new FileOperationRefusal("DeskAI can't find the folder it moved, so it was not moved back.");
        }

        MoveFolderWithoutOverwrite(current, originalPlace);
    }

    /// <summary>
    /// A folder is the one recorded when its made-at time matches. With <paramref name="sameContents"/>,
    /// its own last-changed time must match too: that moves whenever something directly inside is
    /// added, removed, or renamed, so it tells a folder changed since the list was made.
    /// </summary>
    private static bool MatchesRecordedFolder(string path, OperationJournalEntry operation, bool sameContents)
    {
        if (!Directory.Exists(path) || operation.BeforeCreatedAtUtc is not { } created)
        {
            return false;
        }

        var info = new DirectoryInfo(path);
        return info.CreationTimeUtc == created.UtcDateTime &&
               (!sameContents || (operation.BeforeModifiedAtUtc is { } modified && info.LastWriteTimeUtc == modified.UtcDateTime));
    }

    /// <summary>One rename, never replacing. Windows refuses while something inside is open, and says so here in plain words.</summary>
    private static void MoveFolderWithoutOverwrite(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException exception) when (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new FileOperationRefusal("Something with that name is already there, so nothing was replaced.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new FileOperationRefusal("Something inside it is open in another program, so it was left where it is.", exception);
        }
    }

    private static bool IsSameOrInside(string candidate, string folder)
    {
        var relative = Path.GetRelativePath(Normalize(folder), Normalize(candidate));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet build DeskAI.sln -c Release --no-restore` then
`dotnet test DeskAI.sln -c Release --no-build --no-restore`
Expected: 0 warnings; all tests pass. If
`A_folder_with_a_file_open_in_another_program_stays_where_it_is` fails because Windows allowed the
rename, stop and ledger it as a finding (the test's summary says why); do not weaken it.

- [ ] **Step 6: Commit**

```bash
git add src/DeskAI.Infrastructure/Execution tests/DeskAI.Infrastructure.Tests/FolderTidyExecutorTests.cs
git commit -m "Move a whole folder, put it back, and check it after a stop, under its own yes"
```

---
### Task 5: What is on the Desktop, and what each card would move

**Files:**
- Create: `src/DeskAI.Core/Studio/DesktopInventory.cs`
- Create: `src/DeskAI.Core/Studio/DesktopMovePlanner.cs`
- Test: `tests/DeskAI.Core.Tests/DesktopInventoryServiceTests.cs`, `tests/DeskAI.Core.Tests/DesktopMovePlannerTests.cs`

**Interfaces:**
- Consumes: `FolderDiscovered` dates, `ExpectedFile.CreatedAtUtc`, `MoveFolderOperation`, `PlanPurpose` (Task 2); `ReplayScanner` and `StudioFakes` (existing, in `tests/DeskAI.Core.Tests/DesktopLookServiceTests.cs`).
- Produces (namespace `DeskAI.Core.Studio`):
  - `[Flags] enum DesktopThingWarnings { None = 0, ActiveProject = 1, HasPrograms = 2, OnlineOnly = 4, NotFullyLooked = 8 }`
  - `sealed record DesktopThing(string RelativePath, bool IsFolder, DateTimeOffset LastChangedUtc, ExpectedFile Facts, int FileCount, DesktopThingWarnings Warnings, IReadOnlySet<string> ChildNames)` with `Name`, `LookedAllTheWay`
  - `sealed record DesktopInventory(IReadOnlyList<DesktopThing> Things, string? Problem)`
  - `sealed class DesktopInventoryService(IFileScanner scanner)` with `static MetadataScanOptions Bounds` and `Task<DesktopInventory> LookAsync(AuthorizedRoot root, CancellationToken cancellationToken = default)`
  - `enum DesktopMoveCard { ClearOldStuff, FolderByGroup }`
  - `sealed record DesktopMoveItem(Guid OperationId, string RelativePath, bool IsFolder, string Destination, DateTimeOffset LastChangedUtc, int FileCount, bool LookedAllTheWay, string? Warning)` with `Name`, `TickedByDefault`
  - `sealed record DesktopLeftAlone(string Name, string Reason)`
  - `sealed record DesktopMovePreview(DesktopMoveCard Card, AuthorizedRoot Root, OrganizationPlan Plan, IReadOnlyList<DesktopMoveItem> Items, IReadOnlyList<DesktopLeftAlone> LeftAlone, IReadOnlyDictionary<Guid, ExpectedFile> Facts)`
  - `static class DesktopMoveText { string Things(int); string Total(IEnumerable<DesktopMoveItem>); string? WarningFor(DesktopThingWarnings) }`
  - `static class DesktopMovePlanner { const string OldStuffFolder = "Old stuff"; DesktopMovePreview ClearOldStuff(AuthorizedRoot, DesktopInventory, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected); DesktopMovePreview FolderByGroup(AuthorizedRoot, DesktopInventory, DesktopGroupBoard, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected) }`

- [ ] **Step 1: Write the failing tests**

`tests/DeskAI.Core.Tests/DesktopInventoryServiceTests.cs`:

```csharp
using DeskAI.Core.Files;
using DeskAI.Core.Studio;

namespace DeskAI.Core.Tests;

/// <summary>The read-only look Clear old stuff and Folder by group start from (ADR 0044). No disk is touched.</summary>
public sealed class DesktopInventoryServiceTests
{
    private static readonly DateTimeOffset Made = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_folder_takes_the_newest_date_found_inside_it()
    {
        var seen = await LookAsync(
            Folder("Old project", Day(2024, 1)),
            File("notes.txt", Day(2024, 2)),
            Folder(@"Old project\deep", Day(2024, 3)),
            File(@"Old project\a.txt", Day(2024, 4)),
            File(@"Old project\deep\b.txt", Day(2025, 5)));

        var folder = seen.Things.Single(thing => thing.Name == "Old project");
        Assert.True(folder.IsFolder);
        Assert.Equal(Day(2025, 5), folder.LastChangedUtc);
        Assert.Equal(2, folder.FileCount);
        Assert.Equal(["a.txt", "deep"], folder.ChildNames.Order(StringComparer.Ordinal));
        Assert.Equal(Made, folder.Facts.CreatedAtUtc);
        Assert.Equal(Day(2024, 1), folder.Facts.ModifiedAtUtc);
        Assert.True(folder.LookedAllTheWay);
        var file = seen.Things.Single(thing => thing.Name == "notes.txt");
        Assert.False(file.IsFolder);
        Assert.Equal(Day(2024, 2), file.LastChangedUtc);
    }

    [Fact]
    public async Task Project_program_and_online_only_folders_carry_a_warning()
    {
        var seen = await LookAsync(
            Folder("Code", Day(2024, 1)),
            Folder("Games", Day(2024, 1)),
            Folder("Cloud", Day(2024, 1)),
            Folder("Plain", Day(2024, 1)),
            Folder(@"Code\.git", Day(2024, 1), FileTraits.Hidden),
            File(@"Games\game.exe", Day(2024, 1)),
            File(@"Cloud\photo.jpg", Day(2024, 1), FileTraits.OnlineOnly),
            File(@"Plain\a.txt", Day(2024, 1)));

        Assert.Equal(DesktopThingWarnings.ActiveProject, Warnings(seen, "Code"));
        Assert.Equal(DesktopThingWarnings.HasPrograms, Warnings(seen, "Games"));
        Assert.Equal(DesktopThingWarnings.OnlineOnly, Warnings(seen, "Cloud"));
        Assert.Equal(DesktopThingWarnings.None, Warnings(seen, "Plain"));
    }

    [Fact]
    public async Task A_folder_the_look_could_not_finish_is_marked()
    {
        var deep = await LookAsync(
            Folder("Deep", Day(2024, 1)),
            Folder("Shallow", Day(2024, 1)),
            new ScanIssue(@"Deep\a\b\c\d\e\f\g\h", ScanIssueCode.DepthLimitReached, "x"));
        var stopped = await LookAsync(
            Folder("One", Day(2024, 1)),
            File(@"One\a.txt", Day(2024, 1)),
            new ScanIssue(".", ScanIssueCode.EntryLimitReached, "x"));

        Assert.False(deep.Things.Single(thing => thing.Name == "Deep").LookedAllTheWay);
        Assert.True(deep.Things.Single(thing => thing.Name == "Shallow").LookedAllTheWay);
        Assert.False(Assert.Single(stopped.Things).LookedAllTheWay);
    }

    [Fact]
    public async Task Hidden_protected_and_linked_things_are_left_out()
    {
        var seen = await LookAsync(
            Folder("DeskAI", Day(2024, 1)),
            Folder("Secret", Day(2024, 1), FileTraits.Hidden),
            File("hidden.txt", Day(2024, 1), FileTraits.Hidden),
            new ScanIssue("Link", ScanIssueCode.ReparsePointSkipped, "x"),
            Folder("Kept", Day(2024, 1)),
            new ScanIssue(@"DeskAI\app", ScanIssueCode.ProtectedEntrySkipped, "x"));

        Assert.Equal(["Kept"], seen.Things.Select(thing => thing.Name));
    }

    [Fact]
    public async Task Too_many_things_on_the_Desktop_is_a_problem()
    {
        var seen = await LookAsync(new ScanIssue(".", ScanIssueCode.EntryLimitReached, "x"));

        Assert.Empty(seen.Things);
        Assert.Equal(DesktopLookService.TooManyProblem, seen.Problem);
    }

    private static Task<DesktopInventory> LookAsync(params ScanEvent[] events) =>
        new DesktopInventoryService(new ReplayScanner(events)).LookAsync(StudioFakes.Root(), TestContext.Current.CancellationToken);

    private static DesktopThingWarnings Warnings(DesktopInventory seen, string name) =>
        seen.Things.Single(thing => thing.Name == name).Warnings;

    private static DateTimeOffset Day(int year, int month) => new(year, month, 1, 0, 0, 0, TimeSpan.Zero);

    private static FolderDiscovered Folder(string path, DateTimeOffset changed, FileTraits traits = FileTraits.None) =>
        new(path, traits, Made, changed);

    private static FileDiscovered File(string path, DateTimeOffset changed, FileTraits traits = FileTraits.None) =>
        new(new FileItem(Guid.NewGuid(), path, FileKind.Unknown, 10, Made, changed, traits));
}
```

`tests/DeskAI.Core.Tests/DesktopMovePlannerTests.cs`:

```csharp
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Studio;

namespace DeskAI.Core.Tests;

/// <summary>What Clear old stuff and Folder by group would move (ADR 0044): plain rules, no disk, no AI.</summary>
public sealed class DesktopMovePlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Old = Now.AddDays(-200);
    private static readonly DateTimeOffset Recent = Now.AddDays(-3);

    [Fact]
    public void Clear_old_stuff_lists_only_things_unchanged_for_six_months()
    {
        var preview = Clear(Folder("Old project", Old, files: 3), File("old.txt", Old), Folder("Current", Recent), File("new.txt", Recent));

        Assert.Equal(["Old project", "old.txt"], preview.Items.Select(item => item.Name));
        Assert.All(preview.Items, item => Assert.Equal("Old stuff", item.Destination));
        Assert.Equal(PlanPurpose.ClearOldStuff, preview.Plan.Purpose);
        Assert.Equal(
            [PlanOperationKind.CreateDirectory, PlanOperationKind.MoveFolder, PlanOperationKind.MoveFile],
            preview.Plan.Operations.Select(operation => operation.Kind));
        Assert.Equal(preview.Items.Select(item => item.OperationId).Order(), preview.Facts.Keys.Order());
        Assert.Equal(Old, preview.Facts[preview.Items[0].OperationId].CreatedAtUtc);
    }

    [Fact]
    public void Clear_old_stuff_leaves_a_clash_and_an_unfinished_look_alone()
    {
        var preview = Clear(
            Folder("Old stuff", Recent, children: "old.txt"),
            File("old.txt", Old),
            Folder("Deep", Old, warnings: DesktopThingWarnings.NotFullyLooked));

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("old.txt", "Old stuff already has something called old.txt, so nothing was replaced."), preview.LeftAlone);
        Assert.Contains(new DesktopLeftAlone("Deep", "DeskAI couldn't look all the way inside, so it can't tell whether it's old."), preview.LeftAlone);
        Assert.DoesNotContain(preview.Plan.Operations, operation => operation is CreateDirectoryOperation);
    }

    [Fact]
    public void Clear_old_stuff_never_lists_the_Old_stuff_folder_itself()
    {
        var preview = Clear(Folder("Old stuff", Old), File("a.txt", Old));

        Assert.Equal(["a.txt"], preview.Items.Select(item => item.Name));
        Assert.DoesNotContain(preview.Plan.Operations, operation => operation is CreateDirectoryOperation);
    }

    [Fact]
    public void A_file_called_Old_stuff_keeps_everything_where_it_is()
    {
        var preview = Clear(File("Old stuff", Recent), File("a.txt", Old));

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("a.txt", "A file called Old stuff is in the way, so its folder can't be made."), preview.LeftAlone);
    }

    [Fact]
    public void Warned_folders_start_unticked()
    {
        var preview = Clear(
            Folder("Code", Old, warnings: DesktopThingWarnings.ActiveProject | DesktopThingWarnings.HasPrograms),
            Folder("Games", Old, warnings: DesktopThingWarnings.HasPrograms),
            Folder("Plain", Old));

        var code = preview.Items.Single(item => item.Name == "Code");
        Assert.False(code.TickedByDefault);
        Assert.StartsWith("It looks like a project", code.Warning, StringComparison.Ordinal);
        Assert.StartsWith("It has programs inside", preview.Items.Single(item => item.Name == "Games").Warning, StringComparison.Ordinal);
        Assert.True(preview.Items.Single(item => item.Name == "Plain").TickedByDefault);
    }

    [Fact]
    public void A_protected_destination_is_left_alone()
    {
        var preview = DesktopMovePlanner.ClearOldStuff(
            StudioFakes.Root(), Seen(File("secret.txt", Old)), Now, "1", path => path == @"Old stuff\secret.txt");

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("secret.txt", "DeskAI's safety rules keep it where it is."), preview.LeftAlone);
    }

    [Fact]
    public void Folder_by_group_moves_each_group_into_its_own_folder_and_leaves_Not_sure()
    {
        var board = Board([new("Coding", ["Python stuff"]), new("Documents", ["report.docx", "Essays"])], notSure: ["holiday.jpg"]);

        var preview = ByGroup(board, Folder("Python stuff", Recent), File("report.docx", Recent), Folder("Essays", Recent), File("holiday.jpg", Recent));

        Assert.Equal(["Python stuff", "report.docx", "Essays"], preview.Items.Select(item => item.Name));
        Assert.Equal(["Coding", "Documents", "Documents"], preview.Items.Select(item => item.Destination));
        Assert.Equal(["Coding", "Documents"], preview.Plan.Operations.OfType<CreateDirectoryOperation>().Select(create => create.DestinationRelativePath));
        Assert.Equal(PlanPurpose.FolderByGroup, preview.Plan.Purpose);
        Assert.All(preview.Plan.Operations, operation => Assert.Equal(OperationProvenance.User, operation.Provenance));
    }

    [Fact]
    public void Folder_by_group_leaves_a_group_folder_where_it_is_and_never_moves_another_groups_folder()
    {
        var board = Board([new("Coding", ["Coding", "app.py"]), new("Documents", ["School"]), new("School", ["essay.docx"])]);

        var preview = ByGroup(board, Folder("Coding", Recent), File("app.py", Recent), Folder("School", Recent), File("essay.docx", Recent));

        Assert.Equal(["app.py", "essay.docx"], preview.Items.Select(item => item.Name));
        Assert.DoesNotContain(preview.Plan.Operations, operation => operation is CreateDirectoryOperation);
        Assert.Contains(new DesktopLeftAlone("Coding", "This is the Coding folder itself, so the rest of the group goes into it."), preview.LeftAlone);
        Assert.Contains(new DesktopLeftAlone("School", "It's where the School group goes, so it stays."), preview.LeftAlone);
    }

    [Fact]
    public void Folder_by_group_leaves_a_group_alone_when_a_file_has_its_name()
    {
        var board = Board([new("Music", ["song.mp3"])]);

        var preview = ByGroup(board, File("Music", Recent), File("song.mp3", Recent));

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("song.mp3", "A file called Music is in the way, so its folder can't be made."), preview.LeftAlone);
    }

    [Fact]
    public void Folder_by_group_names_what_is_no_longer_on_the_Desktop()
    {
        var preview = ByGroup(Board([new("Coding", ["gone.py"])]));

        Assert.Contains(new DesktopLeftAlone("gone.py", "It is no longer on your Desktop."), preview.LeftAlone);
    }

    [Fact]
    public void The_total_counts_folders_files_inside_and_loose_files()
    {
        DesktopMoveItem Item(bool folder, int files, bool looked = true) =>
            new(Guid.NewGuid(), "x", folder, "Old stuff", Old, files, looked, null);

        Assert.Equal("2 folders holding 1,204 files, plus 5 files",
            DesktopMoveText.Total([Item(true, 3), Item(true, 1201), .. Enumerable.Range(0, 5).Select(_ => Item(false, 0))]));
        Assert.Equal("1 folder holding at least 3 files", DesktopMoveText.Total([Item(true, 3, looked: false)]));
        Assert.Equal("1 file", DesktopMoveText.Total([Item(false, 0)]));
        Assert.Equal("Nothing", DesktopMoveText.Total([]));
    }

    private static DesktopMovePreview Clear(params DesktopThing[] things) =>
        DesktopMovePlanner.ClearOldStuff(StudioFakes.Root(), Seen(things), Now, "1", _ => false);

    private static DesktopMovePreview ByGroup(DesktopGroupBoard board, params DesktopThing[] things) =>
        DesktopMovePlanner.FolderByGroup(StudioFakes.Root(), Seen(things), board, Now, "1", _ => false);

    private static DesktopInventory Seen(params DesktopThing[] things) => new(things, null);

    private static DesktopGroupBoard Board(IReadOnlyList<DesktopGroup> groups, IReadOnlyList<string>? notSure = null) =>
        new(StudioFakes.DesktopId, groups, notSure ?? [], DesktopGroupSource.LocalGuess, Now);

    private static DesktopThing Folder(
        string name, DateTimeOffset changed, int files = 1, DesktopThingWarnings warnings = DesktopThingWarnings.None, params string[] children) =>
        new(name, true, changed, new ExpectedFile(0, changed) { CreatedAtUtc = changed }, files, warnings,
            children.ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static DesktopThing File(string name, DateTimeOffset changed) =>
        new(name, false, changed, new ExpectedFile(10, changed), 0, DesktopThingWarnings.None, new HashSet<string>());
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/DeskAI.Core.Tests -c Release --no-restore`
Expected: build errors — `DesktopInventoryService`, `DesktopThing`, `DesktopMovePlanner`, `DesktopMoveText`, `DesktopLeftAlone` do not exist.

- [ ] **Step 3: Write `DesktopInventory.cs`**

```csharp
using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Files;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Studio;

/// <summary>Why a folder should not move without a second thought. Its row starts unticked.</summary>
[Flags]
public enum DesktopThingWarnings
{
    None = 0,
    ActiveProject = 1,
    HasPrograms = 2,
    OnlineOnly = 4,
    NotFullyLooked = 8,
}

/// <summary>One thing directly on the Desktop, as the moving cards need it. Opens no file.</summary>
/// <param name="LastChangedUtc">A file's own last change; for a folder, the newest of itself and everything found inside.</param>
/// <param name="Facts">What the executor checks again right before it moves.</param>
/// <param name="FileCount">Files found inside a folder; 0 for a file.</param>
/// <param name="ChildNames">Names directly inside a folder, to see a same-name clash before moving something into it.</param>
public sealed record DesktopThing(
    string RelativePath,
    bool IsFolder,
    DateTimeOffset LastChangedUtc,
    ExpectedFile Facts,
    int FileCount,
    DesktopThingWarnings Warnings,
    IReadOnlySet<string> ChildNames)
{
    public string Name => Path.GetFileName(RelativePath);

    public bool LookedAllTheWay => (Warnings & DesktopThingWarnings.NotFullyLooked) == 0;
}

public sealed record DesktopInventory(IReadOnlyList<DesktopThing> Things, string? Problem);

/// <summary>
/// A fresh, read-only look at what sits directly on a connected Desktop, for Clear old stuff and
/// Folder by group (ADR 0044). Nothing it sees is sent anywhere.
/// </summary>
/// <remarks>
/// It looks deeper than Find groups, with the search bounds of ADR 0041, because a folder counts
/// as old only when everything found inside it is old. A folder the look could not finish is
/// marked, never guessed about. Hidden, system, protected, and link items are left out as in
/// <see cref="DesktopLookService"/>, including any top-level folder holding one, so DeskAI's own
/// program folder can never be listed or moved.
/// </remarks>
public sealed class DesktopInventoryService(IFileScanner scanner)
{
    private static readonly HashSet<string> ProjectFolders = new(StringComparer.OrdinalIgnoreCase) { ".git", ".venv" };
    private static readonly HashSet<string> ProjectFiles = new(StringComparer.OrdinalIgnoreCase) { "package.json", "pyvenv.cfg" };
    private static readonly HashSet<string> ProjectEndings = new(StringComparer.OrdinalIgnoreCase) { ".sln", ".csproj" };
    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>();

    public static MetadataScanOptions Bounds { get; } = new(maxDepth: 8, maxEntries: 20_000);

    public async Task<DesktopInventory> LookAsync(AuthorizedRoot root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var folders = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileItem>();
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sawInsideAFolder = false;
        var stoppedEarly = false;
        await foreach (var scanEvent in scanner.ScanAsync(root, Bounds, cancellationToken).ConfigureAwait(false))
        {
            var nested = PathOf(scanEvent)?.Contains(Path.DirectorySeparatorChar) == true;
            sawInsideAFolder |= nested;
            switch (scanEvent)
            {
                // The scanner lists the Desktop itself completely before going into any folder.
                case ScanIssue { RelativePath: ".", Code: ScanIssueCode.EntryLimitReached }:
                    if (!sawInsideAFolder)
                    {
                        return new([], DesktopLookService.TooManyProblem);
                    }

                    stoppedEarly = true;
                    break;
                case ScanIssue { RelativePath: "." }:
                    return new([], DesktopLookService.RootProblem);
                case ScanIssue { Code: ScanIssueCode.ProtectedEntrySkipped or ScanIssueCode.ReparsePointSkipped } issue:
                    excluded.Add(TopSegment(issue.RelativePath));
                    break;
                case ScanIssue issue when nested:
                    if (folders.TryGetValue(TopSegment(issue.RelativePath), out var unfinished))
                    {
                        unfinished.Warnings |= DesktopThingWarnings.NotFullyLooked;
                    }

                    break;
                case ScanIssue issue:
                    // A top-level entry DeskAI could not read is not something it can vouch for.
                    excluded.Add(issue.RelativePath);
                    break;
                case FolderDiscovered folder when !nested:
                    if (IsHiddenOrSystem(folder.Traits) || folder.CreatedAtUtc is not { } made || folder.ModifiedAtUtc is not { } changed)
                    {
                        excluded.Add(folder.RelativePath);
                    }
                    else
                    {
                        folders[folder.RelativePath] = new Folder(made, changed);
                    }

                    break;
                case FolderDiscovered folder:
                    if (folders.TryGetValue(TopSegment(folder.RelativePath), out var parent))
                    {
                        parent.AddFolder(folder, IsDirectChild(folder.RelativePath));
                    }

                    break;
                case FileDiscovered { File: var file } when !nested:
                    if (!IsHiddenOrSystem(file.Traits))
                    {
                        files.Add(file);
                    }

                    break;
                case FileDiscovered { File: var file }:
                    if (folders.TryGetValue(TopSegment(file.RelativePath), out var holder))
                    {
                        holder.AddFile(file, IsDirectChild(file.RelativePath));
                    }

                    break;
            }
        }

        var things = new List<DesktopThing>();
        foreach (var (name, folder) in folders.Where(pair => !excluded.Contains(pair.Key)))
        {
            // Stopped at the item limit: which folders were finished is not known, so none is vouched for.
            var warnings = folder.Warnings | (stoppedEarly ? DesktopThingWarnings.NotFullyLooked : DesktopThingWarnings.None);
            things.Add(new DesktopThing(
                name, true, folder.Newest, new ExpectedFile(0, folder.Modified) { CreatedAtUtc = folder.Created },
                folder.FileCount, warnings, folder.Children));
        }

        foreach (var file in files.Where(file => !excluded.Contains(file.RelativePath)))
        {
            var warnings = (file.Traits & FileTraits.OnlineOnly) != 0 ? DesktopThingWarnings.OnlineOnly : DesktopThingWarnings.None;
            things.Add(new DesktopThing(
                file.RelativePath, false, file.ModifiedAtUtc, new ExpectedFile(file.SizeBytes, file.ModifiedAtUtc), 0, warnings, NoNames));
        }

        return new(things.OrderBy(thing => thing.Name, StringComparer.OrdinalIgnoreCase).ToList(), null);
    }

    private static bool IsHiddenOrSystem(FileTraits traits) => (traits & (FileTraits.Hidden | FileTraits.System)) != 0;

    private static bool IsDirectChild(string relativePath) =>
        relativePath.IndexOf(Path.DirectorySeparatorChar) == relativePath.LastIndexOf(Path.DirectorySeparatorChar);

    private static string TopSegment(string relativePath)
    {
        var index = relativePath.IndexOf(Path.DirectorySeparatorChar);
        return index < 0 ? relativePath : relativePath[..index];
    }

    private static string? PathOf(ScanEvent scanEvent) => scanEvent switch
    {
        FileDiscovered found => found.File.RelativePath,
        FolderDiscovered folder => folder.RelativePath,
        ScanIssue issue => issue.RelativePath,
        _ => null,
    };

    /// <summary>What the look found under one top-level folder.</summary>
    private sealed class Folder(DateTimeOffset created, DateTimeOffset modified)
    {
        public DateTimeOffset Created { get; } = created;

        public DateTimeOffset Modified { get; } = modified;

        public DateTimeOffset Newest { get; private set; } = modified;

        public int FileCount { get; private set; }

        public DesktopThingWarnings Warnings { get; set; }

        public HashSet<string> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddFolder(FolderDiscovered folder, bool direct)
        {
            Saw(folder.ModifiedAtUtc);
            var name = Path.GetFileName(folder.RelativePath);
            if (direct)
            {
                Children.Add(name);
            }

            if (ProjectFolders.Contains(name))
            {
                Warnings |= DesktopThingWarnings.ActiveProject;
            }
        }

        public void AddFile(FileItem file, bool direct)
        {
            FileCount++;
            Saw(file.ModifiedAtUtc);
            var name = Path.GetFileName(file.RelativePath);
            if (direct)
            {
                Children.Add(name);
            }

            if (ProjectFiles.Contains(name) || ProjectEndings.Contains(Path.GetExtension(name)))
            {
                Warnings |= DesktopThingWarnings.ActiveProject;
            }

            if (string.Equals(Path.GetExtension(name), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                Warnings |= DesktopThingWarnings.HasPrograms;
            }

            if ((file.Traits & FileTraits.OnlineOnly) != 0)
            {
                Warnings |= DesktopThingWarnings.OnlineOnly;
            }
        }

        private void Saw(DateTimeOffset? changed)
        {
            if (changed is { } value && value > Newest)
            {
                Newest = value;
            }
        }
    }
}
```

- [ ] **Step 4: Write `DesktopMovePlanner.cs`**

```csharp
using System.Globalization;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.Studio;

public enum DesktopMoveCard
{
    ClearOldStuff,
    FolderByGroup,
}

/// <summary>One thing a card would move, and the folder on the Desktop it would go into.</summary>
/// <param name="Warning">Why it starts unticked, in plain words, or null.</param>
public sealed record DesktopMoveItem(
    Guid OperationId,
    string RelativePath,
    bool IsFolder,
    string Destination,
    DateTimeOffset LastChangedUtc,
    int FileCount,
    bool LookedAllTheWay,
    string? Warning)
{
    public string Name => Path.GetFileName(RelativePath);

    public bool TickedByDefault => Warning is null;
}

/// <summary>Something a card will not move, and why.</summary>
public sealed record DesktopLeftAlone(string Name, string Reason);

/// <summary>What a card would do, and the plan behind it. Nothing has changed yet.</summary>
/// <param name="Facts">How each listed thing looked, checked again right before it moves.</param>
public sealed record DesktopMovePreview(
    DesktopMoveCard Card,
    AuthorizedRoot Root,
    OrganizationPlan Plan,
    IReadOnlyList<DesktopMoveItem> Items,
    IReadOnlyList<DesktopLeftAlone> LeftAlone,
    IReadOnlyDictionary<Guid, ExpectedFile> Facts);

/// <summary>The words the moving cards use, kept in one place so the page and tests agree.</summary>
public static class DesktopMoveText
{
    public static string Things(int count) => count == 1 ? "1 thing" : $"{count} things";

    /// <summary>"3 folders holding 1,204 files, plus 5 files", as the design asks.</summary>
    public static string Total(IEnumerable<DesktopMoveItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items.ToList();
        var folders = list.Where(item => item.IsFolder).ToList();
        var looseFiles = list.Count - folders.Count;
        var atLeast = folders.Any(folder => !folder.LookedAllTheWay) ? "at least " : string.Empty;
        var folderText = folders.Count == 0 ? null : $"{Count(folders.Count, "folder")} holding {atLeast}{Count(folders.Sum(folder => folder.FileCount), "file")}";
        var fileText = looseFiles == 0 ? null : Count(looseFiles, "file");
        return (folderText, fileText) switch
        {
            (null, null) => "Nothing",
            ({ } onlyFolders, null) => onlyFolders,
            (null, { } onlyFiles) => onlyFiles,
            ({ } both, { } plus) => $"{both}, plus {plus}",
        };
    }

    /// <summary>The most important warning first; null when there is none.</summary>
    public static string? WarningFor(DesktopThingWarnings warnings) =>
        (warnings & DesktopThingWarnings.ActiveProject) != 0 ? "It looks like a project you're working on. Moving it can break programs that remember where it is."
        : (warnings & DesktopThingWarnings.HasPrograms) != 0 ? "It has programs inside. Moving it can break shortcuts or games that remember where it is."
        : (warnings & DesktopThingWarnings.OnlineOnly) != 0 ? "Some of it is stored online only."
        : (warnings & DesktopThingWarnings.NotFullyLooked) != 0 ? "DeskAI couldn't look all the way inside."
        : null;

    private static string Count(int count, string word) =>
        count == 1 ? $"1 {word}" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} {word}s";
}

/// <summary>
/// Works out what Clear old stuff and Folder by group would move (ADR 0044). Plain rules over a
/// fresh look; no AI and no disk access. Every move lands in a folder directly on the Desktop.
/// </summary>
public static class DesktopMovePlanner
{
    public const string OldStuffFolder = "Old stuff";

    /// <summary>Everything unchanged for <see cref="StorageSummaryService.OldFileAge"/> goes into Old stuff.</summary>
    public static DesktopMovePreview ClearOldStuff(
        AuthorizedRoot root, DesktopInventory inventory, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var cutoff = nowUtc - StorageSummaryService.OldFileAge;
        var existing = inventory.Things.FirstOrDefault(thing => string.Equals(thing.Name, OldStuffFolder, StringComparison.OrdinalIgnoreCase));
        var builder = new Builder(root, DesktopMoveCard.ClearOldStuff, nowUtc, policyVersion, isProtected);
        foreach (var thing in inventory.Things.Where(thing => !ReferenceEquals(thing, existing) && thing.LastChangedUtc <= cutoff))
        {
            if (thing.IsFolder && !thing.LookedAllTheWay)
            {
                builder.LeaveAlone(thing.Name, "DeskAI couldn't look all the way inside, so it can't tell whether it's old.");
                continue;
            }

            builder.Move(thing, OldStuffFolder, existing, $"Unchanged since {thing.LastChangedUtc:yyyy-MM-dd}", OperationProvenance.Heuristic);
        }

        return builder.Build(PlanPurpose.ClearOldStuff);
    }

    /// <summary>Each group on the board goes into a folder with its name. Not sure stays where it is.</summary>
    public static DesktopMovePreview FolderByGroup(
        AuthorizedRoot root, DesktopInventory inventory, DesktopGroupBoard board, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(board);
        var byPath = inventory.Things.ToDictionary(thing => thing.RelativePath, StringComparer.OrdinalIgnoreCase);
        var groupNames = board.Groups.Select(group => group.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builder = new Builder(root, DesktopMoveCard.FolderByGroup, nowUtc, policyVersion, isProtected);
        foreach (var group in board.Groups)
        {
            byPath.TryGetValue(group.Name, out var existing);
            foreach (var path in group.Items)
            {
                if (!byPath.TryGetValue(path, out var thing))
                {
                    builder.LeaveAlone(Path.GetFileName(path), "It is no longer on your Desktop.");
                }
                else if (thing.IsFolder && string.Equals(thing.Name, group.Name, StringComparison.OrdinalIgnoreCase))
                {
                    builder.LeaveAlone(thing.Name, $"This is the {group.Name} folder itself, so the rest of the group goes into it.");
                }
                else if (thing.IsFolder && groupNames.Contains(thing.Name))
                {
                    builder.LeaveAlone(thing.Name, $"It's where the {thing.Name} group goes, so it stays.");
                }
                else
                {
                    builder.Move(thing, group.Name, existing, $"In your {group.Name} group", OperationProvenance.User);
                }
            }
        }

        return builder.Build(PlanPurpose.FolderByGroup);
    }

    private sealed class Builder(
        AuthorizedRoot root, DesktopMoveCard card, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected)
    {
        private readonly List<PlanOperation> _creates = [];
        private readonly List<PlanOperation> _moves = [];
        private readonly List<DesktopMoveItem> _items = [];
        private readonly List<DesktopLeftAlone> _leftAlone = [];
        private readonly Dictionary<Guid, ExpectedFile> _facts = [];
        private readonly HashSet<string> _made = new(StringComparer.OrdinalIgnoreCase);

        public void LeaveAlone(string name, string reason) => _leftAlone.Add(new(name, reason));

        /// <param name="existing">What already has the destination's name on the Desktop, if anything.</param>
        public void Move(DesktopThing thing, string destination, DesktopThing? existing, string reason, OperationProvenance provenance)
        {
            var target = Path.Combine(destination, thing.Name);
            if (existing is { IsFolder: false })
            {
                LeaveAlone(thing.Name, $"A file called {destination} is in the way, so its folder can't be made.");
                return;
            }

            if (existing is not null && existing.ChildNames.Contains(thing.Name))
            {
                LeaveAlone(thing.Name, $"{destination} already has something called {thing.Name}, so nothing was replaced.");
                return;
            }

            if (isProtected(thing.RelativePath) || isProtected(target))
            {
                LeaveAlone(thing.Name, "DeskAI's safety rules keep it where it is.");
                return;
            }

            if (existing is null && _made.Add(destination))
            {
                _creates.Add(new CreateDirectoryOperation(Guid.NewGuid(), destination, $"A folder called {destination} on your Desktop.", provenance));
            }

            var id = Guid.NewGuid();
            _moves.Add(thing.IsFolder
                ? new MoveFolderOperation(id, thing.RelativePath, target, reason, provenance)
                : new MoveFileOperation(id, thing.RelativePath, target, reason, provenance));
            _items.Add(new DesktopMoveItem(
                id, thing.RelativePath, thing.IsFolder, destination, thing.LastChangedUtc, thing.FileCount, thing.LookedAllTheWay,
                DesktopMoveText.WarningFor(thing.Warnings)));
            _facts[id] = thing.Facts;
        }

        public DesktopMovePreview Build(PlanPurpose purpose) =>
            new(card, root,
                OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, nowUtc, policyVersion, [.. _creates, .. _moves], purpose: purpose),
                _items, _leftAlone, _facts);
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet build DeskAI.sln -c Release --no-restore` then
`dotnet test tests/DeskAI.Core.Tests -c Release --no-build --no-restore`
Expected: 0 warnings; all Core tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/DeskAI.Core/Studio tests/DeskAI.Core.Tests
git commit -m "Look at what is on the Desktop and work out what Clear old stuff and Folder by group would move"
```

---

### Task 6: Move, Put back, and the yes, as one service; Organize leaves Studio changes alone

**Files:**
- Create: `src/DeskAI.Core/Studio/DesktopMoveService.cs`
- Modify: `src/DeskAI.Core/Tidy/TidyRunService.cs`
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs`
- Test: `tests/DeskAI.Presentation.Tests/DesktopMoveServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 2–5; `DesktopGroupingService.FindDesktopAsync`, `IDesktopGroupRepository`, `IFolderTidyExecutor`, `IOperationJournal`, `TidyRunService.FindInterruptedAsync`/`KeepInterruptedAsync`/`LastTidyLookBack`, `IPlanSafetyCheck.PolicyVersion`/`IsProtected`.
- Produces (namespace `DeskAI.Core.Studio`):
  - `sealed record DesktopMovePreviewResult(DesktopMovePreview? Preview, string Message)`
  - `sealed record DesktopMoveResult(bool NeedsPermission, int Moved, int Tried, IReadOnlyList<DesktopLeftAlone> NotMoved, string Summary)`
  - `sealed record DesktopLastChange(DesktopMoveCard Card, Guid TransactionId, DateTimeOffset FinishedAtUtc, IReadOnlyDictionary<Guid, string> Moved)`
  - `sealed class DesktopMoveService` with `const string FindGroupsFirst`, `const string PermissionNeeded`, `const string PutBackPermissionNeeded`, and methods `CanMoveAsync(Guid rootId, ct)` → `bool`, `AllowAsync(Guid rootId, ct)` → `string`, `StopAsync(Guid rootId, ct)` → `string`, `PreviewAsync(Guid rootId, DesktopMoveCard card, ct)` → `DesktopMovePreviewResult`, `ApplyAsync(DesktopMovePreview preview, IReadOnlyCollection<Guid> ticked, ct)` → `DesktopMoveResult`, `FindLastAsync(Guid rootId, DesktopMoveCard card, ct)` → `DesktopLastChange?`, `PutBackAsync(Guid rootId, DesktopMoveCard card, ct)` → `DesktopMoveResult`, `FindInterruptedAsync(Guid rootId, ct)` → `InterruptedTidy?`, `KeepInterruptedAsync(Guid rootId, InterruptedTidy interrupted, ct)` → `string?`, `PutBackInterruptedAsync(Guid rootId, InterruptedTidy interrupted, ct)` → `DesktopMoveResult`.
  - `TidyRunService.FindLastAsync` returns null when the latest change that moved something is not a tidy.

- [ ] **Step 1: Write the failing tests**

`tests/DeskAI.Presentation.Tests/DesktopMoveServiceTests.cs`:

```csharp
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Core.Studio;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The Desktop Studio moving service over the real stack (ADR 0044), on a generated Desktop inside
/// the test's own folder. The page tests use the same service through the page.
/// </summary>
public sealed class DesktopMoveServiceTests
{
    private static readonly TimeSpan SevenMonths = TimeSpan.FromDays(213);

    [Fact]
    public async Task Moving_needs_its_own_yes_and_that_yes_is_not_tidying()
    {
        await using var app = await TestApp.StartAsync();
        MakeOldDesktop(app);
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        var tidying = app.Get<TidyPermissionService>();
        await tidying.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        var preview = (await moves.PreviewAsync(desktop.Id, DesktopMoveCard.ClearOldStuff, TestContext.Current.CancellationToken)).Preview!;
        var all = preview.Items.Select(item => item.OperationId).ToList();

        var refused = await moves.ApplyAsync(preview, all, TestContext.Current.CancellationToken);

        Assert.True(refused.NeedsPermission);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old project")));

        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        await tidying.StopAsync(desktop.Id, TestContext.Current.CancellationToken);
        var moved = await moves.ApplyAsync(preview, all, TestContext.Current.CancellationToken);

        Assert.False(moved.NeedsPermission);
        Assert.Equal(2, moved.Moved);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old stuff", "Old project")));
        var root = await app.Get<IAuthorizedRootRepository>().FindAsync(desktop.Id, TestContext.Current.CancellationToken);
        Assert.False(RootCapabilities.CanTidy(root!));
        Assert.True(RootCapabilities.CanMoveFolders(root!));
    }

    [Fact]
    public async Task Put_back_is_offered_only_for_the_latest_change_on_the_Desktop()
    {
        await using var app = await TestApp.StartAsync();
        MakeOldDesktop(app);
        app.MakeFile("Desktop", "report.docx");
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        await ApplyAllAsync(moves, desktop.Id, DesktopMoveCard.ClearOldStuff);

        Assert.NotNull(await moves.FindLastAsync(desktop.Id, DesktopMoveCard.ClearOldStuff, TestContext.Current.CancellationToken));
        Assert.Null(await moves.FindLastAsync(desktop.Id, DesktopMoveCard.FolderByGroup, TestContext.Current.CancellationToken));

        await app.Get<DesktopGroupingService>().GuessAsync(desktop.Id, TestContext.Current.CancellationToken);
        await ApplyAllAsync(moves, desktop.Id, DesktopMoveCard.FolderByGroup);

        Assert.Null(await moves.FindLastAsync(desktop.Id, DesktopMoveCard.ClearOldStuff, TestContext.Current.CancellationToken));
        Assert.NotNull(await moves.FindLastAsync(desktop.Id, DesktopMoveCard.FolderByGroup, TestContext.Current.CancellationToken));
        var refused = await moves.PutBackAsync(desktop.Id, DesktopMoveCard.ClearOldStuff, TestContext.Current.CancellationToken);
        Assert.Equal("There is nothing to put back.", refused.Summary);
    }

    [Fact]
    public async Task Organize_offers_no_undo_for_a_Desktop_Studio_change()
    {
        await using var app = await TestApp.StartAsync();
        MakeOldDesktop(app);
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);

        await ApplyAllAsync(moves, desktop.Id, DesktopMoveCard.ClearOldStuff);

        Assert.Null(await app.Get<TidyRunService>().FindLastAsync(desktop.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Folder_by_group_sends_nothing()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app, shareNames: true, shareFolderNames: true);
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        app.MakeFile("Desktop", "report.docx");
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        await app.Get<DesktopGroupingService>().GuessAsync(desktop.Id, TestContext.Current.CancellationToken);

        var result = await ApplyAllAsync(moves, desktop.Id, DesktopMoveCard.FolderByGroup);

        Assert.Equal(2, result.Moved);
        Assert.Empty(app.Internet.Requests);
    }

    internal static void MakeOldDesktop(TestApp app)
    {
        app.MakeFile("Desktop", Path.Combine("Old project", "main.py"), age: SevenMonths);
        app.MakeFile("Desktop", Path.Combine("Old project", "notes.md"), age: SevenMonths);
        Age(app, "Old project");
        app.MakeFile("Desktop", "old notes.txt", age: SevenMonths);
        app.MakeFile("Desktop", Path.Combine("Current work", "draft.docx"));
        Age(app, "Current work");
        app.MakeFile("Desktop", "this week.pdf");
    }

    /// <summary>Makes a generated folder's own last-changed time old, as it is for a folder nobody touched.</summary>
    internal static void Age(TestApp app, string folder) =>
        Directory.SetLastWriteTimeUtc(Path.Combine(app.DesktopPath, folder), DateTime.UtcNow - SevenMonths);

    internal static async Task<AuthorizedRoot> ConnectDesktopAsync(TestApp app)
    {
        var folders = app.Get<PersonalFoldersViewModel>();
        await folders.ReloadAsync();
        Assert.NotNull(await folders.ConnectAsync(PersonalFolderKind.Desktop));
        return (await app.Get<DesktopGroupingService>().FindDesktopAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<DesktopMoveResult> ApplyAllAsync(DesktopMoveService moves, Guid desktopId, DesktopMoveCard card)
    {
        var preview = (await moves.PreviewAsync(desktopId, card, TestContext.Current.CancellationToken)).Preview!;
        return await moves.ApplyAsync(preview, preview.Items.Select(item => item.OperationId).ToList(), TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/DeskAI.Presentation.Tests -c Release --no-restore`
Expected: build errors — `DesktopMoveService` and `DesktopMoveResult` do not exist.

- [ ] **Step 3: Write `DesktopMoveService.cs`**

```csharp
using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Tidy;

namespace DeskAI.Core.Studio;

/// <summary>A card's preview, or the plain reason there is none.</summary>
public sealed record DesktopMovePreviewResult(DesktopMovePreview? Preview, string Message);

/// <summary>What pressing Move or Put back did.</summary>
/// <param name="NeedsPermission">Nothing was tried because moving things on the Desktop is not allowed; the page asks, then tries again.</param>
public sealed record DesktopMoveResult(
    bool NeedsPermission, int Moved, int Tried, IReadOnlyList<DesktopLeftAlone> NotMoved, string Summary);

/// <summary>The latest change a card made on the Desktop, which its Put back can undo.</summary>
/// <param name="Moved">Each moved thing's original place by operation ID, so Put back can name it.</param>
public sealed record DesktopLastChange(
    DesktopMoveCard Card, Guid TransactionId, DateTimeOffset FinishedAtUtc, IReadOnlyDictionary<Guid, string> Moved);

/// <summary>
/// Clear old stuff and Folder by group (ADR 0044): the preview, Move, Put back, the separate yes,
/// and a change that stopped part-way.
/// </summary>
/// <remarks>
/// <para>
/// Only the connected Desktop is served; any other folder is refused. The approval covers exactly
/// the ticked rows plus the folders they go into, and each moved thing carries how it looked in
/// the preview, which the executor checks again right before it moves.
/// </para>
/// <para>
/// No AI is involved: Folder by group reads the board the person already saw and could change.
/// Put back is offered only for the latest change on the Desktop, and only by the card that made
/// it; undoing out of order is not what Put back means.
/// </para>
/// </remarks>
public sealed class DesktopMoveService(
    DesktopGroupingService grouping,
    IReadOnlyFolderService folders,
    IFolderMovePermissions permissions,
    DesktopInventoryService inventory,
    IDesktopGroupRepository boards,
    IFolderTidyExecutor executor,
    IOperationJournal journal,
    TidyRunService runs,
    IPlanSafetyCheck safety,
    IClock clock)
{
    public const string FindGroupsFirst = "Find groups first, then DeskAI can put each group into its own folder.";
    public const string PermissionNeeded = "DeskAI needs your permission before it moves anything on your Desktop.";
    public const string PutBackPermissionNeeded = "Putting things back moves them too, so DeskAI needs your permission to move things on your Desktop again.";

    public async Task<bool> CanMoveAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is { } root && RootCapabilities.CanMoveFolders(root);

    /// <summary>Records the yes. Called only after the page's dialog was accepted.</summary>
    public async Task<string> AllowAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return DesktopGroupingService.NotConnected;
        }

        if (!RootCapabilities.CanReadMetadata(root))
        {
            return "Your Desktop was not connected in a way that lets DeskAI move things.";
        }

        // Checked now, not trusted from when it was connected: it may have become a link since.
        if (await folders.CheckStillSafeAsync(root, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        await permissions.AllowAsync(rootId, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return "DeskAI may now move the things you tick on your Desktop. It never deletes anything.";
    }

    /// <summary>Taking the yes back needs no confirmation; that is never the dangerous direction.</summary>
    public async Task<string> StopAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await permissions.StopAsync(rootId, cancellationToken).ConfigureAwait(false);
        return "DeskAI can no longer move things on your Desktop. Nothing was moved.";
    }

    public async Task<DesktopMovePreviewResult> PreviewAsync(Guid rootId, DesktopMoveCard card, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(null, DesktopGroupingService.NotConnected);
        }

        DesktopGroupBoard? board = null;
        if (card == DesktopMoveCard.FolderByGroup)
        {
            board = await boards.LoadAsync(rootId, cancellationToken).ConfigureAwait(false);
            if (board is null || board.Groups.All(group => group.Items.Count == 0))
            {
                return new(null, FindGroupsFirst);
            }
        }

        var seen = await inventory.LookAsync(root, cancellationToken).ConfigureAwait(false);
        if (seen.Problem is not null)
        {
            return new(null, seen.Problem);
        }

        bool IsProtected(string path) => safety.IsProtected(root, path);
        var preview = card == DesktopMoveCard.ClearOldStuff
            ? DesktopMovePlanner.ClearOldStuff(root, seen, clock.UtcNow, safety.PolicyVersion, IsProtected)
            : DesktopMovePlanner.FolderByGroup(root, seen, board!, clock.UtcNow, safety.PolicyVersion, IsProtected);
        var message = preview.Items.Count > 0
            ? string.Empty
            : card == DesktopMoveCard.ClearOldStuff
                ? "Nothing on your Desktop has been left unchanged for 6 months."
                : "Nothing in your groups can go into folders right now.";
        return new(preview, message);
    }

    public async Task<DesktopMoveResult> ApplyAsync(
        DesktopMovePreview preview, IReadOnlyCollection<Guid> ticked, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(ticked);
        if (await DesktopAsync(preview.Root.Id, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(false, 0, 0, [], DesktopGroupingService.NotConnected);
        }

        if (!RootCapabilities.CanMoveFolders(root))
        {
            return new(true, 0, 0, [], PermissionNeeded);
        }

        var items = preview.Items.Where(item => ticked.Contains(item.OperationId)).ToList();
        if (items.Count == 0)
        {
            return new(false, 0, 0, [], "Nothing was ticked, so nothing moved.");
        }

        var destinations = items.Select(item => item.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var creates = preview.Plan.Operations
            .OfType<CreateDirectoryOperation>()
            .Where(create => destinations.Contains(create.DestinationRelativePath))
            .Select(create => create.Id);
        var approval = Approval.Create(Guid.NewGuid(), preview.Plan, creates.Concat(items.Select(item => item.OperationId)), clock.UtcNow);
        var expected = items.ToDictionary(item => item.OperationId, item => preview.Facts[item.OperationId]);

        var result = await executor.ExecuteAsync(preview.Plan, approval, expected, cancellationToken).ConfigureAwait(false);

        var outcomes = result.Operations.ToDictionary(outcome => outcome.OperationId);
        var notMoved = items
            .Where(item => !(outcomes.TryGetValue(item.OperationId, out var outcome) && outcome.Outcome == ExecutionOutcome.Completed))
            .Select(item => new DesktopLeftAlone(item.Name, outcomes.GetValueOrDefault(item.OperationId)?.Error ?? "It was not moved."))
            .ToList();
        var moved = items.Count - notMoved.Count;
        var into = preview.Card == DesktopMoveCard.ClearOldStuff
            ? DesktopMovePlanner.OldStuffFolder
            : destinations.Count == 1 ? $"the {destinations.Single()} folder" : $"{destinations.Count} folders";
        var summary = notMoved.Count == 0
            ? $"Done. {DesktopMoveText.Things(moved)} moved into {into}."
            : moved == 0
                ? "Nothing was moved."
                : $"{moved} of {items.Count} things moved. The rest stayed where they were.";
        return new(false, moved, items.Count, notMoved, summary);
    }

    /// <summary>The card's own latest change, only if it is the latest change on the Desktop and not undone.</summary>
    public async Task<DesktopLastChange?> FindLastAsync(Guid rootId, DesktopMoveCard card, CancellationToken cancellationToken = default)
    {
        var records = await journal.ListForRootAsync(rootId, TidyRunService.LastTidyLookBack, cancellationToken).ConfigureAwait(false);
        var undone = records
            .Where(record => record.Kind == ExecutionTransactionKind.Undo &&
                             record.OriginalTransactionId is not null &&
                             record.State is not (ExecutionTransactionState.Failed or ExecutionTransactionState.Cancelled))
            .Select(record => record.OriginalTransactionId!.Value)
            .ToHashSet();
        foreach (var record in records.Where(record => record.Kind == ExecutionTransactionKind.Execute))
        {
            if (record.State is ExecutionTransactionState.Prepared or ExecutionTransactionState.Executing
                or ExecutionTransactionState.RecoveryRequired)
            {
                return null;
            }

            var moved = record.Operations
                .Where(operation => operation.Kind is PlanOperationKind.MoveFile or PlanOperationKind.MoveFolder &&
                                    operation.State == JournalOperationState.Completed &&
                                    operation.SourceRelativePath is not null)
                .ToDictionary(operation => operation.OperationId, operation => operation.SourceRelativePath!);
            if (moved.Count == 0)
            {
                continue;
            }

            return record.Purpose != PurposeOf(card) || record.State == ExecutionTransactionState.Undone || undone.Contains(record.Id)
                ? null
                : new DesktopLastChange(card, record.Id, record.FinishedAtUtc ?? record.StartedAtUtc, moved);
        }

        return null;
    }

    public async Task<DesktopMoveResult> PutBackAsync(Guid rootId, DesktopMoveCard card, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(false, 0, 0, [], DesktopGroupingService.NotConnected);
        }

        if (!RootCapabilities.CanMoveFolders(root))
        {
            return new(true, 0, 0, [], PutBackPermissionNeeded);
        }

        return await FindLastAsync(rootId, card, cancellationToken).ConfigureAwait(false) is { } last
            ? await UndoAsync(last.TransactionId, last.Moved, cancellationToken).ConfigureAwait(false)
            : new(false, 0, 0, [], "There is nothing to put back.");
    }

    /// <summary>A change on the Desktop that stopped part-way, checked against the disk; asked about first.</summary>
    public Task<InterruptedTidy?> FindInterruptedAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        runs.FindInterruptedAsync(rootId, cancellationToken);

    /// <summary>"Keep them where they are": closes the record as a change of what moved. Moves nothing.</summary>
    public Task<string?> KeepInterruptedAsync(Guid rootId, InterruptedTidy interrupted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interrupted);
        return runs.KeepInterruptedAsync(rootId, interrupted.TransactionId, cancellationToken);
    }

    /// <summary>"Put them back": closes the record, then puts back what it proved had moved, with Put back's own checks.</summary>
    public async Task<DesktopMoveResult> PutBackInterruptedAsync(
        Guid rootId, InterruptedTidy interrupted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interrupted);
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(false, 0, 0, [], DesktopGroupingService.NotConnected);
        }

        // Asked before closing, so refusing leaves the question open exactly as it was.
        if (!RootCapabilities.CanMoveFolders(root))
        {
            return new(true, 0, 0, [], PutBackPermissionNeeded);
        }

        if (!interrupted.CanUndo)
        {
            return new(false, 0, 0, [], "Nothing had moved, so there is nothing to put back.");
        }

        if (await runs.KeepInterruptedAsync(rootId, interrupted.TransactionId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return new(false, 0, 0, [], problem);
        }

        return await UndoAsync(interrupted.TransactionId, interrupted.MovedFiles, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DesktopMoveResult> UndoAsync(Guid transactionId, IReadOnlyDictionary<Guid, string> moved, CancellationToken cancellationToken)
    {
        UndoResult result;
        try
        {
            result = await executor.UndoAsync(transactionId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return new(false, 0, 0, [], exception.Message);
        }

        var named = result.Operations.Where(outcome => moved.ContainsKey(outcome.OperationId)).ToList();
        var back = named.Count(outcome => outcome.Outcome == ExecutionOutcome.Completed);
        var notBack = named
            .Where(outcome => outcome.Outcome != ExecutionOutcome.Completed)
            .Select(outcome => new DesktopLeftAlone(Path.GetFileName(moved[outcome.OperationId]), outcome.Error ?? "It was not moved back."))
            .ToList();
        var summary = notBack.Count == 0
            ? $"Put back. {DesktopMoveText.Things(back)} {(back == 1 ? "is where it was" : "are where they were")}."
            : $"{back} of {named.Count} things went back. The rest stayed where they are now.";
        return new(false, back, named.Count, notBack, summary);
    }

    private static PlanPurpose PurposeOf(DesktopMoveCard card) => card switch
    {
        DesktopMoveCard.ClearOldStuff => PlanPurpose.ClearOldStuff,
        DesktopMoveCard.FolderByGroup => PlanPurpose.FolderByGroup,
        _ => throw new ArgumentOutOfRangeException(nameof(card)),
    };

    /// <summary>The root, only when it is the connected Desktop. Any other folder is refused.</summary>
    private async Task<AuthorizedRoot?> DesktopAsync(Guid rootId, CancellationToken cancellationToken) =>
        await grouping.FindDesktopAsync(cancellationToken).ConfigureAwait(false) is { } desktop && desktop.Id == rootId ? desktop : null;
}
```

- [ ] **Step 4: Organize's Undo leaves Studio changes alone**

In `src/DeskAI.Core/Tidy/TidyRunService.cs`, in `FindLastAsync`, directly after the
`if (moved.Count == 0) { continue; }` block insert:

```csharp
            // Only the latest change in the folder can be undone, and Organize undoes only its own
            // tidies; a Desktop Studio change is put back from Desktop Studio (ADR 0044).
            if (record.Purpose != PlanPurpose.Tidy)
            {
                return null;
            }
```

- [ ] **Step 5: Register the services**

In `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs`, after
`services.AddSingleton<DesktopGroupingService>();` add:

```csharp
        services.AddSingleton<DesktopInventoryService>();
        services.AddSingleton<DesktopMoveService>();
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet build DeskAI.sln -c Release --no-restore` then
`dotnet test DeskAI.sln -c Release --no-build --no-restore`
Expected: 0 warnings; all tests pass. `Organize_offers_no_undo_for_a_Desktop_Studio_change` must
fail with Step 4 reverted (check once by commenting the new `if` out and running that test).

- [ ] **Step 7: Commit**

```bash
git add src/DeskAI.Core src/DeskAI.Presentation/Composition tests/DeskAI.Presentation.Tests/DesktopMoveServiceTests.cs
git commit -m "Move, put back, and allow moving on the Desktop through one service; Organize leaves those changes alone"
```

---

### Task 7: The two cards on the Desktop Studio page

**Files:**
- Create: `src/DeskAI.Presentation/ViewModels/DesktopMoveCardViewModel.cs`
- Modify: `src/DeskAI.Presentation/ViewModels/DesktopStudioViewModel.cs`
- Modify: `src/DeskAI.App/Views/DesktopStudioPage.xaml`, `src/DeskAI.App/Views/DesktopStudioPage.xaml.cs`
- Modify: `src/DeskAI.Presentation/Help/HelpCatalog.cs`
- Modify: `docs/TESTING.md` (Feature Coverage Map)
- Test: `tests/DeskAI.Presentation.Tests/DesktopStudioMovePageTests.cs`, `tests/DeskAI.Presentation.Tests/DesktopStudioLayoutTests.cs`

**Interfaces:**
- Consumes: Task 6's `DesktopMoveService` and result types; `DesktopMoveServiceTests.MakeOldDesktop`, `Age`, `ConnectDesktopAsync` (internal static test helpers).
- Produces (namespace `DeskAI.App.ViewModels`):
  - `DesktopMoveItemViewModel` (`Item`, `Name`, `Glyph`, `Detail`, `Warning`, `HasWarning`, `IsTicked`)
  - `DesktopMoveCardViewModel` (`Card`, `Items`, `LeftAlone` (strings), `Preview`, `HasPreview`, `HasLeftAlone`, `Ticked`, `CanApply`, `ApplyButtonText`, `TotalText`, `CanPutBack`, `LastText`, `Message`, `HasMessage`)
  - `DesktopStudioViewModel` adds `OldStuff`, `FolderByGroup`, `CanMoveThings`, `CannotMoveThings`, `MovesMessage`, `HasMovesMessage`, `HasInterrupted`, `InterruptedText`, `CanPutBackInterrupted`, and `PreviewAsync(card)`, `ApplyAsync(card)` → `DesktopMoveResult?`, `PutBackAsync(card)` → `DesktopMoveResult?`, `AllowMovingAsync()`, `StopMovingAsync()`, `KeepInterruptedAsync()`, `PutBackInterruptedAsync()` → `DesktopMoveResult?`.

- [ ] **Step 1: Write the failing page tests**

`tests/DeskAI.Presentation.Tests/DesktopStudioMovePageTests.cs`:

```csharp
using DeskAI.App.ViewModels;
using DeskAI.Core.Execution;
using DeskAI.Core.Studio;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Desktop Studio's Clear old stuff and Folder by group cards (ADR 0044), used the way a person
/// uses them, on a generated Desktop inside the test's own folder.
/// </summary>
public sealed class DesktopStudioMovePageTests
{
    private static readonly TimeSpan SevenMonths = TimeSpan.FromDays(213);

    [Fact]
    public async Task Clear_old_stuff_shows_only_old_things_and_moves_nothing_until_Move()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var before = Snapshot(app);
        var studio = await OpenAsync(app);

        await studio.PreviewAsync(studio.OldStuff);

        Assert.Equal(["old notes.txt", "Old project"], studio.OldStuff.Items.Select(item => item.Name));
        Assert.All(studio.OldStuff.Items, item => Assert.True(item.IsTicked));
        Assert.Equal("Ticked: 1 folder holding 2 files, plus 1 file", studio.OldStuff.TotalText);
        Assert.Equal("Move 2 things", studio.OldStuff.ApplyButtonText);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task Move_asks_for_the_yes_first_then_moves_the_ticked_things_into_Old_stuff()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAsync(app);
        await studio.PreviewAsync(studio.OldStuff);

        Assert.False(studio.CanMoveThings);
        var refused = await studio.ApplyAsync(studio.OldStuff);

        Assert.True(refused!.NeedsPermission);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old project")));

        // What the page does after the person accepts the dialog.
        await studio.AllowMovingAsync();
        var done = await studio.ApplyAsync(studio.OldStuff);

        Assert.True(studio.CanMoveThings);
        Assert.Equal("Done. 2 things moved into Old stuff.", done!.Summary);
        Assert.Equal(done.Summary, studio.OldStuff.Message);
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Old stuff", "Old project", "main.py")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Old stuff", "old notes.txt")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "this week.pdf")));
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Current work")));
        Assert.False(studio.OldStuff.HasPreview);
        Assert.True(studio.OldStuff.CanPutBack);
    }

    [Fact]
    public async Task Put_back_returns_everything_and_removes_the_Old_stuff_folder_DeskAI_made()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var before = Snapshot(app);
        var studio = await OpenAllowedAsync(app);
        await studio.PreviewAsync(studio.OldStuff);
        await studio.ApplyAsync(studio.OldStuff);

        var back = await studio.PutBackAsync(studio.OldStuff);

        Assert.Equal("Put back. 2 things are where they were.", back!.Summary);
        Assert.Equal(before, Snapshot(app));
        Assert.False(studio.OldStuff.CanPutBack);
    }

    [Fact]
    public async Task Put_back_still_works_after_reopening_DeskAI()
    {
        await using var first = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(first);
        var before = Snapshot(first);
        var studio = await OpenAllowedAsync(first);
        await studio.PreviewAsync(studio.OldStuff);
        await studio.ApplyAsync(studio.OldStuff);
        await using var app = await first.ReopenAsync();

        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();

        Assert.True(again.OldStuff.CanPutBack);
        Assert.StartsWith("Last change: 2 things moved, at ", again.OldStuff.LastText, StringComparison.Ordinal);
        await again.PutBackAsync(again.OldStuff);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task A_project_folder_starts_unticked_with_its_warning()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Game mod", "package.json"), age: SevenMonths);
        DesktopMoveServiceTests.Age(app, "Game mod");
        app.MakeFile("Desktop", Path.Combine("Tools", "setup.exe"), age: SevenMonths);
        DesktopMoveServiceTests.Age(app, "Tools");
        var studio = await OpenAllowedAsync(app);

        await studio.PreviewAsync(studio.OldStuff);

        var project = studio.OldStuff.Items.Single(item => item.Name == "Game mod");
        Assert.False(project.IsTicked);
        Assert.StartsWith("It looks like a project", project.Warning, StringComparison.Ordinal);
        Assert.False(studio.OldStuff.Items.Single(item => item.Name == "Tools").IsTicked);
        Assert.False(studio.OldStuff.CanApply);

        project.IsTicked = true;
        Assert.Equal("Move 1 thing", studio.OldStuff.ApplyButtonText);
        await studio.ApplyAsync(studio.OldStuff);

        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old stuff", "Game mod")));
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Tools")));
    }

    [Fact]
    public async Task An_Old_stuff_folder_already_there_is_used_and_kept_and_a_clash_is_left_alone()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Old stuff", "old notes.txt"));
        app.MakeFile("Desktop", "old notes.txt", age: SevenMonths);
        app.MakeFile("Desktop", "ancient.txt", age: SevenMonths);
        var studio = await OpenAllowedAsync(app);

        await studio.PreviewAsync(studio.OldStuff);

        Assert.Equal(["ancient.txt"], studio.OldStuff.Items.Select(item => item.Name));
        Assert.Contains(studio.OldStuff.LeftAlone, line =>
            line.StartsWith("old notes.txt: Old stuff already has something called old notes.txt", StringComparison.Ordinal));

        await studio.ApplyAsync(studio.OldStuff);
        await studio.PutBackAsync(studio.OldStuff);

        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Old stuff", "old notes.txt")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "ancient.txt")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "old notes.txt")));
    }

    [Fact]
    public async Task A_folder_changed_after_the_list_stays_where_it_is()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.PreviewAsync(studio.OldStuff);
        File.WriteAllText(Path.Combine(app.DesktopPath, "Old project", "new idea.txt"), "Generated DeskAI test data");

        var result = await studio.ApplyAsync(studio.OldStuff);

        Assert.Equal("1 of 2 things moved. The rest stayed where they were.", result!.Summary);
        Assert.Contains(studio.OldStuff.LeftAlone, line => line.StartsWith("Old project: It changed after the list", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old project")));
    }

    [Fact]
    public async Task Folder_by_group_puts_each_group_into_its_own_folder_and_Put_back_undoes_it()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        app.MakeFile("Desktop", Path.Combine("Python stuff", "utils.py"));
        app.MakeFile("Desktop", Path.Combine("Essays", "essay.docx"));
        app.MakeFile("Desktop", "report.docx");
        app.MakeFile("Desktop", "holiday.jpg");
        app.MakeFile("Desktop", "mystery.zzz");
        var before = Snapshot(app);
        var studio = await OpenAllowedAsync(app);
        await studio.GuessAsync();

        await studio.PreviewAsync(studio.FolderByGroup);

        Assert.Contains(studio.FolderByGroup.Items, item => item.Name == "Python stuff" && item.Detail.EndsWith("goes into Coding", StringComparison.Ordinal));
        Assert.DoesNotContain(studio.FolderByGroup.Items, item => item.Name == "mystery.zzz");
        var done = await studio.ApplyAsync(studio.FolderByGroup);

        Assert.StartsWith("Done. ", done!.Summary, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Coding", "Python stuff", "main.py")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Documents", "report.docx")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Pictures", "holiday.jpg")));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "mystery.zzz")));

        await studio.PutBackAsync(studio.FolderByGroup);

        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task Folder_by_group_without_groups_says_find_groups_first()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAsync(app);

        await studio.PreviewAsync(studio.FolderByGroup);

        Assert.False(studio.FolderByGroup.HasPreview);
        Assert.Equal(DesktopMoveService.FindGroupsFirst, studio.FolderByGroup.Message);
    }

    [Fact]
    public async Task Stop_moving_things_takes_the_yes_back()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);

        await studio.StopMovingAsync();

        Assert.False(studio.CanMoveThings);
        await studio.PreviewAsync(studio.OldStuff);
        Assert.True((await studio.ApplyAsync(studio.OldStuff))!.NeedsPermission);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Old project")));
    }

    [Fact]
    public async Task Organize_does_not_offer_to_undo_a_Desktop_Studio_change()
    {
        await using var app = await TestApp.StartAsync();
        DesktopMoveServiceTests.MakeOldDesktop(app);
        var studio = await OpenAllowedAsync(app);
        await studio.PreviewAsync(studio.OldStuff);
        await studio.ApplyAsync(studio.OldStuff);

        var organize = app.Get<TidyViewModel>();
        await organize.InitializeAsync();
        await organize.ConnectAndSelectAsync(app.DesktopPath);

        Assert.Equal("Desktop", organize.SelectedFolder?.Name);
        Assert.False(organize.CanUndo);
    }

    [Fact]
    public async Task An_interrupted_move_is_asked_about_and_Put_them_back_returns_it()
    {
        await using var first = await TestApp.StartStoppableAsync();
        DesktopMoveServiceTests.MakeOldDesktop(first);
        var before = Snapshot(first);
        var studio = await OpenAllowedAsync(first);
        await studio.PreviewAsync(studio.OldStuff);
        var folder = studio.OldStuff.Items.Single(item => item.Name == "Old project");
        first.Stopping.StopBefore(folder.Item.OperationId, JournalOperationState.Completed);
        await Assert.ThrowsAsync<SimulatedStop>(() => studio.ApplyAsync(studio.OldStuff));
        await using var app = await first.ReopenAsync();

        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();

        Assert.True(again.HasInterrupted);
        Assert.Contains("2 of 2 things had moved", again.InterruptedText, StringComparison.Ordinal);
        Assert.False(again.OldStuff.CanPutBack);
        var back = await again.PutBackInterruptedAsync();

        Assert.False(back!.NeedsPermission);
        Assert.Equal(before, Snapshot(app));
        Assert.False(again.HasInterrupted);
    }

    [Fact]
    public async Task DeskAIs_program_folder_and_hidden_things_never_appear_or_move()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile(Path.Combine("Desktop", "DeskAI", "app"), "DeskAI.App.exe", age: SevenMonths);
        Directory.SetLastWriteTimeUtc(app.ProgramFolderPath, DateTime.UtcNow - SevenMonths);
        DesktopMoveServiceTests.Age(app, "DeskAI");
        var hidden = app.MakeFile("Desktop", "secret.txt", age: SevenMonths);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        app.MakeFile("Desktop", "old notes.txt", age: SevenMonths);
        var studio = await OpenAllowedAsync(app);

        await studio.PreviewAsync(studio.OldStuff);

        Assert.Equal(["old notes.txt"], studio.OldStuff.Items.Select(item => item.Name));
        await studio.ApplyAsync(studio.OldStuff);
        Assert.True(File.Exists(Path.Combine(app.ProgramFolderPath, "DeskAI.App.exe")));
        Assert.True(File.Exists(hidden));
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Old stuff", "old notes.txt")));
    }

    private static async Task<DesktopStudioViewModel> OpenAsync(TestApp app)
    {
        await DesktopMoveServiceTests.ConnectDesktopAsync(app);
        var studio = app.Get<DesktopStudioViewModel>();
        await studio.InitializeAsync();
        Assert.True(studio.IsDesktopConnected);
        return studio;
    }

    private static async Task<DesktopStudioViewModel> OpenAllowedAsync(TestApp app)
    {
        var studio = await OpenAsync(app);
        await studio.AllowMovingAsync();
        Assert.True(studio.CanMoveThings);
        return studio;
    }

    private static string[] Snapshot(TestApp app)
    {
        Directory.CreateDirectory(app.DesktopPath);
        return Directory.EnumerateFileSystemEntries(app.DesktopPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(app.DesktopPath, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
```

Append to `tests/DeskAI.Presentation.Tests/DesktopStudioLayoutTests.cs` (inside the class):

```csharp
    [Fact]
    public void Each_moving_card_has_its_help_its_tick_boxes_its_total_and_Put_back()
    {
        var page = Page();
        foreach (var (topic, card) in new[] { ("studio.oldStuff", "OldStuff"), ("studio.folderByGroup", "FolderByGroup") })
        {
            var section = Section(page, $"Topic=\"{topic}\"", "</Border>");
            Assert.Contains($"ViewModel.{card}.Items", section, StringComparison.Ordinal);
            Assert.Contains($"ViewModel.{card}.TotalText", section, StringComparison.Ordinal);
            Assert.Contains("Content=\"Put back\"", section, StringComparison.Ordinal);
        }

        var row = Section(page, "<DataTemplate x:Key=\"MoveItemTemplate\"", "</DataTemplate>");
        Assert.Contains("IsChecked=\"{x:Bind IsTicked, Mode=TwoWay}\"", row, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Warning}\"", row, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/DeskAI.Presentation.Tests -c Release --no-restore`
Expected: build errors — `OldStuff`, `FolderByGroup`, `AllowMovingAsync`, and the other new members
do not exist on `DesktopStudioViewModel`.

- [ ] **Step 3: Write `DesktopMoveCardViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Studio;

namespace DeskAI.App.ViewModels;

/// <summary>One row a moving card lists, with its tick box.</summary>
public sealed class DesktopMoveItemViewModel(DesktopMoveItem item, DesktopMoveCard card) : ObservableObject
{
    private bool _isTicked = item.TickedByDefault;

    public DesktopMoveItem Item { get; } = item;

    public string Name => Item.Name;

    /// <summary>Folder or page glyph from Segoe Fluent Icons.</summary>
    public string Glyph => Item.IsFolder ? "" : "";

    public string Detail => card == DesktopMoveCard.ClearOldStuff
        ? $"{Kind()} · last changed {Item.LastChangedUtc.ToLocalTime():d}"
        : $"{Kind()} · goes into {Item.Destination}";

    public string Warning => Item.Warning ?? string.Empty;

    public bool HasWarning => Item.Warning is not null;

    public bool IsTicked
    {
        get => _isTicked;
        set => SetProperty(ref _isTicked, value);
    }

    private string Kind()
    {
        if (!Item.IsFolder)
        {
            return "File";
        }

        var atLeast = Item.LookedAllTheWay ? string.Empty : "at least ";
        return Item.FileCount == 1 ? $"Folder with {atLeast}1 file" : $"Folder with {atLeast}{Item.FileCount:N0} files";
    }
}

/// <summary>
/// One Desktop Studio card that moves things: what it would move, the total of what is ticked,
/// and its last change for Put back. It holds no service; <see cref="DesktopStudioViewModel"/> fills it.
/// </summary>
public sealed class DesktopMoveCardViewModel(DesktopMoveCard card) : ObservableObject
{
    private DesktopMovePreview? _preview;
    private DesktopLastChange? _last;
    private string _message = string.Empty;

    public DesktopMoveCard Card { get; } = card;

    public ObservableCollection<DesktopMoveItemViewModel> Items { get; } = [];

    /// <summary>"Name: reason" for each thing left where it is.</summary>
    public ObservableCollection<string> LeftAlone { get; } = [];

    public DesktopMovePreview? Preview => _preview;

    public bool HasPreview => Items.Count > 0;

    public bool HasLeftAlone => LeftAlone.Count > 0;

    public IReadOnlyList<Guid> Ticked => Items.Where(item => item.IsTicked).Select(item => item.Item.OperationId).ToList();

    public bool CanApply => Items.Any(item => item.IsTicked);

    public string ApplyButtonText => $"Move {DesktopMoveText.Things(Items.Count(item => item.IsTicked))}";

    public string TotalText => HasPreview
        ? $"Ticked: {DesktopMoveText.Total(Items.Where(item => item.IsTicked).Select(item => item.Item))}"
        : string.Empty;

    public bool CanPutBack => _last is not null;

    public string LastText => _last is null
        ? string.Empty
        : $"Last change: {DesktopMoveText.Things(_last.Moved.Count)} moved, at {_last.FinishedAtUtc.ToLocalTime():t} on {_last.FinishedAtUtc.ToLocalTime():d}.";

    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => Message.Length > 0;

    internal void ShowPreview(DesktopMovePreview? preview)
    {
        foreach (var row in Items)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Items.Clear();
        LeftAlone.Clear();
        _preview = preview;
        if (preview is not null)
        {
            foreach (var item in preview.Items)
            {
                var row = new DesktopMoveItemViewModel(item, Card);
                row.PropertyChanged += OnRowChanged;
                Items.Add(row);
            }

            foreach (var alone in preview.LeftAlone)
            {
                LeftAlone.Add($"{alone.Name}: {alone.Reason}");
            }
        }

        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasLeftAlone));
        RaiseTicks();
    }

    /// <summary>After Move or Put back: the rows go, and anything not moved is listed with its reason.</summary>
    internal void ShowOutcome(string summary, IReadOnlyList<DesktopLeftAlone> notMoved)
    {
        ShowPreview(null);
        foreach (var alone in notMoved)
        {
            LeftAlone.Add($"{alone.Name}: {alone.Reason}");
        }

        OnPropertyChanged(nameof(HasLeftAlone));
        Message = summary;
    }

    internal void ShowLast(DesktopLastChange? last)
    {
        _last = last;
        OnPropertyChanged(nameof(CanPutBack));
        OnPropertyChanged(nameof(LastText));
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopMoveItemViewModel.IsTicked))
        {
            RaiseTicks();
        }
    }

    private void RaiseTicks()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyButtonText));
        OnPropertyChanged(nameof(TotalText));
    }
}
```

- [ ] **Step 4: Extend `DesktopStudioViewModel`**

Add `DesktopMoveService moves` as the last primary-constructor parameter, a field
`private readonly DesktopMoveService _moves = moves;`, and `using DeskAI.Core.Tidy;`. Replace the
class summary's remark "Nothing here can move, rename, or open a file, or change Windows." with
"Find groups changes nothing on the PC; the moving cards change the Desktop only through
<see cref="DesktopMoveService"/>, after the person ticks and presses Move." Then add:

```csharp
    private InterruptedTidy? _interrupted;
    private bool _canMoveThings;
    private string _movesMessage = string.Empty;

    public DesktopMoveCardViewModel OldStuff { get; } = new(DesktopMoveCard.ClearOldStuff);

    public DesktopMoveCardViewModel FolderByGroup { get; } = new(DesktopMoveCard.FolderByGroup);

    public bool CanMoveThings
    {
        get => _canMoveThings;
        private set
        {
            if (SetProperty(ref _canMoveThings, value))
            {
                OnPropertyChanged(nameof(CannotMoveThings));
            }
        }
    }

    public bool CannotMoveThings => !CanMoveThings;

    public string MovesMessage
    {
        get => _movesMessage;
        private set
        {
            if (SetProperty(ref _movesMessage, value))
            {
                OnPropertyChanged(nameof(HasMovesMessage));
            }
        }
    }

    public bool HasMovesMessage => MovesMessage.Length > 0;

    public bool HasInterrupted => _interrupted is not null;

    public bool CanPutBackInterrupted => _interrupted?.CanUndo == true;

    public string InterruptedText
    {
        get
        {
            if (_interrupted is not { } stopped)
            {
                return string.Empty;
            }

            var text = stopped.IsUndo
                ? $"DeskAI stopped while putting things back. {stopped.Moved} of {DesktopMoveText.Things(stopped.Total)} had gone back."
                : $"DeskAI stopped part-way through your last change. {stopped.Moved} of {DesktopMoveText.Things(stopped.Total)} had moved.";
            return stopped.NeedsReview.Count == 0
                ? text
                : $"{text} DeskAI couldn't tell about {string.Join(", ", stopped.NeedsReview.Select(item => item.FileName))}, so it left them alone. Please check them.";
        }
    }

    public Task PreviewAsync(DesktopMoveCardViewModel card) => WithDesktopDoAsync(async id =>
    {
        ArgumentNullException.ThrowIfNull(card);
        var result = await _moves.PreviewAsync(id, card.Card).ConfigureAwait(true);
        card.ShowPreview(result.Preview);
        card.Message = result.Message;
    });

    /// <returns>What happened, or null when nothing was tried. When it needs the yes, the page asks and calls again.</returns>
    public async Task<DesktopMoveResult?> ApplyAsync(DesktopMoveCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (_desktopId is not { } id || card.Preview is not { } preview)
        {
            return null;
        }

        DesktopMoveResult? result = null;
        await RunAsync(async () =>
        {
            result = await _moves.ApplyAsync(preview, card.Ticked).ConfigureAwait(true);
            if (result.NeedsPermission)
            {
                card.Message = result.Summary;
                return;
            }

            card.ShowOutcome(result.Summary, result.NotMoved);
            await RefreshMovesAsync(id).ConfigureAwait(true);
        }).ConfigureAwait(true);
        return result;
    }

    public async Task<DesktopMoveResult?> PutBackAsync(DesktopMoveCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (_desktopId is not { } id)
        {
            return null;
        }

        DesktopMoveResult? result = null;
        await RunAsync(async () =>
        {
            result = await _moves.PutBackAsync(id, card.Card).ConfigureAwait(true);
            if (result.NeedsPermission)
            {
                card.Message = result.Summary;
                return;
            }

            card.ShowOutcome(result.Summary, result.NotMoved);
            await RefreshMovesAsync(id).ConfigureAwait(true);
        }).ConfigureAwait(true);
        return result;
    }

    /// <summary>Called only after the page's permission dialog was accepted.</summary>
    public Task AllowMovingAsync() => WithDesktopDoAsync(async id =>
    {
        var message = await _moves.AllowAsync(id).ConfigureAwait(true);
        await RefreshMovesAsync(id).ConfigureAwait(true);
        MovesMessage = CanMoveThings ? string.Empty : message;
    });

    public Task StopMovingAsync() => WithDesktopDoAsync(async id =>
    {
        MovesMessage = await _moves.StopAsync(id).ConfigureAwait(true);
        await RefreshMovesAsync(id).ConfigureAwait(true);
    });

    public Task KeepInterruptedAsync() => WithDesktopDoAsync(async id =>
    {
        if (_interrupted is { } stopped)
        {
            MovesMessage = await _moves.KeepInterruptedAsync(id, stopped).ConfigureAwait(true) ?? "Kept. Everything stays where it is now.";
        }

        await RefreshMovesAsync(id).ConfigureAwait(true);
    });

    public async Task<DesktopMoveResult?> PutBackInterruptedAsync()
    {
        if (_desktopId is not { } id || _interrupted is not { } stopped)
        {
            return null;
        }

        DesktopMoveResult? result = null;
        await RunAsync(async () =>
        {
            result = await _moves.PutBackInterruptedAsync(id, stopped).ConfigureAwait(true);
            MovesMessage = result.Summary;
            if (!result.NeedsPermission)
            {
                await RefreshMovesAsync(id).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
        return result;
    }

    /// <summary>The yes, a change that stopped part-way (asked about before anything else), and each card's Put back.</summary>
    private async Task RefreshMovesAsync(Guid id)
    {
        CanMoveThings = await _moves.CanMoveAsync(id).ConfigureAwait(true);
        _interrupted = await _moves.FindInterruptedAsync(id).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasInterrupted));
        OnPropertyChanged(nameof(InterruptedText));
        OnPropertyChanged(nameof(CanPutBackInterrupted));
        OldStuff.ShowLast(_interrupted is null ? await _moves.FindLastAsync(id, DesktopMoveCard.ClearOldStuff).ConfigureAwait(true) : null);
        FolderByGroup.ShowLast(_interrupted is null ? await _moves.FindLastAsync(id, DesktopMoveCard.FolderByGroup).ConfigureAwait(true) : null);
    }

    private Task WithDesktopDoAsync(Func<Guid, Task> action)
    {
        if (_desktopId is not { } id)
        {
            Message = DesktopGroupingService.NotConnected;
            return Task.CompletedTask;
        }

        return RunAsync(() => action(id));
    }
```

In `InitializeAsync`, inside `if (_desktopId is { } id) { ... }`, after `Message = loaded.Message;`
add `await RefreshMovesAsync(id).ConfigureAwait(true);`.

- [ ] **Step 5: The page**

In `src/DeskAI.App/Views/DesktopStudioPage.xaml`, add to `<Page.Resources>`:

```xml
        <x:Double x:Key="MoveListMaxHeight">320</x:Double>

        <DataTemplate x:Key="MoveItemTemplate" x:DataType="viewmodels:DesktopMoveItemViewModel">
            <Grid ColumnDefinitions="Auto,Auto,*" ColumnSpacing="8" Padding="0,2">
                <CheckBox IsChecked="{x:Bind IsTicked, Mode=TwoWay}" MinWidth="0" VerticalAlignment="Top"
                          AutomationProperties.Name="{x:Bind Name}" />
                <FontIcon Grid.Column="1" Glyph="{x:Bind Glyph}" FontSize="14" Margin="0,8,0,0" VerticalAlignment="Top"
                          Foreground="{ThemeResource DeskTextSecondaryBrush}" />
                <StackPanel Grid.Column="2" Margin="0,5,0,0">
                    <TextBlock Text="{x:Bind Name}" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{x:Bind Name}" />
                    <TextBlock Style="{StaticResource CaptionStyle}" Text="{x:Bind Detail}" TextWrapping="Wrap" />
                    <TextBlock Style="{StaticResource CaptionStyle}" Text="{x:Bind Warning}" TextWrapping="Wrap"
                               Visibility="{x:Bind HasWarning}" />
                </StackPanel>
            </Grid>
        </DataTemplate>

        <DataTemplate x:Key="LeftAloneTemplate">
            <TextBlock Style="{StaticResource CaptionStyle}" Text="{Binding}" TextWrapping="Wrap" />
        </DataTemplate>
```

After the board's closing `</StackPanel>` (the one opened with
`Visibility="{x:Bind ViewModel.HasBoard, Mode=OneWay}"`) and before the closing `</StackPanel>` of
`PageContent`, add:

```xml
            <!-- A change that stopped part-way is asked about before anything else moves. -->
            <Border Style="{StaticResource CardStyle}" Visibility="{x:Bind ViewModel.HasInterrupted, Mode=OneWay}">
                <StackPanel Spacing="10">
                    <TextBlock Style="{StaticResource SectionTitleStyle}" Text="Your last change stopped part-way" />
                    <TextBlock TextWrapping="Wrap" Text="{x:Bind ViewModel.InterruptedText, Mode=OneWay}" />
                    <StackPanel Orientation="Horizontal" Spacing="10">
                        <Button Content="Keep them where they are" Click="OnKeepInterruptedClick" />
                        <Button Content="Put them back" Visibility="{x:Bind ViewModel.CanPutBackInterrupted, Mode=OneWay}"
                                Click="OnPutBackInterruptedClick" />
                    </StackPanel>
                </StackPanel>
            </Border>

            <StackPanel Spacing="6" Visibility="{x:Bind ViewModel.IsDesktopConnected, Mode=OneWay}">
                <TextBlock Style="{StaticResource CaptionStyle}" TextWrapping="Wrap"
                           Visibility="{x:Bind ViewModel.CannotMoveThings, Mode=OneWay}"
                           Text="The cards below move things on your Desktop. DeskAI asks before the first move, and Put back returns everything." />
                <HyperlinkButton Content="Stop DeskAI moving things on my Desktop" Padding="0"
                                 Visibility="{x:Bind ViewModel.CanMoveThings, Mode=OneWay}" Click="OnStopMovingClick" />
                <TextBlock Style="{StaticResource CaptionStyle}" TextWrapping="Wrap"
                           Text="{x:Bind ViewModel.MovesMessage, Mode=OneWay}" Visibility="{x:Bind ViewModel.HasMovesMessage, Mode=OneWay}" />
            </StackPanel>

            <Border Style="{StaticResource CardStyle}" Visibility="{x:Bind ViewModel.IsDesktopConnected, Mode=OneWay}">
                <StackPanel Spacing="14">
                    <StackPanel Spacing="4">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <TextBlock Style="{StaticResource SectionTitleStyle}" Text="Clear old stuff" />
                            <controls:HelpButton Topic="studio.oldStuff" VerticalAlignment="Center" />
                        </StackPanel>
                        <TextBlock Style="{StaticResource BodySecondaryStyle}" TextWrapping="Wrap"
                                   Text="Folders and files you haven't changed in 6 months go into one Old stuff folder on your Desktop. Nothing is deleted." />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" Spacing="10">
                        <Button Content="Show what would move" Tag="OldStuff" Click="OnPreviewClick" />
                        <Button Style="{StaticResource AccentButtonStyle}" Tag="OldStuff" Click="OnApplyClick"
                                Content="{x:Bind ViewModel.OldStuff.ApplyButtonText, Mode=OneWay}"
                                Visibility="{x:Bind ViewModel.OldStuff.HasPreview, Mode=OneWay}"
                                IsEnabled="{x:Bind ViewModel.OldStuff.CanApply, Mode=OneWay}" />
                        <Button Content="Put back" Tag="OldStuff" Click="OnPutBackClick"
                                Visibility="{x:Bind ViewModel.OldStuff.CanPutBack, Mode=OneWay}" />
                    </StackPanel>
                    <TextBlock Style="{StaticResource CaptionStyle}" TextWrapping="Wrap"
                               Text="{x:Bind ViewModel.OldStuff.LastText, Mode=OneWay}" Visibility="{x:Bind ViewModel.OldStuff.CanPutBack, Mode=OneWay}" />
                    <TextBlock Style="{StaticResource CaptionStyle}" TextWrapping="Wrap"
                               Text="{x:Bind ViewModel.OldStuff.Message, Mode=OneWay}" Visibility="{x:Bind ViewModel.OldStuff.HasMessage, Mode=OneWay}" />
                    <StackPanel Spacing="8" Visibility="{x:Bind ViewModel.OldStuff.HasPreview, Mode=OneWay}">
                        <TextBlock FontWeight="SemiBold" Text="{x:Bind ViewModel.OldStuff.TotalText, Mode=OneWay}" />
                        <ScrollViewer MaxHeight="{StaticResource MoveListMaxHeight}" VerticalScrollBarVisibility="Auto">
                            <ItemsControl ItemsSource="{x:Bind ViewModel.OldStuff.Items}" AutomationProperties.Name="Old things that would move"
                                          ItemTemplate="{StaticResource MoveItemTemplate}" />
                        </ScrollViewer>
                    </StackPanel>
                    <StackPanel Spacing="4" Visibility="{x:Bind ViewModel.OldStuff.HasLeftAlone, Mode=OneWay}">
                        <TextBlock Style="{StaticResource CaptionStyle}" FontWeight="SemiBold" Text="Left where they are" />
                        <ItemsControl ItemsSource="{x:Bind ViewModel.OldStuff.LeftAlone}" AutomationProperties.Name="Old things left where they are"
                                      ItemTemplate="{StaticResource LeftAloneTemplate}" />
                    </StackPanel>
                </StackPanel>
            </Border>

            <Border Style="{StaticResource CardStyle}" Visibility="{x:Bind ViewModel.IsDesktopConnected, Mode=OneWay}">
                <StackPanel Spacing="14">
                    <StackPanel Spacing="4">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <TextBlock Style="{StaticResource SectionTitleStyle}" Text="Folder by group" />
                            <controls:HelpButton Topic="studio.folderByGroup" VerticalAlignment="Center" />
                        </StackPanel>
                        <TextBlock Style="{StaticResource BodySecondaryStyle}" TextWrapping="Wrap"
                                   Text="Each group from Find groups goes into its own folder on your Desktop. Not sure stays where it is." />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" Spacing="10">
                        <Button Content="Show what would move" Tag="FolderByGroup" Click="OnPreviewClick" />
                        <Button Style="{StaticResource AccentButtonStyle}" Tag="FolderByGroup" Click="OnApplyClick"
                                Content="{x:Bind ViewModel.FolderByGroup.ApplyButtonText, Mode=OneWay}"
                                Visibility="{x:Bind ViewModel.FolderByGroup.HasPreview, Mode=OneWay}"
                                IsEnabled="{x:Bind ViewModel.FolderByGroup.CanApply, Mode=OneWay}" />
                        <Button Content="Put back" Tag="FolderByGroup" Click="OnPutBackClick"
                                Visibility="{x:Bind ViewModel.FolderByGroup.CanPutBack, Mode=OneWay}" />
                    </StackPanel>
                    <TextBlock Style="{StaticResource CaptionStyle}" TextWrapping="Wrap"
                               Text="{x:Bind ViewModel.FolderByGroup.LastText, Mode=OneWay}" Visibility="{x:Bind ViewModel.FolderByGroup.CanPutBack, Mode=OneWay}" />
                    <TextBlock Style="{StaticResource CaptionStyle}" TextWrapping="Wrap"
                               Text="{x:Bind ViewModel.FolderByGroup.Message, Mode=OneWay}" Visibility="{x:Bind ViewModel.FolderByGroup.HasMessage, Mode=OneWay}" />
                    <StackPanel Spacing="8" Visibility="{x:Bind ViewModel.FolderByGroup.HasPreview, Mode=OneWay}">
                        <TextBlock FontWeight="SemiBold" Text="{x:Bind ViewModel.FolderByGroup.TotalText, Mode=OneWay}" />
                        <ScrollViewer MaxHeight="{StaticResource MoveListMaxHeight}" VerticalScrollBarVisibility="Auto">
                            <ItemsControl ItemsSource="{x:Bind ViewModel.FolderByGroup.Items}" AutomationProperties.Name="Group things that would move"
                                          ItemTemplate="{StaticResource MoveItemTemplate}" />
                        </ScrollViewer>
                    </StackPanel>
                    <StackPanel Spacing="4" Visibility="{x:Bind ViewModel.FolderByGroup.HasLeftAlone, Mode=OneWay}">
                        <TextBlock Style="{StaticResource CaptionStyle}" FontWeight="SemiBold" Text="Left where they are" />
                        <ItemsControl ItemsSource="{x:Bind ViewModel.FolderByGroup.LeftAlone}" AutomationProperties.Name="Group things left where they are"
                                      ItemTemplate="{StaticResource LeftAloneTemplate}" />
                    </StackPanel>
                </StackPanel>
            </Border>
```

In `src/DeskAI.App/Views/DesktopStudioPage.xaml.cs`, change the class summary to: "Desktop Studio
(ADR 0042, ADR 0044). Its dialogs are the only places a person says yes: Connect; Send, which shows
the exact list the AI would see; and the one-time yes to move things on the Desktop. Everything
else is the view model's." Add:

```csharp
    private DesktopMoveCardViewModel? CardOf(object sender) => (sender as FrameworkElement)?.Tag switch
    {
        "OldStuff" => ViewModel.OldStuff,
        "FolderByGroup" => ViewModel.FolderByGroup,
        _ => null,
    };

    private async void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy && CardOf(sender) is { } card)
        {
            await ViewModel.PreviewAsync(card);
        }
    }

    /// <summary>The first Move asks for the yes, then tries once more with the same ticked list.</summary>
    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || CardOf(sender) is not { } card)
        {
            return;
        }

        if (await ViewModel.ApplyAsync(card) is { NeedsPermission: true } && await ConfirmMovingAsync())
        {
            await ViewModel.AllowMovingAsync();
            await ViewModel.ApplyAsync(card);
        }
    }

    /// <summary>Put back moves things too, so after the yes was taken back it asks again first.</summary>
    private async void OnPutBackClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || CardOf(sender) is not { } card)
        {
            return;
        }

        if (await ViewModel.PutBackAsync(card) is { NeedsPermission: true } && await ConfirmMovingAsync())
        {
            await ViewModel.AllowMovingAsync();
            await ViewModel.PutBackAsync(card);
        }
    }

    private async void OnPutBackInterruptedClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            return;
        }

        if (await ViewModel.PutBackInterruptedAsync() is { NeedsPermission: true } && await ConfirmMovingAsync())
        {
            await ViewModel.AllowMovingAsync();
            await ViewModel.PutBackInterruptedAsync();
        }
    }

    private async void OnKeepInterruptedClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy)
        {
            await ViewModel.KeepInterruptedAsync();
        }
    }

    /// <summary>Taking the yes back needs no dialog; that is never the dangerous direction.</summary>
    private async void OnStopMovingClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy)
        {
            await ViewModel.StopMovingAsync();
        }
    }

    private async Task<bool> ConfirmMovingAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Allow DeskAI to move things on your Desktop?",
            Content = "DeskAI may move folders and files on your Desktop into folders on your Desktop: only the ones you tick, and only when you press Move.\n"
                + "It never deletes anything and never moves anything off your Desktop.\n"
                + "Put back returns them.\n\nYou can take this back at any time.",
            PrimaryButtonText = "Allow moving",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
```

- [ ] **Step 6: Help topics**

In `src/DeskAI.Presentation/Help/HelpCatalog.cs`, after the `studio.groups` entry:

```csharp
        new("studio.oldStuff", "Clear old stuff",
            "A card that gathers what you haven't changed in 6 months into one Old stuff folder on your Desktop.",
            "DeskAI lists each old folder and file with a tick box first. Only what you tick moves, after you press Move. Put back returns everything.",
            "It never deletes anything and never moves anything off your Desktop. Projects and programs start unticked."),
        new("studio.folderByGroup", "Folder by group",
            "A card that puts each group from Find groups into its own folder on your Desktop.",
            "DeskAI lists what would go into which folder first. Only what you tick moves, after you press Move. Not sure stays where it is, and Put back returns everything.",
            "It never deletes anything, uses no AI, and never moves anything off your Desktop."),
```

- [ ] **Step 7: Feature Coverage Map**

In `docs/TESTING.md`, directly after the Desktop Studio "Find groups (ADR 0042)" row, add:

```markdown
| Desktop Studio | Clear old stuff and Folder by group (ADR 0044): the preview lists only things unchanged for 6 months (a folder by the newest thing inside it) and moves nothing; Move asks for its own yes first (the tidy yes is not enough, and it grants no tidying), then moves only the ticked things; the total reads "1 folder holding 2 files, plus 1 file"; Put back returns everything and removes only an Old stuff folder DeskAI made, also after reopening; project and program folders start unticked with a warning; an Old stuff folder already there is used and kept, and a same-name clash is left alone with its reason; a folder changed after the list stays with its reason; Folder by group puts each group into its own folder, leaves Not sure, sends nothing, and says to find groups first when there are none; Stop takes the yes back; Organize offers no undo for a Desktop Studio change; a change interrupted part-way is asked about after reopening and Put them back returns it; DeskAI's program folder and hidden things never appear or move; each card has help, tick boxes, a total, and Put back | `DesktopStudioMovePageTests`, `DesktopMoveServiceTests`, `DesktopStudioLayoutTests`, `DesktopMovePlannerTests`, `DesktopInventoryServiceTests`, `FolderTidyExecutorTests`, `PlanValidatorTests`, `OrganizationPlanTests`, `RootCapabilitiesTests`, `SqlitePlanningPersistenceTests`, `SqliteAuthorizedRootRepositoryTests`, `SqliteDatabaseInitializerTests`, `WindowsMetadataScannerTests` |
```

- [ ] **Step 8: Run the tests to see them pass**

Run: `dotnet build DeskAI.sln -c Release --no-restore` then
`dotnet test DeskAI.sln -c Release --no-build --no-restore`
Expected: 0 warnings; all tests pass, including `HelpCatalogTests`, `HelpPlacementTests`,
`AccessibilityNameTests`, and `NoPlaceholderUiTests`. If
`Organize_does_not_offer_to_undo_a_Desktop_Studio_change` fails because `ConnectAndSelectAsync`
refuses a folder that is already connected, select it instead with
`organize.SelectedFolder = organize.Folders.Single(folder => folder.Name == "Desktop");` followed by
`await organize.RefreshCommand.ExecuteAsync(null);`, and ledger the change.

- [ ] **Step 9: Commit**

```bash
git add src/DeskAI.Presentation src/DeskAI.App docs/TESTING.md tests/DeskAI.Presentation.Tests
git commit -m "Add Clear old stuff and Folder by group to Desktop Studio"
```

---

### Task 8: Documents, handoff, and the full check

**Files:**
- Modify: `docs/ROADMAP.md`, `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, `docs/UI-UX.md`, `docs/USER-GUIDE.md`, `docs/superpowers/specs/2026-09-24-desktop-studio-design.md`, `docs/HANDOFF.md`

**Interfaces:** documents only.

- [ ] **Step 1: Update the documents**

- `docs/ROADMAP.md`: replace `- Step 3 — **Clear old stuff** and **Folder by group** (move a folder). Planned.` with
  `- Step 3 — **Clear old stuff** and **Folder by group** (ADR 0044): built on the branch; moving things on the Desktop needs its own yes, and each card has Put back.`
- `docs/ARCHITECTURE.md`: after the "Desktop Studio — Find groups (ADR 0042)" paragraph, add:
  "**Desktop Studio — moving things (ADR 0044).** `DesktopInventoryService` makes a read-only
  look at the Desktop (8 levels, 20,000 entries) with dates, file counts, and warnings;
  `DesktopMovePlanner` turns it, and for Folder by group the saved board, into a plan whose
  `Purpose` names the card. `DesktopMoveService` approves exactly the ticked rows and the folders
  they need and hands the plan to the one executor, which now also moves a whole folder
  (`MoveFolderOperation`, one rename) and puts it back by its made-at time. Only a non-Tidy plan
  may move a folder, and the executor checks the grant that matches the plan's purpose:
  `CanTidy` for tidies, `CanMoveFolders` (table `folder_move_permissions`, schema 17) for Desktop
  Studio."
- `docs/SECURITY.md`: add this section before "## Backup Files and Start Fresh":

```markdown
## Desktop Studio Moving Things (ADR 0044)

Clear old stuff and Folder by group move folders and files on the connected Desktop only. They
need their own yes, separate from tidying: the tidy dialog promises that DeskAI never touches
what is inside a folder, so that yes can never be read as permission to move one, and this yes
never grants tidying. A folder moves whole, with one rename, only after the person ticks it and
presses Move; every check a file move makes has a folder twin, and Put back moves a folder back
only if it is still the same folder. Project, program, and online-only folders start unticked
with a warning. Nothing is deleted; the only folder ever removed is an empty one DeskAI made in
that run, on Put back. No AI is involved. Review: `docs/security/2026-09-24-desktop-moves-review.md`.
```

- `docs/UI-UX.md`: after the "Desktop Studio (2026-09-24, ADR 0042)" paragraph, add: "**Moving
  cards (ADR 0044).** Below the board: a caption saying DeskAI asks before the first move, or a
  quiet "Stop DeskAI moving things on my Desktop" link once allowed; a "Your last change stopped
  part-way" card when needed, with Keep them where they are and Put them back; then **Clear old
  stuff** and **Folder by group**, each with Show what would move, a list of tick boxes (folder or
  file, how many files inside, last changed or which folder it goes into, and a warning line that
  keeps the row unticked), the ticked total, Move N things, Put back with its last-change line, and
  Left where they are with each reason. The first Move shows the dialog "Allow DeskAI to move
  things on your Desktop?"."
- `docs/USER-GUIDE.md`, in the "Desktop Studio" section, add: "**Clear old stuff** gathers folders
  and files you haven't changed in 6 months into one **Old stuff** folder on your Desktop.
  **Folder by group** puts each group from Find groups into its own folder. Press **Show what would
  move**, untick anything you want to keep where it is, and press **Move**. The first time, DeskAI
  asks for your permission to move things on your Desktop — this is separate from tidying on
  Organize. **Put back** returns everything, even after you close DeskAI. Folders that look like
  projects or hold programs start unticked, because moving them can break shortcuts." Also change
  the line near the top saying Desktop Studio "So far it has one" card to say it has three: Find
  groups, Clear old stuff, and Folder by group.
- `docs/superpowers/specs/2026-09-24-desktop-studio-design.md`, "Disk features": change "They use
  the **existing Tidy flow**: the 'Allow tidying' permission, a preview …" so it reads "They use the
  **existing Tidy flow** — a preview with one tick box per item, approval, the one executor, the
  write-ahead journal, interrupted-run recovery, and **Put back** that survives restarts — but
  under **their own yes**, not the 'Allow tidying' permission, whose dialog promises never to touch
  what is inside a folder (ADR 0044). Put back is offered for the latest change on the Desktop
  only." Keep the rest of that bullet (the total example). Under "Build order" add
  "Step 3 built 2026-09-24 (ADR 0044)."

- [ ] **Step 2: Rewrite the top of `docs/HANDOFF.md`**

Follow the file's "How to update this file" section. "Start here" must say: Desktop Studio steps
1 and 3 are built on `desktop-studio-find-groups`, step 2 was dropped after the probe (ADR 0043),
nothing is pushed (owner, 2026-09-24); the build and test numbers from Step 3 below; what step 3
built in two sentences (ADR 0044); the owner's manual checks for step 3 (Show what would move on
Clear old stuff; Move and accept the dialog; look at the Old stuff folder; Put back; Move again,
close and reopen DeskAI, and check Put back is still offered; Find groups, then Folder by group,
Move, Put back; Stop DeskAI moving things; check that Organize's tidy permission for the Desktop
did not change); and the next task: **Tag names** (step 4, rename a folder), which needs its own
plan, ADR, and review — ask the owner first. Under "Decisions the owner made that are not yet in
code" keep the existing items and keep the line recorded with this plan's commit about skipping the
Find groups hand check. Add a dated section "## 2026-09-24 Desktop Studio step 3: Clear old stuff
and Folder by group" with the data flow, the safety points (the separate yes and why; plan
purpose; latest-only Put back), the final review's findings and fixes, and the known limits: a
Desktop so full that the look stops at 20,000 entries marks every folder as not fully looked at,
so Clear old stuff leaves folders alone there; after Folder by group, the Find groups board shows
the moved items as gone and the new group folders under Not sure the next time it opens; cloud
placeholder folders seen as links are never listed.

- [ ] **Step 3: Full verification**

Run the three verification commands. Expected: 0 warnings, every test passes, format clean.
Record the test count in HANDOFF's "Start here".

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "Record Desktop Studio step 3 in the roadmap, guides, design, and handoff"
```
