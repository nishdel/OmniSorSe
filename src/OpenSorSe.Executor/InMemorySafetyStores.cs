#pragma warning disable CS1591

using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Provides a process-local Change Plan store for tests and compatibility adapters.</summary>
public sealed class InMemoryChangePlanStore : IChangePlanStore
{
    private readonly List<ChangePlan> _plans = [];

    public Task<IReadOnlyList<ChangePlan>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ChangePlan>>(_plans.ToArray());
    }

    public Task<ChangePlan?> GetAsync(string planId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_plans.FirstOrDefault(plan => plan.PlanId == planId));
    }

    public Task UpsertAsync(ChangePlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _plans.RemoveAll(candidate => candidate.PlanId == plan.PlanId);
        _plans.Add(plan);
        return Task.CompletedTask;
    }
}

/// <summary>Provides a process-local journal for tests and legacy compatibility adapters.</summary>
public sealed class InMemoryOperationJournalStore : IOperationJournalStore
{
    private readonly List<OperationJournalRecord> _operations = [];

    public Task<IReadOnlyList<OperationJournalRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OperationJournalRecord>>(
            _operations.OrderByDescending(operation => operation.StartedAtUtc).ToArray());
    }

    public Task<OperationJournalRecord?> GetAsync(string operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_operations.FirstOrDefault(operation => operation.OperationId == operationId));
    }

    public Task UpsertAsync(OperationJournalRecord operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _operations.RemoveAll(candidate => candidate.OperationId == operation.OperationId);
        _operations.Add(operation);
        return Task.CompletedTask;
    }
}
