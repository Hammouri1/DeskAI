using DeskAI.Core.Files;
using DeskAI.Core.Classification;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Scanning;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Tests;

public sealed class WindowsMetadataScannerTests
{
    [Fact]
    public async Task ScanAsync_ReportsEachFolderItEntersOrSkipsForDepth()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile(@"Projects\App\main.py");
        sandbox.CreateDummyDirectory("Empty");
        var scanner = new WindowsMetadataScanner(new WindowsPathPolicy());

        var folders = new List<string>();
        await foreach (var scanEvent in scanner.ScanAsync(CreateRoot(sandbox.Path), new MetadataScanOptions(1, 100), TestContext.Current.CancellationToken))
        {
            if (scanEvent is FolderDiscovered folder)
            {
                folders.Add(folder.RelativePath);
            }
        }

        Assert.Equal(["Empty", "Projects", @"Projects\App"], folders.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAndClassify_ComposesWithoutReadingContentsOrUsingAi()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("report.pdf", "dummy PDF-like test bytes");
        sandbox.CreateDummyFile("Screenshot 2026-09-07.png", "dummy image-like test bytes");
        var scanner = new WindowsMetadataScanner(new WindowsPathPolicy());
        var classifier = new DeterministicFileClassifier(DefaultFileTypeRules.Create());

        var events = await CollectAsync(
            scanner,
            CreateRoot(sandbox.Path),
            TestContext.Current.CancellationToken);
        var classifications = events
            .OfType<FileDiscovered>()
            .ToDictionary(item => item.File.RelativePath, item => classifier.Classify(item.File));

        Assert.Equal(FileCategory.Documents, classifications["report.pdf"].Category);
        Assert.Equal(FileCategory.Screenshots, classifications["Screenshot 2026-09-07.png"].Category);
        Assert.All(classifications.Values, result =>
            Assert.DoesNotContain(result.Source, new[] { ClassificationSource.LocalAi, ClassificationSource.CloudAi }));
    }

    [Fact]
    public async Task ScanAsync_StreamsRootRelativeMetadataFromOwnedSandbox()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile(@"Documents\report.txt", "12345");
        sandbox.CreateDummyFile(@"image.png", "1234567");
        var root = CreateRoot(sandbox.Path);

        var events = await CollectAsync(new WindowsMetadataScanner(new WindowsPathPolicy()), root, TestContext.Current.CancellationToken);
        var files = events.OfType<FileDiscovered>().Select(item => item.File).ToArray();

        Assert.Equal(2, files.Length);
        Assert.Contains(files, file => file.RelativePath == @"Documents\report.txt" && file.SizeBytes == 5);
        Assert.Contains(files, file => file.RelativePath == "image.png" && file.SizeBytes == 7);
        Assert.All(files, file =>
        {
            Assert.False(Path.IsPathRooted(file.RelativePath));
            Assert.Equal(FileKind.Unknown, file.Kind);
        });
    }

    [Fact]
    public async Task ScanAsync_UsesStableIdsForSameRootAndRelativePath()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("stable.txt");
        var root = CreateRoot(sandbox.Path);
        var scanner = new WindowsMetadataScanner(new WindowsPathPolicy());

        var first = (await CollectAsync(scanner, root, TestContext.Current.CancellationToken)).OfType<FileDiscovered>().Single().File;
        var second = (await CollectAsync(scanner, root, TestContext.Current.CancellationToken)).OfType<FileDiscovered>().Single().File;

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task ScanAsync_ReadsMetadataWithoutOpeningFileContents()
    {
        using var sandbox = new TemporaryDirectory();
        var path = sandbox.CreateDummyFile("locked.txt", "content that must not be read");
        var root = CreateRoot(sandbox.Path);
        await using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var events = await CollectAsync(new WindowsMetadataScanner(new WindowsPathPolicy()), root, TestContext.Current.CancellationToken);

        Assert.Contains(events.OfType<FileDiscovered>(), item => item.File.RelativePath == "locked.txt");
    }

    [Fact]
    public async Task ScanAsync_SkipsProtectedEntryWithoutEnumeratingItsFiles()
    {
        using var sandbox = new TemporaryDirectory();
        var protectedDirectory = sandbox.CreateDummyDirectory("Private");
        sandbox.CreateDummyFile(@"Private\secret.txt");
        sandbox.CreateDummyFile("visible.txt");
        var root = CreateRoot(sandbox.Path);
        var scanner = new WindowsMetadataScanner(new WindowsPathPolicy(
            userProtectedEntries: [protectedDirectory]));

        var events = await CollectAsync(scanner, root, TestContext.Current.CancellationToken);

        Assert.Contains(events.OfType<ScanIssue>(), issue =>
            issue.RelativePath == "Private" && issue.Code == ScanIssueCode.ProtectedEntrySkipped);
        Assert.DoesNotContain(events.OfType<FileDiscovered>(), item => item.File.RelativePath.Contains("secret", StringComparison.Ordinal));
        Assert.Contains(events.OfType<FileDiscovered>(), item => item.File.RelativePath == "visible.txt");
    }

    [Fact]
    public async Task ScanAsync_DoesNotFollowDirectoryReparsePoint()
    {
        using var sandbox = new TemporaryDirectory();
        var target = sandbox.CreateDummyDirectory("Target");
        sandbox.CreateDummyFile(@"Target\inside.txt");
        var link = Path.Combine(sandbox.Path, "Link");

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        var events = await CollectAsync(
            new WindowsMetadataScanner(new WindowsPathPolicy()),
            CreateRoot(sandbox.Path),
            TestContext.Current.CancellationToken);

        Assert.Contains(events.OfType<ScanIssue>(), issue =>
            issue.RelativePath == "Link" && issue.Code == ScanIssueCode.ReparsePointSkipped);
        Assert.DoesNotContain(events.OfType<FileDiscovered>(), item => item.File.RelativePath.StartsWith(@"Link\", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_ReportsMissingRootWithoutThrowing()
    {
        using var sandbox = new TemporaryDirectory();
        var missing = Path.Combine(sandbox.Path, "Missing");

        var events = await CollectAsync(
            new WindowsMetadataScanner(new WindowsPathPolicy()),
            CreateRoot(missing),
            TestContext.Current.CancellationToken);

        var issue = Assert.Single(events.OfType<ScanIssue>());
        Assert.Equal(ScanIssueCode.RootUnavailable, issue.Code);
        Assert.Equal(".", issue.RelativePath);
    }

    [Fact]
    public async Task ScanAsync_RefusesProtectedRoot()
    {
        using var sandbox = new TemporaryDirectory();
        var root = AuthorizedRoot.Create(Guid.NewGuid(), sandbox.Path, "Protected test root", RootAccessLevel.Protected, RootAuthorizationScope.Organize);

        var events = await CollectAsync(new WindowsMetadataScanner(new WindowsPathPolicy()), root, TestContext.Current.CancellationToken);

        Assert.Equal(ScanIssueCode.RootProtected, Assert.Single(events.OfType<ScanIssue>()).Code);
    }

    [Fact]
    public async Task ScanAsync_StopsAtConfiguredEntryLimit()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("one.txt");
        sandbox.CreateDummyFile("two.txt");
        var scanner = new WindowsMetadataScanner(new WindowsPathPolicy());

        var events = await CollectAsync(
            scanner,
            CreateRoot(sandbox.Path),
            TestContext.Current.CancellationToken,
            new MetadataScanOptions(maxDepth: 10, maxEntries: 1));

        Assert.Single(events.OfType<FileDiscovered>());
        Assert.Contains(events.OfType<ScanIssue>(), issue => issue.Code == ScanIssueCode.EntryLimitReached);
    }

    [Fact]
    public async Task ScanAsync_DoesNotEnterDirectoryBeyondDepthLimit()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile(@"Nested\hidden.txt");
        sandbox.CreateDummyFile("root.txt");
        var scanner = new WindowsMetadataScanner(new WindowsPathPolicy());

        var events = await CollectAsync(
            scanner,
            CreateRoot(sandbox.Path),
            TestContext.Current.CancellationToken,
            new MetadataScanOptions(maxDepth: 0, maxEntries: 10));

        Assert.Contains(events.OfType<FileDiscovered>(), item => item.File.RelativePath == "root.txt");
        Assert.DoesNotContain(events.OfType<FileDiscovered>(), item => item.File.RelativePath.Contains("hidden", StringComparison.Ordinal));
        Assert.Contains(events.OfType<ScanIssue>(), issue => issue.Code == ScanIssueCode.DepthLimitReached);
    }

    [Fact]
    public async Task ScanAsync_HonorsCancellationBeforeFilesystemWork()
    {
        using var sandbox = new TemporaryDirectory();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var scanner = new WindowsMetadataScanner(new WindowsPathPolicy());

        var action = async () =>
        {
            await foreach (var _ in scanner.ScanAsync(CreateRoot(sandbox.Path), MetadataScanOptions.Default, source.Token))
            {
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
    }

    [Fact]
    public async Task ScanAsync_ReportsHiddenSystemAndOnlineOnlyFilesWithoutOpeningThem()
    {
        using var sandbox = new TemporaryDirectory();
        var hidden = sandbox.CreateDummyFile("hidden.txt");
        var system = sandbox.CreateDummyFile("system.txt");
        var online = sandbox.CreateDummyFile("online.txt");
        sandbox.CreateDummyFile("plain.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(system, FileAttributes.System);
        File.SetAttributes(online, FileAttributes.Offline);
        try
        {
            var events = await CollectAsync(new WindowsMetadataScanner(new WindowsPathPolicy()), CreateRoot(sandbox.Path), TestContext.Current.CancellationToken);
            var files = events.OfType<FileDiscovered>().ToDictionary(item => item.File.RelativePath, item => item.File.Traits);

            Assert.Equal(FileTraits.Hidden, files["hidden.txt"]);
            Assert.Equal(FileTraits.System, files["system.txt"]);
            Assert.Equal(FileTraits.OnlineOnly, files["online.txt"]);
            Assert.Equal(FileTraits.None, files["plain.txt"]);
        }
        finally
        {
            foreach (var path in new[] { hidden, system, online })
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }
    }

    private static AuthorizedRoot CreateRoot(string path) => AuthorizedRoot.Create(
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        path,
        "Generated test root",
        RootAccessLevel.Allowed, RootAuthorizationScope.Organize);

    private static async Task<IReadOnlyList<ScanEvent>> CollectAsync(
        WindowsMetadataScanner scanner,
        AuthorizedRoot root,
        CancellationToken cancellationToken,
        MetadataScanOptions? options = null)
    {
        var events = new List<ScanEvent>();
        await foreach (var scanEvent in scanner.ScanAsync(root, options ?? MetadataScanOptions.Default, cancellationToken))
        {
            events.Add(scanEvent);
        }

        return events;
    }
}
