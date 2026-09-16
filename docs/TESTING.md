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
| Organize | Only files DeskAI doesn't know, or every file rules don't place; rules still win | `TidyAiPageTests`, `TidyAiTests` |
| Organize | AI isn't sure: own group, unticked; refused answers change nothing | `TidyAiPageTests`, `TidyAiTests` |
| Organize | What AI may see: no names unless allowed, no locations, no file IDs, no left-alone or protected files; re-checked before Send | `TidyAiTests`, `PlanSafetyCheckTests`, `TidyAiServiceTests` |
| Search | Connect, refresh, disconnect, protected-folder refusal | `SearchPageTests` |
| Search | Typed search, chips, scope, not-understood, nothing matched | `SearchPageTests` |
| Search | Saved searches | `SearchPageTests` |
| Search | Reading inside text files: allow, search, withdraw | `SearchPageTests` |
| Automatic tasks | Write, draft from a sentence, turn off, delete rules | `AutomationPageTests` |
| Automatic tasks | Practice run (moves nothing) | `AutomationPageTests` |
| Automatic tasks | Check now, history, how often, pause, notifications | `AutomationPageTests` |
| Automatic tasks | Keep checking after the window is closed: asked first, stores nothing until yes, survives reopening, off again at once | `BackgroundCheckingPageTests` |
| Automatic tasks | The icon near the clock: appears when turned on, says how often or paused, never a file name | `BackgroundCheckingPageTests`, `BackgroundCheckingChoiceTests` |
| Automatic tasks | Pause from the icon; the page and the icon never disagree | `BackgroundCheckingPageTests` |
| Automatic tasks | Wording follows the mode: never claims checking stops on close while it does not, always says nothing moves by itself, always says no Windows startup | `BackgroundCheckingPageTests`, `BackgroundCheckingChoiceTests` |
| Whole app | Never registers itself to start with Windows | `NeverStartsWithWindowsTests` |
| Whole app | Launching DeskAI again reveals the running one rather than starting a second | `SingleInstanceDecisionTests` |
| My workspace | Starter packs: five cards, no Custom; preview lists every search and rule and adds nothing; Add writes what happened on that card; names already used are skipped and named, never replaced; adding twice adds nothing; the 50-search limit | `WorkspacePageTests`, `StarterPackServiceTests`, `StarterPackCatalogTests` |
| My workspace | A pack's rules arrive Off, change neither Tidy nor a check until turned on, and move no file | `WorkspacePageTests`, `StarterPackServiceTests`, `StarterPackCatalogTests` |
| My workspace | Every pack search means what its name says (each phrase checked against the translator) | `StarterPackCatalogTests` |
| My workspace | Pinned tiles: count, "No folders connected", "Search not understood", "200+" at the limit, no file names; pin up to eight, refused beside the list, unpin | `WorkspacePageTests`, `PinnedSearchServiceTests`, `SqliteSavedSearchRepositoryTests`, `SqliteDatabaseInitializerTests` |
| My workspace | Open in Search lands on that saved search's results; used once; a removed search is said to be gone | `WorkspacePageTests` |
| My workspace | Nothing here can reach a file, a credential, or AI | `StarterPackServiceTests`, `PinnedSearchServiceTests` |
| My workspace | Folder templates: six cards (five packs and "Your own folders"); with nothing connected the section points to Organize; the preview lists new, already there (by the name on disk), and can't-be-made folders and changes nothing; without the tidy permission it asks first and looks at nothing | `FolderTemplatePageTests`, `FolderTemplateServiceTests`, `FolderNameLookupTests` |
| My workspace | Make makes exactly the listed folders, never re-makes one already there, names each folder not made with its reason, and writes the result on that card; the approval covers exactly the folders shown; the folder changing between preview and Make is refused with a fresh list | `FolderTemplatePageTests`, `FolderTemplateServiceTests` |
| My workspace | Typed folder names: separators, drive letters, traversal, device names, trailing dots, more than 8, duplicates, and blanks refused with a reason and nothing looked at; accepted names made; every accepted name also passes the path policy | `FolderTemplatePageTests`, `FolderNameCheckTests`, `FolderTemplatePolicyTests` |
| My workspace | Undo removes only the empty folders DeskAI made, leaves one that gained a file with a reason, survives reopening, needs the tidy permission again, and is taken off the page once a tidy ran after it | `FolderTemplatePageTests`, `FolderTemplateServiceTests`, `FolderTidyExecutorTests` |
| My workspace / Organize | A template run that stopped part-way is described on Organize as folders ("1 of 2 folders made"), can be removed or kept, and blocks templates in that folder until answered; a folder-only record never settles as "0 of 0 files" | `FolderTemplatePageTests`, `FolderTidyExecutorTests` |
| My workspace | Every catalog folder name passes the real path policy; each pack rule's destination is in its template; adding a pack still makes no folder; no Workspace type holds an executor; the template service holds no scanner, reader, credential, rule evaluator, or AI | `FolderTemplatePolicyTests`, `FolderTemplateCatalogTests`, `FolderTemplatePageTests`, `FolderTemplateServiceTests` |
| My workspace | DeskAI's look: four looks with Slate chosen and Follow Windows by default; choosing a look repaints the window at once, marks it chosen, and is remembered after reopening; light / dark / follow Windows; choosing the same again does nothing; a made-up look changes nothing; a stored look that no longer exists shows as Slate | `LookPageTests`, `SqliteAppearanceSettingsRepositoryTests` |
| My workspace | A look can change only the neutral colours, never the accent, caution, or danger colour; every look keeps the shared text readable (7:1 primary, 4.5:1 secondary) on its ground and surfaces; every colour is six-digit hex | `DeskLookCatalogTests` |
| My workspace | Wallpaper: choosing a picture shows its name and what Windows shows now and changes nothing; Use as wallpaper sets it, writes the old one down first, and offers Put back, also after reopening; Put back restores it (or a plain colour) and clears the offer; a wallpaper changed in Windows since is said beside Put back; Windows refusing leaves no offer behind | `DesktopAndWallpaperPageTests`, `WallpaperServiceTests`, `DesktopAdapterTests` |
| My workspace | Wallpaper refusals with a reason: nothing chosen, a network path, a URL, a relative path, a non-picture extension, a missing file, a link, a folder, an empty file, a file over 50 MB; the file going missing between preview and use | `DesktopAndWallpaperPageTests`, `WallpaperServiceTests` |
| My workspace | Wallpaper containment: the service holds only the setter, the inspector, and the store; nothing that runs with no window (checks, presence) can take the setter; no registry API in any source file | `WallpaperServiceTests`, `AutomaticCheckServiceTests`, `NeverStartsWithWindowsTests` |
| My workspace / Organize | Tidy my Desktop: connects the Desktop found through the known-folder API for names only (no content, no tidy permission), hands it to Organize, which asks permission first; pressing again reuses it; shortcuts (`.lnk`, `.url`) are left alone; nothing moves until Tidy; no Desktop folder means the button is off with a reason | `DesktopAndWallpaperPageTests` |
| Whole app | No test can touch the real wallpaper or the real Desktop: both are replaced in `TestApp`, and the test Desktop is asserted to sit inside the test's own folder and differ from the real one | `DesktopAndWallpaperPageTests`, `TestApp` |
| Privacy and AI | Sharing choices, AI modes, key storage and removal, daily limit | `SettingsPageTests`, `AiJourneyTests` |
| Privacy and AI | Pasted key trimmed, spaced key refused, wrong-looking key warned | `SettingsPageTests` |
| Privacy and AI | Back up: the file holds rules and saved searches and no folder, path, key, or setting; the status says so | `BackupPageTests` |
| Privacy and AI | Restore: preview adds nothing and names what is skipped and why; restore adds once with new IDs, rules Off, names already used skipped; a second restore adds nothing; pins kept only while there is room | `BackupPageTests` |
| Privacy and AI | Restore refuses a file that is not a backup, a newer version, a non-.json file, and an oversized file in plain words; a hostile or unknown rule in the file is skipped with a reason and the rest restored | `BackupPageTests` |
| Privacy and AI | Start fresh forgets every folder, rule, search, key, AI choice, check history and setting, look, and wallpaper memory, hides the icon, and touches no file; the backup and fresh-start services hold nothing that reaches a file | `FreshStartPageTests`, `BackupPageTests` |
| Privacy and AI | The version line | `FreshStartPageTests` |
| Every page | Every button without visible text and every box, list, and switch carries a name for screen readers | `AccessibilityNameTests` |
| Whole app | Performance probe on a few thousand generated files (opt-in, `DESKAI_PERF`), recorded in `docs/PERFORMANCE.md` | `PerformanceProbe` |
| Organize | Ask AI after turning AI on: one request to the chosen service, rejected key in plain words, removing the key stops it, daily limit | `AiJourneyTests`, `SettingsPageTests`, `CloudSuggestionProviderTests` |
| Every page | "?" help next to each feature: complete, short, no jargon, placed | `HelpCatalogTests`, `HelpPlacementTests` |

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
