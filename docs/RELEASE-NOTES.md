# Release notes

## 1.0.0 — 2026-09-16

The first stable release. Everything below was built and tested between 2026-09-07 and
2026-09-16; each capability has a page test, a manual checklist, and, where it touches a file
or a Windows setting, a security review in `docs/security/`.

**What DeskAI does**

- Connects folders you pick and remembers names, sizes, and dates only.
- Tidies a folder you allow: loose files into folders inside it, previewed, ticked, undoable
  after a restart, recovered file by file after a crash. Never deletes, never overwrites.
- Searches by plain phrases, with saved and pinned searches, and can read inside plain text
  files in a folder you separately allow.
- Rules in your own words, practice runs, automatic checks on a schedule, also after the window
  is closed from an icon near the clock (never registered with Windows startup).
- **Tidy while I'm away** (new in 0.9): a switch per folder that lets your switched-on rules
  move at most 25 matching files each check, stopping on anything unexpected, with undo first.
- Home with an honest organization score, storage picture, and a real copy check that reads only
  what it lists and only when you press Compare.
- My workspace: starter packs, folder templates, four looks with light/dark, and your wallpaper
  (the one Windows setting DeskAI can change), with put-back.
- Optional AI, local or with your own key for a vetted list of services, that only ever suggests
  a category after you have seen exactly what it would receive.
- **Back up and restore** of rules and saved searches, and **Start fresh** (new in 0.8).
- A command-center look: grouped menu, top bar with Find a file and an AI pill, dark switch.

**Distribution**

- A self-contained x64 zip on GitHub Releases, built by GitHub Actions per tag, with a software
  bill of materials. No installer, no self-update, no signing yet (see `INSTALL.md`).

**Known limits**

- Windows 11 24H2 or later, 64-bit only.
- The first run shows an unknown-publisher notice until the app is signed.
- Notifications may be unavailable on some machines because the app is unpackaged; the in-app
  notice always works.
- Add-ons, localization beyond English, shortcut and icon suggestions, and image generation are
  deferred (ADR 0030, ROADMAP).

## 0.9.0 — 2026-09-16

Tidy while I'm away, after its security review (ADR 0031).

## 0.8.0 — 2026-09-16

Back up and restore, Start fresh, GitHub workflows, install guide, accessibility-name test,
performance probe, privacy review (ADR 0030). The command-center redesign.

## 0.7 and earlier

See `ROADMAP.md` for V0.1 to V0.7, each with its dates and decisions.
