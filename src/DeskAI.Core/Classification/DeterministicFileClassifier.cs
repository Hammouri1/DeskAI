using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;

namespace DeskAI.Core.Classification;

public sealed class DeterministicFileClassifier(FileTypeRuleSet ruleSet) : IFileClassifier
{
    private static readonly string[] ScreenshotMarkers =
    [
        "screenshot",
        "screen shot",
        "snipping tool",
    ];

    public Classification Classify(FileItem file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var rule = ruleSet.Match(file.RelativePath);
        if (rule is null)
        {
            return new Classification(
                FileCategory.Unknown,
                FileKind.Unknown,
                ClassificationSource.Heuristic,
                0,
                "No deterministic file type rule matched");
        }

        if (rule.Kind == FileKind.Image && IsScreenshotName(file.RelativePath))
        {
            return new Classification(
                FileCategory.Screenshots,
                FileKind.Image,
                ClassificationSource.Heuristic,
                0.95,
                "Image filename matches a common Windows screenshot pattern");
        }

        return new Classification(
            rule.Category,
            rule.Kind,
            ClassificationSource.Rule,
            1,
            rule.Reason);
    }

    private static bool IsScreenshotName(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return ScreenshotMarkers.Any(marker => fileName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
