namespace DeskAI.AI;

public static class ProviderEndpointPolicy
{
    public static Uri RequireLoopback(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !uri.IsLoopback ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("Local AI endpoint must be an HTTP(S) loopback URL without credentials, query, or fragment.", nameof(endpoint));
        }

        return uri;
    }

    public static string RequireModelId(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (modelId.Length > 100 || modelId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not ':' and not '/' and not '-'))
        {
            throw new ArgumentException("The AI model ID contains unsupported characters.", nameof(modelId));
        }

        return modelId;
    }
}
