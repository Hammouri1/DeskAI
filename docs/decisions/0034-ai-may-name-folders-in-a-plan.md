# ADR 0034: AI May Name Folders in a Plan, Checked Four Times

- Status: Accepted
- Date: 2026-09-16
- Review: `docs/security/2026-09-16-plan-folder-review.md`

## Context

ADR 0020 let AI name a category for files DeskAI could not place, with DeskAI's recipe choosing
the folder, so the AI never named a folder. The owner's second AI feature for V1.1 is "Plan this
folder": AI proposes a layout for the whole folder — a few folders with names a person would
choose, and which file goes where. That needs AI to name folders, which ADR 0020 deliberately
did not allow.

## Decision

- **A plan is the same request as Ask AI** — the same files (every file the person's rules do
  not place), the same sharing rules, the same dialog with one more line — marked
  `AiSuggestionTask.PlanFolder`. Nothing new leaves the computer.
- **A planned folder name is untrusted text that becomes part of a path, so it is checked four
  times.** The parser requires one plain name per file and refuses the whole plan if any name
  fails `FolderNameCheck` (the rule a typed template name passes) or more than 12 distinct
  folders are named; `TidyAiService` checks every name again before it becomes advice; the
  plan validator checks the resulting relative path; the executor checks it again before each
  move. A name is always one segment inside the folder being tidied.
- **Everything after the name is unchanged.** The name takes the place of the recipe's folder
  in the ordinary planner; the preview, ticking, "AI isn't sure", rules winning, Tidy, journal,
  and undo are exactly as for any other suggestion. The AI's reason is still never shown.
- **On the page**, a plain "Plan this folder with AI" button under Ask AI, with its own help
  topic; pressing it switches the page to "Every file my rules don't place" first.

## Consequences

- ADR 0020's "AI never names a folder" now reads "AI never names a folder for Ask AI; for a
  plan it may, under the checks above". `docs/SECURITY.md` and `docs/AI-PROVIDERS.md` say so.
- A refused plan is refused whole, never trimmed, so a person never sees half of what the AI
  meant with the dangerous half silently dropped.
