using System.Globalization;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tidy;

/// <summary>Whether AI can be asked about files right now, and what it would see.</summary>
public sealed record TidyAiStatus(
    bool IsSetUp,
    bool CanShareAnything,
    string ServiceName,
    string Destination,
    string Explanation);

/// <summary>
/// A request to AI, prepared and shown to the person but not yet sent.
/// </summary>
/// <remarks>
/// <see cref="FileLines"/> describe, one line per file, exactly the fields in
/// <see cref="Request"/>. Sending sends this same request, so what was shown is what goes.
/// <see cref="FilesByStandIn"/> maps the random numbers the AI sees back to the real files and
/// never leaves the computer.
/// </remarks>
public sealed record TidyAiQuestion(
    Guid RootId,
    AiMode Mode,
    string ProviderId,
    string ServiceName,
    string Destination,
    IReadOnlySet<DisclosureCategory> Categories,
    string SharedInWords,
    IReadOnlyList<string> FileLines,
    int LeftOutCount,
    OrganizationSuggestionRequest Request,
    IReadOnlyDictionary<Guid, FileItem> FilesByStandIn);

/// <summary>A prepared question, or the plain reason there is none.</summary>
public sealed record TidyAiPreparation(TidyAiQuestion? Question, string Explanation);

/// <summary>What came back.</summary>
/// <param name="WasSent">
/// False when DeskAI refused before handing anything to the AI connection. True does not mean
/// the service answered: the connection can still refuse, for example at the daily limit.
/// </param>
/// <param name="Advice">Accepted ideas, keyed by the real file ID.</param>
public sealed record TidyAiAnswer(
    bool WasSent,
    bool Succeeded,
    IReadOnlyDictionary<Guid, TidyAiAdvice> Advice,
    string Message);

/// <summary>
/// Asks the AI the person set up where files in their own folder belong.
/// </summary>
/// <remarks>
/// <para>
/// This is the first place anything about a person's own files can leave the computer, so it
/// works in two steps with the person in between: <see cref="PrepareAsync"/> builds the request
/// and describes it in words, and only <see cref="AskAsync"/>, called after they press Send,
/// hands that same request over. See the disclosure review of 2026-09-10.
/// </para>
/// <para>
/// It cannot look in a folder or open a file: it is handed file descriptions the page already
/// has, and it holds no scanner, reader, index, or executor. A test fails if one is added.
/// </para>
/// </remarks>
public sealed class TidyAiService(
    IAuthorizedRootRepository roots,
    IAiSettingsRepository settingsRepository,
    IOrganizationSuggestionProvider ai,
    IPlanSafetyCheck safety)
{
    /// <summary>An idea below this confidence is shown as "AI isn't sure" and starts unticked.</summary>
    public const double UnsureBelow = 0.7;

    /// <summary>One press is one request; this keeps it small enough to read and to pay for.</summary>
    public const int MaxFilesPerRequest = 100;

    /// <summary>
    /// The most that can ever be sent about a file in someone's own folder, whatever the saved
    /// choices allow: its type, size and date, and name.
    /// </summary>
    /// <remarks>
    /// Full locations are left out even when allowed, because they carry the Windows user name
    /// and folder layout and add nothing to working out what a loose file is. Folder names are
    /// left out because loose top-level files have none. Contents and images are not sendable
    /// anywhere in DeskAI.
    /// </remarks>
    public static IReadOnlySet<DisclosureCategory> RealFolderShareable { get; } = new HashSet<DisclosureCategory>
    {
        DisclosureCategory.Extension,
        DisclosureCategory.Metadata,
        DisclosureCategory.FileName,
    };

    private static readonly IReadOnlyDictionary<Guid, TidyAiAdvice> NoAdvice = new Dictionary<Guid, TidyAiAdvice>();

    public async Task<TidyAiStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Describe(await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Builds the request and describes it. Sends nothing.</summary>
    public async Task<TidyAiPreparation> PrepareAsync(
        Guid rootId,
        IReadOnlyList<FileItem> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanTidy(root))
        {
            return new(null, "DeskAI only asks AI about a folder you allowed it to tidy.");
        }

        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var status = Describe(settings);
        if (!status.IsSetUp || !status.CanShareAnything)
        {
            return new(null, status.Explanation);
        }

        var standIns = new Dictionary<Guid, FileItem>();
        var described = new List<FileItem>();
        foreach (var file in files)
        {
            // The page only offers loose files DeskAI would move, but this is the last point
            // before anything leaves the computer, so it checks again rather than trusting that.
            if (described.Count == MaxFilesPerRequest ||
                file.RelativePath.Contains(Path.DirectorySeparatorChar) ||
                file.Traits != FileTraits.None ||
                safety.IsProtected(root, file.RelativePath))
            {
                continue;
            }

            // A scanned file's ID is a fingerprint of its path and the same on every scan. The
            // AI gets a random number instead, made for this one request.
            var standIn = Guid.NewGuid();
            standIns[standIn] = file;
            described.Add(new FileItem(standIn, file.RelativePath, file.Kind, file.SizeBytes, file.CreatedAtUtc, file.ModifiedAtUtc));
        }

        if (described.Count == 0)
        {
            return new(null, "None of these files can be sent to AI.");
        }

        var shared = Shareable(settings);
        var limits = new AiRequestLimits(
            TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120)),
            described.Count,
            64 * 1024,
            128 * 1024,
            settings.MaximumEstimatedCostUsd);
        var request = AiRequestBuilder.Build(root, described, new HashSet<Guid>(), shared, limits);
        var (_, name, destination) = Target(settings);
        return new(
            new TidyAiQuestion(
                root.Id,
                settings.Mode,
                settings.ProviderId,
                name,
                destination,
                new HashSet<DisclosureCategory>(shared),
                Words(shared),
                request.Files.Select(DescribeLine).ToArray(),
                files.Count - described.Count,
                request,
                standIns),
            status.Explanation);
    }

    /// <summary>Sends a prepared question, after checking nothing changed since it was shown.</summary>
    public async Task<TidyAiAnswer> AskAsync(TidyAiQuestion question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        var root = await roots.FindAsync(question.RootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanTidy(root))
        {
            return Refused("DeskAI can no longer tidy this folder, so nothing was sent.");
        }

        // What was agreed is what was shown. If the AI choice, where it goes, or what may be
        // shared has changed since, the person has not seen this request under today's rules.
        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var (isSetUp, _, destination) = Target(settings);
        if (!isSetUp ||
            settings.Mode != question.Mode ||
            !string.Equals(settings.ProviderId, question.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(destination, question.Destination, StringComparison.Ordinal) ||
            !question.Categories.IsSubsetOf(Shareable(settings)))
        {
            return Refused("Your AI choices changed since you looked, so nothing was sent. Ask again to see what would be sent.");
        }

        var response = await ai.SuggestAsync(question.Request, cancellationToken).ConfigureAwait(false);
        if (!response.IsAvailable)
        {
            return new(true, false, NoAdvice, response.Message);
        }

        // The parser already refuses numbers it did not send; checking again here means a
        // future connection that forgets to cannot attach an idea to a file nobody asked about.
        if (response.Suggestions.Any(item => !question.FilesByStandIn.ContainsKey(item.FileId)) ||
            response.Suggestions.Select(item => item.FileId).Distinct().Count() != response.Suggestions.Count)
        {
            return new(true, false, NoAdvice, "The AI answer did not pass DeskAI's safety checks, so it was ignored.");
        }

        var advice = new Dictionary<Guid, TidyAiAdvice>();
        foreach (var item in response.Suggestions)
        {
            var file = question.FilesByStandIn[item.FileId];
            advice[file.Id] = new TidyAiAdvice(
                item.Category,
                item.Confidence < UnsureBelow,
                question.ServiceName,
                question.Mode == AiMode.Local ? OperationProvenance.LocalAi : OperationProvenance.CloudAi,
                file.SizeBytes,
                file.ModifiedAtUtc);
        }

        var placed = advice.Values.Count(item => item.Category != FileCategory.Unknown);
        var unsure = advice.Values.Count(item => item.Category != FileCategory.Unknown && item.IsUnsure);
        var message = $"{question.ServiceName} suggested a place for {placed} of {Files(question.FilesByStandIn.Count)}.";
        if (unsure > 0)
        {
            message += unsure == 1
                ? " It wasn't sure about 1, so that one starts unticked."
                : $" It wasn't sure about {unsure}, so those start unticked.";
        }

        return new(true, true, advice, message + " Nothing has moved.");
    }

    private static TidyAiAnswer Refused(string message) => new(false, false, NoAdvice, message);

    private static TidyAiStatus Describe(AiSettings settings)
    {
        var (isSetUp, name, destination) = Target(settings);
        if (!isSetUp)
        {
            return new(false, false, name, destination, "Turn on AI in Privacy and AI first.");
        }

        var shared = Shareable(settings);
        return shared.Count == 0
            ? new(true, false, name, destination,
                $"Your choices in Privacy and AI let {name} see nothing about a file, so it cannot help. You can allow file types there.")
            : new(true, true, name, destination, $"{name} would see: {Words(shared)}.");
    }

    /// <summary>Who would be asked, and where that is. Not set up means nothing can be sent.</summary>
    private static (bool IsSetUp, string Name, string Destination) Target(AiSettings settings)
    {
        if (settings.Mode == AiMode.Local &&
            Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var local) &&
            local.IsLoopback)
        {
            return (true, "Local AI", $"{local.Authority} on this computer");
        }

        if (settings.Mode == AiMode.Cloud &&
            settings.CloudConsentGranted &&
            settings.CredentialReference is not null &&
            CloudProviderCatalog.Find(settings.ProviderId) is { } provider)
        {
            return (true, provider.DisplayName, provider.ChatCompletionsEndpoint.Host);
        }

        return (false, "AI", string.Empty);
    }

    private static HashSet<DisclosureCategory> Shareable(AiSettings settings) =>
        [.. settings.CloudDisclosures.Where(RealFolderShareable.Contains)];

    private static string Words(IEnumerable<DisclosureCategory> categories)
    {
        var words = categories.Order().Select(category => category switch
        {
            DisclosureCategory.Extension => "file types",
            DisclosureCategory.Metadata => "sizes and dates",
            DisclosureCategory.FileName => "file names",
            _ => "other information",
        }).ToArray();
        return words.Length == 0 ? "nothing" : string.Join(", ", words);
    }

    /// <summary>One file as the AI will see it, built from the request itself.</summary>
    private static string DescribeLine(AiFileCandidate candidate)
    {
        var parts = new List<string>
        {
            candidate.FileName
                ?? (string.IsNullOrEmpty(candidate.Extension) ? "A file with no type" : $"A {candidate.Extension} file"),
        };
        if (candidate.SizeBytes is long size)
        {
            parts.Add(Size(size));
        }

        if (candidate.ModifiedAtUtc is DateTimeOffset changed)
        {
            parts.Add("changed " + changed.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture));
        }

        return string.Join(" Â· ", parts);
    }

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";
}
