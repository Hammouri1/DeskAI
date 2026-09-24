# Folder-Color Probe Security Review

- Date: 2026-09-24
- Scope: ADR 0046's probe (`tools/FolderColorProbe`), not the later Color groups card
- Result: accepted for Windows Sandbox only

| Threat | Control | Test / evidence |
|---|---|---|
| The probe runs on the owner's real Desktop | `SandboxGuard.Check` runs first; any user other than `WDAGUtilityAccount` exits with code 2 before a file or shell call | `SandboxGuardTests.Refuses_every_user_except_the_sandbox_account`, `SandboxGuardTests.Refuses_a_look_alike_name`; one local run that must refuse (Task 3) |
| The probe changes something outside the Sandbox | The `.wsb` maps the probe folder read-only and only `artifacts/color-probe/results` writable; networking, clipboard, and printers are off | `Run-InSandbox.ps1`; the generated `.wsb` is read before the run (Task 4) |
| The probe touches a folder it did not make | It colours and puts back only the four folders it creates, by full path under the Sandbox Desktop | `Program.cs` builds every path from its own generated names |
| A `desktop.ini` that was already there loses lines | Colouring goes through Windows' own call, which edits only the icon line; the check compares the other lines | `color` stage; `DesktopIniTests` |
| Put back is not exact | Snapshot of folder attributes and `desktop.ini` presence, bytes, and attributes; compared after Put back | `put back` stage; `FolderStateTests` |
| Explorer keeps showing the colour from its icon cache | Pixel check after Put back and a refresh: no colour left, and each icon looks like the start | `put back after refresh` stage; `ColorCountTests` |
| A blank, covered, or moved icon passes the "no colour" check | A capture that is all one colour is unreadable and makes the pixel stage Skipped, which is not reliable; each icon must also be at its start position and look like the start (a File Explorer window, a toast, or the Start menu over the icons fails this); after the Explorer restart the probe starts explorer.exe only if Windows did not bring it back, so no extra window opens | `ColorCountTests.An_all_one_colour_capture_is_unreadable`, `ColorCountTests.A_covered_icon_differs_from_the_start`, `ColorProbeReportTests.A_skipped_pixel_stage_is_not_reliable` |
| AI or a network service involved | None referenced; the project has no package references and links only the icon probe's Desktop view, wait, and geometry files | `FolderColorProbe.csproj` |
| Personal data in the report or pictures | The pictures show only the Sandbox Desktop; the report lists only the probe's own folder names | Written in the Sandbox; read before anything is committed (pictures are not committed) |

No registry API is used. Explorer is restarted only inside the Sandbox. The probe is never copied
into the app's output or release.
