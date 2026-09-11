using System.Security.Cryptography;
using System.Text;
using DeskAI.Core.Content;
using DeskAI.Core.Execution;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Content;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Tests;

/// <summary>The file reader behind confirming copies, on generated files: what it refuses, and what it reads.</summary>
public sealed class FileFingerprinterTests
{
    [Fact]
    public async Task Reads_the_whole_file_or_just_its_beginning_and_changes_nothing()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        var path = sandbox.CreateDummyFile(@"Folder\a.txt", "Generated content for fingerprinting");
        var before = File.GetLastWriteTimeUtc(path);
        var reader = new FileFingerprinter(new WindowsPathPolicy());

        var whole = await reader.FingerprintAsync(Root(folder), "a.txt", Facts(path), null, TestContext.Current.CancellationToken);
        var start = await reader.FingerprintAsync(Root(folder), "a.txt", Facts(path), 9, TestContext.Current.CancellationToken);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), whole.Hash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Generated"))), start.Hash);
        Assert.Equal(9, start.BytesRead);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData(RootAuthorizationScope.ControlledDemo)]
    [InlineData(RootAuthorizationScope.Organize)]
    public async Task A_folder_not_connected_for_reading_is_refused_before_anything_else(RootAuthorizationScope scope)
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        var path = sandbox.CreateDummyFile(@"Folder\a.txt");
        var root = AuthorizedRoot.Create(Guid.NewGuid(), folder, "Folder", RootAccessLevel.Allowed, scope);

        var result = await new FileFingerprinter(new WindowsPathPolicy()).FingerprintAsync(root, "a.txt", Facts(path), null, TestContext.Current.CancellationToken);

        Assert.Equal(FingerprintStatus.NotAllowed, result.Status);
    }

    [Fact]
    public async Task A_protected_file_is_refused()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        var path = sandbox.CreateDummyFile(@"Folder\tax-return.txt");

        var result = await new FileFingerprinter(new WindowsPathPolicy(userProtectedEntries: [path]))
            .FingerprintAsync(Root(folder), "tax-return.txt", Facts(path), null, TestContext.Current.CancellationToken);

        Assert.Equal(FingerprintStatus.Blocked, result.Status);
    }

    [Theory]
    [InlineData(@"..\outside.txt")]
    [InlineData(@"C:\Windows\win.ini")]
    public async Task A_path_leaving_the_folder_is_refused(string relativePath)
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        var outside = sandbox.CreateDummyFile("outside.txt");

        var result = await new FileFingerprinter(new WindowsPathPolicy())
            .FingerprintAsync(Root(folder), relativePath, Facts(outside), null, TestContext.Current.CancellationToken);

        Assert.Equal(FingerprintStatus.Blocked, result.Status);
        Assert.Null(result.Hash);
    }

    [Fact]
    public async Task A_subfolder_that_became_a_link_is_not_followed()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        var elsewhere = sandbox.CreateDummyFile(@"Elsewhere\a.txt");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(folder, "Sub"), Path.GetDirectoryName(elsewhere)!);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        var result = await new FileFingerprinter(new WindowsPathPolicy())
            .FingerprintAsync(Root(folder), @"Sub\a.txt", Facts(elsewhere), null, TestContext.Current.CancellationToken);

        Assert.Equal(FingerprintStatus.Link, result.Status);
    }

    [Fact]
    public async Task A_missing_file_is_reported_and_not_created()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");

        var result = await new FileFingerprinter(new WindowsPathPolicy())
            .FingerprintAsync(Root(folder), "gone.txt", new ExpectedFile(10, DateTimeOffset.UnixEpoch), null, TestContext.Current.CancellationToken);

        Assert.Equal(FingerprintStatus.Missing, result.Status);
        Assert.False(File.Exists(Path.Combine(folder, "gone.txt")));
    }

    private static AuthorizedRoot Root(string folder) =>
        AuthorizedRoot.Create(Guid.NewGuid(), folder, "Folder", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);

    private static ExpectedFile Facts(string path)
    {
        var info = new FileInfo(path);
        return new ExpectedFile(info.Length, info.LastWriteTimeUtc);
    }
}
