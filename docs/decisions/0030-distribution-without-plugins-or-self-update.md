# ADR 0030: Distribution as a zip, no self-update, no add-ons yet, English only

Date: 2026-09-16. Status: accepted (owner's answers of 2026-09-16).

## Context

V0.8 in the roadmap named plugins, packaging, installers, updates, localization, and code
signing in one line. Each is a decision about how much DeskAI reaches, and the owner was asked
about them in plain words.

## Decisions

1. **A zip on GitHub Releases.** Each `v*` tag produces a self-contained x64 zip built by GitHub
   Actions. There is no installer: nothing is written to Program Files, the registry, or the
   Start menu, so uninstalling is deleting the folder (and pressing **Start fresh** first, or
   deleting `%LocalAppData%\DeskAI` and the `DeskAI/*` entries in Credential Manager). This
   keeps the promise in `SECURITY.md` that DeskAI registers nothing with Windows.
2. **No code signing for now.** A certificate costs money. When there is one, the release
   workflow gains a signing step; SignPath (free for open source) and Azure Trusted Signing are
   the two options to evaluate. Until then `INSTALL.md` explains the unknown-publisher warning.
3. **No self-update.** DeskAI never calls a DeskAI server, because there is none, and never
   checks GitHub for a newer version. Its only network use is the AI service a person chose.
   Privacy and AI shows the running version; people look at the Releases page themselves.
4. **No add-ons in V1.** The owner: "let's make this thing after we finish building the
   system." What is recorded now is the boundary a later design must start from. An add-on
   could only ever be given a capability-scoped, typed contract (classify a `FileItem`,
   suggest a category, enrich a search hit) and receive minimized DTOs, never a path, a
   scanner, an executor, a repository, a credential, a setting, or the network. Its output
   would be untrusted like AI output and pass through the same Safety validation. Signing,
   isolation, compatibility, and revocation must be designed and reviewed before any outside
   code is loaded. Nothing in the app loads code from anywhere today, and a test should keep
   it that way when the design arrives.
5. **English only in V1.** All text is in XAML and view models; dates, numbers, and sizes use
   the current culture. Moving strings to resource files is the first step of localization and
   is deferred until there is a second language to ship.
6. **No telemetry**, as before. The privacy review for V0.8 records every place data is written
   or sent.

## Consequences

- Distribution is simple and honest, but a first run shows a Windows warning until signing.
- People update by downloading a new zip. Their data lives in `%LocalAppData%\DeskAI` and
  survives replacing the folder.
- Backup and restore exists so rules and saved searches can move between computers without
  moving the database, keys, or folder permissions.
