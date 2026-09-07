namespace DeskAI.Infrastructure.Tests;

public sealed class TemporaryDirectory : IDisposable
{
    private const string OwnedPrefix = "DeskAI.Tests.";
    private readonly string _canonicalTempRoot;
    private bool _disposed;

    public TemporaryDirectory()
    {
        _canonicalTempRoot = System.IO.Path.TrimEndingDirectorySeparator(
            System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()));
        Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            _canonicalTempRoot,
            OwnedPrefix + Guid.NewGuid().ToString("N")));

        VerifyOwnedPath(Path);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDummyFile(string relativePath, string content = "Generated DeskAI test data")
    {
        var fullPath = System.IO.Path.GetFullPath(relativePath, Path);
        VerifySandboxContained(fullPath);
        var parent = System.IO.Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Dummy file path has no parent.");
        Directory.CreateDirectory(parent);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public string CreateDummyDirectory(string relativePath)
    {
        var fullPath = System.IO.Path.GetFullPath(relativePath, Path);
        VerifySandboxContained(fullPath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        VerifyOwnedPath(Path);
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private void VerifyOwnedPath(string candidate)
    {
        VerifyContained(candidate);
        var name = System.IO.Path.GetFileName(candidate);
        if (!name.StartsWith(OwnedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to clean a directory not owned by this test run.");
        }
    }

    private void VerifyContained(string candidate)
    {
        var relative = System.IO.Path.GetRelativePath(_canonicalTempRoot, candidate);
        if (System.IO.Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The test sandbox escaped the system temporary directory.");
        }
    }

    private void VerifySandboxContained(string candidate)
    {
        var relative = System.IO.Path.GetRelativePath(Path, candidate);
        if (System.IO.Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The requested path escaped this test's owned sandbox.");
        }
    }
}
