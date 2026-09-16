using System.Text.RegularExpressions;

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
        // Checked against the file text exactly as written. Kept even though the
        // separator-sensitive entries here are also covered, in every spelling, by the
        // normalized needle below — an exact match still names itself by its original wording
        // in the failure output.
        string[] forbidden =
        [
            "CurrentVersion\\\\Run",
            "CurrentVersion/Run",
            "StartupTask",
            "Microsoft.Win32.Registry",
            "TaskScheduler",
            "schtasks",
            "SpecialFolder.Startup",
            "Microsoft.Win32",
            "Registry.CurrentUser",
            "Registry.LocalMachine",
        ];

        // A Run-key path can be written as a verbatim string with one backslash, a normal
        // string with an escaped double backslash, or — rarely — with forward slashes. Collapse
        // every run of '/' or '\' to one canonical separator before matching, so the needle
        // below catches all three spellings instead of only the one it happens to be written
        // to match literally.
        string[] forbiddenAfterNormalizingSeparators = ["CurrentVersion\\Run"];

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

            var normalized = NormalizeSeparators(text);
            foreach (var needle in forbiddenAfterNormalizingSeparators)
            {
                if (normalized.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)} contains a normalized '{needle}'");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string NormalizeSeparators(string text) => Regex.Replace(text, "[/\\\\]+", "\\");

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
