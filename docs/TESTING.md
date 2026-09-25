# Testing Strategy

## Objectives

Tests must prove both useful behavior and refusal of unsafe behavior. The highest-risk failures are unauthorized scope expansion, path escape, silent overwrite, stale approval, incorrect recovery, secret disclosure, and claims of success after partial execution.

## Test Layers

### Unit tests

Fast, deterministic tests for Core planning/rules and pure Safety policies. Use tables/theories for path and operation cases. No real provider, database, clock, or personal filesystem.

### Component/integration tests

Exercise SQLite repositories/migrations, scanner adapters, and eventually the executor against a unique generated temporary directory. Use fake AI/network and fake credential storage unless a specifically isolated platform contract test is intended.

### Page tests (`DeskAI.Presentation.Tests`)

Every feature a person can reach is tested the way a person uses it: fill in what the page
asks for, press the command, and check what the page then says and what happened on disk.
`TestApp` builds DeskAI through the same `AddDeskAiApplication` call the app uses, with a real
SQLite database, the real scanner, planner, safety checks, and real-folder executor, all inside a
generated temp folder. Only the credential store, the network, and Windows notifications are
replaced, so no key is written to Windows and no request leaves the machine.

These exist because unit tests alone were not enough. Until 2026-09-10 the engine had 545
tests and the pages had none, and every bug the owner found by hand was on a page. The first
pass of page tests immediately found four more: the Search page sending people to Organize to
connect a folder, Home implying connected folders could be changed after a preview, undo
reporting "partly completed" after a clean practice run, and undo deleting an empty folder
that existed before the run.

A page test asserts the words a person reads where those words are the point — a refusal, a
count, a promise about what DeskAI can or cannot do — and asserts the files on disk whenever
the feature could plausibly have touched them.

### Windows UI tests

Confirmation dialogs, the folder picker, and navigation are WinUI objects and are checked by
hand using `MANUAL-TESTING.md`. The view-model method each dialog calls is page-tested. Add
Windows UI automation only for what page tests cannot reach, keeping selectors stable and
accessibility-driven.

### End-to-end safety scenarios

On a controlled test machine/root only: scan → plan → validate → preview → approve → execute → journal → undo. Capture exact before/after trees and verify no path outside the sandbox changed.

## Filesystem Sandbox Rules

Every mutation test must:

1. create a uniquely named directory through the test framework/system temporary facility;
2. resolve and verify its absolute canonical path is under the intended temporary root;
3. create only dummy files with non-sensitive content;
4. inject that root instead of discovering Desktop/Downloads/Documents/Pictures;
5. record a sentinel outside the sandbox where safe and verify it remains unchanged;
6. clean only the exact owned directory in `finally`/fixture disposal after re-verifying the target;
7. preserve the sandbox and print its safe path when debugging a failure if configured.

Tests must never enumerate or mutate personal known folders. Do not depend on developer usernames, drive letters, locale, clock, network, or installed cloud-sync clients.

Native picker behavior is verified manually only with a newly generated Windows Temp folder containing dummy data. Automated read-only authorization tests use the same owned-temp helper, lock a dummy file to prove contents are not opened, verify protected-root refusal and scan bounds, and confirm revocation changes only persisted permission.

## Required Safety Matrix

- Allowed source and destination inside the same authorized root.
- Source or destination outside root.
- Prefix confusion (`Root` versus `RootOther`) and `..` traversal.
- Mixed separators, trailing separators/dots/spaces, relative paths, case differences, reserved names, long paths, device/UNC/ADS forms according to support policy.
- File/directory mismatch, missing source, locked/access-denied source, read-only item.
- Existing destination and case-only rename collision.
- Protected system or user-configured item.
- Reparse point/symlink/junction in source, destination, or intermediate component.
- Plan changed after approval; policy/root authorization revoked; source metadata changed before execution.
- Duplicate/reordered operation IDs and operations whose combined effects conflict.
- Cancellation before/during scan and between execution operations.
- Partial execution, restart/recovery, repeated execution attempt, and journal write failure.
- Undo when destination changed, old path is occupied, created folder is nonempty, or only part of a plan can reverse.
- Undo never removes a folder that existed before the run, even an empty one, including after an interrupted run is recovered. (Found and fixed 2026-09-10: the demo executor recorded an already-existing folder as created, so undo deleted it if empty.)
- Disconnecting a folder that was tidied succeeds and erases that folder's plans and journal, and never the practice workspace's. (Found and fixed 2026-09-11: the saved tidy history blocked the disconnect with a raw database error after the folder's search memory was already gone.)
- No permanent-delete command exists; blocked operation types remain blocked.

Platform-specific cases may require Windows and privileges. Skip only with an explicit reason and cover policy logic with a platform-neutral fake as well.

## AI Contract Tests

Cover valid structured responses plus malformed/truncated/oversized JSON, unknown schema/enums, invented or duplicate file IDs, absolute/escaping paths, raw commands, prompt injection in names/content, misleading confidence, timeouts, cancellation, authentication/rate limits, and a provider attempting to return more operations than allowed. Verify disclosure filtering before the fake transport and verify logs contain no secret or payload.

V0.3 provider tests use fake transports, fake settings, fake clocks, and fake credential vaults. No automated test contacts OpenRouter or local AI services or writes the developer's credential store. The corpus covers hostile names, extra command fields, duplicate JSON properties/IDs, invented IDs, bad enums/confidence, oversized output, loopback refusal, offline, timeout, cancellation, rate-limit, missing key, disclosure expansion, and daily-cap refusal.

## Index Tests

Index tests must prove refusal as well as storage: traversal, rooted, alternate-data-stream
and oversized paths rejected at construction; protected roots and protected children never
indexed; entry limits and cancellation honoured with nothing written; entries belonging to
another root refused; duplicate file IDs in one refresh refused; an unauthorized root
refused by the foreign key; two roots with identical file names kept separate; no stored
row containing an absolute path; and disconnecting a root erasing its rows. A file held
under an exclusive lock must still index successfully, which proves contents are never
opened.

## Database Tests

Test fresh schema, every supported migration path, foreign keys, transaction rollback, concurrent access policy, enum/version compatibility, retention deletion, interrupted execution records, and that credentials are never stored in tables. Each test uses an isolated database.

## Feature Coverage Map

Every feature a person can reach, and the page tests that use it. A feature missing from this
table, or listed with no test, is not done. Update the table in the same change that adds or
changes a feature.

| Page | Feature | Page tests |
|---|---|---|
| Home | Totals, categories, largest files, last checked | `HomeAndShellTests` |
| Home | First-run welcome: shown once to a brand-new DeskAI and remembered before it opens; not shown to someone with a folder already connected, nor when the memory cannot be read or written; Next, Back, and Done walk three pages; the last page lists only the folders Windows reports (or says none were found) and says whether AI is on; Connect connects only after Home's question and opens Organize; a refused connect says why; Start fresh brings it back; Privacy and AI reopens it on page one without changing what DeskAI remembers | `WelcomePageTests`, `WelcomeLayoutTests`, `FreshStartPageTests`, `WelcomeServiceTests` |
| Home | Ask DeskAI, asked once (ADR 0035 amended): the first question asks and the yes survives reopening; later questions send with no dialog; a send without that yes sends nothing; a different AI service asks again; "Ask me each time" and Start fresh both take the yes back | `AskDeskAiPageTests` |
| Home | Ask DeskAI (ADR 0035): off with AI off; the dialog's words and service; Send sends the question alone; a search question replies with matching names and Open in Search lands the phrase on Search; a space question is answered from the storage summary; a tidy question opens Organize on a connected folder, offers Connect for an unconnected personal folder, or says where DeskAI works; unsure says what can be asked; an off-shape answer is refused; cancel sends nothing | `AskDeskAiPageTests`, `AskDeskAiServiceTests` |
| Home | Ask DeskAI is visibly and accessibly marked BETA beside its heading | `ShellLayoutTests` |
| Home | Greeting by time of day; the four Quick look tiles (folders, files, sitting unused, possible duplicates) read the remembered numbers, and the duplicates caption stays hedged | `HomeAndShellTests`, `ShellLayoutTests` |
| Home | Possible copies (same size, never "confirmed") | `HomeAndShellTests` |
| Home | Check if they're really copies: dialog says what is read first, Cancel reads nothing, identical / different / not checked with reasons, nothing kept, sent, or changed | `CopyCheckPageTests`, `DuplicateCheckTests`, `DuplicateCheckServiceTests`, `FileFingerprinterTests` |
| Home | Health score with its parts | `HomeAndShellTests` |
| Home | Honest wording about what can change | `HomeAndShellTests` |
| Side menu | What is connected and whether files are read | `HomeAndShellTests` |
| Side menu | Notice when a check finds something; opt-in notification | `HomeAndShellTests` |
| Every page | The grouped menu keeps the six names the owner chose, in the top bar's order, under three plain group labels | `ShellLayoutTests`, `ShellPageTests` |
| Every page | Top bar: the page name; "Find a file…" opens Search with the phrase already run, once, and a blank phrase does nothing | `ShellPageTests` |
| Every page | The AI pill says "AI off" until a service is really ready, then names it; pressing it only opens Privacy and AI | `ShellPageTests` |
| Every page | Dark mode switch: saves light or dark for the DeskAI window only, repaints at once, remembered after reopening, shows what the window paints while following Windows | `ShellPageTests` |
| Every page | Tile tints exist in dark, light, and high contrast, are never the accent, and no page paints a tile with anything else | `ShellLayoutTests` |
| Organize, Search, Automatic tasks, My workspace | State pills read the value the page already decides on: Allowed to tidy / Look only, Can read inside / Names, sizes, dates, Paused / the checking frequency, On / Off on a rule, Chosen on a look | `StatePillTests`, `TidyPageTests`, `SearchPageTests`, `AutomationPageTests`, `LookPageTests` |
| Notice | Review in Organize: opens the folder with the most matches, says what its rules place there, asks for permission first if needed, used once, moves nothing | `ReviewInOrganizePageTests` |
| Organize | Pick or connect a folder; protected folder refused | `TidyPageTests` |
| Organize | Tidy permission: asked first, allowed, taken back, erased on disconnect | `TidyPageTests`, `SqliteAuthorizedRootRepositoryTests`, `RootCapabilitiesTests` |
| Organize | Suggestions grouped by folder with reasons; rules win over type | `TidyPageTests`, `TidySuggestionTests` |
| Organize | Untick a group or a file; count and button text follow | `TidyPageTests` |
| Organize | Same name: skip by default, keep both adds a number | `TidyPageTests`, `TidySuggestionTests` |
| Organize | Left alone with reasons: downloading, recent, online-only, hidden, unknown | `TidyPageTests`, `TidySuggestionTests` |
| Organize | Only loose top-level files; at most 500; safety check on every move | `TidySuggestionTests` |
| Organize | Tidy button on only while something is ticked; says it can be undone | `TidyPageTests` |
| Organize | Tidy moves exactly the ticked files; result line; skipped files with reasons | `TidyRunPageTests`, `TidyRunTests` |
| Organize | Re-checked before each file: changed, replaced, gone, name taken, busy, online-only, link, permission withdrawn, disconnected, protected, escaping plan, stale approval | `TidyRunTests`, `FolderTidyExecutorTests` |
| Organize | Undo: files back, folders that were there kept, changed or blocked files refused, once only, asks for permission again | `TidyRunPageTests`, `TidyRunTests` |
| Organize | An old practice folder left in a database is never tidied or undone | `TidyRunTests`, `TidyRecoveryTests` |
| Organize | How tidying works: four steps and the promise, open for someone new, closed once a folder may be tidied | `TidyPageTests` |
| Organize | Last tidy found again after reopening, with Undo; only the latest; asks for permission first; once only | `TidyRecoveryPageTests`, `TidyRecoveryTests` |
| Organize | Interrupted tidy: checked file by file, "N of M moved", Undo those / Keep them, OK when nothing moved, files to check listed, Tidy off until answered | `TidyRecoveryPageTests`, `TidyRecoveryTests` |
| Organize | Interrupted undo: how many went back, OK only, never offered again | `TidyRecoveryPageTests`, `TidyRecoveryTests` |
| Organize | Recovery never guesses or reaches too far: changed file or link needs review, folder moved or unsafe not checked, other folders and practice refused, one run at a time across windows | `TidyRecoveryTests`, `FolderTidyExecutorTests` |
| Search | A tidied folder can be disconnected; its tidy history is forgotten, an old practice folder's is not | `TidyRunPageTests`, `SqliteAuthorizedRootRepositoryTests` |
| Side menu | Says which folders DeskAI may tidy | `HomeAndShellTests` |
| Organize | Ask AI: off until set up, preview before Send, cancel sends nothing, answer beside the button | `TidyAiPageTests`, `TidyAiTests` |
| Organize | Plan this folder with AI (ADR 0034): asks about every file rules do not place, the AI's folder names become the groups, Tidy makes exactly those folders and undo puts files back, a name outside this folder refuses the whole plan, rules still win; parser refuses a folder on a classification, a missing or bad name, or more than 12 folders | `TidyAiPageTests`, `PlanFolderParserTests` |
| Organize | Only files DeskAI doesn't know, or every file rules don't place; rules still win | `TidyAiPageTests`, `TidyAiTests` |
| Organize | AI isn't sure: own group, unticked; refused answers change nothing | `TidyAiPageTests`, `TidyAiTests` |
| Organize | What AI may see: no names unless allowed, no locations, no file IDs, no left-alone or protected files; re-checked before Send | `TidyAiTests`, `PlanSafetyCheckTests`, `TidyAiServiceTests` |
| Search | Connect, refresh, disconnect, protected-folder refusal | `SearchPageTests` |
| Search | Typed search, chips, scope, not-understood, nothing matched | `SearchPageTests` |
| Search | Let AI read this (ADR 0033): no button with AI off; the dialog's words and service; Send sends the words alone (no file names, folder names, or locations); the reading replaces the phrase and is searched; cancel sends nothing; an off-shape answer, a service refusal, the daily limit, and a changed AI choice each leave the phrase alone with a reason | `SentenceAiPageTests`, `SentenceAiServiceTests`, `AiSentenceReadingTests`, `SentenceReadingProviderTests` |
| Search | Saved searches | `SearchPageTests` |
| Search | Reading inside text files: allow, search, withdraw | `SearchPageTests` |
| Search | Reopened page shows a connected-folder status, never the contradictory "No folders connected" text (owner-found 2026-09-20; failing page test before fix) | `SearchPageTests` |
| Search | Separate modern Word/Excel consent, natural-language content word, result when name does not match, readable snippet and honest count; old plain-text grant cannot open Office files | `SearchPageTests`, `ConnectedFolderServiceTests`, `ContentSearchServiceTests`, `PlainTextExtractorTests`, `RootCapabilitiesTests`, `SqliteAuthorizedRootRepositoryTests` |
| Search | Separate PDF consent after Office reading; a generated text PDF matches only after that yes and stops after revocation; old grants, malformed and oversized PDFs stay unreadable | `SearchPageTests`, `PlainTextExtractorTests`, `RootCapabilitiesTests` |
| Search | A PDF added in a subfolder appears after Refresh and PDF consent; Files checked distinguishes a matching PDF, a read PDF with no matching word, and a PDF that could not be read (owner-found ambiguity, 2026-09-20; the page test failed before the explanation was added) | `SearchPageTests` |
| Search | PowerPoint files, including a generated 9 MB deck and a file two subfolders down, all match slide text only after the separate grant; result identifies Slide 2; PDF text after page 20 is found; layout whitespace inside a word does not hide it; revoking slides keeps PDF permission; hostile XML, containers over 32 MB, and media entries remain closed | `SearchPageTests`, `ContentSearchServiceTests`, `PlainTextExtractorTests`, `RootCapabilitiesTests`, `SqliteAuthorizedRootRepositoryTests` |
| Search | Generated nested PPTX and PDF pictures offer slide/page evidence; no transport before a fresh cloud Send; Cancel refuses; a changed AI choice refuses; local AI uses loopback and separate read choice; picture bytes go without file names or paths | `VisualSearchPageTests` |
| Search | Launch build keeps picture reading unavailable even when AI is configured; the Search page has no picture-search entry point | `SearchPageTests` |
| Shell | Published startup explicitly shows, activates, and foregrounds the main window instead of leaving an invisible healthy process | `ShellLayoutTests` |
| Search | A generated 21-page PDF with the searched word only on page 21 appears in Files checked as partly read, with the 20-page or 64-KB text limit stated (owner-found ambiguity, 2026-09-21; page test failed before wording fix) | `SearchPageTests` |
| Search | A flattened PDF is not OCR-read by normal search or after cancel; one-run approval finds a word on its reported page without any AI/network request | `SearchPageTests` |
| Search | "Look in" limits both name and inside-file matches to the selected connected folder; a query cannot select a path itself | `SearchPageTests`, `ContentSearchServiceTests` |
| Search | Looking deeper and further (ADR 0041): a photo six folders down is found; a look that stops at its item limit says so on the folder row and in the message, and a file remembered earlier is still found (failed before the fix); "Nothing matched" in a partly checked folder says a file may be missing; a folder too deep to enter is counted; opening Search more than 10 minutes after the last look finds a new file without Refresh, and opening it again sooner does not look again | `SearchPageTests`, `SqliteFileIndexTests`, `MetadataIndexServiceTests`, `SqliteDatabaseInitializerTests` |
| Desktop Studio | Find groups (ADR 0042): no Desktop → Connect only, nothing sent or saved; Connect Desktop connects it for names only; AI off → DeskAI's guess, labelled, nothing sent; the Send window lists exactly what is sent and nothing goes before Send; the answer becomes the board; no folder-name sharing → nothing sent and the page says what to allow; an off-shape answer (a group named "..\Windows") is refused and the old board kept; rename, merge, move survive reopening and a bad name is refused; a deleted folder drops off with a note; an empty Desktop says so; disconnect forgets the board; DeskAI's own program folder never appears; no file on the generated Desktop changes; a folder too big to look inside fully (5,010 generated files) still lets the Desktop be sorted and says so (failed before the review fix); long non-English names never make Send refuse the approved list (failed before the fix); the board keeps naming the AI that made it after AI is changed (failed before the fix); hidden files inside folders are neither counted nor named; every group and Not sure say how many items they hold; group cards are one size with long lists scrolling inside, Rename and Merge sit in one small menu, and Not sure spans the width in columns with a height limit (owner-found layout) | `DesktopStudioPageTests`, `DesktopStudioLayoutTests`, `DesktopGroupingServiceTests`, `DesktopGroupReadingTests`, `LocalDesktopGrouperTests`, `DesktopLookServiceTests`, `GroupItemsProviderTests`, `ConfiguredSuggestionProviderTests`, `SqliteDesktopGroupRepositoryTests`, `SqliteDatabaseInitializerTests` |
| Desktop Studio | Clear old stuff and Folder by group (ADR 0044): the preview lists only things unchanged for 6 months (a folder by the newest thing inside it) and moves nothing; Move asks for its own yes first (the tidy yes is not enough, and it grants no tidying), then moves only the ticked things; the total reads "1 folder holding 2 files, plus 1 file"; Put back returns everything and removes only an Old stuff folder DeskAI made, also after reopening; project and program folders start unticked with a warning; an Old stuff folder already there is used and kept, and a same-name clash is left alone with its reason; a folder changed after the list stays with its reason; Folder by group puts each group into its own folder, leaves Not sure, sends nothing, and says to find groups first when there are none; Stop takes the yes back; Organize offers no undo for a Desktop Studio change; a change interrupted part-way is asked about after reopening and Put them back returns it; DeskAI's program folder and hidden things never appear or move; each card has help, tick boxes, a total, and Put back; after the whole-change review (each failed before its fix): a part-way change is answered only on the page that made it, with that page's own permission, and the question stays open when the other page is used; when the look stops at its item limit, folders it had finished are still offered; nothing moves into a hidden or protected folder that has the destination's name; the page, dialog, and help never promise Put back for more than the latest change; the page reads as steps (1. Find groups, the board, 2. Put each group in its own folder, then Other tidy-ups), owner-found | `DesktopStudioMovePageTests`, `DesktopMoveServiceTests`, `DesktopStudioLayoutTests`, `DesktopMovePlannerTests`, `DesktopInventoryServiceTests`, `FolderTidyExecutorTests`, `PlanValidatorTests`, `OrganizationPlanTests`, `RootCapabilitiesTests`, `SqlitePlanningPersistenceTests`, `SqliteAuthorizedRootRepositoryTests`, `SqliteDatabaseInitializerTests`, `WindowsMetadataScannerTests` |
| Desktop Studio | Tag names (ADR 0045): the preview lists each folder in a group with its new name ("becomes "Coding – Python stuff"") and never a file; Rename needs the same yes; Put back restores the old names, also after reopening; a folder already named with its group is left alone; a taken name, a too-long name, and a project folder are handled before anything moves; before Find groups it asks for groups in its own words; the card sits with the group steps, before Other tidy-ups; after the whole-change review (each failed before its fix): renamed folders stay in their group on the board, also after reopening and after Put back, and a second press leaves them alone instead of calling them gone; a hidden file on the Desktop counts as a name already in use | `DesktopStudioMovePageTests`, `DesktopMoveServiceTests`, `DesktopStudioLayoutTests`, `DesktopMovePlannerTests`, `DesktopInventoryServiceTests` |
| Desktop Studio | The cards together (end-to-end check 2026-09-25; each failed before its fix): after Put each group in its own folder the board keeps the groups (and a renamed group's name), each showing its new folder, also after reopening, with nothing under Not sure; Put back returns each thing to its group and drops a folder it removed, also after reopening; after a Move or a Put back on one card, the other cards' lists are cleared with "Your Desktop changed…"; when nothing on a list is still there, no empty folder is left behind; Tag names leaves a group's own folder its name; Clear old stuff after Folder by group says it doesn't look inside group folders (and finds the old things again after Put back), and the group step suggests clearing old stuff first | `DesktopStudioMovePageTests`, `DesktopBoardFollowTests` |
| Shell | The menu and the top bar list Desktop Studio after Automatic tasks, in the same order | `ShellPageTests`, `ShellLayoutTests` |
| Automatic tasks | Write, draft from a sentence, turn off, delete rules | `AutomationPageTests` |
| Automatic tasks | Let AI read this (ADR 0033): the words alone are sent after the dialog; the reading fills the boxes as a typed sentence would and saves nothing; a destination outside the folder is refused whole | `SentenceAiPageTests`, `AiSentenceReadingTests` |
| Automatic tasks | Practice run (moves nothing) | `AutomationPageTests` |
| Automatic tasks | Check now, history, how often, pause, notifications | `AutomationPageTests` |
| Automatic tasks | Keep checking after the window is closed: asked first, stores nothing until yes, survives reopening, off again at once | `BackgroundCheckingPageTests` |
| Automatic tasks | The icon near the clock: appears when turned on, says how often or paused, never a file name | `BackgroundCheckingPageTests`, `BackgroundCheckingChoiceTests` |
| Automatic tasks | Where the icon is: the switch caption, the dialog, the still-running notice, and the help topic all say to click the arrow for hidden icons | `BackgroundCheckingPageTests` |
| Automatic tasks | The icon near the clock is DeskAI's logo, not Windows' generic program icon (owner-found 2026-09-24; failed before the fix) | `ShellLayoutTests` |
| Automatic tasks | Pause from the icon; the page and the icon never disagree | `BackgroundCheckingPageTests` |
| Automatic tasks | Wording follows the mode: never claims checking stops on close while it does not, always says nothing moves by itself, always says no Windows startup | `BackgroundCheckingPageTests`, `BackgroundCheckingChoiceTests` |
| Whole app | Never registers itself to start with Windows | `NeverStartsWithWindowsTests` |
| Organize | Tidy while I'm away: the switch is off and disabled until tidying is allowed and a rule is on; the dialog names the folder, the rules, and the 25-file ceiling | `AwayTidyPageTests` |
| Organize | After the yes a check moves only rule-placed loose files (never type-placed, AI, subfolder, or clashing files), at most 25 per run; the away card, the notice, and Undo follow, also after reopening; Got it clears the card | `AwayTidyPageTests` |
| Organize | A same-name clash stops the run before anything moves; a rule added, edited, toggled, or removed turns the mode off with the reason before the next run and when the folder is shown; withdrawing tidy permission ends it; a busy file stays and the mode stops after the run | `AwayTidyPageTests` |
| Home, Automatic tasks | Every "nothing moves by itself" promise follows the mode: pill, hero sentence, first card, checking summary, keep-running dialog, More details | `AwayTidyPageTests`, `BackgroundCheckingPageTests` |
| Whole app | Start fresh and disconnecting end the mode; the away service is the only unattended path, holds no AI, reader, fingerprinter, credential, or file store, and the check service still holds no executor | `AwayTidyPageTests`, `FreshStartPageTests` |
| Whole app | Launching DeskAI again reveals the running one rather than starting a second | `SingleInstanceDecisionTests` |
| My workspace | Starter packs: five cards, no Custom; preview lists every search and rule and adds nothing; Add writes what happened on that card; names already used are skipped and named, never replaced; adding twice adds nothing; the 50-search limit | `WorkspacePageTests`, `StarterPackServiceTests`, `StarterPackCatalogTests` |
| My workspace | A pack's rules arrive Off, change neither Tidy nor a check until turned on, and move no file | `WorkspacePageTests`, `StarterPackServiceTests`, `StarterPackCatalogTests` |
| My workspace | Every pack search means what its name says (each phrase checked against the translator) | `StarterPackCatalogTests` |
| My workspace | Pinned tiles: count, "No folders connected", "Search not understood", "200+" at the limit, no file names; pin up to eight, refused beside the list, unpin; the big number slot shows only a number and words go on a wrapping caption (owner-found clip, 2026-09-16) | `WorkspacePageTests`, `ShellLayoutTests`, `PinnedSearchServiceTests`, `SqliteSavedSearchRepositoryTests`, `SqliteDatabaseInitializerTests` |
| My workspace | Open in Search lands on that saved search's results; used once; a removed search is said to be gone | `WorkspacePageTests` |
| My workspace | Nothing here can reach a file, a credential, or AI | `StarterPackServiceTests`, `PinnedSearchServiceTests` |
| My workspace | Folder templates: six cards (five packs and "Your own folders"); with nothing connected the section points to Organize; the preview lists new, already there (by the name on disk), and can't-be-made folders and changes nothing; without the tidy permission it asks first and looks at nothing | `FolderTemplatePageTests`, `FolderTemplateServiceTests`, `FolderNameLookupTests` |
| My workspace | Make makes exactly the listed folders, never re-makes one already there, names each folder not made with its reason, and writes the result on that card; the approval covers exactly the folders shown; the folder changing between preview and Make is refused with a fresh list | `FolderTemplatePageTests`, `FolderTemplateServiceTests` |
| My workspace | Typed folder names: separators, drive letters, traversal, device names, trailing dots, more than 8, duplicates, and blanks refused with a reason and nothing looked at; accepted names made; every accepted name also passes the path policy | `FolderTemplatePageTests`, `FolderNameCheckTests`, `FolderTemplatePolicyTests` |
| My workspace | Undo removes only the empty folders DeskAI made, leaves one that gained a file with a reason, survives reopening, needs the tidy permission again, and is taken off the page once a tidy ran after it | `FolderTemplatePageTests`, `FolderTemplateServiceTests`, `FolderTidyExecutorTests` |
| My workspace / Organize | A template run that stopped part-way is described on Organize as folders ("1 of 2 folders made"), can be removed or kept, and blocks templates in that folder until answered; a folder-only record never settles as "0 of 0 files" | `FolderTemplatePageTests`, `FolderTidyExecutorTests` |
| My workspace | Every catalog folder name passes the real path policy; each pack rule's destination is in its template; adding a pack still makes no folder; no Workspace type holds an executor; the template service holds no scanner, reader, credential, rule evaluator, or AI | `FolderTemplatePolicyTests`, `FolderTemplateCatalogTests`, `FolderTemplatePageTests`, `FolderTemplateServiceTests` |
| My workspace | DeskAI's look: six looks with Slate chosen and Follow Windows by default; choosing a look repaints the window at once, marks it chosen, and is remembered after reopening; light / dark / follow Windows; choosing the same again does nothing; a made-up look changes nothing; a stored look that no longer exists shows as Slate | `LookPageTests`, `SqliteAppearanceSettingsRepositoryTests` |
| My workspace | A look can change only the neutral colours, never the accent, caution, or danger colour; every look keeps the shared text readable (7:1 primary, 4.5:1 secondary) on its ground and surfaces; every colour is six-digit hex | `DeskLookCatalogTests` |
| My workspace | Wallpaper: choosing a picture shows its name and what Windows shows now and changes nothing; Use as wallpaper sets it, writes the old one down first, and offers Put back, also after reopening; Put back restores it (or a plain colour) and clears the offer; a wallpaper changed in Windows since is said beside Put back; Windows refusing leaves no offer behind | `DesktopAndWallpaperPageTests`, `WallpaperServiceTests`, `DesktopAdapterTests` |
| My workspace | Wallpaper refusals with a reason: nothing chosen, a network path, a URL, a relative path, a non-picture extension, a missing file, a link, a folder, an empty file, a file over 50 MB; the file going missing between preview and use | `DesktopAndWallpaperPageTests`, `WallpaperServiceTests` |
| My workspace | Wallpaper containment: the service holds only the setter, the inspector, and the store; nothing that runs with no window (checks, presence) can take the setter; no registry API in any source file | `WallpaperServiceTests`, `AutomaticCheckServiceTests`, `NeverStartsWithWindowsTests` |
| Home / My workspace | Your folders card (ADR 0032): Desktop, Downloads, Documents, Pictures as rows with "Not connected yet." and Connect; connecting one remembers names only (no content, no tidy permission), hands it to Organize, which asks permission first, and the row then says "Connected. Tidy it in Organize." with Tidy; "Connected and allowed to tidy." with the pill once allowed; pressing again reuses it; shortcuts (`.lnk`, `.url`) are left alone; nothing moves until Tidy; a folder Windows lacks is left off and named in the refusal; none at all shows the "could not find" line; a folder connected on My workspace joins the template list | `YourFoldersPageTests`, `PersonalFolderPolicyTests` |
| Search / Organize | Only the four and folders inside them: a picked folder outside is refused with an actionable route to Home on both pages and nothing is connected; a folder inside one is accepted; a connected folder no longer inside the four cannot be allowed for tidying | `YourFoldersPageTests`, `PersonalFolderPolicyTests`, `ReadOnlyFolderServiceTests` |
| Home / Search | A Desktop that contains DeskAI's own protected program folder still connects and that folder's files are never remembered (owner-found bug, 2026-09-16); a folder inside the program folder is still refused as protected | `YourFoldersPageTests`, `WindowsPathPolicyTests`, `ReadOnlyFolderServiceTests` |
| Whole app | No test can touch the real wallpaper or the real Desktop, Downloads, Documents, or Pictures: all are replaced in `TestApp`, the four test folders are asserted to sit inside the test's own folder and outside the real ones, and every test's Desktop contains a protected "program folder", as the owner's does | `YourFoldersPageTests`, `DesktopAndWallpaperPageTests`, `TestApp` |
| Privacy and AI | Sharing choices, AI modes, key storage and removal, daily limit | `SettingsPageTests`, `AiJourneyTests` |
| Privacy and AI | Pasted key trimmed, spaced key refused, wrong-looking key warned | `SettingsPageTests` |
| Privacy and AI | "Check this now": says hello to the saved AI and reports what answered; refusal, missing key, unreachable local app, unreadable reply, and the daily cap each explained; the result is cleared when the choice changes or the key is removed | `SettingsCheckPageTests`, `AiConnectionCheckTests` |
| Privacy and AI | The check carries nothing about the computer, whatever the sharing choices allow, and never repeats the key back onto the page | `SettingsCheckPageTests` |
| Privacy and AI | An empty model name or local address is refused in words a person can act on, never a framework sentence about a parameter | `SettingsCheckPageTests` |
| Privacy and AI | Back up: the file holds rules and saved searches and no folder, path, key, or setting; the status says so | `BackupPageTests` |
| Privacy and AI | Restore: preview adds nothing and names what is skipped and why; restore adds once with new IDs, rules Off, names already used skipped; a second restore adds nothing; pins kept only while there is room | `BackupPageTests` |
| Privacy and AI | Restore refuses a file that is not a backup, a newer version, a non-.json file, and an oversized file in plain words; a hostile or unknown rule in the file is skipped with a reason and the rest restored | `BackupPageTests` |
| Privacy and AI | Start fresh forgets every folder, rule, search, key, AI choice, check history and setting, look, wallpaper memory, and that the welcome was shown, hides the icon, and touches no file; the backup and fresh-start services hold nothing that reaches a file | `FreshStartPageTests`, `BackupPageTests` |
| Privacy and AI | The version line | `FreshStartPageTests` |
| Every page | Every button without visible text and every box, list, and switch carries a name for screen readers | `AccessibilityNameTests` |
| Whole app | Performance probe on a few thousand generated files (opt-in, `DESKAI_PERF`), recorded in `docs/PERFORMANCE.md` | `PerformanceProbe` |
| Organize | Ask AI after turning AI on: one request to the chosen service, rejected key in plain words, removing the key stops it, daily limit | `AiJourneyTests`, `SettingsPageTests`, `CloudSuggestionProviderTests` |
| Every page | "?" help next to each feature: complete, short, no jargon, placed | `HelpCatalogTests`, `HelpPlacementTests` |
| Every page | Calm-workspace layout: shared introductions; task tabs on My workspace and Privacy and AI; responsive shell; Home centered in the visible wide viewport; the DeskAI icon applied to the executable and live title bar; normal and startup-recovery windows explicitly shown and foregrounded; unpackaged publish retains the app PRI and compiled XAML and the release workflow refuses an incomplete interface; an opt-in visual preview that replaces personal folders, keys, network, wallpaper, pickers, notifications, tray, and timers | `ShellLayoutTests` |
| Quick search | The bar (ADR 0047): the buddy's greeting and three examples; typing finds by name, then words inside only where Search allows reading inside, never listing a file twice; up to 5 each, with See more; no folders, nothing matched, and reading-inside-not-allowed each say so plainly; Up/Down, Enter, Ctrl + Enter; a program or unknown kind is only shown in its folder; a file that went away or Windows refusing keeps the bar open with the reason; each buddy speaks in its own voice; clicking the buddy; a new keystroke or hiding stops a slow inside look; nothing typed or found is remembered | `QuickSearchPageTests`, `QuickSearchServiceTests`, `QuickSearchLayoutTests` |
| Quick search | Opening a file safely: the live file is re-checked (disconnected folder, path leaving the folder, protected place, link on the way, gone, renamed to a program); only the bar holds the launcher and only the launcher can start anything | `WindowsFileLauncherTests`, `QuickSearchContainmentTests` |
| My workspace | Quick search card: the switch starts and stops the shortcut and is kept; seven buddies, the choice kept; a Shortcut drop-down (Ctrl + Alt + D by default, Ctrl + Alt + Space, Ctrl + Shift + Space) listens for the choice at once, names it on the switch and the icon near the clock, is kept, and is only remembered while off; picking the one already in use changes nothing; a shortcut another program uses is named with "Pick another shortcut above", nothing listens, and turning the switch off and on asks again; the icon near the clock offers Find a file, and Pause only while checking; closing keeps DeskAI near the clock only while the icon really shows; "Let my buddy move" is on by default, turns every buddy still at once, is kept, and wins over Windows' Animation effects; Start fresh turns it back on with Sparky, Ctrl + Alt + D, and moving buddies and forgets an old tip mark; each shortcut is one fixed combination, one at a time, never a keyboard hook | `QuickSearchSettingsPageTests`, `QuickSearchLayoutTests`, `FreshStartPageTests`, `QuickSearchHotKeysTests`, `QuickSearchSettingsServiceTests` |
| Home, Search | No quick search tip (removed at the owner's request 2026-09-25: not needed, and on Home it covered the welcome panel; the page test failed before the removal) | `QuickSearchLayoutTests` |
| Home | First-run welcome: the quick search page (Sparky and the shortcut the person picked) comes before the last page | `QuickSearchWelcomePageTests`, `WelcomePageTests` |

## Quality Gates

For each milestone, and for each task inside one:

- every user-visible feature added or changed has a page test that uses it the way a person
  does, and the Feature Coverage Map above lists it;
- every bug the owner finds by hand gets a page test that fails before the fix and passes
  after it;
- solution builds with no unexplained warnings;
- focused and full relevant tests pass;
- new safety branch has positive and negative coverage;
- no test touches real personal data;
- manual exploratory checklist covers new user-visible workflow;
- accessibility checks cover keyboard/focus/name/contrast for affected UI;
- documentation and status match actual behavior.

Do not optimize for a coverage percentage alone. Branches that authorize or mutate deserve exhaustive examples and, later, property-based/fuzz testing. Track performance separately with representative synthetic trees so benchmarks do not make normal tests flaky.

## Manual Release Checklist

- Clean install and first launch with no account/network.
- Rule-only organizer demo in a controlled folder.
- Permissions and protected items are understandable.
- Cloud remains off until configured; provider/disclosure indicator is accurate.
- Preview matches exact execution; collision and failure UX are honest.
- History and undo work after restart.
- Logs/database contain no API key or unintended content.
- Update/uninstall preserves or removes local data according to stated user choice.
