using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteAiUsageBudgetTests
{
    [Fact]
    public async Task TryReserveRequestAsync_AtomicallyEnforcesDailyLimit()
    {
        using var sandbox = new TemporaryDirectory();
        var options = Options.Create(new DatabaseOptions { DatabasePath = Path.Combine(sandbox.Path, "deskai.db") });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);
        var budget = new SqliteAiUsageBudget(options);
        var day = new DateOnly(2026, 9, 8);

        Assert.True(await budget.TryReserveRequestAsync("gemini", 2, day, TestContext.Current.CancellationToken));
        Assert.True(await budget.TryReserveRequestAsync("gemini", 2, day, TestContext.Current.CancellationToken));
        Assert.False(await budget.TryReserveRequestAsync("gemini", 2, day, TestContext.Current.CancellationToken));
        Assert.Equal(2, await budget.GetRequestCountAsync("gemini", day, TestContext.Current.CancellationToken));
    }
}
