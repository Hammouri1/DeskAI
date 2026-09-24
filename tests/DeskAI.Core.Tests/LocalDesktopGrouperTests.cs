using DeskAI.Core.Classification;
using DeskAI.Core.Studio;

namespace DeskAI.Core.Tests;

public sealed class LocalDesktopGrouperTests
{
    [Fact]
    public void Groups_folders_by_the_kind_of_file_they_mostly_hold_and_files_by_type()
    {
        var grouper = new LocalDesktopGrouper(new DeterministicFileClassifier(DefaultFileTypeRules.Create()));
        var items = new[]
        {
            new DesktopItem("Python stuff", true, [new(".py", 12), new(".md", 1)], []),
            new DesktopItem("Essays", true, [new(".docx", 4)], []),
            new DesktopItem("Empty", true, [], []),
            new DesktopItem("holiday.jpg", false, [], []),
        };

        var groups = grouper.Group(items, out var notSure);

        Assert.Contains(groups, g => g.Name == "Coding" && g.Items.SequenceEqual(["Python stuff"]));
        Assert.Contains(groups, g => g.Name == "Documents" && g.Items.SequenceEqual(["Essays"]));
        Assert.Contains(groups, g => g.Name == "Pictures" && g.Items.SequenceEqual(["holiday.jpg"]));
        Assert.Equal(["Empty"], notSure);
    }

    [Fact]
    public void Groups_appear_in_the_order_their_first_item_does()
    {
        var grouper = new LocalDesktopGrouper(new DeterministicFileClassifier(DefaultFileTypeRules.Create()));
        var items = new[]
        {
            new DesktopItem("song.mp3", false, [], []),
            new DesktopItem("notes.docx", false, [], []),
            new DesktopItem("tune.wav", false, [], []),
        };

        var groups = grouper.Group(items, out _);

        Assert.Equal(["Music", "Documents"], groups.Select(g => g.Name));
        Assert.Equal(["song.mp3", "tune.wav"], groups[0].Items);
    }
}
