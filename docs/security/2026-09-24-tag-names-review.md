# Tag Names Security Review

- Date: 2026-09-24
- Scope: ADR 0045
- Result: accepted

| Threat | Control | Test |
|---|---|---|
| A rename overwrites or merges with something | New names already used on the Desktop (visible or left out) are left alone; the executor never overwrites | `DesktopMovePlannerTests.Tag_names_never_takes_a_name_already_used` |
| A name Windows or DeskAI would refuse | `FolderNameCheck` on the new name | `DesktopMovePlannerTests.Tag_names_checks_the_new_name` |
| Pressing it twice stacks prefixes | Already-prefixed folders are left alone | `DesktopStudioMovePageTests.A_folder_already_named_with_its_group_is_left_alone` |
| A project or program breaks after a rename | Unticked with a warning | `DesktopMovePlannerTests.Tag_names_warns_before_renaming_a_project` |
| Files renamed | Only folders are listed | `DesktopStudioMovePageTests.Tag_names_shows_the_new_name_for_each_folder_and_never_renames_files` |
| Renaming without the person's yes | Same separate yes, checked by purpose | `DesktopMoveServiceTests.Tag_names_needs_the_same_yes_and_Put_back_restores_names` |
| Put back fails silently | Existing Put back checks, named reasons | `DesktopStudioMovePageTests.Rename_then_Put_back_restores_the_old_names` |
| Protected or link folders renamed | Inventory leaves them out; path policy re-checked on both names | ADR 0044 tests |

No new executor action, Windows setting, network, or AI capability.
