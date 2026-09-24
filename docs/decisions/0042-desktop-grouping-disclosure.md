# ADR 0042: Find groups — AI may sort what sits on the Desktop

- Status: accepted, 2026-09-24.
- Design: `docs/superpowers/specs/2026-09-24-desktop-studio-design.md` (step 1).
- Review: `docs/security/2026-09-24-desktop-grouping-review.md`

## Decision

- A new page, **Desktop Studio**, has one card, **Find groups**. It sorts the folders and loose
  files sitting directly on a **connected Desktop** into at most **8 groups**, plus **Not sure**.
- **What AI may see**, per request and only after the person sees the exact list and presses
  **Send**:
  - per folder on the Desktop: its **name**, the **kinds of files** found up to 4 levels inside
    (counts per file ending, at most 8 endings), and **up to 5 file names** from inside;
  - per loose file: its **name**.
  Never contents, sizes, dates, locations, full paths, or DeskAI IDs. Items are sent as
  numbers with these details, between untrusted-data markers.
- **Bounds:** at most 60 folders and 200 loose files per request. The rest are sorted by
  DeskAI's own guess and the page says so. Hidden and system items, links, protected entries,
  and any top-level folder holding a protected or link entry (such as DeskAI's own program
  folder) are left out entirely.
- **Sharing choices:** online AI is used only when the saved sharing choices include file types,
  file names, and folder names. Otherwise nothing is sent and the page says which to allow. The
  choice is checked when the list is prepared, again at Send, and again inside the AI
  connection. A change of AI service or sharing between the two steps sends nothing.
- **The reply** must be exactly `{"schemaVersion":"1","groups":[{"name":…,"items":[…]}]}`:
  at most 8 groups, each name passing `FolderNameCheck`, unique, never "Not sure", and every
  number known and used once. Anything else is refused as a whole and the old board is kept.
- **Without AI**, DeskAI's own guess from the kinds of files fills the board, labelled as a
  simpler guess.
- **The board** is stored as bounded JSON in one row per connected folder (schema 16). It
  cascades with the folder, so Disconnect and Start fresh erase it. Items that have left the
  Desktop drop off the board the next time it is shown.
- **Nothing changes on disk or in Windows** in this step: no executor, journal, wallpaper, or
  icon code is reachable from it.

## Why

The owner's original idea for DeskAI is to make a messy Desktop sorted and good-looking. Every
later Desktop Studio card (Keep together, Make zones, Folder by group, Tag names, Color groups)
needs to know which things belong together, and the owner decided AI should decide that because
it detects better.

Until now AI saw file-level details only: an extension, a file name, and, if allowed, the folder
path a file sits in (ADR 0020, ADR 0034). It never saw a whole Desktop summarized folder by
folder, nor file names taken from inside a folder to describe it. That is a new disclosure, so it
gets this decision and its own review. It reuses the existing sharing categories rather than
adding new ones, and requires all three at once.

## Consequences

- AI may misgroup things. The person can rename and merge groups and move any item, and the
  page labels who made the board.
- Later cards consume the board. Each of them gets its own decision and review before it may
  change anything on disk or in Windows.
- A Desktop with more than 60 folders or 200 loose files is only partly sorted by AI.
