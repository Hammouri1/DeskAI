# Security Review: Folder Templates (V0.7 piece C) — Draft

- Date: 2026-09-14
- Scope: `docs/superpowers/specs/2026-09-14-folder-templates-design.md`
- Gate: `SECURITY.md` "Security Review Gates", which covers introducing file mutation. Templates
  do not add a new *kind* of change (the executor already creates folders during Tidy), but they
  are the first feature whose whole purpose is to change a folder without moving a file. They
  also reach the executor from a new page, so the gate is applied in full.
- Status: **Accepted 2026-09-16**, after the owner's decisions (reuse the tidy permission; one
  level; fixed lists **and typed names**; not linked to packs). Typed names triggered the
  re-review this document said would be needed; it is the section "Re-review: typed names"
  below. Every control marked proposed is now built and has the test named. ADR 0027.

## What changes in DeskAI's reach

| Before | After |
|---|---|
| Folders are created only as part of an approved Tidy, and only folders a ticked file goes into | Folders can also be created from a template, approved as exactly those folder names |
| One page (Organize) calls `FolderTidyExecutor` | Two pages call it: Organize and My workspace |
| Every journal record moves at least one file | A journal record may hold only created folders |

What does **not** change: the operations allowed (no new command), the executor, the path
policy, the permission model (the tidy permission is reused, if the owner agrees), the AI
boundary (AI plays no part), and what can be reached with no window (nothing).

## Threat scenarios

| # | Threat | Control (proposed) | Negative test (proposed) |
|---|---|---|---|
| T1 | A template makes folders in a folder the person did not allow to be changed | Preview and make both require `RootCapabilities.CanTidy`; the executor checks it again before the run and before each folder | Service: no tidy permission means the executor is never called. Infrastructure: permission withdrawn between two folders means the rest are refused |
| T2 | A catalog name escapes the folder (`..\x`, `C:\x`, `\\server\x`) or is a Windows reserved name (`CON`, `NUL`, a trailing dot or space) | Names are single segments in a compiled catalog; the catalog test runs every name through `WindowsPathPolicy`; `PlanValidator` checks each operation again; the runner's `Resolve` checks again | Catalog test for every name; a service test with a bad catalog entry proves it is blocked, not created |
| T3 | A link or junction inside the connected folder redirects the new folder elsewhere | `FileOperationRunner.CreateDirectory` refuses a link anywhere on the parent path, and refuses an existing destination that is a link | Infrastructure: a junction named like a template folder is refused and nothing is created through it |
| T4 | The connected folder itself is swapped for a link, or moved, while the dialog is open | `FolderTrust.VerifyAsync` compares the canonical path and runs `CheckStillSafeAsync` before each folder | Infrastructure: root replaced mid-run means the rest are refused |
| T5 | Time of check and time of use: a file with the folder's name appears after the preview | `MakeAsync` rebuilds from fresh state; the runner refuses "A file is where this folder would go." at the moment of creation | Service: the name clash appears between preview and make and is refused and re-shown. Infrastructure: a file created just before the run makes that folder fail on its own |
| T6 | Undo deletes a folder that was already there before the template ran | The runner records an existing folder as `AlreadyPresent`, and undo reverses only `Completed` operations | Infrastructure: a pre-existing empty folder survives undo |
| T7 | Undo deletes something the person has put into a new folder since | Undo deletes only an empty folder (`recursive: false`, after checking it has no entries) and otherwise leaves it with a reason | Infrastructure: a new folder that gained a file stays and is named in the result |
| T8 | Undo deletes a *different* empty folder: the person removed DeskAI's folder and made their own with the same name | **Residual risk.** The runner cannot tell two empty folders with the same name apart. The harm is removing one empty folder, which contains no data, and the undo result names it. Accepted, as it already is for Tidy | Documented here; a test pins the behaviour so it stays deliberate |
| T9 | A template run hides an interrupted tidy question, or is mistaken for one | The executor refuses a new run while the folder has an unanswered record; hazard 2 in the design gives folder-only records their own wording and settling | Page tests on Organize and My workspace for an interrupted template run; Infrastructure: settling counts created folders |
| T10 | A folder-only record closes as `Failed` after an interruption, leaving made folders impossible to undo | Hazard 2 fix: settle by created folders when a record has no moves | Infrastructure test (fails before the fix) |
| T11 | Mass creation: a bug makes thousands of folders | Catalog cap of 8 folders per template, checked by a test; the service refuses a plan with more create operations than the template has names | Catalog and service tests |
| T12 | The folder syncs to the cloud (OneDrive, Dropbox): new folders appear on other devices | Not preventable, and not harmful (empty folders, no contents). **Proposed disclosure:** none on the main surface; the "?" topic notes that folders in a synced folder appear on your other devices too | Help text test (word limits already enforced) |
| T13 | Adding a starter pack quietly makes folders | Templates are a separate button and service; `StarterPackService` keeps its no-executor reflection test | Existing reflection tests keep passing; a new one covers all Workspace types |
| T14 | AI influences which folders are made | No AI, prompt, or model type is referenced by template code; names come only from the compiled catalog | Reflection test on `FolderTemplateService` |
| T15 | Something reachable without a window makes folders (tray, notification, automatic check) | Only the My workspace page calls the template service; the tray and notices gain nothing; automatic checks still hold no executor | Existing tray and check reflection tests; a page test that the tray menu is unchanged |
| T16 | A protected location (user-protected entry, system area) is inside the connected folder | `WindowsPathPolicy` overlap checks on each operation; blocked names shown as "Can't be made" | Service test with a protected entry equal to a template folder's path |
| T17 | Two DeskAI windows run a template and a tidy at once | Shared run lock (`RunLockFile`) and the in-process semaphore; the second waits and then refuses with the busy message | Existing executor lock tests cover it; one template-specific test that the busy message reaches the card |

## User-facing disclosures

- The dialog lists every folder by name before anything happens, and lists what will not be
  made and why.
- The promise line says only empty folders are made; nothing moved, renamed, or deleted; undo
  is available. Each part is true: the plan contains only create operations (T13, T14), and
  undo removes only empty folders DeskAI made (T6, T7).
- The confirming button states the count: "Make 3 folders". It never says just "Continue".
- The result line never says "Done" when anything was not made.

## Rollback and recovery

- **Normal undo:** remove each folder this run made, newest first, only if still empty.
- **After reopening:** the template card offers Undo for the folder's latest folder-only run,
  if it has not been undone and nothing ran in that folder since (design hazard 1).
- **Crash mid-run:** the existing interrupted-run check reads the disk. An existing folder
  counts as "already there" and is never removed, because nothing proves DeskAI made it. With
  the hazard 2 fix, the person is told how many folders were made and chooses keep or undo, the
  same as a tidy.
- **Disconnecting the folder** erases its history as it does today, so its template run can no
  longer be undone. The folders stay, empty and harmless. This is the same trade-off as Tidy.

## Findings before code

1. **Must fix before shipping:** records with no moves settle as `Failed` and read as "0 of 0
   files" on Organize (T9, T10). This is a change to recovery code and gets its own commit and
   tests.
2. **Must decide:** reuse the tidy permission or add a separate one (design question 1). The
   review finds reuse acceptable, because the scope (that folder), the operation surface
   (already includes making folders), and the re-checks are identical.
3. **Accepted residual risk:** T8 (a same-named empty replacement folder can be removed by undo).
4. **No new gate triggered:** no content reading, network, background work, shell presence, or
   Windows setting change.

## Re-review: typed names (2026-09-16)

The owner chose to let people type their own folder names. That adds one untrusted input to a
feature that otherwise had none. It does not add a new operation, a new permission, or a new
path to the disk: a typed name still becomes a `CreateDirectoryOperation` in a plan the policy
and the executor check. What changes is that the *name* is no longer from a compiled list.

| # | Threat | Control (built) | Negative test |
|---|---|---|---|
| T18 | A typed name escapes the folder: `..\x`, `C:\x`, `\\server\x`, `Docs/2026`, or a name containing a separator | `FolderNameCheck.Check` refuses any separator, colon, or dot-only name before anything is looked at; the policy blocks traversal and rooted paths again when the plan is built; `FileOperationRunner.Resolve` and `EnsureContained` refuse a third time | `FolderNameCheckTests` (each form), `FolderTemplatePolicyTests` (name check and policy agree), `FolderTemplatePageTests` (`..\Up` refused on the card, nothing on disk) |
| T19 | A typed name is a Windows device name (`CON`, `NUL`, `COM1.txt`), ends with a dot or space, or holds a character Windows refuses (`* ? " < > \|`) or a control character | Refused by the name check with the reason; also refused by the policy's `IsUnsupported` | `FolderNameCheckTests`, `FolderTemplatePolicyTests`, `FolderTemplatePageTests` (`CON`) |
| T20 | Mass creation through typing: hundreds of names, or the same name many times | At most 8 names per template (`FolderTemplateCatalog.MaxFolders`), duplicates refused ignoring capitals, each name at most 64 characters; `IFolderNameLookup` refuses more than 8 names too | `FolderNameCheckTests`, `FolderNameLookupTests` |
| T21 | A typed name collides with a protected entry, an existing file, or a link inside the folder | Same controls as catalog names: the policy's overlap check blocks it before a plan exists; the lookup reports a file or link and the preview says "Can't be made"; the runner refuses at creation time if it appeared since | `FolderTemplateServiceTests` (policy-blocked name left out of the plan), `FolderTemplatePageTests` (a file with the name) |
| T22 | What was typed is used somewhere other than a folder name — in a prompt, a log, a query | The typed text reaches only `FolderNameCheck.Parse`, then the plan, then the journal's destination path. No AI, network, or logging type is reachable from the service (reflection test). It is shown back to the person in the reason text, unescaped, which is safe in a WinUI `TextBlock` | `FolderTemplateServiceTests` reflection test |

**Residual risk:** none new. A person can, by typing, make an empty folder with an odd but legal
name inside a folder they allowed DeskAI to tidy. That is what they asked for, they saw the name
in the preview, and undo removes it while it is empty.

## Verdict

Built **as designed and agreed**, with finding 1 fixed first (`b81e803`) and the typed-name
re-review above. Re-review is needed if templates gain nested folders, any operation other than
creating a folder, or a path to run without a window.
