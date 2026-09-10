using System.Text.RegularExpressions;
using DeskAI.App.Help;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Reads the page files so the "?" buttons and the help catalog cannot drift apart: a typo
/// in a topic ID, or a topic nobody placed, fails here rather than on someone's screen.
/// </summary>
public sealed partial class HelpPlacementTests
{
    [Fact]
    public void Every_help_button_points_at_a_real_topic()
    {
        foreach (var (file, id) in PlacedTopics())
        {
            Assert.True(HelpCatalog.Find(id) is not null, $"{file} uses unknown help topic '{id}'.");
        }
    }

    [Fact]
    public void Every_topic_is_placed_on_a_page()
    {
        var placed = PlacedTopics().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var topic in HelpCatalog.All)
        {
            Assert.True(placed.Contains(topic.Id), $"Help topic '{topic.Id}' is not on any page.");
        }
    }

    private static IEnumerable<(string File, string Id)> PlacedTopics()
    {
        var appFolder = Path.Combine(RepositoryRoot(), "src", "DeskAI.App");
        var separator = Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(appFolder, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                                    && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)))
        {
            foreach (Match match in HelpButtonPattern().Matches(File.ReadAllText(file)))
            {
                yield return (Path.GetFileName(file), match.Groups[1].Value);
            }
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

    [GeneratedRegex(@"<controls:HelpButton[^>]*\bTopic=""([^""]+)""")]
    private static partial Regex HelpButtonPattern();
}
