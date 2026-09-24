using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

public sealed class RuleSimulationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SimulateAsync_ShowsWhatARuleWouldDoInAConnectedFolder()
    {
        var world = new World();
        var study = world.AddFolder("Study");
        world.AddFile(study, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var simulation = await world.Service.SimulateAsync(Now, TestContext.Current.CancellationToken);

        var folder = Assert.Single(simulation.Folders);
        Assert.Equal("Study", folder.RootName);
        Assert.Equal("Documents", Assert.Single(folder.Preview.Proposals).DestinationRelativeDirectory);
    }

    /// <summary>
    /// Scope comes from the same predicate search uses, so rules can never describe a folder
    /// search would not look in — including the generated practice workspace.
    /// </summary>
    [Theory]
    [InlineData(RootAuthorizationScope.ControlledDemo)]
    [InlineData(RootAuthorizationScope.Organize)]
    public async Task SimulateAsync_IgnoresFoldersThatSearchWouldNotLookIn(RootAuthorizationScope scope)
    {
        var world = new World();
        var demo = world.AddFolder("Practice", scope);
        world.AddFile(demo, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var simulation = await world.Service.SimulateAsync(Now, TestContext.Current.CancellationToken);

        Assert.Empty(simulation.Folders);
        Assert.Equal(0, simulation.ProposalCount);
    }

    /// <summary>
    /// A destination is a folder inside the connected folder, so two folders' "Documents"
    /// are different places. Merging the results would suggest otherwise.
    /// </summary>
    [Fact]
    public async Task SimulateAsync_KeepsEachConnectedFolderSeparate()
    {
        var world = new World();
        var study = world.AddFolder("Study");
        var work = world.AddFolder("Work");
        world.AddFile(study, "invoice-march.pdf");
        world.AddFile(work, "invoice-april.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var simulation = await world.Service.SimulateAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal(2, simulation.Folders.Count);
        Assert.All(simulation.Folders, folder => Assert.Single(folder.Preview.Proposals));
    }

    [Fact]
    public async Task SimulateAsync_CountsRulesThatAreTurnedOffWithoutApplyingThem()
    {
        var world = new World();
        var study = world.AddFolder("Study");
        world.AddFile(study, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"), enabled: false);

        var simulation = await world.Service.SimulateAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal(0, simulation.RulesConsidered);
        Assert.Equal(1, simulation.RulesTurnedOff);
        Assert.Equal(0, simulation.ProposalCount);
    }

    /// <summary>
    /// Rules test names, sizes, and dates. A practice run must not open a file, whatever
    /// permission the folder was given.
    /// </summary>
    [Fact]
    public async Task SimulateAsync_NeverReadsInsideAFile()
    {
        var world = new World();
        var study = world.AddFolder("Study", RootAuthorizationScope.MetadataAndContent);
        world.AddFile(study, "invoice-march.pdf");
        world.AddRule("Invoices", "Documents", new NameContainsCondition("invoice"));

        var simulation = await world.Service.SimulateAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal(1, simulation.ProposalCount);
        Assert.Equal(0, world.Index.ContentReads);
    }

    private sealed class World
    {
        private readonly FakeRules _rules = new();
        private readonly FakeRoots _roots = new();

        public FakeIndex Index { get; } = new();

        public RuleSimulationService Service => new(_rules, _roots, Index);

        public Guid AddFolder(string name, RootAuthorizationScope scope = RootAuthorizationScope.MetadataOnly) =>
            _roots.Add(name, scope);

        public void AddFile(Guid rootId, string relativePath) => Index.Put(rootId, relativePath);

        public void AddRule(string name, string destination, RuleCondition condition, bool enabled = true)
        {
            var rule = AutomationRule.Create(
                Guid.NewGuid(),
                name,
                [condition],
                new MoveToFolderAction(destination));
            _rules.Add(enabled ? rule : rule.WithEnabled(false));
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

    private sealed class FakeIndex : IFileIndex
    {
        private readonly Dictionary<Guid, List<IndexedFile>> _files = [];

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
            FileIndexLook look,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileIndexStatistics> GetStatisticsAsync(
            Guid rootId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearRootAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
