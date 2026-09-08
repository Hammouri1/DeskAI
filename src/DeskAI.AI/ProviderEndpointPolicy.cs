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
            throw new ArgumentException("Enter the address shown by the AI app running on this computer.", nameof(endpoint));
        }

        return uri;
    }

    public static string RequireModelId(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (modelId.Length > 100 || modelId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not ':' and not '/' and not '-'))
        {
            throw new ArgumentException("Enter a valid model name from your AI service.", nameof(modelId));
        }

        return modelId;
    }
}
