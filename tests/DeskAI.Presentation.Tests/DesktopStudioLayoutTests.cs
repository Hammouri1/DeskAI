namespace DeskAI.Presentation.Tests;

/// <summary>
/// Reads the Desktop Studio page so the board stays tidy on a real Desktop with dozens of items.
/// The owner found (2026-09-24) that Not sure grew into one very long column and that the Rename
/// and Merge buttons squeezed group names into "Document s".
/// </summary>
public sealed class DesktopStudioLayoutTests
{
    [Fact]
    public void Group_cards_are_one_size_and_long_lists_scroll_inside_the_card()
    {
        var group = Section(Page(), "<DataTemplate x:DataType=\"viewmodels:DesktopGroupViewModel\">", "</DataTemplate>\n                    </ItemsControl.ItemTemplate>");

        Assert.Contains("Height=\"{StaticResource GroupCardHeight}\"", group, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer", group, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind CountText}\"", group, StringComparison.Ordinal);
    }

    [Fact]
    public void Rename_and_merge_sit_in_one_small_menu_so_the_group_name_keeps_its_room()
    {
        var group = Section(Page(), "<DataTemplate x:DataType=\"viewmodels:DesktopGroupViewModel\">", "</DataTemplate>\n                    </ItemsControl.ItemTemplate>");

        Assert.DoesNotContain("Content=\"Rename\"", group, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Merge into…\"", group, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Rename or merge\"", group, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_sure_uses_the_full_width_in_columns_and_stops_growing()
    {
        var notSure = Section(Page(), "Visibility=\"{x:Bind ViewModel.HasNotSure, Mode=OneWay}\"", "</Border>");

        Assert.DoesNotContain("MaxWidth=", notSure, StringComparison.Ordinal);
        Assert.Contains("<WrapGrid", notSure, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"{StaticResource NotSureMaxHeight}\"", notSure, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind ViewModel.NotSureCountText, Mode=OneWay}\"", notSure, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_moving_card_has_its_help_its_tick_boxes_its_total_and_Put_back()
    {
        var page = Page();
        foreach (var (topic, card) in new[] { ("studio.oldStuff", "OldStuff"), ("studio.folderByGroup", "FolderByGroup") })
        {
            var section = Section(page, $"Topic=\"{topic}\"", "</Border>");
            Assert.Contains($"ViewModel.{card}.Items", section, StringComparison.Ordinal);
            Assert.Contains($"ViewModel.{card}.TotalText", section, StringComparison.Ordinal);
            Assert.Contains("Content=\"Put back\"", section, StringComparison.Ordinal);
        }

        var row = Section(page, "<DataTemplate x:Key=\"MoveItemTemplate\"", "</DataTemplate>");
        Assert.Contains("IsChecked=\"{x:Bind IsTicked, Mode=TwoWay}\"", row, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Warning}\"", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// Found in review 2026-09-24: the page, the permission dialog, and the help promised that Put
    /// back "returns everything", but it is offered only for the latest change on the Desktop.
    /// </summary>
    [Fact]
    public void Put_back_is_never_promised_for_more_than_the_latest_change()
    {
        var dialog = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "DeskAI.App", "Views", "DesktopStudioPage.xaml.cs"));
        var help = string.Join(' ', new[] { "studio.oldStuff", "studio.folderByGroup" }
            .Select(id => DeskAI.App.Help.HelpCatalog.Find(id)!)
            .Select(topic => $"{topic.WhatItDoes} {topic.WhatItNeverDoes}"));

        foreach (var text in new[] { Page(), dialog, help })
        {
            Assert.DoesNotContain("returns everything", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Put back returns them", text, StringComparison.Ordinal);
            Assert.Contains("latest change", text, StringComparison.Ordinal);
        }
    }

    private static string Page() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "DeskAI.App", "Views", "DesktopStudioPage.xaml")).ReplaceLineEndings("\n");

    private static string Section(string xaml, string start, string end)
    {
        var from = xaml.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"Missing {start}");
        var to = xaml.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"Missing {end} after {start}");
        return xaml[from..to];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
