using DeskAI.Core.Classification;
using DeskAI.Core.Rules;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteRuleRepositoryTests
{
    [Fact]
    public async Task SaveAsync_StoresAndReadsBackARuleUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rule = AutomationRule.Create(
            Guid.NewGuid(),
            "Invoices",
            [
                new NameContainsCondition("invoice"),
                new ExtensionIsCondition(".pdf"),
                new CategoryIsCondition(FileCategory.Documents),
                new LargerThanCondition(1_000),
                new OlderThanCondition(TimeSpan.FromDays(30)),
            ],
            new MoveToFolderAction(@"Documents\Invoices"));

        await fixture.Repository.SaveAsync(rule, TestContext.Current.CancellationToken);

        var stored = Assert.Single(await fixture.Repository.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(rule.Id, stored.Id);
        Assert.Equal(rule.Name, stored.Name);
        Assert.Equal(rule.Version, stored.Version);
        Assert.Equal(rule.Conditions, stored.Conditions);
        Assert.Equal(rule.Action, stored.Action);
        Assert.Equal(rule.Describe(), stored.Describe());
    }

    /// <summary>
    /// Turning a rule off has to survive a restart, or a rule someone stopped would quietly
    /// start running again the next time DeskAI opened.
    /// </summary>
    [Fact]
    public async Task SaveAsync_RemembersThatARuleIsTurnedOff()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rule = Rule("Invoices").WithEnabled(false);

        await fixture.Repository.SaveAsync(rule, TestContext.Current.CancellationToken);

        var stored = Assert.Single(await fixture.Repository.ListAsync(TestContext.Current.CancellationToken));
        Assert.False(stored.IsEnabled);
    }

    /// <summary>
    /// An approval is tied to a rule's version, so a stored version that did not survive a
    /// restart would silently make an old approval look current again.
    /// </summary>
    [Fact]
    public async Task SaveAsync_KeepsTheVersionThatAnApprovalWouldBeCheckedAgainst()
    {
        await using var fixture = await Fixture.CreateAsync();
        var edited = Rule("Invoices").WithChanges(action: new MoveToFolderAction("Elsewhere"));

        await fixture.Repository.SaveAsync(edited, TestContext.Current.CancellationToken);

        var stored = await fixture.Repository.FindAsync(edited.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, stored!.Version);
    }

    [Fact]
    public async Task SaveAsync_ReplacesTheStoredCopyOfARuleThatAlreadyExists()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rule = Rule("Invoices");
        await fixture.Repository.SaveAsync(rule, TestContext.Current.CancellationToken);

        var edited = rule.WithChanges(name: "Bills");
        await fixture.Repository.SaveAsync(edited, TestContext.Current.CancellationToken);

        var stored = Assert.Single(await fixture.Repository.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Bills", stored.Name);
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyTheNamedRule()
    {
        await using var fixture = await Fixture.CreateAsync();
        var kept = Rule("Invoices");
        var dropped = Rule("Pictures");
        await fixture.Repository.SaveAsync(kept, TestContext.Current.CancellationToken);
        await fixture.Repository.SaveAsync(dropped, TestContext.Current.CancellationToken);

        await fixture.Repository.RemoveAsync(dropped.Id, TestContext.Current.CancellationToken);

        var stored = Assert.Single(await fixture.Repository.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(kept.Id, stored.Id);
    }

    /// <summary>
    /// Two rules that differ only by capitalisation would be indistinguishable in the list,
    /// and a person could not tell which one they were about to turn off.
    /// </summary>
    [Fact]
    public async Task SaveAsync_RefusesASecondRuleWhoseNameDiffersOnlyByCapitalisation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Repository.SaveAsync(Rule("Invoices"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Repository.SaveAsync(
            Rule("INVOICES"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindAsync_ReportsARuleThatIsNotThere()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Null(await fixture.Repository.FindAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    private static AutomationRule Rule(string name) => AutomationRule.Create(
        Guid.NewGuid(),
        name,
        [new NameContainsCondition(name)],
        new MoveToFolderAction("Sorted"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _sandbox;

        private Fixture(TemporaryDirectory sandbox, string databasePath)
        {
            _sandbox = sandbox;
            Repository = new SqliteRuleRepository(
                Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
                new SystemClock());
        }

        public SqliteRuleRepository Repository { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var sandbox = new TemporaryDirectory();
            var databasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db");
            var initializer = new SqliteDatabaseInitializer(
                Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
                new SystemClock(),
                NullLogger<SqliteDatabaseInitializer>.Instance);
            await initializer.InitializeAsync(TestContext.Current.CancellationToken);
            return new Fixture(sandbox, databasePath);
        }

        public ValueTask DisposeAsync()
        {
            _sandbox.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
