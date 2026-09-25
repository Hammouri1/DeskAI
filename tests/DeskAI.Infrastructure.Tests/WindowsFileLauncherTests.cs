using DeskAI.Core.QuickSearch;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Launching;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Tests;

/// <summary>
/// The launcher re-checks the live file and starts exactly one validated path, or nothing.
/// No real process is started: the shell starter records.
/// </summary>
public sealed class WindowsFileLauncherTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new();
    private readonly RecordingStarter _shell = new();
    private readonly InMemoryAuthorizedRootRepository _roots = new();
    private readonly AuthorizedRoot _root;
    private readonly string _folder;

    public WindowsFileLauncherTests()
    {
        _folder = _temp.CreateDummyDirectory("School");
        _root = AuthorizedRoot.Create(Guid.NewGuid(), _folder, "School", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);
        _roots.SaveAsync(_root).GetAwaiter().GetResult();
    }

    private WindowsFileLauncher Launcher(params string[] protectedPaths) =>
        new(_roots, new WindowsPathPolicy(protectedPaths), _shell);

    [Fact]
    public async Task A_familiar_file_is_opened_by_its_full_path()
    {
        var path = _temp.CreateDummyFile(Path.Combine("School", "essay.pdf"), "x");

        var result = await Launcher().OpenAsync(_root.Id, "essay.pdf", TestContext.Current.CancellationToken);

        Assert.True(result.Done);
        Assert.Equal([path], _shell.Opened);
        Assert.Empty(_shell.Shown);
    }

    [Fact]
    public async Task Show_in_folder_shows_any_kind_of_file()
    {
        var path = _temp.CreateDummyFile(Path.Combine("School", "setup.exe"), "x");

        Assert.True((await Launcher().ShowInFolderAsync(_root.Id, "setup.exe", TestContext.Current.CancellationToken)).Done);
        Assert.Equal([path], _shell.Shown);
        Assert.Empty(_shell.Opened);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("invoice.pdf.exe")]
    [InlineData("notes.lnk")]
    public async Task A_program_is_never_opened(string name)
    {
        _temp.CreateDummyFile(Path.Combine("School", name), "x");

        var result = await Launcher().OpenAsync(_root.Id, name, TestContext.Current.CancellationToken);

        Assert.False(result.Done);
        Assert.Equal("DeskAI only opens familiar kinds of files. Use Show in folder.", result.Reason);
        Assert.Empty(_shell.Opened);
    }

    /// <summary>
    /// The index remembered "report.pdf"; it became "report.pdf.exe" since. The live name decides.
    /// </summary>
    [Fact]
    public async Task A_file_renamed_into_a_program_after_it_was_found_is_not_opened()
    {
        var remembered = _temp.CreateDummyFile(Path.Combine("School", "report.pdf"), "x");
        File.Move(remembered, remembered + ".exe");

        var byOldName = await Launcher().OpenAsync(_root.Id, "report.pdf", TestContext.Current.CancellationToken);
        var byNewName = await Launcher().OpenAsync(_root.Id, "report.pdf.exe", TestContext.Current.CancellationToken);

        Assert.False(byOldName.Done);
        Assert.False(byNewName.Done);
        Assert.Empty(_shell.Opened);
    }

    [Theory]
    [InlineData(@"..\outside.pdf")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("essay.pdf:hidden")]
    [InlineData("")]
    public async Task A_path_leading_out_of_the_folder_is_refused(string relative)
    {
        _temp.CreateDummyFile("outside.pdf", "x");
        _temp.CreateDummyFile(Path.Combine("School", "essay.pdf"), "x");

        var result = await Launcher().OpenAsync(_root.Id, relative, TestContext.Current.CancellationToken);

        Assert.False(result.Done);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task A_file_that_is_gone_is_refused_with_a_plain_reason()
    {
        var result = await Launcher().OpenAsync(_root.Id, "gone.pdf", TestContext.Current.CancellationToken);

        Assert.False(result.Done);
        Assert.Equal("It is no longer there. Press Refresh on Search so DeskAI catches up.", result.Reason);
    }

    [Fact]
    public async Task A_folder_that_was_disconnected_is_refused()
    {
        _temp.CreateDummyFile(Path.Combine("School", "essay.pdf"), "x");
        await _roots.RemoveAsync(_root.Id, TestContext.Current.CancellationToken);

        var result = await Launcher().OpenAsync(_root.Id, "essay.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("That folder is no longer connected in DeskAI.", result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task A_protected_place_is_refused()
    {
        var inside = _temp.CreateDummyDirectory(Path.Combine("School", "Private"));
        _temp.CreateDummyFile(Path.Combine("School", "Private", "essay.pdf"), "x");

        var result = await Launcher(inside).OpenAsync(_root.Id, @"Private\essay.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("This location is protected, so DeskAI did not open it.", result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task A_link_on_the_way_is_never_followed()
    {
        var elsewhere = _temp.CreateDummyDirectory("Elsewhere");
        _temp.CreateDummyFile(Path.Combine("Elsewhere", "essay.pdf"), "x");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_folder, "Linked"), elsewhere);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        var result = await Launcher().OpenAsync(_root.Id, @"Linked\essay.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("That name is a shortcut to somewhere else, so DeskAI did not follow it.", result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task When_Windows_refuses_it_says_so_plainly()
    {
        _temp.CreateDummyFile(Path.Combine("School", "essay.pdf"), "x");
        _shell.Fail = true;

        var result = await Launcher().OpenAsync(_root.Id, "essay.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("Windows couldn't open it.", result.Reason);
    }

    [Fact]
    public async Task The_default_starter_starts_nothing()
    {
        _temp.CreateDummyFile(Path.Combine("School", "essay.pdf"), "x");
        var launcher = new WindowsFileLauncher(_roots, new WindowsPathPolicy(), new NoShellStarter());

        var result = await launcher.OpenAsync(_root.Id, "essay.pdf", TestContext.Current.CancellationToken);

        Assert.False(result.Done);
    }

    public void Dispose() => _temp.Dispose();

    private sealed class RecordingStarter : IShellStarter
    {
        public List<string> Opened { get; } = [];
        public List<string> Shown { get; } = [];
        public bool Fail { get; set; }

        public void OpenWithUsualApp(string fullPath)
        {
            if (Fail) throw new System.ComponentModel.Win32Exception();
            Opened.Add(fullPath);
        }

        public void ShowInFolder(string fullPath)
        {
            if (Fail) throw new System.ComponentModel.Win32Exception();
            Shown.Add(fullPath);
        }
    }
}
