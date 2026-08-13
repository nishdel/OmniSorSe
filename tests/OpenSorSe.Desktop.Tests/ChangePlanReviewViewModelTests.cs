#pragma warning disable CS1591

using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Core.Platform;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies the explicit review, confirmation, progress, result, history, and Undo presentation workflow.</summary>
public sealed class ChangePlanReviewViewModelTests
{
    [Fact]
    public async Task ApprovalCountsAndValidationControlApplyAvailability()
    {
        using var directory = new TemporaryDirectory();
        var context = await CreateContextAsync(directory, "source.txt", "renamed.txt");
        using var viewModel = new ChangePlanReviewViewModel(
            context.Validator,
            context.Executor,
            context.PlanStore);

        await viewModel.LoadAsync(context.Plan);
        Assert.False(viewModel.CanApply);
        Assert.Equal(1, viewModel.PendingCount);

        viewModel.ApproveAllSafeCommand.Execute(null);
        Assert.Equal(1, viewModel.ApprovedCount);
        Assert.Equal(0, viewModel.PendingCount);
        Assert.False(viewModel.CanApply);

        await viewModel.ValidatePlanCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.RequestApplyCommand.CanExecute(null));

        viewModel.DeselectAllCommand.Execute(null);
        Assert.Equal(1, viewModel.RejectedCount);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task EditingDestinationInvalidatesPriorValidationAndUpdatesSummary()
    {
        using var directory = new TemporaryDirectory();
        var context = await CreateContextAsync(directory, "source.txt", "renamed.txt");
        using var viewModel = new ChangePlanReviewViewModel(
            context.Validator,
            context.Executor,
            context.PlanStore);
        await viewModel.LoadAsync(context.Plan);
        viewModel.ApproveAllSafeCommand.Execute(null);
        await viewModel.ValidatePlanCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanApply);

        viewModel.SelectedAction!.EditedFileName = "edited name.txt";
        await viewModel.SaveEditCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.SelectedAction.WasUserEdited);
        Assert.Equal(ChangeSuggestionSource.ManualUserEdit, viewModel.SelectedAction.SuggestionSource);
        Assert.Equal("edited name.txt", Path.GetFileName(viewModel.SelectedAction.ProposedPath));
        Assert.Contains("1 rename(s)", viewModel.ConfirmationSummary);
        Assert.Contains("Overwrite: no", viewModel.ConfirmationSummary);
    }

    [Fact]
    public async Task BlockingConflictKeepsFinalApplyDisabled()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.File("source.txt", "source");
        directory.File("occupied.txt", "existing");
        var gateway = CreateGateway();
        var validator = CreateValidator(gateway);
        var store = new InMemoryChangePlanStore();
        var journal = new InMemoryOperationJournalStore();
        var factory = new ChangePlanFactory(gateway, validator, store);
        var plan = await factory.CreateAsync(
            new ChangePlanCreationRequest(
                directory.Path,
                "scan:test",
                [
                    new ChangeActionProposal(
                        ChangeActionType.RenameFile,
                        source,
                        directory.PathOf("occupied.txt"),
                        ChangeSuggestionSource.Ai,
                        "Suggested by the local model.",
                        1),
                ]),
            CancellationToken.None);
        using var viewModel = new ChangePlanReviewViewModel(
            validator,
            new ChangePlanExecutionService(gateway, validator, store, journal),
            store);
        await viewModel.LoadAsync(plan);

        viewModel.ApproveSelectedCommand.Execute(null);
        await viewModel.ValidatePlanCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.ConflictCount);
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.RequestApplyCommand.CanExecute(null));
        Assert.True(viewModel.CouldOverwrite);
    }

    [Fact]
    public async Task ApplyRequiresFinalConfirmationAndShowsVerifiedUndoResult()
    {
        using var directory = new TemporaryDirectory();
        var context = await CreateContextAsync(directory, "source.txt", "renamed.txt");
        using var viewModel = new ChangePlanReviewViewModel(
            context.Validator,
            context.Executor,
            context.PlanStore);
        await viewModel.LoadAsync(context.Plan);
        viewModel.ApproveAllSafeCommand.Execute(null);
        await viewModel.ValidatePlanCommand.ExecuteAsync(null);

        Assert.Null(viewModel.LastExecution);
        viewModel.RequestApplyCommand.Execute(null);
        Assert.True(viewModel.IsApplyConfirmationPending);
        Assert.Contains("1 rename(s)", viewModel.StatusText);
        await viewModel.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.LastExecution);
        Assert.True(viewModel.LastExecution!.Succeeded);
        Assert.Equal(ChangePlanStatus.Applied, viewModel.CurrentPlan!.Status);
        Assert.True(viewModel.UndoAvailable);
        Assert.Equal("Succeeded", viewModel.ProgressText);

        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Equal(OperationStatus.Undone, viewModel.LastUndo!.Operation.Status);
        Assert.False(viewModel.UndoAvailable);
        Assert.True(File.Exists(directory.PathOf("source.txt")));
    }

    [Fact]
    public async Task PartialExecutionStillPublishesJournalForProjectionReconciliation()
    {
        using var directory = new TemporaryDirectory();
        var context = await CreateContextAsync(directory, "source.txt", "renamed.txt");
        var executor = new PartialExecutionService();
        using var viewModel = new ChangePlanReviewViewModel(
            context.Validator,
            executor,
            context.PlanStore);
        ChangePlanOperationCompleted? completed = null;
        viewModel.OperationCompleted += (_, value) => completed = value;
        await viewModel.LoadAsync(context.Plan);
        viewModel.ApproveAllSafeCommand.Execute(null);
        await viewModel.ValidatePlanCommand.ExecuteAsync(null);
        viewModel.RequestApplyCommand.Execute(null);

        await viewModel.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.NotNull(completed);
        Assert.False(completed.IsUndo);
        Assert.Equal(OperationStatus.RollbackPartiallyFailed, completed.Operation.Status);
        Assert.Equal(ChangePlanStatus.PartiallyApplied, viewModel.CurrentPlan!.Status);
    }

    [Fact]
    public async Task OperationHistoryLoadsDetailsCopiesReportAndExecutesConfirmedUndo()
    {
        using var directory = new TemporaryDirectory();
        var context = await CreateContextAsync(directory, "source.txt", "renamed.txt");
        var approved = context.Plan with
        {
            Actions = Array.AsReadOnly(context.Plan.Actions
                .Select(action => action with { ApprovalState = ChangeApprovalState.Approved })
                .ToArray()),
        };
        approved = (await context.Validator.ValidateAsync(
            approved,
            ChangePlanValidationPhase.Review,
            CancellationToken.None)).Plan;
        var execution = await context.Executor.ExecuteAsync(
            approved,
            "Review Changes",
            null,
            CancellationToken.None);
        var clipboard = new RecordingClipboard();
        var history = new UndoHistoryViewModel(
            context.Journal,
            context.Executor,
            new OperationReportExporter(),
            clipboard);

        await history.RefreshAsync();
        Assert.True(history.HasOperations);
        Assert.Equal(execution.Operation.OperationId, history.SelectedOperation!.OperationId);
        Assert.Contains("RenameFile", history.SelectedOperationDetails);
        Assert.True(history.RequestOperationUndoCommand.CanExecute(null));

        await history.CopyReportCommand.ExecuteAsync(null);
        Assert.Contains("OmniSorSe Operation Report", clipboard.Text);

        history.RequestOperationUndoCommand.Execute(null);
        Assert.True(history.IsUndoConfirmationPending);
        await history.ConfirmOperationUndoCommand.ExecuteAsync(null);
        Assert.Equal(OperationStatus.Undone, history.SelectedOperation!.Status);
        Assert.True(File.Exists(directory.PathOf("source.txt")));
    }

    private static async Task<TestContext> CreateContextAsync(
        TemporaryDirectory directory,
        string sourceName,
        string destinationName)
    {
        var source = directory.File(sourceName, "content");
        var gateway = CreateGateway();
        var validator = CreateValidator(gateway);
        var store = new InMemoryChangePlanStore();
        var journal = new InMemoryOperationJournalStore();
        var factory = new ChangePlanFactory(gateway, validator, store);
        var executor = new ChangePlanExecutionService(gateway, validator, store, journal);
        var plan = await factory.CreateAsync(
            new ChangePlanCreationRequest(
                directory.Path,
                "scan:test",
                [
                    new ChangeActionProposal(
                        ChangeActionType.RenameFile,
                        source,
                        directory.PathOf(destinationName),
                        ChangeSuggestionSource.DeterministicRule,
                        "Test rename.",
                        1),
                ]),
            CancellationToken.None);
        return new TestContext(plan, validator, executor, store, journal);
    }

    private sealed record TestContext(
        ChangePlan Plan,
        IChangePlanValidator Validator,
        IChangePlanExecutionService Executor,
        IChangePlanStore PlanStore,
        IOperationJournalStore Journal);

    private static IPathSemantics TestPathSemantics =>
        OperatingSystem.IsWindows()
            ? new WindowsPathSemantics()
            : new LinuxPathSemantics();

    private static IFileSystemCapabilities SupportedFileSystemCapabilities =>
        new SupportedCapabilities();

    private static PhysicalFileSystemGateway CreateGateway() =>
        new(
            TestPathSemantics,
            FileIdentityProviderFactory.CreateCurrent(),
            SupportedFileSystemCapabilities);

    private static ChangePlanValidator CreateValidator(IFileSystemGateway gateway) =>
        new(gateway, TestPathSemantics, SupportedFileSystemCapabilities);

    private sealed class SupportedCapabilities : IFileSystemCapabilities
    {
        public FileLinkInspection InspectLink(string path) =>
            new(false, null, null, "Test paths are not links.");

        public bool CanWriteDirectory(string path, out string explanation)
        {
            explanation = "The test directory is writable.";
            return true;
        }

        public long? GetAvailableFreeSpace(string path) => long.MaxValue;

        public bool AreOnSameFileSystem(
            string firstPath,
            string secondPath,
            out string explanation)
        {
            explanation = "Temporary test paths share one filesystem.";
            return true;
        }
    }

    private sealed class PartialExecutionService : IChangePlanExecutionService
    {
        public Task<ChangePlanExecutionResult> ExecuteAsync(
            ChangePlan plan,
            string initiatingFeature,
            IProgress<ChangeExecutionProgress>? progress,
            CancellationToken cancellationToken)
        {
            var action = Assert.Single(plan.Actions);
            var journalAction = new OperationJournalAction(
                action.ActionId,
                action.ActionType,
                action.SuggestionSource,
                action.SourcePath,
                action.DestinationPath,
                action.DestinationPath,
                action.SourceIdentity,
                action.SourceIdentity,
                action.ValidationState,
                JournalActionResult.RollbackFailed,
                WasSkipped: false,
                ChangeConflictCategory.IoFailure,
                "rollback failed",
                [],
                RollbackAttempted: true,
                JournalRollbackResult.Failed,
                UndoAvailable: false,
                JournalUndoStatus.NotAvailable,
                UndoTimestampUtc: null,
                UndoConflictDetails: null,
                action.AiModel,
                action.AiRequestCorrelationId,
                DirectoryCreatedByOpenSorSe: false);
            var operation = new OperationJournalRecord(
                OperationJournalSchema.CurrentVersion,
                "operation:partial",
                plan.PlanId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "2.5.0",
                OperationStatus.RollbackPartiallyFailed,
                initiatingFeature,
                plan.RootPath,
                [journalAction],
                CancellationRequested: false,
                "Rollback partially failed.");
            return Task.FromResult(new ChangePlanExecutionResult(
                operation,
                Succeeded: false,
                WasCancelled: false,
                operation.Summary));
        }

        public Task<ChangePlanUndoResult> UndoAsync(
            string operationId,
            IReadOnlyCollection<string>? actionIds,
            IProgress<ChangeExecutionProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<OperationJournalRecord>> RecoverInterruptedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OperationJournalRecord>>([]);
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string Text { get; private set; } = string.Empty;

        public Task SetTextAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"OpenSorSe.Desktop.ChangePlan.{Guid.NewGuid():N}");
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
}
