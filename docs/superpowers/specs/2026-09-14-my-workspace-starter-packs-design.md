# My Workspace: Starter Packs and Pinned Searches — Design

- Date: 2026-09-14
- Milestone: V0.7 "Workspace Profiles and Design", first slice
- Branch: `v0.7-workspace-profiles` (started from `v0.5-background-checking`, not yet merged)
- Status: agreed with the owner in conversation on 2026-09-14; awaiting written review

## Why this slice, and what V0.7 is split into

V0.7 bundles five features with very different risk. The owner agreed to design and build them
one at a time, safest first:

| # | Piece | Changes files or Windows? | Status |
|---|---|---|---|
| A | Profiles, as starter packs | No | **This design** |
| B | Pinned saved searches | No | **This design** |
| C | Folder templates (create a folder layout in a connected folder) | Yes — creates folders; needs its own design and security review | Later |
| D | DeskAI's own look (themes for the DeskAI window) | No, but must keep "green means safe or confirmed" | Later |
| E | Desktop layouts, icons, shortcuts, wallpaper | Yes — changes Windows; `SECURITY.md` review gate | Last |

A and B share one idea — a profile is a bundle of saved searches and rules — and carry no file
risk, so they ship together first.

## Decisions made with the owner

1. **A profile is a one-time starter pack, not a remembered setting.** Picking "Student" previews
   and then copies in ordinary saved searches and rules. DeskAI stores no "current profile", so
   there is nothing hidden to explain and no question of what happens when switching profiles.
2. **It lives on a new side-menu page, "My workspace",** placed after Automatic tasks. Later V0.7
   pieces (folder templates, DeskAI's look) are expected to join this page.
3. **A pinned search is a tile showing its name, a count, and "Open in Search".** No file names on
   this page.
4. **Packs are a fixed catalog in code**, checked by tests — not data files (a new untrusted
   input for no gain) and not packs remembered in the database (contradicts decision 1).
5. **Pack contents and page layout** below were approved as shown.

## The page

Side menu: Home, Organize, Search, Automatic tasks, **My workspace**, Privacy and AI.

Top to bottom:

1. **Title** "My workspace" and one sentence: "Your shortcuts and starter packs. Nothing here
   moves or changes files." A "?" follows the title.
2. **Pinned searches** ("?"). Each pin is a tile: name, count, **Open in Search**, and an unpin
   button. One caption under the tiles: "Counted from what DeskAI remembers, last checked
   <time>." At most 8 pins.
   - Count wording: "23 files" / "1 file"; "200+ files" (the search result limit, `SearchQuery.DefaultLimit`) when the search
     reached its result limit; "No folders connected" when nothing is searchable; "Search not
     understood" when the phrase produces no filters. Never "0 files" for either of the last two.
   - Empty state: "Pin a saved search to see it here," with a link to Search.
   - **Open in Search** navigates to Search with that saved search already run.
3. **Starter packs** ("?"). Five cards, each a name and one line. Pressing a card opens a preview
   dialog; only **Add** adds. The result line appears directly under the card pressed (feedback
   belongs where the action was, `UI-UX.md`). One line under the cards: "Or make your own in
   Search and Automatic tasks." There is no "Custom" card, because a card that adds nothing would
   be a fake option.
4. **Your other saved searches**: saved searches not pinned, each with a **Pin** button, so
   searches a person made themselves can be pinned. Pin is off with a stated reason once 8 are
   pinned.

### Preview dialog

- Title: "Add the <Pack> starter pack?"
- "Saved searches" — each by name. Items that will be skipped are shown with their reason
  ("You already have a search called Screenshots").
- "Rules" — each in its own plain sentence from `AutomationRule.Describe()`, e.g. "When it is
  filed under Screenshots, move it into Screenshots."
- Promise line on the accent rail: "Rules start switched off. Nothing moves until you turn a rule
  on and press Tidy."
- Buttons: **Add** (accent, it confirms something) and **Cancel**. Enter, Esc, and the X behave
  as Cancel — the same convention as DeskAI's other confirmation dialogs.
- If everything in the pack would be skipped, Add is off and the dialog says "You already have
  everything in this pack."

### Result line

"Added 3 searches and 2 rules. Skipped 1 you already had: Screenshots." Added searches that did
not fit under the pin limit are named: "Pinned 2; Big files was added but not pinned — you have
8 pins." Rules are always reported as switched off: "The rules are switched off — turn them on in
Automatic tasks."

## Pack contents

Searches from a pack are pinned when added (while pin space remains). Every rule is added
switched **off**. Destinations are folders inside the connected folder, as every rule's are.

| Pack | One line | Saved searches (name — phrase) | Rules (condition → destination) |
|---|---|---|---|
| Student | Coursework, slides, and screenshots | Slides — `slides`; Recent documents — `documents from last month`; Screenshots — `screenshots` | Presentations → `Slides`; name contains "assignment" → `Assignments`; Screenshots → `Screenshots` |
| Developer | Downloads, archives, and big files | Archives — `archives`; Installers — `installers`; Big files — `big files` | Installers → `Installers`; Archives → `Archives` |
| Gaming | Clips, captures, and installers | Videos — `videos`; Big videos — `big videos`; Screenshots — `screenshots` | Videos → `Clips`; Screenshots → `Screenshots`; Installers → `Installers` |
| Productivity | Documents, sheets, and invoices | Documents — `documents`; Spreadsheets — `spreadsheets`; Presentations — `presentations` | name contains "invoice" → `Invoices`; Spreadsheets → `Spreadsheets` |
| Minimal | Just the two that pile up most | Screenshots — `screenshots`; Installers — `installers` | none |

A rule's name is its destination folder's name ("Screenshots", "Assignments"), with no pack name
added, so a person reading Automatic tasks sees the same words they would have typed.
Every category condition uses the existing `CategoryIsCondition`; every name condition uses
`NameContainsCondition`.

A phrase is kept only if a test proves the translator reads it as the pack claims (for example
`big files` must produce a size chip, not a name search for "big" and "files"). If the translator
disagrees, the wording changes, not the translator.

## Components

| Piece | Project | Responsibility |
|---|---|---|
| `StarterPack`, `StarterPackCatalog` | Core (`Workspace/`) | Immutable description of the five packs: ID, name, one line, saved searches (name, phrase), rules (name, conditions, destination). No I/O. |
| `StarterPackService` | Core | `PreviewAsync(packId)` returns what would be added and what would be skipped with a reason, saving nothing. `AddAsync(packId)` re-reads current state, saves through `ISavedSearchRepository` and `IRuleRepository`, pins, and returns the outcome. |
| `PinnedSearchService` | Core | Pin, unpin (capped at `MaxPinned = 8`), list pinned, and count one pin through `FileSearchService.SearchAsync`. |
| `ISavedSearchRepository` | Core contract, Infrastructure impl | Gains pinned state: `SavedSearch.IsPinned` and `SetPinnedAsync(id, bool)`. |
| Schema version 13 | Infrastructure | `ALTER TABLE saved_searches ADD COLUMN is_pinned INTEGER NOT NULL DEFAULT 0`. |
| `SearchRequest` | Presentation | Same shape as `OrganizeRequest`: carries one saved-search ID, taken once. |
| `WorkspaceViewModel` | Presentation | The page's state and commands. |
| `WorkspacePage` + nav item `"workspace"` | App | XAML page, the preview dialog, and navigation registration. |
| Help topics | Presentation (`HelpCatalog`) | My workspace, pinned searches, starter packs. |

## Data flow

**Adding a pack**

1. Person presses a pack card → `WorkspaceViewModel` calls `StarterPackService.PreviewAsync`.
2. The service lists current saved searches and rules. Any pack item whose name matches an
   existing one (ignoring capitalisation) is marked skipped. Searches beyond
   `SavedSearch.MaxSavedSearches` (50) are marked skipped with that reason.
3. The App layer shows the preview dialog from that result.
4. **Add** → `AddAsync`, which repeats step 2 against fresh state rather than trusting the
   preview, because something may have changed while the dialog was open.
5. Each rule is created with `AutomationRule.Create(..., isEnabled: false)` by the service
   itself, whatever the catalog holds.
6. Each new search is saved, then pinned while fewer than 8 are pinned.
7. The outcome (added, skipped with reasons, pinned, not pinned) becomes the result line.

**A pinned tile**

1. On page load, `PinnedSearchService` lists pinned searches.
2. For each, `FileSearchService.SearchAsync(phrase, now)` runs against the index; the tile
   reads `Hits.Count`, `ReachedLimit`, `FoldersSearched`, and `UnderstoodNothing`.
3. **Open in Search** → `SearchRequest.Ask(id)` → navigate to `"search"` → `SearchViewModel`
   takes the request once and runs that saved search as if the person had pressed it there.

## Safety

This slice adds no new way to change a file, and adds nothing reachable without a window.

| Threat | Control | Test |
|---|---|---|
| A pack becomes a shortcut to moving files | A pack produces only saved searches and switched-off rules; no workspace class references a planner, executor, or journal | Reflection test: no Workspace type depends on `IPlanExecutor`, `FolderTidyExecutor`, or `IOperationJournal` |
| A pack rule acts before a person agrees | Service forces `isEnabled: false`; `RuleSetEvaluator` ignores disabled rules for Tidy, checks, and practice runs | Service test with a catalog rule marked enabled still saves it off; page test: after adding a pack, Tidy shows no "Your rule" suggestion and an automatic check counts nothing until the rule is switched on |
| A catalog rule with a bad destination or no conditions | Built with `AutomationRule.Create` and `MoveToFolderAction`, which refuse both | Catalog test constructs every rule |
| A pack overwrites a person's own search or rule | Name clashes are skipped and reported, never replaced; database names are unique | Service and page tests with a pre-existing "Screenshots" search and rule |
| Preview is stale by the time Add is pressed | `AddAsync` re-reads state | Service test: a clashing item created between preview and add is skipped, not overwritten |
| A count misleads | "200+", "No folders connected", "Search not understood" never shown as a number | Page tests for each |
| A pin reaches a folder search may not | Counts go through `FileSearchService`, which uses `IsSearchable` | Page test: a disconnected folder's files stop counting |
| File names leak to another surface | Tiles hold a name and a count only; nothing added to tray or notifications | Page test asserts tile text holds no file name |
| AI or network involvement | None; Workspace types take no AI or HTTP dependency | Covered by the reflection test |

No `SECURITY.md` review gate applies (no file mutation, content reading, cloud transmission,
background work, or shell presence). This table is the record of that judgement.

## Error handling

- Database failures while adding a pack: the service adds items one at a time in a fixed order
  (searches, then rules). On failure the result line says what was added before it stopped and
  "DeskAI stopped safely: …". Nothing added is rolled back, because each item is an independent,
  ordinary search or switched-off rule the person can delete; pretending none were added would be
  untrue.
- A count failing for one tile shows "Could not count" on that tile only; other tiles still show.
- A `SearchRequest` for a saved search deleted meanwhile: Search opens normally and says "That
  saved search no longer exists."

## Testing

- **Core** (`DeskAI.Core.Tests`): catalog — five packs, every phrase understood as claimed,
  every rule valid, names within limits, no technical words in names or one-liners; service —
  skip on clash, 50-search limit, 8-pin limit, re-read before add, forced off, adding the same
  pack twice adds nothing; pin service — cap, count wording inputs.
- **Infrastructure** (`DeskAI.Infrastructure.Tests`): version 12 → 13 migration keeps existing
  searches unpinned; pinned state round-trips; deleting a search removes its pin.
- **Architecture/reflection**: Workspace types reach no executor, journal, AI, or HTTP type.
- **Page tests** (`DeskAI.Presentation.Tests/WorkspacePageTests.cs`), using generated folders
  only: preview then Add then result line; clash is skipped and named; pin count in a generated
  folder, "200+", no-folder and not-understood wording; Open in Search lands on results; pin and
  unpin, including the cap; a pack rule is Off on Automatic tasks and adds nothing to Tidy until
  switched on.
- **Help**: new topics pass existing word-count, plain-word, and placement tests.
- **Docs in the same change**: Feature Coverage Map rows in `TESTING.md`; a "My workspace" list
  in `MANUAL-TESTING.md` (dialog keyboard behaviour, light/dark, Narrator on tiles); `ROADMAP.md`
  V0.7 progress; `UI-UX.md` navigation and page description; `ARCHITECTURE.md` Workspace
  components.

## Out of scope for this slice

Folder templates (C), DeskAI themes (D), desktop and wallpaper (E), removing a pack as a unit,
editing packs, a remembered current profile, pins on Home, and pin reordering.
