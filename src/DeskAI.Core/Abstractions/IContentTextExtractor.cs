using DeskAI.Core.Content;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Reads a bounded amount of text out of one file in an authorized folder.
/// </summary>
/// <remarks>
/// <para>
/// This is the first capability in DeskAI that opens a file at all. It is therefore the
/// narrowest interface that can do the job: one file per call, named relative to a root the
/// caller had to supply, bounded by options, and returning inert text.
/// </para>
/// <para>
/// The root is a separate argument rather than part of a request object for the same reason
/// searching takes one: the permission travels with the call and cannot be chosen by
/// whatever assembled the path. Implementations must refuse any folder that was not
/// connected for content, and must never create a file that is not there.
/// </para>
/// </remarks>
public interface IContentTextExtractor
{
    Task<TextExtraction> ExtractAsync(
        AuthorizedRoot root,
        string relativePath,
        TextExtractionOptions options,
        CancellationToken cancellationToken = default);
}
