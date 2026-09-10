using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI;

/// <summary>
/// Talks to whichever vetted online AI service the user chose, using their own key.
/// </summary>
/// <remarks>
/// The destination comes from <see cref="CloudProvider"/>, a compile-time allow-list, so
/// no user input, saved setting, or model output can change where a request goes. The
/// adapter reads only the credential reference belonging to the selected provider, so a
/// key saved for one company is never sent to another. It has no filesystem, executor, or
/// credential-enumeration capability, and its answer is advice that Safety still judges.
/// </remarks>
public sealed class CloudChatCompletionsSuggestionProvider(
    IAiHttpTransport transport,
    ICredentialVault credentialVault,
    CloudProvider provider,
    string modelId) : IOrganizationSuggestionProvider
{
    private readonly CloudProvider _provider = provider
        ?? throw new ArgumentNullException(nameof(provider));
    private readonly string _modelId = ProviderEndpointPolicy.RequireModelId(modelId);

    public async Task<OrganizationSuggestionResponse> SuggestAsync(
        OrganizationSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = _provider.DisplayName;

        string? key;
        try
        {
            key = await credentialVault
                .RetrieveAsync(_provider.CredentialReference, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return Failure(AiProviderStatus.AuthenticationFailed, $"Windows could not open your saved {name} key.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Failure(AiProviderStatus.AuthenticationFailed, $"Add your {name} key in Settings first.");
        }

        var prompt = AiPromptFactory.CreateClassificationPrompt(request);
        var body = JsonSerializer.Serialize(new
        {
            model = _modelId,
            messages = new[] { new { role = "user", content = prompt } },
            response_format = new { type = "json_object" },
            temperature = 0,
        });
        if (Encoding.UTF8.GetByteCount(body) > request.Limits.MaximumRequestBytes)
        {
            return Failure(AiProviderStatus.CostLimitReached, "This request is larger than your safety limit.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Limits.Timeout);
        try
        {
            var response = await transport.PostJsonAsync(
                _provider.ChatCompletionsEndpoint,
                body,
                new Dictionary<string, string> { ["Authorization"] = $"Bearer {key}" },
                request.Limits.MaximumResponseBytes,
                timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var said = ServiceExplanation(response.Body, key);
                return Failure(
                    MapStatus(response.StatusCode),
                    said is null
                        ? MessageFor(response.StatusCode, name)
                        : $"{MessageFor(response.StatusCode, name)} {name} said: \"{said}\"");
            }

            string? structuredJson;
            int? inputTokens = null;
            int? outputTokens = null;
            try
            {
                using var envelope = JsonDocument.Parse(response.Body);
                structuredJson = envelope.RootElement.GetProperty("choices")[0]
                    .GetProperty("message").GetProperty("content").GetString();
                if (envelope.RootElement.TryGetProperty("usage", out var usage))
                {
                    inputTokens = ReadInt(usage, "prompt_tokens");
                    outputTokens = ReadInt(usage, "completion_tokens");
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                return Failure(AiProviderStatus.MalformedResponse, $"{name} sent a response DeskAI could not read.");
            }

            if (structuredJson is null)
            {
                return Failure(AiProviderStatus.MalformedResponse, $"{name} returned no suggestions.");
            }

            var parsed = StructuredSuggestionParser.Parse(
                structuredJson,
                request.Files.Select(file => file.FileId).ToHashSet(),
                request.Limits.MaximumResponseBytes,
                AiSuggestionProvenance.CloudAi);
            return parsed.IsValid
                ? new OrganizationSuggestionResponse(
                    AiProviderStatus.Success,
                    name,
                    parsed.Suggestions,
                    $"Found {parsed.Suggestions.Count} AI suggestion(s) for you to review.",
                    new AiUsage(inputTokens, outputTokens, null))
                : Failure(AiProviderStatus.SafetyRejected, "The AI answer did not pass DeskAI's safety checks.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(AiProviderStatus.TimedOut, $"{name} took too long. Nothing else was tried.");
        }
        catch (OperationCanceledException)
        {
            return Failure(AiProviderStatus.Cancelled, "Stopped getting AI ideas.");
        }
        catch (HttpRequestException)
        {
            return Failure(AiProviderStatus.Offline, $"{name} could not be reached. You can still organize without AI.");
        }
        catch (AiResponseTooLargeException)
        {
            return Failure(AiProviderStatus.MalformedResponse, "The AI answer was too large, so DeskAI ignored it.");
        }
    }

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private const int MaximumExplanationLength = 200;

    /// <summary>
    /// The service's own one-line reason for refusing, made safe to show.
    /// </summary>
    /// <remarks>
    /// "The key was not accepted" has several causes a person can fix — a mistyped key, a
    /// deleted one, one from another service — and the service's own words usually say
    /// which. The text is untrusted: it is shown as text only, stripped of control
    /// characters, shortened, and the saved key is removed in case the service echoes it.
    /// </remarks>
    private static string? ServiceExplanation(string body, string key)
    {
        string? message;
        try
        {
            using var document = JsonDocument.Parse(body);
            message = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var text) &&
                text.ValueKind == JsonValueKind.String
                    ? text.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var cleaned = new string(message.Select(character => char.IsControl(character) ? ' ' : character).ToArray())
            .Replace(key, "[your key]", StringComparison.Ordinal)
            .Trim();
        return cleaned.Length <= MaximumExplanationLength
            ? cleaned
            : string.Concat(cleaned.AsSpan(0, MaximumExplanationLength), "…");
    }

    private static AiProviderStatus MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => AiProviderStatus.AuthenticationFailed,
        HttpStatusCode.PaymentRequired => AiProviderStatus.QuotaExceeded,
        HttpStatusCode.TooManyRequests => AiProviderStatus.RateLimited,
        _ => AiProviderStatus.ProviderError,
    };

    private static string MessageFor(HttpStatusCode status, string name) => status switch
    {
        HttpStatusCode.Unauthorized =>
            $"{name} did not accept the saved key. Paste it again in Privacy and AI, copying only the key.",
        // A 403 is a refusal of this request (OpenRouter uses it for moderation), not a
        // verdict on the key, so it must not send people off to replace a working key.
        HttpStatusCode.Forbidden => $"{name} refused this request.",
        HttpStatusCode.PaymentRequired => $"Your {name} account needs credit before this model can be used.",
        HttpStatusCode.TooManyRequests => $"{name} is receiving too many requests. DeskAI did not try again automatically.",
        _ => $"{name} returned an error. Nothing else was tried.",
    };

    private OrganizationSuggestionResponse Failure(AiProviderStatus status, string message) =>
        new(status, _provider.DisplayName, [], message);
}
