namespace DeskAI.Core.Ai;

/// <summary>What a typed sentence is for, which decides the shape AI must answer in.</summary>
public enum SentenceTask
{
    /// <summary>A phrase in the Search box, such as "the slides from my trip last summer".</summary>
    SearchPhrase,

    /// <summary>A rule in the person's own words, such as "put my bank statements somewhere tidy".</summary>
    RuleSentence,

    /// <summary>
    /// A question to "Ask DeskAI" on Home, such as "what's taking space in Downloads?" (V1.1,
    /// ADR 0035). The answer names what kind of thing was asked; DeskAI does the asking itself.
    /// </summary>
    Question,
}

/// <summary>
/// A request to read one sentence a person typed. It carries the sentence, the task, today's
/// date so "last summer" can be worked out, and limits. Nothing about any file.
/// </summary>
/// <remarks>
/// This is the whole of what leaves the computer for a sentence reading (V1.1, ADR 0033):
/// no file names, sizes, dates, folder names, or locations, because the sentence is the only
/// input the task has. The answer is a small fixed JSON shape that DeskAI turns into a
/// sentence in its own vocabulary; see <see cref="AiSentenceReading"/>.
/// </remarks>
public sealed record AiSentenceRequest(
    string SchemaVersion,
    Guid RequestId,
    SentenceTask Task,
    string Sentence,
    DateOnly TodayUtc,
    AiRequestLimits Limits)
{
    public const string CurrentSchemaVersion = "1";

    /// <summary>The same bound the deterministic readers use, so a pasted document is never a sentence.</summary>
    public const int MaxSentenceLength = 256;

    public static AiRequestLimits DefaultLimits { get; } = new(
        TimeSpan.FromSeconds(30),
        1,
        8 * 1024,
        8 * 1024,
        null);
}

/// <summary>
/// What an AI connection returned for a sentence: the raw JSON it answered with, or why not.
/// </summary>
/// <remarks>
/// The JSON is untrusted and bounded by the request's response limit. It is not parsed here;
/// <see cref="AiSentenceReading"/> reads it strictly, and only a sentence in DeskAI's own
/// vocabulary ever comes out of that.
/// </remarks>
public sealed record AiSentenceResponse(
    AiProviderStatus Status,
    string ProviderDisplayName,
    string? Json,
    string Message,
    AiUsage? Usage = null)
{
    public bool IsAvailable => Status == AiProviderStatus.Success && Json is not null;
}
