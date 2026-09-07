# DeskAI

> A private, local-first AI workspace for safely organizing, finding, and understanding files on Windows.

DeskAI is a planned open-source Windows desktop application. It will scan only folders a user selects, generate understandable organization proposals, validate every proposed action using deterministic safety rules, show a preview, and execute only approved operations with history and undo support.

**Current status:** milestones V0.1–V0.3 are implemented. The safe local organizer remains available in Rule Engine Only mode. Optional AI can advise on generated sample metadata through a loopback local endpoint or consent-gated Google Gemini connection. Disclosure categories are filtered before provider code, Gemini keys stay in Windows Credential Manager, structured responses are treated as untrusted, and AI advice cannot edit or execute a plan. SQLite schema version 6 stores non-secret AI settings and daily request counts.

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
3. **Bring Your Own API Key** — optional direct connection to a chosen provider, with explicit data-sharing controls.

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
│   ├── DeskAI.Core/
│   ├── DeskAI.Safety/
│   ├── DeskAI.Infrastructure/
│   └── DeskAI.AI/
└── tests/
```

## Start Building

1. Open this `DeskAI` folder in Codex on Windows.
2. Ask Codex to read `AGENTS.md` and all files under `docs/`.
3. Restore, build, and test using the commands in `docs/DEVELOPMENT.md`.
4. Continue with V0.4's local metadata index and constrained search as the next work cycle.
5. Review every filesystem feature against `docs/SECURITY.md` before enabling real-folder use.

Do not point development tools or tests at real personal folders. Current development uses generated in-memory or temporary test data only.

## Product Principles

- Local first and useful without AI.
- Explicit scope and least privilege.
- Preview before mutation.
- Reversible by design.
- Clear explanations instead of mystery automation.
- Fast deterministic handling for common cases; AI only where it adds value.
- Honest UI: suggestions, confidence, provider use, and irreversible consequences are visible.

## Contributing Direction

This project is not ready for general contributions yet. When implementation begins, changes should be small, tested, documented, and consistent with the dependency and safety rules in `AGENTS.md`. Security-critical behavior requires negative tests, not only happy-path coverage.

## License

No license has been selected in this documentation pack. Choose and add an open-source license before accepting external contributions or distributing the application.
