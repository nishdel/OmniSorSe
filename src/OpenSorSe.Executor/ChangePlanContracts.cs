#pragma warning disable CS1591

using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Captures untrusted proposals as reviewable Change Plans.</summary>
public interface IChangePlanFactory
{
    Task<ChangePlan> CreateAsync(ChangePlanCreationRequest request, CancellationToken cancellationToken);
}

/// <summary>Validates complete plans without mutating the filesystem.</summary>
public interface IChangePlanValidator
{
    Task<ChangePlanValidationResult> ValidateAsync(
        ChangePlan plan,
        ChangePlanValidationPhase phase,
        CancellationToken cancellationToken);
}

/// <summary>Persists reviewable plans independently from executed operations.</summary>
public interface IChangePlanStore
{
    Task<IReadOnlyList<ChangePlan>> ListAsync(CancellationToken cancellationToken);
    Task<ChangePlan?> GetAsync(string planId, CancellationToken cancellationToken);
    Task UpsertAsync(ChangePlan plan, CancellationToken cancellationToken);
}

/// <summary>Persists actual attempted operations durably across restarts.</summary>
public interface IOperationJournalStore
{
    Task<IReadOnlyList<OperationJournalRecord>> ListAsync(CancellationToken cancellationToken);
    Task<OperationJournalRecord?> GetAsync(string operationId, CancellationToken cancellationToken);
    Task UpsertAsync(OperationJournalRecord operation, CancellationToken cancellationToken);
}

/// <summary>Executes, verifies, rolls back, undoes, and recovers approved plans.</summary>
public interface IChangePlanExecutionService
{
    Task<ChangePlanExecutionResult> ExecuteAsync(
        ChangePlan plan,
        string initiatingFeature,
        IProgress<ChangeExecutionProgress>? progress,
        CancellationToken cancellationToken);

    Task<ChangePlanUndoResult> UndoAsync(
        string operationId,
        IReadOnlyCollection<string>? actionIds,
        IProgress<ChangeExecutionProgress>? progress,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OperationJournalRecord>> RecoverInterruptedAsync(CancellationToken cancellationToken);
}

/// <summary>Exports operation records without reading user-file contents.</summary>
public interface IOperationReportExporter
{
    string Export(OperationJournalRecord operation);
}
