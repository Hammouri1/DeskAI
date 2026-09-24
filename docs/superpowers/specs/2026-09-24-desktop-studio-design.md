# Desktop Studio — design

Status: agreed with the owner in conversation on 2026-09-24; awaiting the owner's review of this
document. Nothing here is built yet.

## Goal

The owner's original idea for DeskAI: connect the Desktop, and DeskAI turns a messy Desktop into a
sorted, good-looking one, covering **folders and files**. AI works out what everything is. DeskAI
offers separate, simple features that a person uses **one at a time, only the ones they like**:
someone may want their things shown in groups and nothing else. Every feature previews first,
and each has its own **Put back**.

## Decisions the owner made (2026-09-24)

1. Organizing means more than "put every folder into a big folder". That is one option among
   several designs.
2. Every feature can be chosen on its own. Using one never switches another on.
3. AI decides the groups, because it detects better. Without AI set up, DeskAI makes a simpler
   guess from file types and says so.
4. For each folder, AI may see its **name, the kinds of files inside, and up to 5 file names from
   inside**, only after the person sees the exact list and presses Send. Never contents, never
   where anything is on the computer.
5. AI suggests the group names (at most 8). The person can rename or merge groups and move items
   between them.
6. Build order: Find groups → Keep together / Make zones / Name the zones → Clear old stuff and
   Folder by group → Tag names → Color groups.
7. Naming style "short and friendly": the page is **Desktop Studio**.

## The page and its features

A new page, **Desktop Studio**, for a connected Desktop. It has one card per feature, and each
card has **Preview**, **Apply**, and **Put back**.

| Card | What it does | Changes on the PC | Needs |
|---|---|---|---|
| **Find groups** | AI sorts the Desktop's folders and files into groups; the person sees and adjusts them | Nothing | — |
| **Keep together** | Icons from the same group sit next to each other, no labels | Icon positions | groups |
| **Make zones** | Each group gets its own area of the screen | Icon positions | groups |
| **Name the zones** | A generated wallpaper shows each group's name behind its zone | Wallpaper | Make zones applied |
| **Clear old stuff** | Folders and files unchanged for 6 months go into one "Old stuff" folder | Moves items | — |
| **Folder by group** | Each group goes into its own folder | Moves folders and files | groups |
| **Tag names** | The group name goes in front of each folder's name ("Coding – Python stuff") | Renames folders | groups |
| **Color groups** | Each group's folders get their own color | Folder icon settings | groups |

A card that needs groups first opens **Find groups** if there are none yet. The person still sees
and confirms the groups. **Put back** undoes only that card's own last change.

## Shared piece: the groups board (Find groups)

- **What is looked at:** a fresh, read-only look at the connected Desktop: the folders and loose
  files sitting directly on it (at most 60 folders and 200 files per request; anything beyond
  that is grouped by DeskAI's own type rules, and the page says so). For each folder: its name,
  counts of file types found up to 4 levels inside, and up to 5 file names. Hidden, system,
  link, and protected items (including DeskAI's own program folder) are skipped by the existing
  path policy.
- **The Send window** lists exactly what will be sent, item by item. Cancel sends nothing.
- **The AI request** is a new `AiSuggestionTask` ("group desktop") through the existing provider,
  daily limit, timeout, and no-retry rules. Items are sent as numbers with their details; the
  reply must name at most 8 groups and give a group or "not sure" per number. Each group name
  must pass the existing `FolderNameCheck`. A reply that is off-shape in any way is refused as a
  whole.
- **Without AI**, DeskAI's own grouping by dominant file type (for example many `.py`/`.cs`
  files = Coding) fills the board, labelled as a simpler guess.
- **The board** shows one box per group plus **Not sure**. The person can rename a group, merge
  two, and move an item. It is saved in a new table keyed by the folder and each item's relative
  path, cascading with the folder (Disconnect and Start fresh erase it). An item that has
  disappeared since is dropped from the board with a note.
- AI output is advice only. Nothing in this step can change anything on disk or in Windows.

## Screen features: Keep together, Make zones, Name the zones

- **AI plays no part here.** Layouts are plain arithmetic from the board, the main screen's work
  area, and Windows' icon spacing.
  - **Keep together**: clusters in reading order from the top left, one gap between groups.
  - **Make zones**: the person picks one of up to three designs, each shown as a small picture
    of the screen: **Columns**, **Corners and edges** (4 groups or fewer), **Rows**.
  - Other icons (Recycle Bin, shortcuts, items not on the board) go in one "Everything else"
    area. If the icons don't fit, DeskAI says so and applies nothing.
- **Applying:** the first time, DeskAI asks before turning off Windows' "Auto arrange icons"
  (required for placed icons). It saves **every icon's current position and the Auto arrange
  setting** before moving anything. **Put back** restores them, including after a restart; icons
  that no longer exist are skipped and named.
- **Name the zones** draws a wallpaper on this computer (the current wallpaper, softened, or a
  plain color, with a labelled panel behind each zone) and saves it in DeskAI's own data folder.
  It then uses the existing wallpaper path: the old wallpaper is written down first, and
  **Put back** restores it (ADR 0029). Nothing is downloaded or generated by AI.
- **Main screen only.** A resolution change may make Windows reshuffle icons, and the help says so.
- **Adapter:** icon positions go through a new Core contract implemented in App/Infrastructure
  with the Windows shell's desktop view. Tests and the UI preview replace it with a recording
  fake, as they do for the wallpaper. **The real Desktop is never touched by a test.**
- **Feasibility check first:** Windows has no official "set icon position" setting, but the
  shell lets a program position items in the Desktop's view, and other tools do. The first task
  of this stage is a small probe (in Windows Sandbox, since only a Desktop keeps free positions)
  to confirm this works reliably on the owner's Windows 11 (build 26200). If it does not, these
  three cards are dropped and the owner is told.
  **Outcome (2026-09-24): it does not.** Icons can be placed, but a refresh or an Explorer restart
  puts them back on the grid, so the three cards are dropped (ADR 0043).

## Disk features: Clear old stuff, Folder by group, Tag names

- They use the **existing Tidy flow** — a preview with one tick box per item, approval, the one
  executor, the write-ahead journal, interrupted-run recovery, and **Put back** (undo) that
  survives restarts — but under **their own yes**, not the "Allow tidying" permission, whose
  dialog promises never to touch what is inside a folder (ADR 0044, agreed with the owner
  2026-09-24). Put back is offered for the latest change on the Desktop only. The preview states
  the total, for example "3 folders holding 1,204 files".
- New executor actions, each with live re-checks just before acting: **move a folder**
  (within the same connected Desktop, which is a single rename in Windows, so a folder is never
  half-moved) and **rename a folder** (for Tag names). Built 2026-09-24: the rename is the same
  folder move to a new name in the same place, not a new action (ADR 0045).
- **Unticked by default, with a warning:** folders that look like active projects (`.git`,
  `.sln`, `package.json`, a Python environment), folders containing programs (`.exe`), and
  folders with online-only files. Moving or renaming these can break programs, shortcuts, or
  games that remember the old place.
- **Left alone, with the reason shown:** a same-name clash (never overwritten or merged), a file
  in use, an item changed since the preview, protected or link items.
- **Clear old stuff** uses the same 6-month age as Home's "unused" reading
  (`StorageSummaryService.OldFileAge`) and the item's last-changed date.
- **Tidy while I'm away never moves or renames folders.** It stays limited to loose files.

## Color groups

Each group's folders get a colored folder icon through the folder's hidden `desktop.ini`. This
changes a file inside each folder and needs icon files that stay available even if DeskAI is
removed. It starts with its own probe and review. If it cannot be undone cleanly (restoring or
removing exactly the `desktop.ini` and attributes DeskAI changed), the card is dropped and the
owner is told.
**Outcome (2026-09-24): dropped by the owner.** The probe's first run showed colouring and
an exact Put back work, but the owner decided coloured folders do not look good enough (ADR 0046).

## Safety summary

- AI receives only what the Send window shows; it has no file, shell, or Windows access, and its
  reply can only fill the board.
- Sending folder names and file names from inside folders is a **new disclosure** (until now AI
  never saw folder names). It gets its own ADR and security review before code.
- Icon positions and the generated wallpaper are Windows setting changes, the second and third
  after ADR 0029. They get an ADR and a security review, with snapshot-first and Put back.
- Moving and renaming folders widen the executor. They get an ADR and a security review
  tracing every threat case to a test.
- Nothing is deleted, ever.

## Testing

Every card gets page tests in `DeskAI.Presentation.Tests` using generated Desktop folders in
`TestApp`, a fake AI transport, and a recording icon-position adapter, plus rows in the Feature
Coverage Map. Engine tests cover the AI reply parser (including hostile names), layout
arithmetic, snapshot/restore, and the new executor actions' refusal cases.

## Build order (each step is released and checked by the owner before the next)

1. Find groups (board, Send window, AI request, local fallback, storage).
2. Icon-position probe → Keep together → Make zones → Name the zones.
3. Clear old stuff and Folder by group (move a folder). Built 2026-09-24 (ADR 0044).
4. Tag names (rename a folder). Built 2026-09-24 (ADR 0045).
5. Color-icon probe → Color groups. Dropped by the owner (2026-09-24, ADR 0046).

## Not in scope

Screens other than the main one, desktops of other user accounts, arranging folders other than
the Desktop on screen (the disk features may later extend to other connected folders), and
anything that runs without the person pressing Apply.
