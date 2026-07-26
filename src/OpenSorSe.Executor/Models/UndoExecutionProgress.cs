namespace OpenSorSe.Executor.Models;
/// <summary>Reports undo progress.</summary>
/// <param name="Stage">Current stage.</param><param name="RecordsReceived">Supplied records.</param><param name="RecordsAttempted">Attempted records.</param><param name="RecordsSucceeded">Succeeded records.</param><param name="RecordsFailed">Failed records.</param><param name="CurrentUndoRecordId">Current record ID.</param><param name="CurrentUndoKind">Current kind.</param>
public sealed record UndoExecutionProgress(UndoExecutionProgressStage Stage, long RecordsReceived, long RecordsAttempted, long RecordsSucceeded, long RecordsFailed, string? CurrentUndoRecordId, UndoOperationKind? CurrentUndoKind);
