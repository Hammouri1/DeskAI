# ADR 0027: Folder Templates Through the One Executor

- Status: Accepted
- Date: 2026-09-16
- Design: `docs/superpowers/specs/2026-09-14-folder-templates-design.md`
- Review: `docs/security/2026-09-14-folder-templates-review.md`

## Context

V0.7 piece C promised folder templates: a named set of empty folders made inside a connected
folder, so a starter pack's rules have somewhere visible to point and a new course or download
folder can have its shape ready before there are files. Making a folder is a change to the folder,
and `SECURITY.md` lists creating a directory as one of the three executor commands, so this was the
first My workspace feature that needed its own design and security review. The owner answered the
design's questions on 2026-09-16.

## Decision

- **Reuse "Allow tidying."** Making folders needs the same permission Tidy uses on that folder, not
  a second one. Same folder, same kind of trust, one less thing to understand. If tidying is not
  allowed, the same permission dialog Organize shows appears first.
- **One level only.** A template makes folders directly inside the connected folder, never folders
  inside folders. At most 8.
- **Fixed lists, and typed names too.** Five templates, one per starter pack, in code
  (`FolderTemplateCatalog`), with each name tested against the real path policy and each pack
  rule's destination checked to be in its template. The owner also wanted people to type their own
  names. Typed names are the one untrusted input: `FolderNameCheck` refuses anything that is not a
  single plain folder name with a sentence a person can act on, the path policy checks every name
  again before a plan exists, and the executor checks once more.
- **Not part of adding a pack.** Adding a pack still changes nothing on disk (ADR 0026). Templates
  are their own section with their own button.
- **Through the one executor.** A template is an `OrganizationPlan` holding only
  `CreateDirectoryOperation`s, approved as exactly those operations, run by `FolderTidyExecutor`,
  journaled, and undone by it. `FolderTidyExecutor` stays the only code that changes the disk. The
  template service lives in `Core.Templates`, not `Core.Workspace`, so the reflection test that no
  Workspace type holds an executor stays true.
- **What runs is what was shown.** `MakeAsync` looks at the folder again; if a different set of
  folders would now be made, it makes nothing and shows the fresh list. Otherwise it runs the
  previewed plan itself, so the approval and the journal name the operations that were on screen.
- **Undo removes only empty folders DeskAI made.** A folder that was already there is recorded as
  such and never removed; a folder that gained a file is left with a reason. One Undo, under the
  cards, for the chosen folder's last template run, found in the journal so it survives reopening.
- **A folder-only record settles by folders.** The review's must-fix: a journal record with no
  moves used to settle as Failed after an interruption and read "0 of 0 files" on Organize. It now
  settles by the folders it made, Organize describes it as folders, and it can be undone.

## Alternatives

- A separate "Allow making folders" permission: offered; the owner chose reuse.
- Nested folders: offered; the owner chose one level.
- Fixed lists only: offered as the safer default; the owner chose typed names as well, which
  triggered the review's re-review for typed names (T18–T21).
- Offering a pack's template after adding the pack: offered; the owner chose to keep them apart.
- Undo on each card: the design's first sketch. Replaced by one Undo for the folder's last run,
  because after reopening the journal does not know which card made it, and one place is simpler.

## Consequences

Two pages now call `FolderTidyExecutor`: Organize and My workspace. The page's title sentence
changed from "Nothing here moves or changes files" to "Nothing here moves a file", which is the
promise that is still true. `IFolderNameLookup` is a new read-only contract limited to names and
kinds at one level. Re-review is needed if templates ever gain nested folders, any operation other
than creating a folder, or a path to run without a window.
