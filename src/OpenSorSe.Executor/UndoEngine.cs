using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor.Models;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Executor;

/// <summary>Conservatively reverses explicit executor records without history access.</summary>
public sealed class UndoEngine : IUndoEngine
{
    private readonly IErrorHandler _errorHandler;
    private readonly ILogger _logger;

    /// <summary>Initializes an undo engine.</summary>
    /// <param name="loggingService">The centralized logging service.</param>
    /// <param name="errorHandler">The handler for unexpected failures.</param>
    public UndoEngine(ILoggingService loggingService, IErrorHandler errorHandler)
    {
        ArgumentNullException.ThrowIfNull(loggingService);
        _logger = loggingService.CreateLogger("Executor");
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
    }

    /// <inheritdoc />
    public Task<UndoExecutionResult> UndoAsync(IReadOnlyCollection<UndoRecord> records, IProgress<UndoExecutionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (cancellationToken.IsCancellationRequested) return Task.FromResult(Result(records.Count, [], 0, 0, 0, 0, 0, true, progress));
        Validate(records);
        var outcomes = new List<UndoExecutionOutcome>(); long succeeded = 0, failed = 0, moves = 0, copies = 0, renames = 0;
        Report(UndoExecutionProgressStage.Starting, null, null);
        foreach (var record in records)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromResult(Result(records.Count, outcomes, succeeded, failed, moves, copies, renames, true, progress));
            Report(UndoExecutionProgressStage.Executing, record.UndoRecordId, record.Kind);
            var outcome = UndoOne(record); outcomes.Add(outcome);
            if (outcome.Status == UndoExecutionStatus.Succeeded) { succeeded++; if (record.Kind == UndoOperationKind.Move) moves++; else if (record.Kind == UndoOperationKind.Copy) copies++; else renames++; } else failed++;
            Report(UndoExecutionProgressStage.Completed, null, null);
        }
        return Task.FromResult(Result(records.Count, outcomes, succeeded, failed, moves, copies, renames, false, progress));

        void Report(UndoExecutionProgressStage stage, string? id, UndoOperationKind? kind) => progress?.Report(new(stage, records.Count, outcomes.Count, succeeded, failed, id, kind));
    }

    private UndoExecutionOutcome UndoOne(UndoRecord record)
    {
        if (!TryPaths(record, out var original, out var result, out var issue)) return Failure(record, issue!);
        try
        {
            if (Directory.Exists(result)) return Failure(record, new(record.UndoRecordId, UndoExecutionIssueKind.ResultTypeUnsupported, "The result is not a supported regular file."));
            if (!File.Exists(result)) return Failure(record, new(record.UndoRecordId, UndoExecutionIssueKind.ResultUnavailable, "The result file is unavailable."));
            var attributes = File.GetAttributes(result);
            if (attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint)) return Failure(record, new(record.UndoRecordId, UndoExecutionIssueKind.ResultTypeUnsupported, "The result is not a supported regular file."));
            if (record.Kind == UndoOperationKind.Copy) { File.Delete(result); return Success(record); }
            if (File.Exists(original) || Directory.Exists(original)) return Failure(record, new(record.UndoRecordId, UndoExecutionIssueKind.OriginalPathOccupied, "The original path is occupied."));
            var parent = Path.GetDirectoryName(original);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return Failure(record, new(record.UndoRecordId, UndoExecutionIssueKind.OriginalParentUnavailable, "The original parent directory is unavailable."));
            if (record.Kind == UndoOperationKind.Rename && !SameDirectory(original, result)) return Failure(record, new(record.UndoRecordId, UndoExecutionIssueKind.PathMismatch, "Rename paths must have the same parent directory."));
            File.Move(result, original, false); return Success(record);
        }
        catch (UnauthorizedAccessException ex) { return LoggedFailure(record, UndoExecutionIssueKind.AccessDenied, "Access was denied.", ex); }
        catch (IOException ex) { return LoggedFailure(record, UndoExecutionIssueKind.IoFailure, "The filesystem operation could not be completed.", ex); }
    }

    private static bool TryPaths(UndoRecord record, out string original, out string result, out UndoExecutionIssue? issue)
    {
        original = result = string.Empty; issue = null;
        try { original = Path.GetFullPath(record.OriginalPath); result = Path.GetFullPath(record.ResultPath); }
        catch (ArgumentException) { issue = new(record.UndoRecordId, UndoExecutionIssueKind.PathMismatch, "The record paths are invalid."); return false; }
        var comparer = PlatformServices.CurrentPathSemantics.Comparer;
        if (comparer.Equals(original, result)) { issue = new(record.UndoRecordId, UndoExecutionIssueKind.PathMismatch, "The record paths must differ."); return false; }
        return true;
    }

    private static bool SameDirectory(string first, string second) =>
        string.Equals(
            Path.GetDirectoryName(first),
            Path.GetDirectoryName(second),
            PlatformServices.CurrentPathSemantics.Comparison);
    private static UndoExecutionOutcome Success(UndoRecord record) => new(record.UndoRecordId, record.OperationId, record.Kind, record.OriginalPath, record.ResultPath, UndoExecutionStatus.Succeeded, null);
    private static UndoExecutionOutcome Failure(UndoRecord record, UndoExecutionIssue issue) => new(record.UndoRecordId, record.OperationId, record.Kind, record.OriginalPath, record.ResultPath, UndoExecutionStatus.Failed, issue);
    private UndoExecutionOutcome LoggedFailure(UndoRecord record, UndoExecutionIssueKind kind, string message, Exception exception) { _logger.LogWarning(exception, "Undo issue {IssueKind}: {Message}", kind, message); return Failure(record, new(record.UndoRecordId, kind, message)); }
    private static UndoExecutionResult Result(long received, IReadOnlyList<UndoExecutionOutcome> outcomes, long succeeded, long failed, long moves, long copies, long renames, bool cancelled, IProgress<UndoExecutionProgress>? progress) { var result = new UndoExecutionResult(outcomes, new(received, outcomes.Count, succeeded, failed, moves, copies, renames, failed), cancelled); progress?.Report(new(cancelled ? UndoExecutionProgressStage.Cancelled : UndoExecutionProgressStage.Completed, received, outcomes.Count, succeeded, failed, null, null)); return result; }
    private static void Validate(IReadOnlyCollection<UndoRecord> records) { var ids = new HashSet<string>(StringComparer.Ordinal); foreach (var r in records) { if (r is null || string.IsNullOrWhiteSpace(r.UndoRecordId) || !ids.Add(r.UndoRecordId) || string.IsNullOrWhiteSpace(r.OperationId) || !Enum.IsDefined(r.Kind) || string.IsNullOrWhiteSpace(r.OriginalPath) || string.IsNullOrWhiteSpace(r.ResultPath) || !Path.IsPathRooted(r.OriginalPath) || !Path.IsPathRooted(r.ResultPath) || r.ExecutedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("The undo record collection is invalid.", nameof(records)); if (!TryPaths(r, out _, out _, out _)) throw new ArgumentException("The undo record paths are invalid.", nameof(records)); } }
}
