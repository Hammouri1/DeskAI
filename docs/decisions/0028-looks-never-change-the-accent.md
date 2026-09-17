# ADR 0028: Looks Tint the Neutrals and Never the Accent

- Status: Accepted
- Date: 2026-09-16

## Context

V0.7 piece D promised themes for the DeskAI window, "keeping green means safe or confirmed".
The visual system (`docs/UI-UX.md`) has one governing rule: the accent colour means DeskAI is
allowed to do something, and is never decoration. A theme feature that let a person recolour the
accent would break the one thing every screen relies on. The owner also wants DeskAI to look
striking, so a bare light/dark switch was not enough.

## Decision

- **A look is a palette of five neutral colours per theme** — ground, surface, raised surface,
  line, strong line — and nothing else. `LookPalette` has no accent, caution, danger, or text
  property; a test asserts the property list, so adding one is a deliberate act that fails a test.
- **Six looks in code:** Slate (the original, and the default), Graphite, Sand, Ocean, Lavender,
  and Rose. Each has
  a dark and a light palette. A test checks every look keeps the shared text colours readable on
  its ground and surfaces (7:1 primary, 4.5:1 secondary).
- **Light, dark, or follow Windows** is a separate choice, stored beside the look.
- **Applied in place.** The Windows applier changes the colour of the existing brushes in
  `DeskAITheme.xaml`'s dark and light theme dictionaries, so every page repaints at once without
  a restart and the next theme switch finds the look already there. High contrast is untouched.
  `App.OnLaunched` applies the stored choice before the window is activated.
- **Stored in `app_settings`**, the key/value table that has existed since schema 1 and nothing
  used, so no schema change. Unknown stored values fall back to Slate and follow Windows.
- **On My workspace**, where V0.7 pieces join, not on Privacy and AI: a look is not a privacy
  setting.
- **DeskAI's window only.** Nothing here reads or changes the Windows theme, accent, or
  wallpaper. That is piece E's territory and its security gate.

## Alternatives

- A light/dark switch only: too little for the goal.
- Free accent colour choice, or "accent follows Windows": rejected, because an arbitrary accent
  cannot carry a fixed meaning.
- Separate XAML resource dictionaries per look, swapped at runtime: rejected; WinUI does not
  reliably repaint already-rendered brushes when merged dictionaries change, and four full
  dictionaries would be four places to keep the accent honest.
- A new table or a schema bump: unnecessary.

## Consequences

`IAppearanceApplier` is a new Presentation contract with a no-op, the way `IBackgroundPresence`
is done, so page tests run without a window and assert what was applied. `WorkspaceViewModel`
now takes the appearance repository and applier. Re-review is not needed: no file, network,
background, shell, or Windows-setting change.

## 2026-09-17 Amendment

Lavender and Rose were added during the calm-workspace redesign. They use the same five-neutral
record, contrast tests, persistence path, and in-place painter as the original four, so this is
an expansion of the catalog rather than a new security or architecture decision. The look cards
now show a larger dark/light miniature before a person chooses one.
