namespace DeskAI.Core.Tests;

/// <summary>
/// DeskAI never registers itself to start with Windows. This is a promise the app makes to
/// people in words, on the Automatic tasks page and in the dialog that turns background
/// checking on, so it is asserted rather than left to intent. See docs/SECURITY.md.
/// </summary>
public sealed class NeverStartsWithWindowsTests
{
    [Fact]
    public void No_source_file_registers_DeskAI_to_start_with_Windows()
    {
        string[] forbidden =
        [
            "CurrentVersion\\\\Run",
            "CurrentVersion/Run",
            "StartupTask",
            "Microsoft.Win32.Registry",
            "TaskScheduler",
            "schtasks",
            "SpecialFolder.Startup",
        ];

        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var needle in forbidden)
            {
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)} contains '{needle}'");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var source = Path.Combine(RepositoryRoot(), "src");
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                           && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DeskAI.sln not found.");
    }
}
