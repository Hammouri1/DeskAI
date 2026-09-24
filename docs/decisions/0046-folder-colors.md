# ADR 0046: Coloured Folder Icons (Dropped by the owner)

- Status: Rejected (owner, 2026-09-24)
- Date: 2026-09-24
- Review: `docs/security/2026-09-24-folder-color-probe-review.md`
- Plan: `docs/superpowers/plans/2026-09-24-folder-color-probe.md`

## Context

Desktop Studio's last step, Color groups, gives each group's folders their own colour. Windows
has no colour setting for a folder, but a folder can show its own icon: Windows'
folder-customization call (`SHGetSetFolderCustomSettings`) writes an `IconResource` line into the
folder's hidden `desktop.ini` and marks the folder read-only so Explorer reads that file. This is
the first Desktop Studio change that writes inside a person's folder rather than moving or
renaming it, and the icon file it points to must stay where it is.

The design says the card is dropped if it cannot be undone cleanly: restoring or removing exactly
the `desktop.ini` and attributes DeskAI changed. Whether that works, and whether Explorer shows
the colour after a refresh and a restart and stops showing it after Put back (Explorer keeps an
icon cache), is unknown on the owner's Windows (build 26200).

## Decision (proposed)

- **Probe first.** `tools/FolderColorProbe` answers the question. It runs only inside Windows
  Sandbox (user `WDAGUtilityAccount`), refuses anywhere else, and works only on four folders it
  makes on the Sandbox's throwaway Desktop, with networking off. It is not part of the app.
- **Put back from a snapshot.** Before colouring, the probe writes down each folder's attributes
  and whether it had a `desktop.ini`, with its exact bytes and attributes. Put back restores
  those, or removes the `desktop.ini` if there was none, then tells Explorer the folder changed.
- **Go** only if, in the Sandbox, all of these pass:
  - `color`: every probe folder's `desktop.ini` names the probe icon, the folder that already had
    its own `desktop.ini` keeps its other lines, and the shell reports the probe icon.
  - `refresh` and `explorer restart`: each folder's icon area on screen shows at least 200 pixels
    of its colour (within 40 per colour channel).
  - `put back`: every folder's attributes, `desktop.ini` presence, bytes, and attributes equal the
    snapshot, and the shell reports the original icon.
  - `put back after refresh`: each folder's icon area shows at most 20 pixels of its colour and,
    at the same place as at the start, looks like the start (mean difference at most 20 of 255),
    because a window over the icons also has no colour.
- **Recorded, not required:** whether the colour shows before a refresh (if not, the app must
  refresh the Desktop itself), and what a coloured folder shows after its icon file is deleted
  (what a person would see after removing DeskAI).
- If the screen cannot be read (for example the Sandbox window was minimized), the pixel stages
  are Skipped and the verdict is "not reliable"; the owner's look at the saved pictures decides.
- **No-go** otherwise: Color groups is dropped and the owner is told (design, "Color groups").

## Consequences

The probe writes `desktop.ini` files and changes folder attributes, but only on folders it made
on a Desktop that is deleted when the Sandbox closes. No test, and no agent, touches the owner's
Desktop.

If the answer is go, a later plan still has to decide, with its own security review:

- where the icon files live so a coloured folder does not turn blank if DeskAI is removed;
- that Put back removes a `desktop.ini` only when DeskAI made it and it still holds exactly what
  DeskAI wrote (the rule "nothing is deleted" otherwise holds, as for the empty folders DeskAI
  makes in a run);
- what happens when a person or another program changes the `desktop.ini` after DeskAI did
  (Put back leaves it alone and says so);
- the journal record for a colour change and its separate yes (ADR 0044's moving permission does
  not cover writing inside folders).

## Probe results

**Run 1 (2026-09-24), Windows Sandbox build 26100, dpi 96, spacing 76 x 106.** Verdict "not
reliable", but only because the probe's own check could not compare:

| Stage | Result |
|---|---|
| color | Passed: every `desktop.ini` and the shell named the probe icon; the custom folder kept its info tip and `[ViewState]` lines |
| color seen before refresh | Passed: 1,247 coloured pixels per folder (no refresh needed) |
| refresh, explorer restart | Passed: 1,247 per folder |
| put back | Passed: every folder's attributes and `desktop.ini` (bytes, attributes, or absence) equal the snapshot; the shell names the original icon |
| put back before / after refresh | Failed: 0 coloured pixels, but every folder had moved up one place, so it was not compared with the start picture |
| icon file missing | The folder shows the ordinary yellow folder icon (shell: `imageres.dll,-3`), not a blank icon |

The Explorer restart had re-sorted the Desktop: Microsoft Edge moved from above the probe folders
to below them. The stage pictures show clean yellow folders after Put back. Fix for run 2: the
probe restarts Explorer once before the start picture, so the re-sort happens first.

**Run 2 (2026-09-24).** Stopped at the first step: "Explorer did not show the probe's folders
within 60 seconds", before anything was coloured. The probe did not say what it saw. It now waits
up to 120 seconds, and the report says how long the first look took and what the last look saw.

## Outcome

Dropped by the owner on 2026-09-24, before a third run. Run 1 showed that colouring and an exact
Put back work, but the owner decided coloured folders do not look good enough and are not what
they wanted from Desktop Studio: a designed Desktop where DeskAI places the icons, which ADR 0043
showed Windows does not keep. Color groups is not built. `tools/FolderColorProbe` stays, like the
icon-position probe, as the record of what was tried.
