using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Infrastructure.Execution;
using DeskAI.Infrastructure.Time;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Tests;

/// <summary>
/// The real-folder executor in situations the app itself cannot stage: a folder disconnected
/// part-way through a tidy, and a protected file inside a connected folder.
/// </summary>
public sealed class FolderTidyExecutorTests
{
    [Fact]
    public async Task A_folder_disconnected_part_way_through_stops_the_remaining_moves()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var first = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var second = sandbox.CreateDummyFile(@"Folder\b.pdf");
        var root = Allowed(folder);

        // Looked up at the start, before the run, and before each file: disconnected after the first file.
        var roots = new DisconnectingRootRepository(root, disconnectAfter: 3);
        var policy = new WindowsPathPolicy();
        var executor = Create(roots, policy);
        var moveA = Move("a.pdf");
        var moveB = Move("b.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [moveA, moveB]);

        var result = await executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [moveA.Id, moveB.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [moveA.Id] = Facts(first), [moveB.Id] = Facts(second) },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Operations[0].Outcome);
        Assert.Equal(ExecutionOutcome.Failed, result.Operations[1].Outcome);
        Assert.Contains("no longer connected", result.Operations[1].Error, StringComparison.Ordinal);
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task A_protected_file_inside_a_connected_folder_is_never_moved()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var secret = sandbox.CreateDummyFile(@"Folder\tax-return.pdf");
        var root = Allowed(folder);
        var roots = new DisconnectingRootRepository(root, disconnectAfter: int.MaxValue);
        var executor = Create(roots, new WindowsPathPolicy(userProtectedEntries: [secret]));
        var move = Move("tax-return.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        var result = await executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(secret) },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Failed, Assert.Single(result.Operations).Outcome);
        Assert.True(File.Exists(secret));
    }

    [Fact]
    public async Task A_folder_that_no_longer_passes_the_safety_re_check_moves_nothing()
    {
        using var sandbox = new TemporaryDirectory();
        var folder = sandbox.CreateDummyDirectory("Folder");
        sandbox.CreateDummyDirectory(@"Folder\Documents");
        var file = sandbox.CreateDummyFile(@"Folder\a.pdf");
        var root = Allowed(folder);
        var executor = new FolderTidyExecutor(
            new DisconnectingRootRepository(root, int.MaxValue),
            new FixedFolderService("This folder crosses a link or shortcut, so DeskAI will not tidy it."),
            new PlanValidator(new WindowsPathPolicy()),
            new WindowsPathPolicy(),
            new SystemClock(),
            new InMemoryOperationJournal(),
            new InMemoryPlanRepository());
        var move = Move("a.pdf");
        var plan = OrganizationPlan.CreateDraft(Guid.NewGuid(), root.Id, 1, DateTimeOffset.UtcNow, PlanValidator.CurrentPolicyVersion, [move]);

        var result = await executor.ExecuteAsync(
            plan,
            Approval.Create(Guid.NewGuid(), plan, [move.Id], DateTimeOffset.UtcNow),
            new Dictionary<Guid, ExpectedFile> { [move.Id] = Facts(file) },
            TestContext.Current.CancellationToken);

        Assert.Contains("link or shortcut", Assert.Single(result.Operations).Error, StringComparison.Ordinal);
        Assert.True(File.Exists(file));
    }

    private static FolderTidyExecutor Create(IAuthorizedRootRepository roots, IPathPolicy policy) =>
        new(roots, new FixedFolderService(null), new PlanValidator(policy), policy, new SystemClock(),
            new InMemoryOperationJournal(), new InMemoryPlanRepository());

    private static AuthorizedRoot Allowed(string folder) =>
        AuthorizedRoot.Create(Guid.NewGuid(), folder, "Folder", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly)
            .WithTidyAllowedSince(DateTimeOffset.UnixEpoch);

    private static MoveFileOperation Move(string name) =>
        new(Guid.NewGuid(), name, Path.Combine("Documents", name), "PDF file", OperationProvenance.Rule);

    private static ExpectedFile Facts(string path)
    {
        var info = new FileInfo(path);
        return new ExpectedFile(info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>Answers with the folder until it has been asked a set number of times.</summary>
    private sealed class DisconnectingRootRepository(AuthorizedRoot root, int disconnectAfter) : IAuthorizedRootRepository
    {
        private int _finds;

        public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(rootId == root.Id && ++_finds <= disconnectAfter ? root : null);

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>([root]);

        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedFolderService(string? problem) : IReadOnlyFolderService
    {
        public Task<string?> CheckStillSafeAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) =>
            Task.FromResult(problem);

        public Task<FolderPreviewResult> AuthorizeAndPreviewAsync(
            string selectedPath, MetadataScanOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuthorizedRoot>> ListAuthorizedAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RevokeAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
