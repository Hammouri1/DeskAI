using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Scanning;

namespace DeskAI.Infrastructure.Tests;

/// <summary>
/// The lookup folder templates use to say "already there": names and kinds, one level, in a
/// generated folder only.
/// </summary>
public sealed class FolderNameLookupTests
{
    [Fact]
    public async Task Reports_a_folder_a_file_a_link_and_nothing_by_the_name_on_disk()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\slides");
        sandbox.CreateDummyFile(@"Folder\Notes");
        sandbox.CreateDummyDirectory("Elsewhere");
        var link = Path.Combine(folder, "Screenshots");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(sandbox.Path, "Elsewhere"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"This Windows environment cannot create a test symbolic link: {exception.GetType().Name}");
        }

        var lookup = new FolderNameLookup();

        var found = await lookup.LookAsync(Allowed(folder), ["Slides", "Notes", "Screenshots", "Assignments"], TestContext.Current.CancellationToken);

        Assert.Equal(
            [("slides", FolderEntryKind.Folder), ("Notes", FolderEntryKind.File), ("Screenshots", FolderEntryKind.Link), ("Assignments", FolderEntryKind.Missing)],
            found.Select(item => (item.Name, item.Kind)));
    }

    [Fact]
    public async Task Looks_only_at_the_top_level_and_never_inside_subfolders()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Deep\Notes");
        var lookup = new FolderNameLookup();

        var found = await lookup.LookAsync(Allowed(folder), ["Notes"], TestContext.Current.CancellationToken);

        Assert.Equal(FolderEntryKind.Missing, Assert.Single(found).Kind);
    }

    [Fact]
    public async Task Refuses_a_folder_DeskAI_may_not_read_a_path_as_a_name_and_too_many_names()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        var lookup = new FolderNameLookup();
        var restricted = AuthorizedRoot.Create(Guid.NewGuid(), folder, "Folder", RootAccessLevel.Restricted, RootAuthorizationScope.MetadataOnly);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lookup.LookAsync(restricted, ["Notes"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => lookup.LookAsync(Allowed(folder), [@"..\Notes"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => lookup.LookAsync(
            Allowed(folder), Enumerable.Range(1, 9).Select(i => $"F{i}").ToArray(), TestContext.Current.CancellationToken));
    }

    private static AuthorizedRoot Allowed(string folder) =>
        AuthorizedRoot.Create(Guid.NewGuid(), folder, "Folder", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);
}
