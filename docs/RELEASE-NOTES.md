# Release notes

## 1.1 — 2026-09-16 (not tagged yet)

Prepared after the owner's first look at 1.0. `Directory.Build.props` now reads 1.1.0 for the
public release candidate; the release workflow still derives the final version from its tag.

**Fixes**

- DeskAI now has an original mint-and-navy application logo in the navigation pane, executable,
  and public README.
- **Ask DeskAI** is visibly marked **BETA**. It accepts natural wording for file-search,
  storage, and organize questions; it is not presented as a general chatbot.

- The unpackaged release now explicitly shows and foregrounds its main window at startup,
  instead of sometimes remaining as an invisible process in Task Manager.
- Search no longer skips ordinary 8–32 MB modern PowerPoint decks. It checks up to 200
  slides and 256 KB of text, and returns every matching deck among the bounded files checked.
- PDF text search now checks up to 32 MB, 100 pages, and 256 KB of text. Matching tolerates
  layout whitespace inside words, which is common in exported PDFs and formatted slides.
- AI picture reading and image upload are disabled for this launch. A separate **Search
  scanned PDF words** action can use bounded Windows on-device OCR after confirmation for
  that search; its approximate results show page evidence and must be verified in the PDF.
- The Desktop connects even when DeskAI itself was unzipped onto it. A folder that merely
  contains a protected place (DeskAI's own program folder) connects with that part skipped;
  a folder inside a protected place is still refused.
- Pinned search tiles no longer clip "No folders connected": words go on a caption line and only
  a number uses the big style.

**Only your own four folders (ADR 0032)**

- DeskAI can be given only your Desktop, Downloads, Documents, and Pictures, or folders inside
  them, checked when connecting and again before tidying. A **Your folders** card on Home and My
  workspace lists the four with one Connect or Tidy button each; it replaces "Tidy my Desktop".

**AI that does something (ADR 0033, 0034, 0035)**

- **Let AI read this** on Search and Automatic tasks: your own words go to your service, alone;
  the reading comes back as plain words DeskAI reads itself, in the box, for you to change.
- **Plan this folder with AI** on Organize: the AI names up to 12 plain folders and says which
  file goes where; every name is checked four times, and the plan is the ordinary preview,
  Tidy, and undo.
- **Ask DeskAI** on Home: ask "what's taking space in Downloads?" or "tidy my Desktop"; only
  the question is sent; DeskAI answers from what it remembers, in its own words, with one
  button to open Search or Organize or connect a folder.

Every request still shows you exactly what would be sent first, counts against the daily limit,
and never carries what is inside your files or where they are.

**A calmer interface**

- Every page now leads with one short explanation and its next useful action. Optional detail is
  collapsed, Search puts answers before saved searches, and narrow action rows no longer squeeze
  their labels.
- My workspace is split into Looks, Shortcuts, Folder sets, and Desktop. Privacy and AI is split
  into AI setup, What you share, and Your saved data.
- Lavender and Rose join Slate, Graphite, Sand, and Ocean, each with a larger dark/light preview.
  Looks still change neutral surfaces only; safety colours keep the same meaning.

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
- My workspace: starter packs, folder templates, four original looks with light/dark, and your wallpaper
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
