using DeskAI.Core.Files;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Scanning;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Tests;

public sealed class ReadOnlyFolderServiceTests
{
    [Fact]
    public async Task AuthorizeAndPreviewAsync_StoresMetadataOnlyPermissionAndReadsNoContents()
    {
        using var sandbox = new TemporaryDirectory();
        var lockedPath = sandbox.CreateDummyFile(@"Study\notes.txt", "private dummy content");
        await using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var repository = new InMemoryAuthorizedRootRepository();
        var service = CreateService(repository, new WindowsPathPolicy());

        var result = await service.AuthorizeAndPreviewAsync(
            sandbox.Path,
            new MetadataScanOptions(3, 20),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAllowed);
        Assert.Equal(RootAuthorizationScope.MetadataOnly, result.Root!.AuthorizationScope);
        Assert.Contains(result.Files, file => file.RelativePath == @"Study\notes.txt");
        Assert.Equal(result.Root, await repository.FindAsync(result.Root.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthorizeAndPreviewAsync_RefusesRootContainingProtectedChild()
    {
        using var sandbox = new TemporaryDirectory();
        var protectedChild = sandbox.CreateDummyDirectory("Credentials");
        sandbox.CreateDummyFile("visible.txt");
        var repository = new InMemoryAuthorizedRootRepository();
        var service = CreateService(repository, new WindowsPathPolicy([protectedChild]));

        var result = await service.AuthorizeAndPreviewAsync(
            sandbox.Path,
            new MetadataScanOptions(3, 20),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Empty(await repository.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RevokeAsync_ForgetsPermissionWithoutChangingFiles()
    {
        using var sandbox = new TemporaryDirectory();
        var path = sandbox.CreateDummyFile("keep-me.txt", "generated data");
        var repository = new InMemoryAuthorizedRootRepository();
        var service = CreateService(repository, new WindowsPathPolicy());
        var result = await service.AuthorizeAndPreviewAsync(
            sandbox.Path,
            new MetadataScanOptions(3, 20),
            TestContext.Current.CancellationToken);

        await service.RevokeAsync(result.Root!.Id, TestContext.Current.CancellationToken);

        Assert.Empty(await service.ListAuthorizedAsync(TestContext.Current.CancellationToken));
        Assert.True(File.Exists(path));
        Assert.Equal("generated data", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    // CheckStillSafeAsync is the re-check behind "Allow tidying" and before every file a tidy
    // moves. Until the V0.6 review (2026-09-11) it was only ever replaced by fakes in tests.

    [Fact]
    public async Task CheckStillSafeAsync_AcceptsAnOrdinaryFolder()
    {
        using var sandbox = new TemporaryDirectory();
        var service = CreateService(new InMemoryAuthorizedRootRepository(), new WindowsPathPolicy());

        Assert.Null(await service.CheckStillSafeAsync(Root(sandbox.CreateDummyDirectory("Downloads")), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(@"\\server\share\Downloads")]
    [InlineData(@"\\?\C:\Downloads")]
    [InlineData(@"\\.\C:\Downloads")]
    public async Task CheckStillSafeAsync_RefusesNetworkAndDeviceLocations(string path)
    {
        var service = CreateService(new InMemoryAuthorizedRootRepository(), new WindowsPathPolicy());

        var problem = await service.CheckStillSafeAsync(Root(path), TestContext.Current.CancellationToken);

        Assert.Equal("Network, device, and whole-drive locations cannot be tidied.", problem);
    }

    [Fact]
    public async Task CheckStillSafeAsync_RefusesAWholeDrive()
    {
        var service = CreateService(new InMemoryAuthorizedRootRepository(), new WindowsPathPolicy());
        var drive = Path.GetPathRoot(Path.GetTempPath())!;

        var problem = await service.CheckStillSafeAsync(Root(drive), TestContext.Current.CancellationToken);

        Assert.Equal("Network, device, and whole-drive locations cannot be tidied.", problem);
    }

    [Fact]
    public async Task CheckStillSafeAsync_RefusesAFolderThatIsGone()
    {
        using var sandbox = new TemporaryDirectory();
        var service = CreateService(new InMemoryAuthorizedRootRepository(), new WindowsPathPolicy());

        var problem = await service.CheckStillSafeAsync(Root(Path.Combine(sandbox.Path, "Moved-away")), TestContext.Current.CancellationToken);

        Assert.Equal("That folder is no longer available.", problem);
    }

    [Fact]
    public async Task CheckStillSafeAsync_RefusesAFolderReachedThroughALink()
    {
        using var sandbox = new TemporaryDirectory();
        var target = sandbox.CreateDummyDirectory("Real");
        var link = Path.Combine(sandbox.Path, "Linked");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        var service = CreateService(new InMemoryAuthorizedRootRepository(), new WindowsPathPolicy());

        Assert.Equal(
            "This folder crosses a link or shortcut, so DeskAI will not tidy it.",
            await service.CheckStillSafeAsync(Root(link), TestContext.Current.CancellationToken));
        Assert.Equal(
            "This folder crosses a link or shortcut, so DeskAI will not tidy it.",
            await service.CheckStillSafeAsync(Root(sandbox.CreateDummyDirectory(@"Linked\Inside")), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckStillSafeAsync_RefusesAProtectedFolder()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Taxes");
        var service = CreateService(new InMemoryAuthorizedRootRepository(), new WindowsPathPolicy([folder]));

        Assert.Equal(
            "This location is protected and cannot be tidied.",
            await service.CheckStillSafeAsync(Root(folder), TestContext.Current.CancellationToken));
    }

    private static AuthorizedRoot Root(string path) =>
        AuthorizedRoot.Create(Guid.NewGuid(), path, "Folder", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);

    private static ReadOnlyFolderService CreateService(
        InMemoryAuthorizedRootRepository repository,
        WindowsPathPolicy policy) =>
        new(new WindowsMetadataScanner(policy), repository, policy);
}
