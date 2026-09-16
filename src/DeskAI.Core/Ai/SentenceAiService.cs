using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;

namespace DeskAI.Core.Ai;

/// <summary>Whether a sentence can be read by AI right now, by whom, and where that is.</summary>
public sealed record SentenceAiStatus(bool IsSetUp, string ServiceName, string Destination, string Explanation);

/// <summary>
/// A sentence reading prepared and shown to the person but not yet sent. Sending sends this
/// same request, so what was shown is what goes.
/// </summary>
public sealed record SentenceAiQuestion(
    SentenceTask Task,
    string Sentence,
    AiMode Mode,
    string ProviderId,
    string ServiceName,
    string Destination,
    AiSentenceRequest Request);

/// <summary>A prepared question, or the plain reason there is none.</summary>
public sealed record SentenceAiPreparation(SentenceAiQuestion? Question, string Explanation);

/// <summary>What came back: a sentence in DeskAI's own words, or why not.</summary>
/// <param name="WasSent">False when DeskAI refused before handing anything to the AI connection.</param>
/// <param name="Reading">The sentence DeskAI's own reader will understand, or null.</param>
public sealed record SentenceAiAnswer(bool WasSent, bool Succeeded, string? Reading, string Message);

/// <summary>
/// Lets the AI the person set up read a sentence they typed on Search or Automatic tasks.
/// </summary>
/// <remarks>
/// <para>
/// Two steps with the person in between, like asking about files: <see cref="PrepareAsync"/>
/// says who would get the sentence and where, and only <see cref="AskAsync"/>, after they press
/// Send, sends that same sentence. Nothing about any file is ever part of the request.
/// </para>
/// <para>
/// The answer is read by <see cref="AiSentenceReading"/> into a sentence in DeskAI's own fixed
/// vocabulary and checked against the same deterministic reader a typed sentence meets, so a
/// reading that DeskAI itself cannot understand is refused rather than shown. The service holds
/// the settings and the AI connection and nothing else; a test fixes that.
/// </para>
/// </remarks>
public sealed class SentenceAiService(
    IAiSettingsRepository settingsRepository,
    IOrganizationSuggestionProvider ai,
    IClock clock)
{
    public async Task<SentenceAiStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Describe(await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Builds the request and says where it would go. Sends nothing.</summary>
    public async Task<SentenceAiPreparation> PrepareAsync(
        SentenceTask task,
        string? sentence,
        CancellationToken cancellationToken = default)
    {
        var trimmed = sentence?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new(null, "Type something first.");
        }

        if (trimmed.Length > AiSentenceRequest.MaxSentenceLength)
        {
            return new(null, $"Try a shorter sentence, under {AiSentenceRequest.MaxSentenceLength} characters.");
        }

        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var status = Describe(settings);
        if (!status.IsSetUp)
        {
            return new(null, status.Explanation);
        }

        var request = new AiSentenceRequest(
            AiSentenceRequest.CurrentSchemaVersion,
            Guid.NewGuid(),
            task,
            trimmed,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            AiSentenceRequest.DefaultLimits with { Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120)) });
        return new(
            new SentenceAiQuestion(task, trimmed, settings.Mode, settings.ProviderId, status.ServiceName, status.Destination, request),
            status.Explanation);
    }

    /// <summary>Sends a prepared question, after checking the AI choice is still the one the person saw.</summary>
    public async Task<SentenceAiAnswer> AskAsync(SentenceAiQuestion question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var target = AiTarget.Of(settings);
        if (!target.IsSetUp ||
            settings.Mode != question.Mode ||
            !string.Equals(settings.ProviderId, question.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(target.Destination, question.Destination, StringComparison.Ordinal))
        {
            return new(false, false, null, "Your AI choices changed since you looked, so nothing was sent. Try again to see where it would go.");
        }

        var response = await ai.ReadSentenceAsync(question.Request, cancellationToken).ConfigureAwait(false);
        if (!response.IsAvailable)
        {
            return new(true, false, null, response.Message);
        }

        var reading = AiSentenceReading.Read(question.Task, response.Json!, question.Request.Limits.MaximumResponseBytes);
        if (!reading.IsValid)
        {
            return new(true, false, null, $"{question.ServiceName}'s answer did not pass DeskAI's checks, so it was ignored. {reading.Problem}");
        }

        // The sentence must mean something to DeskAI's own reader, or showing it would only
        // move the confusion from one box to another.
        var understood = question.Task switch
        {
            SentenceTask.SearchPhrase => NaturalLanguageQueryTranslator.Translate(reading.Sentence, clock.UtcNow).UnderstoodAnything,
            SentenceTask.RuleSentence => RuleDraftTranslator.Draft(reading.Sentence).UnderstoodAnything,
            _ => false,
        };
        return understood
            ? new(true, true, reading.Sentence, $"{question.ServiceName} read it as \"{reading.Sentence}\". Change it if that is not what you meant.")
            : new(true, false, null, $"{question.ServiceName} did not find anything DeskAI can look for in that sentence.");
    }

    private static SentenceAiStatus Describe(AiSettings settings)
    {
        var target = AiTarget.Of(settings);
        return target.IsSetUp
            ? new(true, target.Name, target.Destination, $"{target.Name} would see only the words you typed.")
            : new(false, target.Name, target.Destination, "Turn on AI in Privacy and AI first.");
    }
}
