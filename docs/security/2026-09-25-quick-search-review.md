# Quick Search Security Review

- Date: 2026-09-25
- Scope: ADR 0047 (quick search opens files, reads inside files from a pop-up, and keeps DeskAI
  near the clock)
- Result: accepted with the checks below
- Plan: `docs/superpowers/plans/2026-09-25-quick-search.md` (each test is added by the task named)

## What changes

- DeskAI's first "open a file" action: the Windows shell opens one validated file, or File
  Explorer shows it.
- A system-wide shortcut, Ctrl + Alt + Space.
- A pop-up that shows file names and short pieces of text from inside files over other apps.
- Closing the window no longer always quits: DeskAI stays near the clock while quick search is on.

## Assets

The person's files in connected folders. The permission boundary (only connected folders, and
reading inside only where it was allowed). What they type. The promise that nothing runs unseen.

## Threats and answers

| Threat | Control | Test |
|---|---|---|
| A program disguised as a document ("report.pdf.exe", "report.pdf.") | Allow-list on the last extension; a trailing dot or space is not familiar | `FileOpenRuleTests` (Task 2), `WindowsFileLauncherTests.A_program_is_never_opened` (Task 4) |
| The file changed after DeskAI remembered it (renamed to .exe, deleted) | Live checks just before opening | `WindowsFileLauncherTests` (Task 4), `QuickSearchPageTests.A_file_that_went_away_keeps_the_bar_open_with_the_reason` (Task 5) |
| Escape from the connected folder through `..`, a full path, or `:` | Normalised containment check; rooted paths and `:` refused | `WindowsFileLauncherTests.A_path_leading_out_of_the_folder_is_refused` (Task 4) |
| Escape through a link or junction | Every folder and the file on the way is checked for reparse points | `WindowsFileLauncherTests.A_link_on_the_way_is_never_followed` (Task 4) |
| A protected location | `IPathPolicy` on the folder and the path | `WindowsFileLauncherTests.A_protected_place_is_refused` (Task 4) |
| A folder disconnected while the bar showed its file | The launcher looks the folder up by ID when opening | `WindowsFileLauncherTests.A_folder_that_was_disconnected_is_refused` (Task 4) |
| Time between the last check and the open | Accepted (ADR 0047 Consequences): own file, own folder, own press, familiar type | — |
| Starting anything else (a shell, a script, a program from PATH) | Only `IShellStarter`'s two calls; Explorer by full path; the shared registration starts nothing | `WindowsFileLauncherTests` records every start (Task 4) |
| Tests or a mis-composed DeskAI starting real processes | `NoShellStarter` by default; `TestApp` records | Presentation page tests (Task 4 onward) |
| Many file reads while typing | 600 ms pause, one look at a time, cancelled by the next keystroke or by hiding | `QuickSearchPageTests.Typing_again_stops_a_slow_inside_look_...`, `Hiding_the_bar_stops_an_inside_look` (Task 5) |
| Reading a folder that was never allowed | Only `ContentSearchService`, with its own permission checks; withdrawn permission applies at once | `QuickSearchServiceTests.Words_inside_are_found_only_where_reading_inside_is_allowed_and_never_twice` (Task 3), `QuickSearchPageTests.Without_reading_inside_...` (Task 5) |
| The bar granting reading, or reaching the scanned-PDF reader | It holds no permission setter; it calls only `SearchAsync` | `QuickSearchContainmentTests` (Task 5), `QuickSearchServiceTests.It_holds_nothing_that_can_open_change_or_send` (Task 3) |
| Words from inside a file seen over another app | Only after the person's shortcut; short pieces; gone on hide | Behaviour of Task 5; accepted in ADR 0047 |
| A snippet shown as markup or a link | Plain `TextBlock` text only | `QuickSearchLayoutTests.Snippets_are_plain_text` (Task 8) |
| AI reaching the launcher | Only `QuickSearchViewModel` takes `IFileLauncher`; no AI in quick search | `QuickSearchContainmentTests.Only_the_quick_search_bar_holds_the_launcher` (Task 5) |
| The shortcut as a key logger | `RegisterHotKey` for one combination; no hook | `QuickSearchLayoutTests.The_shortcut_is_Ctrl_Alt_Space_without_repeat_and_without_a_keyboard_hook` (Task 8) |
| DeskAI invisible with no way to quit | Stays only while the icon shows; Quit on the icon's menu | `QuickSearchSettingsPageTests.When_the_icon_cannot_show_closing_really_quits` (Task 6) |
| Typed words, results, or snippets kept or logged | Nothing stored; no logging of phrase, names, or snippets | `QuickSearchPageTests.Nothing_typed_or_found_is_remembered` (Task 5) |

## Residual risks

- A file swapped in the moment between the last check and Windows opening it (accepted, above).
- Windows' own choice of app for an allow-listed type (the person's setting).
- Another program already holding Ctrl + Alt + Space: quick search says so and does not fall back
  to another key.

No new AI capability, network access, Windows setting change, or file change.
