using System.Text.RegularExpressions;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// V1.0's rule that no page presents a placeholder as a finished feature. Reads the page files
/// for the words a placeholder is usually made of.
/// </summary>
public sealed partial class NoPlaceholderUiTests
{
    [Fact]
    public void No_page_carries_placeholder_words()
    {
        var views = Path.Combine(RepositoryRoot(), "src", "DeskAI.App", "Views");
        foreach (var file in Directory.EnumerateFiles(views, "*.xaml").Append(Path.Combine(RepositoryRoot(), "src", "DeskAI.App", "MainWindow.xaml")))
        {
            // Comments explain design; only what a person can read on screen counts.
            var visible = XamlComment().Replace(File.ReadAllText(file), string.Empty);
            var hit = PlaceholderWords().Match(visible);
            Assert.False(hit.Success, $"{Path.GetFileName(file)} shows a placeholder: '{hit.Value}'.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DeskAI.sln was not found above the test output.");
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XamlComment();

    [GeneratedRegex(@"coming soon|not implemented|\bTODO\b|placeholder text|lorem ipsum|under construction", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderWords();
}
