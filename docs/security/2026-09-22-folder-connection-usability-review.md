# Folder connection usability review — 2026-09-22

## Scope

This change helps a first-time user choose an allowed folder and makes Windows redirected-folder
lookup consistent. It does not expand which folders DeskAI can see or change.

## Boundary

- Windows supplies Desktop, Downloads, Documents, and Pictures through `SHGetKnownFolderPath`.
- `PersonalFolderPolicy` still accepts only those paths and their descendants.
- `ReadOnlyFolderService` still normalizes the selected path, blocks unsupported roots and
  reparse-point chains, checks protected locations, and applies `PersonalFolderPolicy` before
  saving an authorization.
- Tidying still performs the same live checks again before permission and execution.

## Threat check

| Threat | Control | Evidence |
|---|---|---|
| Friendlier wording accidentally permits an arbitrary folder | Wording and picker start are UI guidance only; `PersonalFolderPolicy.Refuse` remains the authorization decision | `Anything_else_is_refused_in_plain_words`, `A_picked_folder_outside_the_four_is_refused_on_Search_and_on_Organize` |
| A redirected personal folder is guessed from a user name | Every folder uses its documented Windows known-folder ID; no profile string is assembled | `Known_folders_returns_only_usable_absolute_locations_from_Windows` and source review |
| A drive or program folder becomes reachable from the picker | The picker remains only an input; Core and Safety independently reject the path | `ReadOnlyFolderServiceTests`, `YourFoldersPageTests` |
| A link or junction escapes an allowed folder | Root-chain and per-entry reparse-point checks are unchanged | `ReadOnlyFolderServiceTests`, `WindowsMetadataScannerTests` |
| Tests inspect the owner's personal folders | Page and policy tests use generated paths; the adapter test asks Windows only for path strings and never opens them | Test source review |

## Result

The usability failure is addressed without granting new filesystem reach. The Release build
passes with zero warnings, all 1,428 tests pass with no skips, and
`dotnet format --verify-no-changes` passes.
