using DeskAI.FolderColorProbe;

namespace DeskAI.FolderColorProbe.Tests;

public sealed class FolderStateTests
{
    private static readonly FolderState Plain = new(FileAttributes.Directory, null, null);

    private static readonly FolderState Custom = new(
        FileAttributes.Directory | FileAttributes.ReadOnly,
        [1, 2, 3],
        FileAttributes.Hidden | FileAttributes.System);

    [Fact]
    public void Equal_states_have_no_differences()
    {
        Assert.Empty(FolderState.Differences(Plain, Plain with { }));
        Assert.Empty(FolderState.Differences(Custom, Custom with { IniBytes = [1, 2, 3] }));
    }

    [Fact]
    public void Names_a_changed_folder_attribute() =>
        Assert.Contains(
            FolderState.Differences(Plain, Plain with { Attributes = FileAttributes.Directory | FileAttributes.ReadOnly }),
            d => d.Contains("folder attributes", StringComparison.Ordinal));

    [Fact]
    public void Names_changed_desktop_ini_bytes() =>
        Assert.Contains(
            FolderState.Differences(Custom, Custom with { IniBytes = [1, 2, 4] }),
            d => d.Contains("desktop.ini contents", StringComparison.Ordinal));

    [Fact]
    public void Names_changed_desktop_ini_attributes() =>
        Assert.Contains(
            FolderState.Differences(Custom, Custom with { IniAttributes = FileAttributes.Hidden }),
            d => d.Contains("desktop.ini attributes", StringComparison.Ordinal));

    [Fact]
    public void Names_an_added_desktop_ini() =>
        Assert.Contains(
            FolderState.Differences(Plain, Plain with { IniBytes = [1], IniAttributes = FileAttributes.Hidden }),
            d => d.Contains("desktop.ini was added", StringComparison.Ordinal));

    [Fact]
    public void Names_a_removed_desktop_ini() =>
        Assert.Contains(
            FolderState.Differences(Custom, Custom with { IniBytes = null, IniAttributes = null }),
            d => d.Contains("desktop.ini was removed", StringComparison.Ordinal));

    [Fact]
    public void Restores_a_generated_folder_that_had_no_desktop_ini()
    {
        var folder = Directory.CreateTempSubdirectory("deskai-color-probe-test-").FullName;
        try
        {
            var before = FolderState.Read(folder);
            var ini = Path.Combine(folder, "desktop.ini");
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=x.ico,0\r\n");
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            Assert.NotEmpty(FolderState.Differences(before, FolderState.Read(folder)));

            FolderState.Restore(folder, before);

            Assert.Empty(FolderState.Differences(before, FolderState.Read(folder)));
        }
        finally
        {
            Remove(folder);
        }
    }

    [Fact]
    public void Keeps_a_folder_read_only_that_was_read_only_before()
    {
        var folder = Directory.CreateTempSubdirectory("deskai-color-probe-test-").FullName;
        try
        {
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            var before = FolderState.Read(folder);
            var ini = Path.Combine(folder, "desktop.ini");
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=x.ico,0\r\n");
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);

            FolderState.Restore(folder, before);

            Assert.Empty(FolderState.Differences(before, FolderState.Read(folder)));
            Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
            Assert.False(File.Exists(ini));
        }
        finally
        {
            Remove(folder);
        }
    }

    [Fact]
    public void Restores_an_earlier_desktop_ini_exactly()
    {
        var folder = Directory.CreateTempSubdirectory("deskai-color-probe-test-").FullName;
        try
        {
            var ini = Path.Combine(folder, "desktop.ini");
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nInfoTip=Kept\r\n");
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            var before = FolderState.Read(folder);
            File.SetAttributes(ini, FileAttributes.Normal);
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nInfoTip=Kept\r\nIconResource=x.ico,0\r\n");

            FolderState.Restore(folder, before);

            Assert.Empty(FolderState.Differences(before, FolderState.Read(folder)));
        }
        finally
        {
            Remove(folder);
        }
    }

    private static void Remove(string folder)
    {
        // Only the generated temporary folder made by this test.
        File.SetAttributes(folder, FileAttributes.Directory);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(folder, recursive: true);
    }
}
