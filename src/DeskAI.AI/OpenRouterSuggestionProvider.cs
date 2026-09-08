using System.Net;
using System.Text;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI;

public sealed class OpenRouterSuggestionProvider(
    IAiHttpTransport transport,
    ICredentialVault credentialVault,
    string credentialReference,
    string modelId) : IOrganizationSuggestionProvider
{
    private static readonly Uri Endpoint = new("https://openrouter.ai/api/v1/chat/completions");
    private readonly string _modelId = ProviderEndpointPolicy.RequireModelId(modelId);

    public async Task<OrganizationSuggestionResponse> SuggestAsync(
        OrganizationSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? key;
        try
        {
            key = await credentialVault.RetrieveAsync(credentialReference, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Failure(AiProviderStatus.AuthenticationFailed, "Windows could not open your saved OpenRouter key.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Failure(AiProviderStatus.AuthenticationFailed, "Add your OpenRouter key in Settings first.");
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
                Endpoint,
                body,
                new Dictionary<string, string> { ["Authorization"] = $"Bearer {key}" },
                request.Limits.MaximumResponseBytes,
                timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return Failure(MapStatus(response.StatusCode), MessageFor(response.StatusCode));
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
                return Failure(AiProviderStatus.MalformedResponse, "OpenRouter sent a response DeskAI could not read.");
            }

            if (structuredJson is null)
            {
                return Failure(AiProviderStatus.MalformedResponse, "OpenRouter returned no suggestions.");
            }

            var parsed = StructuredSuggestionParser.Parse(
                structuredJson,
                request.Files.Select(file => file.FileId).ToHashSet(),
                request.Limits.MaximumResponseBytes,
                AiSuggestionProvenance.CloudAi);
            return parsed.IsValid
                ? new OrganizationSuggestionResponse(
                    AiProviderStatus.Success,
                    "OpenRouter",
                    parsed.Suggestions,
                    $"Found {parsed.Suggestions.Count} AI suggestion(s) for you to review.",
                    new AiUsage(inputTokens, outputTokens, null))
                : Failure(AiProviderStatus.SafetyRejected, "The AI answer did not pass DeskAI's safety checks.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(AiProviderStatus.TimedOut, "OpenRouter took too long. Nothing else was tried.");
        }
        catch (OperationCanceledException)
        {
            return Failure(AiProviderStatus.Cancelled, "Stopped getting AI ideas.");
        }
        catch (HttpRequestException)
        {
            return Failure(AiProviderStatus.Offline, "OpenRouter could not be reached. You can still organize without AI.");
        }
        catch (AiResponseTooLargeException)
        {
            return Failure(AiProviderStatus.MalformedResponse, "The AI answer was too large, so DeskAI ignored it.");
        }
    }

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static AiProviderStatus MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiProviderStatus.AuthenticationFailed,
        HttpStatusCode.PaymentRequired => AiProviderStatus.QuotaExceeded,
        HttpStatusCode.TooManyRequests => AiProviderStatus.RateLimited,
        _ => AiProviderStatus.ProviderError,
    };

    private static string MessageFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "OpenRouter did not accept the saved key.",
        HttpStatusCode.PaymentRequired => "Your OpenRouter account needs credits before this model can be used.",
        HttpStatusCode.TooManyRequests => "OpenRouter is receiving too many requests. DeskAI did not try again automatically.",
        _ => "OpenRouter returned an error. Nothing else was tried.",
    };

    private static OrganizationSuggestionResponse Failure(AiProviderStatus status, string message) =>
        new(status, "OpenRouter", [], message);
}
