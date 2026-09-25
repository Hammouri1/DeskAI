using DeskAI.Core.QuickSearch;

namespace DeskAI.Core.Tests;

/// <summary>
/// Which files quick search may open in their usual app. Everything not on the known-safe list
/// is only shown in its folder, and a second extension never disguises a program.
/// </summary>
public sealed class FileOpenRuleTests
{
    [Theory]
    [InlineData("essay.pdf")]
    [InlineData("Holiday.JPG")]
    [InlineData("notes.md")]
    [InlineData("budget.xlsx")]
    [InlineData("song.flac")]
    [InlineData("backup.zip")]
    public void Familiar_files_open(string name) => Assert.Equal(OpenChoice.Open, FileOpenRule.For(name));

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("invoice.pdf.exe")]
    [InlineData("run.bat")]
    [InlineData("script.ps1")]
    [InlineData("game.lnk")]
    [InlineData("page.html")]
    [InlineData("macro.docm")]
    [InlineData("old.doc")]
    [InlineData("README")]
    [InlineData("report.pdf.")]
    [InlineData("report.pdf ")]
    [InlineData(".pdf")]
    [InlineData("")]
    public void Everything_else_is_only_shown_in_its_folder(string name) =>
        Assert.Equal(OpenChoice.ShowInFolderOnly, FileOpenRule.For(name));

    [Fact]
    public void The_list_is_exactly_the_agreed_one()
    {
        Assert.Equal(
            [".avi", ".bmp", ".csv", ".docx", ".flac", ".gif", ".heic", ".jpeg", ".jpg", ".m4a", ".md", ".mkv", ".mov", ".mp3", ".mp4",
             ".odp", ".ods", ".odt", ".pdf", ".png", ".pptx", ".rtf", ".txt", ".wav", ".webp", ".xlsx", ".zip"],
            FileOpenRule.OpenableExtensions.Order(StringComparer.Ordinal));
    }
}
