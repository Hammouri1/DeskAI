namespace DeskAI.Core.Ai;

/// <summary>
/// One online AI service DeskAI knows how to talk to.
/// </summary>
/// <remarks>
/// The destination is fixed in code, never typed by a user or chosen by a model. A
/// user brings the key and the model name for whichever service they already pay for;
/// they cannot redirect DeskAI to an arbitrary address, because a request carrying the
/// information they agreed to share must only ever reach the service they picked.
/// Each provider owns a separate credential reference so one saved key can never be
/// sent to a different company.
/// </remarks>
public sealed record CloudProvider
{
    private CloudProvider(
        string id,
        string displayName,
        Uri chatCompletionsEndpoint,
        string credentialReference,
        string modelHint,
        string keySource,
        string? keyPrefix)
    {
        Id = id;
        DisplayName = displayName;
        ChatCompletionsEndpoint = chatCompletionsEndpoint;
        CredentialReference = credentialReference;
        ModelHint = modelHint;
        KeySource = keySource;
        KeyPrefix = keyPrefix;
    }

    /// <summary>Stable identifier persisted in settings. Never shown as the main UI label.</summary>
    public string Id { get; }

    /// <summary>The company name a person recognizes, used in every message about this provider.</summary>
    public string DisplayName { get; }

    /// <summary>The single fixed HTTPS address DeskAI will post to for this provider.</summary>
    public Uri ChatCompletionsEndpoint { get; }

    /// <summary>Windows Credential Manager reference. SQLite stores this string, never the key.</summary>
    public string CredentialReference { get; }

    /// <summary>A friendly example of what a model name looks like for this service.</summary>
    public string ModelHint { get; }

    /// <summary>Where a person gets their own key, shown as plain text for them to visit.</summary>
    public string KeySource { get; }

    /// <summary>
    /// How this service's keys usually begin, when that is stable enough to mention.
    /// </summary>
    /// <remarks>
    /// Used only to warn, never to refuse: a service can change its key format, and DeskAI
    /// must not lock someone out over a guess. The warning catches the common mistake of
    /// pasting a key from a different service.
    /// </remarks>
    public string? KeyPrefix { get; }

    internal static CloudProvider Create(
        string id,
        string displayName,
        string chatCompletionsEndpoint,
        string credentialReference,
        string modelHint,
        string keySource,
        string? keyPrefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelHint);
        ArgumentException.ThrowIfNullOrWhiteSpace(keySource);

        // These guards protect the allow-list from a careless future edit, not from user
        // input: a catalog entry that is not a plain HTTPS address must not compile away
        // silently into a request destination.
        if (!Uri.TryCreate(chatCompletionsEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            !endpoint.IsDefaultPort ||
            endpoint.IsLoopback)
        {
            throw new ArgumentException(
                "A cloud provider must be a plain HTTPS address with no credentials, query, or fragment.",
                nameof(chatCompletionsEndpoint));
        }

        return new CloudProvider(id, displayName, endpoint, credentialReference, modelHint, keySource, keyPrefix);
    }
}

/// <summary>
/// The closed set of online AI services a user may choose between.
/// </summary>
/// <remarks>
/// Every entry speaks the same OpenAI-style chat-completions request and response shape,
/// which is why one adapter can serve all of them. A service with a different API shape
/// needs its own adapter and its own review before it can be listed here. There is
/// deliberately no way to add an entry at runtime.
/// </remarks>
public static class CloudProviderCatalog
{
    public static IReadOnlyList<CloudProvider> All { get; } =
    [
        CloudProvider.Create(
            "openrouter",
            "OpenRouter",
            "https://openrouter.ai/api/v1/chat/completions",
            "DeskAI/OpenRouter",
            "For example: openai/gpt-4o-mini",
            "openrouter.ai/keys",
            "sk-or-"),
        CloudProvider.Create(
            "openai",
            "OpenAI",
            "https://api.openai.com/v1/chat/completions",
            "DeskAI/OpenAI",
            "For example: gpt-4o-mini",
            "platform.openai.com/api-keys"),
        CloudProvider.Create(
            "groq",
            "Groq",
            "https://api.groq.com/openai/v1/chat/completions",
            "DeskAI/Groq",
            "For example: llama-3.1-8b-instant",
            "console.groq.com/keys",
            "gsk_"),
        CloudProvider.Create(
            "mistral",
            "Mistral",
            "https://api.mistral.ai/v1/chat/completions",
            "DeskAI/Mistral",
            "For example: mistral-small-latest",
            "console.mistral.ai/api-keys"),
        CloudProvider.Create(
            "deepseek",
            "DeepSeek",
            "https://api.deepseek.com/chat/completions",
            "DeskAI/DeepSeek",
            "For example: deepseek-chat",
            "platform.deepseek.com/api_keys"),
        CloudProvider.Create(
            "together",
            "Together AI",
            "https://api.together.xyz/v1/chat/completions",
            "DeskAI/TogetherAI",
            "For example: meta-llama/Llama-3-8b-chat-hf",
            "api.together.xyz/settings/api-keys"),
    ];

    public static CloudProvider? Find(string? providerId) =>
        string.IsNullOrWhiteSpace(providerId)
            ? null
            : All.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.Ordinal));

    public static bool IsKnown(string? providerId) => Find(providerId) is not null;
}
