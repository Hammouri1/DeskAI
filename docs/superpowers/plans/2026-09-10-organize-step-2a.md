# Organize Step 2a — Tidy Permission, Suggestions, and the New Page

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Organize page with "Tidy a folder": pick a folder, give tidy permission, and see grouped suggestions from file types and the person's rules. Nothing moves yet; the Tidy button is shown disabled and says so.

**Architecture:** A separate per-folder tidy permission is stored in its own table and surfaced on `AuthorizedRoot`, so changing a reading scope cannot drop or widen it. `RootCapabilities.CanTidy` answers whether a folder may be tidied. A Core `TidySuggestionService` scans the folder fresh, keeps only loose top-level files, leaves busy/online-only/hidden/unknown files alone with reasons, lets rules win over file type, resolves same-name destinations, and builds an `OrganizationPlan` that Safety checks through a new `IPlanSafetyCheck`. A new `TidyViewModel` and page present it. The old practice content moves to a `PracticePage` reached from a link.

**Tech Stack:** C# / .NET 10, WinUI 3, SQLite (Microsoft.Data.Sqlite), xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md` (build step 2). AI suggestions on real folders are step **2b**, a separate plan, because sending real file information to AI is its own security gate.

## Global Constraints

- No executor may act on a real folder in this step. `TemporaryDemoPlanExecutor` stays bound to its own demo root; a test proves it refuses a plan for a tidy-permitted real folder.
- Tidy permission is separate from reading scope, granted only through `TidyPermissionService.AllowAsync` after a dialog, revocable without confirmation, and erased when the folder is disconnected (foreign-key cascade).
- `RootCapabilities` stays deny-by-default. Only `MetadataOnly` and `MetadataAndContent` folders can hold a tidy permission; `ControlledDemo` and `Organize` ignore it.
- `AuthorizedRoot.Create` no longer has a default scope. Every caller names one.
- Only loose files at the top of the chosen folder are suggested. Files in subfolders are never touched.
- Left alone, with the reason shown: endings `.crdownload .part .partial .download .opdownload .tmp`; changed within `TidySuggestionService.RecentlyChanged` (2 minutes); online-only (`Offline`, `RecallOnOpen` 0x40000, `RecallOnDataAccess` 0x400000); hidden or system; unknown type; rules disagree; a file in the way of a folder.
- At most `TidySuggestionService.MaxFilesPerTidy` (500) candidates; beyond that the page says "Showing the first 500."
- Your rules win over file type. Same-name default is Skip; Keep both uses "name (2).ext", the first free number up to 99.
- Destination folders may be nested inside the chosen folder (the spec's "directly inside" is amended: rule destinations such as `Documents\Invoices` are allowed, always inside the folder).
- UI text follows `CLAUDE.md`: short, calm, no technical words. The disabled Tidy button carries the note "Tidying arrives in the next update. Nothing moves yet."
- Load `frontend-design:frontend-design` before Task 4's page design.
- Every visible feature gets a page test and a Feature Coverage Map row (`docs/TESTING.md`).
- Verification: `dotnet build DeskAI.sln -c Release --no-restore`; `dotnet test DeskAI.sln -c Release --no-build --no-restore`; `dotnet format DeskAI.sln --no-restore --verify-no-changes`.

---

### Task 1: The tidy permission

**Files:**
- Modify: `src/DeskAI.Core/Roots/AuthorizedRoot.cs`, `src/DeskAI.Core/Roots/RootCapabilities.cs`
- Modify: `src/DeskAI.Core/Abstractions/IAuthorizedRootRepository.cs`, `src/DeskAI.Core/Abstractions/IReadOnlyFolderService.cs`
- Create: `src/DeskAI.Core/Tidy/TidyPermissionService.cs`
- Modify: `src/DeskAI.Core/Search/ConnectedFolderService.cs` (`ConnectedFolder` gains `CanTidy`)
- Modify: `src/DeskAI.Infrastructure/Persistence/SqliteDatabaseInitializer.cs` (schema 12), `SqliteAuthorizedRootRepository.cs`
- Modify: `src/DeskAI.Infrastructure/Scanning/ReadOnlyFolderService.cs` (`CheckStillSafeAsync`)
- Modify: every test fake implementing `IAuthorizedRootRepository` or `IReadOnlyFolderService` (the compiler lists them), and every `AuthorizedRoot.Create` call that relied on the default scope
- Test: `tests/DeskAI.Core.Tests/RootCapabilitiesTests.cs`, `tests/DeskAI.Infrastructure.Tests/SqliteAuthorizedRootRepositoryTests.cs` (new), `tests/DeskAI.Infrastructure.Tests/SqliteDatabaseInitializerTests.cs`, `tests/DeskAI.Safety.Tests/PlanValidatorTests.cs`, `tests/DeskAI.Infrastructure.Tests/TemporaryDemoPlanExecutorTests.cs`
- Create: `docs/decisions/0019-per-folder-tidy-permission.md`

**Interfaces:**
- Produces: `AuthorizedRoot.TidyAllowedSinceUtc : DateTimeOffset?`; `AuthorizedRoot.WithTidyAllowedSince(DateTimeOffset?) : AuthorizedRoot`; `RootCapabilities.CanTidy(AuthorizedRoot) : bool`; `IAuthorizedRootRepository.AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken)`, `StopTidyAsync(Guid rootId, CancellationToken)`; `IReadOnlyFolderService.CheckStillSafeAsync(AuthorizedRoot, CancellationToken) : Task<string?>`; `TidyPermissionService.AllowAsync(Guid, CancellationToken) : Task<TidyPermissionResult>`, `StopAsync(Guid, CancellationToken)`; `record TidyPermissionResult(bool IsAllowed, string Explanation)`; `ConnectedFolder.CanTidy : bool`.

- [ ] **Step 1: Write the failing capability tests** — append to `RootCapabilitiesTests`:

```csharp
    /// <summary>
    /// Tidying is its own yes. A folder connected for reading cannot be changed until someone
    /// allows tidying it, and allowing it changes nothing about what may be read.
    /// </summary>
    [Theory]
    [InlineData(RootAuthorizationScope.MetadataOnly, false)]
    [InlineData(RootAuthorizationScope.MetadataAndContent, true)]
    public void AReadingFolderCanBeTidiedOnlyAfterTidyingIsAllowed(RootAuthorizationScope scope, bool content)
    {
        var root = Root(scope);
        Assert.False(RootCapabilities.CanTidy(root));
        Assert.False(RootCapabilities.CanMutate(root));

        var allowed = root.WithTidyAllowedSince(DateTimeOffset.UnixEpoch);

        Assert.True(RootCapabilities.CanTidy(allowed));
        Assert.True(RootCapabilities.CanMutate(allowed));
        Assert.True(RootCapabilities.CanReadMetadata(allowed));
        Assert.Equal(content, RootCapabilities.CanReadContent(allowed));
    }

    [Theory]
    [InlineData(RootAuthorizationScope.ControlledDemo)]
    [InlineData(RootAuthorizationScope.Organize)]
    public void ATidyPermissionMeansNothingOnAFolderThatWasNotConnectedForReading(RootAuthorizationScope scope)
    {
        Assert.False(RootCapabilities.CanTidy(Root(scope).WithTidyAllowedSince(DateTimeOffset.UnixEpoch)));
    }

    [Theory]
    [InlineData(RootAccessLevel.Restricted)]
    [InlineData(RootAccessLevel.Protected)]
    public void ATidyPermissionOnAFolderThatIsNotAllowedGrantsNothing(RootAccessLevel permission)
    {
        var root = Root(RootAuthorizationScope.MetadataOnly, permission).WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
        Assert.False(RootCapabilities.CanTidy(root));
        Assert.False(RootCapabilities.CanMutate(root));
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/DeskAI.Core.Tests -c Release --no-restore`
Expected: FAIL — `'AuthorizedRoot' does not contain a definition for 'WithTidyAllowedSince'`.

- [ ] **Step 3: Implement `AuthorizedRoot` changes**

Replace the constructor, add the property and method, and remove the default scope:

```csharp
    private AuthorizedRoot(
        Guid id,
        string canonicalPath,
        string displayName,
        RootAccessLevel permission,
        RootAuthorizationScope authorizationScope,
        DateTimeOffset? tidyAllowedSinceUtc)
    {
        Id = id;
        CanonicalPath = canonicalPath;
        DisplayName = displayName;
        Permission = permission;
        AuthorizationScope = authorizationScope;
        TidyAllowedSinceUtc = tidyAllowedSinceUtc;
    }

    // ... existing properties ...

    /// <summary>
    /// When the person allowed DeskAI to tidy this folder, or null if they have not.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="AuthorizationScope"/>, which says what may be <em>read</em>.
    /// Tidying is a separate yes, stored in its own table, so switching reading inside files on
    /// or off can neither drop nor grant it. Ask <see cref="RootCapabilities.CanTidy"/>.
    /// </remarks>
    public DateTimeOffset? TidyAllowedSinceUtc { get; }

    public AuthorizedRoot WithTidyAllowedSince(DateTimeOffset? sinceUtc) =>
        new(Id, CanonicalPath, DisplayName, Permission, AuthorizationScope, sinceUtc);

    /// <remarks>
    /// There is deliberately no default scope. A default is what a caller gets by forgetting,
    /// and the old default was the scope that may be changed.
    /// </remarks>
    public static AuthorizedRoot Create(
        Guid id,
        string canonicalPath,
        string displayName,
        RootAccessLevel permission,
        RootAuthorizationScope authorizationScope)
    {
        // ... existing validation unchanged ...
        return new AuthorizedRoot(id, canonicalPath, displayName, permission, authorizationScope, null);
    }
```

Then fix every compile error from the removed default: in tests that relied on it, pass `RootAuthorizationScope.Organize` (the value they were silently getting), so their meaning is unchanged.

- [ ] **Step 4: Implement `RootCapabilities.CanTidy` and extend `CanMutate`**

```csharp
    /// <summary>
    /// May DeskAI tidy this folder: move its loose files into folders inside it?
    /// </summary>
    /// <remarks>
    /// Only a folder connected for reading can hold this permission, and only once the person
    /// allowed it. The practice workspace and the legacy organize scope are answered by
    /// <see cref="CanMutate"/> directly and never by a tidy grant.
    /// </remarks>
    public static bool CanTidy(AuthorizedRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return IsUsable(root) && root.TidyAllowedSinceUtc is not null && root.AuthorizationScope switch
        {
            RootAuthorizationScope.MetadataOnly => true,
            RootAuthorizationScope.MetadataAndContent => true,
            _ => false,
        };
    }
```

and in `CanMutate` change the default arm to `_ => CanTidy(root),`.

- [ ] **Step 5: Run capability tests** — `dotnet test tests/DeskAI.Core.Tests -c Release --no-restore --filter-class "*RootCapabilitiesTests"` → PASS.

- [ ] **Step 6: Write failing persistence tests** — create `tests/DeskAI.Infrastructure.Tests/SqliteAuthorizedRootRepositoryTests.cs`:

```csharp
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteAuthorizedRootRepositoryTests
{
    [Fact]
    public async Task Allowing_tidying_is_remembered_and_can_be_taken_back()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var root = Reading(sandbox, RootAuthorizationScope.MetadataOnly);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);

        await repository.AllowTidyAsync(root.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        Assert.True(RootCapabilities.CanTidy((await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!));
        Assert.True(RootCapabilities.CanTidy(Assert.Single(await repository.ListAsync(TestContext.Current.CancellationToken))));

        await repository.StopTidyAsync(root.Id, TestContext.Current.CancellationToken);
        Assert.False(RootCapabilities.CanTidy((await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!));
    }

    [Fact]
    public async Task Changing_what_may_be_read_keeps_the_tidy_permission()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var root = Reading(sandbox, RootAuthorizationScope.MetadataOnly);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);
        await repository.AllowTidyAsync(root.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        await repository.SaveAsync(
            AuthorizedRoot.Create(root.Id, root.CanonicalPath, root.DisplayName, RootAccessLevel.Allowed, RootAuthorizationScope.MetadataAndContent),
            TestContext.Current.CancellationToken);

        var reloaded = (await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!;
        Assert.True(RootCapabilities.CanTidy(reloaded));
        Assert.True(RootCapabilities.CanReadContent(reloaded));
    }

    [Fact]
    public async Task Disconnecting_a_folder_forgets_its_tidy_permission()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var root = Reading(sandbox, RootAuthorizationScope.MetadataOnly);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);
        await repository.AllowTidyAsync(root.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        await repository.RemoveAsync(root.Id, TestContext.Current.CancellationToken);
        await repository.SaveAsync(root, TestContext.Current.CancellationToken);

        Assert.False(RootCapabilities.CanTidy((await repository.FindAsync(root.Id, TestContext.Current.CancellationToken))!));
    }

    [Fact]
    public async Task The_practice_workspace_cannot_be_given_a_tidy_permission()
    {
        using var sandbox = new TemporaryDirectory();
        var repository = await CreateAsync(sandbox);
        var demo = Reading(sandbox, RootAuthorizationScope.ControlledDemo);
        await repository.SaveAsync(demo, TestContext.Current.CancellationToken);

        await repository.AllowTidyAsync(demo.Id, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        Assert.Null((await repository.FindAsync(demo.Id, TestContext.Current.CancellationToken))!.TidyAllowedSinceUtc);
    }

    private static AuthorizedRoot Reading(TemporaryDirectory sandbox, RootAuthorizationScope scope) =>
        AuthorizedRoot.Create(Guid.NewGuid(), sandbox.CreateDummyDirectory("Folder"), "Folder", RootAccessLevel.Allowed, scope);

    private static async Task<SqliteAuthorizedRootRepository> CreateAsync(TemporaryDirectory sandbox)
    {
        var options = Options.Create(new DatabaseOptions { DatabasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db") });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);
        return new SqliteAuthorizedRootRepository(options, new SystemClock());
    }
}
```

Also in `SqliteDatabaseInitializerTests.InitializeAsync_CreatesVersionedFoundationSchemaInSandbox` change the expected list to `"1,2,3,4,5,6,7,8,9,10,11,12"`.

- [ ] **Step 7: Run to verify failure** — build fails: `does not contain a definition for 'AllowTidyAsync'`.

- [ ] **Step 8: Add schema 12** — in `SqliteDatabaseInitializer`: set `CurrentSchemaVersion = 12`, call `await ApplyTidyPermissionMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);` after the history migration, and add:

```csharp
    /// <summary>
    /// Adds the separate "you may tidy this folder" permission.
    /// </summary>
    /// <remarks>
    /// Its own table rather than a column or a new scope value, so that saving a folder's
    /// reading scope — which rewrites the folder row — can never drop or grant it, and so that
    /// disconnecting a folder erases it through the cascade like everything else remembered.
    /// </remarks>
    private static async Task ApplyTidyPermissionMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tidy_permissions (
                root_id        TEXT NOT NULL PRIMARY KEY REFERENCES authorized_roots(id) ON DELETE CASCADE,
                granted_at_utc TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (12, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 9: Extend the repository contract and SQLite implementation**

`IAuthorizedRootRepository`:

```csharp
    /// <summary>
    /// Records that the person allowed DeskAI to tidy this folder. Ignored for any folder not
    /// connected for reading, so this can never add a permission to the practice workspace.
    /// </summary>
    Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Takes the tidy permission back. The folder stays connected.</summary>
    Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default);
```

`SqliteAuthorizedRootRepository` — `FindAsync` and `ListAsync` read the grant with a `LEFT JOIN`, `SaveAsync` is unchanged (it never writes the grant):

```csharp
    // FindAsync
        command.CommandText = """
            SELECT r.canonical_path, r.display_name, r.permission, r.authorization_scope, t.granted_at_utc
            FROM authorized_roots r LEFT JOIN tidy_permissions t ON t.root_id = r.id
            WHERE r.id = $id;
            """;
        // ...
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? AuthorizedRoot.Create(
                    rootId, reader.GetString(0), reader.GetString(1),
                    (RootAccessLevel)reader.GetInt32(2), (RootAuthorizationScope)reader.GetInt32(3))
                .WithTidyAllowedSince(ReadGrant(reader, 4))
            : null;

    // ListAsync
        command.CommandText = """
            SELECT r.id, r.canonical_path, r.display_name, r.permission, r.authorization_scope, t.granted_at_utc
            FROM authorized_roots r LEFT JOIN tidy_permissions t ON t.root_id = r.id
            ORDER BY r.display_name COLLATE NOCASE;
            """;
        // ... roots.Add(AuthorizedRoot.Create(...).WithTidyAllowedSince(ReadGrant(reader, 5)));

    public async Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Only a folder connected for reading can be tidied. Selecting the row through that
        // condition means a grant for any other folder simply inserts nothing.
        command.CommandText = """
            INSERT OR IGNORE INTO tidy_permissions(root_id, granted_at_utc)
            SELECT id, $granted FROM authorized_roots
            WHERE id = $id AND authorization_scope IN ($metadataScope, $contentScope);
            """;
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        command.Parameters.AddWithValue("$granted", grantedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$metadataScope", (int)RootAuthorizationScope.MetadataOnly);
        command.Parameters.AddWithValue("$contentScope", (int)RootAuthorizationScope.MetadataAndContent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tidy_permissions WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset? ReadGrant(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);
```

For every test fake implementing `IAuthorizedRootRepository`, add (adapting to its storage):

```csharp
    public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default)
    {
        if (_roots.TryGetValue(rootId, out var root) &&
            root.AuthorizationScope is RootAuthorizationScope.MetadataOnly or RootAuthorizationScope.MetadataAndContent)
        {
            _roots[rootId] = root.WithTidyAllowedSince(grantedAtUtc);
        }

        return Task.CompletedTask;
    }

    public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        if (_roots.TryGetValue(rootId, out var root))
        {
            _roots[rootId] = root.WithTidyAllowedSince(null);
        }

        return Task.CompletedTask;
    }
```

- [ ] **Step 10: Add `CheckStillSafeAsync`** — to `IReadOnlyFolderService`:

```csharp
    /// <summary>
    /// Re-checks, right now, that a connected folder is still somewhere DeskAI may change.
    /// Returns null when it is, or a plain reason when it is not.
    /// </summary>
    Task<string?> CheckStillSafeAsync(AuthorizedRoot root, CancellationToken cancellationToken = default);
```

`ReadOnlyFolderService`:

```csharp
    public Task<string?> CheckStillSafeAsync(AuthorizedRoot root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();
        var problem =
            IsUnsupportedRoot(root.CanonicalPath) ? "Network, device, and whole-drive locations cannot be tidied."
            : !Directory.Exists(root.CanonicalPath) ? "That folder is no longer available."
            : ContainsReparsePoint(root.CanonicalPath) ? "This folder crosses a link or shortcut, so DeskAI will not tidy it."
            : pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked ? "This location is protected and cannot be tidied."
            : null;
        return Task.FromResult(problem);
    }
```

Test fakes implementing `IReadOnlyFolderService` return `Task.FromResult<string?>(null)`.

- [ ] **Step 11: Create `TidyPermissionService`** at `src/DeskAI.Core/Tidy/TidyPermissionService.cs`:

```csharp
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tidy;

public sealed record TidyPermissionResult(bool IsAllowed, string Explanation);

/// <summary>
/// Gives and takes back permission for DeskAI to tidy one connected folder.
/// </summary>
/// <remarks>
/// Granting is only ever called after the page has shown a dialog naming the folder and what
/// tidying may do. The folder is re-checked at that moment rather than trusted from when it
/// was connected: it may have been replaced by a link, moved, or become protected since.
/// Taking the permission back needs no confirmation; that is never the dangerous direction.
/// </remarks>
public sealed class TidyPermissionService(
    IAuthorizedRootRepository roots,
    IReadOnlyFolderService folders,
    IClock clock)
{
    public async Task<TidyPermissionResult> AllowAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return new(false, "That folder is no longer connected.");
        }

        if (!RootCapabilities.CanReadMetadata(root))
        {
            return new(false, "That folder was not connected in a way that lets DeskAI tidy it.");
        }

        var problem = await folders.CheckStillSafeAsync(root, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return new(false, problem);
        }

        await roots.AllowTidyAsync(rootId, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return new(true,
            $"DeskAI may now tidy {root.DisplayName}. It will only move loose files into folders inside it, and it never deletes anything.");
    }

    public async Task<TidyPermissionResult> StopAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await roots.StopTidyAsync(rootId, cancellationToken).ConfigureAwait(false);
        return new(true, "DeskAI can no longer tidy this folder. It is still connected for searching.");
    }
}
```

In `ConnectedFolderService`, add `bool CanTidy = false` as the last positional parameter of `ConnectedFolder`, and pass `RootCapabilities.CanTidy(root)` in both `ListAsync` and `DescribeAsync`.

- [ ] **Step 12: Safety and executor tests** — append to `PlanValidatorTests`:

```csharp
    [Fact]
    public void AReadingFolderWithTidyingAllowedAcceptsASafeMove()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Tidy", "Tidy", RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly).WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
        var move = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"Documents\notes.pdf", "PDF file", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UnixEpoch, PlanValidator.CurrentPolicyVersion, [move]);

        Assert.True(new PlanValidator(new WindowsPathPolicy()).Validate(plan, root).CanBeApproved);
    }

    [Fact]
    public void AReadingFolderWithoutTidyingAllowedStillRefusesEveryMove()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Tidy", "Tidy", RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly);
        var move = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"Documents\notes.pdf", "PDF file", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UnixEpoch, PlanValidator.CurrentPolicyVersion, [move]);

        Assert.False(new PlanValidator(new WindowsPathPolicy()).Validate(plan, root).CanBeApproved);
    }
```

Append to `TemporaryDemoPlanExecutorTests` (uses the file's existing `CreateExecutor`/`Approve` helpers):

```csharp
    [Fact]
    public async Task ExecuteAsync_RefusesAPlanForARealFolderEvenWhenTidyingIsAllowed()
    {
        using var sandbox = new TemporaryDirectory();
        var executor = CreateExecutor(sandbox);
        await executor.PrepareAsync(TestContext.Current.CancellationToken);
        var realFolder = sandbox.CreateDummyDirectory("RealFolder");
        sandbox.CreateDummyFile(@"RealFolder\notes.pdf");
        var real = AuthorizedRoot.Create(Guid.NewGuid(), realFolder, "RealFolder", RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly).WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
        var move = new MoveFileOperation(Guid.NewGuid(), "notes.pdf", @"Documents\notes.pdf", "PDF file", OperationProvenance.Rule);
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), real.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        var result = await executor.ExecuteAsync(plan, Approve(plan, move.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(System.IO.Path.Combine(realFolder, "notes.pdf")));
    }
```

- [ ] **Step 13: Run everything** — full build and tests (Global Constraints). Expected: all pass.

- [ ] **Step 14: ADR** — create `docs/decisions/0019-per-folder-tidy-permission.md` (Status Accepted, 2026-09-10) stating: context (tidying needs its own consent; scopes are exclusive and rewritten on save; the `Create` default was the changeable scope); decision (separate `tidy_permissions` table with cascade, `TidyAllowedSinceUtc` on the root, `CanTidy` deny-by-default for reading scopes only, re-check at grant time, removed default scope, no executor for real folders in this step); consequences (validation accepts tidy-permitted folders; the only executor still refuses them; step 3 adds the executor under its own review).

- [ ] **Step 15: Commit** — `feat(tidy): a separate, revocable permission to tidy one folder`.

---

### Task 2: The scanner reports hidden, system, and online-only files

**Files:**
- Modify: `src/DeskAI.Core/Files/FileItem.cs`, `src/DeskAI.Infrastructure/Scanning/WindowsMetadataScanner.cs`
- Test: `tests/DeskAI.Infrastructure.Tests/WindowsMetadataScannerTests.cs`

**Interfaces:**
- Produces: `[Flags] enum FileTraits { None = 0, Hidden = 1, System = 2, OnlineOnly = 4 }`; `FileItem.Traits : FileTraits` (new optional last constructor parameter `FileTraits traits = FileTraits.None`).

- [ ] **Step 1: Failing test** — append to `WindowsMetadataScannerTests` (the file has `CreateRoot` and `CollectAsync` helpers):

```csharp
    [Fact]
    public async Task ScanAsync_ReportsHiddenSystemAndOnlineOnlyFilesWithoutOpeningThem()
    {
        using var sandbox = new TemporaryDirectory();
        var hidden = sandbox.CreateDummyFile("hidden.txt");
        var system = sandbox.CreateDummyFile("system.txt");
        var online = sandbox.CreateDummyFile("online.txt");
        sandbox.CreateDummyFile("plain.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(system, FileAttributes.System);
        File.SetAttributes(online, FileAttributes.Offline);
        try
        {
            var events = await CollectAsync(new WindowsMetadataScanner(new WindowsPathPolicy()), CreateRoot(sandbox.Path), TestContext.Current.CancellationToken);
            var files = events.OfType<FileDiscovered>().ToDictionary(item => item.File.RelativePath, item => item.File.Traits);

            Assert.Equal(FileTraits.Hidden, files["hidden.txt"]);
            Assert.Equal(FileTraits.System, files["system.txt"]);
            Assert.Equal(FileTraits.OnlineOnly, files["online.txt"]);
            Assert.Equal(FileTraits.None, files["plain.txt"]);
        }
        finally
        {
            foreach (var path in new[] { hidden, system, online })
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }
    }
```

- [ ] **Step 2: Verify failure** — build fails: `'FileItem' does not contain a definition for 'Traits'`.

- [ ] **Step 3: Implement** — `FileItem`: add parameter `FileTraits traits = FileTraits.None` after `modifiedAtUtc`, assign `Traits = traits;`, add `public FileTraits Traits { get; }` and the enum:

```csharp
/// <summary>
/// Facts about a file that decide whether DeskAI should leave it alone, read from its
/// attributes without opening it.
/// </summary>
[Flags]
public enum FileTraits
{
    None = 0,
    Hidden = 1,
    System = 2,

    /// <summary>Stored online only (a cloud placeholder). Moving one can force a download.</summary>
    OnlineOnly = 4,
}
```

`WindowsMetadataScanner`: change the call to `TryCreateFileItem(root.Id, relativePath, entry, attributes.Value)`, add the parameter, pass `ToTraits(attributes)` as the last `FileItem` argument, and add:

```csharp
    // Not named in the FileAttributes enum, but set by Windows on cloud placeholders.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private static FileTraits ToTraits(FileAttributes attributes)
    {
        var traits = FileTraits.None;
        if ((attributes & FileAttributes.Hidden) != 0)
        {
            traits |= FileTraits.Hidden;
        }

        if ((attributes & FileAttributes.System) != 0)
        {
            traits |= FileTraits.System;
        }

        if ((attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0)
        {
            traits |= FileTraits.OnlineOnly;
        }

        return traits;
    }
```

- [ ] **Step 4: Run all tests** → PASS.

- [ ] **Step 5: Commit** — `feat(scan): notice hidden, system, and online-only files without opening them`.

---

### Task 3: Tidy suggestions

**Files:**
- Create: `src/DeskAI.Core/Tidy/TidyFolderRecipe.cs`, `src/DeskAI.Core/Tidy/TidyPreview.cs`, `src/DeskAI.Core/Tidy/TidySuggestionService.cs`
- Create: `src/DeskAI.Core/Abstractions/IPlanSafetyCheck.cs`, `src/DeskAI.Safety/PlanSafetyCheck.cs`
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs`
- Modify: `tests/DeskAI.Presentation.Tests/TestApp.cs` (files are backdated so they are not "changed recently")
- Test: `tests/DeskAI.Presentation.Tests/TidySuggestionTests.cs`

**Interfaces:**
- Consumes: Task 1 (`CanTidy`, `TidyPermissionService`), Task 2 (`FileTraits`).
- Produces: `TidySuggestionService.PreviewAsync(Guid rootId, Guid planId, int revision, IReadOnlyDictionary<Guid, SameNameChoice> choices, CancellationToken) : Task<TidyPreview?>`; constants `MaxFilesPerTidy = 500`, `RecentlyChanged = 2 min`; records `TidyPreview`, `TidySuggestion`, `TidyLeftAlone`; enums `TidySuggestionSource { FileType, Rule }`, `SameNameChoice { Skip, KeepBoth }`, `LeftAloneReason`; `IPlanSafetyCheck { string PolicyVersion; IReadOnlyDictionary<Guid,string> FindBlocked(OrganizationPlan, AuthorizedRoot); }`.

- [ ] **Step 1: Backdate test files** — in `TestApp.MakeFolder`, after each `CreateDummyFile`, call `File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));`, and add:

```csharp
    /// <summary>Creates one file in a generated folder, changed <paramref name="age"/> ago.</summary>
    public string MakeFile(string folder, string name, string content = "Generated DeskAI test data", TimeSpan? age = null)
    {
        var path = Directory.CreateDummyFile(System.IO.Path.Combine("folders", folder, name), content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - (age ?? TimeSpan.FromDays(3)));
        return path;
    }
```

- [ ] **Step 2: Failing tests** — create `tests/DeskAI.Presentation.Tests/TidySuggestionTests.cs`:

```csharp
using DeskAI.App.ViewModels;
using DeskAI.Core.Tidy;

namespace DeskAI.Presentation.Tests;

/// <summary>What DeskAI would suggest for a real (generated) folder. Nothing here moves a file.</summary>
public sealed class TidySuggestionTests
{
    private static readonly IReadOnlyDictionary<Guid, SameNameChoice> NoChoices = new Dictionary<Guid, SameNameChoice>();

    [Fact]
    public async Task Loose_files_are_suggested_by_type_and_files_in_subfolders_are_left_where_they_are()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "holiday.jpg", "setup.exe", @"Uni\essay.docx");
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        var byName = preview.Suggestions.ToDictionary(item => item.FileName, item => item.DestinationFolder);
        Assert.Equal("Documents", byName["invoice.pdf"]);
        Assert.Equal("Pictures", byName["holiday.jpg"]);
        Assert.Equal("Installers", byName["setup.exe"]);
        Assert.DoesNotContain(preview.Suggestions, item => item.FileName.Contains("essay", StringComparison.Ordinal));
        Assert.All(preview.Suggestions, item => Assert.NotNull(item.MoveOperationId));
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task Busy_recent_hidden_and_unknown_files_are_left_alone_with_a_reason()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        app.MakeFile("Downloads", "movie.mp4.crdownload");
        app.MakeFile("Downloads", "just-saved.pdf", age: TimeSpan.FromSeconds(10));
        var hidden = app.MakeFile("Downloads", "secret.pdf");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        app.MakeFile("Downloads", "mystery.zzz");
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        File.SetAttributes(hidden, FileAttributes.Normal);
        Assert.Empty(preview.Suggestions);
        var reasons = preview.LeftAlone.ToDictionary(item => item.FileName, item => item.Reason);
        Assert.Equal(LeftAloneReason.StillDownloading, reasons["movie.mp4.crdownload"]);
        Assert.Equal(LeftAloneReason.ChangedRecently, reasons["just-saved.pdf"]);
        Assert.Equal(LeftAloneReason.HiddenOrSystem, reasons["secret.pdf"]);
        Assert.Equal(LeftAloneReason.UnknownType, reasons["mystery.zzz"]);
        Assert.All(preview.LeftAlone, item => Assert.False(string.IsNullOrWhiteSpace(item.Explanation)));
    }

    [Fact]
    public async Task An_online_only_file_is_left_alone()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        var online = app.MakeFile("Downloads", "cloud.pdf");
        File.SetAttributes(online, FileAttributes.Offline);
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        File.SetAttributes(online, FileAttributes.Normal);
        Assert.Equal(LeftAloneReason.OnlineOnly, Assert.Single(preview.LeftAlone).Reason);
    }

    [Fact]
    public async Task Your_rule_wins_over_the_file_type()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "bank-invoice.pdf", "notes.pdf");
        var automation = app.Get<AutomationViewModel>();
        await automation.InitializeAsync();
        automation.NewRuleName = "Tidy invoices";
        automation.NewRuleNameContains = "invoice";
        automation.NewRuleDestination = @"Documents\Invoices";
        await automation.AddRuleCommand.ExecuteAsync(null);
        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        var invoice = preview.Suggestions.Single(item => item.FileName == "bank-invoice.pdf");
        Assert.Equal(@"Documents\Invoices", invoice.DestinationFolder);
        Assert.Equal(TidySuggestionSource.Rule, invoice.Source);
        Assert.Contains("Tidy invoices", invoice.Reason, StringComparison.Ordinal);
        Assert.Equal("Documents", preview.Suggestions.Single(item => item.FileName == "notes.pdf").DestinationFolder);
    }

    [Fact]
    public async Task A_same_name_already_there_is_skipped_unless_keep_both_is_chosen()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "report.pdf", @"Documents\report.pdf");
        var rootId = await ConnectAndAllowAsync(app, folder);

        var skipped = Assert.Single((await PreviewAsync(app, rootId)).Suggestions);
        Assert.True(skipped.HasSameName);
        Assert.Equal(SameNameChoice.Skip, skipped.Choice);
        Assert.Null(skipped.MoveOperationId);

        var kept = Assert.Single((await PreviewAsync(app, rootId,
            new Dictionary<Guid, SameNameChoice> { [skipped.FileId] = SameNameChoice.KeepBoth })).Suggestions);
        Assert.Equal(@"Documents\report (2).pdf", kept.DestinationRelativePath);
        Assert.NotNull(kept.MoveOperationId);
    }

    [Fact]
    public async Task At_most_500_files_are_suggested_at_once()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads");
        for (var index = 0; index < TidySuggestionService.MaxFilesPerTidy + 10; index++)
        {
            app.MakeFile("Downloads", $"note-{index:D4}.pdf");
        }

        var rootId = await ConnectAndAllowAsync(app, folder);

        var preview = await PreviewAsync(app, rootId);

        Assert.Equal(TidySuggestionService.MaxFilesPerTidy, preview.Suggestions.Count);
        Assert.True(preview.ReachedLimit);
    }

    [Fact]
    public async Task Without_tidy_permission_the_preview_says_it_cannot_tidy()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);

        var preview = await PreviewAsync(app, Assert.Single(search.Folders).Id);

        Assert.False(preview.CanTidy);
    }

    internal static async Task<Guid> ConnectAndAllowAsync(TestApp app, string folder)
    {
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(folder);
        var rootId = search.Folders.Single(item => item.Path == folder).Id;
        Assert.True((await app.Get<TidyPermissionService>().AllowAsync(rootId, TestContext.Current.CancellationToken)).IsAllowed);
        return rootId;
    }

    private static async Task<TidyPreview> PreviewAsync(
        TestApp app,
        Guid rootId,
        IReadOnlyDictionary<Guid, SameNameChoice>? choices = null) =>
        (await app.Get<TidySuggestionService>().PreviewAsync(
            rootId, Guid.NewGuid(), 1, choices ?? NoChoices, TestContext.Current.CancellationToken))!;
}
```

- [ ] **Step 3: Verify failure** — build fails: `The type or namespace name 'Tidy' does not exist`.

- [ ] **Step 4: `IPlanSafetyCheck` and `PlanSafetyCheck`**

`src/DeskAI.Core/Abstractions/IPlanSafetyCheck.cs`:

```csharp
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Asks the safety policy which operations in a plan it would refuse, and why.
/// </summary>
/// <remarks>
/// Core cannot reference Safety, so it asks through this contract, which Safety implements.
/// A key of <see cref="Guid.Empty"/> means the whole plan was refused.
/// </remarks>
public interface IPlanSafetyCheck
{
    string PolicyVersion { get; }

    IReadOnlyDictionary<Guid, string> FindBlocked(OrganizationPlan plan, AuthorizedRoot root);
}
```

`src/DeskAI.Safety/PlanSafetyCheck.cs`:

```csharp
using DeskAI.Core.Abstractions;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Safety;

/// <summary>The safety policy, answered through the Core contract.</summary>
public sealed class PlanSafetyCheck(PlanValidator validator) : IPlanSafetyCheck
{
    public string PolicyVersion => PlanValidator.CurrentPolicyVersion;

    public IReadOnlyDictionary<Guid, string> FindBlocked(OrganizationPlan plan, AuthorizedRoot root)
    {
        var blocked = new Dictionary<Guid, string>();
        foreach (var item in validator.Validate(plan, root).Operations
                     .Where(item => item.Result.Status == ValidationStatus.Blocked))
        {
            blocked.TryAdd(item.OperationId, item.Result.Explanation);
        }

        return blocked;
    }
}
```

- [ ] **Step 5: `TidyFolderRecipe` and `TidyPreview`**

`src/DeskAI.Core/Tidy/TidyFolderRecipe.cs`:

```csharp
using DeskAI.Core.Classification;
using DeskAI.Core.Recipes;

namespace DeskAI.Core.Tidy;

/// <summary>
/// The folders tidying puts things into, named the way people name them.
/// </summary>
/// <remarks>
/// Separate from the practice recipe so its everyday names ("Pictures", "Music") can differ
/// without changing the practice run. Every destination is relative, so it always lands
/// inside the folder being tidied.
/// </remarks>
public static class TidyFolderRecipe
{
    public static FolderRecipe Create() => new(
        id: "tidy",
        displayName: "Tidy",
        version: 1,
        entries:
        [
            new(FileCategory.Documents, "Documents"),
            new(FileCategory.Presentations, @"Documents\Presentations"),
            new(FileCategory.Spreadsheets, @"Documents\Spreadsheets"),
            new(FileCategory.Images, "Pictures"),
            new(FileCategory.Screenshots, @"Pictures\Screenshots"),
            new(FileCategory.Videos, "Videos"),
            new(FileCategory.Audio, "Music"),
            new(FileCategory.Archives, "Archives"),
            new(FileCategory.Installers, "Installers"),
            new(FileCategory.SourceCode, "Code"),
            new(FileCategory.Data, "Data"),
        ]);
}
```

`src/DeskAI.Core/Tidy/TidyPreview.cs`:

```csharp
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tidy;

public enum TidySuggestionSource
{
    FileType,
    Rule,
}

public enum SameNameChoice
{
    Skip,
    KeepBoth,
}

public enum LeftAloneReason
{
    StillDownloading,
    ChangedRecently,
    OnlineOnly,
    HiddenOrSystem,
    UnknownType,
    RulesDisagree,
    InTheWay,
    TooManyWithThisName,
    BlockedBySafety,
}

/// <summary>One loose file and where DeskAI suggests it goes.</summary>
/// <remarks>
/// <see cref="MoveOperationId"/> is null when nothing would move: the file has the same name
/// as one already in its destination and the person has not chosen "Keep both".
/// </remarks>
public sealed record TidySuggestion(
    Guid FileId,
    string FileName,
    string DestinationFolder,
    string DestinationRelativePath,
    TidySuggestionSource Source,
    string Reason,
    Guid? MoveOperationId,
    bool HasSameName,
    SameNameChoice Choice);

/// <summary>A file DeskAI decided not to touch, and the reason in plain words.</summary>
public sealed record TidyLeftAlone(string FileName, LeftAloneReason Reason, string Explanation);

/// <summary>Everything the Organize page shows for one folder, and the plan behind it.</summary>
public sealed record TidyPreview(
    AuthorizedRoot Root,
    OrganizationPlan Plan,
    IReadOnlyList<TidySuggestion> Suggestions,
    IReadOnlyList<TidyLeftAlone> LeftAlone,
    bool ReachedLimit,
    bool ScanWasIncomplete,
    bool CanTidy,
    string? FolderProblem);
```

- [ ] **Step 6: `TidySuggestionService`**

```csharp
using System.Security.Cryptography;
using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Rules;
using FileClassification = DeskAI.Core.Classification.Classification;

namespace DeskAI.Core.Tidy;

/// <summary>
/// Works out what tidying one folder would do. It reads names, sizes, dates, and attributes,
/// and moves nothing.
/// </summary>
/// <remarks>
/// <para>
/// The folder is scanned fresh each time, never read from the remembered index, because the
/// index records how a folder looked, and a suggestion to move a file must be about the file
/// that is there now.
/// </para>
/// <para>
/// Only loose files at the top of the folder are considered. Files someone has already put in
/// a subfolder are their own organisation, and tidying must not reshuffle it.
/// </para>
/// </remarks>
public sealed class TidySuggestionService(
    IAuthorizedRootRepository roots,
    IFileScanner scanner,
    IFileClassifier classifier,
    IRuleRepository rules,
    IPlanSafetyCheck safety,
    IClock clock)
{
    /// <summary>Kept reviewable: a list longer than this is one nobody reads before approving.</summary>
    public const int MaxFilesPerTidy = 500;

    /// <summary>A file changed this recently may still be being written or downloaded.</summary>
    public static TimeSpan RecentlyChanged { get; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Deep enough to see what already sits in destination folders, so a same-name clash can be
    /// shown before anything happens; the move itself re-checks regardless.
    /// </summary>
    public static MetadataScanOptions ScanBounds { get; } = new(maxDepth: 3, maxEntries: 5000);

    private static readonly HashSet<string> DownloadingEndings = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp",
    };

    private static readonly Recipes.FolderRecipe Recipe = TidyFolderRecipe.Create();

    public async Task<TidyPreview?> PreviewAsync(
        Guid rootId,
        Guid planId,
        int revision,
        IReadOnlyDictionary<Guid, SameNameChoice> choices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadMetadata(root))
        {
            return null;
        }

        var now = clock.UtcNow;
        var canTidy = RootCapabilities.CanTidy(root);
        var scanned = new List<FileItem>();
        var incomplete = false;
        string? folderProblem = null;
        await foreach (var scanEvent in scanner.ScanAsync(root, ScanBounds, cancellationToken).ConfigureAwait(false))
        {
            switch (scanEvent)
            {
                case FileDiscovered discovered:
                    scanned.Add(discovered.File);
                    break;
                case ScanIssue { RelativePath: ".", Code: ScanIssueCode.RootUnavailable or ScanIssueCode.RootProtected or ScanIssueCode.UnsupportedRoot or ScanIssueCode.ReparsePointSkipped }:
                    folderProblem = "DeskAI could not look in this folder safely. It may have moved, or become a link.";
                    break;
                case ScanIssue:
                    incomplete = true;
                    break;
            }
        }

        if (folderProblem is not null)
        {
            return Empty(root, planId, revision, now, canTidy, folderProblem);
        }

        var occupied = new HashSet<string>(scanned.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var leftAlone = new List<TidyLeftAlone>();
        var candidates = new List<(FileItem File, FileClassification Class)>();
        var reachedLimit = false;
        foreach (var file in scanned
                     .Where(file => !file.RelativePath.Contains(Path.DirectorySeparatorChar))
                     .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (WhyLeftAlone(file, now) is { } reason)
            {
                leftAlone.Add(reason);
                continue;
            }

            if (candidates.Count == MaxFilesPerTidy)
            {
                reachedLimit = true;
                break;
            }

            candidates.Add((file, classifier.Classify(file)));
        }

        var ruleResult = RuleSetEvaluator.Evaluate(
            await rules.ListAsync(cancellationToken).ConfigureAwait(false),
            candidates.Select(item => RuleSubject.From(item.File, item.Class)).ToArray(),
            now);
        var ruleByPath = ruleResult.Proposals.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var conflictByPath = ruleResult.Conflicts.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        var taken = new HashSet<string>(occupied, StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<TidySuggestion>();
        var moves = new List<MoveFileOperation>();
        foreach (var (file, classification) in candidates)
        {
            var name = file.RelativePath;
            if (conflictByPath.TryGetValue(name, out var conflict))
            {
                leftAlone.Add(new(name, LeftAloneReason.RulesDisagree, conflict.Explanation));
                continue;
            }

            string folder;
            TidySuggestionSource source;
            string reason;
            if (ruleByPath.TryGetValue(name, out var proposal))
            {
                folder = proposal.DestinationRelativeDirectory;
                source = TidySuggestionSource.Rule;
                reason = proposal.RuleNames.Count == 1
                    ? $"Your rule: {proposal.RuleNames[0]}"
                    : $"Your rules: {string.Join(", ", proposal.RuleNames)}";
            }
            else if (Recipe.FindDestination(classification.Category) is { } typeFolder)
            {
                folder = typeFolder;
                source = TidySuggestionSource.FileType;
                reason = DescribeType(name);
            }
            else
            {
                leftAlone.Add(new(name, LeftAloneReason.UnknownType, "DeskAI does not know this kind of file yet, so it stays where it is."));
                continue;
            }

            if (FileInTheWay(folder, occupied) is { } blocker)
            {
                leftAlone.Add(new(name, LeftAloneReason.InTheWay, $"A file called {blocker} is where the {folder} folder would go."));
                continue;
            }

            var target = Path.Combine(folder, name);
            var sameName = taken.Contains(target);
            var choice = sameName ? choices.GetValueOrDefault(file.Id, SameNameChoice.Skip) : SameNameChoice.Skip;
            if (sameName && choice == SameNameChoice.KeepBoth)
            {
                if (UniqueName(target, taken) is not { } unique)
                {
                    leftAlone.Add(new(name, LeftAloneReason.TooManyWithThisName, "Too many files with this name are already there."));
                    continue;
                }

                target = unique;
            }

            Guid? moveId = null;
            if (!sameName || choice == SameNameChoice.KeepBoth)
            {
                moveId = StableId(planId, $"move:{file.Id:N}:{target}");
                moves.Add(new MoveFileOperation(
                    moveId.Value,
                    name,
                    target,
                    reason,
                    source == TidySuggestionSource.Rule ? OperationProvenance.User : OperationProvenance.Rule));
                taken.Add(target);
            }

            suggestions.Add(new TidySuggestion(file.Id, name, folder, target, source, reason, moveId, sameName, choice));
        }

        var plan = BuildPlan(root.Id, planId, revision, now, moves);

        // Before permission, the policy refuses the whole plan simply because the folder may
        // not be changed yet. That is not a reason to hide suggestions from a person deciding
        // whether to allow tidying, so only a folder that can be tidied is filtered.
        if (canTidy)
        {
            var blocked = safety.FindBlocked(plan, root);
            if (blocked.Count > 0)
            {
                (suggestions, moves) = RemoveBlocked(plan, blocked, suggestions, moves, leftAlone);
                plan = BuildPlan(root.Id, planId, revision, now, moves);
            }
        }

        return new TidyPreview(root, plan, suggestions, leftAlone, reachedLimit, incomplete, canTidy, null);
    }

    private static TidyLeftAlone? WhyLeftAlone(FileItem file, DateTimeOffset now)
    {
        var name = file.RelativePath;
        if (DownloadingEndings.Contains(Path.GetExtension(name)))
        {
            return new(name, LeftAloneReason.StillDownloading, "It looks like it is still downloading.");
        }

        if ((file.Traits & FileTraits.OnlineOnly) != 0)
        {
            return new(name, LeftAloneReason.OnlineOnly, "It is stored online only. Moving it would download it first.");
        }

        if ((file.Traits & (FileTraits.Hidden | FileTraits.System)) != 0)
        {
            return new(name, LeftAloneReason.HiddenOrSystem, "It is a hidden or system file.");
        }

        return now - file.ModifiedAtUtc < RecentlyChanged
            ? new(name, LeftAloneReason.ChangedRecently, "It changed in the last few minutes, so it may still be in use.")
            : null;
    }

    private static string DescribeType(string name)
    {
        var ending = Path.GetExtension(name).TrimStart('.');
        return string.IsNullOrEmpty(ending) ? "Its kind of file" : $"{ending.ToUpperInvariant()} file";
    }

    private static string? FileInTheWay(string folder, HashSet<string> occupied)
    {
        var current = string.Empty;
        foreach (var segment in folder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : Path.Combine(current, segment);
            if (occupied.Contains(current))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>"report.pdf" becomes "report (2).pdf", the first number not already taken.</summary>
    private static string? UniqueName(string target, HashSet<string> taken)
    {
        var folder = Path.GetDirectoryName(target) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(target);
        var ending = Path.GetExtension(target);
        for (var number = 2; number <= 99; number++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({number}){ending}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static (List<TidySuggestion>, List<MoveFileOperation>) RemoveBlocked(
        OrganizationPlan plan,
        IReadOnlyDictionary<Guid, string> blocked,
        List<TidySuggestion> suggestions,
        List<MoveFileOperation> moves,
        List<TidyLeftAlone> leftAlone)
    {
        var blockedFolders = plan.Operations.OfType<CreateDirectoryOperation>()
            .Where(operation => blocked.ContainsKey(operation.Id))
            .Select(operation => operation.DestinationRelativePath + Path.DirectorySeparatorChar)
            .ToArray();
        var wholePlan = blocked.TryGetValue(Guid.Empty, out var wholeReason) ? wholeReason : null;

        string? ReasonFor(MoveFileOperation move) =>
            wholePlan
            ?? (blocked.TryGetValue(move.Id, out var reason) ? reason : null)
            ?? (blockedFolders.Any(prefix => move.DestinationRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ? "DeskAI's safety rules do not allow that folder."
                : null);

        var keptMoves = new List<MoveFileOperation>();
        var refused = new Dictionary<Guid, string>();
        foreach (var move in moves)
        {
            if (ReasonFor(move) is { } reason)
            {
                refused[move.Id] = reason;
            }
            else
            {
                keptMoves.Add(move);
            }
        }

        var keptSuggestions = new List<TidySuggestion>();
        foreach (var suggestion in suggestions)
        {
            if (suggestion.MoveOperationId is { } id && refused.TryGetValue(id, out var reason))
            {
                leftAlone.Add(new(suggestion.FileName, LeftAloneReason.BlockedBySafety, reason));
            }
            else
            {
                keptSuggestions.Add(suggestion);
            }
        }

        return (keptSuggestions, keptMoves);
    }

    private OrganizationPlan BuildPlan(Guid rootId, Guid planId, int revision, DateTimeOffset now, IReadOnlyList<MoveFileOperation> moves)
    {
        var directories = moves
            .Select(move => Path.GetDirectoryName(move.DestinationRelativePath))
            .OfType<string>()
            .Where(path => path.Length > 0)
            .SelectMany(Expand)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CreateDirectoryOperation(
                StableId(planId, $"directory:{path}"), path, "A folder for tidied files", OperationProvenance.Rule));

        return OrganizationPlan.CreateDraft(
            planId, rootId, revision, now, safety.PolicyVersion,
            directories.Cast<PlanOperation>().Concat(moves));
    }

    private TidyPreview Empty(AuthorizedRoot root, Guid planId, int revision, DateTimeOffset now, bool canTidy, string problem) =>
        new(root, BuildPlan(root.Id, planId, revision, now, []), [], [], false, false, canTidy, problem);

    private static IEnumerable<string> Expand(string folder)
    {
        var current = string.Empty;
        foreach (var segment in folder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : Path.Combine(current, segment);
            yield return current;
        }
    }

    private static Guid StableId(Guid planId, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{planId:N}:{value.ToUpperInvariant()}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
```

- [ ] **Step 7: Register** — in `AddDeskAiApplication` add:

```csharp
        services.AddSingleton<IPlanSafetyCheck, PlanSafetyCheck>();
        services.AddSingleton<TidyPermissionService>();
        services.AddSingleton<TidySuggestionService>();
```

(with `using DeskAI.Core.Tidy;`).

- [ ] **Step 8: Run all tests** → PASS. If a same-name or rule test fails, fix the service, not the test.

- [ ] **Step 9: Commit** — `feat(tidy): suggest where each loose file goes, and say why others are left alone`.

---

### Task 4: The new Organize page

**Files:**
- Rename: `src/DeskAI.App/Views/OrganizePage.xaml(.cs)` → `PracticePage.xaml(.cs)`; `src/DeskAI.Presentation/ViewModels/OrganizeViewModel.cs` → `PracticeViewModel.cs` (class renamed); `tests/DeskAI.Presentation.Tests/OrganizePageTests.cs` → `PracticePageTests.cs`
- Remove from the practice page and view model: the "Preview one of your own folders" card, `PreviewFolderAsync`, `RevokeFolderCommand`, `FolderFiles`, folder-preview properties, `ReadOnlyFileItemViewModel.cs`, and the three page tests that used them
- Create: `src/DeskAI.Presentation/ViewModels/TidyViewModel.cs`, `src/DeskAI.App/Views/OrganizePage.xaml(.cs)` (new)
- Modify: `src/DeskAI.App/Navigation/NavigationService.cs` (route `practice`), `src/DeskAI.App/App.xaml.cs` (register `PracticePage`), `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs` (`TidyViewModel`, `PracticeViewModel`)
- Modify: `src/DeskAI.Presentation/Help/HelpCatalog.cs` (+4 topics), `tests/DeskAI.Presentation.Tests/HelpCatalogTests.cs`
- Test: `tests/DeskAI.Presentation.Tests/TidyPageTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: `TidyViewModel` with `Folders`, `SelectedFolder`, `Groups`, `LeftAlone`, `NeedsPermission`, `HasSuggestions`, `HasFolders`, `SummaryTitle`, `IncludedCount`, `TidyButtonText`, `CanPressTidy` (false in this step), `TidyNote`, `LimitNote`, `Message`, `IsBusy`, `InitializeAsync()`, `ConnectAndSelectAsync(string)`, `AllowTidyAsync()`, `StopTidyingCommand`, `RefreshCommand`.

- [ ] **Step 1: Rename the practice page** with `git mv`, rename classes (`PracticePage`, `PracticeViewModel`), update the test file and `AiJourneyTests`/`SettingsPageTests` references, remove the folder-preview card and members listed above, add at the top of `PracticePage.xaml` a `HyperlinkButton` "Back to Tidy a folder" whose click calls `INavigationService.Navigate("organize")` (inject `INavigationService` into `PracticePage`). Route `["practice"] = typeof(PracticePage)`; register `services.AddTransient<PracticePage>();` in `App.xaml.cs`. Build and run tests → PASS (minus the removed tests).

- [ ] **Step 2: Failing page tests** — create `tests/DeskAI.Presentation.Tests/TidyPageTests.cs`:

```csharp
using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>The new Organize page, used the way a person uses it. Nothing here moves a file.</summary>
public sealed class TidyPageTests
{
    [Fact]
    public async Task With_no_folders_the_page_asks_you_to_pick_one()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<TidyViewModel>();

        await page.InitializeAsync();

        Assert.False(page.HasFolders);
        Assert.False(page.HasSuggestions);
        Assert.False(page.NeedsPermission);
    }

    [Fact]
    public async Task Picking_a_new_folder_connects_it_and_asks_for_permission_before_suggesting_anything()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();

        await page.ConnectAndSelectAsync(folder);

        Assert.Equal("Downloads", page.SelectedFolder!.Name);
        Assert.True(page.NeedsPermission);
        Assert.False(page.HasSuggestions);
    }

    [Fact]
    public async Task Allowing_tidying_shows_suggestions_grouped_by_folder_with_reasons()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "notes.pdf", "holiday.jpg");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);

        await page.AllowTidyAsync();

        Assert.False(page.NeedsPermission);
        Assert.True(page.HasSuggestions);
        var documents = page.Groups.Single(group => group.Folder == "Documents");
        Assert.Equal(2, documents.Items.Count);
        Assert.All(documents.Items, item => Assert.Equal("PDF file", item.Reason));
        Assert.Equal(3, page.IncludedCount);
        Assert.Equal("Tidy 3 files", page.TidyButtonText);
        Assert.True(File.Exists(Path.Combine(folder, "invoice.pdf")));
    }

    [Fact]
    public async Task The_Tidy_button_is_off_in_this_version_and_says_so()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();

        Assert.False(page.CanPressTidy);
        Assert.Contains("Nothing moves yet", page.TidyNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unticking_a_group_or_a_file_changes_the_count()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf", "notes.pdf", "holiday.jpg");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();

        page.Groups.Single(group => group.Folder == "Documents").IsIncluded = false;
        Assert.Equal(1, page.IncludedCount);

        var documents = page.Groups.Single(group => group.Folder == "Documents");
        documents.Items[0].IsIncluded = true;
        Assert.Equal(2, page.IncludedCount);
        Assert.Null(documents.IsIncluded);
    }

    [Fact]
    public async Task Choosing_keep_both_shows_the_new_name()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "report.pdf", @"Documents\report.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();
        var item = page.Groups.Single().Items.Single();
        Assert.True(item.HasSameName);
        Assert.False(item.IsIncluded);

        item.KeepBoth = true;
        await page.WhenIdleAsync();

        var updated = page.Groups.Single().Items.Single();
        Assert.True(updated.KeepBoth);
        Assert.True(updated.IsIncluded);
        Assert.Contains("report (2).pdf", updated.Destination, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Left_alone_files_are_listed_with_reasons()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "mystery.zzz");
        app.MakeFile("Downloads", "movie.mp4.crdownload");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();

        Assert.Equal(2, page.LeftAlone.Count);
        Assert.All(page.LeftAlone, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
    }

    [Fact]
    public async Task Stopping_tidying_asks_for_permission_again_and_keeps_the_folder_connected()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();

        await page.StopTidyingCommand.ExecuteAsync(null);

        Assert.True(page.NeedsPermission);
        Assert.False(page.HasSuggestions);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        Assert.Single(search.Folders);
    }

    [Fact]
    public async Task Disconnecting_in_Search_takes_the_tidy_permission_with_it()
    {
        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Downloads", "invoice.pdf");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();
        await page.ConnectAndSelectAsync(folder);
        await page.AllowTidyAsync();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.DisconnectFolderCommand.ExecuteAsync(Assert.Single(search.Folders).Id);

        var reopened = app.Get<TidyViewModel>();
        await reopened.InitializeAsync();
        await reopened.ConnectAndSelectAsync(folder);

        Assert.True(reopened.NeedsPermission);
    }

    [Fact]
    public async Task A_protected_folder_cannot_be_picked()
    {
        await using var app = await TestApp.StartAsync();
        var protectedFolder = app.Directory.CreateDummyDirectory("protected");
        var page = app.Get<TidyViewModel>();
        await page.InitializeAsync();

        await page.ConnectAndSelectAsync(protectedFolder);

        Assert.Null(page.SelectedFolder);
        Assert.False(string.IsNullOrWhiteSpace(page.Message));
    }
}
```

- [ ] **Step 3: Verify failure** — build fails: `The type or namespace name 'TidyViewModel' could not be found`.

- [ ] **Step 4: Implement `TidyViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.Core.Search;
using DeskAI.Core.Tidy;

namespace DeskAI.App.ViewModels;

/// <summary>A connected folder offered in the folder list.</summary>
public sealed record TidyFolderOption(Guid Id, string Name, string Path, bool CanTidy)
{
    public override string ToString() => Name;
}

/// <summary>A file DeskAI will not touch, with the reason in plain words.</summary>
public sealed record TidyLeftAloneViewModel(string FileName, string Reason);

/// <summary>One suggested file on the Organize page.</summary>
public sealed class TidyItemViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private readonly Action<Guid, bool> _keepBothChanged;
    private bool _isIncluded;
    private bool _keepBoth;

    public TidyItemViewModel(TidySuggestion suggestion, Action selectionChanged, Action<Guid, bool> keepBothChanged)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        FileId = suggestion.FileId;
        FileName = suggestion.FileName;
        Reason = suggestion.Reason;
        MoveOperationId = suggestion.MoveOperationId;
        HasSameName = suggestion.HasSameName;
        Destination = suggestion.DestinationRelativePath.Replace("\\", " › ", StringComparison.Ordinal);
        _keepBoth = suggestion.Choice == SameNameChoice.KeepBoth;
        _isIncluded = MoveOperationId is not null;
        _selectionChanged = selectionChanged;
        _keepBothChanged = keepBothChanged;
    }

    public Guid FileId { get; }
    public string FileName { get; }
    public string Reason { get; }
    public string Destination { get; }
    public Guid? MoveOperationId { get; }
    public bool HasSameName { get; }

    /// <summary>A skipped same-name file has nothing to move until "Keep both" is chosen.</summary>
    public bool CanBeIncluded => MoveOperationId is not null;

    public string SameNameMessage => $"A file called {FileName} is already there.";

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (SetProperty(ref _isIncluded, value && CanBeIncluded))
            {
                _selectionChanged();
            }
        }
    }

    public bool KeepBoth
    {
        get => _keepBoth;
        set
        {
            if (SetProperty(ref _keepBoth, value))
            {
                _keepBothChanged(FileId, value);
            }
        }
    }
}

/// <summary>The files that would go into one folder.</summary>
public sealed class TidyGroupViewModel : ObservableObject
{
    public TidyGroupViewModel(string folder, IEnumerable<TidyItemViewModel> items)
    {
        Folder = folder;
        DisplayName = folder.Replace("\\", " › ", StringComparison.Ordinal);
        Items = new ObservableCollection<TidyItemViewModel>(items);
    }

    public string Folder { get; }
    public string DisplayName { get; }
    public ObservableCollection<TidyItemViewModel> Items { get; }

    public int IncludedCount => Items.Count(item => item.IsIncluded);

    public string CountText => Items.Count == 1 ? "1 file" : $"{Items.Count} files";

    /// <summary>True when every movable file is ticked, false when none is, null when mixed.</summary>
    public bool? IsIncluded
    {
        get
        {
            var movable = Items.Where(item => item.CanBeIncluded).ToArray();
            if (movable.Length == 0 || movable.All(item => !item.IsIncluded))
            {
                return false;
            }

            return movable.All(item => item.IsIncluded) ? true : null;
        }
        set
        {
            if (value is bool include)
            {
                foreach (var item in Items)
                {
                    item.IsIncluded = include;
                }
            }

            Refresh();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsIncluded));
        OnPropertyChanged(nameof(IncludedCount));
    }
}

/// <summary>
/// Drives the Organize page: pick a folder, allow tidying, and see what tidying would do.
/// </summary>
/// <remarks>
/// In this version the page stops at the list. The Tidy button is shown switched off with a
/// note saying so, because a button that looked ready and did nothing would be worse than an
/// honest "not yet". Nothing on this page can move a file.
/// </remarks>
public sealed class TidyViewModel : ObservableObject
{
    private readonly ConnectedFolderService _folders;
    private readonly TidyPermissionService _permission;
    private readonly TidySuggestionService _suggestions;
    private readonly Dictionary<Guid, SameNameChoice> _choices = [];
    private TidyFolderOption? _selectedFolder;
    private Guid _planId = Guid.NewGuid();
    private int _revision;
    private bool _isBusy;
    private bool _needsPermission;
    private string _message = string.Empty;
    private string _summaryTitle = string.Empty;
    private string _limitNote = string.Empty;
    private Task _pending = Task.CompletedTask;

    public TidyViewModel(
        ConnectedFolderService folders,
        TidyPermissionService permission,
        TidySuggestionService suggestions)
    {
        _folders = folders;
        _permission = permission;
        _suggestions = suggestions;
        StopTidyingCommand = new AsyncRelayCommand(StopTidyingAsync, () => SelectedFolder?.CanTidy == true && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => SelectedFolder is not null && !IsBusy);
    }

    public ObservableCollection<TidyFolderOption> Folders { get; } = [];
    public ObservableCollection<TidyGroupViewModel> Groups { get; } = [];
    public ObservableCollection<TidyLeftAloneViewModel> LeftAlone { get; } = [];

    public AsyncRelayCommand StopTidyingCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public TidyFolderOption? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                _choices.Clear();
                _planId = Guid.NewGuid();
                _revision = 0;
                _pending = LoadAsync();
            }
        }
    }

    public bool HasFolders => Folders.Count > 0;
    public bool HasNoFolders => Folders.Count == 0;
    public bool HasSuggestions => Groups.Count > 0;
    public bool HasLeftAlone => LeftAlone.Count > 0;
    public string LeftAloneTitle => LeftAlone.Count == 1 ? "Left alone (1 file)" : $"Left alone ({LeftAlone.Count} files)";
    public int IncludedCount => Groups.Sum(group => group.IncludedCount);
    public string TidyButtonText => IncludedCount == 1 ? "Tidy 1 file" : $"Tidy {IncludedCount} files";

    /// <summary>Always false in this version: tidying itself arrives in the next update.</summary>
    public bool CanPressTidy => false;

    public string TidyNote => "Tidying arrives in the next update. Nothing moves yet — this is what it would do.";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                StopTidyingCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool NeedsPermission
    {
        get => _needsPermission;
        private set => SetProperty(ref _needsPermission, value);
    }

    public string PermissionTitle => SelectedFolder is null ? string.Empty : $"Allow DeskAI to tidy {SelectedFolder.Name}?";

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string SummaryTitle
    {
        get => _summaryTitle;
        private set => SetProperty(ref _summaryTitle, value);
    }

    public string LimitNote
    {
        get => _limitNote;
        private set
        {
            if (SetProperty(ref _limitNote, value))
            {
                OnPropertyChanged(nameof(HasLimitNote));
            }
        }
    }

    public bool HasLimitNote => !string.IsNullOrEmpty(LimitNote);

    /// <summary>Lets tests wait for the reload a property change started.</summary>
    public Task WhenIdleAsync() => _pending;

    public async Task InitializeAsync() => await ReloadFoldersAsync(selectId: null).ConfigureAwait(true);

    /// <summary>Connects a folder the person picked and confirmed, then selects it.</summary>
    public async Task ConnectAndSelectAsync(string path)
    {
        IsBusy = true;
        try
        {
            var result = await _folders.ConnectAsync(path).ConfigureAwait(true);
            if (!result.IsAllowed || result.Folder is null)
            {
                Message = result.Explanation;
                return;
            }

            Message = string.Empty;
            await ReloadFoldersAsync(result.Folder.Id).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await _pending.ConfigureAwait(true);
    }

    /// <summary>Called only after the page's permission dialog was accepted.</summary>
    public async Task AllowTidyAsync()
    {
        if (SelectedFolder is not { } folder)
        {
            return;
        }

        var result = await _permission.AllowAsync(folder.Id).ConfigureAwait(true);
        Message = result.Explanation;
        await ReloadFoldersAsync(folder.Id).ConfigureAwait(true);
        await _pending.ConfigureAwait(true);
    }

    private async Task StopTidyingAsync()
    {
        if (SelectedFolder is not { } folder)
        {
            return;
        }

        var result = await _permission.StopAsync(folder.Id).ConfigureAwait(true);
        Message = result.Explanation;
        await ReloadFoldersAsync(folder.Id).ConfigureAwait(true);
        await _pending.ConfigureAwait(true);
    }

    private async Task ReloadFoldersAsync(Guid? selectId)
    {
        var keep = selectId ?? SelectedFolder?.Id;
        var connected = await _folders.ListAsync().ConfigureAwait(true);
        Folders.Clear();
        foreach (var folder in connected)
        {
            Folders.Add(new TidyFolderOption(folder.Id, folder.Name, folder.Path, folder.CanTidy));
        }

        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(HasNoFolders));

        // Assigning the same folder again must still reload, because its permission changed.
        _selectedFolder = null;
        SelectedFolder = Folders.FirstOrDefault(item => item.Id == keep);
        if (SelectedFolder is null)
        {
            _pending = LoadAsync();
        }

        OnPropertyChanged(nameof(PermissionTitle));
        StopTidyingCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAsync()
    {
        Groups.Clear();
        LeftAlone.Clear();
        LimitNote = string.Empty;
        SummaryTitle = string.Empty;
        NeedsPermission = SelectedFolder is { CanTidy: false };
        RaiseListChanges();
        if (SelectedFolder is not { CanTidy: true } folder)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var preview = await _suggestions.PreviewAsync(folder.Id, _planId, ++_revision, _choices).ConfigureAwait(true);
            if (preview is null)
            {
                Message = "That folder is no longer connected.";
                return;
            }

            if (preview.FolderProblem is { } problem)
            {
                Message = problem;
                return;
            }

            Apply(preview);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RaiseListChanges();
        }
    }

    private void Apply(TidyPreview preview)
    {
        foreach (var group in preview.Suggestions
                     .GroupBy(item => item.DestinationFolder, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(new TidyGroupViewModel(
                group.Key,
                group.Select(item => new TidyItemViewModel(item, OnSelectionChanged, OnKeepBothChanged))));
        }

        foreach (var item in preview.LeftAlone.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            LeftAlone.Add(new TidyLeftAloneViewModel(item.FileName, item.Explanation));
        }

        SummaryTitle = preview.Suggestions.Count switch
        {
            0 => "Nothing to tidy right now",
            1 => "1 loose file could be tidied",
            var count => $"{count} loose files could be tidied",
        };
        LimitNote = preview.ReachedLimit
            ? $"Showing the first {TidySuggestionService.MaxFilesPerTidy}. Tidy these, then look again for the rest."
            : string.Empty;
    }

    private void OnSelectionChanged()
    {
        foreach (var group in Groups)
        {
            group.Refresh();
        }

        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(TidyButtonText));
    }

    /// <summary>Changing a same-name choice changes the plan, so the list is worked out again.</summary>
    private void OnKeepBothChanged(Guid fileId, bool keepBoth)
    {
        _choices[fileId] = keepBoth ? SameNameChoice.KeepBoth : SameNameChoice.Skip;
        _pending = LoadAsync();
    }

    private void RaiseListChanges()
    {
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(HasLeftAlone));
        OnPropertyChanged(nameof(LeftAloneTitle));
        OnSelectionChanged();
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Data.Common.DbException;
}
```

Register `services.AddTransient<TidyViewModel>();` and `services.AddTransient<PracticeViewModel>();` (replacing `OrganizeViewModel`).

- [ ] **Step 5: Run the page tests** → PASS. Fix the view model, not the tests.

- [ ] **Step 6: Load `frontend-design:frontend-design`**, then design the page within `DeskAITheme.xaml`: the accent only for the two approving actions (Allow tidying, Tidy); groups as quiet row cards; the "What it never does"-style green rail reserved for the permission card's promise. One bold element: the action bar with the Tidy button and its honest note.

- [ ] **Step 7: Write `OrganizePage.xaml`** (new) — structure, top to bottom, all bound with `x:Bind ViewModel.*`:

1. Header: `PageTitleStyle` "Tidy a folder" + `<controls:HelpButton Topic="organize.tidy" />`; subtitle "Pick a folder. DeskAI suggests where things go. Nothing moves until you say so."
2. Folder bar (`CardStyle`): `ComboBox` (`ItemsSource=Folders`, `SelectedItem=SelectedFolder` TwoWay, `PlaceholderText="Pick a folder"`, `AutomationProperties.Name="Folder to tidy"`, visible when `HasFolders`); `Button` "Choose another folder" (`Click="OnChooseFolderClick"`); `HyperlinkButton` "Stop tidying this folder" (`Command=StopTidyingCommand`); `ProgressRing IsActive=IsBusy`; message `TextBlock` bound to `Message`.
3. Empty state (visible when `HasNoFolders`): `SoftCardStyle` card, "Pick a folder to tidy", one sentence, and an accent `Button` "Choose a folder" (`Click="OnChooseFolderClick"`).
4. Permission card (visible when `NeedsPermission`): `HeroPanelStyle`, title `PermissionTitle`, three short lines — "It moves loose files into folders inside this folder." / "It never deletes anything and never touches files in subfolders." / "Nothing moves until you press Tidy." — `<controls:HelpButton Topic="organize.permission" />`, and accent `Button` "Allow tidying" (`Click="OnAllowTidyClick"`).
5. Suggestions (visible when `HasSuggestions`): `SectionTitleStyle` `SummaryTitle`; caption "Suggestions from: DeskAI and your rules" + `<controls:HelpButton Topic="organize.suggestions" />`; `LimitNote` caption; `ItemsControl ItemsSource=Groups` whose template (`x:DataType="viewmodels:TidyGroupViewModel"`) is a `RowCardStyle` border containing a `CheckBox IsThreeState="True" IsChecked="{x:Bind IsIncluded, Mode=TwoWay}"` with `DisplayName` and `CountText`, and an `Expander Header="Show files"` with an inner `ItemsControl` over `Items` (`x:DataType="viewmodels:TidyItemViewModel"`): `CheckBox IsChecked="{x:Bind IsIncluded, Mode=TwoWay}" IsEnabled="{x:Bind CanBeIncluded}"`, `FileName`, caption `Reason`, caption `Destination`, and when `HasSameName` a caution caption `SameNameMessage` plus `ToggleSwitch OffContent="Skip this file" OnContent="Keep both (adds a number)" IsOn="{x:Bind KeepBoth, Mode=TwoWay}"`.
6. Left alone (visible when `HasLeftAlone`): `Expander` with header `LeftAloneTitle` and `<controls:HelpButton Topic="organize.leftAlone" />`, listing `FileName` + caption `Reason`.
7. Action bar (visible when `HasSuggestions`): `CardStyle`, accent `Button Content=TidyButtonText IsEnabled=CanPressTidy` and caption `TidyNote`.
8. Footer: `HyperlinkButton` "Nervous? Try it on example files first" (`Click="OnPracticeClick"`) + `<controls:HelpButton Topic="organize.practice" />`.

- [ ] **Step 8: Write `OrganizePage.xaml.cs`** — constructor takes `TidyViewModel`, `IFolderPickerService`, `INavigationService`; exposes `ViewModel`; `Loaded` calls `InitializeAsync`. Handlers:

```csharp
    private async void OnChooseFolderClick(object sender, RoutedEventArgs e)
    {
        var window = ((App)Application.Current).MainAppWindow;
        if (window is null)
        {
            return;
        }

        var picked = await _folderPicker.PickFolderAsync(WinRT.Interop.WindowNative.GetWindowHandle(window));
        if (picked.Outcome == FolderPickOutcome.Unavailable)
        {
            await ShowAsync("That folder could not be used",
                "Windows did not give DeskAI a location for that choice. This happens with phones, cameras, and some cloud folders. Pick a folder on this computer.");
            return;
        }

        if (!picked.WasPicked)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Let DeskAI look at this folder?",
            Content = $"{picked.Path}\n\nDeskAI will remember file names, sizes, and dates. It will not read inside your files, and it will not move anything unless you allow tidying next.",
            PrimaryButtonText = "Connect folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ConnectAndSelectAsync(picked.Path!);
        }
    }

    private async void OnAllowTidyClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedFolder is not { } folder)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Allow DeskAI to tidy {folder.Name}?",
            Content = $"{folder.Path}\n\nDeskAI may move loose files at the top of this folder into folders inside it.\n"
                + "It never deletes anything, never touches files in subfolders, and never moves anything out of this folder.\n"
                + "Nothing moves until you press Tidy.\n\nYou can take this back at any time.",
            PrimaryButtonText = "Allow tidying",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.AllowTidyAsync();
        }
    }

    private void OnPracticeClick(object sender, RoutedEventArgs e) => _navigation.Navigate("practice");

    private Task ShowAsync(string title, string content) =>
        new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, CloseButtonText = "OK" }.ShowAsync().AsTask();
```

- [ ] **Step 9: Help topics** — add to `HelpCatalog.All`, and add the four IDs to `Each_feature_the_design_names_has_help`:

```csharp
        new("organize.tidy", "Tidying a folder",
            "A way to sort the loose files in one of your folders into neat folders inside it.",
            "Pick a folder and DeskAI suggests where each loose file belongs, like PDFs into Documents. Untick anything you want to keep where it is.",
            "Nothing moves yet in this version. It will never delete anything or move files out of the folder you picked."),
        new("organize.permission", "Permission to tidy",
            "Your yes, for one folder, that DeskAI may tidy it.",
            "Without it, DeskAI can only look. You can take it back at any time, and the folder stays connected for searching.",
            "It never lets DeskAI delete files, touch files in subfolders, or move anything out of the folder."),
        new("organize.suggestions", "Where suggestions come from",
            "How DeskAI decides where each file should go.",
            "It looks at the kind of file, such as PDF or photo. If one of your rules matches a file, your rule wins. Each file shows which one decided.",
            "Suggestions never move anything by themselves."),
        new("organize.leftAlone", "Left alone",
            "Files DeskAI decided not to touch, each with its reason.",
            "For example files still downloading, changed in the last few minutes, stored online only, hidden, or of a kind DeskAI does not know.",
            "Files listed here are never moved."),
```

- [ ] **Step 10: Build, test, format** → all pass, 0 warnings. `HelpPlacementTests` confirm every new topic is placed.

- [ ] **Step 11: Commit** — `feat(organize): a Tidy a folder page with permission and grouped suggestions`.

---

### Task 5: Documents and hand-over

- [ ] Amend the spec: destinations are folders inside the chosen folder (nested allowed); AI moves to step 2b.
- [ ] `docs/SECURITY.md`: describe the tidy permission (separate, revocable, cascade-erased, re-checked at grant) and state that no executor acts on real folders yet.
- [ ] `docs/ARCHITECTURE.md`: a short `Tidy` section (permission service, suggestion service, `IPlanSafetyCheck`, fresh scan, no index use).
- [ ] `docs/UI-UX.md`: replace the old Organize flow section with the new one; describe the disabled Tidy button and why.
- [ ] `docs/MANUAL-TESTING.md`: "Tidy a folder (V0.6 step 2a)" checklist using a generated Windows Temp folder — pick, permission dialog, groups, untick, same name, left alone, stop tidying, practice link, and confirming no file moved.
- [ ] `docs/TESTING.md` Feature Coverage Map: rows for tidy permission, suggestions, left alone, same name, limit, and the practice link; remove the rows for the removed folder-preview card.
- [ ] `docs/ROADMAP.md` V0.6: mark steps 1 and 2a done with dates.
- [ ] Full build, tests, format; commit `docs: tidy permission and the new Organize page`.
- [ ] Hand the owner the exe path and say plainly: the new page suggests but cannot move yet; the next step (2b) adds AI suggestions, then step 3 makes Tidy work.
