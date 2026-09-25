# DeskAI

<p align="center">
  <img src="src/DeskAI.App/Assets/DeskAI.Logo.png" alt="DeskAI logo" width="144" />
</p>

> A private, local-first AI workspace for safely organizing, finding, and understanding files on Windows.

[![Build and test](https://github.com/Hammouri1/DeskAI/actions/workflows/build.yml/badge.svg)](https://github.com/Hammouri1/DeskAI/actions/workflows/build.yml)
[![Windows 11](https://img.shields.io/badge/Windows_11-24H2%2B-0078D4?logo=windows11)](docs/INSTALL.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-4DD8A8.svg)](LICENSE)

DeskAI is an open-source Windows desktop app that helps you tidy, search, and understand your
own files — without ever handing an AI model control of your computer. It looks only inside
folders you connect, explains what it wants to do, shows you a preview, and changes nothing
until you approve it. Every change it makes can be undone.

The rule the whole product is built around:

> **AI decides what it recommends. Deterministic code decides what is allowed to happen.**

An AI model in DeskAI has no filesystem access, no shell, no registry, no ability to launch a
program, and no way to execute a plan. It answers with a few typed facts; DeskAI does the work
itself. That boundary is enforced by the project structure, not by prompting — the AI project
compiles against the domain layer alone and contains no file APIs at all.

```text
Connected folder → scan → classify → propose plan → safety validation
                  → preview → your approval → execute → journal → undo
```

**Status:** version 1.2.0. More than 1,600 automated tests pass and the x64 Release build has
zero warnings. The download is not code-signed — see [Honest limits](#honest-limits).

## Download for Windows

No programming tools, account, or .NET installation are needed.

### [Download DeskAI 1.2.0 for Windows](https://github.com/Hammouri1/DeskAI/releases/download/v1.2.0/DeskAI-1.2.0-win-x64.zip)

1. Open the downloaded zip and choose **Extract all**. Do not run DeskAI from inside the zip.
2. Open the new `DeskAI-1.2.0-win-x64` folder.
3. Double-click **DeskAI.App.exe**, the file with the mint DeskAI logo.
4. If Windows says **Windows protected your PC**, choose **More info**, then **Run anyway**.
5. On Home, find **Your folders** and press **Connect** beside Desktop, Downloads, Documents,
   or Pictures. Use this card first; the folder picker accepts only these four places or folders
   inside them.

DeskAI supports 64-bit Windows 11 24H2 or newer. If the direct download does not start, use the
[latest release page](https://github.com/Hammouri1/DeskAI/releases/latest) and open **Assets**.
See [the step-by-step install guide](docs/INSTALL.md) if anything looks different.

## What DeskAI does today

**Organizing.** Connect your Desktop, Downloads, Documents, or Pictures — and nothing else, by
design. DeskAI lists what it found, proposes where loose files could go, and shows you a preview.
Nothing moves until you press Tidy. It never overwrites, never deletes, and re-checks every file
the instant before it moves it. A tidy interrupted by a crash is resolved file by file, and
DeskAI never guesses about a file it cannot prove.

**Finding.** Search by name, type, size, or date. With separate, visible permissions, search
words inside notes, modern Word (`.docx`), Excel (`.xlsx`), PDF, and PowerPoint (`.pptx`) files
locally. A separately confirmed on-device OCR pass can search approximate words on scanned PDF
pages and show the matching page. Save searches, pin them to Home with live counts, and find
exact and possible duplicates—comparing file contents only when you ask and never deleting what
it finds. AI picture/scene search is disabled in this release.

**Quick search.** Press **Ctrl + Alt + D** (or another shortcut you pick) in any app and a small
search bar appears with a friendly search buddy (seven to choose from). Type a name, a kind like
"pdf", or a time like "last week"; a moment later it also finds words inside files, but only in
folders where you allowed that on Search. Enter opens familiar files, such as documents,
pictures, music, and videos, in their usual app; anything else, programs included, is only
shown in its folder. It uses no AI, sends nothing, and remembers nothing you type. While it is
on, closing the DeskAI window keeps DeskAI near the clock so the shortcut still works; quit from
the icon there, or turn quick search off on My workspace.

**Understanding.** See what is using space, what has gone stale, and how organized a folder is.

**Automating.** Write rules in plain language, keep a history of every automatic check, and
optionally let DeskAI keep checking after you close its window, from an icon near the clock.
In a folder where you have separately said yes, it can tidy while you are away — at most 25
rule-matched files per run, stopping on anything unexpected, always undoable.

**Desktop Studio.** Connect your Desktop and press **Find groups**: DeskAI sorts the folders
and files sitting on it into up to 8 groups, such as "Coding" or "School", that you can rename,
merge, and rearrange. With AI, you first see the exact list of names and kinds of files the AI
would get; without AI, DeskAI makes a simpler guess from the kinds of files. Finding groups
changes nothing on your PC. Then, only after you tick and press Move and give a separate yes for
the Desktop, DeskAI can put each group in its own folder, add the group's name to each folder's
name, or gather things unchanged for 6 months into one Old stuff folder. **Put back** undoes
your latest change, even after you reopen DeskAI. Nothing is ever deleted.

**Making it yours.** Folder templates, starter packs, themes, dark mode, wallpaper, and a backup
and restore of your rules and saved searches.

**Optional AI, four ways.** Ask AI about files rules could not place; have AI plan a folder
structure; type a sentence like "PDFs from last month" and have it become a deterministic search
or rule; or use **Ask DeskAI (BETA)** on Home. The beta accepts natural wording for file-search,
storage, and organize questions—it is deliberately not a general chatbot. Only the words or
bounded metadata shown in the consent UI can leave the computer.

## Privacy and AI modes

DeskAI needs no account, no subscription, and no DeskAI-hosted server. It works fully with AI
switched off.

1. **Rule engine only** — deterministic organization, no AI at all. This is the default.
2. **Local AI** — a model running on your own machine, restricted to a loopback address.
3. **Bring your own key** — your existing key for OpenRouter, OpenAI, Groq, Mistral, DeepSeek, or
   Together AI. You never type a web address, so your data can only reach the service you picked.

What an AI service can receive is deliberately tiny: a file's type, size, date, and name — each
only if you allowed that category — or the words you typed, plus today's date. Never a path,
never a folder name, never the contents of a file, never a DeskAI identifier. Keys live in
Windows Credential Manager, one entry per service, and appear in no log, no database, and no
request to a different provider.

Read [AI Providers](docs/AI-PROVIDERS.md) and [Security](docs/SECURITY.md).

## Safety guarantees

These are properties of the code, each covered by tests:

- **Nothing is ever permanently deleted.** There is no file-deletion call anywhere in the source.
- **Nothing is silently overwritten.** Moves refuse to overwrite; collisions must be resolved.
- **Nothing runs by itself** outside a standing permission you gave for one specific folder.
- **Quick search opens only what you pick.** It checks the file again the moment you press
  Enter, never opens a program, and never follows a link out of a connected folder
  ([ADR 0047](docs/decisions/0047-quick-search-opens-files.md)).
- **DeskAI never adds itself to Windows startup** and never checks online for updates.
- **Only four folders can be connected** — Desktop, Downloads, Documents, Pictures, or folders
  inside them — checked when connecting and again before tidying.
- **System, program, and credential locations stay blocked**, and a folder reached through a link
  or junction is refused.
- **One class moves files.** Everything else must go through it, with a preview, an approval, an
  append-only journal, and undo.

## Honest limits

- **The download is not code-signed.** Windows SmartScreen will warn about it until a certificate
  exists ([ADR 0030](docs/decisions/0030-distribution-without-plugins-or-self-update.md)).
- **Full manual sign-off is still outstanding.** The automated tests are thorough, but a person
  has not yet walked every screen.
- **Ask DeskAI is beta.** It handles file-search, storage, and organize questions, not general
  conversation, and its interpretation can be wrong.
- **OCR is approximate.** Scanned-PDF search can miss or misread stylized, small, rotated, or
  unsupported-language text; verify important results in the original document.
- **No image-subject search in this release.** AI picture reading and uploads are disabled.
- **Very large folders are only partly searched.** Search looks up to 8 folders deep and at up to
  20,000 items per connected folder, and says so under the folder when it had to stop.
- **Windows only.** Filesystem semantics, known folders, and shell integration differ too much per
  platform for a shared implementation to be honest about its guarantees.
- **Not built:** renaming files, smart collections, semantic search, plugins, localization, and
  cloud sync. See the [Roadmap](docs/ROADMAP.md) for what is deliberately deferred.

## Built with

- C# on .NET 10, WinUI 3 / Windows App SDK, XAML — unpackaged, self-contained, x64
- MVVM presentation, dependency injection at the composition root, async and cancellable I/O
- SQLite (schema version 16) for settings, rules, saved searches, plans, the metadata index, the Desktop groups board, and
  the operation journal
- xUnit — more than 1,800 tests across five projects, including page tests that use each feature the way a
  person does
- No Electron, no Node.js, no Python, no hosted backend

## Repository shape

```text
src/
  DeskAI.App             WinUI views, dialogs, Windows adapters, composition root
  DeskAI.Presentation    View models and service registration, free of WinUI
  DeskAI.Core            Domain types, use cases, rules, provider-neutral contracts
  DeskAI.Safety          Policy evaluation and plan validation
  DeskAI.Infrastructure  Filesystem, SQLite, Windows integration, indexing
  DeskAI.AI              Provider adapters and structured AI translation
tests/
  DeskAI.Core.Tests  DeskAI.Safety.Tests  DeskAI.Infrastructure.Tests
  DeskAI.AI.Tests    DeskAI.Presentation.Tests
```

`Core` references nothing. `AI` references `Core` alone and contains no filesystem API. `App`
composes the system and holds no filesystem business logic.

## Building from source

You need Windows 11 24H2 or later (64-bit) and the .NET SDK named in `global.json`:

```powershell
git clone https://github.com/Hammouri1/DeskAI.git
cd DeskAI
dotnet publish src/DeskAI.App/DeskAI.App.csproj -c Release -r win-x64 --self-contained -p:WindowsAppSDKSelfContained=true -o publish/DeskAI
```

Run `publish\DeskAI\DeskAI.App.exe`. The build takes a couple of minutes and needs no other
tools.

### Why Windows warns the first time

DeskAI is **not signed with a paid certificate**, so Windows shows **"Windows protected your
PC"** the first time you run it. Choose **More info**, then **Run anyway**. This is what Windows
shows for any unsigned program; it is not a judgement about what the app does. If that is not
good enough for you, build it from source yourself using the commands above — then the binary is
one you produced.

### What to expect on first run

DeskAI starts with **no folder connected and AI switched off**. It can see nothing until you
connect a folder, and it can only ever connect your Desktop, Downloads, Documents, or Pictures.
Connecting a folder lets DeskAI *list* names, sizes, and dates — not move, rename, delete, or
open anything. Tidying is a separate permission you give per folder on the Organize page, and
even then nothing moves until you press Tidy and approve the preview. Everything it does can be
undone.

The first time it opens, DeskAI shows a short three-page welcome that says what it will and will
not do and offers to connect a folder. Skip it any time; you can see it again under **Privacy and
AI → Show the welcome again**.

To remove it completely: open **Privacy and AI**, press **Start fresh**, then delete the folder.

- Install, update, and remove in detail: [docs/INSTALL.md](docs/INSTALL.md)
- How the pages work: [docs/USER-GUIDE.md](docs/USER-GUIDE.md)
- What changed: [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md)

## Building it

1. Windows 11 and the .NET SDK named in `global.json`; nothing else is needed.
2. Read `AGENTS.md` and the documents it names; `docs/HANDOFF.md` says where things stand.

```powershell
dotnet build DeskAI.sln -c Release
dotnet test DeskAI.sln -c Release --no-build
```

Do not point development tools or tests at real personal folders. Development and every test use
generated temporary data only.

## Documentation

- [Install, update, and remove](docs/INSTALL.md)
- [User guide](docs/USER-GUIDE.md)
- [Security model](docs/SECURITY.md)
- [AI providers and privacy](docs/AI-PROVIDERS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Testing strategy](docs/TESTING.md)
- [Roadmap](docs/ROADMAP.md)
- [Release notes](docs/RELEASE-NOTES.md)

## Design decisions

Every significant decision is written down with its reasoning, including the ones that were
rejected: 40 records in [docs/decisions](docs/decisions), and a security review for each
capability that touches a file or a Windows setting in [docs/security](docs/security).

## Contributing and security

See `CONTRIBUTING.md`. Changes are small, tested with generated data only, documented in the same
commit, and consistent with the safety rules in `AGENTS.md`; security-critical behaviour needs
negative tests. To report a security problem privately, see `SECURITY.md`.

DeskAI is developed with AI assistance, under the review process described in `AGENTS.md`.

## License

MIT. See `LICENSE`.
