using DeskAI.Core.Classification;
using DeskAI.Core.Files;

namespace DeskAI.Core.Rules;

/// <summary>
/// Everything a rule is allowed to know about one file.
/// </summary>
/// <remarks>
/// <para>
/// Rules test this and nothing else. Handing them a <see cref="FileItem"/> directly would
/// let a future condition reach whatever that type grows next; a deliberate, narrow subject
/// means adding a new fact for rules to test is a decision someone has to make here.
/// </para>
/// <para>
/// There is no absolute path and no file content. A rule can ask what a file is called,
/// what kind it is, how big it is, and when it changed. It cannot ask where it lives on the
/// disk, and it cannot ask what is written inside it.
/// </para>
/// </remarks>
public sealed record RuleSubject
{
    public RuleSubject(
        string relativePath,
        FileCategory category,
        FileKind kind,
        long sizeBytes,
        DateTimeOffset modifiedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        RelativePath = relativePath;
        Name = Path.GetFileName(relativePath);
        Extension = Path.GetExtension(relativePath);
        Category = category;
        Kind = kind;
        SizeBytes = sizeBytes;
        ModifiedAtUtc = modifiedAtUtc;
    }

    public string RelativePath { get; }

    public string Name { get; }

    /// <summary>The ending including its dot, or empty when the file has none.</summary>
    public string Extension { get; }

    public FileCategory Category { get; }

    public FileKind Kind { get; }

    public long SizeBytes { get; }

    public DateTimeOffset ModifiedAtUtc { get; }

    /// <summary>Describes a scanned file in the only terms rules may test.</summary>
    public static RuleSubject From(FileItem file, Classification.Classification classification)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(classification);
        return new RuleSubject(
            file.RelativePath,
            classification.Category,
            file.Kind,
            file.SizeBytes,
            file.ModifiedAtUtc);
    }
}
