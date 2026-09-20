using DeskAI.Core.Abstractions;
using DeskAI.Core.Content;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Search;

/// <summary>One file whose words matched, with the line it matched on.</summary>
/// <remarks>
/// <see cref="Snippet"/> is a short piece of the file, so a person can see why it matched
/// rather than taking the match on trust. It is untrusted text: it is displayed and nothing
/// more, exactly like a file name.
/// </remarks>
public sealed record ContentHit(string RootName, string RelativePath, string Name, string Snippet)
{
    public string? Section { get; init; }
}

/// <summary>One attempted file and what the bounded local read established.</summary>
public enum ContentCheckStatus { Matched, NoMatch, CouldNotRead }

public sealed record ContentFileCheck(
    string RootName,
    string RelativePath,
    string Name,
    ContentCheckStatus Status,
    bool WasTruncated,
    string Explanation);

/// <summary>
/// What looking inside files found, and how much was actually looked at.
/// </summary>
/// <remarks>
/// <see cref="FilesRead"/> and <see cref="ReachedLimit"/> exist so the UI can say what was
/// searched instead of implying every file was. "No matches" and "no matches in the first
/// fifty files" mean different things to someone deciding whether to trust the answer.
/// </remarks>
public sealed record ContentSearchOutcome(
    IReadOnlyList<ContentHit> Hits,
    int FoldersIncluded,
    int FilesRead,
    bool ReachedLimit,
    int FilesTruncated = 0,
    int FilesSkipped = 0)
{
    public static ContentSearchOutcome NotAllowed { get; } = new([], 0, 0, false);

    /// <summary>Only names and fixed status wording for this result; never persisted.</summary>
    public IReadOnlyList<ContentFileCheck> CheckedFiles { get; init; } = [];

    public bool WasSearched => FoldersIncluded > 0;
}

/// <summary>
/// Looks for words written inside supported files of folders that allowed it.
/// </summary>
/// <remarks>
/// <para>
/// This is the only consumer of file content in DeskAI, and it is what the content
/// permission is for. It reads through <see cref="IContentTextExtractor"/> and never touches
/// a file itself, so every refusal that guards reading applies here without being repeated.
/// </para>
/// <para>
/// Scope is decided by <see cref="RootCapabilities.CanReadContent"/>, so a folder connected
/// only for names, sizes, and dates is never opened. A person who has allowed nothing gets
/// an empty result, not an error, because not having granted a permission is a normal state
/// rather than a failure.
/// </para>
/// <para>
/// Nothing read is stored. The snippet lives as long as the result and no longer.
/// </para>
/// </remarks>
public sealed class ContentSearchService(
    IAuthorizedRootRepository roots,
    IFileIndex index,
    IContentTextExtractor extractor)
{
    /// <summary>
    /// How many files one search may open.
    /// </summary>
    /// <remarks>
    /// A bound, not a performance tweak. Without it, typing a common word in a folder of ten
    /// thousand files would open ten thousand files. Fifty is enough to be useful on a
    /// focused folder and small enough that the cost of a search stays predictable and
    /// visible; the UI says when the limit was reached rather than implying a full sweep.
    /// </remarks>
    public const int MaxFilesRead = 50;

    /// <summary>
    /// Below this many characters a phrase is not searched for inside files.
    /// </summary>
    /// <remarks>
    /// One or two letters match almost every document, so the results would be noise while
    /// the cost — opening fifty files — would be real.
    /// </remarks>
    public const int MinimumPhraseLength = 3;

    /// <summary>How much of the surrounding line to show on either side of a match.</summary>
    private const int SnippetPadding = 60;
    private static readonly TimeSpan MaxSearchTime = TimeSpan.FromSeconds(20);

    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IFileIndex _index = index;
    private readonly IContentTextExtractor _extractor = extractor;

    public Task<ContentSearchOutcome> SearchAsync(
        string? phrase,
        CancellationToken cancellationToken = default) =>
        SearchAsync(phrase, DateTimeOffset.UtcNow, selectedRootId: null, cancellationToken);

    public Task<ContentSearchOutcome> SearchAsync(
        string? phrase,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        SearchAsync(phrase, nowUtc, selectedRootId: null, cancellationToken);

    public async Task<ContentSearchOutcome> SearchAsync(
        string? phrase,
        DateTimeOffset nowUtc,
        Guid? selectedRootId,
        CancellationToken cancellationToken = default)
    {
        var translation = NaturalLanguageQueryTranslator.Translate(phrase, nowUtc);
        var needle = translation.Query.PathContains ?? string.Empty;
        if (needle.Length < MinimumPhraseLength)
        {
            return ContentSearchOutcome.NotAllowed;
        }

        var allowed = (await _roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(root => RootCapabilities.CanReadContent(root)
                && (selectedRootId is null || root.Id == selectedRootId))
            .ToArray();

        if (allowed.Length == 0)
        {
            return ContentSearchOutcome.NotAllowed;
        }

        var hits = new List<ContentHit>();
        var checks = new List<ContentFileCheck>();
        var filesRead = 0;
        var filesTruncated = 0;
        var filesSkipped = 0;
        var reachedLimit = false;
        var started = System.Diagnostics.Stopwatch.StartNew();

        var filter = translation.Query;
        // The free words are matched inside a file, not required in its name. All other
        // filters still narrow the candidate set before any handle is opened.
        var candidates = new SearchQuery(
            extensions: filter.Extensions,
            categories: filter.Categories,
            kinds: filter.Kinds,
            minSizeBytes: filter.MinSizeBytes,
            maxSizeBytes: filter.MaxSizeBytes,
            modifiedAfterUtc: filter.ModifiedAfterUtc,
            modifiedBeforeUtc: filter.ModifiedBeforeUtc,
            limit: SearchQuery.MaxLimit);

        foreach (var root in allowed)
        {
            var indexed = await _index.SearchRootAsync(root.Id, candidates, cancellationToken)
                .ConfigureAwait(false);
            if (indexed.Count >= SearchQuery.MaxLimit)
            {
                reachedLimit = true;
            }

            foreach (var file in indexed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((filter.Extensions.Count > 0 && !filter.Extensions.Contains(Path.GetExtension(file.RelativePath).ToLowerInvariant()))
                    || (filter.Categories.Count > 0 && !filter.Categories.Contains(file.Category))
                    || (filter.Kinds.Count > 0 && !filter.Kinds.Contains(file.Kind))
                    || (filter.MinSizeBytes is { } minimum && file.SizeBytes < minimum)
                    || (filter.MaxSizeBytes is { } maximum && file.SizeBytes > maximum)
                    || (filter.ModifiedAfterUtc is { } after && file.ModifiedAtUtc < after)
                    || (filter.ModifiedBeforeUtc is { } before && file.ModifiedAtUtc > before))
                {
                    continue;
                }

                // Decided from the remembered name, so a file DeskAI would refuse to read is
                // never even offered to the extractor.
                if (!TextFileFormats.IsSupported(file.RelativePath))
                {
                    continue;
                }

                if ((file.Extension is ".docx" or ".xlsx")
                    && !RootCapabilities.CanReadDocuments(root))
                {
                    continue;
                }

                if (file.Extension == ".pdf" && !RootCapabilities.CanReadPdf(root))
                {
                    continue;
                }

                if (file.Extension == ".pptx" && !RootCapabilities.CanReadSlides(root))
                {
                    continue;
                }

                if (filesRead >= MaxFilesRead)
                {
                    reachedLimit = true;
                    break;
                }

                if (started.Elapsed >= MaxSearchTime)
                {
                    reachedLimit = true;
                    break;
                }

                filesRead++;
                var extraction = await _extractor
                    .ExtractAsync(root, file.RelativePath, TextExtractionOptions.Default, cancellationToken)
                    .ConfigureAwait(false);

                // A refusal is not an error here. A file may have gone, or turned out not to
                // be text after all; either way there is nothing to match and nothing to say.
                if (!extraction.Succeeded)
                {
                    filesSkipped++;
                    checks.Add(new ContentFileCheck(root.DisplayName, file.RelativePath, file.Name,
                        ContentCheckStatus.CouldNotRead, false, extraction.Explanation));
                    continue;
                }

                if (extraction.WasTruncated)
                {
                    filesTruncated++;
                }

                var position = extraction.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                checks.Add(new ContentFileCheck(root.DisplayName, file.RelativePath, file.Name,
                    position >= 0 ? ContentCheckStatus.Matched : ContentCheckStatus.NoMatch,
                    extraction.WasTruncated, extraction.Explanation));
                if (position >= 0)
                {
                    hits.Add(new ContentHit(
                        root.DisplayName,
                        file.RelativePath,
                        file.Name,
                        Snippet(extraction.Text, position, needle.Length))
                    {
                        Section = extraction.Sections.FirstOrDefault(section =>
                            position >= section.Start && position < section.End)?.Label,
                    });
                }
            }

            if (reachedLimit)
            {
                break;
            }
        }

        return new ContentSearchOutcome(hits.AsReadOnly(), allowed.Length, filesRead, reachedLimit,
            filesTruncated, filesSkipped)
        {
            CheckedFiles = checks.AsReadOnly(),
        };
    }

    /// <summary>
    /// Takes a readable piece of the file around the match.
    /// </summary>
    /// <remarks>
    /// Whitespace is collapsed so a match inside an indented or wrapped file still reads as
    /// one line, and control characters are dropped so a file cannot push odd characters
    /// into the results list.
    /// </remarks>
    private static string Snippet(string text, int position, int length)
    {
        var start = Math.Max(0, position - SnippetPadding);
        var end = Math.Min(text.Length, position + length + SnippetPadding);
        var slice = text[start..end];

        var cleaned = new string(slice
            .Select(character => char.IsControl(character) || char.IsWhiteSpace(character) ? ' ' : character)
            .ToArray());

        var collapsed = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = end < text.Length ? "…" : string.Empty;
        return prefix + collapsed + suffix;
    }
}
