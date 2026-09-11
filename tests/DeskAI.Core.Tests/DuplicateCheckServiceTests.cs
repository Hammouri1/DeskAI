using DeskAI.Core.Abstractions;
using DeskAI.Core.Content;
using DeskAI.Core.Execution;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;
using static DeskAI.Core.Tests.DuplicateFinderServiceTests;

namespace DeskAI.Core.Tests;

/// <summary>
/// The limits of a duplicate check, with a fake reader, so no test needs gigabytes of files.
/// Reading real files is tested through the whole app in <c>DuplicateCheckTests</c>.
/// </summary>
public sealed class DuplicateCheckServiceTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public async Task Preparing_reads_nothing()
    {
        var (service, reader, _) = World((3 * 1024 * 1024, ["a", "b"]));

        var question = await service.PrepareAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, question.Files.Count);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task At_most_200_files_whole_groups_only_and_the_rest_are_counted()
    {
        var groups = Enumerable.Range(0, 9)
            .Select(group => (Size: (group + 1) * 100_000L, Names: Enumerable.Range(0, 30).Select(file => $"g{group}-f{file}").ToArray()))
            .ToArray();
        var (service, _, _) = World(groups);

        var question = await service.PrepareAsync(TestContext.Current.CancellationToken);

        Assert.Equal(180, question.Files.Count);
        Assert.Equal(90, question.LeftOut);
        Assert.True(question.Files.Count <= DuplicateCheckService.MaxFilesPerCheck);
    }

    [Fact]
    public async Task Files_whose_beginnings_differ_are_never_read_to_the_end()
    {
        var (service, reader, _) = World((5 * 1024 * 1024, ["a", "b"]));
        reader.Beginning["a"] = "1";
        reader.Beginning["b"] = "2";

        var result = await service.CompareAsync(await service.PrepareAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Different.Count);
        Assert.Empty(group.IdenticalSets);
        Assert.All(reader.Calls, call => Assert.Equal(DuplicateCheckService.BeginningBytes, call.MaxBytes));
    }

    [Fact]
    public async Task Same_beginning_but_different_whole_is_not_a_copy()
    {
        var (service, reader, _) = World((5 * 1024 * 1024, ["a", "b", "c"]));
        reader.Whole["a"] = "X";
        reader.Whole["b"] = "X";
        reader.Whole["c"] = "Y";

        var result = await service.CompareAsync(await service.PrepareAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        var identical = Assert.Single(group.IdenticalSets);
        Assert.Equal(["a", "b"], identical.Select(file => file.RelativePath).Order());
        Assert.Equal("c", Assert.Single(group.Different).RelativePath);
        Assert.Equal(5 * 1024 * 1024, result.ReclaimableBytes);
    }

    [Fact]
    public async Task A_file_over_2_GB_is_not_read_to_the_end_and_says_so()
    {
        var (service, reader, _) = World((3 * Gigabyte, ["a", "b"]));

        var result = await service.CompareAsync(await service.PrepareAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.NotCompared.Count);
        Assert.All(group.NotCompared, item => Assert.Contains("over 2 GB", item.Reason, StringComparison.Ordinal));
        Assert.DoesNotContain(reader.Calls, call => call.MaxBytes is null);
    }

    [Fact]
    public async Task No_more_than_8_GB_is_read_to_the_end_in_one_check()
    {
        var (service, reader, _) = World((1900L * 1024 * 1024, ["a", "b", "c", "d", "e"]));

        var result = await service.CompareAsync(await service.PrepareAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Equal(4, reader.Calls.Count(call => call.MaxBytes is null));
        Assert.Contains("8 GB", Assert.Single(group.NotCompared).Reason, StringComparison.Ordinal);
        Assert.Equal(4, group.IdenticalCount);
    }

    [Fact]
    public async Task A_folder_no_longer_connected_is_not_read()
    {
        var (service, reader, roots) = World((1_000_000, ["a", "b"]));
        var question = await service.PrepareAsync(TestContext.Current.CancellationToken);
        roots.Remove(question.Files[0].RootId);

        var result = await service.CompareAsync(question, TestContext.Current.CancellationToken);

        Assert.Empty(reader.Calls);
        Assert.All(Assert.Single(result.Groups).NotCompared, item =>
            Assert.Contains("no longer connected", item.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stop_before_comparing_reads_nothing_and_says_it_stopped()
    {
        var (service, reader, _) = World((1_000_000, ["a", "b"]));
        var question = await service.PrepareAsync(TestContext.Current.CancellationToken);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        var result = await service.CompareAsync(question, stop.Token);

        Assert.True(result.Stopped);
        Assert.Empty(reader.Calls);
        Assert.Equal(2, result.NotComparedCount);
    }

    [Fact]
    public async Task Only_files_in_the_question_are_read()
    {
        var (service, reader, _) = World((1_000_000, ["a", "b", "c"]));
        var question = await service.PrepareAsync(TestContext.Current.CancellationToken);
        var shorter = question with { Files = question.Files.Where(file => file.RelativePath != "c").ToArray() };

        await service.CompareAsync(shorter, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(reader.Calls, call => call.RelativePath == "c");
    }

    /// <summary>
    /// Nothing but this service may be handed the reader. The check runs against every type in
    /// DeskAI.Core, so a later service that quietly takes one fails here.
    /// </summary>
    [Fact]
    public void Only_the_duplicate_check_can_reach_the_file_reader()
    {
        // The service's own private helper counts as the service.
        var takers = typeof(DuplicateCheckService).Assembly.GetTypes()
            .Where(type => type.DeclaringType != typeof(DuplicateCheckService))
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IFileFingerprinter))))
            .ToArray();

        Assert.Equal([typeof(DuplicateCheckService)], takers);
    }

    private static (DuplicateCheckService Service, FakeReader Reader, RemovableRoots Roots) World(
        params (long Size, string[] Names)[] groups)
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var folder = roots.Add("Study");
        var seed = 1;
        foreach (var (size, names) in groups)
        {
            foreach (var name in names)
            {
                index.Put(folder, Entry(folder, seed++, name, size));
            }
        }

        var removable = new RemovableRoots(roots);
        var reader = new FakeReader();
        return (new DuplicateCheckService(new DuplicateFinderService(roots, index), removable, reader), reader, removable);
    }

    /// <summary>Answers "same" for everything unless told otherwise, and records every call.</summary>
    private sealed class FakeReader : IFileFingerprinter
    {
        public Dictionary<string, string> Beginning { get; } = [];
        public Dictionary<string, string> Whole { get; } = [];
        public List<(string RelativePath, long? MaxBytes)> Calls { get; } = [];

        public Task<FileFingerprint> FingerprintAsync(
            AuthorizedRoot root,
            string relativePath,
            ExpectedFile expected,
            long? maxBytes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((relativePath, maxBytes));
            var hash = maxBytes is null ? Whole.GetValueOrDefault(relativePath, "same") : Beginning.GetValueOrDefault(relativePath, "same");
            return Task.FromResult(new FileFingerprint(FingerprintStatus.Read, hash, maxBytes ?? expected.SizeBytes, "Read."));
        }
    }

    /// <summary>The fake folders, one of which can be disconnected after the question.</summary>
    private sealed class RemovableRoots(FakeRoots inner) : IAuthorizedRootRepository
    {
        private readonly HashSet<Guid> _removed = [];

        public void Remove(Guid rootId) => _removed.Add(rootId);

        public async Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            _removed.Contains(rootId) ? null : await inner.FindAsync(rootId, cancellationToken);

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);

        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
