using System.IO.Compression;
using System.Text;
using System.Xml;

namespace DeskAI.Infrastructure.Content;

/// <summary>Extracts text nodes from bounded, modern Word and Excel XML parts.</summary>
/// <remarks>
/// No archive entry is written to disk. DTDs and external entities are disabled, and both
/// compressed input and expanded XML have hard limits. This is not a general Office parser:
/// macros, embedded objects, links, and legacy binary formats are never opened.
/// </remarks>
internal static class OfficeOpenXmlReader
{
    internal const long MaxContainerBytes = 8L * 1024 * 1024;
    private const long MaxPartBytes = 256L * 1024;
    private const int MaxEntries = 1_000;
    private const int MaxParts = 40;

    internal static async Task<(string Text, bool Truncated)> ReadAsync(
        Stream stream,
        string extension,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException("Too many parts in this document.");
        }

        var isWord = extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);
        var parts = archive.Entries
            .Where(entry => isWord
                ? entry.FullName.Equals("word/document.xml", StringComparison.Ordinal)
                : entry.FullName.Equals("xl/sharedStrings.xml", StringComparison.Ordinal)
                    || (entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)
                        && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            .OrderBy(entry => entry.FullName.Equals("xl/sharedStrings.xml", StringComparison.Ordinal) ? 0 : 1)
            .Take(MaxParts + 1)
            .ToArray();

        if (parts.Length == 0)
        {
            throw new InvalidDataException("The document has no readable text part.");
        }

        var text = new StringBuilder(Math.Min(maxCharacters, 4096));
        var truncated = parts.Length > MaxParts;
        foreach (var part in parts.Take(MaxParts))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (part.Length > MaxPartBytes)
            {
                truncated = true;
                continue;
            }

            await using var input = part.Open();
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxPartBytes,
                CloseInput = false,
            });

            await reader.ReadAsync().ConfigureAwait(false);
            while (!reader.EOF)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Depth > 64)
                {
                    throw new InvalidDataException("Document XML is too deeply nested.");
                }

                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "t")
                {
                    if (isWord && reader.NodeType == XmlNodeType.EndElement
                        && reader.LocalName == "p" && text.Length > 0 && text.Length < maxCharacters)
                    {
                        text.Append(' ');
                    }
                    await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                var value = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                if (value.Length == 0)
                {
                    continue;
                }

                var remaining = maxCharacters - text.Length;
                if (remaining <= 1)
                {
                    truncated = true;
                    break;
                }

                if (value.Length >= remaining)
                {
                    text.Append(value.AsSpan(0, remaining - 1));
                    truncated = true;
                    break;
                }

                text.Append(value);
                if (!isWord)
                {
                    text.Append(' ');
                }
            }

            if (text.Length >= maxCharacters - 1)
            {
                break;
            }
        }

        return (text.ToString().TrimEnd(), truncated);
    }
}
