using System.Net;
using DeskAI.Core.Ai;

namespace DeskAI.AI.Tests;

/// <summary>
/// The connection check: it must reach only where the saved choice points, carry nothing about
/// the computer, and refuse to claim anything it has not seen.
/// </summary>
public sealed class AiConnectionCheckTests
{
    [Fact]
    public async Task A_tampered_provider_id_is_refused_rather_than_guessed_at()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "some-service-nobody-reviewed",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");

        var result = await Check(settings, transport).CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Worked);
        Assert.Equal(AiProviderStatus.Disabled, result.Status);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task Consent_that_was_never_given_stops_the_check_before_anything_is_sent()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = false,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");

        var result = await Check(settings, transport).CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Worked);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task A_missing_key_is_reported_without_asking_the_service()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");

        var result = await Check(settings, transport, key: null).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.AuthenticationFailed, result.Status);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task A_reply_DeskAI_cannot_read_is_not_called_working()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Local,
            ProviderId = "local-compatible",
            Endpoint = "http://127.0.0.1:11434/v1/chat/completions",
            ModelId = "llama3",
        };
        // A 200 that is not a chat-completions envelope, as a wrong address on this computer
        // could well answer with.
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, """{"hello":"world"}""");

        var result = await Check(settings, transport).CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Worked);
        Assert.Equal(AiProviderStatus.MalformedResponse, result.Status);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task The_daily_cap_is_reserved_before_an_online_check_is_sent()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            ModelId = "test-model",
            CredentialReference = "DeskAI/OpenRouter",
            CloudConsentGranted = true,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var check = new ConfiguredAiConnectionCheck(
            new FakeAiSettingsRepository(settings),
            new FakeCredentialVault("generated-test-key"),
            transport,
            new FakeAiUsageBudget(allow: false),
            new FakeClock());

        var result = await check.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.CostLimitReached, result.Status);
        Assert.Equal(0, transport.CallCount);
    }

    private static ConfiguredAiConnectionCheck Check(
        AiSettings settings,
        FakeAiHttpTransport transport,
        string? key = "generated-test-key") =>
        new(new FakeAiSettingsRepository(settings),
            new FakeCredentialVault(key),
            transport,
            new FakeAiUsageBudget(),
            new FakeClock());
}
