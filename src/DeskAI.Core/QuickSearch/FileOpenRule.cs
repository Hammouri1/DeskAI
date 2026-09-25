namespace DeskAI.Core.QuickSearch;

/// <summary>What quick search may do with a file: open it in its usual app, or only show it in its folder.</summary>
public enum OpenChoice { Open, ShowInFolderOnly }

/// <summary>
/// Decides from a file's name whether quick search may open it in its usual app.
/// </summary>
/// <remarks>
/// An allow-list, because listing every risky kind of file is impossible and missing one would
/// start a program. Only the <b>last</b> extension counts, so "invoice.pdf.exe" is a program. A
/// name ending in a dot or a space has no familiar extension, so it is only shown in its folder
/// even though Windows would drop the dot. Asked twice: when a row is shown (from the remembered
/// name) and again on the live name just before opening (ADR 0047).
/// </remarks>
public static class FileOpenRule
{
    public static IReadOnlySet<string> OpenableExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".rtf", ".csv", ".pdf", ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic",
        ".mp3", ".wav", ".m4a", ".flac", ".mp4", ".mov", ".mkv", ".avi", ".zip",
    };

    public static OpenChoice For(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.EndsWith('.') || fileName.EndsWith(' '))
        {
            return OpenChoice.ShowInFolderOnly;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Length > 1 && extension.Length < fileName.Length && OpenableExtensions.Contains(extension)
            ? OpenChoice.Open
            : OpenChoice.ShowInFolderOnly;
    }
}
