# ADR 0043: Placing Desktop Icons (Rejected after the probe)

- Status: Rejected (probe, 2026-09-24)
- Date: 2026-09-24
- Review: `docs/security/2026-09-24-icon-position-probe-review.md`

## Context

Desktop Studio step 2 (Keep together, Make zones, Name the zones) needs DeskAI to place icons on
the Desktop. Windows has no documented setting for an icon's position. The shell's Desktop view
(`IFolderView2`, reached through `IShellWindows.FindWindowSW(SWC_DESKTOP)`) can read and set
positions when "Auto arrange icons" is off, and other tools use it. Whether that is reliable on
the owner's Windows (build 26200) — kept after a refresh and after Explorer restarts, and put back
exactly — is unknown.

## Decision (proposed)

- **Probe first.** `tools/IconPositionProbe` answers the question. It runs only inside Windows
  Sandbox (user `WDAGUtilityAccount`), refuses anywhere else, and works only on the Sandbox's
  throwaway Desktop with networking off. It is not part of the app.
- **Go** if, in the Sandbox: every placed icon reads back within half an icon-spacing step, the
  positions survive a view refresh, and Put back restores every original position and the Auto
  arrange setting. Surviving an Explorer restart is recorded; if it fails, the app design must
  re-apply or say so, and the owner decides.
- **No-go** otherwise: Keep together, Make zones, and Name the zones are dropped and the owner is
  told (design, "Feasibility check first").
- If go, a later plan adds a Core contract implemented with this same shell path, snapshot-first,
  asking before turning off Auto arrange, and Put back — with its own security review.

## Consequences

The probe changes Explorer settings and icon positions, but only on a Desktop that is deleted
when the Sandbox closes. No test, and no agent, touches the owner's Desktop.

## Probe results (2026-09-24)

Two runs in Windows Sandbox (it reports build 26100; the host is 26200). Both: **not reliable**.

| Stage | Run 1 (as planned) | Run 2 (layout saved after placing and after Put back) |
|---|---|---|
| place | Passed | Passed |
| refresh | Failed — all 12 icons back on the left-edge grid | Failed — same |
| explorer restart | Failed — same grid positions | Failed — same |
| put back | Passed | Failed — after a save and a refresh, Recycle Bin and Microsoft Edge swapped places |

Notes from both runs: dpi 96, icon spacing 76 x 106, work area 0,0 - 1520,775, Auto arrange off
at start, 15 icons at start.

Run 2 added `IShellView.SaveViewState` after placing, on the idea that run 1 lost the positions
only because Explorer was never asked to store them. It made no difference: a refresh (what F5
does) laid the Desktop out again from Explorer's own stored layout, and nothing this path offers
updated that layout in either run. The explorer-restart stage uses a hard stop, which also skips
Explorer's own save; it is recorded, not decisive.

## Outcome

Icons can be placed, but a refresh or an Explorer restart undoes it, so DeskAI could not keep
a person's Desktop the way it promised. Keep together, Make zones, and Name the zones are
dropped (design, "Feasibility check first"). A different route (writing Explorer's stored
layout in the registry, or driving the Desktop's list control from outside) would need
registry or cross-process window access that `docs/SECURITY.md` keeps away from this feature;
it is not pursued without a new decision by the owner.
