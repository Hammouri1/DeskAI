using DeskAI.Core.Content;
using DeskAI.Core.Execution;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Reads one file in a connected folder and returns a fingerprint of its bytes, to tell whether
/// two files are the same. Reads; never writes, keeps, or sends.
/// </summary>
/// <remarks>
/// The folder is a separate argument, for the same reason the text extractor takes one: the
/// permission travels with the call and cannot be chosen by whatever assembled the path. Only
/// <c>DuplicateCheckService</c> is given this, and only after the person agreed to have exactly
/// these files compared; tests assert no other service can reach it.
/// </remarks>
public interface IFileFingerprinter
{
    /// <param name="expected">How DeskAI remembered the file; a file that differs is not read.</param>
    /// <param name="maxBytes">Read only this much from the beginning, or the whole file when null.</param>
    Task<FileFingerprint> FingerprintAsync(
        AuthorizedRoot root,
        string relativePath,
        ExpectedFile expected,
        long? maxBytes,
        CancellationToken cancellationToken = default);
}
