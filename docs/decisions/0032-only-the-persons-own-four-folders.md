# ADR 0032: DeskAI Connects Only a Person's Own Four Folders

- Status: Accepted
- Date: 2026-09-16

## Context

On the owner's first look at V1.0 three things came up at once. "Tidy my Desktop" said the
Desktop was protected (DeskAI's own program folder is on the protected list, and this copy runs
from a folder on the Desktop, so the Desktop "overlapped" it). There was no way to connect
Downloads, Documents, or Pictures from Home or My workspace; only the Windows picker on Search
and Organize, which nothing pointed at. And the owner did not want DeskAI able to touch "the C:
workspace or the main important data and system folders" at all, however it was asked.

## Decision

- **The only places DeskAI can be given are a person's own Desktop, Downloads, Documents, and
  Pictures, and folders inside them.** `PersonalFolderPolicy` in Core holds the rule; the four
  come from `IKnownFolders`, asked from Windows through the known-folder API (Downloads through
  `SHGetKnownFolderPath` by its documented ID, because .NET has no special-folder value for it).
  `ReadOnlyFolderService` refuses anything else at connection with "DeskAI only works inside your
  Desktop, Downloads, Documents, and Pictures." and refuses it again in `CheckStillSafeAsync`, so
  a folder connected before the rule, or a personal folder Windows has moved since, cannot be
  allowed for tidying or tidied. Containment is by path segment and case-insensitive.
- **A folder that contains a protected place may be connected; a folder inside one may not.**
  The root check in `WindowsPathPolicy` returns a warning for the first case and blocks the
  second. The protected part is refused entry by entry through the relative-path check that the
  scanner, the planner, and the executor all consult, so it is never listed, remembered, sent to
  AI, or used as a destination. Every test Desktop now contains a protected "program folder", as
  the owner's does.
- **A "Your folders" card on Home and My workspace replaces "Tidy my Desktop".** One row per
  folder Windows reports, its state, and one plain button: Connect (after "Connect your
  Downloads?") or Tidy, both opening Organize on the folder. It connects through the same service
  the picker uses and grants nothing beyond that. The Windows picker stays on Search and Organize
  for folders inside the four.
- **The side menu's "Nothing connected yet" card** now says where to connect.

## Consequences

- A person cannot connect a whole drive, a program folder, a network share, or another account's
  folder, by any route. A folder connected under the old rule stays listed on Search but cannot be
  tidied; disconnecting it is the way forward.
- In page tests the sandbox's folders root stands in for Documents, so every generated folder
  counts as inside a personal folder; Desktop, Downloads, and Pictures are generated inside it.
- `docs/SECURITY.md`, `docs/PRODUCT.md`, `docs/UI-UX.md`, `docs/USER-GUIDE.md`, `docs/TESTING.md`,
  and `docs/MANUAL-TESTING.md` were updated in the same change. ADR 0029's "Tidy my Desktop adds
  no reach" argument carries over to the card unchanged.
