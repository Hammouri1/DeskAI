using DeskAI.Core.Files;

namespace DeskAI.Core.Classification;

public static class DefaultFileTypeRules
{
    public static FileTypeRuleSet Create() => new(CreateRules());

    private static IEnumerable<FileTypeRule> CreateRules()
    {
        foreach (var extension in new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".md" })
        {
            yield return Rule(extension, FileKind.Document, FileCategory.Documents, "Known document extension");
        }

        foreach (var extension in new[] { ".ppt", ".pptx", ".odp" })
        {
            yield return Rule(extension, FileKind.Presentation, FileCategory.Presentations, "Known presentation extension");
        }

        foreach (var extension in new[] { ".xls", ".xlsx", ".csv", ".ods", ".tsv" })
        {
            yield return Rule(extension, FileKind.Spreadsheet, FileCategory.Spreadsheets, "Known spreadsheet extension");
        }

        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".heic" })
        {
            yield return Rule(extension, FileKind.Image, FileCategory.Images, "Known image extension");
        }

        foreach (var extension in new[] { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv" })
        {
            yield return Rule(extension, FileKind.Video, FileCategory.Videos, "Known video extension");
        }

        foreach (var extension in new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg" })
        {
            yield return Rule(extension, FileKind.Audio, FileCategory.Audio, "Known audio extension");
        }

        foreach (var extension in new[] { ".zip", ".7z", ".rar", ".tar", ".tar.gz", ".gz" })
        {
            yield return Rule(extension, FileKind.Archive, FileCategory.Archives, "Known archive extension");
        }

        foreach (var extension in new[] { ".exe", ".msi", ".msix", ".appx", ".appxbundle", ".msixbundle" })
        {
            yield return Rule(extension, FileKind.Installer, FileCategory.Installers, "Known installer/package extension");
        }

        foreach (var extension in new[]
        {
            ".cs", ".csproj", ".sln", ".fs", ".vb", ".py", ".js", ".ts", ".tsx", ".jsx",
            ".java", ".c", ".cpp", ".h", ".go", ".rs", ".html", ".css", ".sql", ".sh", ".ps1",
        })
        {
            yield return Rule(extension, FileKind.SourceCode, FileCategory.SourceCode, "Known source/project extension");
        }

        foreach (var extension in new[] { ".json", ".xml", ".yaml", ".yml", ".db", ".sqlite", ".parquet" })
        {
            yield return Rule(extension, FileKind.Data, FileCategory.Data, "Known structured-data extension");
        }
    }

    private static FileTypeRule Rule(string extension, FileKind kind, FileCategory category, string reason) =>
        new(extension, kind, category, reason);
}
