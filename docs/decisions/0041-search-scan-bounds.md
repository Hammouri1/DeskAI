# ADR 0041: Search looks deeper, keeps what it could not reach, and looks again on open

Status: accepted, 2026-09-24.

## Decision

- A look at a connected folder for Search now goes **8 folder levels deep and up to 20,000
  items**, instead of 4 levels and 2,000. The bounds live in `SearchScanBounds.Default` and are
  registered once, so tests can use small ones. Tidy keeps its own separate bounds.
- A look that stops at the item limit **adds and updates what it saw but forgets nothing**.
  Only a complete look forgets files that were not seen.
- How the last look went — when, whether it stopped early, and how many folders were too deep —
  is kept per folder in a new `index_looks` table (schema 15). It cascades with the folder, and
  both Disconnect and Start fresh erase it.
- The Search page says when a look did not cover the whole folder: on the folder row, in the
  Connect and Refresh message, and in "Nothing matched" when a partly checked folder was searched.
- Opening Search looks again at any connected folder last checked **more than 10 minutes ago**,
  through the same `ConnectedFolderService.RefreshAsync` that the Refresh button uses. Leaving
  the page stops it. The Refresh button stays.

## Why

The owner's goal is to find a forgotten file anywhere inside a connected folder. With the old
bounds, an ordinary Documents folder went past 2,000 items and Search silently missed files.
Worse, the index treated every file the look had not reached as "no longer there" and dropped
it, so files found on an earlier look disappeared too. A new file also needed a manual Refresh
unless automatic checking was on.

## Threat cases

| Case | Control |
| --- | --- |
| Wider reach | None. The same authorized roots, path policy, protected-location and reparse-point checks apply to every entry, and the scanner still opens no file: it reads names, sizes, dates, and attributes from the directory listing. |
| Cloud placeholder files | Listed only. Reading attributes does not download them; tidy already leaves online-only files alone. |
| Stale remembered names after an early stop | Metadata only. Tidy re-checks every file on disk before moving it, and content readers re-check containment and existence before opening, so nothing can act on a stale entry. The page says the look was partial. |
| A very large folder keeps DeskAI busy | The bounds stay finite and the look can be cancelled. Measured: 20,000 generated files connect in about 1 s and refresh in about 0.3 s (`docs/PERFORMANCE.md`). |
| Automatic looking on open | It only runs what the Refresh button runs, on folders already connected. It changes no permission and sends nothing anywhere. No AI is involved. |
| Bigger database | About ten times more rows per folder at most. Rows are erased on Disconnect and Start fresh as before. |

## Consequences

A folder beyond 20,000 items is still only partly searchable, but the page now says so and no
longer forgets files. A file deleted from the part a look did not reach stays remembered until a
complete look. Opening Search can take about a second longer on a very large folder; searching
works while it runs.
