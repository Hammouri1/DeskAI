# Quick search polish: shortcut choice, motion switch, buddy stage, glowing bar

Date: 2026-09-25. Branch: `quick-search` (not pushed). Builds on
`2026-09-25-quick-search-design.md` and ADR 0047. Mockups (chosen options): bar **C · Glowing
edge**, chooser **B · Big stage and faces**; the mockup files are kept in
`2026-09-25-quick-search-polish-mockups/`.

## Why

After trying quick search, the owner reported (with two screenshots, 2026-09-25):

1. **No animation.** Cause found: Windows' **Animation effects** is off on the owner's PC, and
   `BuddyControl` holds every buddy still when it is off. The My workspace tiles are also still by
   design (`HoldsStill`).
2. **The buddy chooser looks bad.** The chosen tile shows the "Chosen" pill and a clipped
   "Choose" button; "Paige the paper ghost" wraps to two lines and pushes her button out of the
   card (`VariableSizedWrapGrid` fixes each row's height from its first tile).
3. **The bar is not smooth or fancy.** It is the planned fallback shape: a square navy window,
   a hard mint outline, a default text box, no opening motion.
4. **The shortcut clashes.** Ctrl + Alt + Space is also used by the Claude desktop app.

## Decisions (owner, 2026-09-25)

1. **Shortcut: a short fixed list** on the Quick search card, **Ctrl + Alt + D by default**.
2. **Motion: a DeskAI switch "Let my buddy move"**, on by default, that wins over Windows'
   Animation effects.
3. **Bar: C · Glowing edge** — the buddy perched on top, dark glass, a slowly turning
   mint-to-rainbow edge, a soft glow.
4. **Chooser: B · Big stage and faces** — the chosen buddy large on its stage with its hello line;
   seven round faces below; clicking a face chooses it.
5. The design was approved in chat as: shortcut → motion switch → chooser → see-through probe →
   new bar → docs and handoff, each committed on `quick-search`, nothing pushed.

## 1. Choosing the shortcut

- A **Shortcut** drop-down on the Quick search card, under the switch, with exactly three
  choices: **Ctrl + Alt + D** (default), **Ctrl + Alt + Space**, **Ctrl + Shift + Space**.
- Stored as `quicksearch.shortcut` in `app_settings` (value: the choice's stable name, e.g.
  `CtrlAltD`). Anything else stored, or a store that cannot be read, gives Ctrl + Alt + D. Start
  fresh forgets it (added to `QuickSearchSettingsService.Keys`).
- Nobody stored a shortcut before, so every install moves to Ctrl + Alt + D. The release notes
  say so.
- Picking a choice saves it and, when quick search is on, stops listening for the old one and asks
  Windows for the new one at once. If Windows refuses: "Another program already uses
  Ctrl + Alt + D. Pick another shortcut above." The old one is not kept as a fallback (nothing
  listens until the person picks one Windows accepts or turns the switch off and on).
- Every place that names the shortcut uses the choice: the switch header ("Press Ctrl + Alt + D to
  find a file"), the problem line, the icon's tooltip near the clock (`QuickSearchWords`), the
  welcome page, and the help topic. Static text that cannot know the choice (README, user guide)
  names the default and says it can be changed on My workspace.
- **Shape in code:** a `QuickSearchShortcut` enum in Core with its display text; `QuickSearchSettings`
  gains `Shortcut`; `IQuickSearchHotKey.Listen(bool isOn, QuickSearchShortcut shortcut)`;
  `GlobalHotKey` maps the enum to fixed modifier and key values; `QuickSearchSwitch` gains
  `SetShortcutAsync`.
- **Safety:** still `RegisterHotKey` for one fixed combination at a time, no keyboard hook, no
  free "press any keys" recording. The list is a closed enum, so a stored value can never name
  another key.

## 2. "Let my buddy move"

- A switch on the Quick search card, **on by default**: header "Let my buddy move", line under it
  "Turn this off to keep your buddy and the search bar still."
- Stored as `quicksearch.motion` ("yes"/"no"; missing or unreadable = on). Start fresh forgets it.
- `BuddyControl` stops reading Windows' `UISettings.AnimationsEnabled`. Motion is decided by one
  value the app sets from this switch (a small `BuddyMotion` holder the App project reads), so the
  bar, the chooser stage, and the welcome page's Sparky follow the same switch. Off = the existing
  "Still" state: the right pose, no moves. The bar's opening and the stage's pop-in are skipped
  when off.
- **Why override Windows:** the owner chose it; Windows' switch is often off for speed, not
  comfort. The DeskAI switch is on the same card as the buddies, in plain words, so a person who
  needs stillness finds it where the motion is.

## 3. Buddy chooser (B · Big stage and faces)

- Replaces the seven tiles under "Your search buddy".
- **Stage** (left, about 240 × 230): the chosen buddy large on its own coloured background (the
  mockup's per-buddy radial gradients: night, study, lab, forest, sea, meadow, dusk), moving when
  motion is on, with a mint edge and soft glow.
- **Beside the stage:** the buddy's name (large) and its hello line (`SearchBuddyLines.Line(buddy,
  Idle)`) in a gradient speech bubble.
- **Faces:** seven round buttons (about 60 px) under the name, each the buddy's still picture on
  its background; the chosen face has a mint ring. Clicking a face chooses that buddy at once
  (saved as today), the stage swaps, and the new buddy pops in (skipped when motion is off).
- **Accessibility:** the faces are a radio group named "Search buddies"; each face is named
  "Choose Archie the owl" and reports whether it is chosen; arrow keys move between faces; the
  name and line are announced politely when the buddy changes. The stage drawing is decorative.
- **View model:** `QuickSearchCardViewModel` gains `Chosen` (name, hello line, stage colour key);
  `BuddyTileViewModel` keeps `IsChosen` and `ChooseName`.

## 4. See-through window probe (first, throwaway)

- Question: can the bar's window be see-through (per-pixel transparent) on Windows App SDK 2.4, so
  the buddy perches above the card and the glow shows around it, with clicks on the transparent
  parts not stealing focus in a confusing way?
- Try: a custom `SystemBackdrop` that sets a transparent composition brush on the window (the
  approach the WinUIEx `TransparentTintBackdrop` uses), borderless, no DWM rounding; check on the
  owner's Windows 11 in a throwaway UI-preview build: transparency, the glow, clicking outside the
  card hides the bar, high contrast, and a 150% display.
- **If it works:** the bar is built see-through (section 5).
- **If not:** the backup layout: the same glowing card with the buddy inside on the left and its
  line under it (mockup B of the bar round), in a window the size of the card. The owner is told
  which before section 5 is built.
- The probe code is not committed; its result is recorded in this file's "Rulings made while
  building".

## 5. The new bar (C · Glowing edge)

- **Frame:** 20 px round corners; a 1.5 px edge painted with a mint → blue → violet → pink
  gradient that slowly turns (about 6 s per turn, only while shown and motion is on; a still
  gradient when off); a soft mint glow around it (see-through layout only); dark glass inside
  (`#0B1624`, acrylic where the window allows).
- **Buddy:** perched on the top edge, centred, with its line in a mint-to-blue gradient speech
  bubble to its right.
- **Search box:** large (18–20 px text), no default text-box border, a search icon at the left,
  an **Esc** key hint at the right, a mint focus underline.
- **Rows:** a coloured tile per kind of file (document blue, PDF red, picture orange, video
  violet, other grey) with a short label (DOC, PDF, IMG, VID, FILE), name, and where; the
  selected row has a mint left edge and tint and shows "Open ↵" or "Show in folder ↵" (the row's
  existing action). The row's button stays for mouse users.
- **Examples:** pill-shaped buttons.
- **Opening (motion on):** the card fades and drops in (about 0.3 s), the buddy pops up with a
  little overshoot (about 0.5 s, starting slightly later), and the rows slide in one after another.
  Opening with motion off: shown at once.
- **Unchanged:** everything the bar decides and allows (ADR 0047): what it searches, opening only
  familiar kinds after checking the live file, Show in folder for the rest, nothing sent or
  remembered, hiding on Esc or a click elsewhere, the placement near the top of the screen with
  the pointer.

## Threat cases checked

- **Shortcut:** a stored value naming another key → closed enum, unknown values read as the
  default. A keyboard hook → never; `RegisterHotKey` only. Listening for two shortcuts at once
  after a switch → the old one is unregistered before the new one is asked for (test).
- **Motion override:** a person who needs stillness → the switch is on the card with the
  buddies, in plain words; off holds everything still (page test).
- **See-through window:** a transparent area that catches clicks meant for the app below, or a
  window that stays on top invisibly → the probe checks both; the bar still hides when it loses
  focus. No new Windows capability is used beyond drawing.
- **Nothing else changes** in what quick search reads, opens, or keeps.

## Testing

- Page tests in `DeskAI.Presentation.Tests`, each listed in the Feature Coverage Map:
  choosing a shortcut saves it, re-listens with the new keys, and changes the switch header and
  tooltip; a refused shortcut names it and suggests another; Start fresh forgets shortcut and
  motion; motion off holds the buddy still (the view model's motion value); choosing a face saves
  the buddy and updates the stage's name and line; exactly one face is chosen.
- Layout tests (source-level, as `QuickSearchLayoutTests` does today): faces are a radio group with
  names; the bar has the Esc hint, the search icon, and the turning edge only under motion.
- `GlobalHotKey`'s key mapping gets a unit test of the enum → modifiers and key table (no real
  registration in tests).
- Full Release build, all tests, and formatting before each commit; the throwaway probe build uses
  the UI preview.

## Rulings made while building

(Empty until the work starts.)
