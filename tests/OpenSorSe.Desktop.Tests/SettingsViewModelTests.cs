using Microsoft.Extensions.Logging;
using OpenSorSe.Application.AI;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies configuration-backed settings presentation without application restart or filesystem work.
/// </summary>
public sealed class SettingsViewModelTests
{
    /// <summary>Verifies settings corruption recovery is visible rather than silently using defaults.</summary>
    [Fact]
    public void Constructor_ExposesConfigurationRecoveryWarning()
    {
        const string warning = "Invalid owned settings were preserved; defaults are active.";
        using var viewModel = new SettingsViewModel(new TestConfigurationService(warning));

        Assert.Equal(warning, viewModel.StatusText);
    }

    /// <summary>Verifies safe defaults and the settings hierarchy hide all subordinate AI and advanced controls.</summary>
    [Fact]
    public void Constructor_DefaultsHideSubordinateAiAndAdvancedSettings()
    {
        using var viewModel = new SettingsViewModel(new TestConfigurationService());

        Assert.False(viewModel.Draft.AiEnabled);
        Assert.False(viewModel.Draft.ShowAdvancedFeatures);
        Assert.False(viewModel.IsAiCapabilitySettingsVisible);
        Assert.False(viewModel.IsAdvancedSettingsVisible);
        Assert.False(viewModel.IsAdvancedAiSettingsVisible);
        Assert.False(viewModel.TestAiConnectionCommand.CanExecute(null));
    }

    /// <summary>Verifies independent switches update draft visibility without resetting hidden provider values.</summary>
    [Fact]
    public void DraftMasterSwitches_UpdateHierarchyAndPreserveHiddenValues()
    {
        using var viewModel = new SettingsViewModel(new TestConfigurationService());
        viewModel.Draft.SelectedAiModel = "kept-model";
        viewModel.Draft.AiEndpoint = "http://127.0.0.1:12345";

        viewModel.Draft.AiEnabled = true;
        Assert.True(viewModel.IsAiCapabilitySettingsVisible);
        Assert.False(viewModel.IsAdvancedAiSettingsVisible);
        viewModel.Draft.ShowAdvancedFeatures = true;
        Assert.True(viewModel.IsAdvancedSettingsVisible);
        Assert.True(viewModel.IsAdvancedAiSettingsVisible);
        viewModel.Draft.AiEnabled = false;

        Assert.False(viewModel.IsAiCapabilitySettingsVisible);
        Assert.False(viewModel.IsAdvancedAiSettingsVisible);
        Assert.Equal("kept-model", viewModel.Draft.SelectedAiModel);
        Assert.Equal("http://127.0.0.1:12345", viewModel.Draft.AiEndpoint);
    }

    /// <summary>
    /// Verifies a valid draft persists through the centralized configuration service and requests restart.
    /// </summary>
    [Fact]
    public async Task SaveAsync_PersistsValidatedDraftAndMarksRestartRequired()
    {
        var configuration = new TestConfigurationService();
        var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.MinimumLogLevel = LogLevel.Warning;
        viewModel.Draft.FileLoggingEnabled = false;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, configuration.ReplacementSaveCount);
        Assert.Equal(LogLevel.Warning, configuration.Current.Logging.MinimumLevel);
        Assert.False(configuration.Current.Logging.FileLoggingEnabled);
        Assert.True(viewModel.RestartRequired);
    }

    /// <summary>
    /// Verifies invalid input is retained for correction and is not persisted.
    /// </summary>
    [Fact]
    public async Task SaveAsync_InvalidDraft_DoesNotPersist()
    {
        var configuration = new TestConfigurationService();
        var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.RetainedFileCount = 0;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, configuration.ReplacementSaveCount);
        Assert.Equal("Logging settings are invalid.", viewModel.StatusText);
        Assert.False(viewModel.RestartRequired);
    }

    /// <summary>Verifies a custom log path must remain absolute even while file logging is disabled.</summary>
    [Fact]
    public async Task SaveAsync_DisabledLoggingWithRelativePath_DoesNotPersistLatentInvalidConfiguration()
    {
        var configuration = new TestConfigurationService();
        var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.FileLoggingEnabled = false;
        viewModel.Draft.LogDirectoryPath = "relative-logs";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, configuration.ReplacementSaveCount);
        Assert.Equal("Logging settings are invalid.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies restore and discard produce drafts without saving configuration.
    /// </summary>
    [Fact]
    public void RestoreAndDiscard_ChangeOnlyTheEditableDraft()
    {
        var configuration = new TestConfigurationService();
        var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.RetainedFileCount = 2;
        viewModel.Draft.FilesPageDetailsPanelWidthRatio = 0.44;

        viewModel.RestoreDefaultsCommand.Execute(null);
        Assert.Equal(7, viewModel.Draft.RetainedFileCount);
        Assert.Equal(
            FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio,
            viewModel.Draft.FilesPageDetailsPanelWidthRatio);
        viewModel.Draft.RetainedFileCount = 2;
        viewModel.Draft.FilesPageDetailsPanelWidthRatio = 0.44;
        viewModel.CancelCommand.Execute(null);

        Assert.Equal(configuration.Current.Logging.RetainedFileCount, viewModel.Draft.RetainedFileCount);
        Assert.Equal(
            configuration.Current.Features.FilesPageDetailsPanelWidthRatio,
            viewModel.Draft.FilesPageDetailsPanelWidthRatio);
        Assert.Equal(0, configuration.ReplacementSaveCount);
    }

    /// <summary>
    /// Verifies the daily diagnostic-log retention setting has permanent user-facing context and validation guidance.
    /// </summary>
    [Fact]
    public void Constructor_ExposesDailyDiagnosticLogRetentionContext()
    {
        var viewModel = new SettingsViewModel(new TestConfigurationService());

        Assert.Equal("Daily diagnostic log files to retain", viewModel.DailyLogRetentionLabel);
        Assert.Contains("OpenSorSe application diagnostic log files", viewModel.DailyLogRetentionDescription, StringComparison.Ordinal);
        Assert.Contains("does not affect scanned user files", viewModel.DailyLogRetentionDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Enter a whole number of at least 1.", viewModel.DailyLogRetentionValidation);
    }

    /// <summary>Verifies owned AI decision history cannot be cleared without a separate explicit confirmation.</summary>
    [Fact]
    public async Task PreferenceHistoryReset_RequiresConfirmationAndCanBeCancelled()
    {
        var ai = new RecordingAiSuggestionService();
        using var viewModel = new SettingsViewModel(new TestConfigurationService(settings: AiAdvancedSettings()), ai);

        viewModel.RequestPreferenceHistoryResetCommand.Execute(null);
        Assert.True(viewModel.IsPreferenceHistoryResetPending);
        Assert.Equal(0, ai.ResetCallCount);

        viewModel.CancelPreferenceHistoryResetCommand.Execute(null);
        Assert.False(viewModel.IsPreferenceHistoryResetPending);
        Assert.Equal(0, ai.ResetCallCount);

        viewModel.RequestPreferenceHistoryResetCommand.Execute(null);
        await viewModel.ConfirmPreferenceHistoryResetCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsPreferenceHistoryResetPending);
        Assert.Equal(1, ai.ResetCallCount);
        Assert.Contains("No scanned file", viewModel.StatusText, StringComparison.Ordinal);
    }

    /// <summary>Verifies a user cancellation reaches active optional AI work and leaves commands usable.</summary>
    [Fact]
    public async Task TestAiConnection_CancelCommand_CancelsAndPreventsStalePublication()
    {
        var ai = new RecordingAiSuggestionService(blockConnection: true);
        using var viewModel = new SettingsViewModel(new TestConfigurationService(settings: AiAdvancedSettings()), ai);

        var running = viewModel.TestAiConnectionCommand.ExecuteAsync(null);
        await ai.ConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(viewModel.IsAiBusy);

        viewModel.CancelAiOperationCommand.Execute(null);
        await running;

        Assert.False(viewModel.IsAiBusy);
        Assert.Equal(AiAvailabilityState.RequestCancelled, viewModel.AiAvailabilityState);
        Assert.True(viewModel.TestAiConnectionCommand.CanExecute(null));
    }

    /// <summary>Verifies capability values persist independently and visibility-only changes do not require restart.</summary>
    [Fact]
    public async Task SaveAsync_FeatureSwitches_PersistIndependentlyWithoutLoggingRestart()
    {
        var configuration = new TestConfigurationService();
        using var viewModel = new SettingsViewModel(configuration);
        ApplicationSettings? published = null;
        viewModel.SettingsSaved += (_, settings) => published = settings;
        viewModel.Draft.AiEnabled = true;
        viewModel.Draft.FileRenameSuggestionsEnabled = true;
        viewModel.Draft.FolderStructureSuggestionsEnabled = false;
        viewModel.Draft.DocumentTextInterpretationEnabled = true;
        viewModel.Draft.SelectedAiModel = "newly-selected-model";
        viewModel.Draft.ShowAdvancedFeatures = true;
        viewModel.Draft.PdfRasterizationDpi = 300;
        viewModel.Draft.MaximumRasterDimension = 5000;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(viewModel.RestartRequired);
        Assert.Same(configuration.Current, published);
        Assert.True(configuration.Current.Ai.Enabled);
        Assert.True(configuration.Current.Ai.FileRenameSuggestionsEnabled);
        Assert.False(configuration.Current.Ai.FolderStructureSuggestionsEnabled);
        Assert.True(configuration.Current.Ai.DocumentTextInterpretationEnabled);
        Assert.Equal("newly-selected-model", configuration.Current.Ai.SelectedModel);
        Assert.True(configuration.Current.Features.ShowAdvancedFeatures);
        Assert.Equal(300, configuration.Current.Content.PdfRasterizationDpi);
        Assert.Equal(5000, configuration.Current.Content.MaximumRasterDimension);
    }

    /// <summary>Verifies durable indexing policy round-trips through the editable settings boundary.</summary>
    [Fact]
    public async Task SaveAsync_DeepIndexingPolicy_PersistsEveryResourceAndRetentionControl()
    {
        var configuration = new TestConfigurationService();
        using var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.DeepIndexingEnabled = true;
        viewModel.Draft.DefaultIndexingLevel = IndexingLevel.Deep;
        viewModel.Draft.IndexingResourceMode = IndexingResourceMode.Eco;
        viewModel.Draft.MaximumIndexSizeMiB = 2048;
        viewModel.Draft.MaximumExtractedTextCharacters = 262_144;
        viewModel.Draft.MaximumDeepOcrTextCharacters = 131_072;
        viewModel.Draft.MaximumSemanticChunksPerDocument = 12;
        viewModel.Draft.DeletedFileRetentionDays = 45;
        viewModel.Draft.FailedJobHistoryRetentionDays = 21;
        viewModel.Draft.MaximumIndexingRetryCount = 5;
        viewModel.Draft.MaximumIndexingConcurrency = 2;
        viewModel.Draft.ProcessIndexOnlyWhileIdle = true;
        viewModel.Draft.ProcessIndexOnlyOnPower = true;
        viewModel.Draft.PauseIndexingOnLowBattery = true;
        viewModel.Draft.PauseBelowBatteryPercentage = 25;
        viewModel.Draft.DeepOcrProcessingEnabled = true;
        viewModel.Draft.DeepAiProcessingEnabled = true;
        viewModel.Draft.DeepSummaryProcessingEnabled = false;
        viewModel.Draft.DeepSemanticProcessingEnabled = false;
        viewModel.Draft.ArchiveIndexingEnabled = true;
        viewModel.Draft.ExcludeGeneratedFolders = false;
        viewModel.Draft.BinaryAndExecutableMetadataOnly = false;
        viewModel.Draft.UseIndexingTimeWindow = true;
        viewModel.Draft.IndexingWindowStartHour = 22;
        viewModel.Draft.IndexingWindowEndHour = 6;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = configuration.Current.DeepIndexing;
        Assert.Equal(IndexingLevel.Deep, saved.DefaultLevel);
        Assert.Equal(IndexingResourceMode.Eco, saved.ResourceMode);
        Assert.Equal(2048, saved.MaximumIndexSizeMiB);
        Assert.Equal(262_144, saved.MaximumExtractedTextCharacters);
        Assert.Equal(131_072, saved.MaximumOcrTextCharacters);
        Assert.Equal(12, saved.MaximumSemanticChunksPerDocument);
        Assert.Equal(45, saved.DeletedFileRetentionDays);
        Assert.Equal(21, saved.FailedJobHistoryRetentionDays);
        Assert.Equal(5, saved.MaximumRetryCount);
        Assert.Equal(2, saved.MaximumConcurrency);
        Assert.True(saved.ProcessOnlyWhileIdle);
        Assert.True(saved.ProcessOnlyWhileConnectedToPower);
        Assert.Equal(25, saved.PauseBelowBatteryPercentage);
        Assert.True(saved.OcrProcessingEnabled);
        Assert.True(saved.AiProcessingEnabled);
        Assert.False(saved.SummaryProcessingEnabled);
        Assert.False(saved.SemanticProcessingEnabled);
        Assert.True(saved.ArchiveIndexingEnabled);
        Assert.False(saved.ExcludeGeneratedFolders);
        Assert.False(saved.BinaryAndExecutableMetadataOnly);
        Assert.Equal(22, saved.ProcessingWindowStartHour);
        Assert.Equal(6, saved.ProcessingWindowEndHour);
    }

    /// <summary>Verifies an unsafe storage quota is rejected without replacing valid settings.</summary>
    [Fact]
    public async Task SaveAsync_DeepIndexingQuotaBelowMinimum_IsRejected()
    {
        var configuration = new TestConfigurationService();
        using var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.MaximumIndexSizeMiB = 15;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, configuration.ReplacementSaveCount);
        Assert.Equal("Background indexing settings are invalid.", viewModel.StatusText);
    }

    /// <summary>Verifies both documented timeout boundaries persist and out-of-range text is rejected.</summary>
    [Theory]
    [InlineData("5", true)]
    [InlineData("300", true)]
    [InlineData("4", false)]
    [InlineData("301", false)]
    [InlineData("not-a-number", false)]
    public async Task SaveAsync_AiTimeoutText_EnforcesFiveThroughThreeHundredSeconds(string text, bool expectedSaved)
    {
        var configuration = new TestConfigurationService();
        using var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.AiRequestTimeoutText = text;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expectedSaved ? 1 : 0, configuration.ReplacementSaveCount);
        if (expectedSaved)
        {
            Assert.Equal(int.Parse(text, System.Globalization.CultureInfo.InvariantCulture), configuration.Current.Ai.RequestTimeoutSeconds);
        }
        else
        {
            Assert.Contains("5", viewModel.StatusText, StringComparison.Ordinal);
            Assert.Contains("300", viewModel.StatusText, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies raw AI diagnostics require AI, Advanced mode, and the independent opt-in without resetting it.</summary>
    [Fact]
    public async Task SaveAsync_AiDiagnostics_RequiresBothMasterFlagsAndClearsWhenHidden()
    {
        var initial = new ApplicationSettings
        {
            Ai = new AiSettings
            {
                Enabled = true,
                RequestDiagnosticsEnabled = true,
            },
        };
        var configuration = new TestConfigurationService(settings: initial);
        var diagnostics = new AiRequestDiagnosticsStore();
        diagnostics.SetEnabled(true);
        using var viewModel = new SettingsViewModel(configuration, aiRequestDiagnosticsStore: diagnostics);
        Assert.False(diagnostics.IsEnabled);

        viewModel.Draft.ShowAdvancedFeatures = true;
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.True(diagnostics.IsEnabled);

        viewModel.Draft.ShowAdvancedFeatures = false;
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.False(diagnostics.IsEnabled);
        Assert.True(configuration.Current.Ai.RequestDiagnosticsEnabled);
    }

    /// <summary>Saving diagnostics applies redaction mode and disabling clears live history.</summary>
    [Fact]
    public async Task SaveAsync_LiveAiDiagnostics_AppliesPrivacyAndClearsWhenDisabled()
    {
        var configuration = new TestConfigurationService(settings: AiAdvancedSettings());
        var collector = new AiDiagnosticsCollector();
        using var viewModel = new SettingsViewModel(configuration, aiDiagnosticsCollector: collector);
        viewModel.Draft.AiRequestDiagnosticsEnabled = true;
        viewModel.Draft.ShowUnredactedAiDiagnosticContent = true;

        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.True(collector.IsEnabled);
        Assert.True(collector.ShowUnredactedContent);
        Assert.NotNull(collector.Begin(AiSuggestionKind.FileRename, "model", "http://127.0.0.1:11434"));

        viewModel.Draft.AiRequestDiagnosticsEnabled = false;
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.False(collector.IsEnabled);
        Assert.Empty(collector.GetRecent());
    }

    /// <summary>Saving the common master switch applies every category and clears retained content when disabled.</summary>
    [Fact]
    public async Task SaveAsync_CommonDiagnosticsMaster_DisablingClearsHistory()
    {
        var configuration = new TestConfigurationService(settings: AiAdvancedSettings());
        var collector = new InMemoryDiagnosticsCollector();
        using var viewModel = new SettingsViewModel(
            configuration,
            diagnosticsCollector: collector);
        viewModel.Draft.DiagnosticsEnabled = true;
        viewModel.Draft.ScanningDiagnosticsEnabled = true;

        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(collector.BeginSession(DiagnosticCategory.Scanning, "Scan"));
        Assert.Single(collector.GetRecent());

        viewModel.Draft.DiagnosticsEnabled = false;
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(collector.IsEnabled);
        Assert.Empty(collector.GetRecent());
        Assert.Null(collector.BeginSession(DiagnosticCategory.Scanning, "Disabled"));
    }

    private static ApplicationSettings AiAdvancedSettings() => new()
    {
        Features = new FeatureSettings { ShowAdvancedFeatures = true },
        Ai = new AiSettings
        {
            Enabled = true,
            FileRenameSuggestionsEnabled = true,
            FolderStructureSuggestionsEnabled = true,
            SelectedModel = "local-model",
        },
    };

    private sealed class TestConfigurationService(string? initializationWarning = null, ApplicationSettings? settings = null) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = settings ?? new();

        public string? InitializationWarning { get; } = initializationWarning;

        public int ReplacementSaveCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            ReplacementSaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAiSuggestionService(bool blockConnection = false) : IAiSuggestionService
    {
        public TaskCompletionSource<bool> ConnectionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ResetCallCount { get; private set; }

        public async Task<AiConnectionResult> TestConnectionAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            ConnectionStarted.TrySetResult(true);
            if (blockConnection)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new AiConnectionResult(AiAvailabilityState.Connected, "Connected", []);
        }

        public Task<AiConnectionResult> DiscoverModelsAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiConnectionResult(AiAvailabilityState.NoModelsAvailable, "No models", []));

        public Task<AiFileRenameResult> GenerateFileRenameAsync(
            AiFileRenameRequest request,
            AiSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiFileRenameResult(AiAvailabilityState.Disabled, "Disabled", null));

        public Task<AiFolderStructureResult> GenerateFolderStructureAsync(
            AiFolderStructureRequest request,
            AiSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiFolderStructureResult(AiAvailabilityState.Disabled, "Disabled", null));

        public Task<AiDecisionResult> RecordDecisionAsync(AiSuggestionDecision decision, AiSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiDecisionResult(AiAvailabilityState.ModelSelected, "Saved"));

        public Task<AiDecisionResult> ResetDecisionHistoryAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetCallCount++;
            return Task.FromResult(new AiDecisionResult(AiAvailabilityState.ModelSelected, "Local AI review history was reset. No scanned file changed."));
        }
    }
}
