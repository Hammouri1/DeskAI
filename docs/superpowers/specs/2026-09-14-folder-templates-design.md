# Folder Templates — Design (draft, waiting for the owner)

- Date: 2026-09-14
- Milestone: V0.7 "Workspace Profiles and Design", piece C
- Branch: `v0.7-workspace-profiles`
- Status: **Draft. Not agreed with the owner, and not built.** Written on 2026-09-14 while the
  owner was away ("continue until I'm back"). V0.7 piece C creates folders, so `ROADMAP.md`
  requires its own design and security review before any code. This document and
  `docs/security/2026-09-14-folder-templates-review.md` are those two, as proposals. Every choice
  marked **Proposed** needs the owner's yes before it is built; the questions at the end are the
  ones only the owner can answer.

## What a folder template is, in one sentence

A named set of empty folders, such as Student's "Assignments, Slides, Screenshots, Notes", that
DeskAI makes inside one connected folder after showing exactly which folders it will make and
getting a yes, and that can be undone.

## Why it is worth building (and an honest limit)

Tidying already makes the folders it needs as it moves files. A template is for the moment
*before* there are files: someone starting a course, or setting up a new downloads folder, wants
the shape ready. It also gives the starter-pack rules somewhere visible to point. For example,
Student's rules move files into `Assignments` and `Slides`.

Honest limit: a template only makes empty folders. It does not move anything into them. Filling
them is still Tidy, with its own preview and approval.

## Proposed decisions

1. **Proposed: a fixed catalog in code, one template per starter pack.** Same reasoning as
   ADR 0026: no data files someone could edit, and every folder name is checked by a test
   against the real path rules. Folder names match the pack rules' destinations, so a pack's
   rules and its template fit together.
2. **Proposed: one level only.** A template makes folders directly inside the connected folder,
   never folders inside folders. The executor needs a parent to exist first, and "never touches
   subfolders" is already a promise on Organize; one level keeps both simple. At most 8 folders
   per template.
3. **Proposed: it needs "Allow tidying" on that folder, the same permission Tidy uses.** Making
   a folder is a change to the folder, and `SECURITY.md` lists "create a directory within an
   authorized root" as a change. Asking for a second, separate permission would be one more
   thing a person has to understand, for the same folder and the same kind of trust. If tidying
   is not allowed, the template dialog offers the same "Allow tidying" dialog Organize uses.
4. **Proposed: it lives on My workspace**, as a third section, "Folder templates", after Starter
   packs. The Workspace design already expected later V0.7 pieces to join that page.
5. **Proposed: it goes through the one executor.** There is no new way to change the disk. A
   template becomes an `OrganizationPlan` holding only `CreateDirectoryOperation`s, is checked
   by `PlanValidator`, approved as exactly those operations, and run by `FolderTidyExecutor`,
   which journals it and re-checks the folder before each folder it makes. `FolderTidyExecutor`
   stays the only code that changes the disk.
6. **Proposed: Undo removes only folders DeskAI made that are still empty.** This is already how
   the executor undoes a created folder: a folder that was there before is recorded as "already
   there" and never removed, and a folder with anything in it is left alone with a reason.
7. **Proposed: not part of a starter pack's Add.** Adding a pack changes nothing on disk (ADR
   0026), and that promise is written on the page. A template is its own button, with its own
   dialog, so a person never makes folders as a side effect of adding searches.

## The page section

Under Starter packs on My workspace:

- **Folder templates** ("?"). One sentence: "Make a set of empty folders in a folder you chose.
  You see the list first, and you can undo it."
- Five cards, one per template, each with its name, its folder names on one line ("Assignments,
  Slides, Screenshots, Notes"), and a plain **Choose a folder…** button. It is plain because
  choosing confirms nothing.
- The folder choice lists only connected folders. With none connected: "Connect a folder in
  Organize first." and a link to it. There is no "Choose another folder" picker here, because
  connecting belongs to Organize.

### Preview dialog

- Title: "Make the Student folders in Downloads?"
- "DeskAI will make:" followed by each new folder by name.
- "Already there, left as they are:" followed by folders that exist already (whatever their
  capitals).
- "Can't be made:" followed by each blocked name with its reason in the caution colour. For
  example: "A file called Notes is already there." or "Part of the way there is a link or
  shortcut."
- Promise line on the accent rail: "Only empty folders are made. Nothing is moved, renamed, or
  deleted. You can undo it."
- Buttons: **Make 3 folders** (accent, because it confirms) and **Cancel**. Enter, Esc, and the X
  act as Cancel, the same as DeskAI's other confirmation dialogs. When nothing would be made,
  the button is off and the dialog says "Every folder in this template is already there."

### Result line

On the template's own card:

- All made: "Made 3 folders in Downloads." and **Undo**.
- Some made: "Made 2 of 3 folders in Downloads. Notes: a file with that name is already there."
- None made: "No folders were made. <reason>"
- After Undo: "Removed 3 folders." or "Removed 2 folders. Slides has something in it now, so
  it was left in place."

## Proposed template contents

| Template | Folders |
|---|---|
| Student | Assignments, Slides, Screenshots, Notes |
| Developer | Projects, Installers, Archives |
| Gaming | Clips, Screenshots, Installers |
| Productivity | Invoices, Spreadsheets, Documents |
| Minimal | Screenshots, Installers |

Every name must pass `WindowsPathPolicy.ValidateRelativePath` against a generated folder, be a
single segment, and hold no technical words. A catalog test holds all of that.

## Components (proposed)

| Piece | Project | Responsibility |
|---|---|---|
| `FolderTemplate`, `FolderTemplateCatalog` | Core (`Templates/`) | Immutable: ID, name, folder names. No I/O. |
| `FolderTemplateService` | Core (`Templates/`) | `PreviewAsync(templateId, rootId)`: reads which names already exist through the read-only folder service and builds the plan with nothing changed. `MakeAsync(preview)`: re-reads, rebuilds, approves exactly the create operations, calls `IFolderTidyExecutor.ExecuteAsync`, and returns the outcome. `UndoAsync(transactionId)`. |
| `IFolderNameLookup` | Core contract, Infrastructure impl | For a connected folder that passes `CheckStillSafeAsync`: which of up to 8 given single-segment names exist directly inside it, and whether each is a folder, a file, or a link. Names and kinds only; no contents, no recursion, no change. Holds no executor. |
| `FolderTemplatesViewModel` (or a section of `WorkspaceViewModel`) | Presentation | Cards, dialog state, result lines. |
| Dialog and section | App | XAML only. |
| Help topic | Presentation (`HelpCatalog`) | "Folder templates". |

**Placement matters for an existing test.** `StarterPackService` and `PinnedSearchService` have
reflection tests that fail if they take an executor. Those tests stay true: the template service
lives in `Core/Templates`, not `Core/Workspace`, and is the only My workspace service that holds
an executor. A new reflection test pins that down: no Workspace type holds an executor, and the
template service holds no AI, HTTP, scanner-with-content, or rule-evaluator type.

## Data flow

1. The person presses **Choose a folder…** on Student and picks Downloads.
2. `FolderTemplateService.PreviewAsync` checks `RootCapabilities.CanTidy`. If tidying is not
   allowed, the page shows the Allow tidying dialog and stops there.
3. For each template folder name, the service asks a new narrow read-only contract,
   `IFolderNameLookup`, whether a folder, a file, or a link already has that name directly
   inside the connected folder. It reads names and kinds only, never contents, and runs
   `CheckStillSafeAsync` first. (`IReadOnlyFolderService` has no such method today; checked
   2026-09-14.) It builds one
   `CreateDirectoryOperation` per missing folder and runs `PlanValidator` on the plan.
4. The dialog shows new, already there, and blocked names.
5. **Make** calls `MakeAsync`, which repeats step 3 from fresh state instead of trusting the
   preview. If the set of folders to make has changed, it does not guess: it returns "The folder
   changed while the list was open. Look again." and the dialog reopens with the fresh list.
6. It creates `Approval.Create(plan, all create operation IDs)` and calls
   `FolderTidyExecutor.ExecuteAsync(plan, approval, expected: empty)`. The executor takes the run
   lock, checks the folder has no unanswered interrupted tidy, journals every intended folder,
   re-checks the folder's trust before each one, creates it or records "already there", and
   finishes the journal record.
7. The result line reports each outcome. **Undo** calls `FolderTidyExecutor.UndoAsync`, which
   removes only folders recorded as made by this run and still empty.

## Integration hazards found while reading the code

These are places where today's tidy code assumes every journal record moved files. Each needs a
decision and a test before building:

1. **Organize's "Last tidy" skips a record that moved no files** (`TidyRunService.FindLastAsync`
   uses `continue` when a record has no completed moves). That is good: a template run will not
   appear as "Last tidy: 0 files". But it also means Undo for a template after DeskAI is reopened
   must be found by the template service itself. **Proposed:** the template card finds the
   folder's latest run made only of create-folder operations, if it has not been undone and no
   tidy has run in that folder since, and offers Undo there.
2. **An interrupted template run would show on Organize as "Your last tidy was interrupted: 0 of
   0 files moved"** (`TidyRunService.FindInterruptedAsync` counts only moves, and
   `FolderTidyExecutor.Settle` calls a record with zero moves `Failed`). A run that made some
   folders would then close as Failed and never be undoable. **Proposed:** a record with no move
   operations is described as folders ("DeskAI stopped while making folders: 2 of 3 made."),
   settles as Completed or PartiallyCompleted by counting created folders, and gets page tests
   on both Organize and My workspace. This touches the executor's recovery code, so it is the
   riskiest part of piece C and should be its own commit with its own tests.
3. **The run lock is shared.** A template run and a tidy in another window cannot overlap, and
   one waits up to 30 seconds and then refuses with the existing "busy" message. That is the
   right behaviour. It is noted here so nobody adds a second lock.
4. **Automatic checks and the index.** The index does not track folders, so new empty folders
   change no count on Home, Search, or pinned tiles. Nothing needs changing, but it is tested so
   a later change does not quietly start counting them.

## Error handling

- Permission taken back, folder disconnected, folder moved, or a link appearing mid-run: the
  executor refuses the remaining folders with its existing plain messages, and the result line
  names what was and was not made.
- A file with the folder's name: that folder is "Can't be made" in the preview, and refused
  again at run time if the file appeared since.
- Windows refusing (access denied, a read-only drive): "Windows did not let DeskAI make it."
  That folder only; the rest continue.

## Testing (proposed)

- **Core:** the catalog (five templates, single-segment names, all pass the path policy,
  ≤ 8 folders, no technical words); the service (existing folders are not re-made; a file with
  the name blocks that folder; state changing between preview and make is refused and re-shown;
  no tidy permission means nothing is run; approval covers exactly the create operations).
- **Infrastructure:** in a generated temp folder, making creates exactly those folders; an
  existing empty folder is "already there" and survives undo; undo leaves a folder that has
  gained a file; a link in the path refuses; permission withdrawn mid-run refuses the rest; an
  interrupted template record settles by folders made (hazard 2).
- **Reflection:** Workspace types still hold no executor; the template service holds no AI or
  HTTP type.
- **Page tests** (`WorkspacePageTests` or `FolderTemplatePageTests`), generated folders only:
  choose folder, preview, make, result line, undo; already-there folders; blocked name shown in
  caution colour; no tidy permission leads to the allow dialog; no folders connected wording;
  after reopening, Undo is still offered; an interrupted template run reads as folders on both
  pages.
- **Docs in the same change:** Feature Coverage Map rows, a "Folder templates" list in
  `MANUAL-TESTING.md`, `ROADMAP.md`, `UI-UX.md`, `ARCHITECTURE.md`, `SECURITY.md` (one paragraph
  saying templates make folders only through `FolderTidyExecutor` with the tidy permission),
  and an ADR once the owner has agreed.

## Suggested build order (after the owner agrees)

1. Catalog and service with fakes (no disk).
2. The recovery and settle fix for records without moves (hazard 2), with Infrastructure tests.
3. Page section, dialog, and undo, with page tests.
4. Undo after reopening (hazard 1).
5. Docs and ADR.

## Questions only the owner can answer

1. Should making folders reuse **Allow tidying** (proposed), or be its own permission?
2. **One level only** (proposed), or should templates make folders inside folders, such as
   `School\Maths`?
3. Are the five template lists right? Should people be able to **type their own folder names**?
   That is not proposed: typed names are a new untrusted input, and it can come later with its
   own checks.
4. Should adding a starter pack **offer** its matching template afterwards, as a separate
   button, or should they stay unconnected (proposed)?
5. Is piece C still what you want next, or should D (DeskAI's look), which changes no files,
   come first?

## Out of scope

Typed folder names, folders inside folders, templates that move or rename files, making folders
outside a connected folder, templates on the Desktop (that is piece E), removing a folder that
has anything in it, and anything reachable without a window.
