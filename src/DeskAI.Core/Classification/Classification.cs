using DeskAI.Core.Files;

namespace DeskAI.Core.Classification;

public sealed record Classification
{
    public Classification(
        FileCategory category,
        FileKind kind,
        ClassificationSource source,
        double confidence,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between zero and one.");
        }

        Category = category;
        Kind = kind;
        Source = source;
        Confidence = confidence;
        Reason = reason;
    }

    public FileCategory Category { get; }

    public FileKind Kind { get; }

    public ClassificationSource Source { get; }

    public double Confidence { get; }

    public string Reason { get; }
}

public enum FileCategory
{
    Unknown,
    Documents,
    Presentations,
    Spreadsheets,
    Images,
    Screenshots,
    Videos,
    Audio,
    Archives,
    Installers,
    SourceCode,
    Data,
}

public enum ClassificationSource
{
    Rule,
    Heuristic,
    LocalAi,
    CloudAi,
    User,
}
