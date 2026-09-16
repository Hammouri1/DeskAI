using System.Net;
using System.Text;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.Core.Ai;

namespace DeskAI.AI;

public sealed class LocalOpenAiCompatibleSuggestionProvider : IOrganizationSuggestionProvider
{
    private readonly IAiHttpTransport _transport;
    private readonly Uri _endpoint;
    private readonly string _modelId;

    public LocalOpenAiCompatibleSuggestionProvider(IAiHttpTransport transport, string endpoint, string modelId)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _endpoint = ProviderEndpointPolicy.RequireLoopback(endpoint);
        _modelId = ProviderEndpointPolicy.RequireModelId(modelId);
    }

    public async Task<OrganizationSuggestionResponse> SuggestAsync(
        OrganizationSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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
            return Failure(AiProviderStatus.CostLimitReached, "The local AI request is larger than your configured limit.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Limits.Timeout);
        try
        {
            var response = await _transport.PostJsonAsync(
                _endpoint, body, new Dictionary<string, string>(), request.Limits.MaximumResponseBytes, timeout.Token)
                .ConfigureAwait(false);
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
                return Failure(AiProviderStatus.MalformedResponse, "The local AI returned an unreadable response.");
            }

            if (structuredJson is null)
            {
                return Failure(AiProviderStatus.MalformedResponse, "The local AI returned no structured suggestions.");
            }

            var parsed = StructuredSuggestionParser.Parse(
                structuredJson,
                request.Files.Select(file => file.FileId).ToHashSet(),
                request.Limits.MaximumResponseBytes,
                AiSuggestionProvenance.LocalAi);
            return parsed.IsValid
                ? new OrganizationSuggestionResponse(
                    AiProviderStatus.Success, "Local AI", parsed.Suggestions,
                    $"Received {parsed.Suggestions.Count} validated local suggestion(s).",
                    new AiUsage(inputTokens, outputTokens, 0))
                : Failure(AiProviderStatus.SafetyRejected, "The local AI response did not pass DeskAI validation.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(AiProviderStatus.TimedOut, "The local AI took too long. No cloud fallback was used.");
        }
        catch (OperationCanceledException)
        {
            return Failure(AiProviderStatus.Cancelled, "The local AI request was cancelled.");
        }
        catch (HttpRequestException)
        {
            return Failure(AiProviderStatus.Offline, "The local AI endpoint is unavailable. No cloud fallback was used.");
        }
        catch (AiResponseTooLargeException)
        {
            return Failure(AiProviderStatus.MalformedResponse, "The local AI response exceeded the configured limit.");
        }
    }

    public Task<AiSentenceResponse> ReadSentenceAsync(
        AiSentenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChatCompletionsSentenceCall.PostAsync(
            _transport,
            _endpoint,
            new Dictionary<string, string>(),
            _modelId,
            "Local AI",
            request,
            response => ChatCompletionsSentenceCall.Failure("Local AI", MapStatus(response.StatusCode), MessageFor(response.StatusCode)),
            cancellationToken);
    }

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static AiProviderStatus MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiProviderStatus.AuthenticationFailed,
        HttpStatusCode.TooManyRequests => AiProviderStatus.RateLimited,
        _ => AiProviderStatus.ProviderError,
    };

    private static string MessageFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The local endpoint rejected authentication.",
        HttpStatusCode.TooManyRequests => "The local endpoint is busy. DeskAI did not retry automatically.",
        _ => "The local AI endpoint returned an error.",
    };

    private static OrganizationSuggestionResponse Failure(AiProviderStatus status, string message) =>
        new(status, "Local AI", [], message);
}
