using System.Net.Http.Headers;
using System.Text;

namespace DeskAI.AI.Transport;

public sealed class HttpClientAiTransport(HttpClient httpClient) : IAiHttpTransport
{
    public async Task<AiHttpResponse> PostJsonAsync(
        Uri endpoint,
        string json,
        IReadOnlyDictionary<string, string> headers,
        int maximumResponseBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maximumResponseBytes, 16 * 1024));
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maximumResponseBytes)
            {
                throw new AiResponseTooLargeException();
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return new AiHttpResponse(response.StatusCode, Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
