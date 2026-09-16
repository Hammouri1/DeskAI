# ADR 0026: Workspace Profiles as One-Time Starter Packs

- Status: Accepted
- Date: 2026-09-14
- Design: `docs/superpowers/specs/2026-09-14-my-workspace-starter-packs-design.md`

## Context

V0.7 promises Student, Developer, Gaming, Productivity, Minimal, and Custom profiles that
"bundle suggested collections, folder templates, and views". V0.7 also holds folder templates,
DeskAI's own themes, and desktop and wallpaper changes, which differ widely in risk. The owner
agreed to build V0.7 one piece at a time, safest first, starting with profiles and pinned saved
searches, neither of which changes a file or anything in Windows.

The open question was what a profile *is*. A remembered profile ("You're using: Student") would
be a setting DeskAI keeps, could reshape pages, and would have to answer what happens to what an
old profile added when someone switches. A pack copied in once has none of those questions.

## Decision

- **A profile is a starter pack, copied in once.** Picking one shows a preview; only Add adds
  ordinary saved searches and rules. DeskAI stores no current profile, and nothing links an added
  item back to its pack, so each is edited or deleted like the person's own.
- **Rules from a pack always arrive switched off.** `StarterPackRule.ToRule` builds them through
  the ordinary rule factory and destination checks with `isEnabled: false`. The rule evaluator
  already ignores disabled rules for Tidy, automatic checks, and the practice run, so adding a
  pack cannot change what DeskAI suggests moving until the person turns a rule on.
- **Nothing is overwritten.** A search or rule whose name is already used, whatever its capitals,
  is skipped and named. Add works the plan out again rather than trusting the preview.
- **Packs are a fixed catalog in code** (`StarterPackCatalog`), with every phrase tested against
  the search translator so a pack search means what its name says.
- **There is no Custom card.** A card that adds nothing would be a fake option; the page points to
  Search and Automatic tasks instead.
- **Pinned saved searches** are a flag on the saved search (schema 13), capped at eight, shown on a
  new side-menu page, My workspace, as tiles with a count that is never "0 files" for "nothing
  connected" or "not understood".

## Alternatives

- A remembered profile that tailors pages: offered to the owner and not chosen.
- Packs as data files beside the app: rejected; a file someone could edit to make a pack add
  something else is a new untrusted input for no benefit today.
- Packs remembered in the database so one can be removed as a unit: rejected; it reintroduces the
  remembered profile.
- Profiles on Home or split between Search and Automatic tasks: offered; the owner chose a new
  page that later V0.7 pieces can join.

## Consequences

No security review gate applies: this adds no file change, content reading, network use,
background work, or shell presence, and tests fail if either Workspace service is given anything
that can change a file, read one, or reach AI. The design document's threat table records that
judgement. Removing a pack means deleting its searches and rules one by one. Later V0.7 pieces —
folder templates (which create folders and need their own design and review), DeskAI's themes, and
desktop or wallpaper changes (a `SECURITY.md` gate) — are not affected by this decision.
