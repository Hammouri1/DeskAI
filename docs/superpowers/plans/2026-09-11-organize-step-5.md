# Organize Step 5 — Review in Organize, and Retiring the Practice Page

**Goal:** When an automatic check finds files that match the person's rules, its notice offers
**Review in Organize**, which opens Tidy a folder on the folder where the matches are, with the
list ready. Nothing moves until the person presses Tidy. The practice page is removed; Organize
gets a small card explaining how tidying works instead.

**Spec:** `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md` Part 1
("Automatic checks", "What leaves the page") and build order step 5, amended below.

## Owner decision, 2026-09-11

The spec kept the practice run on generated files behind "Nervous? Try it on example files
first", in a simplified view. Asked how to simplify it, the owner chose to **remove the whole
practice page** and add "a small card that shows the user how to use the Organize page". The
practice page's "Get AI ideas" card goes with it; AI is tried on a real folder through Ask AI,
which shows exactly what is sent before anything is.

## Decisions

- **The check names a folder, nothing more.** `AutomaticCheckResult.FolderToReview` is the ID of
  the connected folder with the most matches. It is still nothing that can be carried out: no
  plan, no operation, no path. The check keeps holding no executor.
- **Review in Organize is navigation.** The shell leaves the folder ID with `OrganizeRequest`,
  the window opens a fresh Organize page, and the page selects that folder. If it has since been
  disconnected, the page opens on the first folder as usual. The notice keeps a quieter link to
  Automatic tasks.
- **Honest about what Organize shows.** Rules in a check look at every remembered file, but
  tidying only moves loose files at the top of a folder. So the page says how many files its
  rules place *here*, instead of repeating the check's number.
- **The practice page and everything only it used are removed:** `PracticePage`,
  `PracticeViewModel`, `DemoOrganizationPlanFactory`, the preview-row view models, the status
  converters, and `TemporaryDemoPlanExecutor` with `IPlanExecutor`, `IUndoService`, and
  `DemoWorkspaceOptions`. An executor that nothing in the app can reach is still code that can
  move files, so it goes rather than lingering. `RootAuthorizationScope.ControlledDemo` stays:
  scopes are stored as numbers, old databases can hold one, and it must keep meaning "never
  tidied, never searched".
- **No safety test is lost with it.** The practice executor's tests that exercise the shared move
  rules and are not already covered through the real-folder executor are ported to it first:
  a failing journal moves nothing, and an approval naming an unknown operation moves nothing.
  Tests that only concerned the practice workspace (its marker, its temp base, its recovery) go
  with it. Tests that proved the two executors kept apart become tests that an old practice
  folder in the database can never be tidied or undone.
- **AI journey tests move to Ask AI** on Tidy a folder: one request to the chosen service, only
  what was agreed sent, AI off sends nothing, a rejected key in plain words, and removing the key
  stops sending.
- **Wording that named practice changes:** the side menu and Home say "Nothing connected yet"
  instead of "Practice mode"; Home's "Practice organizing" card becomes a pointer to Organize.

## Threat check

Review in Organize adds no capability: it selects a folder in a page the person could open
themselves, and Tidy still needs the folder's permission and a press. Removing the practice
executor removes a way to move files. No AI, permission, or path code changes.

## Tasks (one commit each)

1. Plan (this document).
2. **Review in Organize:** `FolderToReview`, `OrganizeRequest`, the notice's button, the page's
   note. Page tests.
3. **Retire the practice page:** port tests, move AI journeys, remove the page and its code, add
   the "How tidying works" card, reword Home and the side menu. Page tests.
4. **Documents.**
