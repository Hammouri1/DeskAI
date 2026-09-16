# Security Review — Desktop and Wallpaper (V0.7 piece E)

- Date: 2026-09-16
- Scope: the two things the owner chose for piece E on 2026-09-16 — (1) making a picture the
  person picked the Windows wallpaper, with a button that puts the old one back; (2) a shortcut
  on My workspace that connects the person's Desktop folder and opens it in Organize. Shortcut
  and icon suggestions were **deferred beyond V0.7** by the owner the same day.
- Required by: `docs/SECURITY.md` — a focused review before desktop-shell customization. This is
  the first time DeskAI changes a Windows setting of any kind.
- Status: design review, written before implementation. Every control names the test that
  asserts it; the review is not satisfied until each exists and passes.

## What is being added

**Wallpaper.** The person presses **Choose a picture…**, picks one file in the Windows file
dialog, sees it in DeskAI with its name and what Windows shows now, and presses **Use as
wallpaper** in a dialog that says exactly that. DeskAI remembers what the wallpaper was before
and offers **Put the old wallpaper back**. This is DeskAI's first change to a Windows setting.

**Desktop.** The person presses **Tidy my Desktop**. DeskAI finds the Desktop folder through the
Windows known-folder API, asks "Connect your Desktop?", connects it exactly as the folder picker
would (names, sizes, and dates; nothing inside), and opens Organize on it. From there everything
is the existing Tidy: its permission dialog, its preview, its Tidy button, its undo. Nothing new
can happen to a file.

What does **not** change: no new executor command, no registry write by DeskAI's own code, no
process launch, no network, no AI in either flow, nothing reachable without a window.

## Threat scenarios

| # | Threat | Control | Test |
|---|---|---|---|
| T1 | The wallpaper changes without the person pressing the button — from an automatic check, the tray, AI, or a rule | Only `WorkspaceViewModel.UseWallpaperAsync`, called by the page after its dialog, calls `WallpaperService.UseAsync`. `WallpaperService` takes only the setter, the picture inspector, and the settings store; a reflection test forbids executor, journal, scanner, reader, credential, AI, and rule types. `AutomaticCheckService`, `BackgroundPresenceController`, and the tray adapter keep their existing reflection tests, extended to forbid `IWallpaperSetter` | `WallpaperServiceTests`, `AutomaticCheckServiceTests` (extended), `BackgroundCheckingChoiceTests` |
| T2 | The picture path comes from somewhere other than the person: AI, a rule, a file name in a folder | The path reaches the service only from the page, which got it from the Windows file dialog. No AI or scanner type is reachable (T1). DeskAI never lists folders to find pictures | `WallpaperServiceTests` reflection test; `WallpaperPageTests` |
| T3 | The picked path is not a plain local picture: a network share (`\\server\x`, which Windows would fetch), a URL, a link or junction, a non-image, a huge file | `WallpaperService.PreviewAsync` refuses, with a plain reason, anything that is not a fully qualified local path with a jpg/jpeg/png/bmp extension, an existing plain file that is not a reparse point, at most 50 MB. `UseAsync` runs the same check again at the moment of use | `WallpaperServiceTests` (each form), `FilePictureInspectorTests` (link, missing, size), `WallpaperPageTests` |
| T4 | Time of check and time of use: the file is replaced or removed between the preview and the button | Checked again in `UseAsync`; Windows reads the file at that moment. The harm if a different picture of the same name appears in between is a different wallpaper, undone by Put back | `WallpaperServiceTests` (file removed between preview and use is refused) |
| T5 | Put back cannot restore what was there: DeskAI crashed after changing the wallpaper, or the old file is gone | The previous wallpaper is written to `app_settings` **before** the change is made, in its own row, so a crash after the change still leaves it recorded. If the old file no longer exists, Put back says so and changes nothing. A plain-colour desktop (no picture) is recorded as empty and restored as a plain colour | `WallpaperServiceTests` (order of writes; missing old file; plain colour), `SqliteAppSettingsStoreTests` |
| T6 | The person changed the wallpaper in Windows after DeskAI did; Put back then throws away their newer choice | Put back is described honestly: when Windows now shows something other than what DeskAI set, the page says so next to the button before it is pressed. Put back still restores the one recorded, because that is what the button promises | `WallpaperServiceTests` (`FindRestoreAsync` notes the difference), `WallpaperPageTests` |
| T7 | Windows Spotlight or a slideshow was on; a static picture ends it, and Put back cannot bring the slideshow back | Disclosed in the dialog: "If Windows was showing a slideshow or Spotlight, that stops. Putting the old wallpaper back restores the picture only." Not preventable; the recorded picture is the slideshow's current frame | Dialog text asserted in `WallpaperPageTests` |
| T8 | DeskAI writes the registry | It does not. `SystemParametersInfo(SPI_SETDESKWALLPAPER)` with `SPIF_UPDATEINIFILE` is the documented Windows call, and Windows itself persists the setting. No registry API appears in DeskAI: the existing source-scanning test already forbids `Microsoft.Win32` and `Registry.*` in every source file, and the wallpaper adapter passes it | `NeverStartsWithWindowsTests` |
| T9 | Windows refuses the change (a policy forbids changing the background) | The call's failure becomes "Windows did not let DeskAI change the wallpaper." Nothing else changes; the recorded previous wallpaper is removed again so Put back is not offered for a change that never happened | `WallpaperServiceTests` (setter throws) |
| T10 | Tests change the developer's real wallpaper or read their real Desktop | `TestApp` replaces `IWallpaperSetter` with a recording one and `IKnownFolders` with the sandbox, the same way it replaces the credential vault and the internet, and asserts at start that the Desktop it will use is inside its own temp folder. The real setter exists only in Infrastructure and is registered by `AddDeskAiInfrastructure`; nothing in `DeskAI.Core.Tests` or `DeskAI.Presentation.Tests` can reach it | `TestApp` assertion; `WallpaperPageTests` |
| T11 | Connecting the Desktop reads more than a connected folder may | It goes through `ConnectedFolderService.ConnectAsync`, the same call the picker uses: metadata scope, bounded scan, path policy, reparse-point refusal. The Desktop path comes from the known-folder API, never typed or hardcoded | `DesktopPageTests` (connected as metadata only; no content permission; nothing moved) |
| T12 | Tidying the Desktop moves shortcuts and breaks what the person relies on | `.lnk` and `.url` are not in the classifier, so Tidy leaves them alone as an unknown type; `desktop.ini` is hidden and system and is refused by the executor. Both are existing behaviour, now pinned by a test | `DesktopPageTests` |
| T13 | The Desktop is redirected to OneDrive; moves sync to other devices, and online-only files would download | Online-only files are already refused per file by the executor. The help topic says a OneDrive Desktop syncs moves to other devices | Existing `TidyRunTests`; help text asserted by `HelpCatalogTests` |
| T14 | The public Desktop (`C:\Users\Public\Desktop`) shows icons DeskAI would never see or move | Disclosed in the help topic: "Icons shared by everyone on this computer are not included." Nothing else needed; DeskAI never connects it | `HelpCatalogTests` |
| T15 | The Desktop button is pressed with no Desktop folder (a locked-down account, a broken profile) | `IKnownFolders.Desktop` is null; the page says "DeskAI could not find your Desktop folder." and changes nothing | `DesktopPageTests` |

## User-facing disclosures

- The wallpaper dialog names the picture, says what Windows shows now, says the slideshow
  caveat (T7), and its confirming button reads **Use as wallpaper**, never "OK".
- The Put back line names the old wallpaper ("a plain colour" when there was none) and says
  when Windows now shows something else (T6).
- The Desktop dialog says what connecting does (names, sizes, dates; nothing inside; nothing
  moved) and that tidying still needs its own permission and a press of Tidy.
- The section's one line: "The only Windows setting DeskAI can change is your wallpaper, and only
  when you press the button."

## Rollback and recovery

- Wallpaper: Put back, from the recorded previous wallpaper written before the change (T5).
  Reopening DeskAI keeps the offer. If the old file is gone, it is said and nothing changes.
- Desktop: the ordinary tidy undo, including after reopening, and the ordinary interrupted-tidy
  question. Disconnecting the Desktop in Organize forgets it as any folder.

## Findings before code

1. **Must build:** the previous wallpaper is written before the change, in its own settings row.
2. **Must build:** the recording setter and the sandbox Desktop in `TestApp`, with an assertion,
   before any page test touches either feature.
3. **Accepted residual risks:** T4 (a same-named replacement picture between preview and use),
   T7 (a slideshow cannot be restored), both disclosed.
4. **No new gate beyond this one:** no content reading, network, background work, or shell
   presence is added.

## Verdict

Safe to build as described above. Re-review is needed for anything that changes a Windows
setting other than the wallpaper picture, anything that creates or edits a shortcut or icon,
any wallpaper source other than the Windows file dialog (a folder scan, a download, a generated
image), or any path to either feature without a window.
