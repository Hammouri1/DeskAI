using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Content;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DeskAI.App.Services;

/// <summary>Bounded Windows OCR for a single, explicitly approved scanned-PDF search.</summary>
public sealed class WindowsPdfOcrReader : IPdfOcrReader
{
    public async Task<PdfOcrResult?> ReadAsync(
        ReadOnlyMemory<byte> pdfBytes,
        CancellationToken cancellationToken = default)
    {
        if (pdfBytes.IsEmpty || pdfBytes.Length > WindowsPdfOcrLimits.MaxPdfBytes)
        {
            return null;
        }

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            return null;
        }

        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input))
        {
            writer.WriteBytes(pdfBytes.ToArray());
            await writer.StoreAsync();
            writer.DetachStream();
        }

        input.Seek(0);
        PdfDocument document;
        try
        {
            document = await PdfDocument.LoadFromStreamAsync(input);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        var text = new StringBuilder();
        var sections = new List<ExtractedTextSection>();
        var pages = Math.Min((int)document.PageCount, WindowsPdfOcrLimits.MaxPages);
        var truncated = document.PageCount > pages;
        var textBytes = 0;
        for (var pageIndex = 0; pageIndex < pages; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OcrResult recognized;
            try
            {
                using var page = document.GetPage((uint)pageIndex);
                using var rendered = new InMemoryRandomAccessStream();
                var options = Scale(page.Size.Width, page.Size.Height);
                await page.RenderToStreamAsync(rendered, options);
                rendered.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(rendered);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                recognized = await engine.RecognizeAsync(bitmap);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(recognized.Text))
            {
                continue;
            }

            var pageBytes = Encoding.UTF8.GetByteCount(recognized.Text);
            if (textBytes + pageBytes > WindowsPdfOcrLimits.MaxTextBytes)
            {
                truncated = true;
                break;
            }

            if (text.Length > 0) text.AppendLine();
            var start = text.Length;
            text.Append(recognized.Text);
            textBytes += pageBytes;
            sections.Add(new ExtractedTextSection($"Page {pageIndex + 1}", start, text.Length));
        }

        return new PdfOcrResult(text.ToString(), sections.AsReadOnly(), truncated);
    }

    private static PdfPageRenderOptions Scale(double width, double height)
    {
        var maximum = OcrEngine.MaxImageDimension;
        // PDF dimensions are display units, not pixels. Render larger where possible so
        // small slide text remains legible to OCR, while respecting Windows' hard limit.
        var ratio = Math.Min(2.5d, maximum / Math.Max(width, height));
        return new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(width * ratio)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(height * ratio)),
        };
    }
}
