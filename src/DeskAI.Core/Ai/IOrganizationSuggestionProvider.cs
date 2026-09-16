using DeskAI.Core.Classification;

namespace DeskAI.Core.Ai;

/// <summary>
/// The one AI connection DeskAI has. It answers two kinds of question — where files belong, and
/// what a typed sentence means — and can do nothing else: it is handed a request and returns
/// advice, and holds no filesystem, executor, or credential-enumeration capability.
/// </summary>
public interface IOrganizationSuggestionProvider
{
    Task<OrganizationSuggestionResponse> SuggestAsync(
        OrganizationSuggestionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one typed sentence into the small JSON shape for its task. Sends the sentence and nothing else.</summary>
    Task<AiSentenceResponse> ReadSentenceAsync(
        AiSentenceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OrganizationSuggestionRequest(
    string SchemaVersion,
    Guid RequestId,
    IReadOnlyList<AiFileCandidate> Files,
    DisclosureSummary Disclosure,
    AiRequestLimits Limits)
{
    public const string CurrentSchemaVersion = "1";
}

public sealed record AiFileCandidate(
    Guid FileId,
    string? Extension,
    long? SizeBytes,
    DateTimeOffset? ModifiedAtUtc,
    string? FileName,
    string? RelativeFolder,
    string? FullPath);

public sealed record DisclosureSummary(
    IReadOnlySet<DisclosureCategory> Categories,
    int IncludedFileCount,
    int ProtectedFileCount);

public sealed record AiRequestLimits(
    TimeSpan Timeout,
    int MaximumFiles,
    int MaximumRequestBytes,
    int MaximumResponseBytes,
    decimal? MaximumEstimatedCostUsd)
{
    public static AiRequestLimits Default { get; } = new(
        TimeSpan.FromSeconds(30),
        100,
        64 * 1024,
        128 * 1024,
        null);
}

public sealed record OrganizationSuggestionResponse(
    AiProviderStatus Status,
    string ProviderDisplayName,
    IReadOnlyList<OrganizationSuggestion> Suggestions,
    string Message,
    AiUsage? Usage = null)
{
    public bool IsAvailable => Status == AiProviderStatus.Success;
}

public sealed record OrganizationSuggestion(
    Guid FileId,
    FileCategory Category,
    double Confidence,
    string Reason,
    AiSuggestionProvenance Provenance);

public sealed record AiUsage(int? InputTokens, int? OutputTokens, decimal? EstimatedCostUsd);

public enum AiSuggestionProvenance
{
    DeterministicFake,
    LocalAi,
    CloudAi,
}

public enum AiProviderStatus
{
    Success,
    Disabled,
    Offline,
    AuthenticationFailed,
    RateLimited,
    QuotaExceeded,
    TimedOut,
    Cancelled,
    MalformedResponse,
    SafetyRejected,
    CostLimitReached,
    ProviderError,
}

public enum DisclosureCategory
{
    Extension,
    Metadata,
    FileName,
    FolderNames,
    FullPath,
    ExtractedContent,
    ImageContent,
}
