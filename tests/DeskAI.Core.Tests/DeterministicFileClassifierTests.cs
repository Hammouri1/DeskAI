using DeskAI.Core.Classification;
using DeskAI.Core.Files;

namespace DeskAI.Core.Tests;

public sealed class DeterministicFileClassifierTests
{
    private readonly DeterministicFileClassifier _classifier = new(DefaultFileTypeRules.Create());

    [Theory]
    [InlineData("report.PDF", FileKind.Document, FileCategory.Documents)]
    [InlineData("lecture.pptx", FileKind.Presentation, FileCategory.Presentations)]
    [InlineData("budget.xlsx", FileKind.Spreadsheet, FileCategory.Spreadsheets)]
    [InlineData("photo.jpg", FileKind.Image, FileCategory.Images)]
    [InlineData("clip.mp4", FileKind.Video, FileCategory.Videos)]
    [InlineData("recording.mp3", FileKind.Audio, FileCategory.Audio)]
    [InlineData("backup.tar.gz", FileKind.Archive, FileCategory.Archives)]
    [InlineData("setup.exe", FileKind.Installer, FileCategory.Installers)]
    [InlineData("project.csproj", FileKind.SourceCode, FileCategory.SourceCode)]
    [InlineData("settings.json", FileKind.Data, FileCategory.Data)]
    public void Classify_MapsKnownExtensionsCaseInsensitively(
        string relativePath,
        FileKind expectedKind,
        FileCategory expectedCategory)
    {
        var result = _classifier.Classify(CreateFile(relativePath));

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedCategory, result.Category);
        Assert.Equal(ClassificationSource.Rule, result.Source);
        Assert.Equal(1, result.Confidence);
    }

    [Theory]
    [InlineData("Screenshot 2026-09-07.png")]
    [InlineData("Screen Shot 2026-09-07.jpg")]
    [InlineData("Snipping Tool capture.webp")]
    public void Classify_IdentifiesCommonScreenshotNames(string relativePath)
    {
        var result = _classifier.Classify(CreateFile(relativePath));

        Assert.Equal(FileCategory.Screenshots, result.Category);
        Assert.Equal(FileKind.Image, result.Kind);
        Assert.Equal(ClassificationSource.Heuristic, result.Source);
        Assert.Equal(0.95, result.Confidence);
    }

    [Fact]
    public void Classify_DoesNotTrustAnEarlierExtensionInInstallerName()
    {
        var result = _classifier.Classify(CreateFile("report.pdf.exe"));

        Assert.Equal(FileCategory.Installers, result.Category);
    }

    [Fact]
    public void Classify_ReturnsExplainableUnknownForUnmatchedFile()
    {
        var result = _classifier.Classify(CreateFile("mystery.custom"));

        Assert.Equal(FileCategory.Unknown, result.Category);
        Assert.Equal(FileKind.Unknown, result.Kind);
        Assert.Equal(0, result.Confidence);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void RuleSet_UsesMostSpecificCompoundExtension()
    {
        var rules = new FileTypeRuleSet(
        [
            new(".gz", FileKind.Data, FileCategory.Data, "Generic compressed data"),
            new(".tar.gz", FileKind.Archive, FileCategory.Archives, "Tar archive"),
        ]);

        var result = new DeterministicFileClassifier(rules).Classify(CreateFile("source.tar.gz"));

        Assert.Equal(FileCategory.Archives, result.Category);
        Assert.Equal("Tar archive", result.Reason);
    }

    [Fact]
    public void RuleSet_RejectsDuplicateExtensionsIgnoringCase()
    {
        var action = () => new FileTypeRuleSet(
        [
            new(".pdf", FileKind.Document, FileCategory.Documents, "Document"),
            new(".PDF", FileKind.Data, FileCategory.Data, "Different meaning"),
        ]);

        Assert.Throws<ArgumentException>(action);
    }

    private static FileItem CreateFile(string relativePath) => new(
        Guid.NewGuid(),
        relativePath,
        FileKind.Unknown,
        42,
        new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
}
