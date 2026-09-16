# Performance notes

DeskAI's everyday work is a scan of a connected folder's names, sizes, and dates, an index
refresh in SQLite, a search over that index, and a tidy preview of a folder's loose files.
None of it opens a file, and all of it is bounded: a connect or refresh reads at most 2,000
entries four levels deep (`ConnectedFolderService.ScanBounds`), a search returns at most its
query limit, and a tidy preview considers at most 500 loose files.

## How to measure

`tests/DeskAI.Presentation.Tests/PerformanceProbe.cs` generates 3,000 files under the test's
own temp folder and times connect, refresh, search, the Home summary, and a tidy preview. It
runs only when `DESKAI_PERF` is set, so the ordinary suite never depends on machine speed:

```powershell
$env:DESKAI_PERF = "1"
dotnet test --project tests\DeskAI.Presentation.Tests\DeskAI.Presentation.Tests.csproj -c Release
```

The timings are printed as a diagnostic message and written to `perf.txt` in the test's temp
folder. Record them below when something changes that could move them.

## Recorded runs

| Date | Machine | Files | Connect | Refresh | Search | Home summary | Tidy preview |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-09-16 | Owner's development PC, Windows 11, Release build | 3,000 generated | 128 ms | 33 ms | 24 ms (200 shown) | 24 ms | 63 ms (500 of 500) |
| 2026-09-16 (after V1.1) | Same PC and build | 3,000 generated | 124 ms | 36 ms | 21 ms (200 shown) | 33 ms | 74 ms (500 of 500) |

The second run is after V1.1. Connect, refresh, and search are unchanged within the noise of a
single run, which is what was expected: the new four-folder rule costs one path comparison per
connect, not per file. **Home takes about 9 ms longer** because the page now also loads the Your
folders card (one list of connected folders) and the Ask DeskAI card (two settings reads: the AI
choice, and whether the person has already agreed to send questions) before it draws. That is
three small database reads, and it is the price of the two cards. The run above was taken just
before the second of those reads was added, so Home is a fraction slower again.

Note: the table is updated by hand from the probe's output (`%TEMP%\DeskAI-perf.txt`). A
connect stops at the scan bound and the page says so, and a tidy preview stops at 500 loose
files and says so; both limits are the honest behaviour being measured, not a shortfall.

## What is deliberately not optimised

- Scanning is single-threaded and streams entries; parallel scanning would not change what a
  person waits for on an SSD and would complicate cancellation.
- The index is refreshed by comparing the new scan with the stored rows and writing only real
  differences, so a second refresh of an unchanged folder costs about the same as the first
  scan and no writes.
- Storage summaries aggregate in SQL (`SummarizeRootAsync`), so a folder with a hundred
  thousand remembered files costs about the same to summarise as one with ten.
