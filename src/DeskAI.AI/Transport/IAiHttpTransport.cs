using System.Net;

namespace DeskAI.AI.Transport;

public interface IAiHttpTransport
{
    Task<AiHttpResponse> PostJsonAsync(
        Uri endpoint,
        string json,
        IReadOnlyDictionary<string, string> headers,
        int maximumResponseBytes,
        CancellationToken cancellationToken);
}

public sealed record AiHttpResponse(HttpStatusCode StatusCode, string Body);

public sealed class AiResponseTooLargeException : Exception
{
    public AiResponseTooLargeException() : base("The AI response exceeded the configured size limit.")
    {
    }
}
