# DeskAI

> A private, local-first AI workspace for safely organizing, finding, and understanding files on Windows.

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

**Status:** version 1.1, complete in code, tests, and documentation. 1,355 automated tests pass;
the Release build has zero warnings. It has not yet been through a full manual sign-off, and the
download is not code-signed — see [Honest limits](#honest-limits).

## What DeskAI does today

**Organizing.** Connect your Desktop, Downloads, Documents, or Pictures — and nothing else, by
design. DeskAI lists what it found, proposes where loose files could go, and shows you a preview.
Nothing moves until you press Tidy. It never overwrites, never deletes, and re-checks every file
the instant before it moves it. A tidy interrupted by a crash is resolved file by file, and
DeskAI never guesses about a file it cannot prove.

**Finding.** Search by name, type, size, or date. Save searches, pin them to Home with live
counts, and find exact and possible duplicates — comparing file contents only when you ask, and
never deleting what it finds.

**Understanding.** See what is using space, what has gone stale, and how organized a folder is.

**Automating.** Write rules in plain language, keep a history of every automatic check, and
optionally let DeskAI keep checking after you close its window, from an icon near the clock.
In a folder where you have separately said yes, it can tidy while you are away — at most 25
rule-matched files per run, stopping on anything unexpected, always undoable.

**Making it yours.** Folder templates, starter packs, themes, dark mode, wallpaper, and a backup
and restore of your rules and saved searches.

**Optional AI, four ways.** Ask AI about files rules could not place; have AI plan a folder
structure; type a sentence like "PDFs from last month" and have it become a real search or rule;
or ask DeskAI a question on Home. Every one shows you exactly what would be sent before it sends
anything.

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
- **Windows only.** Filesystem semantics, known folders, and shell integration differ too much per
  platform for a shared implementation to be honest about its guarantees.
- **Not built:** renaming files, smart collections, semantic search, plugins, localization, and
  cloud sync. See the [Roadmap](docs/ROADMAP.md) for what is deliberately deferred.

## Built with

- C# on .NET 10, WinUI 3 / Windows App SDK, XAML — unpackaged, self-contained, x64
- MVVM presentation, dependency injection at the composition root, async and cancellable I/O
- SQLite (schema version 14) for settings, rules, saved searches, plans, the metadata index, and
  the operation journal
- xUnit — 1,355 tests across five projects, including page tests that use each feature the way a
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

## Getting it and running it

There is **no published release yet**, so there is no zip to download from the Releases page.
Until there is, there are two ways to run DeskAI.

**If someone sent you a zip.** Unzip it wherever you keep programs — for example
`C:\Apps\DeskAI` — and run `DeskAI.App.exe`. Nothing is installed: no Program Files, no registry,
no Windows startup entry. Deleting the folder removes the app.

**If you have the source.** You need Windows 11 24H2 or later (64-bit) and the .NET SDK named in
`global.json`. Then:

```powershell
git clone https://github.com/Hammouri1/DeskAI.git
cd DeskAI
dotnet publish src/DeskAI.App/DeskAI.App.csproj -c Release -r win-x64 --self-contained -p:WindowsAppSDKSelfContained=true -o publish/DeskAI
```

Run `publish\DeskAI\DeskAI.App.exe`. The build takes a couple of minutes and needs no other
tools.

### Windows will warn you the first time

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

## Design decisions

Every significant decision is written down with its reasoning, including the ones that were
rejected: 35 records in [docs/decisions](docs/decisions), and a security review for each
capability that touches a file or a Windows setting in [docs/security](docs/security).

## Contributing and security

See `CONTRIBUTING.md`. Changes are small, tested with generated data only, documented in the same
commit, and consistent with the safety rules in `AGENTS.md`; security-critical behaviour needs
negative tests. To report a security problem privately, see `SECURITY.md`.

DeskAI is developed with AI assistance, under the review process described in `AGENTS.md`.

## License

MIT. See `LICENSE`.
