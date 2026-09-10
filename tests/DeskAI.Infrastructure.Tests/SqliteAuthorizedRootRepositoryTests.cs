using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

/// <summary>
/// The tidy permission as it is stored: separate from what may be read, erased with the
/// folder, and impossible to attach to the practice workspace.
/// </summary>
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
