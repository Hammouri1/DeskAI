using DeskAI.FolderColorProbe;

namespace DeskAI.FolderColorProbe.Tests;

public sealed class DesktopIniTests
{
    [Fact]
    public void Reads_a_key_from_its_section() =>
        Assert.Equal(
            @"C:\x\magenta.ico,0",
            DesktopIni.Value("[.ShellClassInfo]\r\nInfoTip=Hi\r\nIconResource=C:\\x\\magenta.ico,0\r\n", ".ShellClassInfo", "IconResource"));

    [Fact]
    public void Section_and_key_names_ignore_case() =>
        Assert.Equal("a", DesktopIni.Value("[.shellclassinfo]\niconresource = a\n", ".ShellClassInfo", "IconResource"));

    [Fact]
    public void Ignores_the_same_key_in_another_section() =>
        Assert.Null(DesktopIni.Value("[ViewState]\nIconResource=a\n[.ShellClassInfo]\nInfoTip=b\n", ".ShellClassInfo", "IconResource"));

    [Fact]
    public void Null_when_absent() =>
        Assert.Null(DesktopIni.Value("", ".ShellClassInfo", "IconResource"));

    [Fact]
    public void Lists_the_lines_other_than_the_icon_lines() =>
        Assert.Equal(
            ["[.ShellClassInfo]", "InfoTip=b", "[ViewState]", "Mode="],
            DesktopIni.LinesWithoutIcon("[.ShellClassInfo]\r\nIconResource=a\r\nIconFile=c\r\nIconIndex=0\r\nInfoTip=b\r\n\r\n[ViewState]\r\nMode=\r\n"));
}
