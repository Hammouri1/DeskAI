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
public sealed record ContentHit(string RootName, string RelativePath, string Name, string Snippet);

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
    bool ReachedLimit)
{
    public static ContentSearchOutcome NotAllowed { get; } = new([], 0, 0, false);

    public bool WasSearched => FoldersIncluded > 0;
}

/// <summary>
/// Looks for words written inside the text files of folders that allowed it.
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

    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IFileIndex _index = index;
    private readonly IContentTextExtractor _extractor = extractor;

    public async Task<ContentSearchOutcome> SearchAsync(
        string? phrase,
        CancellationToken cancellationToken = default)
    {
        var needle = phrase?.Trim() ?? string.Empty;
        if (needle.Length < MinimumPhraseLength)
        {
            return ContentSearchOutcome.NotAllowed;
        }

        var allowed = (await _roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(RootCapabilities.CanReadContent)
            .ToArray();

        if (allowed.Length == 0)
        {
            return ContentSearchOutcome.NotAllowed;
        }

        var hits = new List<ContentHit>();
        var filesRead = 0;
        var reachedLimit = false;

        foreach (var root in allowed)
        {
            foreach (var file in await _index
                .SearchRootAsync(root.Id, new SearchQuery(limit: SearchQuery.MaxLimit), cancellationToken)
                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Decided from the remembered name, so a file DeskAI would refuse to read is
                // never even offered to the extractor.
                if (!TextFileFormats.IsSupported(file.RelativePath))
                {
                    continue;
                }

                if (filesRead >= MaxFilesRead)
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
                    continue;
                }

                var position = extraction.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (position >= 0)
                {
                    hits.Add(new ContentHit(
                        root.DisplayName,
                        file.RelativePath,
                        file.Name,
                        Snippet(extraction.Text, position, needle.Length)));
                }
            }

            if (reachedLimit)
            {
                break;
            }
        }

        return new ContentSearchOutcome(hits.AsReadOnly(), allowed.Length, filesRead, reachedLimit);
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
