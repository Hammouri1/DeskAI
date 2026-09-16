namespace DeskAI.AI;

public static class ProviderEndpointPolicy
{
    /// <remarks>
    /// An empty box gets its own sentence. The framework's own wording for a missing value
    /// ("The value cannot be an empty string…, Parameter 'endpoint'") reached the Privacy and
    /// AI page word for word, which told a person nothing about what to type.
    /// </remarks>
    public static Uri RequireLoopback(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException(
                "Enter the address of the AI app running on this computer, for example http://127.0.0.1:11434/v1/chat/completions.",
                nameof(endpoint));
        }

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

    /// <remarks>
    /// The empty case is worded separately for the same reason as <see cref="RequireLoopback"/>:
    /// the model box shows a grey example, which is easily mistaken for a value already filled in.
    /// </remarks>
    public static string RequireModelId(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException(
                "Enter the model name from your AI service in the \"Model name\" box. The grey example is only a hint.",
                nameof(modelId));
        }

        if (modelId.Length > 100 || modelId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not ':' and not '/' and not '-'))
        {
            throw new ArgumentException("Enter a valid model name from your AI service.", nameof(modelId));
        }

        return modelId;
    }
}
