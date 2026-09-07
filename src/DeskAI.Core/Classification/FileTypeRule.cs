using DeskAI.Core.Files;

namespace DeskAI.Core.Classification;

public sealed record FileTypeRule
{
    public FileTypeRule(string extension, FileKind kind, FileCategory category, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!extension.StartsWith('.') ||
            extension.Length < 2 ||
            extension.Any(char.IsWhiteSpace) ||
            extension.Contains(Path.DirectorySeparatorChar) ||
            extension.Contains(Path.AltDirectorySeparatorChar) ||
            extension.Contains(':'))
        {
            throw new ArgumentException("A file type rule needs a simple dot-prefixed extension.", nameof(extension));
        }

        Extension = extension.ToLowerInvariant();
        Kind = kind;
        Category = category;
        Reason = reason;
    }

    public string Extension { get; }
    public FileKind Kind { get; }
    public FileCategory Category { get; }
    public string Reason { get; }
}
