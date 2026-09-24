using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Roots;
using DeskAI.Core.Studio;
using static DeskAI.Core.Ai.DisclosureCategory;

namespace DeskAI.Core.Tests;

public sealed class DesktopGroupingServiceTests
{
    private static readonly Guid DesktopId = StudioFakes.DesktopId;

    [Fact]
    public async Task Prepare_lists_exactly_what_would_be_sent_and_sends_nothing()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Scanner.Events = [.. Folder("Python stuff", "main.py"), StudioFakes.File("report.docx")];

        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);

        Assert.Equal(["Folder \"Python stuff\": 1 .py; main.py", "File \"report.docx\""], prepared.Question!.Lines);
        Assert.Equal(["Python stuff", "report.docx"], prepared.Question.PathsByNumber);
        Assert.Equal([1, 2], prepared.Question.Request.Items.Select(i => i.Number));
        Assert.Contains("OpenRouter", prepared.Explanation, StringComparison.Ordinal);
        Assert.Equal(0, world.Ai.Calls);
    }

    [Fact]
    public async Task Online_AI_without_folder_name_sharing_offers_no_send()
    {
        var world = new World(cloudSharing: [Extension]);
        world.Scanner.Events = [StudioFakes.File("report.docx")];

        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);

        Assert.Null(prepared.Question);
        Assert.Contains("folder names", prepared.Explanation, StringComparison.Ordinal);
        Assert.Equal(0, world.Ai.Calls);
    }

    [Fact]
    public async Task Without_AI_prepare_says_to_turn_it_on_or_use_the_guess()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Settings.Current = AiSettings.Default;
        world.Scanner.Events = [StudioFakes.File("report.docx")];

        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);

        Assert.Null(prepared.Question);
        Assert.Contains("Use DeskAI's guess", prepared.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_Desktop_has_nothing_to_sort()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Scanner.Events = [];

        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);
        var guessed = await world.Service.GuessAsync(DesktopId, TestContext.Current.CancellationToken);

        Assert.Null(prepared.Question);
        Assert.Equal("There is nothing on your Desktop to sort.", prepared.Explanation);
        Assert.False(guessed.Succeeded);
        Assert.Equal("There is nothing on your Desktop to sort.", guessed.Message);
    }

    [Fact]
    public async Task A_folder_that_is_not_the_connected_Desktop_is_refused()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Scanner.Events = [StudioFakes.File("report.docx")];

        var prepared = await world.Service.PrepareAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        var guessed = await world.Service.GuessAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(prepared.Question);
        Assert.False(guessed.Succeeded);
    }

    [Fact]
    public async Task Send_refuses_when_the_AI_choice_changed_after_prepare()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Scanner.Events = [.. Folder("Python stuff", "main.py")];
        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);
        world.Settings.Current = world.Settings.Current with { ProviderId = "openai", CredentialReference = "DeskAI/OpenAI" };

        var result = await world.Service.SendAsync(prepared.Question!, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(0, world.Ai.Calls);
    }

    [Fact]
    public async Task Send_refuses_when_sharing_was_narrowed_after_prepare()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Scanner.Events = [.. Folder("Python stuff", "main.py")];
        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);
        world.Settings.Current = world.Settings.Current with { CloudDisclosures = new HashSet<DisclosureCategory> { Extension } };

        var result = await world.Service.SendAsync(prepared.Question!, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(0, world.Ai.Calls);
    }

    [Fact]
    public async Task A_valid_answer_becomes_a_saved_board_with_unmentioned_items_not_sure()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Scanner.Events = [.. Folder("Python stuff", "main.py"), StudioFakes.File("report.docx")];
        world.Ai.Json = """{"schemaVersion":"1","groups":[{"name":"Coding","items":[1]}]}""";
        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);

        var result = await world.Service.SendAsync(prepared.Question!, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(prepared.Question!.Request, world.Ai.LastRequest);
        var board = world.Boards.Saved!;
        Assert.Equal("Coding", Assert.Single(board.Groups).Name);
        Assert.Equal(["Python stuff"], board.Groups[0].Items);
        Assert.Equal(["report.docx"], board.NotSure);
        Assert.Equal(DesktopGroupSource.Ai, board.Source);
        Assert.Equal(["Python stuff"], board.Folders);
    }

    [Fact]
    public async Task Items_beyond_the_bounds_are_sorted_by_DeskAIs_guess_and_never_sent()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Scanner.Events = Enumerable.Range(0, DesktopLookService.MaxFiles + 2)
            .Select(i => (ScanEvent)StudioFakes.File($"song{i:D3}.mp3")).ToArray();
        world.Ai.Json = """{"schemaVersion":"1","groups":[{"name":"Music","items":[1]}]}""";
        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);

        var result = await world.Service.SendAsync(prepared.Question!, TestContext.Current.CancellationToken);

        Assert.Equal(DesktopLookService.MaxFiles, prepared.Question!.Request.Items.Count);
        Assert.Equal(2, prepared.Question.LeftOut);
        Assert.DoesNotContain(prepared.Question.Request.Items, i => i.Name == "song201.mp3");
        Assert.Contains("2 more were sorted by DeskAI's own guess.", result.Message, StringComparison.Ordinal);
        Assert.Contains("song201.mp3", world.Boards.Saved!.Groups.Single(g => g.Name == "Music").Items);
    }

    [Fact]
    public async Task An_answer_outside_the_shape_leaves_the_old_board_alone()
    {
        var world = new World(cloudSharing: [Extension, FileName, FolderNames]);
        world.Scanner.Events = [.. Folder("Python stuff", "main.py")];
        var old = new DesktopGroupBoard(DesktopId, [new DesktopGroup("Mine", ["Python stuff"])], [], DesktopGroupSource.LocalGuess, DateTimeOffset.UnixEpoch);
        world.Boards.Saved = old;
        world.Ai.Json = "{}";
        var prepared = await world.Service.PrepareAsync(DesktopId, TestContext.Current.CancellationToken);

        var result = await world.Service.SendAsync(prepared.Question!, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("ignored", result.Message, StringComparison.Ordinal);
        Assert.Same(old, world.Boards.Saved);
    }

    [Fact]
    public async Task Guess_sorts_by_kind_of_file_and_says_it_is_simpler()
    {
        var world = new World(cloudSharing: []);
        world.Scanner.Events = [.. Folder("Python stuff", "main.py"), StudioFakes.File("holiday.jpg"), StudioFakes.File("mystery")];

        var result = await world.Service.GuessAsync(DesktopId, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("Sorted by DeskAI's own simpler guess from the kinds of files. You can change any group.", result.Message);
        Assert.Equal(["Coding", "Pictures"], result.Board!.Groups.Select(g => g.Name));
        Assert.Equal(["mystery"], result.Board.NotSure);
        Assert.Equal(DesktopGroupSource.LocalGuess, result.Board.Source);
        Assert.Equal(["Python stuff"], result.Board.Folders);
        Assert.Equal(0, world.Ai.Calls);
    }

    [Fact]
    public async Task Rename_merge_and_move_check_names_and_the_eight_group_limit()
    {
        var world = new World(cloudSharing: []);
        world.Boards.Saved = new DesktopGroupBoard(
            DesktopId,
            [new DesktopGroup("Coding", ["Python stuff"]), new DesktopGroup("Pictures", ["holiday.jpg"])],
            ["report.docx"],
            DesktopGroupSource.LocalGuess,
            DateTimeOffset.UnixEpoch);
        var ct = TestContext.Current.CancellationToken;

        Assert.False((await world.Service.RenameAsync(DesktopId, "Coding", "a/b", ct)).Succeeded);
        Assert.False((await world.Service.RenameAsync(DesktopId, "Coding", "pictures", ct)).Succeeded);
        Assert.False((await world.Service.RenameAsync(DesktopId, "Coding", "Not sure", ct)).Succeeded);
        Assert.Equal("Coding", world.Boards.Saved.Groups[0].Name);

        var renamed = await world.Service.RenameAsync(DesktopId, "Coding", "Programming", ct);
        Assert.True(renamed.Succeeded);
        Assert.Equal(["Programming", "Pictures"], renamed.Board!.Groups.Select(g => g.Name));

        var moved = await world.Service.MoveAsync(DesktopId, "report.docx", "Programming", ct);
        Assert.Equal(["Python stuff", "report.docx"], moved.Board!.Groups[0].Items);
        Assert.Empty(moved.Board.NotSure);

        var merged = await world.Service.MergeAsync(DesktopId, "Pictures", "Programming", ct);
        Assert.Equal(["Python stuff", "report.docx", "holiday.jpg"], Assert.Single(merged.Board!.Groups).Items);

        var unsure = await world.Service.MoveAsync(DesktopId, "holiday.jpg", null, ct);
        Assert.Equal(["holiday.jpg"], unsure.Board!.NotSure);

        Assert.False((await world.Service.MoveAsync(DesktopId, "not-on-board.txt", null, ct)).Succeeded);
        Assert.False((await world.Service.MoveAsync(DesktopId, "report.docx", "Nowhere", ct)).Succeeded);
        Assert.False((await world.Service.MergeAsync(DesktopId, "Programming", "Programming", ct)).Succeeded);
    }

    [Fact]
    public async Task Loading_drops_items_that_are_no_longer_on_the_Desktop()
    {
        var world = new World(cloudSharing: []);
        world.Boards.Saved = new DesktopGroupBoard(
            DesktopId, [new DesktopGroup("Coding", ["Python stuff", "Gone"])], ["gone.txt"], DesktopGroupSource.Ai, DateTimeOffset.UnixEpoch);
        world.Scanner.Events = [.. Folder("Python stuff", "main.py"), StudioFakes.File("new.txt")];

        var loaded = await world.Service.LoadBoardAsync(DesktopId, TestContext.Current.CancellationToken);

        Assert.Equal(["Python stuff"], loaded.Board!.Groups[0].Items);
        Assert.Equal(["new.txt"], loaded.Board.NotSure);
        Assert.Contains("2 things are no longer on your Desktop", loaded.Message, StringComparison.Ordinal);
        Assert.Same(loaded.Board, world.Boards.Saved);
    }

    [Fact]
    public void Holds_no_file_changing_dependency()
    {
        var parameters = typeof(DesktopGroupingService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name);

        Assert.DoesNotContain(parameters, n =>
            n.Contains("Executor", StringComparison.Ordinal) || n.Contains("Journal", StringComparison.Ordinal) ||
            n.Contains("Wallpaper", StringComparison.Ordinal) || n.Contains("Credential", StringComparison.Ordinal) ||
            n.Contains("Writer", StringComparison.Ordinal) || n.Contains("Content", StringComparison.Ordinal));
    }

    private static ScanEvent[] Folder(string name, params string[] files) =>
        [new FolderDiscovered(name, FileTraits.None), .. files.Select(f => (ScanEvent)StudioFakes.File(Path.Combine(name, f)))];

    private sealed class World
    {
        public World(DisclosureCategory[] cloudSharing)
        {
            Settings = new FixedAiSettings(AiSettings.Default with
            {
                Mode = AiMode.Cloud,
                ProviderId = "openrouter",
                ModelId = "test-model",
                CredentialReference = "DeskAI/OpenRouter",
                CloudConsentGranted = true,
                CloudDisclosures = cloudSharing.ToHashSet(),
            });
            var roots = new FixedRoots(StudioFakes.Root());
            Service = new DesktopGroupingService(
                new PersonalFolderPolicy(new DesktopOnlyKnownFolders(StudioFakes.DesktopPath)),
                roots,
                new DesktopLookService(Scanner),
                new LocalDesktopGrouper(new DeterministicFileClassifier(DefaultFileTypeRules.Create())),
                Settings,
                Ai,
                Boards,
                new FixedClock());
        }

        public ReplayScanner Scanner { get; } = new([]);

        public FixedAiSettings Settings { get; }

        public RecordingGroupingAi Ai { get; } = new();

        public MemoryBoards Boards { get; } = new();

        public DesktopGroupingService Service { get; }
    }

    private sealed class RecordingGroupingAi : IOrganizationSuggestionProvider
    {
        public int Calls { get; private set; }

        public AiGroupingRequest? LastRequest { get; private set; }

        public string Json { get; set; } = """{"schemaVersion":"1","groups":[]}""";

        public Task<AiGroupingResponse> GroupItemsAsync(AiGroupingRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new AiGroupingResponse(AiProviderStatus.Success, "OpenRouter", Json, "OpenRouter sorted your Desktop."));
        }

        public Task<OrganizationSuggestionResponse> SuggestAsync(OrganizationSuggestionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AiSentenceResponse> ReadSentenceAsync(AiSentenceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryBoards : IDesktopGroupRepository
    {
        public DesktopGroupBoard? Saved { get; set; }

        public Task<DesktopGroupBoard?> LoadAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved?.RootId == rootId ? Saved : null);

        public Task SaveAsync(DesktopGroupBoard board, CancellationToken cancellationToken = default)
        {
            Saved = board;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedAiSettings(AiSettings settings) : IAiSettingsRepository
    {
        public AiSettings Current { get; set; } = settings;

        public Task<AiSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(AiSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRoots(AuthorizedRoot root) : IAuthorizedRootRepository
    {
        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(rootId == root.Id ? root : null);

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>([root]);

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DesktopOnlyKnownFolders(string desktop) : IKnownFolders
    {
        public string? Desktop => desktop;

        public string? Downloads => null;

        public string? Documents => null;

        public string? Pictures => null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    }
}
