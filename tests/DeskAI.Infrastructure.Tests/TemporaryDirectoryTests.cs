namespace DeskAI.Infrastructure.Tests;

public sealed class TemporaryDirectoryTests
{
    [Fact]
    public void CreateDummyFile_CreatesOnlyInsideOwnedSandbox()
    {
        using var sandbox = new TemporaryDirectory();

        var file = sandbox.CreateDummyFile(@"input\sample.txt");

        Assert.True(File.Exists(file));
        Assert.StartsWith(sandbox.Path, file, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDummyFile_RejectsTraversal()
    {
        using var sandbox = new TemporaryDirectory();

        var action = () => sandbox.CreateDummyFile(@"..\outside.txt");

        Assert.Throws<InvalidOperationException>(action);
    }
}
