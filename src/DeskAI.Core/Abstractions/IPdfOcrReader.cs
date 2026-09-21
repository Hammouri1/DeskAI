using DeskAI.Core.Content;

namespace DeskAI.Core.Abstractions;

/// <summary>Reads approximate words from PDF page pixels without retaining them.</summary>
public interface IPdfOcrReader
{
    Task<PdfOcrResult?> ReadAsync(
        ReadOnlyMemory<byte> pdfBytes,
        CancellationToken cancellationToken = default);
}

public sealed record PdfOcrResult(
    string Text,
    IReadOnlyList<ExtractedTextSection> Sections,
    bool WasTruncated);

/// <summary>Portable default; the Windows app replaces this with its on-device reader.</summary>
public sealed class NoPdfOcrReader : IPdfOcrReader
{
    public Task<PdfOcrResult?> ReadAsync(ReadOnlyMemory<byte> pdfBytes,
        CancellationToken cancellationToken = default) => Task.FromResult<PdfOcrResult?>(null);
}
