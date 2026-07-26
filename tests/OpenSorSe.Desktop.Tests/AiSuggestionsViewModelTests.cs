using OpenSorSe.Application.AI;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies Results AI visibility, command enforcement, and review-only state.</summary>
public sealed class AiSuggestionsViewModelTests
{
    /// <summary>Verifies disabled AI remains hidden and cannot invoke an injected service through commands.</summary>
    [Fact]
    public async Task DisabledAi_WithValidContext_IsHiddenAndBlocked()
    {
        var configuration = new MutableConfigurationService(new ApplicationSettings());
        var service = new RecordingService();
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();

        viewModel.SetContext(file, CreateSnapshot(file), [file]);
        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);
        await viewModel.GenerateFolderStructureCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsFileRenameVisible);
        Assert.False(viewModel.IsFolderStructureVisible);
        Assert.False(viewModel.GenerateSuggestionCommand.CanExecute(null));
        Assert.Equal(0, service.RenameCallCount);
        Assert.Equal(0, service.FolderCallCount);
    }

    /// <summary>Verifies capability switches are independent in panel and command state.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EnabledAi_CapabilitySwitchesControlIndependentSections(bool rename, bool folder)
    {
        var configuration = new MutableConfigurationService(Settings(rename, folder));
        using var viewModel = new AiSuggestionsViewModel(configuration, new RecordingService());
        var file = CreateFile();

        viewModel.SetContext(file, CreateSnapshot(file), [file]);

        Assert.True(viewModel.IsVisible);
        Assert.Equal(rename, viewModel.IsFileRenameVisible);
        Assert.Equal(folder, viewModel.IsFolderStructureVisible);
        Assert.Equal(rename, viewModel.GenerateSuggestionCommand.CanExecute(null));
        Assert.Equal(folder, viewModel.GenerateFolderStructureCommand.CanExecute(null));
    }

    /// <summary>Verifies an oversized results-page selection is visibly blocked and no files can be silently omitted.</summary>
    [Fact]
    public async Task FolderSuggestion_MoreThanTwelveFiles_IsBlockedWithExactCount()
    {
        var configuration = new MutableConfigurationService(Settings(rename: false, folder: true));
        var service = new RecordingService();
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var files = Enumerable.Range(1, AiPromptLimits.MaximumFolderStructureFiles + 1)
            .Select(index => CreateFile($"file:{index}", $"invoice-{index}.pdf"))
            .ToArray();

        viewModel.SetContext(files[0], CreateSnapshot(files[0]), files);
        await viewModel.GenerateFolderStructureCommand.ExecuteAsync(null);

        Assert.False(viewModel.GenerateFolderStructureCommand.CanExecute(null));
        Assert.Contains(files.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), viewModel.FolderStructureContextText, StringComparison.Ordinal);
        Assert.Contains("none will be sent", viewModel.FolderStructureContextText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AiPromptBuilder.FolderStructureTaskId, viewModel.AiRequestContextText, StringComparison.Ordinal);
        Assert.Equal(0, service.FolderCallCount);
    }

    /// <summary>Verifies a generated rename remains editable and acceptance records only a local decision.</summary>
    [Fact]
    public async Task RenameSuggestion_EditAndAccept_RecordsDecisionWithoutFilesystemAction()
    {
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        var service = new RecordingService();
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();
        viewModel.SetContext(file, CreateSnapshot(file), [file]);

        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);
        viewModel.ProposedFileName = "edited.pdf";
        await viewModel.AcceptRenameCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.RenameSuggestion);
        Assert.Equal("edited.pdf", service.RecordedDecision?.FinalValue);
        Assert.Equal(AiSuggestionDecisionOutcome.Edited, service.RecordedDecision?.Outcome);
        Assert.Contains("No file", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies an edit back to the current filename is treated as no change and is not recorded.</summary>
    [Fact]
    public async Task RenameSuggestion_EditToCurrentName_DoesNotRecordDecision()
    {
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        var service = new RecordingService();
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();
        viewModel.SetContext(file, CreateSnapshot(file), [file]);
        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);

        viewModel.ProposedFileName = file.DisplayFileName;
        await viewModel.AcceptRenameCommand.ExecuteAsync(null);

        Assert.Null(service.RecordedDecision);
        Assert.Contains("does not propose a change", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies disabling active AI clears previously generated suggestions and blocks stale commands.</summary>
    [Fact]
    public async Task RefreshFeatureAvailability_AfterDisable_ClearsSuggestionsAndCommands()
    {
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        var service = new RecordingService();
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();
        viewModel.SetContext(file, CreateSnapshot(file), [file]);
        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasRenameSuggestion);

        configuration.Current = new ApplicationSettings();
        viewModel.RefreshFeatureAvailability();

        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.HasRenameSuggestion);
        Assert.False(viewModel.AcceptRenameCommand.CanExecute(null));
    }

    /// <summary>Verifies a context change cancels work and prevents a proposal for the old file from becoming reviewable.</summary>
    [Fact]
    public async Task SetContext_DuringRenameRequest_PreventsStaleSuggestionPublication()
    {
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        var service = new RecordingService { RenameCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var first = CreateFile();
        var second = CreateFile("file:2", "other.pdf");
        viewModel.SetContext(first, CreateSnapshot(first), [first]);

        var operation = viewModel.GenerateSuggestionCommand.ExecuteAsync(null);
        await service.RenameStarted.Task;
        viewModel.SetContext(second, CreateSnapshot(second), [second]);
        service.RenameCompletion!.SetResult(RenameResult(first.Id));
        await operation;

        Assert.Null(viewModel.RenameSuggestion);
        Assert.Equal(second.Id, viewModel.SelectedFile?.Id);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>Verifies the rename action explains common user-correctable unavailable states.</summary>
    [Fact]
    public void RenameAvailability_ExplainsSelectionCapabilityModelAndUnsupportedItem()
    {
        var service = new RecordingService();
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();
        var snapshot = CreateSnapshot(file);

        viewModel.SetContext(null, snapshot, [file]);
        Assert.Contains("Choose a file", viewModel.RenameActionAvailabilityText, StringComparison.OrdinalIgnoreCase);

        configuration.Current = new ApplicationSettings();
        viewModel.RefreshFeatureAvailability();
        viewModel.SetContext(file, snapshot, [file]);
        Assert.Contains("AI features are off", viewModel.RenameActionAvailabilityText, StringComparison.OrdinalIgnoreCase);

        configuration.Current = Settings(rename: false, folder: true);
        viewModel.RefreshFeatureAvailability();
        Assert.Contains("Rename suggestions are off", viewModel.RenameActionAvailabilityText, StringComparison.OrdinalIgnoreCase);

        configuration.Current = Settings(rename: true, folder: false, model: null);
        viewModel.RefreshFeatureAvailability();
        Assert.Contains("Choose an installed AI model", viewModel.RenameActionAvailabilityText, StringComparison.OrdinalIgnoreCase);

        configuration.Current = Settings(rename: true, folder: false);
        viewModel.RefreshFeatureAvailability();
        var unsupported = CreateFile("file:unsafe", "..");
        viewModel.SetContext(unsupported, CreateSnapshot(unsupported), [unsupported]);
        Assert.Contains("not have a supported filename", viewModel.RenameActionAvailabilityText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a cancelled request returns to idle and a later request uses a fresh cancellation source.</summary>
    [Fact]
    public async Task Rename_CancelThenRetry_Succeeds()
    {
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        var service = new RecordingService();
        service.RenameBehaviors.Enqueue(async (request, _, cancellationToken) =>
        {
            service.RenameStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return RenameResult(request.File.Id);
        });
        service.RenameBehaviors.Enqueue((request, _, _) => Task.FromResult(RenameResult(request.File.Id)));
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();
        viewModel.SetContext(file, CreateSnapshot(file), [file]);

        var cancelled = viewModel.GenerateSuggestionCommand.ExecuteAsync(null);
        await service.RenameStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelAiOperationCommand.Execute(null);
        await cancelled;

        Assert.False(viewModel.IsBusy);
        Assert.Equal(AiReadinessState.Cancelled, viewModel.ReadinessState);
        Assert.True(viewModel.GenerateSuggestionCommand.CanExecute(null));

        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.HasRenameSuggestion);
        Assert.Equal(2, service.RenameCallCount);
        Assert.Equal(AiReadinessState.Ready, viewModel.ReadinessState);
    }

    /// <summary>Verifies failed, timed-out, and malformed results remain retryable and do not retain stale failure state.</summary>
    [Theory]
    [InlineData(AiAvailabilityState.Unavailable)]
    [InlineData(AiAvailabilityState.ResponseInvalid)]
    public async Task Rename_ControlledFailureThenRetry_Succeeds(AiAvailabilityState failureState)
    {
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        var service = new RecordingService();
        service.RenameBehaviors.Enqueue((_, _, _) =>
            Task.FromResult(new AiFileRenameResult(failureState, "Request failed safely.", null)));
        service.RenameBehaviors.Enqueue((request, _, _) => Task.FromResult(RenameResult(request.File.Id)));
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();
        viewModel.SetContext(file, CreateSnapshot(file), [file]);

        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasRenameSuggestion);
        Assert.True(viewModel.GenerateSuggestionCommand.CanExecute(null));

        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasRenameSuggestion);
        Assert.Equal(AiReadinessState.Ready, viewModel.ReadinessState);
    }

    /// <summary>Verifies retry distinguishes server and model readiness and can recover from an unavailable server.</summary>
    [Fact]
    public async Task RetryConnection_UnavailableThenReady_RefreshesReadiness()
    {
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        var service = new RecordingService
        {
            ConnectionResult = new AiConnectionResult(AiAvailabilityState.Unavailable, "Not running", []),
        };
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();
        viewModel.SetContext(file, CreateSnapshot(file), [file]);

        await viewModel.RetryConnectionCommand.ExecuteAsync(null);
        Assert.Equal(AiReadinessState.ServerUnavailable, viewModel.ReadinessState);
        Assert.Contains("not running", viewModel.ReadinessText, StringComparison.OrdinalIgnoreCase);

        service.ConnectionResult = new AiConnectionResult(AiAvailabilityState.Connected, "Connected", []);
        service.ModelsResult = new AiConnectionResult(
            AiAvailabilityState.Connected,
            "Models",
            [new AiModel("local-model", "local-model")]);
        await viewModel.RetryConnectionCommand.ExecuteAsync(null);

        Assert.Equal(AiReadinessState.Ready, viewModel.ReadinessState);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>Verifies changing the configured model after failure affects the very next outgoing request.</summary>
    [Fact]
    public async Task Rename_ModelChangedAfterFailure_UsesNewModel()
    {
        var configuration = new MutableConfigurationService(Settings(rename: true, folder: false));
        var service = new RecordingService();
        service.RenameBehaviors.Enqueue((_, _, _) => throw new InvalidOperationException("Simulated failure."));
        service.RenameBehaviors.Enqueue((request, _, _) => Task.FromResult(RenameResult(request.File.Id, "new-model")));
        using var viewModel = new AiSuggestionsViewModel(configuration, service);
        var file = CreateFile();
        viewModel.SetContext(file, CreateSnapshot(file), [file]);
        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);

        configuration.Current = Settings(rename: true, folder: false, model: "new-model");
        viewModel.RefreshFeatureAvailability();
        await viewModel.GenerateSuggestionCommand.ExecuteAsync(null);

        Assert.Equal("new-model", service.RecordedModels.Last());
        Assert.Equal("new-model", viewModel.ActualModelUsed);
        Assert.Equal(AiReadinessState.Ready, viewModel.ReadinessState);
    }

    private static ApplicationSettings Settings(bool rename, bool folder, string? model = "local-model") => new()
    {
        Ai = new AiSettings
        {
            Enabled = true,
            FileRenameSuggestionsEnabled = rename,
            FolderStructureSuggestionsEnabled = folder,
            SelectedModel = model,
        },
    };

    private static ResultFile CreateFile(string id = "file:1", string name = "invoice.pdf") => new(
        id,
        $"C:\\Selected\\{name}",
        name,
        ".pdf",
        10,
        DateTimeOffset.UnixEpoch,
        FileCategory.Document,
        "Document",
        DuplicateStatus.Unique,
        null,
        false);

    private static ResultsSnapshot CreateSnapshot(ResultFile file) => new(
        "session:1",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        [file],
        [new ResultDirectory("C:\\Selected", "Selected")],
        [],
        [],
        [],
        new ResultsSnapshotStatistics(1, 1, 0, 0, 0, 0, 0),
        true);

    private sealed class MutableConfigurationService(ApplicationSettings settings) : IConfigurationService
    {
        public ApplicationSettings Current { get; set; } = settings;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingService : IAiSuggestionService
    {
        public int RenameCallCount { get; private set; }

        public int FolderCallCount { get; private set; }

        public AiSuggestionDecision? RecordedDecision { get; private set; }

        public TaskCompletionSource<bool> RenameStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AiFileRenameResult>? RenameCompletion { get; init; }

        public Queue<Func<AiFileRenameRequest, AiSettings, CancellationToken, Task<AiFileRenameResult>>> RenameBehaviors { get; } = [];

        public List<string?> RecordedModels { get; } = [];

        public AiConnectionResult ConnectionResult { get; set; } =
            new(AiAvailabilityState.Connected, "Connected", []);

        public AiConnectionResult ModelsResult { get; set; } =
            new(AiAvailabilityState.Connected, "Connected", []);

        public Task<AiConnectionResult> TestConnectionAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionResult);

        public Task<AiConnectionResult> DiscoverModelsAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(ModelsResult);

        public async Task<AiFileRenameResult> GenerateFileRenameAsync(AiFileRenameRequest request, AiSettings settings, CancellationToken cancellationToken)
        {
            RenameCallCount++;
            RecordedModels.Add(settings.SelectedModel);
            RenameStarted.TrySetResult(true);
            if (RenameBehaviors.TryDequeue(out var behavior))
            {
                return await behavior(request, settings, cancellationToken);
            }

            return RenameCompletion is null
                ? RenameResult(request.File.Id)
                : await RenameCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task<AiFolderStructureResult> GenerateFolderStructureAsync(AiFolderStructureRequest request, AiSettings settings, CancellationToken cancellationToken)
        {
            FolderCallCount++;
            return Task.FromResult(new AiFolderStructureResult(AiAvailabilityState.NoSuggestion, "No suggestion", null));
        }

        public Task<AiDecisionResult> RecordDecisionAsync(AiSuggestionDecision decision, AiSettings settings, CancellationToken cancellationToken)
        {
            RecordedDecision = decision;
            return Task.FromResult(new AiDecisionResult(AiAvailabilityState.ModelSelected, "The local review decision was saved. No file or folder was changed."));
        }

        public Task<AiDecisionResult> ResetDecisionHistoryAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiDecisionResult(AiAvailabilityState.ModelSelected, "Reset"));
    }

    private static AiFileRenameResult RenameResult(string sourceFileId, string model = "local-model") => new(
        AiAvailabilityState.ModelSelected,
        "Review only",
        new AiFileRenameSuggestion("suggestion:1", sourceFileId, "renamed.pdf", "Clearer", 0.5, "Ollama", model, DateTimeOffset.UnixEpoch));
}
