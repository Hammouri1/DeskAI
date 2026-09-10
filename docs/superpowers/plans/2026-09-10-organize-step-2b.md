# Organize Step 2b — AI as a Suggestion Source on Your Own Folders

**Goal:** On the "Tidy a folder" page, AI can suggest where a file goes: by default only for
files DeskAI cannot place, or, if the person chooses, for every file their rules do not place.
Nothing is sent until the person has seen exactly what the AI will see and pressed Send.
Nothing moves; the Tidy button stays off until step 3.

**Spec:** `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md` (Part 1
"Suggestions from", Part 2 §6). **Security review:**
`docs/security/2026-09-10-real-folder-ai-disclosure-review.md`, written before the code.

## One change to the agreed design, and why

The design says that in "DeskAI + my rules" mode AI "is asked" about files DeskAI cannot place
"if AI is set up", which reads as asking by itself when the list loads. The V0.3 AI review
left a gate for exactly this moment: sending anything about a real folder needs "UI preview of
the exact real request". A request that goes out when a page opens has no moment for that
preview, and it would also spend the person's daily limit and money without a press. So:

- The "Suggestions from" switch decides **which** files may be asked about.
- A button, "Ask AI about N files", opens a dialog listing exactly what the AI will see about
  each file and where it goes (the service and its web address, or "this computer").
- Only **Send** sends. `docs/SECURITY.md` wins where documents conflict, and this is the
  reading that satisfies both.

## Decisions

- **Random stand-in numbers, not DeskAI's file IDs.** A scanned file's ID is a fingerprint of
  the folder and the file's path, and it is the same every time. Sending it would give an AI
  service a stable label for each file across requests. Each request gets fresh random
  numbers, mapped back locally.
- **Only file type, size and date, and name can be sent from your own folders**, and only
  those the person switched on in Privacy and AI. Full locations are never sent from a real
  folder even when that switch is on, because they reveal the Windows user name and folder
  layout and add nothing to working out what a loose file is. Folder names are meaningless
  for loose top-level files and are never sent either.
- **Only what can still move is asked about.** Left-alone files (downloading, recent, online
  only, hidden, system) are never sent. Files a rule already places are never sent, even in
  "every file" mode, because rules win over AI and sending them would disclose for nothing.
- **Protected check at the moment of asking.** Each file is checked against the safety policy
  again (`IPlanSafetyCheck.IsProtected`) and excluded if protected, counted but not named.
- **AI picks a category, never a folder.** The category maps to a folder through
  `TidyFolderRecipe`. An AI answer that says "Unknown" leaves the file alone.
- **AI's own words are not shown.** The row says "AI idea from OpenRouter", not the AI's
  reason text, so a file name crafted to make the AI say something alarming cannot put words on
  DeskAI's screen.
- **Unsure ideas** (confidence below `TidyAiService.UnsureBelow`, 0.7) go into an "AI isn't
  sure" group and start unticked. No percentage is shown.
- **Re-checked before sending.** Between the preview and Send: the folder must still allow
  tidying, and the AI choice, destination, and sharing choices must be unchanged. Otherwise
  nothing is sent and the person is asked to look again.
- **Advice about a changed file is dropped.** Each answer is remembered with the file's size
  and last-changed time; if either differs later, the idea no longer applies.
- **At most `TidyAiService.MaxFilesPerRequest` (100) files per request.** One press is one
  request, counted against the daily limit.

## Tasks (one commit each)

1. **Plan and review design** — this file and the security review. (docs)
2. **Engine** — `TidyAiService` (status, prepare, ask), `TidyAiAdvice`, `TidySuggestionMode`,
   `TidySuggestionSource.Ai`, `TidySuggestion.IsUnsure`, `TidyPreview.AskableFiles`,
   `IPlanSafetyCheck.IsProtected`. `TidySuggestionService.PreviewAsync` takes the mode and the
   advice; precedence is rule > AI (every-file mode) > type > AI (default mode) > left alone.
   Tests: `TidyAiTests` (through `TestApp`, recording internet), a Safety test for
   `IsProtected`, and a containment test that `TidyAiService` holds nothing that can scan,
   read, or move a file.
3. **Page** — "Suggestions from" switch, "Ask AI" button with its sharing note and result line
   beside it, preview dialog, Stop, "AI isn't sure" group, help topic `organize.askAi`, updated
   `organize.suggestions` and `settings.dailyLimit`. Tests: `TidyAiPageTests`.
4. **Documents** — ADR 0020, the review's result, `SECURITY.md`, `AI-PROVIDERS.md`,
   `ARCHITECTURE.md`, `UI-UX.md`, `TESTING.md` coverage map, `MANUAL-TESTING.md`, `ROADMAP.md`,
   and the spec amendment.

## Threat tests (each must fail if its control is removed)

| Threat | Test |
|---|---|
| AI off, or not finished setting up | Nothing sent; "every file" is disabled and says how to turn AI on |
| Something sent without the person seeing it | Preparing sends nothing; only Send does |
| Names sent without agreement | Default sharing: no file name in the request |
| Full location or user name sent | Full-location switch on: the folder path is still absent |
| DeskAI's stable file IDs sent | No scanned file ID appears in the request |
| Left-alone files sent | Hidden, online-only, and downloading files absent even in every-file mode |
| Rule-placed or type-known files sent by default | Absent from the request |
| AI answer with a path or extra field | Whole answer refused; type and rule suggestions unchanged |
| AI answer naming a file not asked about | Whole answer refused |
| AI text trying to choose a folder | Destination still comes from DeskAI's folders; AI text not shown |
| Settings changed after the preview | Nothing sent |
| Tidy permission withdrawn after the preview | Nothing sent |
| File changed after the answer | Idea dropped |
| Daily limit reached | Nothing sent |
| Rules vs AI in every-file mode | Rule wins and the rule's file is not sent |
| Asking AI moves a file | Files on disk unchanged after every test |
