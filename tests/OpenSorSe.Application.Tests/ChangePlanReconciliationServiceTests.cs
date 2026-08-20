using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Application.Models;
using OpenSorSe.Executor.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

#pragma warning disable CS1591

/// <summary>Verifies post-operation projections follow journal and filesystem truth instead of plan intent.</summary>
public sealed class ChangePlanReconciliationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "OmniSorSe.Reconciliation.Tests",
        Guid.NewGuid().ToString("N"));

    public ChangePlanReconciliationServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SuccessfulRename_PreservesLogicalIdentityAndRemovesStalePath()
    {
        var original = FileAt("old-name.txt");
        var destination = Path.Combine(_root, "new-name.txt");
        File.Move(original, destination);
        var context = Context(
            [FileResult("file:1", original, planned: true)],
            [Action("action:1", "file:1", original, destination)]);
        var journal = Journal(
            OperationStatus.Succeeded,
            [JournalAction(context.Plan.Actions[0], JournalActionResult.Succeeded, destination)]);

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            context.Plan,
            journal,
            isUndo: false);

        var file = Assert.Single(result.Snapshot!.Files);
        Assert.Equal("file:1", file.Id);
        Assert.Equal(destination, file.FullPath);
        Assert.False(file.HasPlannedOperation);
        Assert.DoesNotContain(result.Snapshot.Files, item => item.FullPath == original);
        Assert.False(result.RequiresTargetedRefresh);
    }

    [Fact]
    public void FolderRestructure_ProjectsEverySuccessfulResultingPath()
    {
        var first = FileAt("one.txt");
        var second = FileAt("two.txt");
        var folder = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(folder);
        var firstDestination = Path.Combine(folder, "one.txt");
        var secondDestination = Path.Combine(folder, "two.txt");
        File.Move(first, firstDestination);
        File.Move(second, secondDestination);
        var actions = new[]
        {
            Action("action:1", "file:1", first, firstDestination),
            Action("action:2", "file:2", second, secondDestination),
        };
        var context = Context(
            [FileResult("file:1", first), FileResult("file:2", second)],
            actions);
        var journal = Journal(
            OperationStatus.Succeeded,
            actions.Select(action => JournalAction(action, JournalActionResult.Succeeded, action.DestinationPath)).ToArray());

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            context.Plan,
            journal,
            isUndo: false);

        Assert.Equal([firstDestination, secondDestination], result.Snapshot!.Files.Select(file => file.FullPath));
        Assert.Contains(result.Snapshot.Directories, directory => directory.FullPath == folder);
        Assert.Equal(2, result.Snapshot.Files.Select(file => file.Id).Distinct().Count());
    }

    [Fact]
    public void RollbackSuccess_LeavesOriginalProjection()
    {
        var original = FileAt("source.txt");
        var destination = Path.Combine(_root, "destination.txt");
        var action = Action("action:1", "file:1", original, destination);
        var context = Context([FileResult("file:1", original)], [action]);
        var journal = Journal(
            OperationStatus.RolledBack,
            [JournalAction(action, JournalActionResult.RolledBack, destination, JournalRollbackResult.Succeeded)]);

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            context.Plan,
            journal,
            isUndo: false);

        Assert.Equal(original, Assert.Single(result.Snapshot!.Files).FullPath);
        Assert.False(result.RequiresTargetedRefresh);
    }

    [Fact]
    public void RollbackPartialFailure_ReflectsMixedDiskStateAndRequestsTargetedRefresh()
    {
        var first = FileAt("first.txt");
        var second = FileAt("second.txt");
        var firstDestination = Path.Combine(_root, "first-moved.txt");
        var secondDestination = Path.Combine(_root, "second-moved.txt");
        File.Move(first, firstDestination);
        var actions = new[]
        {
            Action("action:1", "file:1", first, firstDestination),
            Action("action:2", "file:2", second, secondDestination),
        };
        var context = Context(
            [FileResult("file:1", first), FileResult("file:2", second)],
            actions);
        var journal = Journal(
            OperationStatus.RollbackPartiallyFailed,
            [
                JournalAction(actions[0], JournalActionResult.RollbackFailed, firstDestination, JournalRollbackResult.Failed),
                JournalAction(actions[1], JournalActionResult.Failed, null),
            ]);

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            context.Plan,
            journal,
            isUndo: false);

        Assert.Equal(firstDestination, result.Snapshot!.Files.Single(file => file.Id == "file:1").FullPath);
        Assert.Equal(second, result.Snapshot.Files.Single(file => file.Id == "file:2").FullPath);
        Assert.True(result.RequiresTargetedRefresh);
        Assert.Contains(result.AffectedPaths, path => path == first);
        Assert.Contains(result.AffectedPaths, path => path == secondDestination);
    }

    [Fact]
    public void Undo_UsesSameContractAndRestoresOriginalPath()
    {
        var original = FileAt("restored.txt");
        var destination = Path.Combine(_root, "post-operation.txt");
        var action = Action("action:1", "file:1", original, destination);
        var context = Context([FileResult("file:1", destination)], [action]);
        var journalAction = JournalAction(action, JournalActionResult.Succeeded, destination) with
        {
            UndoStatus = JournalUndoStatus.Succeeded,
            UndoTimestampUtc = DateTimeOffset.UtcNow,
            UndoAvailable = false,
        };
        var journal = Journal(OperationStatus.Undone, [journalAction]);

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            context.Plan,
            journal,
            isUndo: true);

        var file = Assert.Single(result.Snapshot!.Files);
        Assert.Equal("file:1", file.Id);
        Assert.Equal(original, file.FullPath);
        Assert.False(result.RequiresTargetedRefresh);
    }

    [Fact]
    public void JournalOnlyUndo_PreservesLogicalIdentityWhenSourcePlanWasPruned()
    {
        var original = FileAt("journal-restored.txt");
        var destination = Path.Combine(_root, "journal-post-operation.txt");
        var action = Action("action:journal-only", "filesystem:identity", original, destination);
        var context = Context([FileResult("file:logical-result", destination)], [action]);
        var journalAction = JournalAction(action, JournalActionResult.Succeeded, destination) with
        {
            UndoStatus = JournalUndoStatus.Succeeded,
            UndoTimestampUtc = DateTimeOffset.UtcNow,
            UndoAvailable = false,
        };
        var journal = Journal(OperationStatus.Undone, [journalAction]);

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            journal,
            isUndo: true);

        var file = Assert.Single(result.Snapshot!.Files);
        Assert.Equal("file:logical-result", file.Id);
        Assert.Equal(original, file.FullPath);
        Assert.False(result.RequiresTargetedRefresh);
    }

    [Fact]
    public void FilesystemIdentityCollision_DoesNotSelectAnUnrelatedResultsRow()
    {
        var original = FileAt("collision-source.txt");
        var unrelated = FileAt("collision-unrelated.txt");
        var destination = Path.Combine(_root, "collision-destination.txt");
        File.Move(original, destination);
        var action = Action("action:collision", "filesystem:collision", original, destination);
        var context = Context(
            [
                FileResult("file:logical-source", original, planned: true),
                FileResult("filesystem:collision", unrelated, planned: true),
            ],
            [action]);
        var journal = Journal(
            OperationStatus.Succeeded,
            [JournalAction(action, JournalActionResult.Succeeded, destination)]);

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            journal,
            isUndo: false);

        Assert.Equal(destination, result.Snapshot!.Files.Single(file => file.Id == "file:logical-source").FullPath);
        Assert.Equal(unrelated, result.Snapshot.Files.Single(file => file.Id == "filesystem:collision").FullPath);
        Assert.Equal("file:logical-source", Assert.Single(result.ActionOutcomes).SourceFileId);
    }

    [Fact]
    public void DuplicateRecoveryUndo_RequestsTargetedRefreshWhenApplyRemovedVisibleRow()
    {
        var original = FileAt("duplicate-restored.txt");
        var recoveryPath = Path.Combine(_root, "Duplicate Recovery", "duplicate-restored.txt");
        var action = Action("action:duplicate", "filesystem:duplicate", original, recoveryPath) with
        {
            SuggestionSource = ChangeSuggestionSource.DuplicateAnalysis,
        };
        var context = Context([], [action]);
        var journalAction = JournalAction(action, JournalActionResult.Succeeded, recoveryPath) with
        {
            UndoStatus = JournalUndoStatus.Succeeded,
            UndoTimestampUtc = DateTimeOffset.UtcNow,
            UndoAvailable = false,
        };
        var journal = Journal(OperationStatus.Undone, [journalAction]);

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            journal,
            isUndo: true);

        Assert.Empty(result.Snapshot!.Files);
        Assert.True(result.RequiresTargetedRefresh);
        Assert.Contains(original, result.AffectedPaths);
        Assert.Contains(recoveryPath, result.AffectedPaths);
    }

    [Fact]
    public void MissingResult_RemovesStaleProjectionAndRequestsRefresh()
    {
        var original = Path.Combine(_root, "missing-old.txt");
        var destination = Path.Combine(_root, "missing-new.txt");
        var action = Action("action:1", "file:1", original, destination);
        var context = Context([FileResult("file:1", original)], [action]);
        var journal = Journal(
            OperationStatus.RollbackPartiallyFailed,
            [JournalAction(action, JournalActionResult.RollbackFailed, destination, JournalRollbackResult.Failed)]);

        var result = new ChangePlanReconciliationService().Reconcile(
            context.Snapshot,
            context.Plan,
            journal,
            isUndo: false);

        Assert.Empty(result.Snapshot!.Files);
        Assert.True(result.RequiresTargetedRefresh);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string FileAt(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, name);
        return path;
    }

    private (ResultsSnapshot Snapshot, ChangePlan Plan) Context(
        IReadOnlyList<ResultFile> files,
        IReadOnlyList<ProposedChangeAction> actions)
    {
        var directories = files.Select(file => Path.GetDirectoryName(file.FullPath)!)
            .Where(Directory.Exists)
            .Distinct()
            .Select(path => new ResultDirectory(path, Path.GetFileName(path)))
            .ToArray();
        var filesByPath = files.ToDictionary(
            file => file.FullPath,
            OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparer);
        var planned = actions
            .Where(action =>
                action.SourcePath is not null && filesByPath.ContainsKey(action.SourcePath))
            .Select(action => new ResultPlannedOperation(
                action.ActionId,
                action.ActionType == ChangeActionType.RenameFile
                    ? OpenSorSe.Rules.Models.PlannedOperationKind.Rename
                    : OpenSorSe.Rules.Models.PlannedOperationKind.Move,
                filesByPath[action.SourcePath!].Id,
                action.DestinationPath,
                "Test"))
            .ToArray();
        var snapshot = new ResultsSnapshot(
            "scan:1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            files,
            directories,
            [],
            planned,
            [],
            new ResultsSnapshotStatistics(files.Count, directories.Length, 0, 0, planned.Length, 0, 0),
            IsDuplicateDataAvailable: true);
        var plan = new ChangePlan(
            ChangePlanSchema.CurrentVersion,
            "plan:1",
            DateTimeOffset.UtcNow,
            snapshot.SessionId,
            _root,
            ChangePlanStatus.Applied,
            actions,
            [],
            DateTimeOffset.UtcNow,
            IsSourceScanStale: false);
        return (snapshot, plan);
    }

    private static ResultFile FileResult(string id, string path, bool planned = false) => new(
        id,
        path,
        Path.GetFileName(path),
        Path.GetExtension(path).ToLowerInvariant(),
        File.Exists(path) ? new FileInfo(path).Length : 1,
        DateTimeOffset.UtcNow,
        FileCategory.Document,
        "Document",
        DuplicateStatus.Unique,
        null,
        planned);

    private static ProposedChangeAction Action(
        string actionId,
        string fileId,
        string source,
        string destination) => new(
            actionId,
            "plan:1",
            Path.GetFileName(source) == Path.GetFileName(destination)
                ? ChangeActionType.MoveFile
                : ChangeActionType.RenameFile,
            source,
            destination,
            Path.GetFileName(source),
            Path.GetFileName(destination),
            new FileIdentitySnapshot(fileId, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null),
            ChangeSuggestionSource.Ai,
            "Test",
            ChangeValidationState.Valid,
            ChangeApprovalState.Approved,
            1,
            [],
            [],
            false,
            null,
            null);

    private static OperationJournalAction JournalAction(
        ProposedChangeAction action,
        JournalActionResult result,
        string? actual,
        JournalRollbackResult rollback = JournalRollbackResult.NotRequired) => new(
            action.ActionId,
            action.ActionType,
            action.SuggestionSource,
            action.SourcePath,
            action.DestinationPath,
            actual,
            action.SourceIdentity,
            action.SourceIdentity,
            action.ValidationState,
            result,
            result == JournalActionResult.Skipped,
            result is JournalActionResult.Failed or JournalActionResult.RollbackFailed
                ? ChangeConflictCategory.IoFailure
                : ChangeConflictCategory.None,
            null,
            [],
            rollback != JournalRollbackResult.NotRequired,
            rollback,
            UndoAvailable: result == JournalActionResult.Succeeded,
            UndoStatus: result == JournalActionResult.Succeeded ? JournalUndoStatus.Available : JournalUndoStatus.NotAvailable,
            UndoTimestampUtc: null,
            UndoConflictDetails: null,
            AiModel: null,
            AiRequestCorrelationId: null,
            DirectoryCreatedByOpenSorSe: false);

    private static OperationJournalRecord Journal(
        OperationStatus status,
        IReadOnlyList<OperationJournalAction> actions) => new(
            OperationJournalSchema.CurrentVersion,
            "operation:1",
            "plan:1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "2.5.0",
            status,
            "test",
            "test-root",
            actions,
            CancellationRequested: false,
            status.ToString());
}
