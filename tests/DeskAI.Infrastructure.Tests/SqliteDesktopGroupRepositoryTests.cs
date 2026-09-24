using DeskAI.Core.Roots;
using DeskAI.Core.Studio;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteDesktopGroupRepositoryTests
{
    private static readonly DateTimeOffset MadeAt = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Save_then_load_keeps_groups_their_order_and_not_sure()
    {
        await using var world = await World.CreateAsync();
        var board = new DesktopGroupBoard(
            world.RootId,
            [new DesktopGroup("School", ["Essays", "report.docx"]), new DesktopGroup("Coding", ["Python stuff"])],
            ["holiday.jpg"],
            DesktopGroupSource.Ai,
            MadeAt)
        { Folders = new HashSet<string> { "Essays", "Python stuff" } };

        await world.Repository.SaveAsync(board, TestContext.Current.CancellationToken);
        var loaded = await world.Repository.LoadAsync(world.RootId, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(["School", "Coding"], loaded.Groups.Select(g => g.Name));
        Assert.Equal(["Essays", "report.docx"], loaded.Groups[0].Items);
        Assert.Equal(["holiday.jpg"], loaded.NotSure);
        Assert.Equal(DesktopGroupSource.Ai, loaded.Source);
        Assert.Equal(MadeAt, loaded.MadeAtUtc);
        Assert.True(loaded.Folders.SetEquals(["essays", "Python stuff"]));
    }

    [Fact]
    public async Task Saving_again_replaces_the_board()
    {
        await using var world = await World.CreateAsync();
        await world.Repository.SaveAsync(new DesktopGroupBoard(world.RootId, [new DesktopGroup("Old", ["a.txt"])], [], DesktopGroupSource.Ai, MadeAt), TestContext.Current.CancellationToken);

        await world.Repository.SaveAsync(new DesktopGroupBoard(world.RootId, [], ["a.txt"], DesktopGroupSource.LocalGuess, MadeAt), TestContext.Current.CancellationToken);

        var loaded = await world.Repository.LoadAsync(world.RootId, TestContext.Current.CancellationToken);
        Assert.Empty(loaded!.Groups);
        Assert.Equal(DesktopGroupSource.LocalGuess, loaded.Source);
    }

    [Fact]
    public async Task Removing_the_folder_erases_its_board()
    {
        await using var world = await World.CreateAsync();
        await world.Repository.SaveAsync(new DesktopGroupBoard(world.RootId, [new DesktopGroup("Coding", ["x"])], [], DesktopGroupSource.Ai, MadeAt), TestContext.Current.CancellationToken);

        await world.Roots.RemoveAsync(world.RootId, TestContext.Current.CancellationToken);

        Assert.Null(await world.Repository.LoadAsync(world.RootId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_damaged_row_loads_as_nothing()
    {
        await using var world = await World.CreateAsync();
        await world.Repository.SaveAsync(new DesktopGroupBoard(world.RootId, [], [], DesktopGroupSource.Ai, MadeAt), TestContext.Current.CancellationToken);
        await world.ExecuteAsync("UPDATE desktop_group_boards SET board_json = '{\"groups\":7}';");

        Assert.Null(await world.Repository.LoadAsync(world.RootId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_board_for_a_folder_that_is_not_connected_is_not_saved()
    {
        await using var world = await World.CreateAsync();
        var stranger = Guid.NewGuid();

        await world.Repository.SaveAsync(new DesktopGroupBoard(stranger, [], [], DesktopGroupSource.Ai, MadeAt), TestContext.Current.CancellationToken);

        Assert.Null(await world.Repository.LoadAsync(stranger, TestContext.Current.CancellationToken));
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly TemporaryDirectory _sandbox = new();

        private World() => DatabasePath = Path.Combine(_sandbox.Path, "deskai.db");

        public string DatabasePath { get; }

        public Guid RootId { get; } = Guid.NewGuid();

        public SqliteDesktopGroupRepository Repository { get; private set; } = null!;

        public SqliteAuthorizedRootRepository Roots { get; private set; } = null!;

        public static async Task<World> CreateAsync()
        {
            var world = new World();
            var options = Options.Create(new DatabaseOptions { DatabasePath = world.DatabasePath });
            await new SqliteDatabaseInitializer(options, new SystemClock(), NullLogger<SqliteDatabaseInitializer>.Instance)
                .InitializeAsync(TestContext.Current.CancellationToken);
            world.Roots = new SqliteAuthorizedRootRepository(options, new SystemClock());
            await world.Roots.SaveAsync(
                AuthorizedRoot.Create(world.RootId, world._sandbox.CreateDummyDirectory("Desktop"), "Desktop", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly),
                TestContext.Current.CancellationToken);
            world.Repository = new SqliteDesktopGroupRepository(options);
            return world;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            _sandbox.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
