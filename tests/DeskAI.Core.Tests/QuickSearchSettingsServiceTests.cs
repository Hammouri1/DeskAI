using System.Data.Common;
using DeskAI.Core.Abstractions;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Core.Tests;

/// <summary>Quick search's three remembered values, and their defaults when nothing is remembered.</summary>
public sealed class QuickSearchSettingsServiceTests
{
    [Fact]
    public async Task Nothing_remembered_means_on_with_Sparky_and_the_tip_showing()
    {
        var service = new QuickSearchSettingsService(new FakeStore());

        Assert.Equal(QuickSearchSettings.Default, await service.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new QuickSearchSettings(true, SearchBuddy.Sparky, false), QuickSearchSettings.Default);
    }

    [Fact]
    public async Task Choices_are_remembered()
    {
        var store = new FakeStore();
        var service = new QuickSearchSettingsService(store);
        var token = TestContext.Current.CancellationToken;

        await service.SetOnAsync(false, token);
        await service.SetBuddyAsync(SearchBuddy.Inky, token);
        await service.DismissTipAsync(token);

        Assert.Equal(new QuickSearchSettings(false, SearchBuddy.Inky, true), await service.LoadAsync(token));
        Assert.Equal(["quicksearch.buddy", "quicksearch.on", "quicksearch.tip.dismissed"], store.Values.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(store.Values.Keys.Order(StringComparer.Ordinal), QuickSearchSettingsService.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Godzilla")]
    [InlineData("99")]
    [InlineData("sparky ")]
    public async Task A_buddy_name_DeskAI_does_not_know_falls_back_to_Sparky(string stored)
    {
        var store = new FakeStore();
        store.Values[QuickSearchSettingsService.BuddyKey] = stored;

        Assert.Equal(SearchBuddy.Sparky, (await new QuickSearchSettingsService(store).LoadAsync(TestContext.Current.CancellationToken)).Buddy);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_gives_the_defaults()
    {
        var service = new QuickSearchSettingsService(new FakeStore { FailReads = true });

        Assert.Equal(QuickSearchSettings.Default, await service.LoadAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FakeStore : IAppSettingsStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public bool FailReads { get; init; }

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            FailReads ? throw new FakeDbException() : Task.FromResult(Values.GetValueOrDefault(key));

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
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
}
