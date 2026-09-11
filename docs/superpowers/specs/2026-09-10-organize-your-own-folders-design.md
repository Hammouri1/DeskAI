# Organize Your Own Folders, and "?" Help — Design

- Status: Proposed, awaiting owner review
- Date: 2026-09-10
- Milestone: V0.6 "Organize Your Own Folders" (this document is its step 1: design and
  security review before code)
- Decided with the owner in conversation on 2026-09-10

## Why

The owner found the Organize page confusing and purposeless: it stacks a practice run on
made-up files, AI ideas about those same made-up files, a history box, and a read-only
listing of a real folder that can do nothing. None of it does what a page called Organize
promises — tidy *your* folder — because no connected folder can be changed yet. This design
rebuilds Organize around that one job and adds the capability behind it, and adds a short
"?" explanation next to every feature in the app so ordinary people can tell what each thing
is for.

## Goals

1. Organize does one thing: pick a folder, see what DeskAI would tidy, choose, press Tidy,
   undo if wanted — on the person's real folder.
2. Nothing moves until the person presses Tidy on a list they have seen; everything moved
   can be undone, including after DeskAI is closed and reopened.
3. Every feature in the app has a "?" that explains it in plain words.
4. Every step a person can take is covered by a page test; every refusal is covered by a
   test that tries to break it.

## Non-goals (not in this milestone)

- Moving files outside the chosen folder (for example into the Windows Documents folder).
- Tidying files already inside subfolders.
- Renaming destination folders, or tidying several folders at once.
- Deleting or sending anything to the Recycle Bin.
- Letting automatic checks move anything. They keep only looking.
- Sending real file names to AI unless the person already allowed file names in Privacy and AI.

---

## Part 1 — What the person sees

### The Organize page

```
Tidy a folder                                          (?)
Pick a folder. DeskAI suggests where things go. Nothing moves until you say so.

📁 Downloads                                        [Change]
Suggestions from:  (•) DeskAI + my rules              (?)
                   ( ) Ask AI about every file

101 loose files could be tidied
☑ Documents    38 files  ▸
☑ Pictures     51 files  ▸
☑ Installers   12 files  ▸
   ⚠ report.pdf — a file with this name is already in Documents
      (•) Skip   ( ) Keep both → "report (2).pdf"
☐ AI isn't sure  3 files  ▸

                    [ Tidy 101 files ]
          Nothing moves until you press it. You can undo it.

Last tidy: Downloads, 101 files, today 10:40          [ Undo ]

Nervous? Try it on example files first →
```

The flow:

1. **Pick a folder.** The Windows folder picker, then — the first time for that folder — the
   tidy permission dialog (Part 2 §1). A folder already connected in Search can be chosen
   from a short list instead of the picker.
2. **Suggestions appear, grouped by destination folder.** Each group opens to show its
   files. Each file shows why it goes there: "PDF file", "Your rule: Tidy invoices", or
   "AI idea". Only **loose files at the top of the folder** are considered.
3. **Untick anything unwanted** — a whole group or single files.
4. **Same name already there:** the row asks Skip (default) or Keep both, which adds " (2)".
5. **Press Tidy.** Exactly the ticked files move. Afterwards one line says the result, for
   example "Downloads: 101 loose files → 4 tidy folders", plus any files that were skipped
   and why.
6. **Undo** reverses the last tidy, including after a restart.

"Suggestions from" is a switch:

- **DeskAI + my rules** (default): files are placed by type; the person's enabled rules win
  over type; AI is asked only about files DeskAI cannot place, and only if AI is set up.
- **Ask AI about every file:** available only when AI is set up in Privacy and AI; otherwise
  the option is shown disabled with "Turn on AI in Privacy and AI first". The person's rules
  still win over AI, because a rule is something they wrote on purpose.

(Amended in step 2b, ADR 0020: AI is never asked by itself when the list loads. The switch
decides which files may be asked about; an "Ask AI about N files" button opens a dialog showing
exactly what the AI will see, and only Send sends. The V0.3 AI review requires a preview of
the exact real request, and a request on page load has no moment for one. The two choices are
worded on the page as "Only files DeskAI doesn't know" and "Every file my rules don't place".)

AI suggestions DeskAI's AI is not sure about (confidence below a named threshold, initially
0.7) are shown as "AI isn't sure" and start **unticked**. Percentages are never shown.

### What leaves the page

The "Needs attention" box, "Recent activity", "More details", plan revision numbers, the
separate "Want a second opinion?" card, and "Preview one of your own folders" are removed;
the flow above replaces them. The practice run on generated files survives behind the
"Nervous? Try it on example files first" link, in a simplified view that uses the same list
design.

(Amended 2026-09-11, step 5, ADR 0023: asked how to simplify it, the owner chose to remove the
practice page entirely and explain the page with a small "How tidying works" card instead. The
practice executor went with it.)

### Files that are left out, and said so

Shown as "Left alone" with a reason, never silently dropped:

- still downloading: `.crdownload`, `.part`, `.partial`, `.download`, `.opdownload`, `.tmp`;
- changed in the last few minutes (named threshold, initially 2 minutes);
- online-only cloud files (Windows `Offline`, `RecallOnOpen`, or `RecallOnDataAccess`
  attributes), because moving one can force a download;
- hidden and system files;
- anything the safety policy blocks.

Files in use by another program cannot be detected without opening them, so they are found
at the moment of moving: that file is skipped with "It's open in another program" and the
rest continue.

### Limits

At most 500 files per tidy (named constant). Beyond that: "Showing the first 500. Tidy these,
then run it again."

### Automatic checks

When a check finds rule matches, its notice gains a **Review in Organize** button that opens
this page for that folder with the list ready. Automatic checks still never move anything.

### "?" help, everywhere

A small (?) sits next to each feature's title. Pressing it opens a short pop-up with three
parts:

- **What it is** — one sentence.
- **What it does** — one or two sentences, with an example.
- **What it never does** — the relevant safety promise.

Placement: Home (health score, possible copies, storage), Organize (tidy a folder, suggestions
from, undo, try on example files), Search (searching, connect a folder, read inside files,
saved searches), Automatic tasks (checking for you, how often, pause, notifications, rules,
practice run, write a rule from a sentence), Privacy and AI (what is shared, AI choice, your
key, daily limit), and the side-menu reminder.

All help text lives in one catalog. Tests require every topic to have all three parts, keep
each part under a word limit, and contain none of a list of technical words the UI rules keep
out of the main interface (for example metadata, endpoint, provider, schema, SQLite,
deterministic, authorization, telemetry). The button is keyboard reachable and named "Help:
<feature>" for screen readers.

---

## Part 2 — Safety rules

Everything here implements `docs/SECURITY.md`; where this document is silent, that one wins.

### 1. A separate tidy permission per folder

- A new, separately granted, separately revocable **tidy** permission. It is recorded per
  authorized folder alongside, not instead of, the reading scope (look only, or look and
  read inside). `RootCapabilities` answers "may this folder be tidied" from that record and
  denies by default, exactly as it does today for every capability not explicitly granted.
- It is granted only through its own dialog naming the folder and exactly what DeskAI may do
  there. It is never inferred from connecting, reading inside, a rule, an automatic check,
  or AI.
- Withdrawing it needs no confirmation and returns the folder to what it was before.
  **Undo also needs the permission**: after it is withdrawn, pressing Undo first asks to allow
  tidying again, because undo moves files too. (This corrects the conversation's Part 2,
  which said undo would still work after withdrawal.)
- The practice workspace keeps its own `ControlledDemo` scope and is unaffected.

### 2. Allowed operations

(Amended during step 2a: "directly inside" became "inside", because the person's own rules
already use nested destinations such as `Documents\Invoices`. Every destination is still
relative and is validated to stay inside the chosen folder. AI suggestions moved to their own
step, 2b, because they are the first time real file information could go to an AI service.)

Only: create a folder inside the chosen folder; move a loose top-level file into
one of those folders; the same move with a " (2)"-style unique name when the person chose
Keep both. No delete, no move outside, no touching files in subfolders, no other rename.

### 3. Never tidied

The existing permanent protected locations (Windows, program folders, ProgramData, app and
credential storage, browser profiles, DeskAI's own folders), drive roots, network and device
paths, and any folder whose path contains a link or junction. Online-only and busy files as
in Part 1.

### 4. Pipeline

```text
fresh bounded scan of top-level loose files (not the index: the index is never authority)
  → classify by type; apply enabled rules; optionally ask AI (category names only)
  → planner builds typed operations inside the chosen folder
  → Safety validates: containment, protected paths, links, collisions, capability
  → preview (the grouped list), with same-name choices
  → approval bound to plan ID, revision, policy version, and the ticked operation IDs
  → executor rechecks live state per file, journals, moves
  → result line, skipped files with reasons, undo available
```

A same-name choice changes the plan and so produces a new revision; approval always binds to
the revision on screen.

### 5. Checked again right before each move

The file still exists at the same place, with the same size and last-write time as when the
list was built; no component of its path or of the destination is a link or junction; the
destination is still free; the folder's tidy permission is still granted and its canonical
path unchanged. Any mismatch skips that file with a reason. Access denied, disappeared, and
in-use files are ordinary per-file failures; access is never widened to make a move work.

### 6. AI and rules can only suggest

- AI answers with a category from the fixed list (Documents, Pictures, …), parsed by the
  existing strict parser. The category maps to a folder name through DeskAI's recipe; AI never
  supplies a path or a folder name, and has no route to the executor.
- In "Ask AI about every file" the request carries only the categories allowed in Privacy and
  AI, rechecked before sending, with the existing daily cap, timeout, and size limits.
  Protected files are never included.
- Rule destinations are relative folders validated to stay inside the chosen folder (the
  existing rule validation already refuses `..` and absolute paths; it is re-applied here).

### 7. Journal, undo, and interruption

- Before each move DeskAI writes the intended move and the file's size and last-write time to
  the append-only journal; afterwards, the outcome.
- Undo is its own validated run: it moves a file back only if it is unchanged since the tidy
  and its original spot is free; it never overwrites; it removes only folders DeskAI itself
  created and only if empty (the AlreadyPresent rule fixed on 2026-09-10).
- Undo works after a restart because the journal and folder identity persist.
- If DeskAI or Windows stops mid-tidy, the next launch verifies each in-flight move against
  the disk and shows: "Your last tidy was interrupted: 7 of 12 files moved." with
  [Undo those 7] and [Keep them]. A move it cannot verify is marked for review, never guessed.

### 8. Executor

The generated practice workspace and real folders use one executor, so there is one set of
move, collision, link, and journal rules to test rather than two that can drift. What differs
is how the root is trusted: the practice root by its ownership marker, a real folder by a live
check of its tidy permission and canonical path. The decision and its threat review are
recorded in new ADRs when built.

### Threat scenarios (each becomes a negative test)

| Scenario | Expected outcome |
|---|---|
| File changed, replaced, or renamed after the list was shown | That file skipped with a reason |
| Destination occupied after the list was shown | Skipped; never overwritten |
| A file or folder in the path is a link/junction pointing outside | Refused |
| Rule destination `..\x` or `C:\x` | Refused when the rule is saved and again when planned |
| AI returns a path, command, unknown category, or extra fields | Whole AI answer rejected; type/rule suggestions unaffected |
| AI names a file that was not in the request | Rejected |
| Tidy permission withdrawn between preview and Tidy | Nothing moves |
| Folder disconnected mid-tidy | Remaining moves refused |
| Still-downloading, recently changed, online-only, hidden, or system file | Left alone with a reason |
| File locked by another program | That file skipped; others continue |
| Crash between journal write and move, and between move and outcome | Next launch verifies and reports; unverifiable moves marked for review |
| Undo when the file changed, the original spot is taken, or the folder is not empty | Refused per file; nothing overwritten or deleted |
| Undo of a folder that existed before the tidy | Folder left in place |
| More than 500 candidates | First 500 only, stated |
| Automatic check finds matches | Notice offers review; nothing moves |
| Protected location, drive root, network path chosen | Tidy permission refused |

---

## Part 3 — Testing and delivery

### Tests

- Page tests (`DeskAI.Presentation.Tests`) for every step: pick, allow, suggestions, group
  untick, same-name choice, tidy, result line, undo, undo after reopening (a second `TestApp`
  over the same temp database), the interruption prompt, "Review in Organize", and the
  practice link. `TestApp` gains a way to reopen DeskAI over the same folder.
- A negative test for each threat scenario above, using generated files in the owned temp
  sandbox with a sentinel file outside it that must remain unchanged. Online-only files are
  simulated with the `Offline` attribute on a generated file.
- Help catalog tests: completeness, word limits, jargon list, and a coverage check that each
  page's listed features have a topic.
- Manual checklist entries (`docs/MANUAL-TESTING.md`) for the permission dialog, pop-ups,
  keyboard use, and a real tidy of a generated folder under Windows Temp.
- The Feature Coverage Map in `docs/TESTING.md` gains a row per feature.

### Build order (one commit each, app built and handed over after each)

1. **"?" help** on every page.
2. **New Organize page, look and flow:** pick a folder, tidy permission, grouped suggestions
   with reasons and "left alone" items. The Tidy button is shown disabled with "Coming in the
   next step".
3. **Tidying for real:** executor for real folders, live checks, same-name choices, busy and
   online-only handling, 500 limit, result line. (Amended when built, ADR 0021: Undo of the
   tidy just done ships here too, because real moves must not arrive without undo.)
4. **Undo after restart** and the interruption prompt.
5. **Review in Organize** from automatic checks, and the practice link.
6. **Security review record** (`docs/security/`), ADRs, roadmap, README, UI-UX, and
   INTERVIEW-NOTES updated.

The security review record in step 6 checks the built system against this document; the
threat design itself is this document, written before any mutation code as `SECURITY.md`
requires.

## Open for later

- Destinations outside the chosen folder (owner: "maybe later").
- Letting the person rename destination folders.
- Confirming duplicates by content, which remains the open V0.4 item.
