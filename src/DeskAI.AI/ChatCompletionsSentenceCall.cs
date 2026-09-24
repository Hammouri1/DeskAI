using System.Net;
using System.Text;
using System.Text.Json;
using DeskAI.AI.Transport;
using DeskAI.Core.Ai;

namespace DeskAI.AI;

/// <summary>
/// The one HTTP round trip for reading a sentence or sorting Desktop items, shared by the local
/// and online adapters. It posts the prompt to the address it is given, pulls the answer text
/// out of the chat-completions envelope, and hands that text back unread. Strict reading
/// happens in Core.
/// </summary>
internal static class ChatCompletionsSentenceCall
{
    public static Task<AiSentenceResponse> PostAsync(
        IAiHttpTransport transport,
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        string modelId,
        string displayName,
        AiSentenceRequest request,
        Func<AiHttpResponse, AiSentenceResponse> refusedBy,
        CancellationToken cancellationToken) =>
        PostPromptAsync(
            transport, endpoint, headers, modelId, displayName, AiPromptFactory.CreateSentencePrompt(request),
            request.Limits, $"{displayName} read the sentence.", refusedBy, cancellationToken);

    public static async Task<AiSentenceResponse> PostPromptAsync(
        IAiHttpTransport transport,
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        string modelId,
        string displayName,
        string prompt,
        AiRequestLimits limits,
        string successMessage,
        Func<AiHttpResponse, AiSentenceResponse> refusedBy,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = modelId,
            messages = new[] { new { role = "user", content = prompt } },
            response_format = new { type = "json_object" },
            temperature = 0,
        });
        if (Encoding.UTF8.GetByteCount(body) > limits.MaximumRequestBytes)
        {
            return Failure(displayName, AiProviderStatus.CostLimitReached, "This request is larger than your safety limit.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.Timeout);
        try
        {
            var response = await transport.PostJsonAsync(
                endpoint, body, headers, limits.MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return refusedBy(response);
            }

            string? answer;
            int? inputTokens = null;
            int? outputTokens = null;
            try
            {
                using var envelope = JsonDocument.Parse(response.Body);
                answer = envelope.RootElement.GetProperty("choices")[0]
                    .GetProperty("message").GetProperty("content").GetString();
                if (envelope.RootElement.TryGetProperty("usage", out var usage))
                {
                    inputTokens = ReadInt(usage, "prompt_tokens");
                    outputTokens = ReadInt(usage, "completion_tokens");
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                return Failure(displayName, AiProviderStatus.MalformedResponse, $"{displayName} sent a response DeskAI could not read.");
            }

            return string.IsNullOrWhiteSpace(answer)
                ? Failure(displayName, AiProviderStatus.MalformedResponse, $"{displayName} returned no reading.")
                : new AiSentenceResponse(
                    AiProviderStatus.Success,
                    displayName,
                    answer,
                    successMessage,
                    new AiUsage(inputTokens, outputTokens, null));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(displayName, AiProviderStatus.TimedOut, $"{displayName} took too long. Nothing else was tried.");
        }
        catch (OperationCanceledException)
        {
            return Failure(displayName, AiProviderStatus.Cancelled, "Stopped asking AI.");
        }
        catch (HttpRequestException)
        {
            return Failure(displayName, AiProviderStatus.Offline, $"{displayName} could not be reached. You can still type it DeskAI's way.");
        }
        catch (AiResponseTooLargeException)
        {
            return Failure(displayName, AiProviderStatus.MalformedResponse, "The AI answer was too large, so DeskAI ignored it.");
        }
    }

    public static AiSentenceResponse Failure(string displayName, AiProviderStatus status, string message) =>
        new(status, displayName, null, message);

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
}
