# Find Groups (Desktop Studio step 1) Security Review

- Date: 2026-09-24
- Scope: ADR 0042 — a read-only look at a connected Desktop, one AI grouping request, and a
  stored group board
- Result: accepted; no file, Windows setting, or executor is reachable from this step

| Threat | Control | Test |
|---|---|---|
| Prompt injection via names | Names are sent only inside `BEGIN_UNTRUSTED_ITEM_DATA` markers with an instruction never to follow them; the reply must match the exact shape or is refused whole | `DesktopGroupReadingTests.Refuses_a_reply_outside_the_exact_shape`, `GroupItemsProviderTests.Cloud_sends_the_items_between_markers_and_returns_the_answer_text_unread` |
| AI names a path or command as a group | `FolderNameCheck.Check` on every group name; the whole reply is refused | `DesktopGroupReadingTests.Refuses_a_reply_outside_the_exact_shape` (slash case) |
| AI invents item numbers or repeats one | Numbers must be 1..N and appear once across all groups | `DesktopGroupReadingTests.Refuses_a_reply_outside_the_exact_shape` (unknown and repeated cases) |
| More than 8 groups or an oversized reply | Group count and byte length checked before anything is kept | `Refuses_more_than_eight_groups`, `Refuses_a_reply_larger_than_the_limit` |
| Sending more than the person saw | `SendAsync` sends the prepared request object unchanged; refuses if the AI choice changed | `DesktopStudioPageTests.Send_sends_exactly_the_lines_shown_and_the_answer_becomes_the_board`, `DesktopGroupingServiceTests.Send_refuses_when_the_AI_choice_changed_after_prepare` |
| Sharing choices bypassed | Checked when preparing, again at Send, and again in `ConfiguredSuggestionProvider.GroupItemsAsync` | `DesktopStudioPageTests.Online_AI_without_folder_name_sharing_sends_nothing_and_says_what_to_allow`, `ConfiguredSuggestionProviderTests.GroupItems_refuses_online_AI_when_folder_names_are_not_shared` |
| Locations leak | Only names, endings, and counts are sent; no full path or DeskAI ID | `DesktopStudioPageTests.Send_sends_exactly_the_lines_shown_and_the_answer_becomes_the_board`, `GroupItemsProviderTests` (request ID absent) |
| Hidden, system, link, or protected items | Scanner path policy, plus exclusion of any top-level folder with a protected or link entry under it | `DesktopLookServiceTests.Leaves_out_hidden_system_and_any_folder_holding_a_protected_or_link_entry`, `DesktopStudioPageTests` (DeskAI's own folder never appears) |
| AI gains file or Windows access | `GroupItemsAsync` returns text only; the grouping service holds no executor, journal, writer, or setting changer | `DesktopGroupingServiceTests.Holds_no_file_changing_dependency`, page tests assert the generated Desktop is unchanged |
| A stale board acts on things that are gone | Items no longer on the Desktop are dropped when the board is shown | `DesktopGroupingServiceTests.Loading_drops_items_that_are_no_longer_on_the_Desktop`, `DesktopStudioPageTests.A_folder_deleted_since_is_gone_from_the_board_next_time` |
| Board outlives the folder | JSON row cascades with `authorized_roots`; Start fresh erases it; stored JSON is size-limited and a damaged row loads as nothing | `SqliteDatabaseInitializerTests`, `SqliteDesktopGroupRepositoryTests`, `DesktopStudioPageTests.Disconnecting_the_Desktop_forgets_the_board` |

The look opens no file: it reads names and attributes from the directory listing through the
existing scanner, within the connected Desktop only. The daily AI limit, timeout, no-retry, and
key handling are the existing ones. Nothing in this step can move, rename, delete, or change
a file, an icon position, or the wallpaper; later Desktop Studio cards need their own reviews.

**Branch review follow-up (2026-09-24).** Hidden and system files and folders are skipped at every
depth, not only on the Desktop itself (`DesktopLookServiceTests.Hidden_and_system_files_inside_folders_are_neither_counted_nor_named`).
A look that stops at its item limit inside a big folder keeps every top-level item and says the
look was partial, instead of refusing with a safety message that was not true. Prepare keeps the
list within the request's byte limit, measured the way the AI connection writes it, so Send never
refuses a list the person approved; the rest get DeskAI's own guess and are never sent. A name
may say anything, including a data marker such as `END_UNTRUSTED_ITEM_DATA`: it can only become a
group label, because the reply is read strictly and nothing reads the board to act on disk.
