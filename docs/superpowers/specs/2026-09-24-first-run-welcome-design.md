# First-Run Welcome — Design

- Date: 2026-09-24
- Owner decisions (2026-09-24, in conversation): a welcome **pop-up**, not a Home checklist;
  **three pages**; shown **only to brand-new people**; can be opened again from **Privacy and
  AI**.
- Branch: `first-run-welcome`, started from `desktop-studio-find-groups` (not yet pushed).

## Why

DeskAI is for ordinary, non-technical people. Today a newcomer lands on Home, which says
"Nothing connected yet" and offers the Your folders card, but nothing walks them through what
DeskAI is, what it will never do, and where to begin. `docs/UI-UX.md` "First-Run Experience" asks
for exactly that: DeskAI does nothing until a folder is chosen, the approve-before-anything-moves
promise, AI off by default and offered later, and connecting through a trusted choice rather than
silently.

## What the person sees

A pop-up over Home, the first time DeskAI opens. It has the DeskAI logo, a row of three dots
showing which page is open, and **Back**, **Next**, and **Skip**. Skip, the X, and Esc close it at
once. It never blocks the app and never comes back by itself.

1. **Welcome to DeskAI** — "Find your files and keep them tidy."
2. **You stay in charge** — four lines, each with a tick:
   - DeskAI sees nothing until you connect a folder.
   - It only works in your Desktop, Downloads, Documents, and Pictures.
   - Nothing moves until you see it and say yes.
   - You can put things back.
3. **Let's start** — one **Connect** button per personal folder Windows reports (Desktop,
   Downloads, Documents, Pictures), and "AI is off. You can turn it on later in Privacy and AI."
   If Windows reports none, the page says DeskAI could not find them, as Home's card does. The
   last page's main button is **Done** instead of Next.

**Connect** closes the welcome and then shows Home's existing "Connect your Downloads?" question
(`PersonalFolderDialogs.ConfirmConnectAsync`). Only that question's **Connect my Downloads**
connects; then Organize opens on the folder, exactly as from Home. Cancel leaves the person on
Home with nothing connected. Windows allows one pop-up at a time, and reusing the checked question
keeps the safety wording in one place.

**Privacy and AI** gets a small **Show the welcome again** button, which opens the same pop-up.

## Who sees it, and when

- Shown at startup when both are true: DeskAI has never shown it, and no folder is connected.
  People already using DeskAI do not get it after updating.
- Remembered as shown **the moment it opens**, so Skip, the X, closing DeskAI, or a crash all
  count and it never nags.
- **Start fresh** forgets it, so a reset DeskAI greets the person like new.
- The reminder is one small value, `welcome.shown`, in the existing `app_settings` key/value store
  (`IAppSettingsStore`). No schema change.

## How it is built

| Layer | Piece | Job |
|---|---|---|
| Core | `WelcomeService` | `ShouldShowAsync` (not shown and no connected folder), `MarkShownAsync`, key `WelcomeService.ShownKey` |
| Core | `FreshStartService` | also removes `WelcomeService.ShownKey` |
| Presentation | `WelcomeViewModel` | the three pages' text, current page, `CanGoBack`, `IsLastPage`, Next/Back, the folder rows (from `PersonalFoldersViewModel`), and the chosen folder when Connect is pressed |
| Presentation | `ShellViewModel` | asks `WelcomeService` at startup and marks it shown when the pop-up opens |
| Presentation | `SettingsViewModel` | "Show the welcome again" request |
| App | `WelcomeDialog` | a `ContentDialog` showing the view model's page; returns Skip/Done or Connect(folder) |
| App | `MainWindow` / Home | shows the dialog at startup when asked; on Connect, runs Home's existing connect path |

The view model has no WinUI types, so every behaviour is tested in `DeskAI.Presentation.Tests`.

## Safety

- The welcome cannot connect, read, or move anything by itself. Its Connect button only opens the
  same question Home asks; the connect itself is the existing `PersonalFoldersViewModel.ConnectAsync`
  path with its normal checks.
- No AI, no network, nothing read from any folder. The only thing stored is `welcome.shown`.
- Wording is truthful: "You can put things back" matches Undo on Organize and Put back on Desktop
  Studio; it does not promise that every change can be undone.

## Tests (page tests, generated data only)

- Shows on a fresh DeskAI; not after it was shown; not when a folder is already connected.
- Marked shown as soon as it opens (closing without finishing does not bring it back).
- Next, Back, and Done move through three pages; Back is off on page 1; Skip closes.
- Page 3 lists only folders Windows reports; none reported shows the "could not find" line.
- Connect hands over to the normal connect question: cancel connects nothing; yes connects and
  opens Organize.
- Start fresh brings it back; Privacy and AI's button opens it without changing `welcome.shown`.
- A row in the Feature Coverage Map in `docs/TESTING.md`.

## Docs to update

`docs/UI-UX.md` (First-Run Experience), `docs/USER-GUIDE.md`, `docs/MANUAL-TESTING.md` (a manual
check), `README.md` ("What to expect on first run"), `docs/TESTING.md`, `docs/HANDOFF.md`.

## Not in scope

A checklist on Home, a tour of the side menu, setting up AI from the welcome, and any animation
beyond the page dots.

## After this (owner's request, 2026-09-24)

Once the welcome is built, check that Desktop Studio works well end to end, and bring the owner
suggestions for Desktop Studio additions to choose from.
