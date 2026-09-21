using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

// The worker receives bytes, never a path. It has no reason to open another file or use
// the network. A malformed parser input can terminate this process without taking Search down.
const int maxInputBytes = 8 * 1024 * 1024;
const int maxPages = 20;
const int maxOutputBytes = 64 * 1024;

try
{
    using var input = new MemoryStream();
    var stdin = Console.OpenStandardInput();
    var chunk = new byte[8192];
    int count;
    while ((count = await stdin.ReadAsync(chunk)) > 0)
    {
        if (input.Length + count > maxInputBytes)
        {
            return 2;
        }

        await input.WriteAsync(chunk.AsMemory(0, count));
    }
    if (input.Length is < 8 or > maxInputBytes)
    {
        return 2;
    }

    input.Position = 0;
    using var document = PdfDocument.Open(input, ParsingOptions.LenientParsingOff);
    if (document.IsEncrypted || document.NumberOfPages < 1)
    {
        return 2;
    }

    if (args.Length == 1 && args[0] == "images")
    {
        var images = new List<object>();
        var totalBytes = 0;
        for (var page = 1; page <= Math.Min(document.NumberOfPages, maxPages)
            && images.Count < 12; page++)
        {
            foreach (var image in document.GetPage(page).GetImages())
            {
                if (image.WidthInSamples < 1 || image.HeightInSamples < 1
                    || (long)image.WidthInSamples * image.HeightInSamples > 4_000_000)
                {
                    continue;
                }

                byte[]? bytes = null;
                string? mediaType = null;
                if (image.RawMemory.Length <= 1024 * 1024
                    && image.RawBytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
                {
                    bytes = image.RawMemory.ToArray();
                    mediaType = "image/jpeg";
                }
                else if (image.TryGetPng(out var png) && png is { Length: <= 1024 * 1024 })
                {
                    bytes = png;
                    mediaType = "image/png";
                }

                if (bytes is null || totalBytes + bytes.Length > 4 * 1024 * 1024)
                {
                    continue;
                }

                images.Add(new { page, mediaType, data = Convert.ToBase64String(bytes) });
                totalBytes += bytes.Length;
                if (images.Count >= 12)
                {
                    break;
                }
            }
        }

        await Console.OpenStandardOutput().WriteAsync(JsonSerializer.SerializeToUtf8Bytes(images));
        return 0;
    }

    var text = new StringBuilder();
    var truncated = document.NumberOfPages > maxPages;
    for (var page = 1; page <= Math.Min(document.NumberOfPages, maxPages); page++)
    {
        var words = ContentOrderTextExtractor.GetText(document.GetPage(page));
        if (Encoding.UTF8.GetByteCount(words) + Encoding.UTF8.GetByteCount(text.ToString()) + 1 > maxOutputBytes)
        {
            var remaining = maxOutputBytes - Encoding.UTF8.GetByteCount(text.ToString());
            foreach (var rune in words.EnumerateRunes())
            {
                var width = rune.Utf8SequenceLength;
                if (width > remaining)
                {
                    break;
                }

                text.Append(rune.ToString());
                remaining -= width;
            }

            truncated = true;
            break;
        }

        text.Append(words).Append(' ');
    }

    var output = Encoding.UTF8.GetBytes(text.ToString());
    var stream = Console.OpenStandardOutput();
    await stream.WriteAsync(new byte[] { truncated ? (byte)1 : (byte)0 });
    await stream.WriteAsync(output);
    return 0;
}
catch (Exception exception) when (exception is not OutOfMemoryException)
{
    // File contents and parser messages never go to stderr or an application log.
    return 2;
}
