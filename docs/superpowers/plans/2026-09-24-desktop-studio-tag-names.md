# Desktop Studio Step 4: Tag names — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Desktop Studio card, right under the Find groups step, that puts each group's name in
front of the names of the folders in that group ("Python stuff" → "Coding – Python stuff"), with
the same tick-box preview, Move-style approval, and Put back as the other cards.

**Architecture:** On Windows, renaming a folder in place is the same single rename as moving it to
a new name in the same parent. So Tag names **reuses** `MoveFolderOperation` (ADR 0044) with the
destination in the same folder — every check, the journal, the check after a stop, and Put back
already exist and are tested. New: `PlanPurpose.TagNames`, `DesktopMoveCard.TagNames`, a pure
`DesktopMovePlanner.TagNames`, the service and page wiring, and the wording that says "rename"
where the person sees it. The same separate yes covers it; its dialog now says "move or rename".

**Tech Stack:** C# / .NET 10, WinUI 3, CommunityToolkit.Mvvm, SQLite, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-24-desktop-studio-design.md` ("Disk features": "**Tag
names** … rename a folder"; "Build order" step 4; table row "Tag names | The group name goes in
front of each folder's name ('Coding – Python stuff') | Renames folders | groups").

## Global Constraints

- "They use the existing Tidy flow — a preview with one tick box per item, approval, the one
  executor, the write-ahead journal, interrupted-run recovery, and **Put back** … — but under
  **their own yes**" (spec as amended by ADR 0044).
- "**Unticked by default, with a warning:** folders that look like active projects …, folders
  containing programs, and folders with online-only files. Moving or renaming these can break
  programs, shortcuts, or games that remember the old place."
- "**Left alone, with the reason shown:** a same-name clash (never overwritten or merged), a file
  in use, an item changed since the preview, protected or link items."
- "Tidy while I'm away never moves or renames folders."
- Only **folders** are renamed; files and Not sure keep their names (spec table: "each folder's
  name").
- Page order (owner, 2026-09-24): a step that builds on the groups goes right under the board;
  buttons say what they do.
- Tests use generated folders only. Commit each task locally on `desktop-studio-find-groups`;
  do not push.
- Verification: `dotnet build DeskAI.sln -c Release --no-restore --no-incremental`,
  `dotnet test DeskAI.sln -c Release --no-build --no-restore`,
  `dotnet format DeskAI.sln --no-restore --verify-no-changes`.

## Decisions this plan makes (ADR 0045, Task 1)

1. **A rename is a folder move to a new name in the same place.** No new executor action and no
   new schema: `MoveFolderOperation` already does one rename, never overwrites, re-checks the
   folder, journals first, and puts back by made-at time.
2. **The separator is " – "** (space, en dash, space), as in the spec's example.
3. **A folder whose name already starts with "Group – " is left alone** ("Its name already starts
   with the group's name."), so pressing it twice never gives "Coding – Coding – Python stuff".
4. **The new name must pass `FolderNameCheck`** (at most 64 characters, no reserved names), or the
   folder is left alone with the reason.
5. **The same separate yes** (`CanMoveFolders`) covers renaming; the dialog now says "move or
   rename". Nobody has given that yes yet (step 3 is not released), so no earlier consent widens.

## Review Focus

1. **The new name is already taken** on the Desktop (by a file, a folder, or a hidden/left-out
   item) → left alone with the reason; never merged. Pinned in Task 2
   (`Tag_names_never_takes_a_name_already_used`).
2. **Pressed twice** → no double prefix. Pinned in Task 2 and Task 4
   (`A_folder_already_named_with_its_group_is_left_alone`).
3. **A very long group or folder name** → the new name fails `FolderNameCheck` and the folder stays.
   Pinned in Task 2 (`Tag_names_checks_the_new_name`).
4. **A project or program folder** → unticked with the "renaming can break" warning. Pinned in
   Task 2 (`Tag_names_warns_before_renaming_a_project`).
5. **Put back after reopening** → the old names return. Pinned in Task 4
   (`Rename_then_Put_back_restores_the_old_names`).

---

## File Structure

| File | Responsibility |
|---|---|
| `docs/decisions/0045-tag-names-renames-folders.md`, `docs/security/2026-09-24-tag-names-review.md` | Decision and threat table |
| `src/DeskAI.Core/Plans/PlanPurpose.cs` | + `TagNames` |
| `src/DeskAI.Core/Studio/DesktopMovePlanner.cs` | + `DesktopMoveCard.TagNames`, `TagNames(...)`, `Builder.Rename`, warning wording |
| `src/DeskAI.Core/Studio/DesktopMoveService.cs` | Preview / summary / purpose for the new card |
| `src/DeskAI.Presentation/ViewModels/DesktopMoveCardViewModel.cs`, `DesktopStudioViewModel.cs` | The card |
| `src/DeskAI.App/Views/DesktopStudioPage.xaml(.cs)` | The card under step 2; dialog wording |
| `src/DeskAI.Presentation/Help/HelpCatalog.cs` | `studio.tagNames` |
| Tests: `tests/DeskAI.Core.Tests/DesktopMovePlannerTests.cs`, `tests/DeskAI.Presentation.Tests/{DesktopMoveServiceTests,DesktopStudioMovePageTests,DesktopStudioLayoutTests}.cs` | |

---

### Task 1: The decision and the security review

**Files:** Create `docs/decisions/0045-tag-names-renames-folders.md`, `docs/security/2026-09-24-tag-names-review.md`.

- [ ] **Step 1: Write ADR 0045**

```markdown
# ADR 0045: Tag Names Renames Folders on the Desktop

- Status: Accepted
- Date: 2026-09-24
- Builds on: ADR 0044
- Review: `docs/security/2026-09-24-tag-names-review.md`

## Context

Tag names puts each group's name in front of the names of the folders in that group ("Coding –
Python stuff"). The design called for a new executor action, "rename a folder".

## Decision

- A rename is carried out as ADR 0044's `MoveFolderOperation` with the destination in the same
  folder: one Windows rename, with every existing check (same folder as in the list, never
  overwriting, journal first, check after a stop, Put back by made-at time). No new executor
  action and no schema change; the plan's purpose is the new `PlanPurpose.TagNames`.
- Only folders are renamed; files and Not sure keep their names. The separator is " – ".
- A folder already starting with "Group – " is left alone. A new name that fails
  `FolderNameCheck`, or that is already used by anything on the Desktop (including hidden or
  left-out items), is left alone with the reason.
- The same separate yes as ADR 0044 applies; its dialog says "move or rename".

## Consequences

Put back of a rename moves the folder back to its old name only if that name is free and the
folder is still the same one. Project and program folders start unticked, because renaming them
can break shortcuts or programs that remember the old name.
```

- [ ] **Step 2: Write the review**

```markdown
# Tag Names Security Review

- Date: 2026-09-24
- Scope: ADR 0045
- Result: accepted

| Threat | Control | Test |
|---|---|---|
| A rename overwrites or merges with something | New names already used on the Desktop (visible or left out) are left alone; the executor never overwrites | `DesktopMovePlannerTests.Tag_names_never_takes_a_name_already_used` |
| A name Windows or DeskAI would refuse | `FolderNameCheck` on the new name | `DesktopMovePlannerTests.Tag_names_checks_the_new_name` |
| Pressing it twice stacks prefixes | Already-prefixed folders are left alone | `DesktopStudioMovePageTests.A_folder_already_named_with_its_group_is_left_alone` |
| A project or program breaks after a rename | Unticked with a warning | `DesktopMovePlannerTests.Tag_names_warns_before_renaming_a_project` |
| Files renamed | Only folders are listed | `DesktopStudioMovePageTests.Tag_names_shows_the_new_name_for_each_folder_and_never_renames_files` |
| Renaming without the person's yes | Same separate yes, checked by purpose | `DesktopMoveServiceTests.Tag_names_needs_the_same_yes_and_Put_back_restores_names` |
| Put back fails silently | Existing Put back checks, named reasons | `DesktopStudioMovePageTests.Rename_then_Put_back_restores_the_old_names` |
| Protected or link folders renamed | Inventory leaves them out; path policy re-checked on both names | ADR 0044 tests |

No new executor action, Windows setting, network, or AI capability.
```

- [ ] **Step 3: Commit**

```bash
git add docs/decisions/0045-tag-names-renames-folders.md docs/security/2026-09-24-tag-names-review.md
git commit -m "Decide how Tag names renames folders, reusing the folder move, and review it"
```

---

### Task 2: What Tag names would rename

**Files:** Modify `src/DeskAI.Core/Plans/PlanPurpose.cs`, `src/DeskAI.Core/Studio/DesktopMovePlanner.cs`; Test `tests/DeskAI.Core.Tests/DesktopMovePlannerTests.cs`.

**Interfaces — Produces:** `PlanPurpose.TagNames` (= 3); `DesktopMoveCard.TagNames` (appended);
`DesktopMovePlanner.TagSeparator = " – "`; `DesktopMovePlanner.TagNames(AuthorizedRoot root,
DesktopInventory inventory, DesktopGroupBoard board, DateTimeOffset nowUtc, string policyVersion,
Func<string, bool> isProtected)` → `DesktopMovePreview` whose items have `Destination` = the new
name and whose plan holds one `MoveFolderOperation(source, newName)` per item.

- [ ] **Step 1: Write the failing tests** — append inside `DesktopMovePlannerTests`:

```csharp
    [Fact]
    public void Tag_names_puts_the_group_name_in_front_of_each_folder_and_leaves_files()
    {
        var board = Board([new("Coding", ["Python stuff", "app.py"])], notSure: ["Loose"]);

        var preview = Tag(board, Folder("Python stuff", Recent), File("app.py", Recent), Folder("Loose", Recent));

        var item = Assert.Single(preview.Items);
        Assert.Equal("Python stuff", item.Name);
        Assert.Equal("Coding – Python stuff", item.Destination);
        var rename = Assert.IsType<MoveFolderOperation>(Assert.Single(preview.Plan.Operations));
        Assert.Equal("Coding – Python stuff", rename.DestinationRelativePath);
        Assert.Equal(PlanPurpose.TagNames, preview.Plan.Purpose);
    }

    [Fact]
    public void Tag_names_never_takes_a_name_already_used()
    {
        var board = Board([new("Coding", ["Python stuff", "Tools"])]);
        var seen = new DesktopInventory(
            [Folder("Python stuff", Recent), Folder("Tools", Recent), File("Coding – Python stuff", Recent)], null)
        {
            LeftOutNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Coding – Tools" },
        };

        var preview = DesktopMovePlanner.TagNames(StudioFakes.Root(), seen, board, Now, "1", _ => false);

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("Python stuff", "Something called Coding – Python stuff is already there, so nothing was replaced."), preview.LeftAlone);
        Assert.Contains(new DesktopLeftAlone("Tools", "Something called Coding – Tools is already there, so nothing was replaced."), preview.LeftAlone);
    }

    [Fact]
    public void Tag_names_leaves_a_folder_already_named_with_its_group()
    {
        var preview = Tag(Board([new("Coding", ["Coding – Tools"])]), Folder("Coding – Tools", Recent));

        Assert.Empty(preview.Items);
        Assert.Contains(new DesktopLeftAlone("Coding – Tools", "Its name already starts with the group's name."), preview.LeftAlone);
    }

    [Fact]
    public void Tag_names_checks_the_new_name()
    {
        var longName = new string('a', 60);

        var preview = Tag(Board([new("Coding", [longName])]), Folder(longName, Recent));

        Assert.Empty(preview.Items);
        Assert.StartsWith("The new name can't be used:", Assert.Single(preview.LeftAlone).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_names_warns_before_renaming_a_project()
    {
        var preview = Tag(Board([new("Coding", ["Game mod"])]), Folder("Game mod", Recent, warnings: DesktopThingWarnings.ActiveProject));

        var item = Assert.Single(preview.Items);
        Assert.False(item.TickedByDefault);
        Assert.Contains("renaming", item.Warning, StringComparison.Ordinal);
    }

    private static DesktopMovePreview Tag(DesktopGroupBoard board, params DesktopThing[] things) =>
        DesktopMovePlanner.TagNames(StudioFakes.Root(), Seen(things), board, Now, "1", _ => false);
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/DeskAI.Core.Tests -c Release --no-restore`
Expected: build errors — `DesktopMovePlanner.TagNames` and `PlanPurpose.TagNames` do not exist.

- [ ] **Step 3: Implement**

`PlanPurpose.cs`: append `TagNames,` after `FolderByGroup,`.

`DesktopMovePlanner.cs`:
- `DesktopMoveCard`: append `TagNames,`.
- In `DesktopMoveText.WarningFor`, change "Moving it can break programs that remember where it is."
  to "Moving or renaming it can break programs that remember where it is.", and "Moving it can break
  shortcuts or games that remember where it is." to "Moving or renaming it can break shortcuts or
  games that remember where it is."
- Add to `DesktopMovePlanner` (after `FolderByGroup`):

```csharp
    /// <summary>Between the group's name and the folder's own name, as in the design's example.</summary>
    public const string TagSeparator = " – ";

    /// <summary>
    /// Each folder in a group gets the group's name in front ("Coding – Python stuff"), with ADR
    /// 0044's folder move to a new name in the same place (ADR 0045). Files and Not sure keep their
    /// names. A new name that is already used, even by something the look left out, is never taken.
    /// </summary>
    public static DesktopMovePreview TagNames(
        AuthorizedRoot root, DesktopInventory inventory, DesktopGroupBoard board, DateTimeOffset nowUtc, string policyVersion, Func<string, bool> isProtected)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(board);
        var byPath = inventory.Things.ToDictionary(thing => thing.RelativePath, StringComparer.OrdinalIgnoreCase);
        var taken = inventory.Things.Select(thing => thing.Name).Concat(inventory.LeftOutNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builder = new Builder(root, DesktopMoveCard.TagNames, nowUtc, policyVersion, isProtected, inventory.LeftOutNames);
        foreach (var group in board.Groups)
        {
            var prefix = group.Name + TagSeparator;
            foreach (var path in group.Items)
            {
                if (!byPath.TryGetValue(path, out var thing))
                {
                    builder.LeaveAlone(Path.GetFileName(path), "It is no longer on your Desktop.");
                    continue;
                }

                if (!thing.IsFolder)
                {
                    continue;
                }

                var newName = prefix + thing.Name;
                if (thing.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    builder.LeaveAlone(thing.Name, "Its name already starts with the group's name.");
                }
                else if (FolderNameCheck.Check(newName) is { } problem)
                {
                    builder.LeaveAlone(thing.Name, $"The new name can't be used: {problem}");
                }
                else if (!taken.Add(newName))
                {
                    builder.LeaveAlone(thing.Name, $"Something called {newName} is already there, so nothing was replaced.");
                }
                else
                {
                    builder.Rename(thing, newName, $"In your {group.Name} group");
                }
            }
        }

        return builder.Build(PlanPurpose.TagNames);
    }
```

- Add `using DeskAI.Core.Templates;` and, inside `Builder`, after `Move`:

```csharp
        /// <summary>A folder gets a new name in the same place: one rename (ADR 0045).</summary>
        public void Rename(DesktopThing thing, string newName, string reason)
        {
            if (isProtected(thing.RelativePath) || isProtected(newName))
            {
                LeaveAlone(thing.Name, "DeskAI's safety rules keep it where it is.");
                return;
            }

            var id = Guid.NewGuid();
            _moves.Add(new MoveFolderOperation(id, thing.RelativePath, newName, reason, OperationProvenance.User));
            _items.Add(new DesktopMoveItem(
                id, thing.RelativePath, true, newName, thing.LastChangedUtc, thing.FileCount, thing.LookedAllTheWay,
                DesktopMoveText.WarningFor(thing.Warnings)));
            _facts[id] = thing.Facts;
        }
```

- [ ] **Step 4: Run all tests** — build (no-incremental) and test the solution. Expected: 0 warnings, all pass.

- [ ] **Step 5: Commit** — `git add src/DeskAI.Core tests/DeskAI.Core.Tests` and
  `git commit -m "Work out what Tag names would rename"`.

---

### Task 3: The service offers Tag names

**Files:** Modify `src/DeskAI.Core/Studio/DesktopMoveService.cs`; Test `tests/DeskAI.Presentation.Tests/DesktopMoveServiceTests.cs`.

- [ ] **Step 1: Write the failing test** — append inside `DesktopMoveServiceTests`:

```csharp
    [Fact]
    public async Task Tag_names_needs_the_same_yes_and_Put_back_restores_names()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        var desktop = await ConnectDesktopAsync(app);
        var moves = app.Get<DesktopMoveService>();
        await app.Get<DesktopGroupingService>().GuessAsync(desktop.Id, TestContext.Current.CancellationToken);
        var preview = (await moves.PreviewAsync(desktop.Id, DesktopMoveCard.TagNames, TestContext.Current.CancellationToken)).Preview!;
        var all = preview.Items.Select(item => item.OperationId).ToList();

        Assert.True((await moves.ApplyAsync(preview, all, TestContext.Current.CancellationToken)).NeedsPermission);
        await moves.AllowAsync(desktop.Id, TestContext.Current.CancellationToken);
        var done = await moves.ApplyAsync(preview, all, TestContext.Current.CancellationToken);

        Assert.Equal("Done. 1 folder renamed.", done.Summary);
        Assert.True(Directory.Exists(Path.Combine(app.DesktopPath, "Coding – Python stuff")));
        var back = await moves.PutBackAsync(desktop.Id, DesktopMoveCard.TagNames, TestContext.Current.CancellationToken);
        Assert.Equal(1, back.Moved);
        Assert.True(File.Exists(Path.Combine(app.DesktopPath, "Python stuff", "main.py")));
    }
```

- [ ] **Step 2: Run to see it fail** — build tests; expected: error, `DesktopMoveCard.TagNames` preview not supported (runtime `ArgumentOutOfRangeException` from `PurposeOf`, or the FolderByGroup branch runs). Record what you saw.

- [ ] **Step 3: Implement** in `DesktopMoveService`:
- `PreviewAsync`: the board is needed when `card is DesktopMoveCard.FolderByGroup or DesktopMoveCard.TagNames`; choose the planner with a `switch` on `card` (`ClearOldStuff`, `FolderByGroup`, `TagNames`); the empty-list message for Tag names is "None of the folders in your groups can get a new name right now."
- `ApplyAsync` summary: when `preview.Card == DesktopMoveCard.TagNames`, use
  `notMoved.Count == 0 ? $"Done. {Folders(moved)} renamed." : moved == 0 ? "Nothing was renamed." : $"{moved} of {items.Count} folders renamed. The rest kept their names."`,
  with `private static string Folders(int count) => count == 1 ? "1 folder" : $"{count} folders";`.
- `PurposeOf`: add `DesktopMoveCard.TagNames => PlanPurpose.TagNames,`.
- Class summary: "Clear old stuff, Folder by group, and Tag names (ADR 0044, ADR 0045)".

- [ ] **Step 4: Run all tests** — expected: all pass, 0 warnings.

- [ ] **Step 5: Commit** — `git commit -m "Offer Tag names through the Desktop moving service"`.

---

### Task 4: The card on the page

**Files:** Modify `DesktopMoveCardViewModel.cs`, `DesktopStudioViewModel.cs`, `DesktopStudioPage.xaml(.cs)`, `HelpCatalog.cs`, `docs/TESTING.md`; Test `DesktopStudioMovePageTests.cs`, `DesktopStudioLayoutTests.cs`.

- [ ] **Step 1: Write the failing tests** — append inside `DesktopStudioMovePageTests`:

```csharp
    [Fact]
    public async Task Tag_names_shows_the_new_name_for_each_folder_and_never_renames_files()
    {
        await using var app = await TestApp.StartAsync();
        MakeGroupDesktop(app);
        var before = Snapshot(app);
        var studio = await OpenAsync(app);
        await studio.GuessAsync();

        await studio.PreviewAsync(studio.TagNames);

        Assert.Equal(["Essays", "Python stuff"], studio.TagNames.Items.Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.Contains("becomes \"Coding – Python stuff\"", studio.TagNames.Items.Single(item => item.Name == "Python stuff").Detail, StringComparison.Ordinal);
        Assert.Equal("Rename 2 folders", studio.TagNames.ApplyButtonText);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task Rename_then_Put_back_restores_the_old_names()
    {
        await using var first = await TestApp.StartAsync();
        MakeGroupDesktop(first);
        var before = Snapshot(first);
        var studio = await OpenAllowedAsync(first);
        await studio.GuessAsync();
        await studio.PreviewAsync(studio.TagNames);

        var done = await studio.ApplyAsync(studio.TagNames);

        Assert.Equal("Done. 2 folders renamed.", done!.Summary);
        Assert.True(Directory.Exists(Path.Combine(first.DesktopPath, "Coding – Python stuff")));
        Assert.True(Directory.Exists(Path.Combine(first.DesktopPath, "Documents – Essays")));
        Assert.True(File.Exists(Path.Combine(first.DesktopPath, "report.docx")));
        await using var app = await first.ReopenAsync();
        var again = app.Get<DesktopStudioViewModel>();
        await again.InitializeAsync();
        Assert.True(again.TagNames.CanPutBack);
        await again.PutBackAsync(again.TagNames);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public async Task A_folder_already_named_with_its_group_is_left_alone()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFile("Desktop", Path.Combine("Coding – Tools", "tool.py"));
        var studio = await OpenAllowedAsync(app);
        await studio.GuessAsync();

        await studio.PreviewAsync(studio.TagNames);

        Assert.False(studio.TagNames.HasPreview);
        Assert.Contains(studio.TagNames.LeftAlone, line => line == "Coding – Tools: Its name already starts with the group's name.");
    }

    private static void MakeGroupDesktop(TestApp app)
    {
        app.MakeFile("Desktop", Path.Combine("Python stuff", "main.py"));
        app.MakeFile("Desktop", Path.Combine("Essays", "essay.docx"));
        app.MakeFile("Desktop", "report.docx");
    }
```

Append inside `DesktopStudioLayoutTests`:

```csharp
    [Fact]
    public void Tag_names_sits_with_the_group_steps_before_the_other_tidy_ups()
    {
        var page = Page();
        var groupFolders = page.IndexOf("Topic=\"studio.folderByGroup\"", StringComparison.Ordinal);
        var tagNames = page.IndexOf("Topic=\"studio.tagNames\"", StringComparison.Ordinal);
        var other = page.IndexOf("Text=\"Other tidy-ups\"", StringComparison.Ordinal);

        Assert.True(groupFolders > 0 && tagNames > groupFolders && other > tagNames,
            $"Order was: group folders {groupFolders}, tag names {tagNames}, other {other}");
        var card = Section(page, "Topic=\"studio.tagNames\"", "</Border>");
        Assert.Contains("Content=\"Add the group's name to each folder's name\"", card, StringComparison.Ordinal);
        Assert.Contains("ViewModel.TagNames.Items", card, StringComparison.Ordinal);
        Assert.Contains("Content=\"Put back\"", card, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run to see them fail** — build tests; expected: `TagNames` does not exist on `DesktopStudioViewModel`.

- [ ] **Step 3: View models**
- `DesktopMoveItemViewModel.Detail`: a `switch` on `card` — `ClearOldStuff` → `$"{Kind()} · last changed {…:d}"`; `FolderByGroup` → `$"{Kind()} · goes into {Item.Destination}"`; `TagNames` → `$"{Kind()} · becomes \"{Item.Destination}\""`.
- `DesktopMoveCardViewModel.ApplyButtonText`: for `TagNames`,
  `count == 1 ? "Rename 1 folder" : $"Rename {count} folders"`; otherwise as now.
- `DesktopStudioViewModel`: add `public DesktopMoveCardViewModel TagNames { get; } = new(DesktopMoveCard.TagNames);`
  and in `RefreshMovesAsync` a third `TagNames.ShowLast(…FindLastAsync(id, DesktopMoveCard.TagNames)…)` line.

- [ ] **Step 4: The page**
- Code-behind `CardOf`: add `"TagNames" => ViewModel.TagNames,`. Dialog content first line becomes
  "DeskAI may move or rename folders and files on your Desktop: only the ones you tick, and only when you press Move or Rename.";
  title "Allow DeskAI to move or rename things on your Desktop?"; primary button "Allow".
- XAML: directly after the "2. Happy with these groups?" card's closing `</Border>`, add a copy of
  that card with: `Visibility="{x:Bind ViewModel.HasBoard, Mode=OneWay}"`; title
  "Or keep them where they are, and add the group's name"; help `Topic="studio.tagNames"`;
  description "Each folder in a group gets the group's name in front, like "Coding – Python stuff".
  Files and Not sure keep their names. You see the list first."; first button
  `Content="Add the group's name to each folder's name" Tag="TagNames" Click="OnPreviewClick"`;
  every `FolderByGroup` binding and Tag replaced with `TagNames`; automation names "Folders that
  would be renamed" and "Folders left with their names".
- The caption above the cards: "The cards below move or rename things on your Desktop. DeskAI asks
  before the first change, and Put back returns your latest change."

- [ ] **Step 5: Help** — after `studio.folderByGroup` in `HelpCatalog.cs`:

```csharp
        new("studio.tagNames", "Adding group names to folders",
            "The step after Find groups that puts each group's name in front of its folders' names.",
            "DeskAI lists each folder and its new name first. Only what you tick is renamed, after you press Rename. Files keep their names, and Put back returns your latest change.",
            "It never deletes anything, never renames files, and asks no AI when renaming."),
```

- [ ] **Step 6: Coverage map** — in `docs/TESTING.md`, after the ADR 0044 Desktop Studio row, add:
  `| Desktop Studio | Tag names (ADR 0045): the preview lists each folder in a group with its new name ("becomes "Coding – Python stuff"") and never a file; Rename needs the same yes; Put back restores the old names, also after reopening; a folder already named with its group is left alone; a taken name, a too-long name, and a project folder are handled before anything moves; the card sits with the group steps, before Other tidy-ups | `DesktopStudioMovePageTests`, `DesktopMoveServiceTests`, `DesktopStudioLayoutTests`, `DesktopMovePlannerTests` |`

- [ ] **Step 7: Run all tests** — expected: 0 warnings, all pass (including `HelpCatalogTests`, `HelpPlacementTests`, `AccessibilityNameTests`).

- [ ] **Step 8: Commit** — `git commit -m "Add Tag names to Desktop Studio"`.

---

### Task 5: Documents and handoff

- [ ] **Step 1:** Update `docs/ROADMAP.md` (step 4 built, ADR 0045), `docs/ARCHITECTURE.md` (one
  sentence under the ADR 0044 paragraph: Tag names is a same-folder `MoveFolderOperation`,
  `PlanPurpose.TagNames`), `docs/SECURITY.md` (one sentence in "Desktop Studio Moving Things":
  renaming uses the same yes and the same single rename; files are never renamed),
  `docs/UI-UX.md` and `docs/USER-GUIDE.md` (the new card and the "move or rename" dialog),
  the design spec (Build order step 4 built; "rename a folder" is a same-folder folder move per ADR
  0045).
- [ ] **Step 2:** Rewrite `docs/HANDOFF.md` "Start here" per its rules: step 4 built, test count,
  manual checks for Tag names, the next task **Color groups** (starts with its own probe, like the
  icon probe, and is dropped if the colour cannot be removed cleanly); add a dated section with the
  final review's findings and fixes; update the starter prompt.
- [ ] **Step 3:** Run the three verification commands; record the test count.
- [ ] **Step 4:** `git add docs` and `git commit -m "Record Tag names in the roadmap, guides, design, and handoff"`.
