namespace DeskAI.Core.Files;

public sealed record FileItem
{
    public FileItem(
        Guid id,
        string relativePath,
        FileKind kind,
        long sizeBytes,
        DateTimeOffset createdAtUtc,
        DateTimeOffset modifiedAtUtc,
        FileTraits traits = FileTraits.None)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A file item needs a stable ID.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("File items store root-relative paths only.", nameof(relativePath));
        }

        var segments = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("File item paths cannot contain traversal segments.", nameof(relativePath));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        Id = id;
        RelativePath = relativePath;
        Kind = kind;
        SizeBytes = sizeBytes;
        CreatedAtUtc = createdAtUtc;
        ModifiedAtUtc = modifiedAtUtc;
        Traits = traits;
    }

    public Guid Id { get; }
    public string RelativePath { get; }
    public FileKind Kind { get; }
    public long SizeBytes { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ModifiedAtUtc { get; }
    public FileTraits Traits { get; }
}

/// <summary>
/// Facts about a file that decide whether DeskAI should leave it alone, read from its
/// attributes without opening it.
/// </summary>
[Flags]
public enum FileTraits
{
    None = 0,
    Hidden = 1,
    System = 2,

    /// <summary>Stored online only (a cloud placeholder). Moving one can force a download.</summary>
    OnlineOnly = 4,
}

public enum FileKind
{
    Unknown,
    Document,
    Presentation,
    Spreadsheet,
    Image,
    Video,
    Audio,
    Archive,
    Installer,
    SourceCode,
    Data,
}
