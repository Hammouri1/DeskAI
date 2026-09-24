using System.Data.Common;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Core.Welcome;

namespace DeskAI.Core.Tests;

/// <summary>
/// The first-run welcome opens only for someone brand new, is remembered before it opens so it
/// never nags, and stays shut when DeskAI cannot remember having shown it.
/// </summary>
public sealed class WelcomeServiceTests
{
    [Fact]
    public async Task A_brand_new_DeskAI_shows_it_once_and_remembers_before_it_opens()
    {
        var store = new FakeStore();
        var service = new WelcomeService(store, new FakeRoots());

        Assert.True(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(store.Values.GetValueOrDefault(WelcomeService.ShownKey));
        Assert.False(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Someone_with_a_folder_already_remembered_is_not_greeted_and_nothing_is_written()
    {
        var store = new FakeStore();
        var roots = new FakeRoots();
        roots.Roots.Add(AuthorizedRoot.Create(
            Guid.NewGuid(), @"C:\deskai-tests\Downloads", "Downloads", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly));
        var service = new WelcomeService(store, roots);

        Assert.False(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
        Assert.Empty(store.Values);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_means_no_welcome()
    {
        var service = new WelcomeService(new FakeStore { FailReads = true }, new FakeRoots());

        Assert.False(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_welcome_that_cannot_be_remembered_is_not_shown_so_it_can_never_return_every_start()
    {
        var service = new WelcomeService(new FakeStore { FailWrites = true }, new FakeRoots());

        Assert.False(await service.ClaimFirstShowAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FakeStore : IAppSettingsStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public bool FailReads { get; init; }

        public bool FailWrites { get; init; }

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            FailReads ? throw new FakeDbException() : Task.FromResult(Values.GetValueOrDefault(key));

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new FakeDbException();
            }

            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDbException : DbException;

    private sealed class FakeRoots : IAuthorizedRootRepository
    {
        public List<AuthorizedRoot> Roots { get; } = [];

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>(Roots);

        // The welcome only lists folders.
        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
