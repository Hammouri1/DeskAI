using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;

namespace DeskAI.Core.Tests;

public sealed class IndexedFileTests
{
    private static readonly Guid RootId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Moment = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(@"..\..\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"Study\..\..\secrets.txt")]
    [InlineData(@"C:\Users\Someone\Desktop\report.pdf")]
    [InlineData(@"\\server\share\report.pdf")]
    [InlineData(@"Study\notes.txt:hidden")]
    [InlineData(@"\Study\report.pdf")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("   ")]
    public void Constructor_RejectsPathsThatCouldEscapeTheAuthorizedRoot(string relativePath)
    {
        Assert.ThrowsAny<ArgumentException>(() => Create(relativePath));
    }

    [Fact]
    public void Constructor_RejectsPathLongerThanTheSupportedLimit()
    {
        var tooLong = new string('a', IndexedFile.MaxRelativePathLength + 1);

        Assert.Throws<ArgumentException>(() => Create(tooLong));
    }

    [Fact]
    public void Constructor_RejectsMissingRootOrFileIdentity()
    {
        Assert.Throws<ArgumentException>(() => new IndexedFile(
            Guid.Empty, FileId, "notes.txt", FileKind.Document, FileCategory.Documents, 1, Moment, Moment, Moment));
        Assert.Throws<ArgumentException>(() => new IndexedFile(
            RootId, Guid.Empty, "notes.txt", FileKind.Document, FileCategory.Documents, 1, Moment, Moment, Moment));
    }

    [Fact]
    public void Constructor_RejectsNegativeSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexedFile(
            RootId, FileId, "notes.txt", FileKind.Document, FileCategory.Documents, -1, Moment, Moment, Moment));
    }

    [Theory]
    [InlineData(@"Study/term one\notes.TXT", @"Study\term one\notes.TXT", "notes.TXT", ".txt")]
    [InlineData(@"Study\\report.PDF", @"Study\report.PDF", "report.PDF", ".pdf")]
    [InlineData("readme", "readme", "readme", "")]
    public void Constructor_NormalizesPathAndDerivesNameAndExtension(
        string input,
        string expectedPath,
        string expectedName,
        string expectedExtension)
    {
        var file = Create(input);

        Assert.Equal(expectedPath, file.RelativePath);
        Assert.Equal(expectedName, file.Name);
        Assert.Equal(expectedExtension, file.Extension);
    }

    [Fact]
    public void MatchesStoredFacts_IgnoresWhenTheEntryWasIndexed()
    {
        var first = Create(@"Study\notes.txt");
        var laterRefresh = new IndexedFile(
            RootId, FileId, @"Study\notes.txt", FileKind.Document, FileCategory.Documents,
            10, Moment, Moment, Moment.AddDays(3));

        Assert.True(first.MatchesStoredFacts(laterRefresh));
    }

    [Theory]
    [InlineData(@"Study\renamed.txt", 10L, FileCategory.Documents)]
    [InlineData(@"Study\notes.txt", 11L, FileCategory.Documents)]
    [InlineData(@"Study\notes.txt", 10L, FileCategory.Archives)]
    public void MatchesStoredFacts_DetectsFactsARescanCanChange(
        string relativePath,
        long sizeBytes,
        FileCategory category)
    {
        var stored = Create(@"Study\notes.txt");
        var rescanned = new IndexedFile(
            RootId, FileId, relativePath, FileKind.Document, category, sizeBytes, Moment, Moment, Moment);

        Assert.False(stored.MatchesStoredFacts(rescanned));
    }

    private static IndexedFile Create(string relativePath) => new(
        RootId,
        FileId,
        relativePath,
        FileKind.Document,
        FileCategory.Documents,
        10,
        Moment,
        Moment,
        Moment);
}
