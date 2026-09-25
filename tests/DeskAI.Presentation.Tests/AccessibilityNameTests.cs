using System.Text.RegularExpressions;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Reads the page files so every control a screen reader lands on has a name: a button with no
/// visible text, and every box, list, and switch, must carry an automation name or a header.
/// A control with no name is one Narrator reads as "button", which is no help to anyone.
/// </summary>
public sealed partial class AccessibilityNameTests
{
    [Fact]
    public void Every_button_without_visible_text_has_an_automation_name()
    {
        foreach (var (file, tag) in Controls("Button"))
        {
            // Content that is text, or bound to text, is what a screen reader reads; only an icon-only button needs a name.
            if (tag.Contains("Content=\"", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(
                tag.Contains("AutomationProperties.Name=", StringComparison.Ordinal),
                $"{file}: a button without visible text needs AutomationProperties.Name: {Trim(tag)}");
        }
    }

    [Theory]
    [InlineData("TextBox")]
    [InlineData("PasswordBox")]
    [InlineData("ComboBox")]
    [InlineData("AutoSuggestBox")]
    [InlineData("ToggleSwitch")]
    [InlineData("NumberBox")]
    public void Every_input_has_a_header_or_an_automation_name(string control)
    {
        foreach (var (file, tag) in Controls(control))
        {
            Assert.True(
                tag.Contains("Header=", StringComparison.Ordinal) || tag.Contains("AutomationProperties.Name=", StringComparison.Ordinal),
                $"{file}: a {control} needs a Header or AutomationProperties.Name: {Trim(tag)}");
        }
    }

    private static IEnumerable<(string File, string Tag)> Controls(string control)
    {
        var app = Path.Combine(RepositoryRoot(), "src", "DeskAI.App");
        var files = Directory.EnumerateFiles(Path.Combine(app, "Views"), "*.xaml")
            .Append(Path.Combine(app, "MainWindow.xaml"));
        // "(?![.\w])": a property element such as <TextBox.Resources> is part of a control, not another one.
        var pattern = new Regex($@"<{control}(?![.\w])[^>]*?(/>|>)", RegexOptions.Singleline);
        foreach (var file in files)
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                yield return (Path.GetFileName(file), match.Value);
            }
        }
    }

    private static string Trim(string tag) => Regex.Replace(tag, @"\s+", " ");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DeskAI.sln was not found above the test output.");
    }
}
