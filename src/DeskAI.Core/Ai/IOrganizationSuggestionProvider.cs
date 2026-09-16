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

/// <summary>What the AI is asked to do with the files it is shown.</summary>
public enum AiSuggestionTask
{
    /// <summary>Name a category for each file; DeskAI's recipe chooses the folder (ADR 0020).</summary>
    Classify,

    /// <summary>
    /// Plan the folder: name a small set of plain folders and say which file goes into which
    /// (V1.1, ADR 0034). Every name is checked by <c>FolderNameCheck</c> before it is believed,
    /// and again by the path policy and the executor.
    /// </summary>
    PlanFolder,
}

public sealed record OrganizationSuggestionRequest(
    string SchemaVersion,
    Guid RequestId,
    IReadOnlyList<AiFileCandidate> Files,
    DisclosureSummary Disclosure,
    AiRequestLimits Limits,
    AiSuggestionTask Task = AiSuggestionTask.Classify)
{
    public const string CurrentSchemaVersion = "1";

    /// <summary>The most folders a plan may name. A plan with more is refused whole.</summary>
    public const int MaxPlanFolders = 12;
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

/// <param name="FolderName">
/// For a <see cref="AiSuggestionTask.PlanFolder"/> answer only: one plain folder name, already
/// checked by the parser. Null for a classification.
/// </param>
public sealed record OrganizationSuggestion(
    Guid FileId,
    FileCategory Category,
    double Confidence,
    string Reason,
    AiSuggestionProvenance Provenance,
    string? FolderName = null);

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
