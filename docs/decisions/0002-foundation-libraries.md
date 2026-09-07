# ADR 0002: Foundation Libraries

- Status: Accepted
- Date: 2026-09-07

## Context

V0.1 needs MVVM support, dependency injection/configuration/logging, SQLite, WinUI, and tests. Dependencies should be few, centrally pinned, and replaceable behind project boundaries.

## Decision

- Use CommunityToolkit.Mvvm 8.4.2 for observable view-model primitives.
- Use Microsoft.Extensions.Hosting 10.0.11 for the composition root, dependency injection, options, and logging abstractions.
- Use Microsoft.Data.Sqlite 10.0.11 directly rather than an ORM.
- Use xUnit v3 4.0.0 with Microsoft.NET.Test.Sdk 18.9.0 and Microsoft Testing Platform.
- Manage versions in `Directory.Packages.props` and generate NuGet lock files.

## Alternatives

Hand-written observable/DI infrastructure would reduce packages but create undifferentiated framework code. Entity Framework Core would provide migrations and mapping, but its added abstraction is unnecessary for the three-table foundation schema. A generic repository was rejected because it would hide rather than clarify meaningful persistence operations.

## Consequences

The App is the only composition root. SQLite types remain inside Infrastructure, toolkit types remain inside App, and Core stays framework-neutral. Future dependency upgrades are deliberate central changes with lock-file diffs that can be reviewed.
