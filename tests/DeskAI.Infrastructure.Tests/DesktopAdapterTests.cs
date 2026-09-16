using DeskAI.Core.Abstractions;
using DeskAI.Infrastructure.Desktop;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

/// <summary>
/// The pieces behind the wallpaper and Desktop features that touch the disk or the database,
/// on generated data only. The real wallpaper setter is deliberately not exercised here: no
/// test may change the developer's wallpaper.
/// </summary>
public sealed class DesktopAdapterTests
{
    [Fact]
    public void The_picture_inspector_reports_a_file_a_folder_a_link_and_nothing_without_opening_anything()
    {
        using var sandbox = new TemporaryDirectory();
        var picture = sandbox.CreateDummyFile(@"Pictures\holiday.jpg", "not really a picture, and never opened");
        var folder = sandbox.CreateDummyDirectory(@"Pictures\album.jpg");
        var inspector = new FilePictureInspector();

        Assert.Equal(new PictureFacts(true, false, new FileInfo(picture).Length), inspector.Inspect(picture));
        Assert.Equal(new PictureFacts(false, false, 0), inspector.Inspect(folder));
        Assert.Null(inspector.Inspect(Path.Combine(sandbox.Path, "Pictures", "missing.jpg")));

        var link = Path.Combine(sandbox.Path, "Pictures", "link.jpg");
        try
        {
            File.CreateSymbolicLink(link, picture);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        Assert.True(inspector.Inspect(link)!.IsLink);
    }

    [Fact]
    public void Known_folders_asks_Windows_rather_than_building_a_path_from_a_user_name()
    {
        var desktop = new WindowsKnownFolders().Desktop;

        // Whatever the account's Desktop is, it comes from the known-folder API. This test does
        // not look inside it and does not depend on where it is.
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), desktop ?? string.Empty);
    }

    [Fact]
    public async Task The_settings_store_reads_writes_overwrites_and_removes_one_key_at_a_time()
    {
        using var sandbox = new TemporaryDirectory();
        var options = Options.Create(new DatabaseOptions { DatabasePath = Path.Combine(sandbox.Path, "deskai.db") });
        await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(TestContext.Current.CancellationToken);
        var store = new SqliteAppSettingsStore(options, new SystemClock());

        Assert.Null(await store.ReadAsync("wallpaper.previous", TestContext.Current.CancellationToken));
        await store.WriteAsync("wallpaper.previous", @"C:\old.jpg", TestContext.Current.CancellationToken);
        await store.WriteAsync("wallpaper.set", @"C:\new.jpg", TestContext.Current.CancellationToken);
        Assert.Equal(@"C:\old.jpg", await store.ReadAsync("wallpaper.previous", TestContext.Current.CancellationToken));

        await store.WriteAsync("wallpaper.previous", string.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, await store.ReadAsync("wallpaper.previous", TestContext.Current.CancellationToken));

        await store.RemoveAsync("wallpaper.previous", TestContext.Current.CancellationToken);
        Assert.Null(await store.ReadAsync("wallpaper.previous", TestContext.Current.CancellationToken));
        Assert.Equal(@"C:\new.jpg", await store.ReadAsync("wallpaper.set", TestContext.Current.CancellationToken));
        SqliteConnection.ClearAllPools();
    }
}
