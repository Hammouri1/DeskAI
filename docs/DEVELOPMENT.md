# Development Guide

## Prerequisites

Development is Windows-first. Before selecting exact versions, inspect the machine for:

- a supported modern .NET SDK;
- Visual Studio 2022 or compatible Build Tools with Windows application tooling;
- Windows SDK and Windows App SDK / WinUI 3 templates or packages;
- Git.

Record chosen target framework, minimum Windows version, Windows App SDK, architecture targets, and packaging mode in an ADR. Do not blindly upgrade packages or invent versions. If WinUI cannot build in the current environment, create/test Core and Safety first and document the exact missing workload rather than replacing the agreed stack.

## Bootstrap Shape

```text
DeskAI.sln
src/DeskAI.App/
src/DeskAI.Presentation/        view models, free of WinUI (added 2026-09-10)
src/DeskAI.Core/
src/DeskAI.Safety/
src/DeskAI.Infrastructure/
src/DeskAI.AI/
tests/DeskAI.Core.Tests/
tests/DeskAI.Safety.Tests/
tests/DeskAI.Infrastructure.Tests/
tests/DeskAI.AI.Tests/
tests/DeskAI.Presentation.Tests/
docs/decisions/
test-data/README.md             optional documentation only; generated data ignored
```

Select project references according to `ARCHITECTURE.md`. Add `Directory.Build.props` for shared nullable, language, analyzer, and warning settings only after checking WinUI compatibility. Central package management is optional; use it if it reduces version drift without complicating the first build.

## First Build Prompt for Codex

Copy this into a Codex task opened at the `DeskAI` folder:

> Read `AGENTS.md`, `README.md`, and every file in `docs/` before making changes. Implement only roadmap milestone V0.1 Safe Foundation. First inspect and report the installed .NET, Windows SDK, Visual Studio/Build Tools, and WinUI tooling; choose compatible explicit versions and record consequential choices in short ADRs. Create the solution, source/test projects, correct project references, `.gitignore`, and a minimal compilable WinUI 3 shell with Dashboard, Organize, Search, Automation, and Settings placeholders if the environment supports it. Add dependency injection at the composition root, initial provider-neutral Core records/interfaces for an organization plan, pure Safety validation abstractions, and xUnit tests including a unique temporary-directory helper. Do not scan or modify Desktop, Downloads, Documents, Pictures, cloud-sync folders, or any personal data. Do not implement real file mutation, API providers, automation, semantic search, plugins, or desktop customization. Use fakes where needed. Build and run all relevant tests, fix failures caused by the work, update documentation/status honestly, and finish with the project tree, decisions, build/test evidence, limitations, next recommended milestone, and a beginner-friendly explanation of every important concept introduced.

## Working Loop After V0.1

Use small tasks. A good prompt states one outcome, acceptance criteria, safety constraints, files/docs to consult, verification expected, and concepts to explain. Example:

> Implement the read-only folder scanner from V0.2. It may scan only an explicitly supplied authorized test root, must be async/cancellable, must not follow reparse points, and must report access errors per item. Test only with generated temporary files. Do not move, rename, delete, read contents, or access known personal folders. Run tests and explain the scanner, interface boundary, cancellation, and error model.

Before each task, inspect current code and Git state. After each task, review the diff and test results, then ask questions until the design can be explained in your own words.

## Code Conventions

- Nullable reference types enabled; implicit usings may be enabled consistently.
- Follow normal .NET naming: PascalCase public members/types, camelCase locals/parameters, `I` prefix for interfaces.
- Prefer file-scoped namespaces and one main public type per file when it improves navigation.
- Domain records/value objects should validate invariants at construction or through explicit factories.
- Use `async` suffix for asynchronous methods and pass `CancellationToken` through I/O boundaries.
- Avoid `async void` except UI event handlers; avoid `.Result`/`.Wait()`.
- Use `DateTimeOffset`/UTC through an injected clock for persisted events.
- Use `Path` APIs and Windows-aware comparers; never concatenate paths manually.
- Keep code-behind minimal. View models expose state and commands, not WinUI controls.
- Use typed options and validate configuration at startup.
- No secrets or personal absolute paths in source, samples, snapshots, logs, or fixtures.
- Comments and XML docs explain contracts, safety invariants, and non-obvious reasons.

## Design Guidance

Interfaces belong at boundaries likely to vary or needing safe fakes: filesystem inspection/execution, plan repository, credential store, AI provider, clock, and perhaps dispatcher/navigation. Do not create an interface for every class.

Use DTOs at serialization/network/database boundaries, domain types inside Core, and mapping code between them. Do not let database rows or provider JSON become the domain model.

Dependency injection connects implementations in `DeskAI.App`; domain objects do not request the container. A repository represents a meaningful persistence collection/use case, not generic CRUD everywhere.

## Configuration and Secrets

Commit safe default configuration and schemas, never real keys. Development secrets use an approved local mechanism outside source control. Provider selection, endpoint, model, disclosure categories, and credential reference are distinct settings. Custom endpoints are validated.

## Database Changes

Use numbered/versioned migrations and test fresh creation plus upgrade from every supported schema. Never manually edit a user database as the product migration strategy. Back up or make migrations recoverable before destructive schema changes. Persist enum values explicitly and plan for unknown future values.

## Git and Review

- Preserve unrelated work and avoid destructive Git commands.
- Prefer focused commits with intention-based messages.
- Review generated diffs, especially project files, package changes, migrations, filesystem code, and security policy.
- Do not commit build output, user databases, models, logs, test sandboxes, keys, or IDE user settings.
- Mark incomplete work clearly; avoid fake implementations that return successful results.

## Build and Run

Codex should discover and document exact commands after scaffolding because WinUI packaging changes command details. At minimum maintain commands for restore, solution build, unit tests, formatting/analyzers, and launching the App through the supported Windows development path. CI may build portable libraries/tests before a Windows runner is configured; never report the UI as verified from an unsupported host.

V0.1 commands from the repository root:

```powershell
dotnet restore DeskAI.sln --configfile NuGet.Config
dotnet build DeskAI.sln --no-restore --configuration Debug
dotnet test --solution DeskAI.sln --no-build --no-restore --configuration Debug
```

.NET 10 uses Microsoft Testing Platform as selected in `global.json`, which is why the solution form is `dotnet test --solution DeskAI.sln`. The app is an x64, unpackaged, self-contained Windows App SDK application. Open `DeskAI.sln` in Visual Studio for interactive launch; install the Windows application development workload and Windows 11 SDK 10.0.26100 or later if Visual Studio reports missing tooling. The command-line build obtains compile-time Windows App SDK assets from the pinned NuGet package.

## Continuous Integration and Releases (V0.8)

`.github/workflows/build.yml` runs on every push and pull request on a Windows runner: locked
restore, Release build, all tests, `dotnet format --verify-no-changes`, and a
known-vulnerable-dependency check. `.github/workflows/release.yml` runs on a `v*` tag: it builds
with `-p:Version=<tag>`, publishes `src/DeskAI.App` self-contained for `win-x64`, zips it as
`DeskAI-<version>-win-x64.zip`, makes a CycloneDX SBOM with the `CycloneDX` .NET tool (installed
on the runner only), and attaches both to a GitHub Release. The version shown on Privacy and AI
comes from `<Version>` in `Directory.Build.props` unless the tag overrides it. To release:

The unpackaged .NET 10 WinUI publish path currently omits the app's own PRI and compiled XAML
(Windows App SDK issue #6720). `DeskAI.App.csproj` copies those already-generated resources into
the publish directory, and the release workflow refuses to zip an output missing the executable,
PRI, core XBF files, icon, or PDF worker. Do not remove that workaround until the pinned Windows
App SDK is verified to publish the same complete output on a clean runner.

```powershell
git tag v0.8.0
git push origin v0.8.0
```

There is no code signing yet (ADR 0030). `docs/INSTALL.md` is the user-facing install guide.

## Definition of a Good Handoff

Include outcome, changed files, user-visible behavior, architecture/data flow, security analysis, build/test command results, warnings or environment limitations, remaining work, exact next task, and short explanations of new concepts. Screenshots help when UI changes, but they do not replace tests.
