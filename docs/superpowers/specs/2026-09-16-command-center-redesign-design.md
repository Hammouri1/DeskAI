# Command-center redesign and naming pass — design

Date: 2026-09-16. Agreed with the owner in conversation the same day, from a reference
screenshot of a dark "command center" dashboard (a left menu with small grouped labels, a top
bar with a search box, a welcoming hero card with floating icon circles, a row of soft pastel
stat tiles with an icon and one big number, and two columns of rounded cards with date
captions and small status pills). The owner's words: "i want my system to look professional
like this, i am not asking you to just copy and paste the design but be creative and also make
sure that all the screens and features are named in a simple way so normal users feel that
they can use the system easily."

## Decisions the owner made

- **Menu names stay as they are**: Home, Organize, Search, Automatic tasks, My workspace,
  Privacy and AI. The naming pass therefore touches section titles, buttons, and dialog
  titles only where a word is technical; nothing in the menu changes. (Asked 2026-09-16 with
  three choices; the owner picked "Keep the current names".)
- The redesign must keep every rule in `docs/UI-UX.md`: the accent means "safe or confirmed"
  and is never decoration; colour never carries state alone; the scope reminder stays truthful;
  no page implies a capability that does not exist; the four looks keep working through the
  same theme brushes.

## What every page gets (the shell)

1. **Grouped side menu.** Three small grey group labels above the existing items: "Your files"
   (Home, Organize, Search), "DeskAI for you" (Automatic tasks, My workspace), "Settings"
   (Privacy and AI). The selected item sits on a filled neutral pill, never the accent. The
   DeskAI name and a small mark sit at the top of the pane.
2. **Top bar.** Above the page: the page name on the left; on the right a search box
   ("Find a file…"), and an **AI pill** that states AI's state truthfully — "AI off",
   "AI on this computer", or "AI: OpenRouter" — and opens Privacy and AI when pressed. Enter
   in the search box opens Search with that phrase already run, through the same one-shot
   `SearchRequest` that "Open in Search" uses; it grants nothing a person could not do on
   Search.
3. **Dark mode switch** in the pane footer, above the scope reminder. On means DeskAI is
   always dark, off means always light; it saves the same appearance setting My workspace
   does, so "Follow Windows" is still chosen there and the switch shows the theme actually on
   screen. Nothing about Windows changes.
4. **The scope reminder** stays exactly as it is, on the accent rail, refreshed on every
   navigation.
5. **Cards** go from 6px to 12px corners with a hairline; the hero panel keeps its 3px accent
   rail and gains 14px corners on the free side. Type ramp unchanged.
6. **Pills**: a shared `PillStyle` (small rounded chip, hairline, icon + word). Neutral by
   default; the accent variant only for a permission or confirmed state; the caution variant
   for "paused", "needs a look". Every pill carries an icon and a word.
7. **Stat tiles**: a shared `TileStyle` with four soft tints — `DeskTintBlueBrush`,
   `DeskTintVioletBrush`, `DeskTintAmberBrush`, `DeskTintRoseBrush` — declared as alpha tints
   in the theme dictionaries so they sit on every look, mapped to the window colour in high
   contrast. Never green: a count is not a permission.

## Home

- **Hero**: "Good morning/afternoon/evening" from the clock, the existing state line (folders
  connected) on the accent dot, the existing title and promise sentence, and a row of pills:
  "Nothing moves by itself", "AI off / AI: <service>". Floating soft circles with Fluent
  glyphs (folder, search, shield, undo) decorate the right edge in the neutral tint.
- **Quick look** heading and four tiles: Folders connected, Files remembered, Sitting unused,
  Possible duplicates — icon, big number, caption. Each number already exists on the view
  model; the duplicates count is `DuplicateHeadline`.
- **Two columns** at 1100px and wider, one column below: left "How organized this looks" and
  "Where your space is going"; right "Possible duplicates" (with the copy check) and "Largest
  files". Then "What you can try now" full width, and the control promise.

## The other pages

Same content, same words, same buttons. Each page's first card becomes its hero (the folder
bar on Organize, the search box on Search, "Checking for you" on Automatic tasks, "At a glance"
on Privacy and AI, pinned tiles on My workspace) with 12px cards, pills for states
("Allowed to tidy" accent, "Look only" neutral, "Paused" caution, "Every 15 minutes" neutral,
"On / Off" for rules, "Chosen" accent on looks), and captions in the tertiary colour. Automatic
tasks becomes two columns at width: rules and "Write a rule" on the left, "Checking for you"
on the right so it is never scrolled past (ADR 0017). My workspace is done last.

## Tests

- `ShellPageTests`: the AI pill words follow the saved AI choice; the top-bar search hands a
  phrase to Search which runs it once; the dark-mode switch saves Light/Dark and repaints
  through the recording painter, and never touches a Windows setting.
- `HomeAndShellTests`: the greeting follows the clock; the four tile values; the hero pill
  says AI off by default.
- `ShellLayoutTests` (reads the XAML like `HelpPlacementTests`): the six menu items keep
  their names, the three group labels exist, no page uses `DeskAccentBrush` as a tile tint,
  every tile tint brush is one of the four, and the high-contrast dictionary maps every new
  token.
- `DeskLookCatalogTests` unchanged and still pass; the new tint tokens are not part of a look.

## Manual steps added to `MANUAL-TESTING.md`

"Looks the way the design says" steps for each page: the grouped menu, the top bar, the AI
pill, the dark switch, the tiles in all four looks and both themes, high contrast, and the
two-column fold at a narrow window.

## Order of commits

1. Shell: theme tokens and styles, grouped pane, top bar, AI pill, search handoff, dark switch.
2. Home.
3. Organize, Search, Automatic tasks, Privacy and AI, then My workspace.
Each commit builds, passes all tests, and updates `UI-UX.md`, `TESTING.md`, and
`MANUAL-TESTING.md`.
