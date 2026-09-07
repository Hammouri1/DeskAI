using DeskAI.Infrastructure.Logging;

namespace DeskAI.Infrastructure.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_RemovesAuthorizationTokenAndAbsoluteWindowsPath()
    {
        const string message = @"Authorization: Bearer obvious-test-token; file=C:\SyntheticUser\private.txt";

        var redacted = SensitiveDataRedactor.Redact(message);

        Assert.DoesNotContain("obvious-test-token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("SyntheticUser", redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_PATH]", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x-goog-api-key: obvious-gemini-secret")]
    [InlineData("api_key=obvious-provider-secret")]
    public void Redact_RemovesProviderKeyHeaders(string message)
    {
        var redacted = SensitiveDataRedactor.Redact(message);

        Assert.DoesNotContain("obvious-", redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
    }
}
