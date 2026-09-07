using System.Net;
using DeskAI.AI.Transport;
using DeskAI.Core.Ai;

namespace DeskAI.AI.Tests;

public sealed class ProviderResilienceTests
{
    [Fact]
    public async Task Gemini_RateLimitIsReportedWithoutRetry()
    {
        var transport = new FakeAiHttpTransport(HttpStatusCode.TooManyRequests, "provider details are not surfaced");
        var provider = new GeminiSuggestionProvider(
            transport, new FakeCredentialVault("key"), "DeskAI/Gemini", "test-model");

        var response = await provider.SuggestAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.RateLimited, response.Status);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task Local_MalformedEnvelopeIsContained()
    {
        var provider = new LocalOpenAiCompatibleSuggestionProvider(
            new FakeAiHttpTransport(HttpStatusCode.OK, "{\"choices\":[]}"),
            "http://localhost:11434/v1/chat/completions",
            "test-model");

        var response = await provider.SuggestAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.MalformedResponse, response.Status);
    }

    [Fact]
    public async Task Local_OfflineFailureIsFriendly()
    {
        var provider = new LocalOpenAiCompatibleSuggestionProvider(
            new ThrowingTransport(new HttpRequestException("sensitive transport detail")),
            "http://localhost:11434/v1/chat/completions",
            "test-model");

        var response = await provider.SuggestAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.Offline, response.Status);
        Assert.DoesNotContain("sensitive transport detail", response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_TimeoutAndExternalCancellationAreDistinct()
    {
        var timed = new LocalOpenAiCompatibleSuggestionProvider(
            new BlockingTransport(), "http://localhost:11434/v1/chat/completions", "test-model");
        var timedRequest = CreateRequest() with { Limits = AiRequestLimits.Default with { Timeout = TimeSpan.FromMilliseconds(10) } };
        var timeoutResponse = await timed.SuggestAsync(timedRequest, TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelledResponse = await timed.SuggestAsync(CreateRequest(), cancellation.Token);

        Assert.Equal(AiProviderStatus.TimedOut, timeoutResponse.Status);
        Assert.Equal(AiProviderStatus.Cancelled, cancelledResponse.Status);
    }

    [Fact]
    public async Task ConfiguredProvider_DailyCapStopsBeforeTransport()
    {
        var settings = AiSettings.Default with
        {
            Mode = AiMode.Cloud,
            ProviderId = "gemini",
            ModelId = "test-model",
            CredentialReference = "DeskAI/Gemini",
            CloudConsentGranted = true,
        };
        var transport = new FakeAiHttpTransport(HttpStatusCode.OK, "{}");
        var provider = new ConfiguredSuggestionProvider(
            new FakeAiSettingsRepository(settings),
            new FakeCredentialVault("key"),
            transport,
            new FakeAiUsageBudget(allow: false),
            new FakeClock());

        var response = await provider.SuggestAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(AiProviderStatus.CostLimitReached, response.Status);
        Assert.Equal(0, transport.CallCount);
    }

    private static OrganizationSuggestionRequest CreateRequest() => new(
        "1", Guid.NewGuid(),
        [new AiFileCandidate(Guid.NewGuid(), ".pdf", null, null, null, null, null)],
        new DisclosureSummary(new HashSet<DisclosureCategory> { DisclosureCategory.Extension }, 1, 0),
        AiRequestLimits.Default);

    private sealed class ThrowingTransport(Exception exception) : IAiHttpTransport
    {
        public Task<AiHttpResponse> PostJsonAsync(
            Uri endpoint,
            string json,
            IReadOnlyDictionary<string, string> headers,
            int maximumResponseBytes,
            CancellationToken cancellationToken) => Task.FromException<AiHttpResponse>(exception);
    }

    private sealed class BlockingTransport : IAiHttpTransport
    {
        public async Task<AiHttpResponse> PostJsonAsync(
            Uri endpoint,
            string json,
            IReadOnlyDictionary<string, string> headers,
            int maximumResponseBytes,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable test code.");
        }
    }
}
