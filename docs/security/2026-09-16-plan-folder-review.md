# Security Review: Plan This Folder (V1.1, ADR 0034)

- Date: 2026-09-16
- Scope: "Plan this folder with AI" on Organize; `AiSuggestionTask.PlanFolder`, the plan
  prompt, `StructuredSuggestionParser` folder handling, `TidyAiAdvice.FolderName`,
  `TidySuggestionService`.
- Gate: cloud transmission and file mutation (`docs/SECURITY.md`). Written with the code.

## What changes

For the first time an AI answer can name a folder that DeskAI will make. Nothing new is sent:
the request is the Ask AI request about every file the person's rules do not place. What
changes is that the answer's `folder` text, after checks, replaces the recipe's folder name.

## Threat cases and controls

| # | Threat | Control | Test |
|---|---|---|---|
| 1 | A folder name that is a path: `..\Windows`, `C:\Users`, `Invoices/2026` | `FolderNameCheck` refuses separators, drive letters, wildcards, and traversal; the parser refuses the whole plan; `TidyAiService` checks again; the path policy blocks an escaping relative path; the executor re-checks | `PlanFolderParserTests` (theory), `TidyAiPageTests.A_plan_that_names_a_folder_outside…` |
| 2 | A name Windows cannot make or that hides something: `CON`, a trailing dot, a control character, only dots, leading space, over 64 | `FolderNameCheck` refuses each; whole plan refused | `PlanFolderParserTests` |
| 3 | A plan with a great many folders, to scatter files | At most 12 distinct names (case-insensitive); the thirteenth refuses the plan | `PlanFolderParserTests.A_plan_with_more_folders…` |
| 4 | A folder name on an ordinary "Ask AI" answer | Refused (`UnexpectedFolder`) so the classification path cannot be steered into naming folders | `PlanFolderParserTests.A_classification_answer_that_names_a_folder…` |
| 5 | A plan that overrides the person's own rules | Rules are placed first in `TidySuggestionService`; AI advice applies only to files rules do not place | `TidyAiPageTests.A_rule_still_wins…` |
| 6 | Moving a file somewhere the person did not see | The plan is a preview, grouped by the AI's folders, ticked per file, moved only by Tidy through the one executor with every per-file re-check, journaled, and undoable | `TidyAiPageTests.Plan_this_folder…` (tidy and undo) |
| 7 | Sending more than Ask AI would | Same request builder, same `RealFolderShareable` limit, same dialog lines plus one; the test asserts the body | `TidyAiPageTests.Plan_this_folder…`, existing `TidyAiTests` disclosure tests |
| 8 | A refused plan half-applied | The parser and the service return nothing on any failure; the list stays as it was | `TidyAiPageTests.A_plan_that_names_a_folder_outside…` |
| 9 | The AI's own words on screen | The reason is still never displayed; groups carry the checked folder name and "AI idea from <service>" | `TidyAiPageTests` |

## User-facing disclosure

The card line says AI names folders and DeskAI checks every name; the dialog adds "It may also
suggest folder names. DeskAI checks every name and only ever makes folders inside this one;
you see the whole plan before anything moves." Help topic `organize.planAi` says the same.

## Result

Accepted. AI gains the power to propose a name for a folder inside the one being tidied, and
nothing else; a proposal still becomes a file move only through the unchanged preview,
approval, executor, and journal.

## Still not accepted

Nested folder names; folders outside the one being tidied; a plan applied without the preview;
any AI-written text shown as a reason.
