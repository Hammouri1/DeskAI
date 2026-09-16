using DeskAI.Core.Templates;

namespace DeskAI.Core.Tests;

/// <summary>
/// Typed folder names are the one untrusted input in folder templates. Every way a name could
/// reach outside the folder, name a Windows device, or be something Windows refuses is turned
/// away here with a reason a person can act on.
/// </summary>
public sealed class FolderNameCheckTests
{
    [Theory]
    [InlineData("Tax 2026")]
    [InlineData("Photos")]
    [InlineData("Année 1")]
    [InlineData("v1.0")]
    [InlineData(".hidden")]
    [InlineData("CONFIG")]
    [InlineData("Notes (old)")]
    public void A_plain_folder_name_passes(string name)
    {
        Assert.Null(FolderNameCheck.Check(name));
    }

    [Theory]
    [InlineData(@"..\Outside", "\\ / : *")]
    [InlineData("../Outside", "\\ / : *")]
    [InlineData(@"C:\Windows", "\\ / : *")]
    [InlineData(@"\\server\share", "\\ / : *")]
    [InlineData("Docs/2026", "\\ / : *")]
    [InlineData("What?", "\\ / : *")]
    [InlineData("a*b", "\\ / : *")]
    [InlineData("\"quoted\"", "\\ / : *")]
    [InlineData("a<b", "\\ / : *")]
    [InlineData("a|b", "\\ / : *")]
    [InlineData("tab\there", "\\ / : *")]
    [InlineData("..", "only dots")]
    [InlineData(".", "only dots")]
    [InlineData("Notes.", "end with a dot")]
    [InlineData("CON", "keeps the name")]
    [InlineData("nul", "keeps the name")]
    [InlineData("COM1.txt", "keeps the name")]
    [InlineData("LPT9", "keeps the name")]
    [InlineData("", "empty")]
    [InlineData(" Notes", "empty")]
    [InlineData("Notes ", "empty")]
    public void A_name_that_could_escape_or_break_is_refused_with_a_reason(string name, string reasonPart)
    {
        var reason = FolderNameCheck.Check(name);

        Assert.NotNull(reason);
        Assert.Contains(reasonPart, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_refused()
    {
        Assert.Contains("up to 64", FolderNameCheck.Check(new string('a', FolderNameCheck.MaxNameLength + 1)), StringComparison.Ordinal);
        Assert.Null(FolderNameCheck.Check(new string('a', FolderNameCheck.MaxNameLength)));
    }

    [Fact]
    public void Parse_splits_on_commas_and_new_lines_trims_and_ignores_blank_entries()
    {
        var names = FolderNameCheck.Parse(" Tax 2026, Receipts ,\n\nPhotos;Letters, ", out var problem);

        Assert.Null(problem);
        Assert.Equal(["Tax 2026", "Receipts", "Photos", "Letters"], names);
    }

    [Fact]
    public void Parse_refuses_nothing_typed()
    {
        Assert.Empty(FolderNameCheck.Parse("  ,  ", out var problem));
        Assert.Equal("Type at least one folder name.", problem);
        Assert.Empty(FolderNameCheck.Parse(null, out problem));
        Assert.Equal("Type at least one folder name.", problem);
    }

    [Fact]
    public void Parse_refuses_more_than_eight_names()
    {
        var typed = string.Join(", ", Enumerable.Range(1, FolderTemplateCatalog.MaxFolders + 1).Select(i => $"Folder {i}"));

        Assert.Empty(FolderNameCheck.Parse(typed, out var problem));
        Assert.Equal("Up to 8 folders at a time.", problem);
    }

    [Fact]
    public void Parse_refuses_the_same_name_twice_whatever_the_capitals()
    {
        Assert.Empty(FolderNameCheck.Parse("Notes, notes", out var problem));
        Assert.Equal("notes is listed twice.", problem);
    }

    [Fact]
    public void Parse_stops_at_the_first_bad_name_and_names_it()
    {
        Assert.Empty(FolderNameCheck.Parse("Notes, ..\\Up, Photos", out var problem));
        Assert.StartsWith("..\\Up can't be used.", problem, StringComparison.Ordinal);
    }
}
