using DeskAI.Core.Ai;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteAiSettingsRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsPrivacySettingsWithoutASecret()
    {
        using var sandbox = new TemporaryDirectory();
        var databasePath = Path.Combine(sandbox.Path, "deskai.db");
        var options = Options.Create(new DatabaseOptions { DatabasePath = databasePath });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);
        var repository = new SqliteAiSettingsRepository(options);
        var expected = AiSettings.Default with
        {
            CloudDisclosures = new HashSet<DisclosureCategory>
            {
                DisclosureCategory.Extension,
                DisclosureCategory.FileName,
            },
            CredentialReference = "DeskAI/Gemini",
        };

        await repository.SaveAsync(expected, TestContext.Current.CancellationToken);
        var actual = await repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected.CredentialReference, actual.CredentialReference);
        Assert.Equal(expected.CloudDisclosures, actual.CloudDisclosures);
        var bytes = await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("obvious-test-api-key", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }
}
