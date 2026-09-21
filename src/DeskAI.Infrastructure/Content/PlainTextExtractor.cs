using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Content;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Content;

/// <summary>
/// Reads bounded words from approved text and modern Office files in a content-authorized folder.
/// </summary>
/// <remarks>
/// <para>
/// This is the only code in DeskAI that opens a file. Every check that lets it do so is
/// below, in order, and the first refusal wins. Permission is checked before anything else,
/// so a folder that was not connected for content never reaches a path calculation, let
/// alone a file handle.
/// </para>
/// <para>
/// DOCX, XLSX, and PPTX use bounded, local ZIP/XML readers. PDF text uses a bounded local
/// helper after its own grant. Older Office formats and images are refused before opening.
/// </para>
/// <para>
/// Nothing is written, created, or kept. The file is opened read-only, a bounded prefix is
/// decoded, and the text is returned to the caller and stored nowhere.
/// </para>
/// </remarks>
public sealed class PlainTextExtractor(
    IPathPolicy pathPolicy,
    PdfProcessReader pdfReader,
    IPdfOcrReader pdfOcrReader) : IContentTextExtractor
{
    private readonly IPathPolicy _pathPolicy = pathPolicy;
    private readonly PdfProcessReader _pdfReader = pdfReader;
    private readonly IPdfOcrReader _pdfOcrReader = pdfOcrReader;

    public PlainTextExtractor(IPathPolicy pathPolicy) : this(pathPolicy, new PdfProcessReader(), new NoPdfOcrReader()) { }

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

        if ((relativePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            && !RootCapabilities.CanReadDocuments(root))
        {
            return TextExtraction.Refused(relativePath, TextExtractionStatus.NotAuthorized,
                "This folder has not been allowed to read Word and Excel documents.");
        }

        if (relativePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            && !RootCapabilities.CanReadPdf(root))
        {
            return TextExtraction.Refused(relativePath, TextExtractionStatus.NotAuthorized,
                "This folder has not been allowed to read PDF text.");
        }

        if (relativePath.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
            && !RootCapabilities.CanReadSlides(root))
        {
            return TextExtraction.Refused(relativePath, TextExtractionStatus.NotAuthorized,
                "This folder has not been allowed to read PowerPoint slide text.");
        }

        if (_pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked
            || _pathPolicy.ValidateRelativePath(root, relativePath).Status == ValidationStatus.Blocked)
        {
            return TextExtraction.Refused(
                relativePath,
                TextExtractionStatus.Blocked,
                "This location is protected, so DeskAI did not open it.");
        }

        // Refused before opening: an unsupported file is never touched at all. The list
        // lives in Core so the code choosing candidates and the code opening them cannot
        // drift into disagreeing about which files get read.
        if (!TextFileFormats.IsSupported(relativePath))
        {
            return TextExtraction.Refused(
                relativePath,
                TextExtractionStatus.UnsupportedFormat,
                "DeskAI does not read inside this kind of file, so it was left closed.");
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

    private async Task<TextExtraction> ReadAsync(
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

            var extension = Path.GetExtension(relativePath);
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                if (stream.Length > PdfProcessReader.MaxPdfBytes)
                {
                    return TextExtraction.Refused(relativePath, TextExtractionStatus.Unavailable,
                        "This PDF is too large for a quick search, so DeskAI skipped it.");
                }

                var result = await _pdfReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!options.UsePdfOcr && (result is not { } extracted || string.IsNullOrWhiteSpace(extracted.Text)))
                {
                    return TextExtraction.Refused(relativePath, TextExtractionStatus.Unavailable,
                        "This PDF could not be read here. It may be encrypted, damaged, or unsupported.");
                }

                var text = result?.Text ?? string.Empty;
                var sections = new List<ExtractedTextSection>();
                var ocrTruncated = false;
                if (options.UsePdfOcr)
                {
                    if (stream.Length > WindowsPdfOcrLimits.MaxPdfBytes)
                    {
                        return TextExtraction.Refused(relativePath, TextExtractionStatus.Unavailable,
                            "This scanned PDF is over the 8 MB local OCR limit.");
                    }

                    stream.Position = 0;
                    var source = new byte[stream.Length];
                    await stream.ReadExactlyAsync(source, cancellationToken).ConfigureAwait(false);
                    var ocr = await _pdfOcrReader.ReadAsync(source, cancellationToken).ConfigureAwait(false);
                    if (ocr is { } recognized && !string.IsNullOrWhiteSpace(recognized.Text))
                    {
                        var start = text.Length;
                        if (start > 0) text += "\n";
                        start = text.Length;
                        text += recognized.Text;
                        sections.AddRange(recognized.Sections.Select(section =>
                            section with { Start = section.Start + start, End = section.End + start }));
                        ocrTruncated = recognized.WasTruncated;
                    }
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return TextExtraction.Refused(relativePath, TextExtractionStatus.Unavailable,
                        options.UsePdfOcr
                            ? "No words were recognized on the scanned pages. OCR is approximate and may miss text."
                            : "This PDF could not be read here. It may be encrypted, damaged, or unsupported.");
                }

                var pdfBytes = Encoding.UTF8.GetBytes(text);
                var limit = Math.Min(options.MaxBytes, 256 * 1024);
                var length = Math.Min(pdfBytes.Length, limit);
                while (length < pdfBytes.Length && length > 0 && (pdfBytes[length] & 0xC0) == 0x80)
                {
                    length--;
                }

                var pdfTruncated = (result?.Truncated ?? false) || ocrTruncated || length < pdfBytes.Length;
                return new TextExtraction(relativePath, TextExtractionStatus.Extracted,
                    Encoding.UTF8.GetString(pdfBytes, 0, length), pdfTruncated,
                    options.UsePdfOcr
                        ? "Scanned PDF words were read with approximate on-device OCR. Verify the result in the file."
                        : pdfTruncated ? "Part of this PDF was read. There may be more." : "PDF text was read locally.")
                {
                    Sections = sections,
                };
            }
            if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                if (stream.Length > OfficeOpenXmlReader.MaxContainerBytes)
                {
                    return TextExtraction.Refused(relativePath, TextExtractionStatus.Unavailable,
                        "This document is too large for a quick search, so DeskAI skipped it.");
                }

                var (words, wasTruncated) = await OfficeOpenXmlReader
                    .ReadAsync(stream, extension, options.MaxBytes, cancellationToken)
                    .ConfigureAwait(false);
                return new TextExtraction(relativePath, TextExtractionStatus.Extracted, words,
                    wasTruncated, wasTruncated
                        ? "Part of this document was read. There may be more."
                        : "This document was read locally.");
            }

            if (extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            {
                if (stream.Length > SlideOpenXmlReader.MaxContainerBytes)
                {
                    return TextExtraction.Refused(relativePath, TextExtractionStatus.Unavailable,
                        "This presentation is too large for a quick search, so DeskAI skipped it.");
                }

                var (words, wasTruncated, sections) = await SlideOpenXmlReader
                    .ReadAsync(stream, options.MaxBytes, cancellationToken).ConfigureAwait(false);
                return new TextExtraction(relativePath, TextExtractionStatus.Extracted, words,
                    wasTruncated, wasTruncated
                        ? "Part of this presentation was read. There may be more slides."
                        : "Slide text was read locally.")
                {
                    Sections = sections,
                };
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
            or System.Security.SecurityException
            or System.Xml.XmlException
            or NotSupportedException)
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
