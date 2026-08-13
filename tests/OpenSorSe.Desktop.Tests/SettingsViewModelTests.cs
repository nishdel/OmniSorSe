using Microsoft.Extensions.Logging;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Media;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies configuration-backed settings presentation without application restart or filesystem work.
/// </summary>
public sealed class SettingsViewModelTests
{
    /// <summary>The optional companion path round-trips without making OmniBrille a startup dependency.</summary>
    [Fact]
    public void OmniBrillePath_RoundTripsAsOptionalAbsolutePath()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OmniBrille", "OmniBrille.exe"));
        var draft = SettingsDraft.FromSettings(new ApplicationSettings
        {
            ExplorerCompanion = new ExplorerCompanionSettings { ExecutablePath = expected },
        });

        Assert.Equal(expected, draft.OmniBrilleExecutablePath);
        Assert.Equal(expected, draft.ToSettings().ExplorerCompanion.ExecutablePath);

        draft.OmniBrilleExecutablePath = "   ";
        Assert.Null(draft.ToSettings().ExplorerCompanion.ExecutablePath);
    }

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

    /// <summary>Verifies every bounded media setting survives the editable draft round trip.</summary>
    [Fact]
    public void MediaSettings_RoundTripWithoutResettingHiddenResourceLimits()
    {
        var expected = new MediaIntelligenceSettings
        {
            ImageOcrEnabled = true,
            AudioTranscriptionEnabled = true,
            VideoFrameAnalysisEnabled = true,
            MaximumMediaFileSizeMiB = 321,
            MaximumAudioDurationMinutes = 61,
            MaximumVideoDurationMinutes = 122,
            MaximumVideoFrames = 7,
            MaximumVideoOcrFrames = 3,
            MaximumTranscriptCharacters = 12_345,
            MaximumDescriptionCharacters = 777,
            MaximumThumbnailDimension = 256,
            MaximumThumbnailSourcePixels = 50_000_000,
            ProviderTimeoutSeconds = 45,
            WhisperExecutablePath = Path.Combine(Path.GetTempPath(), "whisper-cli"),
            WhisperModelPath = Path.Combine(Path.GetTempPath(), "ggml-model.bin"),
            TranscriptionTimeoutSeconds = 750,
        };
        var draft = SettingsDraft.FromSettings(new ApplicationSettings { MediaIntelligence = expected });

        var actual = draft.ToSettings().MediaIntelligence;

        Assert.Equal(expected.MaximumThumbnailDimension, actual.MaximumThumbnailDimension);
        Assert.Equal(expected.MaximumThumbnailSourcePixels, actual.MaximumThumbnailSourcePixels);
        Assert.Equal(expected.ProviderTimeoutSeconds, actual.ProviderTimeoutSeconds);
        Assert.Equal(expected.MaximumVideoFrames, actual.MaximumVideoFrames);
        Assert.Equal(expected.MaximumTranscriptCharacters, actual.MaximumTranscriptCharacters);
        Assert.True(actual.ImageOcrEnabled);
        Assert.True(actual.AudioTranscriptionEnabled);
        Assert.True(actual.VideoFrameAnalysisEnabled);
        Assert.Equal(expected.WhisperExecutablePath, actual.WhisperExecutablePath);
        Assert.Equal(expected.WhisperModelPath, actual.WhisperModelPath);
        Assert.Equal(expected.TranscriptionTimeoutSeconds, actual.TranscriptionTimeoutSeconds);
    }

    /// <summary>Verifies all bounded local Content Intelligence choices survive settings editing.</summary>
    [Fact]
    public void ContentIntelligenceSettings_RoundTripWithoutLosingBounds()
    {
        var expected = new ContentIntelligenceSettings
        {
            TopicExtractionEnabled = false,
            EntityExtractionEnabled = true,
            SummaryGenerationEnabled = false,
            MaximumInputCharacters = 32_768,
            MaximumTopics = 9,
            MaximumEntities = 11,
            MaximumKeywords = 17,
            MaximumSummaryCharacters = 640,
            MaximumEvidenceExcerptCharacters = 192,
        };

        var actual = SettingsDraft.FromSettings(new ApplicationSettings { ContentIntelligence = expected })
            .ToSettings()
            .ContentIntelligence;

        Assert.Equal(expected.TopicExtractionEnabled, actual.TopicExtractionEnabled);
        Assert.Equal(expected.EntityExtractionEnabled, actual.EntityExtractionEnabled);
        Assert.Equal(expected.SummaryGenerationEnabled, actual.SummaryGenerationEnabled);
        Assert.Equal(expected.MaximumInputCharacters, actual.MaximumInputCharacters);
        Assert.Equal(expected.MaximumTopics, actual.MaximumTopics);
        Assert.Equal(expected.MaximumEntities, actual.MaximumEntities);
        Assert.Equal(expected.MaximumKeywords, actual.MaximumKeywords);
        Assert.Equal(expected.MaximumSummaryCharacters, actual.MaximumSummaryCharacters);
        Assert.Equal(expected.MaximumEvidenceExcerptCharacters, actual.MaximumEvidenceExcerptCharacters);
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
        Assert.Contains("OmniSorSe application diagnostic log files", viewModel.DailyLogRetentionDescription, StringComparison.Ordinal);
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
        viewModel.Draft.SearchAssistanceEnabled = true;
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
        Assert.True(configuration.Current.Ai.SearchAssistanceEnabled);
        Assert.True(configuration.Current.Ai.DocumentTextInterpretationEnabled);
        Assert.Equal("newly-selected-model", configuration.Current.Ai.SelectedModel);
        Assert.True(configuration.Current.Features.ShowAdvancedFeatures);
        Assert.Equal(300, configuration.Current.Content.PdfRasterizationDpi);
        Assert.Equal(5000, configuration.Current.Content.MaximumRasterDimension);
    }

    /// <summary>Verifies discovery reports a provider-confirmed running model without inventing state.</summary>
    [Fact]
    public async Task DiscoverModels_ShowsSelectedRunningState()
    {
        var models = new AiConnectionResult(
            AiAvailabilityState.Connected,
            "Discovered models",
            [new AiModel("local-model", "local-model") { RuntimeState = AiModelRuntimeState.Running }])
        {
            RuntimeStateAvailable = true,
        };
        using var viewModel = new SettingsViewModel(
            new TestConfigurationService(settings: AiAdvancedSettings()),
            new RecordingAiSuggestionService(discovery: models));

        await viewModel.DiscoverAiModelsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedModelAvailable);
        Assert.Contains("currently running", viewModel.SelectedModelStatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a removed persisted model falls back to a deterministic installed choice in the draft.</summary>
    [Fact]
    public async Task DiscoverModels_MissingSelectionFallsBackAndExplainsSaveRequirement()
    {
        var models = new AiConnectionResult(
            AiAvailabilityState.Connected,
            "Discovered models",
            [new AiModel("available-a", "available-a"), new AiModel("available-b", "available-b")]);
        using var viewModel = new SettingsViewModel(
            new TestConfigurationService(settings: AiAdvancedSettings()),
            new RecordingAiSuggestionService(discovery: models));

        await viewModel.DiscoverAiModelsCommand.ExecuteAsync(null);

        Assert.Equal("available-a", viewModel.Draft.SelectedAiModel);
        Assert.Contains("no longer installed", viewModel.AiStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("save Settings", viewModel.AiStatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies endpoint privacy text only calls a verified loopback URI local.</summary>
    [Theory]
    [InlineData("http://localhost:11434", "Local endpoint")]
    [InlineData("http://127.0.0.1:11434", "Local endpoint")]
    [InlineData("http://[::1]:11434", "Local endpoint")]
    [InlineData("https://ollama.example.test", "Remote endpoint")]
    [InlineData("not an endpoint", "valid HTTP or HTTPS")]
    public void AiEndpointPrivacy_ClassifiesConservatively(string endpoint, string expected)
    {
        using var viewModel = new SettingsViewModel(new TestConfigurationService());

        viewModel.Draft.AiEndpoint = endpoint;

        Assert.Contains(expected, viewModel.AiEndpointPrivacyText, StringComparison.Ordinal);
    }

    /// <summary>Verifies optional media states distinguish missing, invalid, unavailable, and ready providers.</summary>
    [Fact]
    public async Task MediaCapabilityCheck_UsesTruthfulProviderStates()
    {
        var capabilities = new[]
        {
            new MediaCapability(MediaCapabilityKind.Transcription, false, "whisper", "1", "Choose a model.")
            {
                State = MediaCapabilityState.NotConfigured,
            },
            new MediaCapability(MediaCapabilityKind.VideoMetadata, false, "ffprobe", null, "The configured path is invalid.")
            {
                State = MediaCapabilityState.InvalidConfiguration,
            },
            new MediaCapability(MediaCapabilityKind.VideoFrameSampling, false, "ffmpeg", null, "ffmpeg is unavailable."),
            new MediaCapability(MediaCapabilityKind.ImageMetadata, true, "image", "1", "Built in."),
        };
        using var viewModel = new SettingsViewModel(
            new TestConfigurationService(),
            mediaIntelligenceService: new CapabilityMediaService(capabilities));

        await viewModel.CheckMediaCapabilitiesCommand.ExecuteAsync(null);

        Assert.Contains("Transcription — Not configured", viewModel.MediaCapabilityStatusText, StringComparison.Ordinal);
        Assert.Contains("Video metadata — Invalid configuration", viewModel.MediaCapabilityStatusText, StringComparison.Ordinal);
        Assert.Contains("Video frame analysis — Unavailable", viewModel.MediaCapabilityStatusText, StringComparison.Ordinal);
        Assert.Contains("Image metadata — Available", viewModel.MediaCapabilityStatusText, StringComparison.Ordinal);
    }

    /// <summary>Verifies changing provider identity invalidates stale discovery and tells the user what to do.</summary>
    [Fact]
    public async Task AiEndpointChanged_ClearsDiscoveredModelsAndRequiresRetry()
    {
        var models = new AiConnectionResult(
            AiAvailabilityState.Connected,
            "Discovered models",
            [new AiModel("local-model", "local-model")]);
        using var viewModel = new SettingsViewModel(
            new TestConfigurationService(settings: AiAdvancedSettings()),
            new RecordingAiSuggestionService(discovery: models));
        await viewModel.DiscoverAiModelsCommand.ExecuteAsync(null);
        Assert.NotEmpty(viewModel.AvailableAiModels);

        viewModel.Draft.AiEndpoint = "https://ollama.example.test";

        Assert.Empty(viewModel.AvailableAiModels);
        Assert.Equal(AiReadinessState.NotChecked, viewModel.AiReadinessState);
        Assert.Contains("Retry the connection", viewModel.AiStatusText, StringComparison.Ordinal);
        Assert.Contains("may leave this computer", viewModel.AiEndpointPrivacyText, StringComparison.Ordinal);
    }

    /// <summary>Verifies durable indexing policy round-trips through the editable settings boundary.</summary>
    [Fact]
    public async Task SaveAsync_DeepIndexingPolicy_PersistsEveryResourceAndRetentionControl()
    {
        var configuration = new TestConfigurationService();
        using var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.DeepIndexingEnabled = true;
        viewModel.Draft.DefaultIndexingLevel = IndexingLevel.Deep;
        viewModel.Draft.InitialScanDepth = InitialScanDepth.DeepInitialAnalysis;
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
        viewModel.Draft.RelationshipAnalysisEnabled = false;
        viewModel.Draft.RelationshipExcludedExtensions = ".pem, key, .pem";
        viewModel.Draft.MaximumRelationshipCandidates = 320;
        viewModel.Draft.MaximumRelationshipsPerFile = 80;
        viewModel.Draft.MaximumSmartCollectionMembers = 1200;
        viewModel.Draft.ArchiveIndexingEnabled = true;
        viewModel.Draft.ExcludeGeneratedFolders = false;
        viewModel.Draft.BinaryAndExecutableMetadataOnly = false;
        viewModel.Draft.UseIndexingTimeWindow = true;
        viewModel.Draft.IndexingWindowStartHour = 22;
        viewModel.Draft.IndexingWindowEndHour = 6;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = configuration.Current.DeepIndexing;
        Assert.Equal(IndexingLevel.Deep, saved.DefaultLevel);
        Assert.Equal(InitialScanDepth.DeepInitialAnalysis, saved.InitialScanDepth);
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
        Assert.False(saved.RelationshipAnalysisEnabled);
        Assert.Equal([".pem", ".key"], saved.RelationshipExcludedExtensions);
        Assert.Equal(320, saved.MaximumRelationshipCandidates);
        Assert.Equal(80, saved.MaximumRelationshipsPerFile);
        Assert.Equal(1200, saved.MaximumSmartCollectionMembers);
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

    /// <summary>Verifies malformed relationship exclusions are rejected without replacing valid settings.</summary>
    [Fact]
    public async Task SaveAsync_MalformedRelationshipExclusion_IsRejected()
    {
        var configuration = new TestConfigurationService();
        using var viewModel = new SettingsViewModel(configuration);
        viewModel.Draft.RelationshipExcludedExtensions = ".";

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

    private sealed class RecordingAiSuggestionService(
        bool blockConnection = false,
        AiConnectionResult? discovery = null) : IAiSuggestionService
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
            Task.FromResult(discovery ?? new AiConnectionResult(AiAvailabilityState.NoModelsAvailable, "No models", []));

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

    private sealed class CapabilityMediaService(IReadOnlyList<MediaCapability> capabilities) : IMediaIntelligenceService
    {
        public MediaKind Classify(string fullPath) => MediaKind.None;

        public Task<IReadOnlyList<MediaCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(capabilities);
        }

        public Task<MediaIntelligenceResult> ExtractMetadataAsync(
            FileEntry file,
            IndexedMediaEvidence? existing,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MediaIntelligenceResult> ExtractAsync(
            FileEntry file,
            IndexedMediaEvidence? existing,
            bool allowOcr,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
