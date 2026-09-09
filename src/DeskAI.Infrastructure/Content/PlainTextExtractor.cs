using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Content;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Content;

/// <summary>
/// Reads a bounded prefix of text out of plain-text files in a content-authorized folder.
/// </summary>
/// <remarks>
/// <para>
/// This is the only code in DeskAI that opens a file. Every check that lets it do so is
/// below, in order, and the first refusal wins. Permission is checked before anything else,
/// so a folder that was not connected for content never reaches a path calculation, let
/// alone a file handle.
/// </para>
/// <para>
/// Only plain-text formats are read. Formats such as PDF and Office documents need a
/// third-party parser to interpret an attacker-controlled binary structure, which is a much
/// larger security question than reading bytes; they are deliberately refused here rather
/// than half-supported. An extension DeskAI does not read is refused <em>before</em> the
/// file is opened, so an unsupported file is never touched at all.
/// </para>
/// <para>
/// Nothing is written, created, or kept. The file is opened read-only, a bounded prefix is
/// decoded, and the text is returned to the caller and stored nowhere.
/// </para>
/// </remarks>
public sealed class PlainTextExtractor(IPathPolicy pathPolicy) : IContentTextExtractor
{
    /// <summary>
    /// The file endings this reads.
    /// </summary>
    /// <remarks>
    /// All of these are text by definition, so reading one cannot execute a parser over
    /// hostile structure. The list is deliberately short; widening it is a security
    /// decision, not a convenience one.
    /// </remarks>
    private static readonly string[] SupportedExtensions =
    [
        ".txt", ".md", ".log", ".csv", ".tsv", ".json", ".jsonl", ".ndjson",
        ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
    ];

    private readonly IPathPolicy _pathPolicy = pathPolicy;

    public async Task<TextExtraction> ExtractAsync(
        AuthorizedRoot root,
        string relativePath,
        TextExtractionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // Permission first, before any path work. A folder connected only for names, sizes
        // and dates has not agreed to have its files opened, and no later check should be
        // the thing standing between that folder and a file handle.
        if (!RootCapabilities.CanReadContent(root))
        {
            return TextExtraction.Refused(
                relativePath,
                TextExtractionStatus.NotAuthorized,
                "This folder was not connected for reading inside files.");
        }

        if (_pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked
            || _pathPolicy.ValidateRelativePath(root, relativePath).Status == ValidationStatus.Blocked)
        {
            return TextExtraction.Refused(
                relativePath,
                TextExtractionStatus.Blocked,
                "This location is protected, so DeskAI did not open it.");
        }

        // Refused before opening: an unsupported file is never touched at all.
        if (!SupportedExtensions.Any(extension =>
            relativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return TextExtraction.Refused(
                relativePath,
                TextExtractionStatus.UnsupportedFormat,
                "DeskAI only reads plain text files, so this one was left closed.");
        }

        var resolved = ResolveInsideRoot(root.CanonicalPath, relativePath);
        if (resolved is null)
        {
            return TextExtraction.Refused(
                relativePath,
                TextExtractionStatus.Blocked,
                "That path leads outside the connected folder.");
        }

        return await ReadAsync(resolved, relativePath, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the file inside the root, or returns null if it would land outside it.
    /// </summary>
    /// <remarks>
    /// Both sides are canonicalized before comparison, so "..", a doubled separator, or a
    /// differently written form of the same path cannot present as contained when it is not.
    /// The separator is required after the root prefix so that a sibling folder whose name
    /// merely starts with the root's name is not mistaken for a child of it.
    /// </remarks>
    private static string? ResolveInsideRoot(string rootPath, string relativePath)
    {
        try
        {
            var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var full = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
            var prefix = canonicalRoot + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static async Task<TextExtraction> ReadAsync(
        string fullPath,
        string relativePath,
        TextExtractionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            if (IsLink(fullPath))
            {
                return TextExtraction.Refused(
                    relativePath,
                    TextExtractionStatus.Blocked,
                    "That name is a shortcut to somewhere else, so DeskAI did not follow it.");
            }

            // FileMode.Open, never OpenOrCreate: asking for a file that is not there must
            // report that it is not there, never bring one into existence.
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            // Checked again now the handle is open. A file can be replaced by a link between
            // the check above and this open; the handle still refers to what was opened, so
            // finding a link here means abandoning the read rather than trusting it.
            if (IsLink(fullPath))
            {
                return TextExtraction.Refused(
                    relativePath,
                    TextExtractionStatus.Blocked,
                    "That file changed while DeskAI was opening it, so it was left alone.");
            }

            var buffer = new byte[options.MaxBytes];
            var read = await stream.ReadAtLeastAsync(
                buffer,
                buffer.Length,
                throwOnEndOfStream: false,
                cancellationToken).ConfigureAwait(false);

            // Truncation is decided by whether anything remains, not by whether the buffer
            // filled: a file of exactly the limit is complete, not cut short.
            var truncated = read == buffer.Length && stream.Position < stream.Length;
            var bytes = buffer.AsMemory(0, read);

            if (LooksBinary(bytes.Span))
            {
                return TextExtraction.Refused(
                    relativePath,
                    TextExtractionStatus.NotText,
                    "This file is not text, so there are no words to read.");
            }

            return new TextExtraction(
                relativePath,
                TextExtractionStatus.Extracted,
                Decode(bytes.Span),
                truncated,
                truncated
                    ? "The beginning of this file was read. The rest was left unread."
                    : "This file was read.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return TextExtraction.Refused(
                relativePath,
                TextExtractionStatus.Unavailable,
                "This file could not be read. It may have been moved, or another program may be using it.");
        }
    }

    private static bool IsLink(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists
            && ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null);
    }

    /// <summary>
    /// A zero byte does not occur in text, and is the cheapest reliable sign of a file that
    /// only looks like text by its name. Decoding one anyway would return convincing
    /// nonsense, which is worse than saying there are no words here.
    /// </summary>
    private static bool LooksBinary(ReadOnlySpan<byte> bytes) => bytes.IndexOf((byte)0) >= 0;

    /// <summary>
    /// Decodes as UTF-8, replacing anything invalid rather than throwing.
    /// </summary>
    /// <remarks>
    /// The bytes are whatever happened to be in the file, so malformed sequences are
    /// expected input and not an error. A byte-order mark is dropped so it does not appear
    /// as a stray character at the start of the text.
    /// </remarks>
    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> utf8Mark = [0xEF, 0xBB, 0xBF];
        if (bytes.StartsWith(utf8Mark))
        {
            bytes = bytes[utf8Mark.Length..];
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(bytes);
    }
}
