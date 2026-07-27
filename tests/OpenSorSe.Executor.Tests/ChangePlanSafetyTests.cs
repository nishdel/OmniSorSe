#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor.Tests;

/// <summary>Exercises the v1.1 preview, validation, journal, rollback, recovery, and Undo safety boundary.</summary>
public sealed class ChangePlanSafetyTests
{
    [Fact]
    public async Task Factory_CapturesIdentityAndPersistsNonMutatingRenamePlan()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("original.txt", "unchanged");
        var context = Context(directory.Path);

        var plan = await context.Factory.CreateAsync(
            Request(directory.Path, Proposal(ChangeActionType.RenameFile, source, directory.PathOf("renamed.txt"))),
            CancellationToken.None);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(ChangePlanStatus.AwaitingReview, plan.Status);
        Assert.Equal(ChangeApprovalState.Pending, action.ApprovalState);
        Assert.Equal(ChangeValidationState.Valid, action.ValidationState);
        Assert.NotNull(action.SourceIdentity);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(directory.PathOf("renamed.txt")));
        Assert.Equal(plan, await context.PlanStore.GetAsync(plan.PlanId, CancellationToken.None));
    }

    [Fact]
    public async Task Validator_RejectsInvalidNamesDuplicateDestinationsAndConflictingSources()
    {
        using var directory = new TemporaryDirectory();
        var first = directory.File("first.txt", "one");
        var second = directory.File("second.txt", "two");
        var destination = directory.PathOf("CON.txt");
        var context = Context(directory.Path);

        var plan = await context.Factory.CreateAsync(
            new ChangePlanCreationRequest(
                directory.Path,
                "scan:test",
                [
                    Proposal(ChangeActionType.RenameFile, first, destination),
                    Proposal(ChangeActionType.MoveFile, second, destination),
                    Proposal(ChangeActionType.MoveFile, first, directory.PathOf("third.txt")),
                ]),
            CancellationToken.None);

        Assert.Contains(plan.Actions.SelectMany(action => action.Conflicts), conflict =>
            conflict.Category == ChangeConflictCategory.InvalidFileName);
        Assert.Contains(plan.Actions.SelectMany(action => action.Conflicts), conflict =>
            conflict.Category == ChangeConflictCategory.DuplicateDestination);
        Assert.Contains(plan.Actions.SelectMany(action => action.Conflicts), conflict =>
            conflict.Category == ChangeConflictCategory.ConflictingSourceActions);
        Assert.Equal(ChangePlanStatus.ValidationFailed, plan.Status);
    }

    [Fact]
    public async Task PreExecution_PlatformChecksRejectUnwritableOrCrossFilesystemMove()
    {
        using var directory = new TemporaryDirectory();
        var destinationDirectory = directory.PathOf("destination");
        Directory.CreateDirectory(destinationDirectory);
        var source = directory.File("source.txt", "source");
        var gateway = new PhysicalFileSystemGateway();
        var capabilities = new ControlledFileSystemCapabilities(
            canWrite: false,
            sameFileSystem: false);
        var validator = new ChangePlanValidator(
            gateway,
            PlatformServices.CurrentPathSemantics,
            capabilities);
        var factory = new ChangePlanFactory(
            gateway,
            validator,
            new InMemoryChangePlanStore());
        var plan = await factory.CreateAsync(
            Request(
                directory.Path,
                Proposal(
                    ChangeActionType.MoveFile,
                    source,
                    Path.Combine(destinationDirectory, "source.txt"))),
            CancellationToken.None);
        var approved = plan with
        {
            Actions = Array.AsReadOnly(plan.Actions.Select(action =>
                action with { ApprovalState = ChangeApprovalState.Approved }).ToArray()),
        };

        var result = await validator.ValidateAsync(
            approved,
            ChangePlanValidationPhase.PreExecution,
            CancellationToken.None);

        var conflicts = Assert.Single(result.Plan.Actions).Conflicts;
        Assert.Contains(conflicts, conflict =>
            conflict.Category == ChangeConflictCategory.PermissionDenied);
        Assert.Contains(conflicts, conflict =>
            conflict.Category == ChangeConflictCategory.InvalidAction &&
            conflict.Message.Contains("Cross-filesystem", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.CanApply);
    }

    [Fact]
    public async Task Validator_DetectsSourceAndDestinationChangesAfterApproval()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "before");
        var destination = directory.PathOf("destination.txt");
        var context = Context(directory.Path);
        var plan = await ApproveAsync(
            context,
            await context.Factory.CreateAsync(
                Request(directory.Path, Proposal(ChangeActionType.RenameFile, source, destination)),
                CancellationToken.None));

        await File.WriteAllTextAsync(source, "materially changed");
        await File.WriteAllTextAsync(destination, "occupied");
        var validation = await context.Validator.ValidateAsync(
            plan,
            ChangePlanValidationPhase.PreExecution,
            CancellationToken.None);

        var action = Assert.Single(validation.Plan.Actions);
        Assert.Equal(ChangeValidationState.Stale, action.ValidationState);
        Assert.True(validation.Plan.IsSourceScanStale);
        Assert.Contains(action.Conflicts, conflict => conflict.Category == ChangeConflictCategory.SourceChanged);
        Assert.Contains(action.Conflicts, conflict => conflict.Category == ChangeConflictCategory.DestinationOccupied);
        Assert.False(validation.CanApply);
    }

    [Fact]
    public async Task Validator_DetectsExternalRenameHashChangeLockAndLongDestination()
    {
        using var directory = new TemporaryDirectory();
        var renamedSource = directory.File("rename-source.txt", "same");
        var hashSource = directory.File("hash-source.txt", "aaaa");
        var lockSource = directory.File("lock-source.txt", "locked");
        var context = Context(directory.Path);
        var renamedPlan = await context.Factory.CreateAsync(
            Request(directory.Path, Proposal(
                ChangeActionType.RenameFile,
                renamedSource,
                directory.PathOf("renamed.txt"))),
            CancellationToken.None);
        File.Move(renamedSource, directory.PathOf("external-name.txt"));
        var renamedValidation = await context.Validator.ValidateAsync(
            renamedPlan,
            ChangePlanValidationPhase.Review,
            CancellationToken.None);
        Assert.Contains(
            Assert.Single(renamedValidation.Plan.Actions).Conflicts,
            conflict => conflict.Category == ChangeConflictCategory.SourceRenamedExternally);

        var hashPlan = await context.Factory.CreateAsync(
            Request(
                directory.Path,
                Proposal(ChangeActionType.RenameFile, hashSource, directory.PathOf("hash-done.txt")) with
                {
                    ContentHash = "capture-current-hash",
                }),
            CancellationToken.None);
        var originalWriteTime = File.GetLastWriteTimeUtc(hashSource);
        await File.WriteAllTextAsync(hashSource, "bbbb");
        File.SetLastWriteTimeUtc(hashSource, originalWriteTime);
        var hashValidation = await context.Validator.ValidateAsync(
            hashPlan,
            ChangePlanValidationPhase.Review,
            CancellationToken.None);
        Assert.Contains(
            Assert.Single(hashValidation.Plan.Actions).Conflicts,
            conflict => conflict.Category == ChangeConflictCategory.SourceHashChanged);

        await using (var locked = new FileStream(
                         lockSource,
                         FileMode.Open,
                         FileAccess.ReadWrite,
                         FileShare.None))
        {
            var lockPlan = await context.Factory.CreateAsync(
                Request(directory.Path, Proposal(
                    ChangeActionType.RenameFile,
                    lockSource,
                    directory.PathOf("lock-done.txt"))),
                CancellationToken.None);
            if (OperatingSystem.IsWindows())
            {
                Assert.Contains(
                    Assert.Single(lockPlan.Actions).Conflicts,
                    conflict => conflict.Category == ChangeConflictCategory.SourceLocked);
            }
        }

        var longPlan = renamedPlan with
        {
            Actions =
            [
                renamedPlan.Actions[0] with
                {
                    DestinationPath = Path.Combine(
                        directory.Path,
                        new string('x', ChangePlanSchema.MaximumPathLength + 1)),
                },
            ],
        };
        var longValidation = await context.Validator.ValidateAsync(
            longPlan,
            ChangePlanValidationPhase.Review,
            CancellationToken.None);
        Assert.Contains(
            Assert.Single(longValidation.Plan.Actions).Conflicts,
            conflict => conflict.Category is
                ChangeConflictCategory.PathTooLong or
                ChangeConflictCategory.DestinationInvalid);
    }

    [Fact]
    public async Task Execute_RenamesAndPersistsVerifiedUndoInformation()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("résumé original.txt", "unicode");
        var destination = directory.PathOf("résumé final.txt");
        var context = Context(directory.Path);
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, source, destination));

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(source));
        Assert.Equal("unicode", await File.ReadAllTextAsync(destination));
        var action = Assert.Single(result.Operation.Actions);
        Assert.Equal(JournalActionResult.Succeeded, action.ExecutionResult);
        Assert.True(action.UndoAvailable);
        Assert.Equal(JournalUndoStatus.Available, action.UndoStatus);
        Assert.NotNull(action.PostExecutionIdentity);
        Assert.Equal(result.Operation, await context.Journal.GetAsync(result.Operation.OperationId, CancellationToken.None));

        var undo = await context.Executor.UndoAsync(
            result.Operation.OperationId,
            null,
            null,
            CancellationToken.None);
        Assert.Equal(OperationStatus.Undone, undo.Operation.Status);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Execute_CombinedPlanCreatesFolderMovesAndRenamesThenUndoRestoresAll()
    {
        using var directory = new TemporaryDirectory();
        var moveSource = directory.File("move me.txt", "move");
        var renameSource = directory.File("rename me.txt", "rename");
        var targetFolder = directory.PathOf("Organized");
        var context = Context(directory.Path);
        var plan = await CreateApprovedAsync(
            context,
            directory.Path,
            Proposal(ChangeActionType.MoveFile, moveSource, Path.Combine(targetFolder, "move me.txt"), 2),
            Proposal(ChangeActionType.RenameFile, renameSource, directory.PathOf("renamed.txt"), 3));

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(Directory.Exists(targetFolder));
        Assert.True(File.Exists(Path.Combine(targetFolder, "move me.txt")));
        Assert.True(File.Exists(directory.PathOf("renamed.txt")));
        Assert.Equal(3, result.Operation.Actions.Count);

        var undo = await context.Executor.UndoAsync(result.Operation.OperationId, null, null, CancellationToken.None);
        Assert.Equal(OperationStatus.Undone, undo.Operation.Status);
        Assert.True(File.Exists(moveSource));
        Assert.True(File.Exists(renameSource));
        Assert.False(Directory.Exists(targetFolder));
    }

    [Fact]
    public async Task Execute_BlocksOccupiedDestinationWithoutOverwriting()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "source");
        var destination = directory.File("destination.txt", "existing");
        var context = Context(directory.Path);
        var plan = await context.Factory.CreateAsync(
            Request(directory.Path, Proposal(ChangeActionType.RenameFile, source, destination)),
            CancellationToken.None);
        plan = plan with
        {
            Actions = Array.AsReadOnly(plan.Actions
                .Select(action => action with { ApprovalState = ChangeApprovalState.Approved })
                .ToArray()),
        };

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationStatus.Failed, result.Operation.Status);
        Assert.Equal("existing", await File.ReadAllTextAsync(destination));
        Assert.Equal("source", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task Execute_CancellationAtSafeBoundaryRollsBackCompletedAction()
    {
        using var directory = new TemporaryDirectory();
        var first = directory.File("first.txt", "one");
        var second = directory.File("second.txt", "two");
        using var cancellation = new CancellationTokenSource();
        var gateway = new ControlledGateway(cancellation: cancellation);
        var context = Context(directory.Path, gateway);
        var plan = await CreateApprovedAsync(
            context,
            directory.Path,
            Proposal(ChangeActionType.RenameFile, first, directory.PathOf("first done.txt"), 1),
            Proposal(ChangeActionType.RenameFile, second, directory.PathOf("second done.txt"), 2));

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(OperationStatus.RolledBack, result.Operation.Status);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(JournalRollbackResult.Succeeded, result.Operation.Actions[0].RollbackResult);
        Assert.True(result.Operation.CancellationRequested);
    }

    [Fact]
    public async Task Execute_FailureRollsBackAndReportsRollbackFailureWithoutClaimingSuccess()
    {
        using var directory = new TemporaryDirectory();
        var first = directory.File("first.txt", "one");
        var second = directory.File("second.txt", "two");
        var gateway = new ControlledGateway(failMoveNumber: 2, failRollback: true);
        var context = Context(directory.Path, gateway);
        var firstDestination = directory.PathOf("first done.txt");
        var plan = await CreateApprovedAsync(
            context,
            directory.Path,
            Proposal(ChangeActionType.RenameFile, first, firstDestination, 1),
            Proposal(ChangeActionType.RenameFile, second, directory.PathOf("second done.txt"), 2));

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationStatus.RollbackPartiallyFailed, result.Operation.Status);
        Assert.True(File.Exists(firstDestination));
        Assert.Contains(result.Operation.Actions, action =>
            action.RollbackResult == JournalRollbackResult.Failed);
    }

    [Fact]
    public async Task Execute_VerificationFailureIsJournalledAndOriginalRemainsSafe()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "source");
        var destination = directory.PathOf("destination.txt");
        var context = Context(directory.Path, new ControlledGateway(noOpMove: true));
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, source, destination));

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationStatus.Failed, result.Operation.Status);
        Assert.Equal(ChangeConflictCategory.VerificationFailed, Assert.Single(result.Operation.Actions).ErrorCategory);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Execute_JournalFailureAfterMutationTriggersVerifiedEmergencyRollback()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "source");
        var destination = directory.PathOf("destination.txt");
        var gateway = new PhysicalFileSystemGateway();
        var validator = new ChangePlanValidator(gateway);
        var planStore = new InMemoryChangePlanStore();
        var journal = new FailingJournalStore(failOnCall: 3);
        var factory = new ChangePlanFactory(gateway, validator, planStore);
        var executor = new ChangePlanExecutionService(gateway, validator, planStore, journal);
        var context = new SafetyContext(factory, validator, executor, planStore, journal);
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, source, destination));

        var result = await executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationStatus.RolledBack, result.Operation.Status);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
        Assert.Equal(JournalRollbackResult.Succeeded, Assert.Single(result.Operation.Actions).RollbackResult);
        Assert.Contains("Journal", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OperationStatus.Running, Assert.Single(await journal.ListAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Execute_PermissionFailureIsStructuredAndPersistsWithoutMutation()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "source");
        var destination = directory.PathOf("destination.txt");
        var context = Context(directory.Path, new ControlledGateway(denyMove: true));
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, source, destination));

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationStatus.Failed, result.Operation.Status);
        var action = Assert.Single(result.Operation.Actions);
        Assert.Equal(ChangeConflictCategory.PermissionDenied, action.ErrorCategory);
        Assert.Equal(JournalActionResult.Failed, action.ExecutionResult);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
        Assert.Equal(result.Operation, await context.Journal.GetAsync(result.Operation.OperationId, CancellationToken.None));
    }

    [Fact]
    public async Task Undo_BlocksModifiedResultAndOccupiedOriginalWithoutDestroyingData()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "before");
        var destination = directory.PathOf("destination.txt");
        var context = Context(directory.Path);
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, source, destination));
        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);
        await File.WriteAllTextAsync(destination, "externally changed");
        await File.WriteAllTextAsync(source, "new occupant");

        var undo = await context.Executor.UndoAsync(result.Operation.OperationId, null, null, CancellationToken.None);

        Assert.Equal(OperationStatus.UndoBlockedByConflicts, undo.Operation.Status);
        Assert.Equal(1, undo.ActionsBlocked);
        Assert.Equal("new occupant", await File.ReadAllTextAsync(source));
        Assert.Equal("externally changed", await File.ReadAllTextAsync(destination));
        Assert.Equal(JournalUndoStatus.Blocked, Assert.Single(undo.Operation.Actions).UndoStatus);
    }

    [Fact]
    public async Task Undo_PreservesCreatedDirectoryWhenItIsNoLongerEmpty()
    {
        using var directory = new TemporaryDirectory();
        var created = directory.PathOf("Created");
        var context = Context(directory.Path);
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.CreateDirectory, null, created));
        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(created, "external.txt"), "keep");

        var undo = await context.Executor.UndoAsync(result.Operation.OperationId, null, null, CancellationToken.None);

        Assert.Equal(OperationStatus.UndoBlockedByConflicts, undo.Operation.Status);
        Assert.True(Directory.Exists(created));
        Assert.True(File.Exists(Path.Combine(created, "external.txt")));
    }

    [Fact]
    public async Task Undo_ReportsPartialCompletionWhenOnlySomeResultsRemainSafe()
    {
        using var directory = new TemporaryDirectory();
        var first = directory.File("first.txt", "one");
        var second = directory.File("second.txt", "two");
        var context = Context(directory.Path);
        var plan = await CreateApprovedAsync(
            context,
            directory.Path,
            Proposal(ChangeActionType.RenameFile, first, directory.PathOf("first done.txt"), 1),
            Proposal(ChangeActionType.RenameFile, second, directory.PathOf("second done.txt"), 2));
        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);
        await File.WriteAllTextAsync(directory.PathOf("second done.txt"), "externally modified");

        var undo = await context.Executor.UndoAsync(result.Operation.OperationId, null, null, CancellationToken.None);

        Assert.Equal(OperationStatus.UndoPartiallyCompleted, undo.Operation.Status);
        Assert.Equal(1, undo.ActionsUndone);
        Assert.Equal(1, undo.ActionsBlocked);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(directory.PathOf("second done.txt")));
    }

    [Fact]
    public async Task Undo_SelectedActionRestoresOnlyThatIndependentResult()
    {
        using var directory = new TemporaryDirectory();
        var first = directory.File("first.txt", "one");
        var second = directory.File("second.txt", "two");
        var firstDestination = directory.PathOf("first done.txt");
        var secondDestination = directory.PathOf("second done.txt");
        var context = Context(directory.Path);
        var plan = await CreateApprovedAsync(
            context,
            directory.Path,
            Proposal(ChangeActionType.RenameFile, first, firstDestination, 1),
            Proposal(ChangeActionType.RenameFile, second, secondDestination, 2));
        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);
        var selectedId = result.Operation.Actions.Single(action =>
            string.Equals(action.OriginalPath, first, StringComparison.Ordinal)).ActionId;

        var undo = await context.Executor.UndoAsync(
            result.Operation.OperationId,
            [selectedId],
            null,
            CancellationToken.None);

        Assert.Equal(OperationStatus.UndoPartiallyCompleted, undo.Operation.Status);
        Assert.Equal(1, undo.ActionsUndone);
        Assert.True(File.Exists(first));
        Assert.False(File.Exists(firstDestination));
        Assert.False(File.Exists(second));
        Assert.True(File.Exists(secondDestination));
        Assert.True(undo.Operation.UndoAvailable);
    }

    [Fact]
    public async Task Undo_BlocksAnOperationThatALaterSucceededOperationDependsOn()
    {
        using var directory = new TemporaryDirectory();
        var original = directory.File("original.txt", "data");
        var middle = directory.PathOf("middle.txt");
        var final = directory.PathOf("final.txt");
        var context = Context(directory.Path);
        var firstPlan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, original, middle));
        var first = await context.Executor.ExecuteAsync(firstPlan, "Test", null, CancellationToken.None);
        var secondPlan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, middle, final));
        var second = await context.Executor.ExecuteAsync(secondPlan, "Test", null, CancellationToken.None);
        Assert.True(second.Succeeded);

        var undo = await context.Executor.UndoAsync(first.Operation.OperationId, null, null, CancellationToken.None);

        Assert.Equal(OperationStatus.UndoBlockedByConflicts, undo.Operation.Status);
        Assert.Contains("later OpenSorSe operation", Assert.Single(undo.Operation.Actions).UndoConflictDetails);
        Assert.True(File.Exists(final));
    }

    [Fact]
    public async Task Execute_PreCancelledDoesNotMutateOrCreateJournalRecord()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "data");
        var context = Context(directory.Path);
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, source, directory.PathOf("done.txt")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Executor.ExecuteAsync(plan, "Test", null, cancellation.Token));

        Assert.True(File.Exists(source));
        Assert.Empty(await context.Journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Execute_CaseOnlyRenameUsesSafeIntermediatePathOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var source = directory.File("mixedcase.txt", "data");
        var destination = directory.PathOf("MixedCase.txt");
        var context = Context(directory.Path);
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, source, destination));

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(destination));
        Assert.Contains(
            Assert.Single(result.Operation.Actions).WarningDetails,
            warning => warning.Contains("temporary", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, ".opensorse-*.tmp"));
    }

    [Fact]
    public async Task Execute_CaseOnlyFinalHopFailureRestoresAndJournalsOriginalOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var source = directory.File("lowercase.txt", "data");
        var context = Context(directory.Path, new ControlledGateway(failMoveNumber: 2));
        var plan = await CreateApprovedAsync(context, directory.Path,
            Proposal(ChangeActionType.RenameFile, source, directory.PathOf("LowerCase.txt")));

        var result = await context.Executor.ExecuteAsync(plan, "Test", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationStatus.Failed, result.Operation.Status);
        var action = Assert.Single(result.Operation.Actions);
        Assert.Equal(JournalActionResult.RolledBack, action.ExecutionResult);
        Assert.Equal(JournalRollbackResult.Succeeded, action.RollbackResult);
        Assert.Equal(source, action.ActualResultingPath);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, ".opensorse-*.tmp"));
    }

    [Fact]
    public async Task JsonStoresSurviveReloadAndExporterIncludesRecoveryFacts()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "data");
        var planPath = directory.PathOf("plans.json");
        var journalPath = directory.PathOf("journal.json");
        var logging = new TestLoggingService();
        var gateway = new PhysicalFileSystemGateway();
        var validator = new ChangePlanValidator(gateway);
        var planStore = new JsonChangePlanStore(planPath, logging);
        var journal = new JsonOperationJournalStore(journalPath, logging);
        var factory = new ChangePlanFactory(gateway, validator, planStore);
        var executor = new ChangePlanExecutionService(gateway, validator, planStore, journal);
        var plan = await factory.CreateAsync(
            Request(directory.Path, Proposal(ChangeActionType.RenameFile, source, directory.PathOf("done.txt"))),
            CancellationToken.None);
        plan = await ApproveAsync(new SafetyContext(factory, validator, executor, planStore, journal), plan);
        var result = await executor.ExecuteAsync(plan, "Review Changes", null, CancellationToken.None);

        var reloadedPlans = await new JsonChangePlanStore(planPath, logging).ListAsync(CancellationToken.None);
        var reloadedOperations = await new JsonOperationJournalStore(journalPath, logging).ListAsync(CancellationToken.None);
        var report = new OperationReportExporter().Export(Assert.Single(reloadedOperations));

        Assert.Single(reloadedPlans);
        Assert.Contains("OpenSorSe Operation Report", report);
        Assert.Contains(result.Operation.OperationId, report);
        Assert.Contains("Undo: Available", report);
        Assert.DoesNotContain("data", report);
    }

    [Fact]
    public async Task JournalReadsLegacyArrayAndCorruptDataFailsGracefully()
    {
        using var directory = new TemporaryDirectory();
        var logging = new TestLoggingService();
        var path = directory.PathOf("journal.json");
        var operation = SampleOperation(directory.Path);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new[] { operation }, options));

        var migrated = await new JsonOperationJournalStore(path, logging).ListAsync(CancellationToken.None);
        Assert.Equal(OperationJournalSchema.CurrentVersion, Assert.Single(migrated).SchemaVersion);

        await File.WriteAllTextAsync(path, "{broken");
        Assert.Empty(await new JsonOperationJournalStore(path, logging).ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecoveryMarksRunningOperationInterruptedAfterInspectingPaths()
    {
        using var directory = new TemporaryDirectory();
        var original = directory.PathOf("source.txt");
        var destination = directory.File("destination.txt", "moved");
        var gateway = new PhysicalFileSystemGateway();
        var identity = await gateway.CaptureFileIdentityAsync(destination, false, CancellationToken.None);
        var planStore = new InMemoryChangePlanStore();
        var journal = new InMemoryOperationJournalStore();
        var operation = SampleOperation(directory.Path) with
        {
            Status = OperationStatus.Running,
            Actions =
            [
                SampleOperation(directory.Path).Actions[0] with
                {
                    OriginalPath = original,
                    IntendedDestinationPath = destination,
                    PreExecutionIdentity = identity,
                },
            ],
        };
        await journal.UpsertAsync(operation, CancellationToken.None);
        var executor = new ChangePlanExecutionService(
            gateway,
            new ChangePlanValidator(gateway),
            planStore,
            journal);

        var recovered = await executor.RecoverInterruptedAsync(CancellationToken.None);

        var item = Assert.Single(recovered);
        Assert.Equal(OperationStatus.Interrupted, item.Status);
        Assert.Equal(JournalActionResult.Succeeded, Assert.Single(item.Actions).ExecutionResult);
        Assert.True(item.UndoAvailable);
    }

    [Fact]
    public async Task RecoveryRetainsJournalledTemporaryCaseRenamePathForSafeUndo()
    {
        using var directory = new TemporaryDirectory();
        var original = directory.PathOf("source.txt");
        var temporary = directory.File(".opensorse-test.tmp", "moved");
        var destination = directory.PathOf("Source.txt");
        var gateway = new PhysicalFileSystemGateway();
        var identity = await gateway.CaptureFileIdentityAsync(temporary, false, CancellationToken.None);
        var journal = new InMemoryOperationJournalStore();
        var sample = SampleOperation(directory.Path);
        await journal.UpsertAsync(
            sample with
            {
                Status = OperationStatus.Running,
                Actions =
                [
                    sample.Actions[0] with
                    {
                        OriginalPath = original,
                        IntendedDestinationPath = destination,
                        ActualResultingPath = temporary,
                        PreExecutionIdentity = identity,
                    },
                ],
            },
            CancellationToken.None);
        var planStore = new InMemoryChangePlanStore();
        var executor = new ChangePlanExecutionService(
            gateway,
            new ChangePlanValidator(gateway),
            planStore,
            journal);

        var recovered = Assert.Single(await executor.RecoverInterruptedAsync(CancellationToken.None));
        var action = Assert.Single(recovered.Actions);
        Assert.Equal(OperationStatus.Interrupted, recovered.Status);
        Assert.Equal(temporary, action.ActualResultingPath);
        Assert.Equal(JournalActionResult.Succeeded, action.ExecutionResult);
        Assert.Equal(ChangeConflictCategory.InterruptedStateAmbiguous, action.ErrorCategory);
        Assert.True(action.UndoAvailable);
    }

    private static ChangePlanCreationRequest Request(string root, params ChangeActionProposal[] proposals) =>
        new(root, "scan:test", proposals);

    private static ChangeActionProposal Proposal(
        ChangeActionType type,
        string? source,
        string destination,
        int order = 1) =>
        new(
            type,
            source,
            destination,
            ChangeSuggestionSource.DeterministicRule,
            "Test proposal.",
            order);

    private static SafetyContext Context(string root, IFileSystemGateway? gateway = null)
    {
        gateway ??= new PhysicalFileSystemGateway();
        var validator = new ChangePlanValidator(gateway);
        var planStore = new InMemoryChangePlanStore();
        var journal = new InMemoryOperationJournalStore();
        var factory = new ChangePlanFactory(gateway, validator, planStore);
        var executor = new ChangePlanExecutionService(gateway, validator, planStore, journal);
        return new SafetyContext(factory, validator, executor, planStore, journal);
    }

    private static async Task<ChangePlan> CreateApprovedAsync(
        SafetyContext context,
        string root,
        params ChangeActionProposal[] proposals) =>
        await ApproveAsync(
            context,
            await context.Factory.CreateAsync(
                new ChangePlanCreationRequest(root, "scan:test", proposals),
                CancellationToken.None));

    private static async Task<ChangePlan> ApproveAsync(SafetyContext context, ChangePlan plan)
    {
        var approved = plan with
        {
            Actions = Array.AsReadOnly(plan.Actions.Select(action =>
                action with { ApprovalState = ChangeApprovalState.Approved }).ToArray()),
            ValidatedAtUtc = null,
        };
        return (await context.Validator.ValidateAsync(
            approved,
            ChangePlanValidationPhase.Review,
            CancellationToken.None)).Plan;
    }

    private static OperationJournalRecord SampleOperation(string root)
    {
        var action = new OperationJournalAction(
            "action:test",
            ChangeActionType.RenameFile,
            ChangeSuggestionSource.DeterministicRule,
            Path.Combine(root, "source.txt"),
            Path.Combine(root, "destination.txt"),
            null,
            null,
            null,
            ChangeValidationState.Valid,
            JournalActionResult.Pending,
            false,
            ChangeConflictCategory.None,
            null,
            [],
            false,
            JournalRollbackResult.NotRequired,
            false,
            JournalUndoStatus.NotAvailable,
            null,
            null,
            null,
            null,
            false);
        return new OperationJournalRecord(
            0,
            "operation:test",
            "plan:test",
            DateTimeOffset.UtcNow,
            null,
            "1.0.0",
            OperationStatus.Pending,
            "Test",
            root,
            [action],
            false,
            "Pending test operation.");
    }

    private sealed record SafetyContext(
        IChangePlanFactory Factory,
        IChangePlanValidator Validator,
        IChangePlanExecutionService Executor,
        IChangePlanStore PlanStore,
        IOperationJournalStore Journal);

    private sealed class ControlledFileSystemCapabilities(
        bool canWrite,
        bool sameFileSystem) : IFileSystemCapabilities
    {
        public FileLinkInspection InspectLink(string path) =>
            new(false, null, null, "Not a link.");

        public bool CanWriteDirectory(string path, out string explanation)
        {
            explanation = canWrite ? "Writable." : "Controlled permission denial.";
            return canWrite;
        }

        public long? GetAvailableFreeSpace(string path) => 1_000_000;

        public bool AreOnSameFileSystem(
            string firstPath,
            string secondPath,
            out string explanation)
        {
            explanation = sameFileSystem
                ? "Same controlled filesystem."
                : "Different controlled filesystems.";
            return sameFileSystem;
        }
    }

    private sealed class ControlledGateway(
        int? failMoveNumber = null,
        bool failRollback = false,
        CancellationTokenSource? cancellation = null,
        bool noOpMove = false,
        bool denyMove = false) : IFileSystemGateway
    {
        private readonly PhysicalFileSystemGateway _inner = new();
        private int _moveCount;

        public string NormalizePath(string path) => _inner.NormalizePath(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool IsDirectoryEmpty(string path) => _inner.IsDirectoryEmpty(path);
        public bool IsReparsePoint(string path) => _inner.IsReparsePoint(path);
        public Task<FileIdentitySnapshot?> CaptureFileIdentityAsync(
            string path,
            bool includeHash,
            CancellationToken cancellationToken) =>
            _inner.CaptureFileIdentityAsync(path, includeHash, cancellationToken);
        public Task<bool> CanOpenExclusivelyAsync(string path, CancellationToken cancellationToken) =>
            _inner.CanOpenExclusivelyAsync(path, cancellationToken);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public void DeleteDirectory(string path) => _inner.DeleteDirectory(path);

        public void MoveFile(string sourcePath, string destinationPath)
        {
            _moveCount++;
            if (denyMove)
            {
                throw new UnauthorizedAccessException("Injected access denial.");
            }

            if (failMoveNumber == _moveCount ||
                failRollback && _moveCount > (failMoveNumber ?? int.MaxValue))
            {
                throw new IOException("Injected filesystem failure.");
            }

            if (noOpMove)
            {
                return;
            }

            _inner.MoveFile(sourcePath, destinationPath);
            if (_moveCount == 1)
            {
                cancellation?.Cancel();
            }
        }
    }

    private sealed class FailingJournalStore(int failOnCall) : IOperationJournalStore
    {
        private readonly InMemoryOperationJournalStore _inner = new();
        private int _calls;

        public Task<IReadOnlyList<OperationJournalRecord>> ListAsync(CancellationToken cancellationToken) =>
            _inner.ListAsync(cancellationToken);

        public Task<OperationJournalRecord?> GetAsync(
            string operationId,
            CancellationToken cancellationToken) =>
            _inner.GetAsync(operationId, cancellationToken);

        public Task UpsertAsync(OperationJournalRecord operation, CancellationToken cancellationToken)
        {
            _calls++;
            if (_calls >= failOnCall)
            {
                throw new IOException("Injected journal failure.");
            }

            return _inner.UpsertAsync(operation, cancellationToken);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"OpenSorSe.ChangePlan.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public string PathOf(string name) => System.IO.Path.Combine(Path, name);

        public string File(string name, string contents)
        {
            var path = PathOf(name);
            System.IO.File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TestLoggingService : ILoggingService
    {
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }
        public void Initialize(LogLevel minimumLevel) { }
    }
}
