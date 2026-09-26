using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;
using DeskAI.Infrastructure.Indexing;
using DeskAI.Infrastructure.Scanning;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Tests;

public sealed class MetadataIndexServiceTests
{
    private static readonly MetadataScanOptions Bounds = new(maxDepth: 3, maxEntries: 50);

    [Fact]
    public async Task RefreshAsync_RemembersMetadataWithoutOpeningFileContents()
    {
        using var sandbox = new TemporaryDirectory();
        var lockedPath = sandbox.CreateDummyFile(@"Study\notes.txt", "private dummy content");
        sandbox.CreateDummyFile("budget.xlsx");
        // An exclusive lock proves indexing never opens the file itself.
        await using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());
        var root = Root(sandbox.Path);

        var result = await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        Assert.True(result.IsAllowed);
        Assert.Equal(2, result.Changes.Added);
        var stored = await index.ListForRootAsync(root.Id, TestContext.Current.CancellationToken);
        Assert.Contains(stored, file => file.RelativePath == @"Study\notes.txt");
        Assert.Contains(stored, file => file.Category == FileCategory.Spreadsheets);
        Assert.All(stored, file => Assert.False(System.IO.Path.IsPathRooted(file.RelativePath)));
    }

    [Fact]
    public async Task RefreshAsync_LeavesOutHiddenAndSystemFilesAndEverythingInsideHiddenFolders()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("essay.pdf");
        File.SetAttributes(sandbox.CreateDummyFile("desktop.ini"), FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(sandbox.CreateDummyFile("thumbs.db"), FileAttributes.System);
        sandbox.CreateDummyFile(@".git\config");
        sandbox.CreateDummyFile(@".git\objects\pack.idx");
        File.SetAttributes(System.IO.Path.Combine(sandbox.Path, ".git"), FileAttributes.Directory | FileAttributes.Hidden);
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());
        var root = Root(sandbox.Path);

        var result = await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Changes.Added);
        var stored = await index.ListForRootAsync(root.Id, TestContext.Current.CancellationToken);
        Assert.Equal("essay.pdf", Assert.Single(stored).RelativePath);
    }

    [Fact]
    public async Task RefreshAsync_ReportsALookThatStoppedAtTheItemLimit()
    {
        using var sandbox = new TemporaryDirectory();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" })
        {
            sandbox.CreateDummyFile(name);
        }

        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());

        var result = await service.RefreshAsync(
            Root(sandbox.Path), new MetadataScanOptions(maxDepth: 3, maxEntries: 2), TestContext.Current.CancellationToken);

        Assert.True(result.StoppedEarly);
        Assert.True(index.LastLook?.StoppedEarly);
        Assert.Equal(0, index.LastLook?.DeepFoldersSkipped);
    }

    [Fact]
    public async Task RefreshAsync_CountsFoldersTooDeepToEnterAndCallsTheLookComplete()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile(@"One\Two\deep.txt");
        sandbox.CreateDummyFile(@"Uno\Dos\deep.txt");
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());

        var result = await service.RefreshAsync(
            Root(sandbox.Path), new MetadataScanOptions(maxDepth: 1, maxEntries: 50), TestContext.Current.CancellationToken);

        Assert.False(result.StoppedEarly);
        Assert.Equal(2, result.DeepFoldersSkipped);
        Assert.Equal(2, index.LastLook?.DeepFoldersSkipped);
        Assert.False(index.LastLook?.StoppedEarly);
    }

    [Fact]
    public async Task RefreshAsync_RefusesAProtectedRootWithoutTouchingTheIndex()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("secret.txt");
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());
        var root = Root(sandbox.Path, RootAccessLevel.Protected);

        var result = await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Equal(FileIndexSyncResult.Empty, result.Changes);
        Assert.Equal(0, index.SynchronizeCalls);
    }

    [Fact]
    public async Task RefreshAsync_RefusesARootThatOverlapsAProtectedLocation()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("secret.txt");
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy([sandbox.Path]));

        var result = await service.RefreshAsync(Root(sandbox.Path), Bounds, TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Equal(0, index.SynchronizeCalls);
    }

    [Fact]
    public async Task RefreshAsync_SkipsProtectedChildrenInsteadOfRememberingThem()
    {
        using var sandbox = new TemporaryDirectory();
        var protectedChild = sandbox.CreateDummyDirectory("Credentials");
        File.WriteAllText(System.IO.Path.Combine(protectedChild, "token.txt"), "dummy");
        sandbox.CreateDummyFile("visible.txt");
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy(userProtectedEntries: [protectedChild]));
        var root = Root(sandbox.Path);

        var result = await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        var stored = await index.ListForRootAsync(root.Id, TestContext.Current.CancellationToken);
        Assert.Equal("visible.txt", Assert.Single(stored).RelativePath);
        Assert.Contains(result.Issues, issue => issue.Code == ScanIssueCode.ProtectedEntrySkipped);
    }

    [Fact]
    public async Task RefreshAsync_ForgetsFilesThatAreNoLongerThere()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("keep.txt");
        var removedPath = sandbox.CreateDummyFile("gone.txt");
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());
        var root = Root(sandbox.Path);
        await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        File.Delete(removedPath);
        var result = await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Changes.Removed);
        Assert.Equal("keep.txt", Assert.Single(
            await index.ListForRootAsync(root.Id, TestContext.Current.CancellationToken)).RelativePath);
    }

    [Fact]
    public async Task RefreshAsync_ReportsNothingChangedOnAnUnchangedFolder()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("stable.txt");
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());
        var root = Root(sandbox.Path);
        await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        var result = await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        Assert.False(result.Changes.ChangedAnything);
        Assert.Equal(1, result.Changes.Unchanged);
        Assert.Contains("Nothing changed", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_HonoursTheConfiguredEntryLimit()
    {
        using var sandbox = new TemporaryDirectory();
        for (var i = 0; i < 8; i++)
        {
            sandbox.CreateDummyFile($"file-{i}.txt");
        }

        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());
        var root = Root(sandbox.Path);

        var result = await service.RefreshAsync(
            root,
            new MetadataScanOptions(maxDepth: 1, maxEntries: 3),
            TestContext.Current.CancellationToken);

        Assert.True(result.Changes.TotalSeen <= 3);
        Assert.Contains(result.Issues, issue => issue.Code == ScanIssueCode.EntryLimitReached);
    }

    [Fact]
    public async Task RefreshAsync_StopsWhenCancelledAndWritesNothing()
    {
        using var sandbox = new TemporaryDirectory();
        sandbox.CreateDummyFile("one.txt");
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RefreshAsync(Root(sandbox.Path), Bounds, cancellation.Token));
        Assert.Equal(0, index.SynchronizeCalls);
    }

    [Fact]
    public async Task ForgetAsync_ErasesWhatWasRememberedButLeavesFilesAlone()
    {
        using var sandbox = new TemporaryDirectory();
        var filePath = sandbox.CreateDummyFile("keep-me.txt", "generated data");
        var index = new InMemoryFileIndex();
        var service = CreateService(index, new WindowsPathPolicy());
        var root = Root(sandbox.Path);
        await service.RefreshAsync(root, Bounds, TestContext.Current.CancellationToken);

        await service.ForgetAsync(root.Id, TestContext.Current.CancellationToken);

        Assert.Equal(0, (await service.GetStatisticsAsync(root.Id, TestContext.Current.CancellationToken)).FileCount);
        Assert.True(File.Exists(filePath));
        Assert.Equal("generated data", await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    private static MetadataIndexService CreateService(IFileIndex index, WindowsPathPolicy policy) => new(
        new WindowsMetadataScanner(policy),
        new DeterministicFileClassifier(DefaultFileTypeRules.Create()),
        index,
        policy,
        new FixedClock(new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero)));

    private static AuthorizedRoot Root(string path, RootAccessLevel permission = RootAccessLevel.Allowed) =>
        AuthorizedRoot.Create(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            path,
            "Practice folder",
            permission,
            RootAuthorizationScope.MetadataOnly);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class InMemoryFileIndex : IFileIndex
    {
        private readonly Dictionary<Guid, List<IndexedFile>> _entries = [];

        public int SynchronizeCalls { get; private set; }

        public FileIndexLook? LastLook { get; private set; }

        /// <summary>
        /// Deliberately unsupported. This fake exists to observe synchronization, and
        /// re-implementing the filter rules here would create a second copy that could
        /// silently disagree with the real one. Search behaviour is covered against the
        /// real store in <see cref="SqliteFileIndexTests"/>.
        /// </summary>
        public Task<IReadOnlyList<IndexedFile>> SearchRootAsync(
            Guid rootId,
            SearchQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This fake does not implement search.");

        public Task<FileIndexSyncResult> SynchronizeRootAsync(
            Guid rootId,
            IReadOnlyList<IndexedFile> files,
            FileIndexLook look,
            CancellationToken cancellationToken = default)
        {
            SynchronizeCalls++;
            LastLook = look;
            var existing = _entries.TryGetValue(rootId, out var stored) ? stored : [];
            var added = 0;
            var updated = 0;
            var unchanged = 0;
            foreach (var file in files)
            {
                var match = existing.FirstOrDefault(entry => entry.FileId == file.FileId);
                if (match is null)
                {
                    added++;
                }
                else if (match.MatchesStoredFacts(file))
                {
                    unchanged++;
                }
                else
                {
                    updated++;
                }
            }

            var removed = existing.Count(entry => files.All(file => file.FileId != entry.FileId));
            _entries[rootId] = [.. files];
            return Task.FromResult(new FileIndexSyncResult(added, updated, unchanged, removed));
        }

        public Task<IReadOnlyList<IndexedFile>> ListForRootAsync(
            Guid rootId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexedFile>>(
                _entries.TryGetValue(rootId, out var stored) ? stored.AsReadOnly() : []);

        /// <summary>
        /// Deliberately unsupported, for the same reason search is: this fake observes
        /// synchronization, and a second copy of the aggregate rules could silently
        /// disagree with the real one.
        /// </summary>
        public Task<IReadOnlyList<SizeGroup>> GetSizeCountsAsync(
            Guid rootId,
            long minimumSizeBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This fake does not implement size grouping.");

        public Task<RootStorageSummary> SummarizeRootAsync(
            Guid rootId,
            DateTimeOffset unchangedSinceUtc,
            int largestFileCount,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This fake does not implement summaries.");

        public Task<FileIndexStatistics> GetStatisticsAsync(
            Guid rootId,
            CancellationToken cancellationToken = default)
        {
            if (!_entries.TryGetValue(rootId, out var stored) || stored.Count == 0)
            {
                return Task.FromResult(FileIndexStatistics.Empty);
            }

            return Task.FromResult(new FileIndexStatistics(
                stored.Count,
                stored.Sum(file => file.SizeBytes),
                stored.Max(file => file.IndexedAtUtc)));
        }

        public Task ClearRootAsync(Guid rootId, CancellationToken cancellationToken = default)
        {
            _entries.Remove(rootId);
            return Task.CompletedTask;
        }
    }
}
