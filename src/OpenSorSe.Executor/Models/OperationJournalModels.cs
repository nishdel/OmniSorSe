#pragma warning disable CS1591

namespace OpenSorSe.Executor.Models;

/// <summary>Defines hard bounds for the durable Operation Journal.</summary>
public static class OperationJournalSchema
{
    public const int CurrentVersion = 1;
    public const int MaximumOperations = 500;
    public const long MaximumFileBytes = 128L * 1024 * 1024;
    public const int MaximumMessageLength = 4_096;
}

/// <summary>Identifies the lifecycle of an actual filesystem execution.</summary>
public enum OperationStatus
{
    Pending,
    Running,
    Succeeded,
    PartiallySucceeded,
    Failed,
    RolledBack,
    RollbackPartiallyFailed,
    Undone,
    UndoPartiallyCompleted,
    UndoBlockedByConflicts,
    Interrupted,
    Cancelled,
}

/// <summary>Identifies the execution outcome of one attempted action.</summary>
public enum JournalActionResult
{
    Pending,
    Succeeded,
    Failed,
    Skipped,
    RolledBack,
    RollbackFailed,
}

/// <summary>Identifies action-level rollback state.</summary>
public enum JournalRollbackResult
{
    NotRequired,
    Pending,
    Succeeded,
    Failed,
    Blocked,
}

/// <summary>Identifies action-level undo state.</summary>
public enum JournalUndoStatus
{
    NotAvailable,
    Available,
    Pending,
    Succeeded,
    Failed,
    Blocked,
}

/// <summary>Records one attempted action and all known recovery facts.</summary>
public sealed record OperationJournalAction(
    string ActionId,
    ChangeActionType ActionType,
    ChangeSuggestionSource SuggestionSource,
    string? OriginalPath,
    string IntendedDestinationPath,
    string? ActualResultingPath,
    FileIdentitySnapshot? PreExecutionIdentity,
    FileIdentitySnapshot? PostExecutionIdentity,
    ChangeValidationState ValidationState,
    JournalActionResult ExecutionResult,
    bool WasSkipped,
    ChangeConflictCategory ErrorCategory,
    string? ErrorDetails,
    IReadOnlyList<string> WarningDetails,
    bool RollbackAttempted,
    JournalRollbackResult RollbackResult,
    bool UndoAvailable,
    JournalUndoStatus UndoStatus,
    DateTimeOffset? UndoTimestampUtc,
    string? UndoConflictDetails,
    string? AiModel,
    string? AiRequestCorrelationId,
    bool DirectoryCreatedByOpenSorSe);

/// <summary>Persists what OmniSorSe actually attempted, independently from its source Change Plan.</summary>
public sealed record OperationJournalRecord(
    int SchemaVersion,
    string OperationId,
    string SourcePlanId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string OpenSorSeVersion,
    OperationStatus Status,
    string InitiatingFeature,
    string AffectedRootFolder,
    IReadOnlyList<OperationJournalAction> Actions,
    bool CancellationRequested,
    string Summary)
{
    public int SucceededCount => Actions.Count(action => action.ExecutionResult == JournalActionResult.Succeeded);
    public int FailedCount => Actions.Count(action => action.ExecutionResult == JournalActionResult.Failed);
    public int SkippedCount => Actions.Count(action => action.ExecutionResult == JournalActionResult.Skipped);
    public int RolledBackCount => Actions.Count(action => action.ExecutionResult == JournalActionResult.RolledBack);
    public bool UndoAvailable => Actions.Any(action => action.UndoAvailable && action.UndoStatus == JournalUndoStatus.Available);
}

/// <summary>Reports deterministic execution progress at safe action boundaries.</summary>
public sealed record ChangeExecutionProgress(
    OperationStatus Status,
    int TotalActions,
    int AttemptedActions,
    int SucceededActions,
    int FailedActions,
    string? CurrentActionId,
    string Message);

/// <summary>Contains a terminal operation result.</summary>
public sealed record ChangePlanExecutionResult(
    OperationJournalRecord Operation,
    bool Succeeded,
    bool WasCancelled,
    string Summary);

/// <summary>Contains a terminal undo result.</summary>
public sealed record ChangePlanUndoResult(
    OperationJournalRecord Operation,
    int ActionsUndone,
    int ActionsBlocked,
    int ActionsFailed,
    string Summary);
