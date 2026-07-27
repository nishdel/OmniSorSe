#pragma warning disable CS1591

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>
/// Provides deterministic Change Plan execution with durable action-level journalling,
/// verification, reverse-order rollback, conflict-aware undo, and restart recovery.
/// </summary>
public sealed class ChangePlanExecutionService : IChangePlanExecutionService
{
    private readonly IFileSystemGateway _fileSystem;
    private readonly IChangePlanValidator _validator;
    private readonly IChangePlanStore _planStore;
    private readonly IOperationJournalStore _journal;
    private readonly ILogger _logger;

    public ChangePlanExecutionService(
        IFileSystemGateway fileSystem,
        IChangePlanValidator validator,
        IChangePlanStore planStore,
        IOperationJournalStore journal,
        ILoggingService loggingService)
        : this(
            fileSystem,
            validator,
            planStore,
            journal,
            (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(ChangePlanExecutionService)))
    {
    }

    /// <summary>Initializes a service with null logging for isolated compatibility workflows.</summary>
    public ChangePlanExecutionService(
        IFileSystemGateway fileSystem,
        IChangePlanValidator validator,
        IChangePlanStore planStore,
        IOperationJournalStore journal)
        : this(fileSystem, validator, planStore, journal, NullLogger.Instance)
    {
    }

    private ChangePlanExecutionService(
        IFileSystemGateway fileSystem,
        IChangePlanValidator validator,
        IChangePlanStore planStore,
        IOperationJournalStore journal,
        ILogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChangePlanExecutionResult> ExecuteAsync(
        ChangePlan plan,
        string initiatingFeature,
        IProgress<ChangeExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(initiatingFeature))
        {
            throw new ArgumentException("An initiating feature is required.", nameof(initiatingFeature));
        }

        var validation = await _validator.ValidateAsync(
            plan,
            ChangePlanValidationPhase.PreExecution,
            cancellationToken).ConfigureAwait(false);
        var immutablePlan = validation.Plan with
        {
            Actions = Array.AsReadOnly(validation.Plan.Actions
                .Where(action => action.ApprovalState == ChangeApprovalState.Approved)
                .OrderBy(action => action.ExecutionOrder)
                .ThenBy(action => action.ActionId, StringComparer.Ordinal)
                .ToArray()),
        };
        var operation = NewOperation(immutablePlan, initiatingFeature);
        try
        {
            await PersistOperationAsync(operation).ConfigureAwait(false);
        }
        catch (JournalPersistenceException)
        {
            operation = operation with
            {
                Status = OperationStatus.Failed,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Summary = "Apply did not start because the Operation Journal could not be written. No file was changed.",
            };
            return new ChangePlanExecutionResult(operation, false, false, operation.Summary);
        }
        if (!validation.CanApply || immutablePlan.Actions.Count == 0)
        {
            operation = operation with
            {
                Status = OperationStatus.Failed,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Summary = "Apply was blocked because the approved plan did not pass immediate pre-execution validation.",
            };
            await PersistOperationAsync(operation).ConfigureAwait(false);
            var blockedPlan = validation.Plan with { Status = ChangePlanStatus.ValidationFailed };
            await _planStore.UpsertAsync(blockedPlan, CancellationToken.None).ConfigureAwait(false);
            return new ChangePlanExecutionResult(operation, false, false, operation.Summary);
        }

        operation = operation with { Status = OperationStatus.Running };
        try
        {
            await PersistOperationAsync(operation).ConfigureAwait(false);
        }
        catch (JournalPersistenceException)
        {
            operation = operation with
            {
                Status = OperationStatus.Failed,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Summary = "Apply did not start because the Operation Journal could not enter Running state. No file was changed.",
            };
            return new ChangePlanExecutionResult(operation, false, false, operation.Summary);
        }
        await _planStore.UpsertAsync(
            validation.Plan with { Status = ChangePlanStatus.Applying },
            CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation(
            "Operation {OperationId} started for Change Plan {PlanId} with {ActionCount} approved action(s).",
            operation.OperationId,
            immutablePlan.PlanId,
            immutablePlan.Actions.Count);
        Report(progress, operation, null, "Applying approved and revalidated actions.");

        var completedActionIds = new List<string>();
        var cancellationRequested = false;
        var blockingFailure = false;
        for (var index = 0; index < immutablePlan.Actions.Count; index++)
        {
            var action = immutablePlan.Actions[index];
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationRequested = true;
                break;
            }

            Report(progress, operation, action.ActionId, $"Applying {action.ActionType}.");
            try
            {
                var outcome = await ExecuteOneAsync(
                    operation.OperationId,
                    action,
                    async intermediate =>
                    {
                        operation = ReplaceAction(operation, intermediate);
                        await PersistOperationAsync(operation).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
                operation = ReplaceAction(operation, outcome);
                if (outcome.ExecutionResult == JournalActionResult.Succeeded)
                {
                    completedActionIds.Add(action.ActionId);
                }
                await PersistOperationAsync(operation).ConfigureAwait(false);
                if (outcome.ExecutionResult is
                    JournalActionResult.Failed or
                    JournalActionResult.RolledBack or
                    JournalActionResult.RollbackFailed)
                {
                    blockingFailure = true;
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                cancellationRequested = true;
                break;
            }
            catch (JournalPersistenceException exception)
            {
                return await FinishAfterJournalFailureAsync(
                    validation.Plan,
                    operation,
                    completedActionIds,
                    progress,
                    exception).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsFilesystemFailure(exception))
            {
                _logger.LogWarning(
                    exception,
                    "Change Plan action {ActionId} failed with {ExceptionType}.",
                    action.ActionId,
                    exception.GetType().Name);
                var currentAction = Action(operation, action.ActionId);
                operation = ReplaceAction(
                    operation,
                    currentAction with
                    {
                        ActualResultingPath = exception is IntermediateRecoveredException
                            ? currentAction.OriginalPath
                            : currentAction.ActualResultingPath,
                        ExecutionResult = exception switch
                        {
                            IntermediateRecoveredException => JournalActionResult.RolledBack,
                            IntermediateRecoveryFailedException => JournalActionResult.RollbackFailed,
                            _ => JournalActionResult.Failed,
                        },
                        ErrorCategory = CategoryFor(exception),
                        ErrorDetails = SafeError(exception),
                        RollbackAttempted = exception is
                            IntermediateRecoveredException or
                            IntermediateRecoveryFailedException,
                        RollbackResult = exception switch
                        {
                            IntermediateRecoveredException => JournalRollbackResult.Succeeded,
                            IntermediateRecoveryFailedException => JournalRollbackResult.Failed,
                            _ => currentAction.RollbackResult,
                        },
                        UndoAvailable = false,
                        UndoStatus = JournalUndoStatus.NotAvailable,
                    });
                try
                {
                    await PersistOperationAsync(operation).ConfigureAwait(false);
                }
                catch (JournalPersistenceException journalException)
                {
                    return await FinishAfterJournalFailureAsync(
                        validation.Plan,
                        operation,
                        completedActionIds,
                        progress,
                        journalException).ConfigureAwait(false);
                }
                blockingFailure = true;
                break;
            }
        }

        if (cancellationRequested || blockingFailure)
        {
            operation = operation with { CancellationRequested = cancellationRequested };
            operation = MarkUnattemptedSkipped(operation);
            await PersistOperationAsync(operation).ConfigureAwait(false);
            var rollbackFailuresBefore = operation.Actions.Count(action =>
                action.RollbackResult == JournalRollbackResult.Failed);
            var rollback = await RollBackAsync(
                operation,
                completedActionIds,
                progress).ConfigureAwait(false);
            var rollbackFailureCount = rollbackFailuresBefore + rollback.Failures;
            operation = rollback.Operation with
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Status = rollbackFailureCount > 0
                    ? OperationStatus.RollbackPartiallyFailed
                    : completedActionIds.Count > 0
                        ? OperationStatus.RolledBack
                        : cancellationRequested
                            ? OperationStatus.Cancelled
                            : OperationStatus.Failed,
                Summary = rollbackFailureCount > 0
                    ? "Apply stopped and rollback was only partially successful. Review Operation Details before manual recovery."
                    : completedActionIds.Count > 0
                        ? "Apply stopped safely and completed actions were rolled back and verified."
                        : cancellationRequested
                            ? "Apply was cancelled before any filesystem action completed."
                            : "Apply failed before any filesystem action completed.",
            };
            await PersistOperationAsync(operation).ConfigureAwait(false);
            await _planStore.UpsertAsync(
                validation.Plan with
                {
                    Status = operation.Status == OperationStatus.RollbackPartiallyFailed
                        ? ChangePlanStatus.PartiallyApplied
                        : ChangePlanStatus.Failed,
                },
                CancellationToken.None).ConfigureAwait(false);
            _logger.LogWarning(
                "Operation {OperationId} ended with {Status}; succeeded={Succeeded}, failed={Failed}, rolledBack={RolledBack}.",
                operation.OperationId,
                operation.Status,
                operation.SucceededCount,
                operation.FailedCount,
                operation.RolledBackCount);
            Report(progress, operation, null, operation.Summary);
            return new ChangePlanExecutionResult(
                operation,
                false,
                cancellationRequested,
                operation.Summary);
        }

        operation = operation with
        {
            Status = OperationStatus.Succeeded,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Summary = $"Applied and verified {operation.Actions.Count} approved action(s). Undo is available while the resulting files remain unchanged.",
        };
        await PersistOperationAsync(operation).ConfigureAwait(false);
        await _planStore.UpsertAsync(
            validation.Plan with { Status = ChangePlanStatus.Applied },
            CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation(
            "Operation {OperationId} succeeded with {ActionCount} verified action(s).",
            operation.OperationId,
            operation.Actions.Count);
        Report(progress, operation, null, operation.Summary);
        return new ChangePlanExecutionResult(operation, true, false, operation.Summary);
    }

    /// <inheritdoc />
    public async Task<ChangePlanUndoResult> UndoAsync(
        string operationId,
        IReadOnlyCollection<string>? actionIds,
        IProgress<ChangeExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("An operation identity is required.", nameof(operationId));
        }

        var operation = await _journal.GetAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException("The requested Operation Journal record was not found.", nameof(operationId));
        _logger.LogInformation(
            "Undo validation started for operation {OperationId}; selectedActionCount={SelectedActionCount}.",
            operationId,
            actionIds?.Count ?? operation.Actions.Count);
        var requested = actionIds is null
            ? null
            : actionIds.ToHashSet(StringComparer.Ordinal);
        if (requested is not null &&
            requested.Any(id => operation.Actions.All(action => action.ActionId != id)))
        {
            throw new ArgumentException("The undo request contains an unknown action identity.", nameof(actionIds));
        }

        var laterOperations = (await _journal.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(candidate =>
                candidate.StartedAtUtc > operation.StartedAtUtc &&
                candidate.Status is OperationStatus.Succeeded or OperationStatus.PartiallySucceeded)
            .ToArray();
        var undone = 0;
        var blocked = 0;
        var failed = 0;
        var cancellationRequested = false;
        foreach (var action in operation.Actions.Reverse())
        {
            if (requested is not null && !requested.Contains(action.ActionId) ||
                !action.UndoAvailable ||
                action.UndoStatus == JournalUndoStatus.Succeeded)
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                cancellationRequested = true;
                break;
            }

            Report(progress, operation, action.ActionId, $"Validating Undo for {action.ActionType}.");
            var dependency = FindDependency(action, laterOperations);
            if (dependency is not null)
            {
                operation = ReplaceAction(
                    operation,
                    action with
                    {
                        UndoStatus = JournalUndoStatus.Blocked,
                        UndoTimestampUtc = DateTimeOffset.UtcNow,
                        UndoConflictDetails = dependency,
                    });
                blocked++;
                await PersistOperationAsync(operation).ConfigureAwait(false);
                continue;
            }

            try
            {
                var reversed = await UndoOneAsync(action, cancellationToken).ConfigureAwait(false);
                operation = ReplaceAction(operation, reversed);
                undone++;
            }
            catch (UndoBlockedException exception)
            {
                operation = ReplaceAction(
                    operation,
                    action with
                    {
                        UndoStatus = JournalUndoStatus.Blocked,
                        UndoTimestampUtc = DateTimeOffset.UtcNow,
                        UndoConflictDetails = exception.Message,
                    });
                blocked++;
            }
            catch (Exception exception) when (IsFilesystemFailure(exception))
            {
                _logger.LogWarning(
                    exception,
                    "Undo for action {ActionId} failed with {ExceptionType}.",
                    action.ActionId,
                    exception.GetType().Name);
                operation = ReplaceAction(
                    operation,
                    action with
                    {
                        UndoStatus = JournalUndoStatus.Failed,
                        UndoTimestampUtc = DateTimeOffset.UtcNow,
                        UndoConflictDetails = SafeError(exception),
                    });
                failed++;
            }
            catch (OperationCanceledException)
            {
                cancellationRequested = true;
                break;
            }

            await PersistOperationAsync(operation).ConfigureAwait(false);
        }

        var remainingAvailable = operation.Actions.Any(action =>
            action.UndoAvailable &&
            action.UndoStatus == JournalUndoStatus.Available);
        var allUndoableSucceeded = operation.Actions
            .Where(action => action.UndoAvailable || action.UndoStatus == JournalUndoStatus.Succeeded)
            .All(action => action.UndoStatus == JournalUndoStatus.Succeeded);
        var status = cancellationRequested && undone > 0
            ? OperationStatus.UndoPartiallyCompleted
            : allUndoableSucceeded
            ? OperationStatus.Undone
            : undone > 0
                ? OperationStatus.UndoPartiallyCompleted
                : blocked > 0
                    ? OperationStatus.UndoBlockedByConflicts
                    : operation.Status;
        var summary = status switch
        {
            OperationStatus.Undone => "Undo restored and verified every supported action.",
            OperationStatus.UndoPartiallyCompleted =>
                $"Undo restored {undone} action(s); {blocked} were blocked and {failed} failed.",
            OperationStatus.UndoBlockedByConflicts =>
                $"Undo was blocked for {blocked} action(s) because the current filesystem state is no longer safe.",
            _ when cancellationRequested =>
                $"Undo cancellation was observed at a safe action boundary after restoring {undone} action(s).",
            _ when remainingAvailable => "No requested action was undone; other safe Undo actions remain available.",
            _ => "No supported Undo action was available.",
        };
        operation = operation with
        {
            Status = status,
            CancellationRequested = operation.CancellationRequested || cancellationRequested,
            CompletedAtUtc = operation.CompletedAtUtc ?? DateTimeOffset.UtcNow,
            Summary = summary,
        };
        await PersistOperationAsync(operation).ConfigureAwait(false);
        _logger.LogInformation(
            "Undo for operation {OperationId} ended with {Status}; undone={Undone}, blocked={Blocked}, failed={Failed}.",
            operation.OperationId,
            operation.Status,
            undone,
            blocked,
            failed);
        Report(progress, operation, null, summary);
        return new ChangePlanUndoResult(operation, undone, blocked, failed, summary);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationJournalRecord>> RecoverInterruptedAsync(
        CancellationToken cancellationToken)
    {
        var operations = await _journal.ListAsync(cancellationToken).ConfigureAwait(false);
        var recovered = new List<OperationJournalRecord>();
        foreach (var operation in operations.Where(candidate =>
                     candidate.Status is OperationStatus.Pending or OperationStatus.Running))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var updated = operation;
            foreach (var action in updated.Actions.Where(action =>
                         action.ExecutionResult == JournalActionResult.Pending).ToArray())
            {
                var inspected = await InspectInterruptedActionAsync(action, cancellationToken)
                    .ConfigureAwait(false);
                updated = ReplaceAction(updated, inspected);
            }

            updated = updated with
            {
                Status = OperationStatus.Interrupted,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Summary = "OpenSorSe found an interrupted operation and inspected the current paths. Review Operation Details before recovery or Undo.",
            };
            await PersistOperationAsync(updated).ConfigureAwait(false);
            _logger.LogWarning(
                "Operation {OperationId} was recovered as Interrupted after inspecting {ActionCount} action(s).",
                updated.OperationId,
                updated.Actions.Count);
            recovered.Add(updated);
        }

        return Array.AsReadOnly(recovered.ToArray());
    }

    private async Task<OperationJournalAction> ExecuteOneAsync(
        string operationId,
        ProposedChangeAction action,
        Func<OperationJournalAction, Task> persistIntermediateAsync,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var journalAction = new OperationJournalAction(
            action.ActionId,
            action.ActionType,
            action.SuggestionSource,
            action.SourcePath,
            action.DestinationPath,
            null,
            action.SourceIdentity,
            null,
            action.ValidationState,
            JournalActionResult.Pending,
            false,
            ChangeConflictCategory.None,
            null,
            action.Warnings,
            false,
            JournalRollbackResult.NotRequired,
            false,
            JournalUndoStatus.NotAvailable,
            null,
            null,
            action.AiModel,
            action.AiRequestCorrelationId,
            false);
        if (action.ActionType == ChangeActionType.CreateDirectory)
        {
            if (_fileSystem.DirectoryExists(action.DestinationPath))
            {
                return journalAction with
                {
                    ActualResultingPath = action.DestinationPath,
                    ExecutionResult = JournalActionResult.Skipped,
                    WasSkipped = true,
                    WarningDetails = Array.AsReadOnly(
                        action.Warnings.Append("The destination directory already existed.").ToArray()),
                };
            }

            _fileSystem.CreateDirectory(action.DestinationPath);
            if (!_fileSystem.DirectoryExists(action.DestinationPath))
            {
                throw new VerificationException("The created directory could not be verified.");
            }

            return journalAction with
            {
                ActualResultingPath = action.DestinationPath,
                ExecutionResult = JournalActionResult.Succeeded,
                UndoAvailable = true,
                UndoStatus = JournalUndoStatus.Available,
                DirectoryCreatedByOpenSorSe = true,
            };
        }

        var source = action.SourcePath
            ?? throw new InvalidDataException("The approved file action has no source path.");
        var preIdentity = await _fileSystem.CaptureFileIdentityAsync(
            source,
            includeHash: action.SourceIdentity?.ContentHash is not null,
            cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The approved source file is unavailable.", source);
        if (action.SourceIdentity is not null &&
            !ChangePlanValidator.SameIdentity(action.SourceIdentity, preIdentity))
        {
            throw new SourceChangedException("The source changed immediately before its filesystem action.");
        }

        if (IsCaseOnlyRename(source, action.DestinationPath))
        {
            var temporary = TemporaryRenamePath(source, operationId, action.ActionId);
            journalAction = journalAction with
            {
                PreExecutionIdentity = preIdentity,
                ActualResultingPath = temporary,
                WarningDetails = Array.AsReadOnly(
                    journalAction.WarningDetails
                        .Append("A safe temporary case-rename path was prepared and journalled.")
                        .ToArray()),
            };
            await persistIntermediateAsync(journalAction).ConfigureAwait(false);
            MoveCaseOnly(source, action.DestinationPath, operationId, action.ActionId);
        }
        else
        {
            _fileSystem.MoveFile(source, action.DestinationPath);
        }

        var postIdentity = await _fileSystem.CaptureFileIdentityAsync(
            action.DestinationPath,
            includeHash: preIdentity.ContentHash is not null,
            CancellationToken.None).ConfigureAwait(false);
        if (postIdentity is null ||
            !ChangePlanValidator.SameIdentity(preIdentity, postIdentity) ||
            !IsCaseOnlyRename(source, action.DestinationPath) && _fileSystem.FileExists(source))
        {
            return await RecoverFailedVerificationAsync(
                journalAction,
                source,
                action.DestinationPath,
                preIdentity,
                postIdentity).ConfigureAwait(false);
        }

        return journalAction with
        {
            PreExecutionIdentity = preIdentity,
            PostExecutionIdentity = postIdentity,
            ActualResultingPath = action.DestinationPath,
            ExecutionResult = JournalActionResult.Succeeded,
            UndoAvailable = true,
            UndoStatus = JournalUndoStatus.Available,
        };
    }

    private async Task<OperationJournalAction> RecoverFailedVerificationAsync(
        OperationJournalAction action,
        string original,
        string intendedDestination,
        FileIdentitySnapshot preIdentity,
        FileIdentitySnapshot? postIdentity)
    {
        var destinationExists = _fileSystem.FileExists(intendedDestination);
        var originalExists = _fileSystem.FileExists(original);
        if (!destinationExists && originalExists)
        {
            return action with
            {
                PreExecutionIdentity = preIdentity,
                PostExecutionIdentity = postIdentity,
                ExecutionResult = JournalActionResult.Failed,
                ErrorCategory = ChangeConflictCategory.VerificationFailed,
                ErrorDetails = "The filesystem action did not produce the intended destination; the original file remains in place.",
            };
        }

        if (destinationExists && !originalExists)
        {
            try
            {
                _fileSystem.MoveFile(intendedDestination, original);
                var restored = await _fileSystem.CaptureFileIdentityAsync(
                    original,
                    includeHash: preIdentity.ContentHash is not null,
                    CancellationToken.None).ConfigureAwait(false);
                if (restored is null || !ChangePlanValidator.SameIdentity(preIdentity, restored))
                {
                    throw new VerificationException("The immediate recovery could not verify the original file.");
                }

                return action with
                {
                    PreExecutionIdentity = preIdentity,
                    PostExecutionIdentity = postIdentity,
                    ActualResultingPath = original,
                    ExecutionResult = JournalActionResult.RolledBack,
                    ErrorCategory = ChangeConflictCategory.VerificationFailed,
                    ErrorDetails = "Result verification failed; the action was immediately rolled back and the original was verified.",
                    RollbackAttempted = true,
                    RollbackResult = JournalRollbackResult.Succeeded,
                };
            }
            catch (Exception exception) when (IsFilesystemFailure(exception))
            {
                return action with
                {
                    PreExecutionIdentity = preIdentity,
                    PostExecutionIdentity = postIdentity,
                    ActualResultingPath = _fileSystem.FileExists(intendedDestination)
                        ? intendedDestination
                        : null,
                    ExecutionResult = JournalActionResult.RollbackFailed,
                    ErrorCategory = ChangeConflictCategory.VerificationFailed,
                    ErrorDetails = "Result verification failed and the original state could not be restored automatically.",
                    RollbackAttempted = true,
                    RollbackResult = JournalRollbackResult.Failed,
                    WarningDetails = Array.AsReadOnly(
                        action.WarningDetails.Append(SafeError(exception)).ToArray()),
                };
            }
        }

        return action with
        {
            PreExecutionIdentity = preIdentity,
            PostExecutionIdentity = postIdentity,
            ActualResultingPath = destinationExists ? intendedDestination : null,
            ExecutionResult = JournalActionResult.RollbackFailed,
            ErrorCategory = ChangeConflictCategory.VerificationFailed,
            ErrorDetails = "Result verification found an ambiguous filesystem state. Manual recovery is required.",
            RollbackAttempted = true,
            RollbackResult = JournalRollbackResult.Failed,
        };
    }

    private async Task<(OperationJournalRecord Operation, int Failures)> RollBackAsync(
        OperationJournalRecord operation,
        IReadOnlyCollection<string> completedActionIds,
        IProgress<ChangeExecutionProgress>? progress,
        bool persistTransitions = true)
    {
        var completed = completedActionIds.ToHashSet(StringComparer.Ordinal);
        var failures = 0;
        foreach (var action in operation.Actions.Reverse().Where(action => completed.Contains(action.ActionId)))
        {
            progress?.Report(new ChangeExecutionProgress(
                OperationStatus.Running,
                operation.Actions.Count,
                operation.Actions.Count(candidate => candidate.ExecutionResult != JournalActionResult.Pending),
                operation.SucceededCount,
                operation.FailedCount,
                action.ActionId,
                "Rolling back a completed action."));
            var rollingBack = action with
            {
                RollbackAttempted = true,
                RollbackResult = JournalRollbackResult.Pending,
            };
            operation = ReplaceAction(operation, rollingBack);
            if (persistTransitions)
            {
                await PersistOperationAsync(operation).ConfigureAwait(false);
            }
            try
            {
                if (action.ActionType == ChangeActionType.CreateDirectory)
                {
                    if (action.DirectoryCreatedByOpenSorSe && _fileSystem.DirectoryExists(action.IntendedDestinationPath))
                    {
                        _fileSystem.DeleteDirectory(action.IntendedDestinationPath);
                    }
                }
                else
                {
                    var original = action.OriginalPath
                        ?? throw new InvalidDataException("The rollback record has no original path.");
                    var result = action.ActualResultingPath ?? action.IntendedDestinationPath;
                    var caseOnly = IsCaseOnlyRename(original, result);
                    if (!_fileSystem.FileExists(result) ||
                        !caseOnly && _fileSystem.FileExists(original) ||
                        _fileSystem.DirectoryExists(original))
                    {
                        throw new IOException("The rollback paths are no longer safe.");
                    }

                    if (caseOnly)
                    {
                        MoveCaseOnly(
                            result,
                            original,
                            operation.OperationId,
                            $"{action.ActionId}:rollback");
                    }
                    else
                    {
                        _fileSystem.MoveFile(result, original);
                    }
                    var restored = await _fileSystem.CaptureFileIdentityAsync(
                        original,
                        includeHash: action.PreExecutionIdentity?.ContentHash is not null,
                        CancellationToken.None).ConfigureAwait(false);
                    if (restored is null ||
                        action.PreExecutionIdentity is not null &&
                        !ChangePlanValidator.SameIdentity(action.PreExecutionIdentity, restored))
                    {
                        throw new VerificationException("The restored source could not be verified.");
                    }
                }

                operation = ReplaceAction(
                    operation,
                    rollingBack with
                    {
                        ExecutionResult = JournalActionResult.RolledBack,
                        RollbackResult = JournalRollbackResult.Succeeded,
                        UndoAvailable = false,
                        UndoStatus = JournalUndoStatus.NotAvailable,
                    });
            }
            catch (Exception exception) when (IsFilesystemFailure(exception))
            {
                failures++;
                _logger.LogError(
                    exception,
                    "Rollback for action {ActionId} could not be verified.",
                    action.ActionId);
                operation = ReplaceAction(
                    operation,
                    rollingBack with
                    {
                        ExecutionResult = JournalActionResult.RollbackFailed,
                        RollbackResult = JournalRollbackResult.Failed,
                        ErrorCategory = CategoryFor(exception),
                        ErrorDetails = SafeError(exception),
                        UndoAvailable = false,
                        UndoStatus = JournalUndoStatus.NotAvailable,
                    });
            }

            if (persistTransitions)
            {
                await PersistOperationAsync(operation).ConfigureAwait(false);
            }
        }

        return (operation, failures);
    }

    private async Task<ChangePlanExecutionResult> FinishAfterJournalFailureAsync(
        ChangePlan validatedPlan,
        OperationJournalRecord operation,
        IReadOnlyCollection<string> completedActionIds,
        IProgress<ChangeExecutionProgress>? progress,
        JournalPersistenceException journalException)
    {
        _logger.LogError(
            journalException,
            "The Operation Journal became unavailable during {OperationId}; emergency rollback is starting.",
            operation.OperationId);
        operation = MarkUnattemptedSkipped(operation);
        var rollback = await RollBackAsync(
            operation,
            completedActionIds,
            progress,
            persistTransitions: false).ConfigureAwait(false);
        var rollbackFailures = rollback.Operation.Actions.Count(action =>
            action.RollbackResult == JournalRollbackResult.Failed);
        operation = rollback.Operation with
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Status = rollbackFailures > 0
                ? OperationStatus.RollbackPartiallyFailed
                : completedActionIds.Count > 0
                    ? OperationStatus.RolledBack
                    : OperationStatus.Failed,
            Summary = rollbackFailures > 0
                ? "The Operation Journal became unavailable and emergency rollback was only partially successful. Inspect the listed paths manually."
                : completedActionIds.Count > 0
                    ? "The Operation Journal became unavailable; completed actions were immediately rolled back and verified."
                    : "The Operation Journal became unavailable before a file action completed. No file was changed.",
        };
        try
        {
            await PersistOperationAsync(operation).ConfigureAwait(false);
        }
        catch (JournalPersistenceException)
        {
            // The earlier durable Pending/Running record remains available for startup inspection.
        }

        try
        {
            await _planStore.UpsertAsync(
                validatedPlan with
                {
                    Status = rollbackFailures > 0
                        ? ChangePlanStatus.PartiallyApplied
                        : ChangePlanStatus.Failed,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFilesystemFailure(exception))
        {
            _logger.LogWarning(
                exception,
                "The Change Plan status could not be persisted after journal failure for {OperationId}.",
                operation.OperationId);
        }

        Report(progress, operation, null, operation.Summary);
        return new ChangePlanExecutionResult(operation, false, false, operation.Summary);
    }

    private async Task<OperationJournalAction> UndoOneAsync(
        OperationJournalAction action,
        CancellationToken cancellationToken)
    {
        if (action.ActionType == ChangeActionType.CreateDirectory)
        {
            if (!action.DirectoryCreatedByOpenSorSe)
            {
                throw new UndoBlockedException("OpenSorSe did not create this directory.");
            }

            if (!_fileSystem.DirectoryExists(action.IntendedDestinationPath))
            {
                throw new UndoBlockedException("The created directory no longer exists.");
            }

            if (!_fileSystem.IsDirectoryEmpty(action.IntendedDestinationPath))
            {
                throw new UndoBlockedException("The created directory is no longer empty.");
            }

            _fileSystem.DeleteDirectory(action.IntendedDestinationPath);
            if (_fileSystem.DirectoryExists(action.IntendedDestinationPath))
            {
                throw new VerificationException("Directory removal could not be verified.");
            }

            return action with
            {
                UndoAvailable = false,
                UndoStatus = JournalUndoStatus.Succeeded,
                UndoTimestampUtc = DateTimeOffset.UtcNow,
                UndoConflictDetails = null,
            };
        }

        var result = action.ActualResultingPath ?? action.IntendedDestinationPath;
        var original = action.OriginalPath
            ?? throw new UndoBlockedException("The original path was not recorded.");
        var caseOnly = IsCaseOnlyRename(original, result);
        if (!_fileSystem.FileExists(result))
        {
            throw new UndoBlockedException("The resulting file no longer exists.");
        }

        if (!caseOnly && (_fileSystem.FileExists(original) || _fileSystem.DirectoryExists(original)))
        {
            throw new UndoBlockedException("The original path is now occupied. Undo will not overwrite it.");
        }

        var current = await _fileSystem.CaptureFileIdentityAsync(
            result,
            includeHash: action.PostExecutionIdentity?.ContentHash is not null,
            cancellationToken).ConfigureAwait(false);
        if (current is null ||
            action.PostExecutionIdentity is not null &&
            !ChangePlanValidator.SameIdentity(action.PostExecutionIdentity, current))
        {
            throw new UndoBlockedException("The resulting file was replaced or materially modified.");
        }

        if (caseOnly)
        {
            MoveCaseOnly(result, original, "undo", action.ActionId);
        }
        else
        {
            _fileSystem.MoveFile(result, original);
        }
        var restored = await _fileSystem.CaptureFileIdentityAsync(
            original,
            includeHash: action.PreExecutionIdentity?.ContentHash is not null,
            CancellationToken.None).ConfigureAwait(false);
        if (restored is null ||
            action.PreExecutionIdentity is not null &&
            !ChangePlanValidator.SameIdentity(action.PreExecutionIdentity, restored))
        {
            throw new VerificationException("The restored file could not be verified.");
        }

        return action with
        {
            UndoAvailable = false,
            UndoStatus = JournalUndoStatus.Succeeded,
            UndoTimestampUtc = DateTimeOffset.UtcNow,
            UndoConflictDetails = null,
        };
    }

    private async Task<OperationJournalAction> InspectInterruptedActionAsync(
        OperationJournalAction action,
        CancellationToken cancellationToken)
    {
        if (action.ActionType == ChangeActionType.CreateDirectory)
        {
            return action with
            {
                ExecutionResult = _fileSystem.DirectoryExists(action.IntendedDestinationPath)
                    ? JournalActionResult.Failed
                    : JournalActionResult.Skipped,
                ErrorCategory = _fileSystem.DirectoryExists(action.IntendedDestinationPath)
                    ? ChangeConflictCategory.InterruptedStateAmbiguous
                    : ChangeConflictCategory.None,
                ErrorDetails = _fileSystem.DirectoryExists(action.IntendedDestinationPath)
                    ? "The directory exists, but ownership cannot be inferred after interruption."
                    : null,
            };
        }

        var originalExists = action.OriginalPath is not null && _fileSystem.FileExists(action.OriginalPath);
        var candidatePaths = new[] { action.ActualResultingPath, action.IntendedDestinationPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(ChangePlanFactory.PathComparer)
            .ToArray();
        var existingResults = candidatePaths.Where(_fileSystem.FileExists).ToArray();
        if (originalExists && existingResults.Length == 0)
        {
            return action with
            {
                ActualResultingPath = null,
                ExecutionResult = JournalActionResult.Skipped,
                WasSkipped = true,
            };
        }

        if (!originalExists && existingResults.Length == 1)
        {
            var resultingPath = existingResults[0];
            var resultIdentity = await _fileSystem.CaptureFileIdentityAsync(
                resultingPath,
                includeHash: action.PreExecutionIdentity?.ContentHash is not null,
                cancellationToken).ConfigureAwait(false);
            if (resultIdentity is not null &&
                action.PreExecutionIdentity is not null &&
                ChangePlanValidator.SameIdentity(action.PreExecutionIdentity, resultIdentity))
            {
                return action with
                {
                    ActualResultingPath = resultingPath,
                    PostExecutionIdentity = resultIdentity,
                    ExecutionResult = JournalActionResult.Succeeded,
                    ErrorCategory = ChangePlanFactory.PathComparer.Equals(
                        resultingPath,
                        action.IntendedDestinationPath)
                        ? ChangeConflictCategory.None
                        : ChangeConflictCategory.InterruptedStateAmbiguous,
                    ErrorDetails = ChangePlanFactory.PathComparer.Equals(
                        resultingPath,
                        action.IntendedDestinationPath)
                        ? null
                        : "The file is verified at a journalled temporary rename path and can be safely restored with Undo.",
                    UndoAvailable = true,
                    UndoStatus = JournalUndoStatus.Available,
                };
            }
        }

        return action with
        {
            ExecutionResult = JournalActionResult.Failed,
            ErrorCategory = ChangeConflictCategory.InterruptedStateAmbiguous,
            ErrorDetails = "The filesystem state after interruption is ambiguous and requires manual review.",
        };
    }

    private static OperationJournalRecord NewOperation(ChangePlan plan, string initiatingFeature)
    {
        var actions = plan.Actions.Select(action => new OperationJournalAction(
            action.ActionId,
            action.ActionType,
            action.SuggestionSource,
            action.SourcePath,
            action.DestinationPath,
            null,
            action.SourceIdentity,
            null,
            action.ValidationState,
            JournalActionResult.Pending,
            false,
            ChangeConflictCategory.None,
            null,
            action.Warnings,
            false,
            JournalRollbackResult.NotRequired,
            false,
            JournalUndoStatus.NotAvailable,
            null,
            null,
            action.AiModel,
            action.AiRequestCorrelationId,
            false)).ToArray();
        return new OperationJournalRecord(
            OperationJournalSchema.CurrentVersion,
            $"operation:{Guid.NewGuid():N}",
            plan.PlanId,
            DateTimeOffset.UtcNow,
            null,
            VersionText(),
            OperationStatus.Pending,
            initiatingFeature.Trim(),
            plan.RootPath,
            Array.AsReadOnly(actions),
            false,
            "Execution is pending.");
    }

    private static string VersionText()
    {
        var version = typeof(ChangePlanExecutionService).Assembly.GetName().Version;
        return version is null ? "1.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static OperationJournalRecord ReplaceAction(
        OperationJournalRecord operation,
        OperationJournalAction replacement) =>
        operation with
        {
            Actions = Array.AsReadOnly(operation.Actions
                .Select(action => action.ActionId == replacement.ActionId ? replacement : action)
                .ToArray()),
        };

    private static OperationJournalAction Action(OperationJournalRecord operation, string actionId) =>
        operation.Actions.Single(action => action.ActionId == actionId);

    private static OperationJournalRecord MarkUnattemptedSkipped(OperationJournalRecord operation) =>
        operation with
        {
            Actions = Array.AsReadOnly(operation.Actions.Select(action =>
                action.ExecutionResult == JournalActionResult.Pending
                    ? action with
                    {
                        ActualResultingPath = null,
                        ExecutionResult = JournalActionResult.Skipped,
                        WasSkipped = true,
                        ErrorDetails = "The action was not attempted after execution stopped at a safe boundary.",
                    }
                    : action).ToArray()),
        };

    private static bool IsCaseOnlyRename(string source, string destination) =>
        !PlatformServices.CurrentPathSemantics.IsCaseSensitive &&
        PlatformServices.CurrentPathSemantics.IsCaseOnlyDifference(source, destination);

    private static string TemporaryRenamePath(string source, string operationId, string actionId)
    {
        var directory = Path.GetDirectoryName(source)
            ?? throw new InvalidDataException("The rename source has no parent directory.");
        var token = $"{operationId}:{actionId}".GetHashCode(StringComparison.Ordinal).ToString("x8");
        return Path.Combine(directory, $".opensorse-{token}.tmp");
    }

    private void MoveCaseOnly(
        string source,
        string destination,
        string operationId,
        string actionId)
    {
        var temporary = TemporaryRenamePath(source, operationId, actionId);
        if (_fileSystem.FileExists(temporary) || _fileSystem.DirectoryExists(temporary))
        {
            throw new IOException("The safe temporary rename path is occupied.");
        }

        _fileSystem.MoveFile(source, temporary);
        try
        {
            _fileSystem.MoveFile(temporary, destination);
        }
        catch
        {
            if (_fileSystem.FileExists(temporary) &&
                !_fileSystem.FileExists(source) &&
                !_fileSystem.DirectoryExists(source))
            {
                try
                {
                    _fileSystem.MoveFile(temporary, source);
                    if (!_fileSystem.FileExists(source) || _fileSystem.FileExists(temporary))
                    {
                        throw new IOException("The original case-only source could not be verified.");
                    }
                }
                catch (Exception exception) when (IsFilesystemFailure(exception))
                {
                    throw new IntermediateRecoveryFailedException(
                        "The final case-only rename failed and its temporary file could not be restored automatically.",
                        exception);
                }

                throw new IntermediateRecoveredException(
                    "The final case-only rename failed; the temporary file was restored and verified.");
            }

            throw;
        }
    }

    private static string? FindDependency(
        OperationJournalAction action,
        IReadOnlyCollection<OperationJournalRecord> laterOperations)
    {
        var result = action.ActualResultingPath ?? action.IntendedDestinationPath;
        foreach (var later in laterOperations)
        {
            if (later.Actions.Any(candidate =>
                    candidate.ExecutionResult == JournalActionResult.Succeeded &&
                    (PathEquals(candidate.OriginalPath, result) ||
                     PathEquals(candidate.IntendedDestinationPath, result))))
            {
                return $"A later OpenSorSe operation ({later.OperationId}) depends on this path.";
            }
        }

        return null;
    }

    private static bool PathEquals(string? left, string? right) =>
        left is not null &&
        right is not null &&
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            ChangePlanFactory.PathComparison);

    private async Task PersistOperationAsync(OperationJournalRecord operation)
    {
        try
        {
            await _journal.UpsertAsync(operation, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFilesystemFailure(exception))
        {
            throw new JournalPersistenceException(
                "The Operation Journal could not be updated durably.",
                exception);
        }
    }

    private static bool IsFilesystemFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException;

    private static ChangeConflictCategory CategoryFor(Exception exception) => exception switch
    {
        UnauthorizedAccessException => ChangeConflictCategory.PermissionDenied,
        VerificationException => ChangeConflictCategory.VerificationFailed,
        IntermediateRecoveredException => ChangeConflictCategory.VerificationFailed,
        IntermediateRecoveryFailedException => ChangeConflictCategory.VerificationFailed,
        SourceChangedException => ChangeConflictCategory.SourceChanged,
        FileNotFoundException => ChangeConflictCategory.SourceMissing,
        _ => ChangeConflictCategory.IoFailure,
    };

    private static string SafeError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Filesystem access was denied.",
        FileNotFoundException => "A required source file is unavailable.",
        SourceChangedException => exception.Message,
        VerificationException => exception.Message,
        IntermediateRecoveredException => exception.Message,
        IntermediateRecoveryFailedException => exception.Message,
        InvalidDataException => exception.Message,
        _ => "The filesystem operation could not be completed safely.",
    };

    private static void Report(
        IProgress<ChangeExecutionProgress>? progress,
        OperationJournalRecord operation,
        string? actionId,
        string message)
    {
        progress?.Report(new ChangeExecutionProgress(
            operation.Status,
            operation.Actions.Count,
            operation.Actions.Count(action => action.ExecutionResult != JournalActionResult.Pending),
            operation.SucceededCount,
            operation.FailedCount,
            actionId,
            message));
    }

    private sealed class VerificationException(string message) : IOException(message);
    private sealed class SourceChangedException(string message) : IOException(message);
    private sealed class UndoBlockedException(string message) : IOException(message);
    private sealed class IntermediateRecoveredException(string message) : IOException(message);
    private sealed class IntermediateRecoveryFailedException(string message, Exception innerException)
        : IOException(message, innerException);
    private sealed class JournalPersistenceException(string message, Exception innerException)
        : IOException(message, innerException);
}
