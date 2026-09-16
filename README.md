# DeskAI

> A private, local-first AI workspace for safely organizing, finding, and understanding files on Windows.

DeskAI is a planned open-source Windows desktop application. It will scan only folders a user selects, generate understandable organization proposals, validate every proposed action using deterministic safety rules, show a preview, and execute only approved operations with history and undo support.

**Current status (2026-09-16):** V0.1–V0.9 are complete. The app has a command-center look (a grouped menu, a top bar with a file search box and an AI state pill, a dark-mode switch, a Home page of tinted count tiles), a backup and restore of rules and saved searches, a Start fresh button, GitHub Actions that build and test every push and make a release zip per tag, and `docs/INSTALL.md`. Tidy while I'm away (V0.9) lets a rule you turned on move at most 25 rule-matched files per check in a folder you chose, stopping on anything unexpected, with undo first. V1.0 (release polish) is next. The paragraph below is the older running history.

Milestones V0.1–V0.3 are implemented, and V0.4 has begun with a local metadata index (backend only — DeskAI does not index anything on its own yet). The safe local organizer works with AI off by default. Optional AI can advise through an AI service running on this computer, or through whichever supported online service you already have a key for — OpenRouter, OpenAI, Groq, Mistral, DeepSeek, or Together AI — about files in a folder you allowed DeskAI to tidy (since V0.6 step 2b), after DeskAI has shown you exactly what the AI will see and you press Send. Since V0.6 step 3, DeskAI can also tidy a folder you connected and separately allowed it to tidy: it moves only the loose files you ticked into folders inside it, checks each file again right before moving it, never overwrites or deletes, and can undo the tidy — since V0.6 step 4 also after DeskAI is closed and reopened. A tidy stopped part-way by a crash is checked file by file when DeskAI next shows that folder, and the person chooses to undo what moved or keep it; DeskAI never guesses about a file it cannot prove. When an automatic check finds files matching your rules, its notice can open that folder on the Organize page, where nothing moves until you press Tidy. The practice page with made-up files was retired on 2026-09-11; a short "How tidying works" card on Organize explains the page instead. Home can also check which possible copies are really identical, reading only the files it lists and only after you press Compare (V0.4, completed 2026-09-11). V0.6 was completed on 2026-09-11 with a milestone security review (docs/security/2026-09-11-v0.6-milestone-review.md). V0.5 finished with checking after the window is closed, and V0.7 began on 2026-09-14 with a My workspace page: starter packs that add saved searches and switched-off rules after a preview, and pinned searches with counts. For the latest status see [Roadmap](docs/ROADMAP.md). Sharing choices are applied before any online request, each service's key stays in Windows Credential Manager under its own entry, structured responses are treated as untrusted, and AI advice cannot edit or execute a plan. SQLite schema version 7 stores non-secret AI settings, daily request counts, and the local metadata index, which is scoped per connected folder and erased when that folder is disconnected.

## Why DeskAI

File organizers are useful only when users can trust them. DeskAI combines conventional rules and metadata analysis with optional AI, but never gives an LLM control of the filesystem.

```text
Selected folder → scan → classify → propose plan → safety validation
                 → visual preview → user approval → execute → journal/undo
```

The security boundary is simple: **AI recommends; code authorizes and executes.**

## Planned Capabilities

- Organize Desktop, Downloads, and user-selected folders.
- Classify files using extensions, metadata, rules, and optional content analysis.
- Suggest safe folder structures and meaningful file names.
- Preview moves, renames, and folder creation before anything changes.
- Keep an operation history and undo reversible changes.
- Protect favorite, sensitive, and system locations.
- Search with filters, natural language, local indexing, and later semantic search.
- Create Smart Collections without physically moving files.
- Detect exact and possible duplicates without deleting them.
- Explain storage use, stale files, archives, installers, and organization health.
- Translate natural-language instructions into reviewable deterministic rules.
- Run explicitly approved recurring rules with notifications and audit history.
- Support Student, Developer, Gaming, Productivity, and custom workspace profiles.
- Later offer desktop themes, layouts, wallpapers, templates, and plugins.

See [Product](docs/PRODUCT.md) and [Roadmap](docs/ROADMAP.md) for exact scope.

## Privacy and AI Modes

DeskAI is designed to work without an account, subscription, hosted DeskAI backend, or cloud model.

1. **Rule Engine Only** — deterministic organization without AI.
2. **Local AI** — compatible models run on the user's machine.
3. **Bring Your Own API Key** — optional direct connection to whichever supported service you choose, using your own key, with explicit data-sharing controls. DeskAI never asks you to type a web address, so your data can only reach the service you picked.

Cloud providers receive only categories of information the user has permitted. Keys belong in Windows-protected credential storage, never plaintext SQLite. Read [AI Providers](docs/AI-PROVIDERS.md) and [Security](docs/SECURITY.md).

## Planned Stack

- C# and a modern supported .NET version
- WinUI 3 / Windows App SDK and XAML
- MVVM-style presentation architecture
- SQLite for local settings, index metadata, plans, rules, and operation history
- Dependency injection and asynchronous, cancellable I/O
- xUnit-based automated tests

The app is intentionally Windows-first. Cross-platform support is not an early goal because filesystem semantics, permissions, desktop integration, Recycle Bin behavior, packaging, and known folders differ by operating system.

## Repository Shape

```text
DeskAI/
├── AGENTS.md
├── README.md
├── docs/
├── src/
│   ├── DeskAI.App/
│   ├── DeskAI.Presentation/
│   ├── DeskAI.Core/
│   ├── DeskAI.Safety/
│   ├── DeskAI.Infrastructure/
│   └── DeskAI.AI/
└── tests/
```

## Using DeskAI

- Install, update, and remove: [docs/INSTALL.md](docs/INSTALL.md).
- How the pages work: [docs/USER-GUIDE.md](docs/USER-GUIDE.md).
- What changed: [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md).

## Building It

1. Windows 11 and the .NET SDK named in `global.json`; nothing else is needed.
2. Read `AGENTS.md` and the documents it names; `docs/HANDOFF.md` says where things stand.
3. Restore, build, and test with the commands in `docs/DEVELOPMENT.md`.
4. Follow `CONTRIBUTING.md` for how a change is made and what is never accepted.

Do not point development tools or tests at real personal folders. Development uses generated temporary test data only.

## Product Principles

- Local first and useful without AI.
- Explicit scope and least privilege.
- Preview before mutation.
- Reversible by design.
- Clear explanations instead of mystery automation.
- Fast deterministic handling for common cases; AI only where it adds value.
- Honest UI: suggestions, confidence, provider use, and irreversible consequences are visible.

## Contributing and Security

See `CONTRIBUTING.md`. Changes are small, tested with generated data only, documented in the same commit, and consistent with the safety rules in `AGENTS.md`; security-critical behaviour needs negative tests. To report a security problem privately, see `SECURITY.md`.

## License

MIT. See `LICENSE`.
