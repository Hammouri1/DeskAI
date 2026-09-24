# Icon-Position Probe Security Review

- Date: 2026-09-24
- Scope: ADR 0043's probe (`tools/IconPositionProbe`), not the later Desktop Studio cards
- Result: accepted for Windows Sandbox only

| Threat | Control | Test / evidence |
|---|---|---|
| The probe runs on the owner's real Desktop | `SandboxGuard.Check` runs first in `Main`; any user other than `WDAGUtilityAccount` exits with code 2 before a file or shell call | `SandboxGuardTests.Refuses_every_user_except_the_sandbox_account`, `SandboxGuardTests.Refuses_a_look_alike_name` |
| The probe changes something outside the Sandbox | The `.wsb` maps the probe folder read-only and only `artifacts/icon-probe/results` writable; networking is disabled | `Run-InSandbox.ps1` writes `<ReadOnly>true</ReadOnly>` and `<Networking>Disable</Networking>`; checked by reading the generated `.wsb` in Task 4 |
| The probe leaves the Desktop changed | Put back restores every original position and the Auto arrange flags; the Sandbox is discarded on close anyway | The report's `put back` stage; `ProbeReportTests.A_failed_put_back_makes_the_verdict_not_reliable` |
| Placed icons go off-screen | Targets are computed inside the work area | `ProbeLayoutTests.Targets_stay_inside_a_small_work_area` |
| Look-alike names confuse which icon moved | Matching by exact parsing name; generated unique names | `PositionCheckTests.Matches_by_full_name_not_by_display_name` |
| AI or a network service involved | None referenced; the project has no package or project references | `IconPositionProbe.csproj` has no `PackageReference`/`ProjectReference` |
| Personal data in the report | The report lists only the probe's own generated names and the Sandbox's default icons | Report written in the Sandbox; read by the owner before it is committed |

No registry API is used. Explorer is restarted only inside the Sandbox. The probe is never copied
into the app's output or release.
