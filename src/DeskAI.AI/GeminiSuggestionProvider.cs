using System.Net;
using System.Text;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI;

public sealed class GeminiSuggestionProvider(
    IAiHttpTransport transport,
    ICredentialVault credentialVault,
    string credentialReference,
    string modelId) : IOrganizationSuggestionProvider
{
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
            return Failure(AiProviderStatus.AuthenticationFailed, "Windows could not retrieve the Gemini credential.");
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            return Failure(AiProviderStatus.AuthenticationFailed, "Add your Gemini API key in Privacy & AI settings.");
        }

        var prompt = AiPromptFactory.CreateClassificationPrompt(request);
        var body = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                temperature = 0,
                responseMimeType = "application/json",
            },
        });
        if (Encoding.UTF8.GetByteCount(body) > request.Limits.MaximumRequestBytes)
        {
            return Failure(AiProviderStatus.CostLimitReached, "The Gemini request is larger than your configured limit.");
        }

        var endpoint = new Uri(
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_modelId)}:generateContent");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Limits.Timeout);
        try
        {
            var response = await transport.PostJsonAsync(
                endpoint,
                body,
                new Dictionary<string, string> { ["x-goog-api-key"] = key },
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
                structuredJson = envelope.RootElement.GetProperty("candidates")[0]
                    .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                if (envelope.RootElement.TryGetProperty("usageMetadata", out var usage))
                {
                    inputTokens = ReadInt(usage, "promptTokenCount");
                    outputTokens = ReadInt(usage, "candidatesTokenCount");
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                return Failure(AiProviderStatus.MalformedResponse, "Gemini returned an unreadable response.");
            }

            if (structuredJson is null)
            {
                return Failure(AiProviderStatus.MalformedResponse, "Gemini returned no structured suggestions.");
            }

            var parsed = StructuredSuggestionParser.Parse(
                structuredJson,
                request.Files.Select(file => file.FileId).ToHashSet(),
                request.Limits.MaximumResponseBytes,
                AiSuggestionProvenance.CloudAi);
            return parsed.IsValid
                ? new OrganizationSuggestionResponse(
                    AiProviderStatus.Success,
                    "Google Gemini",
                    parsed.Suggestions,
                    $"Received {parsed.Suggestions.Count} validated Gemini suggestion(s).",
                    new AiUsage(inputTokens, outputTokens, null))
                : Failure(AiProviderStatus.SafetyRejected, "Gemini's response did not pass DeskAI validation.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(AiProviderStatus.TimedOut, "Gemini took too long. DeskAI did not try another provider.");
        }
        catch (OperationCanceledException)
        {
            return Failure(AiProviderStatus.Cancelled, "The Gemini request was cancelled.");
        }
        catch (HttpRequestException)
        {
            return Failure(AiProviderStatus.Offline, "Gemini is unreachable. The rule engine remains available.");
        }
        catch (AiResponseTooLargeException)
        {
            return Failure(AiProviderStatus.MalformedResponse, "Gemini's response exceeded the configured limit.");
        }
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
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Gemini rejected the API key or permission.",
        HttpStatusCode.TooManyRequests => "Gemini reported a rate or quota limit. DeskAI did not retry automatically.",
        _ => "Gemini returned an error. No other provider was used.",
    };

    private static OrganizationSuggestionResponse Failure(AiProviderStatus status, string message) =>
        new(status, "Google Gemini", [], message);
}
