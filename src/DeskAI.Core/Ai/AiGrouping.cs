namespace DeskAI.Core.Ai;

/// <summary>One numbered thing on the Desktop as AI sees it: never a location or a DeskAI ID.</summary>
/// <param name="Kind"><c>"folder"</c> or <c>"file"</c>.</param>
/// <param name="Types">The kinds of files inside a folder, such as <c>"12 .py"</c>.</param>
public sealed record AiGroupingItem(int Number, string Kind, string Name, IReadOnlyList<string> Types, IReadOnlyList<string> SampleNames);

/// <summary>A request to sort numbered Desktop items into groups (ADR 0042).</summary>
public sealed record AiGroupingRequest(string SchemaVersion, Guid RequestId, IReadOnlyList<AiGroupingItem> Items, AiRequestLimits Limits)
{
    public const string CurrentSchemaVersion = "1";

    /// <summary>What a grouping request reveals, checked against the saved sharing choices for online AI.</summary>
    public static IReadOnlySet<DisclosureCategory> Discloses { get; } =
        new HashSet<DisclosureCategory> { DisclosureCategory.Extension, DisclosureCategory.FileName, DisclosureCategory.FolderNames };

    public static AiRequestLimits DefaultLimits { get; } = new(TimeSpan.FromSeconds(30), 260, 64 * 1024, 32 * 1024, null);
}

/// <summary>The raw, untrusted JSON AI answered with, or why not. Read strictly by <c>DesktopGroupReading</c>.</summary>
public sealed record AiGroupingResponse(AiProviderStatus Status, string ProviderDisplayName, string? Json, string Message, AiUsage? Usage = null)
{
    public bool IsAvailable => Status == AiProviderStatus.Success && Json is not null;

    public static AiGroupingResponse From(AiSentenceResponse raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return new(raw.Status, raw.ProviderDisplayName, raw.Json, raw.Message, raw.Usage);
    }
}
