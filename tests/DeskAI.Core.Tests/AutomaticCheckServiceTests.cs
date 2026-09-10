using System.Reflection;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

/// <summary>
/// An automatic check is the first thing in DeskAI that happens without someone pressing
/// something at that moment. These tests fix both halves of that: it finds what it should,
/// and it cannot do anything with what it finds.
/// </summary>
public sealed class AutomaticCheckServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ReportsWhatTheRulesWouldMove()
    {
        var world = new World();
        var study = world.AddFolder("Study");
        world.AddFile(study, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var result = await world.Service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FoldersChecked);
        Assert.Equal(1, result.ProposalCount);
        Assert.True(result.HasSomethingToReview);
    }

    [Fact]
    public async Task RunAsync_RefreshesWhatEachConnectedFolderRemembersFirst()
    {
        var world = new World();
        world.AddFolder("Study");
        world.AddFolder("Work");

        await world.Service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, world.Index.Refreshes);
    }

    /// <summary>
    /// A check must never widen scope. It looks where search looks and nowhere else, so the
    /// generated practice workspace is not swept up in it.
    /// </summary>
    [Fact]
    public async Task RunAsync_IgnoresFoldersSearchWouldNotLookIn()
    {
        var world = new World();
        var demo = world.AddFolder("Practice", RootAuthorizationScope.ControlledDemo);
        world.AddFile(demo, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var result = await world.Service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, result.FoldersChecked);
        Assert.Equal(0, result.ProposalCount);
        Assert.Equal(0, world.Index.Refreshes);
    }

    /// <summary>
    /// Rules test names, sizes, and dates. Nothing on this path opens a file, whatever
    /// permission a folder was given — a check that ran unattended and read people's
    /// documents would be exactly the thing the permission model exists to prevent.
    /// </summary>
    [Fact]
    public async Task RunAsync_NeverReadsInsideAFile()
    {
        var world = new World();
        var study = world.AddFolder("Study", RootAuthorizationScope.MetadataAndContent);
        world.AddFile(study, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));

        await world.Service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, world.Index.ContentReads);
    }

    /// <summary>
    /// Containment by construction. A check cannot move a file because nothing that moves
    /// files is reachable from it: if someone ever adds an executor, a planner, or an undo
    /// service to the constructor, this fails before the feature ships.
    /// </summary>
    [Fact]
    public void Constructor_CannotReachAnythingThatChangesAFile()
    {
        var forbidden = new[]
        {
            typeof(IPlanExecutor),
            typeof(IFolderTidyExecutor),
            typeof(IUndoService),
            typeof(IOrganizationPlanner),
            typeof(IFileScanner),
            typeof(IContentTextExtractor),
        };

        var dependencies = typeof(AutomaticCheckService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));
    }

    [Fact]
    public async Task RunAsync_RemembersWhenItLastChecked()
    {
        var world = new World();
        world.AddFolder("Study");

        await world.Service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now, await world.Settings.ReadLastCheckedAtUtcAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunIfDueAsync_DoesNothingWhilePaused()
    {
        var world = new World();
        world.AddFolder("Study");
        await world.Settings.SaveAsync(
            AutomaticCheckSettings.Default with { IsPaused = true },
            TestContext.Current.CancellationToken);

        var result = await world.Service.RunIfDueAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, world.Index.Refreshes);
    }

    /// <summary>
    /// Pausing stops DeskAI checking on its own. Someone who presses "Check now" is asking,
    /// and asking is never refused by a setting about automatic behaviour.
    /// </summary>
    [Fact]
    public async Task RunAsync_StillChecksWhenSomeoneAsksWhilePaused()
    {
        var world = new World();
        world.AddFolder("Study");
        await world.Settings.SaveAsync(
            AutomaticCheckSettings.Default with { IsPaused = true },
            TestContext.Current.CancellationToken);

        var result = await world.Service.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FoldersChecked);
    }

    [Fact]
    public async Task RunIfDueAsync_ChecksWhenTheIntervalHasPassed()
    {
        var world = new World();
        world.AddFolder("Study");
        await world.Settings.RecordCheckedAtAsync(Now.AddHours(-2), TestContext.Current.CancellationToken);

        var result = await world.Service.RunIfDueAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(1, world.Index.Refreshes);
    }

    [Fact]
    public async Task RunIfDueAsync_WaitsWhenACheckJustHappened()
    {
        var world = new World();
        world.AddFolder("Study");
        await world.Settings.RecordCheckedAtAsync(Now.AddMinutes(-1), TestContext.Current.CancellationToken);

        Assert.Null(await world.Service.RunIfDueAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Two passes over the same folders would produce two counts for one state and race each
    /// other to the notice, so a second request while one is running is dropped rather than
    /// queued — the check already in flight will report the same current state anyway.
    /// </summary>
    [Fact]
    public async Task Coordinator_RunsOneCheckAtATime()
    {
        var world = new World();
        world.AddFolder("Study");
        world.Index.Gate = new TaskCompletionSource();
        using var coordinator = world.Coordinator();

        var first = coordinator.RunNowAsync(TestContext.Current.CancellationToken);
        var second = await coordinator.RunNowAsync(TestContext.Current.CancellationToken);
        world.Index.Gate.SetResult();

        Assert.Null(second);
        Assert.NotNull(await first);
        Assert.Equal(1, world.Index.Refreshes);
    }

    /// <summary>
    /// Someone reaching for a stop control means the thing happening now. Stopping is a
    /// normal outcome rather than a failure, and nothing needs unwinding because a check
    /// changed nothing.
    /// </summary>
    [Fact]
    public async Task Coordinator_StopsACheckThatIsAlreadyRunning()
    {
        var world = new World();
        world.AddFolder("Study");
        world.Index.Gate = new TaskCompletionSource();
        using var coordinator = world.Coordinator();

        var running = coordinator.RunNowAsync(TestContext.Current.CancellationToken);
        coordinator.StopRunningCheck();

        Assert.Null(await running);
        Assert.Null(coordinator.Latest);
    }

    [Fact]
    public async Task Coordinator_AnnouncesWhatACheckFound()
    {
        var world = new World();
        var study = world.AddFolder("Study");
        world.AddFile(study, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));
        using var coordinator = world.Coordinator();
        AutomaticCheckResult? announced = null;
        coordinator.Checked += (_, result) => announced = result;

        await coordinator.RunNowAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(announced);
        Assert.Equal(1, announced.ProposalCount);
        Assert.Same(announced, coordinator.Latest);
    }

    [Fact]
    public async Task Coordinator_RecordsWhatEachCheckFound()
    {
        var world = new World();
        var study = world.AddFolder("Study");
        world.AddFile(study, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));
        using var coordinator = world.Coordinator();

        await coordinator.RunNowAsync(TestContext.Current.CancellationToken);

        var run = Assert.Single(world.History.Runs);
        Assert.Equal(AutomaticCheckOutcome.Completed, run.Outcome);
        Assert.Equal(1, run.ProposalCount);
        Assert.Contains("Nothing was moved", run.Describe(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A history that quietly omitted the interrupted runs would be a reassuring record
    /// rather than an accurate one.
    /// </summary>
    [Fact]
    public async Task Coordinator_RecordsACheckThatWasStopped()
    {
        var world = new World();
        world.AddFolder("Study");
        world.Index.Gate = new TaskCompletionSource();
        using var coordinator = world.Coordinator();

        var running = coordinator.RunNowAsync(TestContext.Current.CancellationToken);
        coordinator.StopRunningCheck();
        await running;

        var run = Assert.Single(world.History.Runs);
        Assert.Equal(AutomaticCheckOutcome.Stopped, run.Outcome);
        Assert.Contains("Nothing was changed", run.Describe(), StringComparison.Ordinal);
    }

    /// <summary>A check that was not due did not happen, so it is not part of the history.</summary>
    [Fact]
    public async Task Coordinator_RecordsNothingWhenNoCheckWasDue()
    {
        var world = new World();
        world.AddFolder("Study");
        await world.Settings.RecordCheckedAtAsync(Now.AddMinutes(-1), TestContext.Current.CancellationToken);
        using var coordinator = world.Coordinator();

        await coordinator.RunIfDueAsync(TestContext.Current.CancellationToken);

        Assert.Empty(world.History.Runs);
    }

    /// <summary>
    /// Missed checks are never replayed, so the long gap has to be said out loud instead.
    /// Otherwise a history would look as though DeskAI had been watching the whole time.
    /// </summary>
    [Fact]
    public async Task RunAsync_MarksTheFirstCheckAfterALongGapAsCatchUp()
    {
        var world = new World();
        world.AddFolder("Study");
        await world.Settings.RecordCheckedAtAsync(Now.AddDays(-2), TestContext.Current.CancellationToken);

        var result = await world.Service.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(result.WasCatchUp);
    }

    [Fact]
    public async Task RunAsync_DoesNotCallAnOrdinaryCheckACatchUp()
    {
        var world = new World();
        world.AddFolder("Study");
        await world.Settings.RecordCheckedAtAsync(Now.AddMinutes(-16), TestContext.Current.CancellationToken);

        var result = await world.Service.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(result.WasCatchUp);
    }

    private sealed class World
    {
        private readonly FakeRules _rules = new();
        private readonly FakeRoots _roots = new();
        private readonly FakeFolders _folders;

        public World()
        {
            Index = new FakeIndex(_roots);
            _folders = new FakeFolders(_roots);
        }

        public FakeIndex Index { get; }

        public FakeCheckSettings Settings { get; } = new();

        public FakeCheckHistory History { get; } = new();

        public AutomaticCheckCoordinator Coordinator() =>
            new(Service, History, new FixedClock(Now));

        public AutomaticCheckService Service => new(
            new ConnectedFolderService(_folders, Index, _roots),
            new RuleSimulationService(_rules, _roots, Index),
            Settings,
            _roots,
            new FixedClock(Now));

        public Guid AddFolder(string name, RootAuthorizationScope scope = RootAuthorizationScope.MetadataOnly) =>
            _roots.Add(name, scope);

        public void AddFile(Guid rootId, string relativePath) => Index.Put(rootId, relativePath);

        public void AddRule(string name, string destination, RuleCondition condition)
            => _rules.Add(AutomationRule.Create(
                Guid.NewGuid(),
                name,
                [condition],
                new MoveToFolderAction(destination)));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeCheckSettings : IAutomaticCheckSettingsRepository
    {
        private AutomaticCheckSettings _settings = AutomaticCheckSettings.Default;
        private DateTimeOffset? _lastChecked;

        public Task<AutomaticCheckSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveAsync(AutomaticCheckSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> ReadLastCheckedAtUtcAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_lastChecked);

        public Task RecordCheckedAtAsync(DateTimeOffset checkedAtUtc, CancellationToken cancellationToken = default)
        {
            _lastChecked = checkedAtUtc;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCheckHistory : IAutomaticCheckHistoryRepository
    {
        public List<AutomaticCheckRun> Runs { get; } = [];

        public Task AppendAsync(AutomaticCheckRun run, CancellationToken cancellationToken = default)
        {
            Runs.Add(run);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AutomaticCheckRun>> ListRecentAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AutomaticCheckRun>>(
                Runs.AsEnumerable().Reverse().Take(limit).ToList().AsReadOnly());

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Runs.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRules : IRuleRepository
    {
        private readonly List<AutomationRule> _rules = [];

        public void Add(AutomationRule rule) => _rules.Add(rule);

        public Task<IReadOnlyList<AutomationRule>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AutomationRule>>(_rules.AsReadOnly());

        public Task<AutomationRule?> FindAsync(Guid ruleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules.FirstOrDefault(rule => rule.Id == ruleId));

        public Task SaveAsync(AutomationRule rule, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRoots : IAuthorizedRootRepository
    {
        private readonly List<AuthorizedRoot> _roots = [];

        public Guid Add(string name, RootAuthorizationScope scope)
        {
            var id = Guid.NewGuid();
            _roots.Add(AuthorizedRoot.Create(
                id,
                Path.Combine(Path.GetTempPath(), "deskai-tests", name),
                name,
                RootAccessLevel.Allowed,
                scope));
            return id;
        }

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>(_roots.AsReadOnly());

        public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_roots.FirstOrDefault(root => root.Id == rootId));

        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeFolders(FakeRoots roots) : IReadOnlyFolderService
    {
        private readonly FakeRoots _roots = roots;

        public Task<FolderPreviewResult> AuthorizeAndPreviewAsync(
            string selectedPath,
            MetadataScanOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuthorizedRoot>> ListAuthorizedAsync(CancellationToken cancellationToken = default) =>
            _roots.ListAsync(cancellationToken);

        public Task RevokeAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> CheckStillSafeAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Remembers files and counts what was asked of it. Refreshing here re-reads metadata
    /// and nothing else, which is what the real index does.
    /// </summary>
    private sealed class FakeIndex(FakeRoots roots) : IFileIndex, IMetadataIndexService
    {
        private readonly Dictionary<Guid, List<IndexedFile>> _files = [];
        private readonly FakeRoots _roots = roots;

        public int Refreshes { get; private set; }

        /// <summary>Stays zero unless something tries to read a file's bytes.</summary>
        public int ContentReads { get; }

        public void Put(Guid rootId, string relativePath)
        {
            if (!_files.TryGetValue(rootId, out var list))
            {
                list = [];
                _files[rootId] = list;
            }

            list.Add(new IndexedFile(
                rootId,
                Guid.NewGuid(),
                relativePath,
                FileKind.Document,
                FileCategory.Documents,
                1_000,
                Now,
                Now,
                Now));
        }

        /// <summary>Set to hold a refresh open, so a second check meets one in progress.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task<IndexUpdateResult> RefreshAsync(
            AuthorizedRoot root,
            MetadataScanOptions options,
            CancellationToken cancellationToken = default)
        {
            Refreshes++;
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return await Task.FromResult(new IndexUpdateResult(
                true,
                "Remembered names, sizes, and dates only.",
                FileIndexSyncResult.Empty,
                [])).ConfigureAwait(false);
        }

        public Task<FileIndexStatistics> GetStatisticsAsync(
            Guid rootId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileIndexStatistics(
                _files.TryGetValue(rootId, out var files) ? files.Count : 0,
                TotalSizeBytes: 0,
                Now));

        public Task ForgetAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IndexedFile>> SearchRootAsync(
            Guid rootId,
            SearchQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            IReadOnlyList<IndexedFile> files = _files.TryGetValue(rootId, out var stored)
                ? stored.Take(query.Limit).ToList().AsReadOnly()
                : [];
            return Task.FromResult(files);
        }

        public Task<IReadOnlyList<SizeGroup>> GetSizeCountsAsync(
            Guid rootId,
            long minimumSizeBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RootStorageSummary> SummarizeRootAsync(
            Guid rootId,
            DateTimeOffset unchangedSinceUtc,
            int largestFileCount,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IndexedFile>> ListForRootAsync(
            Guid rootId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileIndexSyncResult> SynchronizeRootAsync(
            Guid rootId,
            IReadOnlyList<IndexedFile> files,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearRootAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
