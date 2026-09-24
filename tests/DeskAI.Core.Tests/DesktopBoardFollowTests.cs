using DeskAI.Core.Studio;

namespace DeskAI.Core.Tests;

/// <summary>How the Find groups board follows Folder by group and its Put back: plain rules, no disk.</summary>
public sealed class DesktopBoardFollowTests
{
    [Fact]
    public void A_group_folder_the_person_placed_elsewhere_stays_where_they_put_it()
    {
        var board = Board([new("Coding", ["tool.py"]), new("Keep", ["Coding"])], []);

        var after = DesktopBoardFollow.PutInGroupFolders(board, [("tool.py", "Coding")]);

        Assert.Empty(after.Groups[0].Items);
        Assert.Equal(["Coding"], after.Groups[1].Items);
    }

    [Fact]
    public void A_group_folder_under_Not_sure_joins_its_group()
    {
        var board = Board([new("Coding", ["tool.py"])], ["Coding", "mystery.zzz"]);

        var after = DesktopBoardFollow.PutInGroupFolders(board, [("tool.py", "Coding")]);

        Assert.Equal(["Coding"], after.Groups[0].Items);
        Assert.Equal(["mystery.zzz"], after.NotSure);
        Assert.Contains("Coding", after.Folders);
    }

    [Fact]
    public void Put_back_keeps_a_folder_that_was_already_there_and_drops_one_it_removed()
    {
        var board = Board([new("Coding", ["Coding"]), new("Pictures", ["Pictures"])], []) with
        {
            Folders = new HashSet<string>(["Coding", "Pictures"], StringComparer.OrdinalIgnoreCase),
        };

        var after = DesktopBoardFollow.TakenOutOfGroupFolders(
            board,
            [(Path.Combine("Coding", "tool.py"), "tool.py"), (Path.Combine("Pictures", "cat.jpg"), "cat.jpg")],
            ["Pictures"]);

        Assert.Equal(["Coding", "tool.py"], after.Groups[0].Items);
        Assert.Equal(["cat.jpg"], after.Groups[1].Items);
        Assert.DoesNotContain("Pictures", after.Folders);
    }

    [Fact]
    public void Something_put_back_whose_group_is_gone_goes_under_Not_sure()
    {
        var board = Board([new("Writing", ["essay.docx"])], []);

        var after = DesktopBoardFollow.TakenOutOfGroupFolders(board, [(Path.Combine("Coding", "tool.py"), "tool.py")], []);

        Assert.Equal(["essay.docx"], after.Groups[0].Items);
        Assert.Equal(["tool.py"], after.NotSure);
    }

    private static DesktopGroupBoard Board(IReadOnlyList<DesktopGroup> groups, IReadOnlyList<string> notSure) =>
        new(Guid.NewGuid(), groups, notSure, DesktopGroupSource.LocalGuess, DateTimeOffset.UnixEpoch);
}
