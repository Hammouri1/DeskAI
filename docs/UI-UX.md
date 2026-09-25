# UI and UX Specification

## Experience Goals

DeskAI should feel calm, native, transparent, and reversible. It must communicate scope and consequences more clearly than a normal file manager. Fluent Windows patterns are appropriate, but visual polish never hides warnings or incomplete features.

## Information Architecture

Primary navigation:

- **Dashboard** — health summary, authorized folders, pending suggestions, recent activity, and undo.
- **Organize** — choose a root, scan, build a plan, review, approve, execute, and inspect results.
- **Search** — filters, natural-language query, results, and Smart Collections.
- **Automation** — deterministic rules, simulations, approval scope, schedules/watchers, and run history.
- **Settings** — AI mode/provider, privacy/disclosure, authorized and protected locations, appearance, data/history, diagnostics, and About.

Storage insights may begin on Dashboard and later gain a dedicated view. Workspace Profiles/Design appear only when implemented, not as misleading active navigation. Since V0.7's first slice (2026-09-14) the side menu reads Home, Organize, Search, Automatic tasks, **My workspace**, Privacy and AI (Desktop Studio joined after Automatic tasks on 2026-09-24, ADR 0042); My workspace holds only what is built — pinned searches and starter packs — and later V0.7 pieces join it when they exist. On 2026-09-16 the owner was offered simpler names ("Tidy up", "Checks that run for you", "Privacy") and chose to keep these; the menu is grouped under "Your files", "DeskAI for you", and "Settings" instead (see the command-center shell below).

## First-Run Experience

1. Explain that DeskAI is local-first and does nothing until a folder is selected.
2. State the safety sequence: scan → suggestion → validation → preview → approval → execution → undo.
3. Default to Rule Engine Only; offer AI setup later.
4. Ask the user to add a folder with a trusted Windows picker. Do not pre-authorize personal roots silently.
5. Explain Allowed, Restricted, and Protected in plain language.
6. Explain what tidying does before anything moves. (A safe sample folder was offered until 2026-09-11; the owner retired it in favour of the "How tidying works" card on Organize, ADR 0023.)

Do not overwhelm the first run with every future feature or provider.

**The welcome pop-up (built 2026-09-24,
`docs/superpowers/specs/2026-09-24-first-run-welcome-design.md`).** The first time a brand-new
DeskAI opens — never shown before and no folder remembered — a three-page pop-up appears over
Home, with the logo, three page dots, and **Back**, **Next** (**Done** on the last page), and
**Skip**:

1. **Welcome to DeskAI** — "Find your files and keep them tidy."
2. **You stay in charge** — four ticked lines: DeskAI sees nothing until you connect a folder; it
   only works in your Desktop, Downloads, Documents, and Pictures; nothing moves until you see it
   and say yes; you can put things back.
3. **Let's start** — one button per personal folder Windows reports, and a line saying AI is off
   (or which AI is on, when reopened later).

It is remembered as shown the moment it opens, so Skip, Esc, or closing DeskAI all count;
people already using DeskAI are not greeted after an update. A folder button closes the pop-up and
asks Home's usual "Connect your …?" question; only that question's yes connects, then Organize
opens on the folder. **Privacy and AI → Show the welcome again** reopens it on page one. **Start
fresh** makes DeskAI greet the person again at the next start.

## Dashboard

Suggested hierarchy:

```text
Greeting / status                         Active mode: Local / Rule-only / Provider

Organization Health 78/100                (no action attached to it)
Made of: copies 81 (x80), unused 7 (x20)   (only authorized roots)

Needs attention                          Storage overview
Possible duplicates: 12                  Used / available
Old downloads: 39                        largest categories
Unorganized items: 74

Recent activity                          Undo last eligible action
```

Home now shows real numbers from the index rather than placeholders: folders connected, files remembered with their total size, and how many have not changed in six months. Below that, size by category and the largest files explain where space went. Every number is a reading of remembered metadata, so the page states when it was last checked instead of implying it is live, and says "DeskAI could not read the storage summary" rather than showing zeros if the read fails. The headline states what is actually connected, for the same reason the navigation pane does. There is deliberately no cleanup button anywhere on this page: the summary describes, and any cleanup still goes through preview and approval. Possible duplicates follow the same rule: files sharing an exact size are listed as "possible duplicates" with an "up to" saving, never as confirmed copies, because DeskAI has compared their sizes and not their contents. Confirming means reading file bytes, which connecting a folder does not allow, so since 2026-09-11 (ADR 0024) the card has a plain **Check if they're really copies** button. It opens a dialog stating how many files, in how many folders, and how much DeskAI would read from beginning to end, and that nothing read is saved, sent, or changed; only **Compare** reads. Results replace the guesswork in the same card, group by group — Identical, Same size different contents, Some identical, or Not checked — with every file named and every file not checked given its reason, and one line saying how much was read. Stop is offered while it runs. Even confirmed copies get no remove button: removing anything still goes through Tidy a folder.

**Home as the command center (2026-09-16).** Top to bottom: the hero — the connected-state line on the accent dot, "Good morning / afternoon / evening" from the clock, the existing title and promise sentence, two pills that are true on every screen ("Nothing moves by itself", "You approve every change"), and five neutral floating circles with Fluent glyphs on the right as decoration, hidden on a narrow window. Then **Quick look**: four soft-tinted tiles — Folders connected (blue), Files remembered (violet), Sitting unused for 6 months (amber), Possible duplicates (rose) — each an icon, one big number, and a caption; the duplicates caption reads "Same size, not compared yet", "Nothing looks duplicated", or "Connect a folder to look", never a confirmation. Then two columns at 1100px and wider (one below): the score and "Where your space is going" on the left, "Possible duplicates" with the copy check and "Largest files" on the right; the "up to … " saving on a duplicate row is a caution pill with the copy icon. "What you can try now" and the control promise sit under both columns. Nothing on the page gained an action: every card still describes.

**Your folders (2026-09-16, ADR 0032).** Between the hero and Quick look, under the label YOUR FOLDERS, one card: "DeskAI works only inside these four folders", a line saying connecting remembers names, sizes, and dates and nothing moves until tidying is allowed on Organize, and a row per folder Windows reports — Desktop, Downloads, Documents, Pictures — each with the folder icon, its name, an "Allowed to tidy" accent pill when that permission exists, a status line ("Not connected yet.", "Connected. Tidy it in Organize.", "Connected and allowed to tidy."), and one plain button: **Connect**, which asks "Connect your Downloads?" (names, sizes, dates; nothing inside; nothing moved; Enter, Esc, and the X cancel) and then opens Organize on it, or **Tidy** once connected, which opens Organize straight away. A refusal is written under the rows in the caution colour. When Windows reports none of the four, the card says DeskAI could not find them. This is the one card on Home that does something, and what it does is what the folder picker did already; it exists because the owner's first look found no way to connect anything from Home.

**Ask DeskAI (2026-09-16, ADR 0035).** Under Your folders, one card: "Ask about your folders in your own words" with a visible **BETA** pill and a "?", a box ("What's taking space in Downloads?"), and a plain button named for the service ("Ask OpenRouter"); both off until AI is set up, with the line "Turn on AI in Privacy and AI to ask questions here." The BETA label is honest launch wording: the feature accepts ordinary phrasing, but supports only file search, storage summaries, and guidance into Organize rather than general conversation. When AI is on the line says which way it stands: before the first question, "Only your question is sent to OpenRouter, and DeskAI asks you first. It never sends anything about your files."; afterwards, "Questions go straight to OpenRouter when you press Ask. Only your words are sent, never anything about your files.", with an **Ask me each time** link beside it that brings the question back. Enter or the button opens the same "Send this to …?" dialog as every sentence, with its last line saying the AI answers with what kind of question it is and DeskAI replies itself, **and one more line saying DeskAI asks this once for that service** (2026-09-16, ADR 0035 amended: a dialog before every question was tiring). Only the question box works this way; Ask AI, Plan this folder, and Let AI read this ask every time. Each reply appears as a row card above the older ones — the question in bold, DeskAI's reply, and at most one plain button: **Open in Search**, **Open in Organize**, or **Connect Downloads** (which goes through the Your folders dialog). A refusal is written under the box in the caution colour and the question stays in the box. The list is this visit's only and is not saved.

The score is now on the page, and it is never shown as a bare number. The two parts that produced it — possible copies, and files sitting unused — sit on the same card, each with what it measured, the part score it earned, and how much it counted for, so the total can be added up rather than trusted. Age counts for only a fifth of the score and says so on the row itself ("older files are perfectly normal"), because a settled archive is not a mess and the score must never imply otherwise. Files whose type DeskAI cannot name do **not** lower the score; instead a line beside it states how much of the folder the reading actually covered, so the limit is DeskAI's to own rather than the person's to be charged for. It is worded as an observation ("Looking tidy", "Mostly fine", "Worth a look") and says plainly that DeskAI is describing and not suggesting a change. There is no "improve my score" action, and there must not be one: an action attached to a score is a cleanup shortcut around preview and approval. When nothing is connected or nothing has been read yet the card says it has not been measured, instead of showing a flattering or alarming number from no evidence. The score must not imply that moving more files is always better. Values are local unless telemetry is separately enabled.

## Organize Flow

### Tidy a folder (built, V0.6 step 2a)

The Organize page does one job. It was rebuilt on 2026-09-10 after the owner found the old
page — a practice run, AI ideas about made-up files, a history box, and a read-only folder list
stacked together — impossible to understand. Top to bottom:

1. **Tidy a folder**, with one sentence: pick a folder, DeskAI suggests, nothing moves until
   you say so.
2. **Folder bar**: a list of connected folders (the first is picked automatically) and
   "Choose inside your folders", which goes through the Windows picker and a connect dialog.
3. **Permission card** when the folder may not be tidied yet: three promises on the green
   rail and one accent button, "Allow tidying", which opens a dialog naming the folder.
4. **Suggestions grouped by destination folder**, each group a row with a three-state
   checkbox and a count, opening to its files. Every file shows why ("PDF file", "Your rule:
   …") and where it goes. A same-name clash shows a caution line and a switch between "Skip
   this file" (default) and "Keep both (adds a number)".
5. **Ask AI** (step 2b), a quiet card with a plain button. A two-way choice says which files AI
   may suggest a place for: "Only files DeskAI doesn't know" (default) or "Every file my rules
   don't place" (switched off until AI is set up in Privacy and AI). One line says what there is
   to ask about, the button reads "Ask AI about N files", and a caption states who would see
   what ("OpenRouter would see: file types."). Pressing it opens a dialog naming the service
   and its address and listing each file exactly as the AI will see it; only **Send** sends,
   and the answer appears under the button. AI ideas show "AI idea from OpenRouter", never
   the AI's own words. Ideas the AI was unsure about go into an "AI isn't sure" group at the
   end, with a question-mark icon and a caution note, and start unticked. No percentage is
   shown. The button is plain rather than accent because pressing it confirms nothing; Send is
   the confirming act. **Plan this folder with AI** (V1.1, ADR 0034) sits under a rule on the
   same card with one line: AI names folders and says which file goes into which, DeskAI
   checks every name, and the whole plan is seen before anything moves. It switches the choice
   above to "Every file my rules don't place", opens the same dialog with one more line about
   folder names, and the answer arrives as groups named by the AI's folders — "Bank", "Trip
   2026" — each file saying "AI idea from OpenRouter". A plan that names a folder DeskAI cannot
   make is refused whole and the list stays as it was.
6. **Left alone**, collapsed, listing every file DeskAI will not touch with its reason.
7. **The action bar**, the page's one bold element: the number of ticked files, the Tidy
   button, and one honest line: "Nothing moves until you press it. You can undo it." The button
   is on only while something is ticked (in step 2a it was shown off, with a line saying so).
   After pressing, a result card sits right under it: one line ("Done. Downloads: 12 files
   tidied into 3 folders." only when everything moved; otherwise "10 of 12 files …"), every
   file that stayed with its reason in the caution colour, and **Undo**, whose answer appears
   under it. If the tidy permission was taken back, Undo shows the permission dialog again,
   because undo moves files too. The side menu counts folders DeskAI may tidy, and Home says
   files move only in such a folder, only when Tidy is pressed, and never get deleted.
8. ~~"Nervous? Try it on example files first", leading to the practice page.~~ Retired on
   2026-09-11 with the practice page (ADR 0023). Instead, right under the title, a **How tidying
   works** expander holds four short steps — pick a folder; allow tidying; untick what should
   stay; press Tidy, and Undo even after closing DeskAI — and one promise line with the accent
   shield: DeskAI never deletes, never touches subfolders, never moves anything out of the
   folder. It is open for someone who has not allowed tidying anywhere yet, closed otherwise, and
   stays however the person leaves it.

**Tidy while I'm away (V0.9, 2026-09-16, ADR 0031).** Under the folder bar, only where tidying is allowed: a switch **Tidy this folder while I'm away** with a line under it — "Turn on a rule in Automatic tasks first." (disabled), "Off. DeskAI moves nothing here on its own.", "On since 14:05, for 2 rules. DeskAI moves at most 25 files each time it checks, and stops if anything looks different.", or, in the caution colour, "DeskAI stopped tidying while you're away: <reason> Turn it on again when you've looked." Turning it on opens a dialog naming the folder, each rule as its sentence, and the promise on the accent rail (25 files each time; stops if a rule changes, a file is in the way, or a file can't be moved; never deletes; never moves a file out of the folder; undo every run); the confirming button is the accent **Tidy while I'm away**; Enter, Esc, and the X cancel and the switch snaps back. Off needs no dialog. Above everything else on the page, when there are runs the person has not seen: a **While you were away** card with one line per run — "While you were away, DeskAI tidied 12 files into 3 folders at 14:05 on 16/09/2026." or "DeskAI stopped tidying while you're away at …: <reason>" — never a file name, **Undo the latest run** (the same Undo as any tidy, shown when the latest run is the folder's last tidy) and **Got it**. The notice in the window reads "While you were away, DeskAI tidied 12 files in Downloads. Nothing was deleted." or "DeskAI stopped tidying while you're away in Downloads and needs you to look.", with Review in Organize; the notification carries a count only. Home's pill, Home's promise sentence, Automatic tasks' first card and checking summary, the keep-running dialog, More details, and the help topics all say "nothing moves by itself" only while no folder has the mode on, and the narrower truth otherwise.

When an automatic check's notice sends someone here (**Review in Organize**, step 5), the page
opens on the folder with the most matches and one line under the folder bar says what their
rules place there — "From your automatic check: your rules place 2 files here, marked "Your
rule". Nothing moves until you press Tidy." — or, without the tidy permission, asks for it first.
It never repeats the check's own count, because a check also sees files in subfolders that
tidying leaves alone.

After DeskAI is reopened (step 4), the result card shows the folder's last tidy instead —
"Last tidy: 3 files tidied into 2 folders, at 10:40 on 11/09/2026." — with **Undo**, even
while the tidy permission is off (Undo then asks for it). Only the latest tidy is offered.
If a tidy stopped part-way, a card sits right under the folder bar before everything else:
a caution icon, "Your last tidy was interrupted: 7 of 12 files moved.", one line saying DeskAI
checked each file, any file it could not tell about with where to look (in the caution
colour), and **Undo those 7** and **Keep them** — both plain buttons, because the card asks a
question rather than confirming anything. A tidy stopped before anything moved, and an
interrupted undo ("3 of 5 files went back"), offer only **OK**. Until the card is answered the
Tidy button is off and the line under it reads "Answer the question about your last tidy
first." Answering Keep turns the record into the last tidy, so Undo is still there afterwards.

Removed from this page on purpose: plan revision numbers, "Needs attention", "Recent
activity", "More details", the old AI ideas card about made-up files (AI returned in step 2b
as the Ask AI card above), and the read-only folder preview.

### Select and scan

Show selected root, permission badge, exclusions, scan depth, metadata/content scope, and cloud/local processing state. Progress is cancellable and reports files scanned, skipped, and errors without blocking the UI.

### Plan preview

Group operations into Suggested, Warnings, Conflicts, and Blocked. Each row shows checkbox/eligibility, operation icon/type, source, destination, reason, provenance, confidence if meaningful, and safety status. Provide filters and a side-by-side tree preview for larger plans.

Blocked operations are not selectable. Editing a destination or exclusions creates a new plan revision and visibly triggers revalidation. The approval button says exactly what will happen, for example “Move 14 and rename 3 files,” not merely “Continue.”

### Execution and result

Show per-operation progress, allow safe cancellation between operations, and never turn a partial result into a green generic success. Result states include Completed, Partially completed, Failed safely, and Cancelled, with counts and actionable errors. Surface history and Undo when eligible.

### Undo

Preview undo consequences and conflicts. Explain that files changed after the original operation may require manual resolution. Never imply undo is guaranteed until validation completes.

## Safety Language

Feedback belongs where the action was. The answer to pressing a button must appear next to that button, not in a status line elsewhere on the page: a refusal shown beside a list while someone is typing in a form at the bottom of the page is a refusal nobody reads, and the button looks broken. A message about what someone just typed is also cleared as soon as they change it, because a complaint that outlives the thing it complained about stops being true and reads as if the fix did not work. A button that appears to do nothing is a bug, not a quiet success. Choosing a folder distinguishes three outcomes: a folder was picked, the person cancelled (silence is right — they meant it), or Windows gave no location for what they chose (a phone, a camera, some cloud folders). The third case must always say so, because pressing "Select folder" and seeing nothing happen reads as a broken app and gives someone no idea what to try instead.

Prefer precise, neutral labels:

- “DeskAI can scan metadata in this folder.”
- “This plan contains 2 conflicts and cannot run yet.”
- “Cloud AI will receive filenames and extensions; file contents remain local.”
- “This item is protected and was not analyzed.”

Avoid vague text such as “The AI has access,” “Everything is safe,” or “Your files never leave your computer” when a cloud feature is active.

Status colors always pair with icons and text: Allowed, Needs review, Conflict, Blocked. Color alone must not communicate meaning.

## Help Pop-ups

A small round "?" sits right after the title of each feature. Pressing it opens a pop-up with
three parts, always in this order: **What it is** (one sentence), **What it does** (one or two
sentences with an example), and **What it never does** (the safety promise). Only the last
part carries the accent colour and the green left rail, because it is the one line that is a
safety promise; the "?" button itself is neutral, since asking what something is confirms
nothing.

All text lives in `HelpCatalog` (`DeskAI.Presentation/Help`), never in a page. Tests hold it to
20 / 40 / 25 words per part, ban technical words (metadata, endpoint, provider, schema,
SQLite, deterministic, authorization, telemetry, and similar), and scan the page files so every
"?" points at a real topic and every topic is placed. Help text describes current behaviour
and changes in the same commit as the feature it explains. The button is named "Help: <title>"
for screen readers and has the tooltip "What is this?".

## Privacy Dashboard

Make active state glanceable:

```text
Internet/provider use: Off
AI processing: Rule Engine Only
Cloud data shared: None
Telemetry: Off
Authorized folders: 3
Protected entries: 5
```

Changing from local/rule-only to cloud requires provider selection and a disclosure review. Expanding a disclosure category requires confirmation. Provide controls to remove credentials, revoke a root, clear index/history according to retention rules, and export redacted diagnostics.

**Back up and restore, and Start fresh (V0.8, 2026-09-16).** Two cards at the bottom of Privacy and AI, then an About line with the version. "Save a backup file…" and "Restore from a backup file…" are plain buttons (choosing a file confirms nothing). Restore opens a dialog listing every rule and saved search in the file, with any skip reason in the caution colour, the promise on the accent rail that restored rules start switched off and nothing already here is replaced, and a button that says the count, **Restore 3**, off when nothing would be added; Enter, Esc, and the X cancel. The result is written under the buttons, always naming what was skipped and always saying restored rules are off. "Start fresh…" is a plain button too; its dialog says exactly what is forgotten and that files and the wallpaper on screen are not touched, and its confirming button reads **Forget everything** — a caution word, not the accent, because forgetting is not a permission. The About line says DeskAI never checks the internet for updates.

## Search and Smart Collections

Always show actual scope (“Searching 3 authorized folders”). Natural-language input translates to visible filter chips so the user can correct interpretation. Results show path context, classification source, and why they matched. A Smart Collection is labeled virtual; saving it does not move files.

**Let AI read this (V1.1, 2026-09-16, ADR 0033).** Beside Search, only while AI is set up, a plain button named for the service ("Let OpenRouter read this"), off while the box is empty, with its own "?". It opens "Send this to OpenRouter?", which shows the exact words in a box, says the service and its address, and says nothing about any file is sent and the AI cannot search, move, or change anything itself; Enter, Esc, and the X cancel; **Send** is plain. The AI's reading replaces the phrase in the box as plain words DeskAI reads on its own ("photos holiday"), the chips and results follow as if it had been typed, and a line under the box says "OpenRouter read it as "photos holiday". Change it if that is not what you meant." A refusal or a service problem is said on the same line and the phrase is left alone.

Reading inside files is a separate, visible permission. Connecting a folder never grants it; a second dialog asks, and names what is opened, what is not, and that nothing read is saved or sent. Each folder row states in words whether DeskAI may read inside it, because a permission a person cannot see is one they cannot reconsider, and withdrawing it takes one click with no confirmation. Results found by their contents appear in their own section with a snippet showing why they matched, and the section always says how many files were actually opened — “nothing matched” and “nothing matched in the first fifty files” mean different things. No safety sentence anywhere may claim DeskAI never opens files: that stops being true the moment someone grants this, so the wording is conditional on the permission instead.

**2026-09-20 refinement (ADR 0036).** An old plain-text grant is shown as such and has a separate **Read Word and Excel too** action. The new dialog names the two modern formats and the local, bounded read. Search can accept "Word document containing galaxy" without requiring galaxy in the name; results show the matching text and the actual number of files read. The folder-status line must never say "No folders connected" while a folder row is visible; a page test caught this owner-visible contradiction before the fix. Search, Organize, and Automatic tasks align their page content with the left content edge and use the viewport width up to a readable maximum instead of leaving a large empty gutter.

**PDF text (ADR 0037).** An Office-approved row offers **Read PDF text too**, with its own local-reading dialog. The row then shows that PDF text is allowed and offers **Stop PDF reading**. Empty, encrypted, damaged, or unsupported PDFs are counted as skipped, and a partial read is stated. Scanned pages and photo subjects remain unsupported.

**PowerPoint slide text (ADR 0039).** After the Word/Excel grant, the folder row independently
offers **Read PowerPoint text too** and **Stop PowerPoint reading**. The dialog names `.pptx`,
the connected folder, bounds, and that slide pictures remain closed under this grant. A content match shows
the slide number after the relative folder location. Withdrawing this grant retains PDF
reading if it was separately allowed.

**Find pictures with AI (ADR 0038).** This control is temporarily hidden for the launch build,
and the view model reports the capability unavailable even when AI is configured. When it is
reconsidered, the reviewed design is a separate Search button that appears when AI is configured.
The first dialog names the selected connected-folder scope and the 30-file / 12-picture /
4-MB limits, and asks to read pictures for this search. With a local AI, that is the only
approval. With cloud AI, a second dialog lists every picture and its page or slide, total
bytes, the provider destination, and a cost warning. Cancel sends nothing. Results sit in
**Found in pictures**, with the AI's short reason and page/slide when available. The page
says AI can be mistaken. It does not imply all files, PDF pages, or image encodings were read.

After a person found one PDF match while two other files could not be read, Search gained a collapsed **Files checked** list. It names each attempted file and says whether its text matched, had no match, was only partly read, or could not be read. The summary points there and explains that searching `pdf` alone lists remembered PDFs by name. This keeps a missed match distinct from a PDF the reader could not open.

**Search scanned PDF words** is separate from ordinary Search. It is enabled only for a
non-empty phrase and a selected connected scope with PDF permission. Every click previews
the local-only limits and warns that OCR is approximate; Cancel reads no page image. Results
state that the person must verify important matches in the original PDF.
If a PDF is only partly read, its row names the first-100-pages or 256-KB text limit and says a word may be later. The folder location remains visible even when no content word matches, so a PDF in a subfolder is not mistaken for a file the scanner missed.

**Look in** sits before Search: all connected folders by default, or one folder the person
chooses. A saved search still saves words only; the folder choice belongs to this visit and
never becomes a lasting permission or an AI-selected path.

## Rules and Automation

Use a readable “When / If / Then / Scope” editor. When AI drafts a rule, show the deterministic interpretation and a simulation against sample/current indexed files before approval. Clearly distinguish enabled, scheduled/watched, manual-only, paused, and needs-review. Provide a kill switch/pause-all action.

**Let AI read this (V1.1, 2026-09-16, ADR 0033).** Next to **Read my sentence**, only while AI is set up, the same plain button and dialog as on Search. The AI's reading replaces the sentence with plain words ("move .pdf statement into Bank") and is read into the boxes exactly as a typed sentence is, so the usual "DeskAI read that as: …" line follows and nothing is saved until **Add**. A destination the AI names that is not a plain folder name is refused whole and the boxes stay empty.

## My workspace (V0.7, built 2026-09-14)

One sentence under the title: "Your shortcuts and starter packs. Nothing here moves or changes
files." Top to bottom:

1. **Pinned searches.** Tiles, at most eight, each a saved search's name, a count, **Open in
   Search**, and **Unpin**. Counts are worded so no guess reads as a fact: "1 file", "23 files",
   "200+ files" when the search reached its limit, "No folders connected", "Search not
   understood", or "Could not count" on that tile alone. A caption says when the counts were made
   and to refresh a folder in Search to update them. A tile never shows a file name. With nothing
   pinned: "Pin a saved search to see it here." and a link to Search.
2. **Starter packs.** Five cards — Student, Developer, Gaming, Productivity, Minimal — each a name,
   one line, and a plain **See what it adds** button (plain, because seeing confirms nothing). It
   opens a dialog listing each search by name and words and each rule as its sentence, with any
   item to be skipped and why in the caution colour, and the promise on the accent rail: "Rules
   start switched off. Nothing moves until you turn a rule on and press Tidy." **Add** is the
   accent button; Enter, Esc, and the X behave as Cancel; Add is off when the pack would add
   nothing. The result is written on that pack's own card, for example "Added 1 search. Skipped 1
   you already had: Screenshots.", and always says added rules are switched off. There is no
   Custom card; a line says "Or make your own in Search and Automatic tasks."
3. **Folder templates** (piece C, 2026-09-16). "Make a set of empty folders in a folder you
   chose. You see the list first, and you can undo it." With nothing connected: "Connect a folder
   in Organize first." and a link. Otherwise a **Make them in** folder choice (connected folders
   only; connecting stays in Organize), then six cards — the five pack templates with their
   folder names on one line, and **Your own folders** with a text box "Folder names, separated by
   commas" — each with a plain **See what it makes** button (plain, because seeing confirms
   nothing). If the folder may not be tidied, the same "Allow DeskAI to tidy …?" dialog Organize
   shows comes first. The preview dialog lists "DeskAI will make:", "Already there, left as they
   are:" (by the name on disk), and "Can't be made:" with each reason in the caution colour, the
   promise on the accent rail — "Only empty folders are made. Nothing is moved, renamed, or
   deleted. You can undo it." — and the accent button says the count, **Make 3 folders**, and is
   off when nothing can be made. Enter, Esc, and the X behave as Cancel. The result is written on
   that card, for example "Made 2 of 3 folders in Downloads. Notes: A file called Notes is already
   there. Already there: slides.", and never says "Made" for a folder that was already there.
   Under the cards, one line and one **Undo** for the chosen folder's last template run, found in
   DeskAI's history so it is still there after reopening; a tidy in that folder afterwards takes
   it off the page. A typed name that cannot be a folder is refused on the card with a plain
   reason before anything is looked at.
4. **DeskAI's look** (piece D, 2026-09-16). "Colours for the DeskAI window only. Your Windows
   theme and wallpaper are not touched." A **Light or dark** choice — Follow Windows, Light,
   Dark — and six look cards, Slate, Graphite, Sand, Ocean, Lavender, and Rose, each with a
   larger two-part swatch (how it
   reads in dark and in light), a line, and a plain **Use this look** button. The chosen card
   says "Chosen" in the accent, because a chosen state is a confirmed state. Choosing repaints
   the window at once and is remembered. See "Looks" under the visual system below.
5. **Desktop and wallpaper** (piece E, 2026-09-16). One line: "The only Windows setting DeskAI
   can change is your wallpaper, and only when you press the button." Two cards. **Wallpaper**:
   a plain **Choose a picture…** button (the Windows file dialog, pictures only); then a dialog
   showing the picture, its name, "Windows shows now: …", the promise on the accent rail (DeskAI
   remembers the current wallpaper; a slideshow or Spotlight stops; no other setting changes),
   and the accent button **Use as wallpaper**. The result is written on the card. When DeskAI
   has changed the wallpaper, the card shows "Your old wallpaper: holiday.jpg." (or "a plain
   colour") with **Put the old wallpaper back**, and a caution line when Windows now shows
   something DeskAI did not set. **Your folders** (which replaced "Your Desktop" on 2026-09-16,
   ADR 0032): the same card Home shows — Desktop, Downloads, Documents, and Pictures as rows,
   each with its state and one **Connect** or **Tidy** button, "Connect your Desktop?" asked
   first (names, sizes, dates; nothing inside; nothing moved), then Organize opened on it, where
   the usual permission and preview apply. A folder connected here appears in the template
   folder choice at once.
6. **Your other saved searches.** Saved searches not pinned, each with **Pin**. Past eight, Pin is
   off and a line beside the list says to unpin one to make room.

The sentence under the page title is "Your shortcuts, starter packs, and folder templates.
Nothing here moves a file." It stopped saying "changes files" the day templates arrived, because
a template does change a folder; the narrower promise is the one that is still true.

## Quick search (built 2026-09-25, ADR 0047)

Spec: `docs/superpowers/specs/2026-09-25-quick-search-design.md`.

- **The bar** (look C, "Glowing edge", 2026-09-25). The shortcut picked on My workspace (Ctrl +
  Alt + D to start) shows it on the screen with the pointer, centred, about 12% down from the
  top. The window is see-through around the card, with a soft mint glow; the card itself is
  solid dark navy (fully opaque: at 95% the owner saw text behind it through the box), about
  640 px wide, with 20 px corners and a 1.5 px edge in mint → blue → violet → pink that turns
  slowly (once every 6 s). The chosen buddy perches on the card's top edge, centred, with its line
  in a mint-to-blue bubble to its right. The search box is large, with a search icon on the left,
  an **Esc** key hint on the right, no frame of its own, and a mint underline while typing; it
  has focus at once. Empty, it shows three example searches as pill buttons. Results come in two
  short groups, **By name** and, a moment later, **Words inside** (with one line of text), each
  at most 5 rows: a coloured tile for the kind of file (DOC blue, PDF red, IMG orange, VID
  violet, FILE grey), the name, where it is ("Documents › School"), and **Open** or **Show in
  folder**; the selected row has a mint left edge and tint and shows "Open ↵" or "Show in
  folder ↵". Plain fact lines sit under the rows in DeskAI's usual voice, never in the buddy's.
  Opening, while **Let my buddy move** is on: the card fades and drops in, the buddy pops up a
  moment later, and rows slide in one after another; with it off, the bar appears at once and
  nothing moves, the edge included. Up/Down, Enter, Ctrl + Enter (show in folder), and Esc work
  as expected; clicking elsewhere, or on the see-through part around the card, hides it. It is
  not in the taskbar or Alt + Tab.
- **Buddies.** Seven drawings (Sparky, Archie the owl, Pip the robot, Fetch the fox, Inky the
  octopus, Mochi, Paige the paper ghost), each with five moods: waiting, thinking, found,
  nothing, happy (after a click). They stand still when **Let my buddy move** is off (on the Quick
  search card, on by default; DeskAI's own switch wins over Windows' Animation effects, the
  owner's choice 2026-09-25).
  Screen readers hear the buddy's line (announced politely), never the drawing.
- **My workspace → Looks → Quick search.** The switch "Press {shortcut} to find a file"; under it a
  **Shortcut** drop-down with Ctrl + Alt + D (the default), Ctrl + Alt + Space, and Ctrl + Shift +
  Space, which DeskAI listens for at once; if another program uses the one picked: "Another program
  already uses {shortcut}. Pick another shortcut above." (nothing listens until another is picked);
  a line saying closing keeps DeskAI near the clock, and **Your search buddy** (chooser B, "Big
  stage and faces", 2026-09-25): a stage about 240 × 230 with the chosen buddy large on its own
  background (night, study, lab, forest, sea, meadow, dusk) and a mint edge; beside it the buddy's
  name and its hello line in a mint-to-blue bubble; under them seven round faces (60 px, a mint
  ring on the chosen one; one row when DeskAI is wide, two rows below 1,200 px so none is cut off).
  Clicking a face, or moving to it with the arrow keys, chooses it at once;
  the new buddy pops in when **Let my buddy move** is on. Screen readers hear a radio group "Search
  buddies", each face as "Choose Archie the owl" with whether it is chosen, and the new name and
  line announced politely; the stage drawing is silent. Under the chooser: the **Let my buddy
  move** switch and "Turn this off to keep your buddy and the search bar still."
- **Welcome.** A page before the last: "Find any file, from anywhere", with Sparky and the shortcut
  the person picked.
- **Icon near the clock.** Its menu is Open DeskAI, **Find a file** (while quick search is on),
  Pause checking (only while checking in the background), and Quit DeskAI.
- **No tip on Home or Search.** One was built and removed at the owner's request: it was not
  needed, and on Home it covered the welcome panel.

## Implemented Visual System

The shared vocabulary lives in `src/DeskAI.App/Themes/DeskAITheme.xaml`, merged from `App.xaml` after `XamlControlsResources` so DeskAI's palette wins. No page invents its own colours.

The system is called **calm workspace**, and it has one governing rule:

> The accent colour means "safe or confirmed". It is never used as decoration.

A person should be able to learn one thing — green means DeskAI is allowed to do this — and have it hold on every screen. Anything that is merely structure uses the neutral line colour instead. This is why the palette is declared per theme rather than borrowed from the Windows accent: an arbitrary user-chosen accent cannot carry a fixed meaning.

Palette tokens are defined for both themes in `ResourceDictionary.ThemeDictionaries`:

| Token | Dark (`Default`) | Light |
| --- | --- | --- |
| `DeskGroundBrush` | `#0F1216` | `#F6F7F9` |
| `DeskSurfaceBrush` | `#161B21` | `#FFFFFF` |
| `DeskSurfaceRaisedBrush` | `#1D242C` | `#FFFFFF` |
| `DeskLineBrush` | `#272E38` | `#E1E5EA` |
| `DeskAccentBrush` | `#4DD8A8` | `#0E8C64` |
| `DeskCautionBrush` | `#E8955A` | `#A8541B` |
| `DeskDangerBrush` | `#F0685C` | `#C0392B` |
| `DeskTextPrimaryBrush` | `#E7EBF0` | `#10161D` |
| `DeskTextSecondaryBrush` | `#8C97A5` | `#5A6572` |

The `HighContrast` dictionary maps every token back to `SystemColor*` brushes, so Windows high contrast overrides the palette entirely.

**Looks (V0.7 piece D, 2026-09-16; expanded 2026-09-17).** The table above is the **Slate** look, DeskAI's default. A person can choose Graphite, Sand, Ocean, Lavender, or Rose instead on My workspace, and light, dark, or follow Windows. A look is a `LookPalette` of exactly five neutral tokens per theme — `DeskGroundBrush`, `DeskSurfaceBrush`, `DeskSurfaceRaisedBrush`, `DeskLineBrush`, `DeskLineStrongBrush` (and the WinUI card fills that mirror them) — applied by changing those brushes' colours in place in both theme dictionaries, so every page repaints and the next theme switch finds the look already there. High contrast is never touched. The accent, caution, danger, and text tokens are not part of a look, by construction: `LookPalette` has no such property, a test asserts it, and another test checks every look keeps the shared text at 7:1 (primary) and 4.5:1 (secondary) contrast on its ground and surfaces. That is how "green means safe or confirmed" survives a person choosing their own colours.

Shared styles:

- `PageContentStyle` and `PageIntroStyle` — the common content width, spacing, and softly tinted introduction on every page. An introduction is orientation, not a permission, so it has no accent rail.
- `HeroPanelStyle` — reserved for a safety promise or permission. A 3px left rail in the accent carries that meaning; it is no longer generic page decoration.
- `CardStyle`, `SoftCardStyle`, `RowCardStyle` — surfaces at 16px/16px/10px radius, differentiated by fill weight. `SoftCardStyle` is transparent with a hairline only.
- `DeskDisplayStyle`, `PageTitleStyle`, `SectionTitleStyle`, `MetricStyle`, `BodySecondaryStyle`, `CaptionStyle` — one type ramp on Segoe UI Variable Display for headings and Segoe UI Variable Text for body, with negative tracking on the display sizes. `MetricStyle` sets numbers large and light so the value reads before its label.
- `StepBadgeStyle` — a quiet bordered chip. It is deliberately **not** accent-filled, because the accent is reserved for safety state.

Accent buttons override `AccentButtonBackground` as well as `AccentFillColorDefaultBrush`, because the WinUI accent button style takes its fill from the former; setting only the latter leaves the button on the Windows accent colour. They are used for actions that confirm or authorize something, never for ordinary commands — the Search button is a plain button, since searching confirms nothing.

`HeroSheenBrush` is retired. It remains defined as a transparent brush so any page still referencing the old gradient wash renders nothing rather than failing to load.

Three treatments were removed as generic and meaningless here: the all-caps eyebrow labels (`LOCAL AND PRIVATE`, `PRACTICE MODE`), the translucent gradient wash over the accent, and the 56–72px decorative icons in the page headers. Metrics that belong to one reading now share a single panel divided by hairlines instead of being split into identical repeated cards.

The window uses a Mica backdrop, pages paint `DeskGroundBrush`, and the navigation pane footer carries a permanent scope reminder ("Nothing connected yet" until a folder is connected) on the same accent rail, because "which files can this app touch" should never require navigating to find out.

**The command-center shell (2026-09-16, design `docs/superpowers/specs/2026-09-16-command-center-redesign-design.md`).** The owner asked for a professional dashboard feel from a reference screenshot and chose to keep the menu names. What every page now has:

- **A grouped menu.** Small grey labels ("Your files": Home, Organize, Search; "DeskAI for you": Automatic tasks, Desktop Studio, My workspace; "Settings": Privacy and AI) above the items (seven since Desktop Studio joined on 2026-09-24), the DeskAI mark and name at the top, and the pane painted with the surface colour so it reads as one panel in every look. The selected item sits on a neutral raised pill, never the accent. `ShellViewModel.Pages` holds the routes and names for the top bar, and `ShellLayoutTests` reads `MainWindow.xaml` to keep the two lists identical, in order.
- **A top bar** above the page: the page name on the left; on the right a search box ("Find a file…") and the **AI pill**. Enter in the box leaves the phrase in the same one-shot `SearchRequest` that "Open in Search" uses and opens Search with it already run, so it grants nothing a person could not type on Search. The pill reads "AI off", "AI on this computer", or "AI: OpenRouter", from the saved AI choice on every refresh — only a service that is actually ready (consent given, key reference saved, a catalog service) is named, so the pill can never claim a service that cannot be asked. Pressing it only opens Privacy and AI.
- **A dark-mode switch** in the pane footer, above the scope reminder. It saves the same light/dark choice My workspace offers (an explicit Light or Dark; "Follow Windows" stays available there), repaints the DeskAI window at once, and touches nothing in Windows. While following Windows it shows the theme the window is actually painting, reported by the window because the view model cannot see WinUI.
- **Rounder surfaces**: 12px cards and soft cards, 10px row cards, the hero's free corners at 14px; the accent rail is unchanged. New shared styles: `TileStyle` (a soft-tinted stat tile), `PillStyle` / `AccentPillStyle` / `CautionPillStyle` with `PillTextStyle` (an icon and a word, never colour alone; accent only for a permission or a confirmed state, caution for paused or needs-a-look), `GroupLabelStyle`, and `TopBarStyle`.
- **Four tile tints** — `DeskTintBlueBrush`, `DeskTintVioletBrush`, `DeskTintAmberBrush`, `DeskTintRoseBrush` — declared as alpha tints in the dark and light dictionaries so they sit on every look, and transparent in high contrast. They are for counts, and a test asserts none is the accent and no page paints a tile with anything else. They are not part of a `LookPalette`: a look still changes only the five neutral tokens.

**Calm-workspace revision (2026-09-17).** The six working pages keep the same commands and
safety decisions, but the first screen now reads in a simpler order: a short introduction, the
next useful action, then supporting information. Home uses four roomy folder cards instead of
squeezing a badge and button onto one line. The beginner guide and Search's long protection
explanation are collapsed until asked for. Search puts results before reusable saved searches.
My workspace groups its many independent jobs under **Looks**, **Shortcuts**, **Folder sets**,
and **Desktop**. Privacy and AI groups them under **AI setup**, **What you share**, and **Your
saved data**. These are view-only tabs; switching one grants no permission and runs no work.
Every page uses the same rounded intro surface and content spacing, the side menu collapses at
smaller widths, and action rows wrap or move below their label rather than clipping it.

On a wide window, Home's bounded content is centered in the usable page viewport rather than
appearing offset toward the right. The unpackaged window also explicitly applies the same DeskAI
icon embedded in the executable, so the title bar and task switcher do not show WinUI's generic
default mark.

Two more looks, **Lavender** and **Rose**, join Slate, Graphite, Sand, and Ocean. The preview for
each look is large enough to show both its dark and light surfaces before choosing it. ADR 0028
still applies unchanged: a look may tint only five neutral colours. Green remains the fixed
safety/confirmed signal in all six looks, and high contrast still comes from Windows.

Visual inspection uses an explicit `DeskAiUiPreview` build. That build creates a unique folder
under Windows Temp with generated dummy files and replaces known folders, keys, network,
wallpaper, pickers, notifications, tray presence, and hosted timers. It cannot load the owner's
DeskAI database or personal folders and it is never part of a normal build.

**The other pages (2026-09-16), same content and words, restyled.** Organize's folder bar carries a pill beside the folder — "Allowed to tidy" on the accent when tidying is allowed, "Look only" plain when it is not — and each suggestion group's count is a plain pill. Search's "DeskAI read this as" chips became plain pills (a reading is not a permission), and each folder row carries "Can read inside" on the accent or "Names, sizes, dates" plain, beside the sentence it already had. Automatic tasks is two columns at 1320px and wider — rules, the practice run, and "Write a rule" on the left; "Checking for you" on the right so it is never scrolled past (ADR 0017), first when the page folds to one column — and its card carries "Paused" as a caution pill or the frequency ("Every 15 minutes") as a plain pill; each rule row shows On on the accent (the person's confirmed choice) or Off plain. Privacy and AI's hero holds the five "At a glance" readouts as small cards inside it. My workspace's pinned searches are blue-tinted tiles with the search icon and the count set large, and the chosen look wears a small accent pill reading "Chosen". Every pill has an icon and a word; the accent variants are exactly the permission and confirmed states listed here and nothing else.

Status is expressed through `PreviewStatusLevel` (`Ready`, `Attention`, `Blocked`) mapped by converters to a system semantic brush, a paired Segoe Fluent glyph, and a tinted badge background. Colour is never alone: every badge carries an icon **and** the status word, so a blocked row still reads as blocked in greyscale or high contrast. `PreviewStatusLevel` is presentation severity only — Safety decides what is blocked, and the enum merely chooses how that decision is drawn.

Empty states stay truthful rather than becoming decorative, and no page implies a capability that does not exist.

Automatic tasks used to state outright that DeskAI was doing nothing in the background. That sentence expired when automatic checks arrived: DeskAI now looks by itself. The card was rewritten to "DeskAI never moves a file on its own" — the narrower claim that is still true — rather than kept because it was reassuring. The same rule as the scope label applies: a promise about what DeskAI does is the one sentence that must never outlive its truth.

A finished check leaves a quiet notice in the top-right corner of the window, over the page rather than inside it, because a check can finish while someone is on any page or away from the machine. It reports a count and says nothing has moved in the same sentence, stays until it is reviewed or dismissed rather than fading, and its only actions are navigation: **Review in Organize** (V0.6 step 5) opens Tidy a folder on the folder with the most matches, and a quieter link opens Automatic tasks. A check that found nothing says nothing at all: announcing "nothing matched" every fifteen minutes would train someone to ignore the one time it says something did.

The navigation pane always states the current scope, and that text is derived from what is actually connected rather than written as a fixed string. An earlier version hard-coded "Sample files only. Your personal folders are not connected.", which stayed on screen after a real folder was connected: the one label that promises what DeskAI can reach was the label that lied. It now reads "Practice mode" only while nothing is connected, and otherwise reports the folder and file counts. If the scope cannot be read it says so, and never falls back to the reassuring wording.

The scope a search reports and the folder list a page shows come from one predicate, `FileSearchService.IsSearchable`, so they cannot disagree. It requires both an `Allowed` permission and `MetadataOnly` scope; permission alone would include the controlled demo workspace and make the page claim to search a temporary folder nobody connected.

Search keeps three outcomes visibly distinct, because collapsing them is how a search screen starts lying:

| Outcome | What the page says |
| --- | --- |
| No folders connected | "No folders connected yet" and points to Organize |
| Phrase not understood | "I did not understand that", with examples. **No results are listed.** |
| Understood | A count, the matches, and the scope actually searched |

The second row is the important one. An unrecognised phrase produces a query with no filters, which would list every remembered file — a full listing presented as a search result. The page refuses instead.

Above the results, chips show how the phrase was read ("Photos", "Larger than 100 MB", "Changed in the last month"), so interpretation is visible and correctable rather than silently assumed. Vague words state their real threshold instead of hiding it. Scope is always stated — "Searched 2 connected folders" — so the page never implies the whole computer was searched, and a truncated list says so rather than passing as complete. Results show a folder name and a path relative to it, never an absolute path.

## Visual Direction

- Use WinUI/Fluent conventions and Windows system typefaces, with DeskAI's own declared palette for both light and dark. High contrast defers to Windows.
- Favor calm neutrals with one accent. The accent is reserved for "safe or confirmed" and is never decorative; warning and error colors likewise carry meaning only.
- Use density appropriate for file lists with optional comfortable mode.
- File icons/thumbnails must not leak content to cloud services.
- Animations are subtle, respect reduced-motion settings, and never delay confirmation.
- Wallpapers/themes should affect preview/design areas later, not readability of safety UI.

## Accessibility and Localization

All functions must be keyboard reachable with logical focus order, visible focus, automation names, scalable text, and adequate contrast. Announce scan/execution progress without excessive screen-reader noise. Do not encode actions only in icons. Prepare strings for localization and avoid building sentences by concatenation. Respect Windows high contrast, text scaling, locale, date, number, and path display conventions.

## Empty, Loading, and Error States

Every page has a truthful empty state with the next safe action. Use skeleton/progress only for real work, support cancellation, preserve recoverable user selections, and present per-item errors where possible. Settings/provider errors must not prevent rule-only local operation.

## V0.1 UI Scope

Implement a native shell, navigation, theme support from system defaults, placeholder page headings/descriptions, and a visible “Foundation only—no files are being accessed” status. Do not simulate fake scan results or functional buttons. The first real vertical UI slice belongs to V0.2.

## V0.2 Preview Demonstration

(History. The practice page this describes was retired on 2026-09-11, ADR 0023.)

The implemented Organize page uses progressive disclosure. Its main surface shows a short Review → Choose → Try it flow, recognizable filenames, friendly source/destination folder names, simple Ready/Not included states, one plain-language attention card, and a button that states how many sample files will be organized. Internal create-folder operations are automatic and hidden from the main list. Plan revision, exact temporary path, and supporting-operation counts remain available in a collapsed Technical details section.

The sample uses the controlled temporary executor introduced in V0.2 step 5. Practice mode is named prominently and explains in one sentence that personal files are not used. Conflicts use calm language and remain unselected; technical safety terminology is not required to complete the demo.

Step 8 adds a separate, optional “Preview a folder” card below the practice flow. Its short description says exactly what is read and that moving/deleting remain off. Choosing a folder opens the native Windows picker and then a confirmation dialog showing the path and metadata scope. Results stay collapsed behind a file count by default, and Disconnect revokes permission without changing files.

## V0.3 AI and Privacy UI

Settings is named “Privacy and AI” and starts with a short status card. Sharing controls use everyday names and require confirmation when allowing more information. The three main choices are “Don't use AI,” “AI running on this computer,” and “Online AI with my own key.” Choosing the online option reveals a list of supported services so a person can pick the one they already have an account with; the model hint, key label, agreement wording, and remove-key button all rename themselves to that service. Addresses, model names, timeouts, and daily limits are grouped below the main choice instead of leading with technical language. Online activation repeats exactly what may be shared, names the exact host that will receive it, and reminds the user that the chosen service controls pricing and service-side data handling.

Organize presented AI as an optional “second opinion” for generated samples (retired 2026-09-11; AI is now asked from Tidy a folder). The card identifies where data would go, offers a Stop button, reports usage when available, and shows the category, confidence, explanation, and source for each idea. It explicitly states that AI cannot move, rename, or change files.

**"Check this now" (2026-09-17).** Beside **Save AI choice**, a plain button — checking is not a
permission. Under both, the saved-choice line, then a result card that exists only once a check
has run: a tick in the accent and "Working. <service> answered.", or a cross in the danger colour
and one sentence naming the thing on this page to change ("<service> did not accept your key.
Paste it again above, copying only the key itself."), with the service's own words quoted after it
when it gave any and the key itself never echoed back. A caption under the card says checking
sends no file names, sizes, or locations, and that online AI spends one request from the daily
allowance; the online button confirms that in a dialog first. Saving again or removing the key
clears the card, because a result belongs to the setup it was taken from.

Saving now says what it did, not what it assumes: "Saved. Now press "Check this now" to see
whether <service> answers." — never "ready", which DeskAI had not checked. A refusal reads
"Not saved." followed by a sentence a person can act on, and also opens a dialog headed
**DeskAI did not save that yet**: the status line alone sat below the fold in small grey text,
and the owner read a filled-in form and an unchanged page as a broken app (2026-09-17).

**Desktop Studio (2026-09-24, ADR 0042).** A page in the menu after Automatic tasks, titled
"Desktop Studio" with the line "Make your Desktop easier to find your way around. Each card does
one job, and nothing changes until you choose." Until the Desktop is connected it shows only a
**Connect Desktop** card, using the same dialog as the Your folders card. The **Find groups** card
says "DeskAI sorts the folders and files on your Desktop into groups. Nothing on your PC
changes." and offers **Find groups with <service>** (only when AI is set up) and **Use DeskAI's
guess**. The AI button opens a Send window listing every line the AI would see, with the service
and its address, "names and kinds of files. Not what is inside them, and not where they are.",
and **Send** / **Cancel** (Cancel is the default). The board shows who made it ("Grouped by
OpenRouter." or "Grouped by DeskAI's own simpler guess."), then the groups as a grid of
same-size cards. Each card shows the group name on its own line, how many items it holds ("8
items"), and a small **⋯** button (named "Rename or merge") that opens **Rename…** and **Merge
into** (left out when there is no other group); each item has a folder or file icon and a quiet
**Move to…**, and a long group scrolls inside its card. **Not sure** comes last, across the full
width, with its items in columns and its own scroll after about eight rows. The owner found on a
real Desktop (2026-09-24) that Not sure ran down the page in one column and that the Rename and
Merge buttons squeezed names into "Document s"; `DesktopStudioLayoutTests` keeps both fixed. Later cards (Clear old stuff, Folder by group, and so on) join this page one at a time.

**Moving cards (ADR 0044).** Below the board: a caption saying DeskAI asks before the first move, or a quiet "Stop DeskAI moving things on my Desktop" link once allowed; a "Your last change stopped part-way" card when needed, with Keep them where they are and Put them back; then, right under the board, **2. Happy with these groups?** with **Put each group in its own folder** (shown only once there is a board) and the small line "Want to clear old stuff too? Do it first, under Other tidy-ups." (Clear old stuff looks only at what is loose on the Desktop), and after an **Other tidy-ups** heading **Clear old stuff** with Show what would move — when it finds nothing and a group's own folder is on the Desktop, it says it doesn't look inside group folders and works best first; each shows a list of tick boxes (folder or file, how many files inside, last changed or which folder it goes into, and a warning line that keeps the row unticked), the ticked total ("Ticked: 1 folder holding 2 files, plus 1 file"), Move N things, Put back with its last-change line, and Left where they are with each reason. The first Move or Rename shows the dialog "Allow DeskAI to move or rename things on your Desktop?" with Allow / Cancel (Cancel is the default).

**Tag names card (ADR 0045).** Right after **2. Happy with these groups?**, and only once there is a board: **Or keep them where they are, and add the group's name**, with **Add the group's name to each folder's name**. Its rows show each folder in a group with “becomes "Coding – Python stuff"” (never a file), the button reads Rename N folders, Put back gives the old names back, and "Left with their names" lists each folder it will not rename with the reason.

**Page order (owner-found 2026-09-24).** The owner found the page confusing: after Find groups the next step sat at the bottom under the unclear name "Folder by group". The page now reads as steps: **1. Find groups**, the board, **2. Happy with these groups?** with **Put each group in its own folder**, then **Other tidy-ups** with Clear old stuff. `DesktopStudioLayoutTests` keeps that order.
