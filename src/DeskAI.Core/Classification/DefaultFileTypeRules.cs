using DeskAI.Core.Files;

namespace DeskAI.Core.Classification;

public static class DefaultFileTypeRules
{
    public static FileTypeRuleSet Create() => new(CreateRules());

    private static IEnumerable<FileTypeRule> CreateRules()
    {
        foreach (var extension in new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".md", ".epub", ".pages", ".tex" })
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

        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".heic", ".tif", ".tiff", ".ico", ".avif", ".psd" })
        {
            yield return Rule(extension, FileKind.Image, FileCategory.Images, "Known image extension");
        }

        foreach (var extension in new[] { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v", ".flv", ".mpg", ".mpeg", ".3gp", ".m2ts" })
        {
            yield return Rule(extension, FileKind.Video, FileCategory.Videos, "Known video extension");
        }

        foreach (var extension in new[] { ".mp3", ".wav", ".flac", ".m4a", ".m4b", ".aac", ".ogg", ".wma", ".opus", ".aiff", ".mid", ".midi" })
        {
            yield return Rule(extension, FileKind.Audio, FileCategory.Audio, "Known audio extension");
        }

        foreach (var extension in new[] { ".zip", ".7z", ".rar", ".tar", ".tar.gz", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".cab", ".iso" })
        {
            yield return Rule(extension, FileKind.Archive, FileCategory.Archives, "Known archive extension");
        }

        foreach (var extension in new[] { ".exe", ".msi", ".msix", ".appx", ".appxbundle", ".msixbundle", ".msp", ".msu", ".appinstaller" })
        {
            yield return Rule(extension, FileKind.Installer, FileCategory.Installers, "Known installer/package extension");
        }

        foreach (var extension in new[]
        {
            ".cs", ".csproj", ".sln", ".fs", ".vb", ".py", ".js", ".ts", ".tsx", ".jsx",
            ".java", ".c", ".cpp", ".h", ".go", ".rs", ".html", ".css", ".sql", ".sh", ".ps1",
            ".rb", ".php", ".swift", ".kt", ".lua", ".bat", ".cmd", ".scss", ".less", ".vue",
            ".svelte", ".gradle", ".cmake",
        })
        {
            yield return Rule(extension, FileKind.SourceCode, FileCategory.SourceCode, "Known source/project extension");
        }

        foreach (var extension in new[]
        {
            ".json", ".jsonl", ".ndjson", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
            ".log", ".db", ".sqlite", ".sqlite3", ".mdb", ".accdb", ".parquet",
        })
        {
            yield return Rule(extension, FileKind.Data, FileCategory.Data, "Known structured-data extension");
        }
    }

    private static FileTypeRule Rule(string extension, FileKind kind, FileCategory category, string reason) =>
        new(extension, kind, category, reason);
}
