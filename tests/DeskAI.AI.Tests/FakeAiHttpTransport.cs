using System.Net;
using DeskAI.AI.Transport;

namespace DeskAI.AI.Tests;

internal sealed class FakeAiHttpTransport(HttpStatusCode statusCode, string responseBody) : IAiHttpTransport
{
    public Uri? Endpoint { get; private set; }
    public string? RequestBody { get; private set; }
    public IReadOnlyDictionary<string, string>? Headers { get; private set; }
    public int CallCount { get; private set; }

    public Task<AiHttpResponse> PostJsonAsync(
        Uri endpoint,
        string json,
        IReadOnlyDictionary<string, string> headers,
        int maximumResponseBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        Endpoint = endpoint;
        RequestBody = json;
        Headers = headers;
        return Task.FromResult(new AiHttpResponse(statusCode, responseBody));
    }
}
