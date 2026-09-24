# Folder-Color Probe Plan

> Built inline, one task at a time, with one fresh review at the end (owner, 2026-09-24).

**Goal:** Find out, on Windows Sandbox's throwaway Desktop, whether DeskAI can give Desktop
folders a coloured icon that stays after a refresh and an Explorer restart, and then put every
folder back exactly as it was, before any Color groups code is written.

**Architecture:** A small program, `FolderColorProbe`, that is **not part of the app**, built the
same way as the icon-position probe (ADR 0043). It sets each folder's icon with Windows' own
folder-customization call (`SHGetSetFolderCustomSettings`, which writes the folder's hidden
`desktop.ini` and marks the folder), and puts it back from a snapshot taken first: the exact
`desktop.ini` bytes and attributes and the folder's attributes. It checks the files, asks the
shell which icon it would use (`SHGetFileInfo`), and reads the screen pixels where each folder's
icon is drawn, saving a picture of the Desktop at each stage. It refuses to run anywhere except
inside Windows Sandbox.

**Tech stack:** C# / .NET 10 (`net10.0-windows`, `WinExe` so no console window covers the
icons), `[DllImport]` and the existing `[ComImport]` Desktop view, xUnit v3 for the pure parts,
PowerShell for the Sandbox script.

**Spec:** `docs/superpowers/specs/2026-09-24-desktop-studio-design.md` ("Color groups").

## Global constraints

- The real Desktop is never touched by a test, by the probe, or by the agent. The probe's only
  allowed Desktop is Windows Sandbox's (`WDAGUtilityAccount`).
- "If it cannot be undone cleanly (restoring or removing exactly the `desktop.ini` and attributes
  DeskAI changed), the card is dropped and the owner is told." (spec)
- No AI, no registry API, no network (`<Networking>Disable</Networking>`).
- Nothing from the probe ships in `DeskAI.App`; no app project references it.
- The rejected icon-position probe keeps its behaviour; only `Point`/`Rect` move to their own file
  so the new probe can reuse the Desktop view and the bounded wait by linking the files.
- Commit each task locally on `desktop-studio-find-groups`; do not push.

## Review focus

1. **Started on the owner's PC** → refuses before creating a file or calling the shell (own
   `SandboxGuard`, first line of the run).
2. **A folder that already had its own `desktop.ini`** (another icon, an info tip, extra
   sections) → colouring keeps its other lines, and Put back restores the exact bytes and
   attributes.
3. **A folder that was already read-only** → Put back leaves it read-only; a folder that was not
   → Put back clears the mark Windows added.
4. **Windows keeps showing an old icon from its icon cache** → the pixel check after Put back and
   after a refresh catches it.
5. **The screen cannot be read** (Sandbox window minimized, capture all black) → the pixel stages
   say so and are not counted as passed; the saved pictures and the owner's eyes decide.
6. **The icon file is gone later** (DeskAI removed) → recorded, not required: what the folder
   shows then.

## Files

| File | Responsibility |
|---|---|
| `docs/decisions/0046-folder-colors.md` | Proposed decision, the go/no-go rule |
| `docs/security/2026-09-24-folder-color-probe-review.md` | Threats for the probe itself |
| `tools/IconPositionProbe/Geometry.cs` | `Point` and `Rect`, moved out of `SandboxGuard.cs` |
| `tools/FolderColorProbe/FolderColorProbe.csproj` | Probe project; links `Geometry.cs`, `Waiting.cs`, `DesktopShellView.cs` |
| `tools/FolderColorProbe/SandboxGuard.cs` | Only-in-Sandbox rule (pure) |
| `tools/FolderColorProbe/PngWriter.cs` | RGBA pixels → PNG bytes (pure) |
| `tools/FolderColorProbe/FolderIcon.cs` | A coloured folder shape → `.ico` bytes (pure) |
| `tools/FolderColorProbe/FolderState.cs` | Snapshot of a folder's attributes and `desktop.ini`; differences (pure) |
| `tools/FolderColorProbe/DesktopIni.cs` | Reads a key from `desktop.ini` text (pure) |
| `tools/FolderColorProbe/ColorCount.cs` | Counts pixels near a colour inside a rectangle (pure) |
| `tools/FolderColorProbe/ColorProbeReport.cs` | Stages, notes, the verdict (pure) |
| `tools/FolderColorProbe/FolderShell.cs` | `SHGetSetFolderCustomSettings`, `SHGetFileInfo`, `SHChangeNotify` |
| `tools/FolderColorProbe/ScreenCapture.cs` | Screen pixels as BGRA |
| `tools/FolderColorProbe/Program.cs` | The run |
| `tools/FolderColorProbe/Run-InSandbox.ps1` | Publish, write the `.wsb`, start the Sandbox |
| `tests/DeskAI.FolderColorProbe.Tests/*` | Tests for the pure parts |

## Task 1: Proposed decision and security review

Write ADR 0046 (Status "Proposed") with the go/no-go rule below, and the probe's security review
with one row per review-focus item and the test that pins it. Commit.

**Go** only if, in the Sandbox, all of these pass:

- `color`: every probe folder's `desktop.ini` names the probe icon, the pre-existing folder keeps
  its other lines, and the shell reports the probe icon for each folder.
- `refresh` and `explorer restart`: each folder's icon area shows at least 200 pixels of its
  colour (within 40 per channel).
- `put back`: every folder's attributes, `desktop.ini` presence, bytes, and attributes equal the
  snapshot, and the shell reports the original icon.
- `put back after refresh`: each folder's icon area shows at most 20 pixels of its colour and,
  at the same place as at the start, looks like the start (mean difference at most 20 of 255),
  because a window over the icons also has no colour.

Recorded only: `color seen before refresh`, and `icon file missing` (what a coloured folder shows
after its icon file is deleted). If the screen cannot be read, the pixel stages are Skipped, the
verdict is "not reliable", and the owner's look at the saved pictures decides.

## Task 2: The probe project and its pure, tested parts

Move `Point`/`Rect` to `tools/IconPositionProbe/Geometry.cs`. Create the probe project (stub
`Program.cs`), the test project, and add both to `DeskAI.sln` under the `tools` folder and
tests. Write tests first, see them fail, then the code:

- `SandboxGuardTests`: only `WDAGUtilityAccount` (exact, case-sensitive) is allowed.
- `PngWriterTests`: signature, IHDR size, the known `IEND` CRC `AE426082`, and the pixels
  round-trip through `ZLibStream`.
- `FolderIconTests`: an ICO header with 4 images (16, 32, 48, 256), each a PNG, and the colour
  present in the 48 px image.
- `FolderStateTests`: equal snapshots give no differences; a changed attribute, changed bytes,
  an added or a removed `desktop.ini` are each named.
- `DesktopIniTests`: reads `IconResource` from `[.ShellClassInfo]`, case-insensitive key, ignores
  the same key in another section, null when absent.
- `ColorCountTests`: counts only pixels within tolerance and inside the rectangle; a rectangle
  partly off-screen is clipped.
- `ColorProbeReportTests`: reliable only when the five required stages passed; a Skipped pixel
  stage is not reliable; notes appear.

Run all three verification commands. Commit.

## Task 3: The Windows calls and the run

`FolderShell` (set the icon with `FCS_FORCEWRITE | FCSM_ICONFILE`, read the icon location with
`SHGFI_ICONLOCATION`, notify with `SHCNE_UPDATEITEM`/`SHCNE_UPDATEDIR`), `ScreenCapture`
(`BitBlt` into a DIB, per-monitor DPI aware), and `Program`:

guard → make four folders on the Sandbox Desktop (plain, plain, already read-only, one with its
own `desktop.ini` holding an icon, an info tip, and a `[ViewState]` section) → write two icon
files (magenta, green) under the Sandbox's `%LOCALAPPDATA%\DeskAIColorProbe` → snapshot → colour
→ picture → refresh → picture → restart Explorer → picture → put back → picture → refresh →
picture → colour one folder again, delete its icon file, refresh, picture, put back → report.

Build, run the tests, then run the exe here once to prove it refuses (the one run allowed outside
the Sandbox). Commit.

## Task 4: The Sandbox script, the run, and the answer

`Run-InSandbox.ps1` like the icon probe's (probe folder read-only, results folder writable,
networking, clipboard, and printers off). Add a short section to `docs/DEVELOPMENT.md`. The owner
turns on the Sandbox if needed, runs the script, and keeps the Sandbox window open until the
report appears. Read the report and pictures, record the result in ADR 0046, the design's "Color
groups" section, and `docs/HANDOFF.md`, and tell the owner go or no-go. Commit.

Then the one fresh review of the whole probe change, and its fixes.

## Review fixes (before the Sandbox run)

The fresh review ran before the owner's Sandbox run, so the run uses the fixed probe. No critical
findings. Fixed: (1) "no colour left" also passed for an icon hidden behind a window, so Put back
now also compares each icon with the start picture, and after the Explorer restart the probe
starts `explorer.exe` only if Windows did not bring it back (a second one opens a window over the
icons); (2) the custom folder's own icon had green in it and was scored against green, so it now
uses the plain folder icon (`shell32.dll,3`); (3) icon paths are compared after expanding
`%LOCALAPPDATA%` and similar; (4) a failure while Explorer is starting now means "not ready yet"
instead of ending the run before Put back, and a refused icon is recorded and the run goes on.
Smaller: a UTF-8 `desktop.ini` is read, the shell's icon answer after Put back is asked once more
before it counts as wrong, a screen-capture handle is always released, and the thresholds in the
ADR match the code.
