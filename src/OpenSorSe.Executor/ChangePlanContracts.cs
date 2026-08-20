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

/// <summary>Tracks whether authoritative recovery state currently permits new filesystem mutation.</summary>
public interface IRecoverySafetyState
{
    /// <summary>Gets whether new mutation and Undo operations are blocked.</summary>
    bool IsMutationBlocked { get; }

    /// <summary>Gets a bounded user-facing recovery explanation.</summary>
    string? Message { get; }

    /// <summary>Gets the affected store classification without exposing document content.</summary>
    string? StoreName { get; }

    /// <summary>Blocks mutation after authoritative corruption has been detected.</summary>
    void Block(OpenSorSe.Core.Persistence.AuthoritativeStoreCorruptionException exception);
}

/// <summary>Provides process-wide fail-closed recovery state for the desktop profile.</summary>
public sealed class RecoverySafetyState : IRecoverySafetyState
{
    private readonly object _sync = new();

    /// <summary>Gets a non-blocking state for isolated compatibility tests.</summary>
    public static IRecoverySafetyState Unmanaged => new RecoverySafetyState();

    /// <inheritdoc />
    public bool IsMutationBlocked { get; private set; }

    /// <inheritdoc />
    public string? Message { get; private set; }

    /// <inheritdoc />
    public string? StoreName { get; private set; }

    /// <inheritdoc />
    public void Block(OpenSorSe.Core.Persistence.AuthoritativeStoreCorruptionException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            IsMutationBlocked = true;
            StoreName = exception.StoreName;
            Message = $"{exception.StoreName} recovery is required. File changes and Undo are blocked; read-only discovery remains available.";
        }
    }
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
