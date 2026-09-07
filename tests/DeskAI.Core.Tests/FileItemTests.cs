using DeskAI.Core.Files;

namespace DeskAI.Core.Tests;

public sealed class FileItemTests
{
    [Fact]
    public void Constructor_RejectsAbsolutePath()
    {
        var action = () => new FileItem(
            Guid.NewGuid(),
            @"C:\Personal\document.pdf",
            FileKind.Document,
            10,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData(@"..\escape.txt")]
    [InlineData(@"folder\..\escape.txt")]
    public void Constructor_RejectsTraversalPath(string relativePath)
    {
        var action = () => new FileItem(
            Guid.NewGuid(),
            relativePath,
            FileKind.Document,
            10,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(action);
    }
}
