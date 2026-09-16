using System.ComponentModel;
using System.Net;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI;

/// <summary>
/// "Check this now": one tiny question to whichever AI is saved, so the answer on screen is
/// something DeskAI has seen happen rather than something it assumed.
/// </summary>
/// <remarks>
/// Saving a choice only writes it down. Until this existed nothing had ever proved the address
/// was listening or the key was accepted, and a person could only find out when a real feature
/// failed. The request is a fixed greeting with no file information in it at all, sent to the
/// same address the real features use — the compile-time one for an online service, the
/// loopback one the person typed for an AI on their computer — so a check that works means the
/// real path works. An online check spends one request from the daily allowance, because it is
/// a real paid request and a cap a button can skip is not a cap.
/// </remarks>
public sealed class ConfiguredAiConnectionCheck(
    IAiSettingsRepository settingsRepository,
    ICredentialVault credentialVault,
    IAiHttpTransport transport,
    IAiUsageBudget usageBudget,
    IClock clock) : IAiConnectionCheck
{
    /// <summary>Short, answerable by any model, and it says nothing about this computer.</summary>
    private const string Greeting = "Reply with the single word: ok";

    /// <summary>A greeting needs no room. A model that overruns this still proves it answered.</summary>
    private const int ReplyTokenAllowance = 5;

    private const string LocalName = "The AI on this computer";

    public async Task<AiConnectionResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        return settings.Mode switch
        {
            AiMode.Local => await CheckLocalAsync(settings, cancellationToken).ConfigureAwait(false),
            AiMode.Cloud => await CheckCloudAsync(settings, cancellationToken).ConfigureAwait(false),
            _ => NotSetUp("AI is off, so there is nothing to check. Choose how AI works, then press Save AI choice."),
        };
    }

    private async Task<AiConnectionResult> CheckLocalAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        Uri endpoint;
        string model;
        try
        {
            endpoint = ProviderEndpointPolicy.RequireLoopback(settings.Endpoint ?? string.Empty);
            model = ProviderEndpointPolicy.RequireModelId(settings.ModelId);
        }
        catch (ArgumentException)
        {
            return NotSetUp("The saved address or model name is not complete. Fill in both and press Save AI choice.");
        }

        return await AskAsync(endpoint, new Dictionary<string, string>(), model, LocalName, settings, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AiConnectionResult> CheckCloudAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        if (CloudProviderCatalog.Find(settings.ProviderId) is not { } provider ||
            settings.CredentialReference is null ||
            !settings.CloudConsentGranted)
        {
            return NotSetUp(
                "Online AI is not finished yet. Enter your key, turn on the sharing agreement, and press Save AI choice.");
        }

        string model;
        try
        {
            model = ProviderEndpointPolicy.RequireModelId(settings.ModelId);
        }
        catch (ArgumentException)
        {
            return NotSetUp($"No model name is saved. Type the model you want from {provider.DisplayName} and press Save AI choice.");
        }

        string? key;
        try
        {
            key = await credentialVault
                .RetrieveAsync(provider.CredentialReference, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return new AiConnectionResult(
                AiProviderStatus.AuthenticationFailed,
                provider.DisplayName,
                $"Windows could not open your saved {provider.DisplayName} key.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return new AiConnectionResult(
                AiProviderStatus.AuthenticationFailed,
                provider.DisplayName,
                $"Windows no longer has your {provider.DisplayName} key. Enter it again and press Save AI choice.");
        }

        // The same per-service daily cap the real features reserve against, counted before
        // anything is sent, so a person cannot spend past their limit by checking repeatedly.
        if (!await usageBudget.TryReserveRequestAsync(
                provider.Id,
                Math.Clamp(settings.DailyRequestLimit, 1, 1000),
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                cancellationToken).ConfigureAwait(false))
        {
            return new AiConnectionResult(
                AiProviderStatus.CostLimitReached,
                provider.DisplayName,
                "You have reached today's online AI limit, so nothing was sent. A check costs one request.");
        }

        return await AskAsync(
            provider.ChatCompletionsEndpoint,
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {key}" },
            model,
            provider.DisplayName,
            settings,
            cancellationToken,
            key).ConfigureAwait(false);
    }

    private async Task<AiConnectionResult> AskAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        string model,
        string name,
        AiSettings settings,
        CancellationToken cancellationToken,
        string? key = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = new[] { new { role = "user", content = Greeting } },
            temperature = 0,
            max_tokens = ReplyTokenAllowance,
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120)));
        try
        {
            var response = await transport.PostJsonAsync(
                endpoint,
                body,
                headers,
                AiRequestLimits.Default.MaximumResponseBytes,
                timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var said = key is null ? null : ServiceReply.Explanation(response.Body, key);
                return new AiConnectionResult(
                    MapStatus(response.StatusCode),
                    name,
                    said is null
                        ? MessageFor(response.StatusCode, name)
                        : $"{MessageFor(response.StatusCode, name)} {name} said: \"{said}\"");
            }

            return ReadAnswer(response.Body) is { Length: > 0 }
                ? new AiConnectionResult(AiProviderStatus.Success, name, $"Working. {name} answered.")
                : new AiConnectionResult(
                    AiProviderStatus.MalformedResponse,
                    name,
                    $"{name} replied, but not in a way DeskAI understands. Check the model name.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiConnectionResult(
                AiProviderStatus.TimedOut,
                name,
                $"{name} took too long to answer. Nothing was tried again.");
        }
        catch (OperationCanceledException)
        {
            return new AiConnectionResult(AiProviderStatus.Cancelled, name, "The check was stopped.");
        }
        catch (HttpRequestException)
        {
            return new AiConnectionResult(
                AiProviderStatus.Offline,
                name,
                string.Equals(name, LocalName, StringComparison.Ordinal)
                    ? "DeskAI could not reach it. Check the AI app on this computer is running, and that the address matches the one it shows."
                    : $"{name} could not be reached. Check your internet connection.");
        }
        catch (AiResponseTooLargeException)
        {
            return new AiConnectionResult(
                AiProviderStatus.MalformedResponse,
                name,
                $"{name} sent back more than DeskAI would read.");
        }
    }

    /// <summary>The reply text, read only far enough to know something answered. Nothing in it is believed.</summary>
    private static string? ReadAnswer(string responseBody)
    {
        try
        {
            using var envelope = JsonDocument.Parse(responseBody);
            return envelope.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString()?.Trim();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static AiConnectionResult NotSetUp(string message) =>
        new(AiProviderStatus.Disabled, "AI", message);

    private static AiProviderStatus MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => AiProviderStatus.AuthenticationFailed,
        HttpStatusCode.PaymentRequired => AiProviderStatus.QuotaExceeded,
        HttpStatusCode.TooManyRequests => AiProviderStatus.RateLimited,
        _ => AiProviderStatus.ProviderError,
    };

    /// <summary>
    /// What to do next, in the words of someone setting AI up rather than someone whose
    /// organizing has just failed: each sentence names the thing on this page to change.
    /// </summary>
    private static string MessageFor(HttpStatusCode status, string name) => status switch
    {
        HttpStatusCode.Unauthorized =>
            $"{name} did not accept your key. Paste it again above, copying only the key itself.",
        HttpStatusCode.NotFound =>
            $"{name} has no such model. Check the model name you typed.",
        HttpStatusCode.Forbidden => $"{name} refused the check.",
        HttpStatusCode.PaymentRequired =>
            $"Your {name} account needs credit before this model can be used. The key itself was accepted.",
        HttpStatusCode.TooManyRequests =>
            $"{name} is receiving too many requests right now. Wait a moment and check again.",
        _ => $"{name} returned an error, so DeskAI cannot say it is working yet.",
    };
}
