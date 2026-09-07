using DeskAI.Core.Ai;
using DeskAI.Core.Files;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tests;

public sealed class AiRequestBuilderTests
{
    [Fact]
    public void Build_IncludesOnlyExplicitlyAllowedFieldsAndOmitsProtectedFiles()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Synthetic", "Synthetic", RootAccessLevel.Allowed);
        var visible = CreateFile(@"Study\report.private.pdf");
        var protectedFile = CreateFile(@"Private\secret.txt");

        var request = AiRequestBuilder.Build(
            root,
            [visible, protectedFile],
            new HashSet<Guid> { protectedFile.Id },
            new HashSet<DisclosureCategory> { DisclosureCategory.Extension, DisclosureCategory.Metadata },
            AiRequestLimits.Default);

        var candidate = Assert.Single(request.Files);
        Assert.Equal(visible.Id, candidate.FileId);
        Assert.Equal(".pdf", candidate.Extension);
        Assert.Equal(42, candidate.SizeBytes);
        Assert.Null(candidate.FileName);
        Assert.Null(candidate.RelativeFolder);
        Assert.Null(candidate.FullPath);
        Assert.Equal(1, request.Disclosure.ProtectedFileCount);
    }

    [Fact]
    public void Build_RespectsMaximumFileCount()
    {
        var root = AuthorizedRoot.Create(Guid.NewGuid(), @"C:\DeskAITests\Synthetic", "Synthetic", RootAccessLevel.Allowed);
        var limits = AiRequestLimits.Default with { MaximumFiles = 1 };

        var request = AiRequestBuilder.Build(
            root,
            [CreateFile("a.txt"), CreateFile("b.txt")],
            new HashSet<Guid>(),
            new HashSet<DisclosureCategory> { DisclosureCategory.Extension },
            limits);

        Assert.Single(request.Files);
    }

    private static FileItem CreateFile(string relativePath) => new(
        Guid.NewGuid(), relativePath, FileKind.Unknown, 42,
        DateTimeOffset.Parse("2026-09-08T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-09-08T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
