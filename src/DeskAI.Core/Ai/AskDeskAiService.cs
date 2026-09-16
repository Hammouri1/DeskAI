using System.Globalization;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.Ai;

/// <summary>What the page may offer after an answer. Each is something a person could do by hand on that page.</summary>
public enum AskAction
{
    None,

    /// <summary>Open Search with <see cref="AskDeskAiAnswer.SearchPhrase"/> in the box.</summary>
    OpenSearch,

    /// <summary>Open Organize on <see cref="AskDeskAiAnswer.FolderId"/>.</summary>
    OpenOrganize,

    /// <summary>Connect <see cref="AskDeskAiAnswer.ConnectKind"/> through the Your folders card, dialog first.</summary>
    ConnectFolder,
}

/// <summary>
/// DeskAI's reply to a question, in DeskAI's own words, with at most one thing to do next.
/// </summary>
/// <param name="WasSent">False when DeskAI refused before handing anything to the AI connection.</param>
public sealed record AskDeskAiAnswer(
    bool WasSent,
    bool Succeeded,
    string Reply,
    AskAction Action = AskAction.None,
    string? SearchPhrase = null,
    Guid? FolderId = null,
    PersonalFolderKind? ConnectKind = null);

/// <summary>
/// "Ask DeskAI" on Home (V1.1, ADR 0035): a question in the person's own words, read by the AI
/// they set up into a kind of question, and answered by DeskAI itself from what it remembers.
/// </summary>
/// <remarks>
/// <para>
/// Only the typed question leaves the computer, through <see cref="SentenceAiService"/>, with
/// the same dialog, consent, catalog, and daily cap as every sentence. The AI answers with a
/// kind (search, space, tidy, unsure), the folder name the person wrote, and for a search the
/// search in DeskAI's words. Everything after that is deterministic: DeskAI searches its own
/// index, sums its own storage summary, or points at Organize. Nothing found is ever sent back;
/// there is no conversation, each question stands alone.
/// </para>
/// <para>
/// Every reply is DeskAI's wording. The AI's text never reaches the screen, and the only file
/// names shown are from the local index, exactly as Search shows them.
/// </para>
/// </remarks>
public sealed class AskDeskAiService(
    SentenceAiService sentences,
    FileSearchService search,
    StorageSummaryService storage,
    ConnectedFolderService folders,
    PersonalFolderPolicy personalFolders,
    IAppSettingsStore settings,
    IClock clock)
{
    /// <summary>How many matching file names a reply lists before saying "and N more".</summary>
    public const int NamesInReply = 5;

    /// <summary>
    /// Where the person's "yes, send my questions" is remembered, so DeskAI asks once rather
    /// than before every question (the owner's request, 2026-09-16). Start fresh removes it.
    /// </summary>
    /// <remarks>
    /// The stored value names the service and its address, so agreeing to send questions to one
    /// service is not agreeing to send them to the next one. It holds nothing about any file.
    /// </remarks>
    public const string AgreedKey = "ask.questions.agreed";

    public const string UnsureReply =
        "I'm not sure what you mean. You can ask things like \"what's taking space?\", \"find my slides from last month\", or \"tidy my Downloads\".";

    public Task<SentenceAiStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        sentences.GetStatusAsync(cancellationToken);

    /// <summary>Builds the request and says where it would go. Sends nothing.</summary>
    public Task<SentenceAiPreparation> PrepareAsync(string? question, CancellationToken cancellationToken = default) =>
        sentences.PrepareAsync(SentenceTask.Question, question, cancellationToken);

    /// <summary>
    /// Whether the person must be asked before this question goes out: nothing agreed yet, or
    /// agreed for a different service than the one set up now.
    /// </summary>
    public async Task<bool> NeedsPermissionAsync(CancellationToken cancellationToken = default)
    {
        var agreed = await settings.ReadAsync(AgreedKey, cancellationToken).ConfigureAwait(false);
        return !string.Equals(agreed, await AgreementAsync(cancellationToken).ConfigureAwait(false), StringComparison.Ordinal);
    }

    /// <summary>Remembers the yes for the service that is set up now.</summary>
    public async Task RememberPermissionAsync(CancellationToken cancellationToken = default)
    {
        var agreement = await AgreementAsync(cancellationToken).ConfigureAwait(false);
        if (agreement.Length > 0)
        {
            await settings.WriteAsync(AgreedKey, agreement, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Takes the yes back, so the next question asks again. Never confirmed: stopping is safe.</summary>
    public Task ForgetPermissionAsync(CancellationToken cancellationToken = default) =>
        settings.RemoveAsync(AgreedKey, cancellationToken);

    /// <summary>Sends the prepared question, reads the kind strictly, and answers from local memory.</summary>
    /// <param name="agreedNow">
    /// True when the person has just pressed Send in the first-time dialog. Without it, and
    /// without a remembered yes for this service, nothing is sent.
    /// </param>
    public async Task<AskDeskAiAnswer> AskAsync(
        SentenceAiQuestion question,
        bool agreedNow = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (question.Task != SentenceTask.Question)
        {
            return new(false, false, "That was not a question for Ask DeskAI.");
        }

        if (await NeedsPermissionAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!agreedNow)
            {
                return new(false, false, $"Nothing was sent, because you have not agreed to send your questions to {question.ServiceName} yet.");
            }

            await RememberPermissionAsync(cancellationToken).ConfigureAwait(false);
        }

        var (wasSent, response, message) = await sentences.SendAsync(question, cancellationToken).ConfigureAwait(false);
        if (!wasSent)
        {
            return new(false, false, message);
        }

        if (response is null || !response.IsAvailable)
        {
            return new(true, false, message);
        }

        var reading = AiSentenceReading.ReadQuestion(response.Json!, question.Request.Limits.MaximumResponseBytes);
        if (!reading.IsValid)
        {
            return new(true, false, $"{question.ServiceName}'s answer did not pass DeskAI's checks, so it was ignored. {reading.Problem}");
        }

        var intent = reading.Intent!;
        var connected = await folders.ListAsync(cancellationToken).ConfigureAwait(false);
        var named = intent.FolderName is null
            ? null
            : connected.FirstOrDefault(folder => string.Equals(folder.Name, intent.FolderName, StringComparison.OrdinalIgnoreCase));

        return intent.Kind switch
        {
            AskIntentKind.Search => await AnswerSearchAsync(intent, connected, named, cancellationToken).ConfigureAwait(false),
            AskIntentKind.Space => await AnswerSpaceAsync(connected, cancellationToken).ConfigureAwait(false),
            AskIntentKind.Tidy => AnswerTidy(intent, connected, named),
            _ => new(true, true, UnsureReply),
        };
    }

    private async Task<AskDeskAiAnswer> AnswerSearchAsync(
        AskIntent intent,
        IReadOnlyList<ConnectedFolder> connected,
        ConnectedFolder? named,
        CancellationToken cancellationToken)
    {
        if (connected.Count == 0)
        {
            return new(true, true, "No folders are connected yet, so there is nothing to search. Connect one from Your folders above.");
        }

        var phrase = intent.SearchSentence!;
        var outcome = await search.SearchAsync(phrase, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (outcome.UnderstoodNothing)
        {
            return new(true, true, $"I could not turn that into a search. Try Search and type it there.", AskAction.OpenSearch, phrase);
        }

        var hits = named is null ? outcome.Hits : outcome.Hits.Where(hit => hit.RootId == named.Id).ToArray();
        var where = named is not null
            ? $" in {named.Name}"
            : intent.FolderName is not null
                ? $" (\"{intent.FolderName}\" is not connected, so I looked in the folders you connected)"
                : string.Empty;
        if (hits.Count == 0)
        {
            return new(true, true, $"Nothing matched \"{phrase}\"{where}.", AskAction.OpenSearch, phrase);
        }

        var names = hits.Take(NamesInReply).Select(hit => hit.File.Name).ToArray();
        var more = hits.Count - names.Length;
        var list = string.Join(", ", names) + (more > 0 ? $", and {more} more" : string.Empty);
        var count = outcome.ReachedLimit ? $"{hits.Count}+ files" : Files(hits.Count);
        return new(true, true, $"Found {count} for \"{phrase}\"{where}: {list}.", AskAction.OpenSearch, phrase);
    }

    private async Task<AskDeskAiAnswer> AnswerSpaceAsync(IReadOnlyList<ConnectedFolder> connected, CancellationToken cancellationToken)
    {
        if (connected.Count == 0)
        {
            return new(true, true, "No folders are connected yet, so I cannot see what takes space. Connect one from Your folders above.");
        }

        var summary = await storage.BuildAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (!summary.HasAnything)
        {
            return new(true, true, "DeskAI has not remembered any files yet. Refresh a folder in Search and ask again.");
        }

        var kinds = summary.Categories
            .OrderByDescending(item => item.TotalSizeBytes)
            .Take(3)
            .Select(item => $"{item.Category} {Size(item.TotalSizeBytes)}");
        var largest = summary.LargestFiles.Count > 0 ? summary.LargestFiles[0] : null;
        var reply = $"Your {Folders(summary.FoldersIncluded)} hold {Size(summary.TotalSizeBytes)} across {Files(summary.TotalFiles)}. "
            + $"Biggest kinds: {string.Join(", ", kinds)}."
            + (largest is null ? string.Empty : $" Largest file: {largest.Name} ({Size(largest.SizeBytes)}) in {largest.RootName}.");
        return new(true, true, reply, AskAction.OpenSearch, $"larger than {NaturalLanguageQueryTranslator.LargeFileThresholdBytes / (1024 * 1024)} mb");
    }

    private AskDeskAiAnswer AnswerTidy(AskIntent intent, IReadOnlyList<ConnectedFolder> connected, ConnectedFolder? named)
    {
        if (named is not null)
        {
            return new(
                true,
                true,
                $"Open {named.Name} in Organize to see what DeskAI would move. Nothing moves until you press Tidy.",
                AskAction.OpenOrganize,
                FolderId: named.Id);
        }

        if (intent.FolderName is not null)
        {
            var personal = personalFolders.List()
                .FirstOrDefault(folder => string.Equals(folder.Name, intent.FolderName, StringComparison.OrdinalIgnoreCase));
            return personal is not null
                ? new(
                    true,
                    true,
                    $"{personal.Name} is not connected yet. Connect it and DeskAI will show what it would tidy.",
                    AskAction.ConnectFolder,
                    ConnectKind: personal.Kind)
                : new(
                    true,
                    true,
                    $"\"{intent.FolderName}\" is not one of your connected folders. DeskAI works only inside your Desktop, Downloads, Documents, and Pictures.");
        }

        return connected.Count switch
        {
            0 => new(true, true, "No folders are connected yet. Connect one from Your folders above and DeskAI will show what it would tidy."),
            1 => new(
                true,
                true,
                $"Open {connected[0].Name} in Organize to see what DeskAI would move. Nothing moves until you press Tidy.",
                AskAction.OpenOrganize,
                FolderId: connected[0].Id),
            _ => new(true, true, $"Which folder? You have: {string.Join(", ", connected.Select(folder => folder.Name))}."),
        };
    }

    /// <summary>The service and address a yes belongs to, or an empty string when AI is off.</summary>
    private async Task<string> AgreementAsync(CancellationToken cancellationToken)
    {
        var status = await sentences.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.IsSetUp ? $"{status.ServiceName}|{status.Destination}" : string.Empty;
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private static string Folders(int count) => count == 1 ? "1 connected folder" : $"{count} connected folders";

    private static string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.#} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };
}
