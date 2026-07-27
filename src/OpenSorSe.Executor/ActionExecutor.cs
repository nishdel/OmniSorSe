using System.Buffers;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor.Models;
using OpenSorSe.Core.Platform;
using OpenSorSe.Rules.Models;

namespace OpenSorSe.Executor;

/// <summary>Executes approved operations sequentially without overwriting destinations.</summary>
public sealed class ActionExecutor : IActionExecutor
{
    private const int BufferSize = 64 * 1024;
    private readonly IErrorHandler _errorHandler;
    private readonly ILogger _logger;
    private readonly Action<CopyLoopPhase>? _copyObserver;

    /// <summary>Initializes the executor with shared diagnostics infrastructure.</summary>
    /// <param name="loggingService">The centralized logging service.</param><param name="errorHandler">The handler for unexpected failures.</param>
    public ActionExecutor(ILoggingService loggingService, IErrorHandler errorHandler)
        : this(loggingService, errorHandler, null)
    {
    }

    internal ActionExecutor(ILoggingService loggingService, IErrorHandler errorHandler, Action<CopyLoopPhase>? copyObserver)
    {
        ArgumentNullException.ThrowIfNull(loggingService);
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _logger = loggingService.CreateLogger("Executor");
        _copyObserver = copyObserver;
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> ExecuteAsync(IReadOnlyCollection<PlannedOperation> operations, IProgress<ActionExecutionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (cancellationToken.IsCancellationRequested)
        {
            progress?.Report(new ActionExecutionProgress(ActionExecutionProgressStage.Cancelled, operations.Count, 0, 0, 0, 0, null, null));
            return new ActionExecutionResult(
                Array.Empty<ActionExecutionOutcome>(),
                Array.Empty<UndoRecord>(),
                new ActionExecutionStatistics(operations.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                true);
        }

        Validate(operations);
        var outcomes = new List<ActionExecutionOutcome>();
        var undo = new List<UndoRecord>();
        long succeeded = 0, failed = 0, skipped = 0, moves = 0, copies = 0, renames = 0, bytes = 0;
        Report(ActionExecutionProgressStage.Starting, null, null);
        var index = 0;
        while (index < operations.Count)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Result(true);
            }

            var operation = operations.ElementAt(index);
            Report(ActionExecutionProgressStage.Executing, operation.OperationId, operation.Kind);
            var executed = await ExecuteOneAsync(operation, index, cancellationToken).ConfigureAwait(false);
            outcomes.Add(executed.Outcome);
            if (executed.Undo is not null)
            {
                undo.Add(executed.Undo);
                succeeded++;
                bytes += executed.BytesCopied;
                switch (operation.Kind) { case PlannedOperationKind.Move: moves++; break; case PlannedOperationKind.Copy: copies++; break; case PlannedOperationKind.Rename: renames++; break; }
            }
            else if (executed.Outcome.Status == ActionExecutionStatus.Skipped) skipped++; else failed++;
            index++;
            Report(executed.Cancelled ? ActionExecutionProgressStage.Cancelled : ActionExecutionProgressStage.Completed, null, null);
            if (executed.Cancelled) return Result(true);
        }
        Report(ActionExecutionProgressStage.Completed, null, null);
        return Result(false);

        ActionExecutionResult Result(bool cancelled) => new(outcomes, undo, new(operations.Count, outcomes.Count, succeeded, failed, skipped, moves, copies, renames, bytes, failed + skipped), cancelled);
        void Report(ActionExecutionProgressStage stage, string? id, PlannedOperationKind? kind) => progress?.Report(new(stage, operations.Count, outcomes.Count, succeeded, failed, skipped, id, kind));
    }

    private async Task<ExecutionAttempt> ExecuteOneAsync(PlannedOperation operation, int index, CancellationToken token)
    {
        if (operation.Kind == PlannedOperationKind.Delete)
            return Failure(operation, ActionExecutionStatus.Skipped, ActionExecutionIssueKind.UnsupportedOperation, "Delete execution is unsupported in v0.1.");
        if (!TryValidateLive(operation, out var issue)) return Failure(operation, ActionExecutionStatus.Failed, issue!.Kind, issue.Message);
        try
        {
            if (operation.Kind == PlannedOperationKind.Copy)
            {
                var copied = await CopyAsync(operation.SourcePath, operation.DestinationPath!, token, _copyObserver).ConfigureAwait(false);
                return Success(operation, index, UndoOperationKind.Copy, copied);
            }
            if (operation.Kind == PlannedOperationKind.Rename && !SameDirectory(operation.SourcePath, operation.DestinationPath!))
                return Failure(operation, ActionExecutionStatus.Failed, ActionExecutionIssueKind.InvalidOperation, "Rename must remain in the source directory.");
            File.Move(operation.SourcePath, operation.DestinationPath!, false);
            return Success(operation, index, operation.Kind == PlannedOperationKind.Move ? UndoOperationKind.Move : UndoOperationKind.Rename, 0);
        }
        catch (OperationCanceledException)
        {
            return Failure(operation, ActionExecutionStatus.Failed, ActionExecutionIssueKind.CancelledDuringOperation, "Cancellation interrupted the copy operation.", true);
        }
        catch (UnauthorizedAccessException exception) { return LoggedFailure(operation, ActionExecutionIssueKind.AccessDenied, "Access was denied.", exception); }
        catch (IOException exception) { return LoggedFailure(operation, File.Exists(operation.DestinationPath) || Directory.Exists(operation.DestinationPath) ? ActionExecutionIssueKind.DestinationAlreadyExists : ActionExecutionIssueKind.IoFailure, "The filesystem operation could not be completed.", exception); }
    }

    private static bool TryValidateLive(PlannedOperation operation, out ActionExecutionIssue? issue)
    {
        issue = null;
        if (operation.File is null || string.IsNullOrWhiteSpace(operation.SourcePath) || string.IsNullOrWhiteSpace(operation.DestinationPath) || !Path.IsPathRooted(operation.SourcePath) || !Path.IsPathRooted(operation.DestinationPath)) { issue = new(operation.OperationId, ActionExecutionIssueKind.InvalidOperation, "The operation contains invalid paths."); return false; }
        string source, file, destination;
        try { source = Path.GetFullPath(operation.SourcePath); file = Path.GetFullPath(operation.File.FullPath); destination = Path.GetFullPath(operation.DestinationPath); } catch (ArgumentException) { issue = new(operation.OperationId, ActionExecutionIssueKind.InvalidOperation, "The operation contains invalid paths."); return false; }
        var comparer = PlatformServices.CurrentPathSemantics.Comparer;
        if (!comparer.Equals(source, file) || comparer.Equals(source, destination)) { issue = new(operation.OperationId, ActionExecutionIssueKind.InvalidOperation, "The source and destination are invalid."); return false; }
        if (Directory.Exists(source)) { issue = new(operation.OperationId, ActionExecutionIssueKind.SourceTypeUnsupported, "The source is not a supported regular file."); return false; }
        if (!File.Exists(source)) { issue = new(operation.OperationId, ActionExecutionIssueKind.SourceUnavailable, "The source file is unavailable."); return false; }
        try
        {
            var attributes = File.GetAttributes(source);
            if (attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint)) { issue = new(operation.OperationId, ActionExecutionIssueKind.SourceTypeUnsupported, "The source is not a supported regular file."); return false; }
            var parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) { issue = new(operation.OperationId, ActionExecutionIssueKind.DestinationDirectoryUnavailable, "The destination directory is unavailable."); return false; }
            if (File.Exists(destination) || Directory.Exists(destination)) { issue = new(operation.OperationId, ActionExecutionIssueKind.DestinationAlreadyExists, "The destination already exists."); return false; }
            var info = new FileInfo(source);
            if (operation.File.Metadata?.SizeInBytes is long size && size != info.Length || operation.File.Metadata?.LastWriteTimeUtc is DateTimeOffset time && time.UtcDateTime != info.LastWriteTimeUtc) { issue = new(operation.OperationId, ActionExecutionIssueKind.SourceChanged, "The source changed since planning."); return false; }
            return true;
        }
        catch (UnauthorizedAccessException) { issue = new(operation.OperationId, ActionExecutionIssueKind.AccessDenied, "Access was denied."); return false; }
        catch (IOException) { issue = new(operation.OperationId, ActionExecutionIssueKind.IoFailure, "The filesystem operation could not be completed."); return false; }
    }

    private static async Task<long> CopyAsync(string source, string destination, CancellationToken token, Action<CopyLoopPhase>? observer)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize); var created = false; long bytes = 0;
        try
        {
            await using var input = new FileStream(source, new FileStreamOptions { Access = FileAccess.Read, Mode = FileMode.Open, Share = FileShare.Read, Options = FileOptions.Asynchronous | FileOptions.SequentialScan, BufferSize = BufferSize });
            await using var output = new FileStream(destination, new FileStreamOptions { Access = FileAccess.Write, Mode = FileMode.CreateNew, Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.SequentialScan, BufferSize = BufferSize });
            created = true; observer?.Invoke(CopyLoopPhase.DestinationCreated); int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), token).ConfigureAwait(false)) != 0) { observer?.Invoke(CopyLoopPhase.BlockRead); await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); bytes += read; observer?.Invoke(CopyLoopPhase.BlockWritten); }
            await output.FlushAsync(token).ConfigureAwait(false); return bytes;
        }
        catch { if (created) { try { File.Delete(destination); } catch { } } throw; }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    private static bool SameDirectory(string source, string destination) =>
        string.Equals(
            Path.GetDirectoryName(source),
            Path.GetDirectoryName(destination),
            PlatformServices.CurrentPathSemantics.Comparison);
    private static ExecutionAttempt Success(PlannedOperation op, int index, UndoOperationKind kind, long bytes) { var id = $"undo:{index}"; return new(new(op.OperationId, op.Kind, op.SourcePath, op.DestinationPath, ActionExecutionStatus.Succeeded, id, null), new(id, op.OperationId, kind, op.SourcePath, op.DestinationPath!, DateTimeOffset.UtcNow), bytes, false); }
    private static ExecutionAttempt Failure(PlannedOperation op, ActionExecutionStatus status, ActionExecutionIssueKind kind, string message, bool cancelled = false) => new(new(op.OperationId, op.Kind, op.SourcePath, op.DestinationPath, status, null, new(op.OperationId, kind, message)), null, 0, cancelled);
    private ExecutionAttempt LoggedFailure(PlannedOperation op, ActionExecutionIssueKind kind, string message, Exception exception) { _logger.LogWarning(exception, "Execution issue {IssueKind}: {Message}", kind, message); return Failure(op, ActionExecutionStatus.Failed, kind, message); }
    private static void Validate(IReadOnlyCollection<PlannedOperation> operations) { var ids = new HashSet<string>(StringComparer.Ordinal); foreach (var op in operations) { if (op is null || string.IsNullOrWhiteSpace(op.OperationId) || !ids.Add(op.OperationId) || op.File is null || string.IsNullOrWhiteSpace(op.SourcePath) || !Enum.IsDefined(op.Kind) || (op.Kind != PlannedOperationKind.Delete && string.IsNullOrWhiteSpace(op.DestinationPath)) || (op.Kind == PlannedOperationKind.Delete && op.DestinationPath is not null)) throw new ArgumentException("The operation collection is invalid.", nameof(operations)); } }
    private sealed record ExecutionAttempt(ActionExecutionOutcome Outcome, UndoRecord? Undo, long BytesCopied, bool Cancelled);
}

internal enum CopyLoopPhase
{
    DestinationCreated,
    BlockRead,
    BlockWritten,
}
