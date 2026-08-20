using OpenSorSe.Application.Models;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Application.ChangePlans;

/// <summary>Describes the verified post-operation location of one Change Plan action.</summary>
public sealed record ChangePlanPathOutcome(
    string ActionId,
    string? SourceFileId,
    ChangeActionType ActionType,
    ChangeSuggestionSource SuggestionSource,
    string? OriginalPath,
    string IntendedDestinationPath,
    string? CurrentPath,
    bool IsUndo,
    bool IsAmbiguous);

/// <summary>Contains one coherent projection and targeted-refresh request derived from journal truth.</summary>
public sealed record ChangePlanReconciliationResult(
    ResultsSnapshot? Snapshot,
    IReadOnlyList<ChangePlanPathOutcome> ActionOutcomes,
    IReadOnlyList<string> AffectedPaths,
    bool RequiresTargetedRefresh,
    string Summary);

/// <summary>Projects actual execution or Undo outcomes into the completed-scan review state.</summary>
public interface IChangePlanReconciliationService
{
    /// <summary>Reconciles a snapshot from verified journal outcomes and current local filesystem truth.</summary>
    ChangePlanReconciliationResult Reconcile(
        ResultsSnapshot? snapshot,
        ChangePlan plan,
        OperationJournalRecord operation,
        bool isUndo);

}

/// <summary>
/// Keeps Files, duplicate groups, planned actions, selection identity, and targeted indexing refresh inputs
/// aligned with what the executor actually left on disk.
/// </summary>
public sealed class ChangePlanReconciliationService : IChangePlanReconciliationService
{
    private static readonly StringComparer PathComparer =
        OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparer;

    /// <inheritdoc />
    public ChangePlanReconciliationResult Reconcile(
        ResultsSnapshot? snapshot,
        ChangePlan plan,
        OperationJournalRecord operation,
        bool isUndo)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(plan.PlanId, operation.SourcePlanId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The operation does not belong to the supplied Change Plan.", nameof(operation));
        }

        return ReconcileCore(snapshot, operation, isUndo);
    }

    /// <summary>
    /// Reconciles from durable journal facts when the shorter-retained source Change Plan is no longer available.
    /// </summary>
    internal ChangePlanReconciliationResult Reconcile(
        ResultsSnapshot? snapshot,
        OperationJournalRecord operation,
        bool isUndo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ReconcileCore(snapshot, operation, isUndo);
    }

    private static ChangePlanReconciliationResult ReconcileCore(
        ResultsSnapshot? snapshot,
        OperationJournalRecord operation,
        bool isUndo)
    {
        var outcomes = operation.Actions.Select(action =>
            ResolveOutcome(action, isUndo)).ToArray();
        var affectedPaths = outcomes
            .SelectMany(outcome => new[] { outcome.OriginalPath, outcome.IntendedDestinationPath, outcome.CurrentPath })
            .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(PathComparer)
            .ToArray();
        var requiresRefresh =
            operation.Status is not OperationStatus.Succeeded and
                not OperationStatus.RolledBack and
                not OperationStatus.Undone ||
            outcomes.Any(outcome =>
                outcome.IsAmbiguous ||
                outcome.CurrentPath is null ||
                outcome.IsUndo && outcome.SuggestionSource == ChangeSuggestionSource.DuplicateAnalysis);
        var reconciled = snapshot is null
            ? null
            : ApplySnapshot(snapshot, outcomes, operation.Actions);
        var summary = isUndo
            ? requiresRefresh
                ? "Undo reached a mixed filesystem state. Visible results were reconciled to verified paths and affected indexed roots will be refreshed."
                : "Undo results were reconciled with Files, Search, and duplicate review."
            : requiresRefresh
                ? "The Change Plan reached a mixed filesystem state. Visible results were reconciled to verified paths and affected indexed roots will be refreshed."
                : "The Change Plan result was reconciled with Files, Search, and duplicate review.";
        return new ChangePlanReconciliationResult(
            reconciled,
            Array.AsReadOnly(outcomes),
            Array.AsReadOnly(affectedPaths),
            requiresRefresh,
            summary);
    }

    private static ChangePlanPathOutcome ResolveOutcome(
        OperationJournalAction action,
        bool isUndo)
    {
        var original = action.OriginalPath;
        var destination = action.ActualResultingPath ?? action.IntendedDestinationPath;
        var originalExists = Exists(action.ActionType, original);
        var destinationExists = Exists(action.ActionType, destination);
        var samePath = original is not null && PathComparer.Equals(
            Path.GetFullPath(original),
            Path.GetFullPath(destination));
        string? preferred = isUndo
            ? action.UndoStatus == JournalUndoStatus.Succeeded ? original : destination
            : action.ExecutionResult switch
            {
                JournalActionResult.Succeeded => destination,
                JournalActionResult.RolledBack => original,
                JournalActionResult.RollbackFailed => destination,
                _ => original,
            };
        string? current;
        var ambiguous = false;
        if (samePath)
        {
            current = originalExists || destinationExists ? preferred : null;
        }
        else if (originalExists && destinationExists)
        {
            current = preferred;
            ambiguous = true;
        }
        else if (originalExists)
        {
            current = original;
            ambiguous = preferred is not null && !PathComparer.Equals(preferred, original);
        }
        else if (destinationExists)
        {
            current = destination;
            ambiguous = preferred is not null && !PathComparer.Equals(preferred, destination);
        }
        else
        {
            current = null;
            ambiguous = action.ActionType != ChangeActionType.CreateDirectory ||
                        action.ExecutionResult is JournalActionResult.Succeeded or JournalActionResult.RollbackFailed;
        }

        return new ChangePlanPathOutcome(
            action.ActionId,
            SourceFileId: null,
            action.ActionType,
            action.SuggestionSource,
            original,
            action.IntendedDestinationPath,
            current,
            isUndo,
            ambiguous);
    }

    private static ResultsSnapshot ApplySnapshot(
        ResultsSnapshot snapshot,
        ChangePlanPathOutcome[] outcomes,
        IReadOnlyList<OperationJournalAction> journalActions)
    {
        var files = snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var affectedFileIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < outcomes.Length; index++)
        {
            var outcome = outcomes[index];
            if (outcome.ActionType == ChangeActionType.CreateDirectory)
            {
                continue;
            }

            var journalAction = journalActions[index];
            var postOperationPath = journalAction.ActualResultingPath ?? journalAction.IntendedDestinationPath;
            var match = outcome.IsUndo
                ? files.Values.FirstOrDefault(file => PathComparer.Equals(file.FullPath, postOperationPath)) ??
                  files.Values.FirstOrDefault(file =>
                      outcome.OriginalPath is not null && PathComparer.Equals(file.FullPath, outcome.OriginalPath))
                : files.Values.FirstOrDefault(file =>
                    outcome.OriginalPath is not null && PathComparer.Equals(file.FullPath, outcome.OriginalPath));
            if (match is null)
            {
                continue;
            }

            outcomes[index] = outcome with { SourceFileId = match.Id };
            affectedFileIds.Add(match.Id);
            var movedToRecovery = !outcome.IsUndo &&
                                  outcome.SuggestionSource == ChangeSuggestionSource.DuplicateAnalysis &&
                                  outcome.CurrentPath is not null &&
                                  !PathComparer.Equals(outcome.CurrentPath, outcome.OriginalPath);
            if (outcome.CurrentPath is null || movedToRecovery)
            {
                files.Remove(match.Id);
                continue;
            }

            var current = Path.GetFullPath(outcome.CurrentPath);
            files[match.Id] = RefreshFile(match, current);
        }

        var duplicateGroups = snapshot.DuplicateGroups
            .Select(group =>
            {
                var members = group.MemberFileIds.Where(files.ContainsKey).ToArray();
                return group with
                {
                    MemberFileIds = Array.AsReadOnly(members),
                    MemberCount = members.Length,
                    PotentialReclaimableBytes = group.CommonFileSizeInBytes is { } size
                        ? size * Math.Max(0, members.Length - 1)
                        : null,
                };
            })
            .Where(group => group.MemberCount >= 2)
            .ToArray();
        var groupByFile = duplicateGroups
            .SelectMany(group => group.MemberFileIds.Select(id => (id, group.GroupId)))
            .ToDictionary(pair => pair.id, pair => pair.GroupId, StringComparer.Ordinal);
        var finalFiles = files.Values
            .Select(file => file with
            {
                DuplicateGroupId = groupByFile.GetValueOrDefault(file.Id),
                DuplicateStatus = groupByFile.ContainsKey(file.Id)
                    ? OpenSorSe.Scanner.Models.DuplicateStatus.Duplicate
                    : file.DuplicateStatus == OpenSorSe.Scanner.Models.DuplicateStatus.Unknown
                        ? OpenSorSe.Scanner.Models.DuplicateStatus.Unknown
                        : OpenSorSe.Scanner.Models.DuplicateStatus.Unique,
                HasPlannedOperation = file.HasPlannedOperation && !affectedFileIds.Contains(file.Id),
            })
            .OrderBy(file => file.FullPath, PathComparer)
            .ToArray();
        var planned = snapshot.PlannedOperations
            .Where(item => item.SourceFileId is null || !affectedFileIds.Contains(item.SourceFileId))
            .ToArray();
        var directories = snapshot.Directories.Select(directory => directory.FullPath)
            .Concat(finalFiles.Select(file => Path.GetDirectoryName(file.FullPath)))
            .Concat(outcomes.Where(outcome => outcome.ActionType == ChangeActionType.CreateDirectory)
                .Select(outcome => outcome.CurrentPath))
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(PathComparer)
            .OrderBy(path => path, PathComparer)
            .Select(path => new ResultDirectory(path, Path.GetFileName(path) is { Length: > 0 } name ? name : path))
            .ToArray();
        var duplicateFileCount = duplicateGroups.Sum(group => group.MemberCount);
        var statistics = snapshot.Statistics with
        {
            FilesDiscovered = finalFiles.LongLength,
            DirectoriesDiscovered = directories.LongLength,
            ExactDuplicateGroupCount = duplicateGroups.LongLength,
            ExactDuplicateFileCount = duplicateFileCount,
            PlannedOperationCount = planned.LongLength,
        };
        return snapshot with
        {
            ProjectedAtUtc = DateTimeOffset.UtcNow,
            Files = Array.AsReadOnly(finalFiles),
            Directories = Array.AsReadOnly(directories),
            DuplicateGroups = Array.AsReadOnly(duplicateGroups),
            PlannedOperations = Array.AsReadOnly(planned),
            Statistics = statistics,
        };
    }

    private static ResultFile RefreshFile(ResultFile file, string path)
    {
        try
        {
            var info = new FileInfo(path);
            return file with
            {
                FullPath = path,
                DisplayFileName = Path.GetFileName(path),
                NormalizedExtension = Path.GetExtension(path).ToLowerInvariant(),
                SizeInBytes = info.Exists ? info.Length : file.SizeInBytes,
                CreationTimeUtc = info.Exists ? info.CreationTimeUtc : file.CreationTimeUtc,
                LastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : file.LastWriteTimeUtc,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return file with
            {
                FullPath = path,
                DisplayFileName = Path.GetFileName(path),
                NormalizedExtension = Path.GetExtension(path).ToLowerInvariant(),
            };
        }
    }

    private static bool Exists(ChangeActionType actionType, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return false;
        }

        try
        {
            return actionType == ChangeActionType.CreateDirectory
                ? Directory.Exists(path)
                : File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
