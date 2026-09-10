using System.Collections.Concurrent;
using System.Net;
using DeskAI.AI.Transport;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;

namespace DeskAI.Presentation.Tests;

/// <summary>Stands in for Windows Credential Manager. Holds only generated test strings.</summary>
internal sealed class InMemoryCredentialVault : ICredentialVault
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task SaveAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        _secrets[reference] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secrets.TryGetValue(reference, out var secret) ? secret : null);

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default)
    {
        _secrets.TryRemove(reference, out _);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for the internet. Records what DeskAI would have sent and answers with a
/// canned reply, so no test ever reaches a real, paid AI service.
/// </summary>
internal sealed class RecordingAiTransport : IAiHttpTransport
{
    public List<(Uri Endpoint, string Body, IReadOnlyDictionary<string, string> Headers)> Requests { get; } = [];

    /// <summary>Builds the reply from the request, so it can echo the file IDs it was sent.</summary>
    public Func<string, AiHttpResponse> Reply { get; set; } =
        _ => new AiHttpResponse(HttpStatusCode.InternalServerError, "{}");

    public Task<AiHttpResponse> PostJsonAsync(
        Uri endpoint,
        string json,
        IReadOnlyDictionary<string, string> headers,
        int maximumResponseBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add((endpoint, json, headers));
        return Task.FromResult(Reply(json));
    }
}

internal sealed class RecordingNotifier : IFindingNotifier
{
    public List<string> Messages { get; } = [];

    public bool IsAvailable => true;

    public void Notify(string title, string message) => Messages.Add(message);
}
