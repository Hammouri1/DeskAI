namespace DeskAI.Core.Content;

/// <summary>
/// The file endings DeskAI is willing to open and read as text.
/// </summary>
/// <remarks>
/// <para>
/// One list, in Core, so the code that opens files and the code that decides which files are
/// worth opening cannot drift apart. Two copies of this would eventually disagree, and the
/// disagreement would be a file opened that the other half believed was never touched.
/// </para>
/// <para>
/// All of these are text by definition, so reading one runs no parser over hostile
/// structure. PDFs and Office documents are absent deliberately: interpreting them means
/// running a third-party parser over attacker-controlled binary, which is a separate
/// security question. Widening this list is a security decision, not a convenience one.
/// </para>
/// </remarks>
public static class TextFileFormats
{
    private static readonly string[] Extensions =
    [
        ".txt", ".md", ".log", ".csv", ".tsv", ".json", ".jsonl", ".ndjson",
        ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
    ];

    /// <summary>The endings, for wording a disclosure that must match the behaviour.</summary>
    public static IReadOnlyList<string> Supported { get; } = Extensions.AsReadOnly();

    public static bool IsSupported(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Extensions.Any(extension => relativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }
}
