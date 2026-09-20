using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using DeskAI.Core.Content;

namespace DeskAI.Infrastructure.Content;

/// <summary>Reads bounded visible text from modern PowerPoint slide XML only.</summary>
/// <remarks>
/// This never follows relationships, opens embedded objects, runs macros, or expands an
/// archive entry to disk. Text in pictures remains closed under the separate visual grant.
/// </remarks>
internal static class SlideOpenXmlReader
{
    internal const long MaxContainerBytes = 8L * 1024 * 1024;
    private const long MaxSlideBytes = 256L * 1024;
    private const int MaxEntries = 1_000;
    private const int MaxSlides = 40;
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";

    internal static async Task<(string Text, bool Truncated, IReadOnlyList<ExtractedTextSection> Sections)> ReadAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException("Too many parts in this presentation.");
        }

        var slides = archive.Entries
            .Select(entry => (Entry: entry, Number: SlideNumber(entry.FullName)))
            .Where(slide => slide.Number is not null)
            .OrderBy(slide => slide.Number)
            .Take(MaxSlides + 1)
            .ToArray();
        if (slides.Length == 0)
        {
            throw new InvalidDataException("The presentation has no readable slide text parts.");
        }

        var text = new StringBuilder(Math.Min(maxBytes, 4096));
        var sections = new List<ExtractedTextSection>();
        var truncated = slides.Length > MaxSlides;
        var bytesUsed = 0;
        foreach (var slide in slides.Take(MaxSlides))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slide.Entry.Length > MaxSlideBytes)
            {
                truncated = true;
                continue;
            }

            if (text.Length > 0 && bytesUsed < maxBytes)
            {
                text.Append(' ');
                bytesUsed++;
            }

            var start = text.Length;
            await using var input = slide.Entry.Open();
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxSlideBytes,
                CloseInput = false,
            });
            await reader.ReadAsync().ConfigureAwait(false);
            while (!reader.EOF)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Depth > 64)
                {
                    throw new InvalidDataException("Slide XML is too deeply nested.");
                }

                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "t"
                    || reader.NamespaceURI != DrawingNamespace)
                {
                    await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                var value = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                if (value.Length == 0)
                {
                    continue;
                }

                foreach (var rune in value.EnumerateRunes())
                {
                    if (bytesUsed + rune.Utf8SequenceLength > maxBytes)
                    {
                        truncated = true;
                        break;
                    }

                    text.Append(rune.ToString());
                    bytesUsed += rune.Utf8SequenceLength;
                }

                if (truncated && bytesUsed >= maxBytes)
                {
                    break;
                }

                if (bytesUsed < maxBytes)
                {
                    text.Append(' ');
                    bytesUsed++;
                }
            }

            if (text.Length > start)
            {
                sections.Add(new ExtractedTextSection($"Slide {slide.Number}", start, text.Length));
            }

            if (truncated && bytesUsed >= maxBytes)
            {
                break;
            }
        }

        return (text.ToString(), truncated, sections.AsReadOnly());
    }

    private static int? SlideNumber(string path)
    {
        const string prefix = "ppt/slides/slide";
        const string suffix = ".xml";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)
            || !path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var number = path.AsSpan(prefix.Length, path.Length - prefix.Length - suffix.Length);
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0 ? parsed : null;
    }
}
