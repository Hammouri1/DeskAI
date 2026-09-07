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

    private static ReadOnlyFolderService CreateService(
        InMemoryAuthorizedRootRepository repository,
        WindowsPathPolicy policy) =>
        new(new WindowsMetadataScanner(policy), repository, policy);
}
