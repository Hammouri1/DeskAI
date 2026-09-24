# Desktop Studio Moves Security Review

- Date: 2026-09-24
- Scope: ADR 0044 — move a folder, the separate yes, Clear old stuff, Folder by group
- Result: accepted

| Threat | Control | Test |
|---|---|---|
| An earlier tidy yes (promising "never touches subfolders") is used to move folders | Separate grant; executor checks the grant for the plan's purpose before the run and each action | `FolderTidyExecutorTests.A_folder_move_needs_its_own_yes_tidying_is_not_enough`, `DesktopMoveServiceTests.Moving_needs_its_own_yes_and_that_yes_is_not_tidying` |
| The Studio yes lets Organize tidy | Tidy plans still need the tidy grant | `FolderTidyExecutorTests.A_tidy_plan_on_a_folder_with_only_the_move_yes_moves_nothing` |
| Organize, templates, or Tidy while I'm away move a folder | `OrganizationPlan.CreateDraft` refuses a folder move in a Tidy plan | `OrganizationPlanTests.A_tidy_plan_cannot_move_a_folder` |
| The yes is taken back part-way | Re-checked before every action | `FolderTidyExecutorTests.Taking_back_the_yes_part_way_stops_the_remaining_folder_moves` |
| A folder holding DeskAI's program folder or a protected entry moves | Path policy blocks a path that contains a protected entry; the inventory leaves out any top-level folder holding one | `PlanValidatorTests.A_folder_holding_a_protected_entry_cannot_be_moved`, `DesktopInventoryServiceTests.Hidden_protected_and_linked_things_are_left_out`, `DesktopStudioMovePageTests.DeskAIs_program_folder_and_hidden_things_never_appear_or_move` |
| A folder is moved into itself or out of the Desktop | Validator refuses both | `PlanValidatorTests.A_folder_cannot_be_moved_into_itself`, `PlanValidatorTests.A_folder_move_cannot_leave_the_folder` |
| A folder changed after the preview is moved | Made-at and own last-changed time re-checked | `FolderTidyExecutorTests.A_folder_changed_since_the_list_is_left_where_it_is`, `DesktopStudioMovePageTests.A_folder_changed_after_the_list_stays_where_it_is` |
| Something with the same name is overwritten or merged | Destination checked; `Directory.Move` never overwrites; planner leaves clashes alone | `FolderTidyExecutorTests.A_folder_is_never_moved_onto_something_with_the_same_name`, `DesktopStudioMovePageTests.An_Old_stuff_folder_already_there_is_used_and_kept_and_a_clash_is_left_alone` |
| A folder half-moved, or moved while in use | One rename; Windows' refusal reported | `FolderTidyExecutorTests.A_folder_with_a_file_open_in_another_program_stays_where_it_is` |
| Put back moves a different folder with the same name | Made-at time must match | `FolderTidyExecutorTests.A_folder_replaced_after_the_move_is_not_moved_back` |
| A stop part-way leaves an unknown state | Disk check by made-at time; needs-review never moved | `FolderTidyExecutorTests.An_interrupted_folder_move_is_checked_against_the_disk`, `DesktopStudioMovePageTests.An_interrupted_move_is_asked_about_and_Put_them_back_returns_it` |
| A card undoes another feature's change, or out of order | Latest-only, purpose-matched Put back; Organize ignores Studio runs | `DesktopMoveServiceTests.Put_back_is_offered_only_for_the_latest_change_on_the_Desktop`, `DesktopStudioMovePageTests.Organize_does_not_offer_to_undo_a_Desktop_Studio_change` |
| A project, program, or online-only folder is moved without a thought | Unticked with a warning | `DesktopMovePlannerTests.Warned_folders_start_unticked`, `DesktopStudioMovePageTests.A_project_folder_starts_unticked_with_its_warning` |
| A folder is called old from a partial look | Left alone with the reason | `DesktopMovePlannerTests.Clear_old_stuff_leaves_a_clash_and_an_unfinished_look_alone` |
| AI influences a move | No AI in either card; Folder by group reads the saved board only | `DesktopMoveServiceTests.Folder_by_group_sends_nothing` |
| Something is deleted | Only an empty folder DeskAI made in that run is removed on Put back | `DesktopStudioMovePageTests.An_Old_stuff_folder_already_there_is_used_and_kept_and_a_clash_is_left_alone` |

No new Windows setting, registry, network, or AI capability is added.
