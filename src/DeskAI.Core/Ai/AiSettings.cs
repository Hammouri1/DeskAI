namespace DeskAI.Core.Ai;

public sealed record AiSettings(
    AiMode Mode,
    string ProviderId,
    string ModelId,
    string? Endpoint,
    IReadOnlySet<DisclosureCategory> CloudDisclosures,
    string? CredentialReference,
    int TimeoutSeconds,
    int DailyRequestLimit,
    decimal? MaximumEstimatedCostUsd,
    bool CloudConsentGranted)
{
    public static AiSettings Default { get; } = new(
        AiMode.RuleEngineOnly,
        "none",
        "",
        null,
        new HashSet<DisclosureCategory> { DisclosureCategory.Extension },
        null,
        30,
        20,
        null,
        false);
}

public enum AiMode
{
    RuleEngineOnly,
    Local,
    Cloud,
}
