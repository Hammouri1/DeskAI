# ADR 0043: Placing Desktop Icons (Proposed, waiting for the probe)

- Status: Proposed
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
