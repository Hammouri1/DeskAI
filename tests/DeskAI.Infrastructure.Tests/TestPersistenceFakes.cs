using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Infrastructure.Tests;

/// <param name="plans">Where to find which folder a record's plan belongs to, if a test needs that.</param>
internal class InMemoryOperationJournal(InMemoryPlanRepository? plans = null) : IOperationJournal
{
    private readonly Dictionary<Guid, ExecutionJournalEntry> _entries = [];

    public Task<IReadOnlyList<ExecutionJournalEntry>> ListForRootAsync(
        Guid rootId,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ExecutionJournalEntry> result = _entries.Values
            .Where(entry => plans?.RootOf(entry.PlanId, entry.PlanRevision) == rootId)
            .OrderByDescending(entry => entry.StartedAtUtc)
            .Take(maximumCount)
            .ToArray();
        return Task.FromResult(result);
    }

    public virtual Task CreateAsync(ExecutionJournalEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Add(entry.Id, entry);
        return Task.CompletedTask;
    }

    public Task UpdateOperationAsync(
        Guid transactionId,
        Guid operationId,
        JournalOperationState state,
        string? failureMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = _entries[transactionId];
        var operations = entry.Operations
            .Select(operation => operation.OperationId == operationId
                ? operation with { State = state, Error = failureMessage }
                : operation)
            .ToArray();
        _entries[transactionId] = entry with { Operations = operations };
        return Task.CompletedTask;
    }

    public Task UpdateTransactionAsync(
        Guid transactionId,
        ExecutionTransactionState state,
        DateTimeOffset? finishedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries[transactionId] = _entries[transactionId] with { State = state, FinishedAtUtc = finishedAtUtc };
        return Task.CompletedTask;
    }

    public Task<ExecutionJournalEntry?> FindAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.GetValueOrDefault(transactionId));
    }

    public Task<IReadOnlyList<ExecutionJournalEntry>> ListRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ExecutionJournalEntry> result = _entries.Values
            .OrderByDescending(entry => entry.StartedAtUtc)
            .Take(maximumCount)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ExecutionJournalEntry>> ListIncompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ExecutionJournalEntry> result = _entries.Values
            .Where(entry => entry.State is ExecutionTransactionState.Prepared or
                ExecutionTransactionState.Executing or ExecutionTransactionState.RecoveryRequired)
            .ToArray();
        return Task.FromResult(result);
    }
}

internal sealed class FailingCreateOperationJournal : InMemoryOperationJournal
{
    public override Task CreateAsync(ExecutionJournalEntry entry, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Generated journal failure.");
}

internal sealed class InMemoryAuthorizedRootRepository : IAuthorizedRootRepository
{
    private readonly Dictionary<Guid, AuthorizedRoot> _roots = [];

    public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _roots[root.Id] = root;
        return Task.CompletedTask;
    }

    public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_roots.GetValueOrDefault(rootId));
    }

    public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AuthorizedRoot>>(_roots.Values.ToArray());
    }

    public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _roots.Remove(rootId);
        return Task.CompletedTask;
    }

    public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default)
    {
        if (_roots.TryGetValue(rootId, out var root) &&
            root.AuthorizationScope is RootAuthorizationScope.MetadataOnly or RootAuthorizationScope.MetadataAndContent)
        {
            _roots[rootId] = root.WithTidyAllowedSince(grantedAtUtc);
        }

        return Task.CompletedTask;
    }

    public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        if (_roots.TryGetValue(rootId, out var root))
        {
            _roots[rootId] = root.WithTidyAllowedSince(null);
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryPlanRepository : IPlanRepository
{
    private readonly Dictionary<(Guid Id, int Revision), OrganizationPlan> _plans = [];

    public Task SaveAsync(OrganizationPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _plans[(plan.Id, plan.Revision)] = plan;
        return Task.CompletedTask;
    }

    public Task<OrganizationPlan?> FindAsync(Guid planId, int revision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_plans.GetValueOrDefault((planId, revision)));
    }

    public Guid? RootOf(Guid planId, int revision) => _plans.GetValueOrDefault((planId, revision))?.RootId;
}
