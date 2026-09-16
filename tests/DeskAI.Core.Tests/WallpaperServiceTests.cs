using System.Reflection;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Desktop;
using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

/// <summary>
/// The wallpaper is DeskAI's first change to a Windows setting, so these tests fix exactly what
/// it may do: only a plain local picture the person picked; the old wallpaper written down
/// before the change so Put back survives a stop; a refusal by Windows leaves nothing behind;
/// and nothing that runs on its own can reach it.
/// </summary>
public sealed class WallpaperServiceTests
{
    private const string Picture = @"C:\deskai-tests\Pictures\holiday.jpg";
    private const string Older = @"C:\deskai-tests\Pictures\older.png";

    [Theory]
    [InlineData(null, "Choose a picture first.")]
    [InlineData("", "Choose a picture first.")]
    [InlineData(@"\\server\share\pic.jpg", "The picture must be a file on this computer.")]
    [InlineData("//server/share/pic.jpg", "The picture must be a file on this computer.")]
    [InlineData("https://example.test/pic.jpg", "The picture must be a file on this computer.")]
    [InlineData(@"Pictures\pic.jpg", "The picture must be a file on this computer.")]
    [InlineData(@"C:\deskai-tests\notes.txt", "DeskAI can use JPG, PNG, and BMP pictures.")]
    [InlineData(@"C:\deskai-tests\setup.exe", "DeskAI can use JPG, PNG, and BMP pictures.")]
    [InlineData(@"C:\deskai-tests\missing.jpg", "That picture is no longer there.")]
    public async Task Preview_refuses_anything_that_is_not_a_plain_local_picture(string? path, string reason)
    {
        var world = new World();
        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);

        var preview = await world.Service.PreviewAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(reason, preview.Problem);
        Assert.False(preview.CanUse);
        Assert.Empty(world.Setter.Sets);
    }

    [Fact]
    public async Task Preview_refuses_a_link_a_folder_an_empty_file_and_a_huge_file()
    {
        var world = new World();
        world.Pictures.Files[@"C:\deskai-tests\link.jpg"] = new PictureFacts(true, true, 1024);
        world.Pictures.Files[@"C:\deskai-tests\folder.jpg"] = new PictureFacts(false, false, 0);
        world.Pictures.Files[@"C:\deskai-tests\empty.jpg"] = new PictureFacts(true, false, 0);
        world.Pictures.Files[@"C:\deskai-tests\huge.jpg"] = new PictureFacts(true, false, WallpaperService.MaxBytes + 1);

        Assert.Equal("That picture is a link or shortcut, so it was left alone.",
            (await world.Service.PreviewAsync(@"C:\deskai-tests\link.jpg", TestContext.Current.CancellationToken)).Problem);
        Assert.Equal("That is not a picture file.",
            (await world.Service.PreviewAsync(@"C:\deskai-tests\folder.jpg", TestContext.Current.CancellationToken)).Problem);
        Assert.Equal("That picture file is empty.",
            (await world.Service.PreviewAsync(@"C:\deskai-tests\empty.jpg", TestContext.Current.CancellationToken)).Problem);
        Assert.Equal("That picture is bigger than 50 MB.",
            (await world.Service.PreviewAsync(@"C:\deskai-tests\huge.jpg", TestContext.Current.CancellationToken)).Problem);
    }

    [Fact]
    public async Task Preview_names_the_picture_and_what_Windows_shows_now_and_changes_nothing()
    {
        var world = new World();
        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Setter.Current = Older;

        var preview = await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken);

        Assert.True(preview.CanUse);
        Assert.Equal("holiday.jpg", preview.Name);
        Assert.Equal("older.png", preview.CurrentDescription);
        Assert.Empty(world.Setter.Sets);
        Assert.Empty(world.Store.Values);

        world.Setter.Current = string.Empty;
        Assert.Equal("a plain colour", (await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken)).CurrentDescription);
        world.Setter.Current = null;
        Assert.Equal("DeskAI could not tell", (await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken)).CurrentDescription);
    }

    [Fact]
    public async Task Use_writes_the_old_wallpaper_down_before_changing_it_and_offers_to_put_it_back()
    {
        var world = new World();
        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Setter.Current = Older;
        var preview = await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken);

        var outcome = await world.Service.UseAsync(preview, TestContext.Current.CancellationToken);

        Assert.True(outcome.Changed);
        Assert.Equal("holiday.jpg is now your wallpaper.", outcome.Summary);
        Assert.Equal([Picture], world.Setter.Sets);
        Assert.Equal(Older, world.Store.Values["wallpaper.previous"]);
        Assert.Equal(Picture, world.Store.Values["wallpaper.set"]);
        Assert.True(world.Store.WriteOrder.IndexOf("wallpaper.previous") < world.Setter.FirstSetAt, "the old wallpaper must be recorded before the change");

        var restore = await world.Service.FindRestoreAsync(TestContext.Current.CancellationToken);
        Assert.Equal("older.png", restore!.PreviousDescription);
        Assert.False(restore.WindowsShowsSomethingElse);
    }

    [Fact]
    public async Task Using_twice_keeps_the_wallpaper_the_person_had_before_DeskAI_touched_it()
    {
        var world = new World();
        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Pictures.Files[Older] = new PictureFacts(true, false, 1024);
        world.Pictures.Files[@"C:\deskai-tests\second.png"] = new PictureFacts(true, false, 1024);
        world.Setter.Current = Older;
        await world.Service.UseAsync(await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        await world.Service.UseAsync(await world.Service.PreviewAsync(@"C:\deskai-tests\second.png", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Equal(Older, world.Store.Values["wallpaper.previous"]);
        var outcome = await world.Service.PutBackAsync(TestContext.Current.CancellationToken);
        Assert.Equal("older.png is your wallpaper again.", outcome.Summary);
        Assert.Equal(Older, world.Setter.Current);
        Assert.Empty(world.Store.Values);
        Assert.Null(await world.Service.FindRestoreAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_wallpaper_the_person_changed_in_Windows_since_is_said_and_becomes_the_one_remembered()
    {
        var world = new World();
        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Pictures.Files[@"C:\deskai-tests\theirs.bmp"] = new PictureFacts(true, false, 1024);
        world.Setter.Current = Older;
        await world.Service.UseAsync(await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        world.Setter.Current = @"C:\deskai-tests\theirs.bmp";
        var restore = await world.Service.FindRestoreAsync(TestContext.Current.CancellationToken);
        Assert.True(restore!.WindowsShowsSomethingElse);
        Assert.Equal("older.png", restore.PreviousDescription);

        await world.Service.UseAsync(await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.Equal(@"C:\deskai-tests\theirs.bmp", world.Store.Values["wallpaper.previous"]);
    }

    [Fact]
    public async Task A_plain_colour_desktop_is_remembered_and_put_back_as_a_plain_colour()
    {
        var world = new World();
        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Setter.Current = string.Empty;
        await world.Service.UseAsync(await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Equal("a plain colour", (await world.Service.FindRestoreAsync(TestContext.Current.CancellationToken))!.PreviousDescription);
        var outcome = await world.Service.PutBackAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Your wallpaper is a plain colour again.", outcome.Summary);
        Assert.Equal(string.Empty, world.Setter.Current);
    }

    [Fact]
    public async Task Use_refuses_when_the_file_went_missing_since_the_preview_or_Windows_will_not_say_what_it_shows()
    {
        var world = new World();
        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Setter.Current = Older;
        var preview = await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken);

        world.Pictures.Files.Remove(Picture);
        var gone = await world.Service.UseAsync(preview, TestContext.Current.CancellationToken);
        Assert.Equal("That picture is no longer there.", gone.Summary);

        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Setter.Current = null;
        var unknown = await world.Service.UseAsync(preview, TestContext.Current.CancellationToken);
        Assert.Equal("DeskAI could not tell what your wallpaper is now, so it did not change it.", unknown.Summary);

        Assert.Empty(world.Setter.Sets);
        Assert.Empty(world.Store.Values);
    }

    [Fact]
    public async Task When_Windows_refuses_nothing_is_recorded_so_Put_back_is_not_offered_for_a_change_that_never_happened()
    {
        var world = new World();
        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Setter.Current = Older;
        world.Setter.RefuseWith = "Windows did not let DeskAI change the wallpaper.";

        var outcome = await world.Service.UseAsync(await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.False(outcome.Changed);
        Assert.Equal("Windows did not let DeskAI change the wallpaper.", outcome.Summary);
        Assert.Empty(world.Store.Values);
        Assert.Null(await world.Service.FindRestoreAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Put_back_says_so_and_changes_nothing_when_the_old_file_is_gone_or_there_is_nothing_to_put_back()
    {
        var world = new World();
        Assert.Equal("There is no old wallpaper to put back.", (await world.Service.PutBackAsync(TestContext.Current.CancellationToken)).Summary);

        world.Pictures.Files[Picture] = new PictureFacts(true, false, 1024);
        world.Setter.Current = Older;
        await world.Service.UseAsync(await world.Service.PreviewAsync(Picture, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var outcome = await world.Service.PutBackAsync(TestContext.Current.CancellationToken);

        Assert.False(outcome.Changed);
        Assert.Equal("Your old wallpaper file is no longer there, so nothing was changed.", outcome.Summary);
        Assert.Equal(Picture, world.Setter.Current);
        Assert.NotNull(await world.Service.FindRestoreAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_holds_only_the_setter_the_inspector_and_the_store()
    {
        var forbidden = new[]
        {
            typeof(IFolderTidyExecutor),
            typeof(IOperationJournal),
            typeof(IOrganizationPlanner),
            typeof(IFileScanner),
            typeof(IContentTextExtractor),
            typeof(ICredentialVault),
            typeof(IOrganizationSuggestionProvider),
            typeof(IFileIndex),
            typeof(IReadOnlyFolderService),
            typeof(RuleSetEvaluator),
        };

        var dependencies = typeof(WallpaperService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal([typeof(IWallpaperSetter), typeof(IPictureInspector), typeof(IAppSettingsStore)], dependencies);
        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));
    }

    private sealed class World
    {
        public World() => Service = new WallpaperService(Setter, Pictures, Store);

        public FakeSetter Setter { get; } = new();

        public FakePictures Pictures { get; } = new();

        public FakeStore Store { get; } = new();

        public WallpaperService Service { get; }
    }

    private sealed class FakeSetter : IWallpaperSetter
    {
        private static int _tick;

        public string? Current { get; set; } = string.Empty;

        public string? RefuseWith { get; set; }

        public List<string> Sets { get; } = [];

        /// <summary>A global order stamp, so a test can prove a write came before the first Set.</summary>
        public int FirstSetAt { get; private set; } = int.MaxValue;

        public static int NextTick() => Interlocked.Increment(ref _tick);

        public string? ReadCurrent() => Current;

        public void Set(string imagePath)
        {
            if (RefuseWith is { } reason)
            {
                throw new InvalidOperationException(reason);
            }

            FirstSetAt = Math.Min(FirstSetAt, NextTick());
            Sets.Add(imagePath);
            Current = imagePath;
        }
    }

    private sealed class FakePictures : IPictureInspector
    {
        public Dictionary<string, PictureFacts> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public PictureFacts? Inspect(string path) => Files.GetValueOrDefault(path);
    }

    private sealed class FakeStore : IAppSettingsStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        /// <summary>Keys in the order they were written, stamped against the setter's clock.</summary>
        public List<string> WriteOrder { get; } = [];

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.GetValueOrDefault(key));

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            WriteOrder.Add(key);
            FakeSetter.NextTick();
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
