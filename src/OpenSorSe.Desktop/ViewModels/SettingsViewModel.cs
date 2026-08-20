using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Resilience;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Edits the supported application settings and persists only through the configuration service.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly IAiSuggestionService? _aiSuggestionService;
    private readonly IAiRequestDiagnosticsStore? _aiRequestDiagnosticsStore;
    private readonly IAiDiagnosticsCollector? _aiDiagnosticsCollector;
    private readonly IDiagnosticsCollector? _diagnosticsCollector;
    private readonly IContentStore? _contentStore;
    private readonly IOcrService? _ocrService;
    private readonly IMediaIntelligenceService? _mediaIntelligenceService;
    private readonly IStateBackupService? _stateBackupService;
    private readonly IOperationalHealthService? _healthService;
    private readonly ObservableCollection<string> _availableAiModels = [];
    private readonly HashSet<string> _installedAiModelIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AiModelRuntimeState> _aiModelRuntimeStates = new(StringComparer.Ordinal);
    private SettingsDraft _draft;
    private bool _restartRequired;
    private bool _isAiBusy;
    private bool _isPreferenceHistoryResetPending;
    private bool _isContentBusy;
    private bool _isContentCacheResetPending;
    private CancellationTokenSource? _aiOperationCancellation;
    private long _aiOperationVersion;
    private bool _isDisposed;
    private bool _hasCompletedModelDiscovery;
    private bool _isAiRuntimeStateAvailable;
    private AiReadinessState _aiReadinessState = AiReadinessState.NotConfigured;
    private string _statusText = "Ready";
    private StatusPresentation _status = StatusPresentation.Information("Ready");
    private StatusPresentation _aiStatus = StatusPresentation.Information("AI assistance is disabled until enabled and configured.");
    private StatusPresentation _contentStatus = StatusPresentation.Information("OCR is disabled by default. Capability has not been checked.");
    private string _mediaCapabilityStatusText = "Media capabilities have not been checked. Image metadata is built in; audio/video tools are optional.";
    private string _operationalHealthText = "Data and index health has not been checked.";
    private string _stateTransferStatusText = "No state export or restore is active.";
    private bool _isStateTransferBusy;
    private StateRestorePreview? _pendingRestore;
    private string? _pendingRestorePath;

    /// <summary>
    /// Initializes a settings editor over the already initialized configuration service.
    /// </summary>
    /// <param name="configurationService">The centralized configuration service.</param>
    /// <param name="aiSuggestionService">The optional application-owned local AI suggestion service.</param>
    /// <param name="aiRequestDiagnosticsStore">The optional bounded session diagnostics store.</param>
    /// <param name="contentStore">The optional application-owned local content cache.</param>
    /// <param name="ocrService">The optional local OCR capability service.</param>
    /// <param name="aiDiagnosticsCollector">The optional live process-session diagnostics collector.</param>
    /// <param name="diagnosticsCollector">The shared process-session advanced-diagnostics collector.</param>
    /// <param name="plugins">The optional local plugin-management presentation model.</param>
    /// <param name="platformDiagnostics">The optional platform capability and location presentation model.</param>
    /// <param name="mediaIntelligenceService">The optional provider-neutral media capability service.</param>
    /// <param name="stateBackupService">The optional logical state export/restore service.</param>
    /// <param name="healthService">The optional bounded operational-health service.</param>
    public SettingsViewModel(
        IConfigurationService configurationService,
        IAiSuggestionService? aiSuggestionService = null,
        IAiRequestDiagnosticsStore? aiRequestDiagnosticsStore = null,
        IContentStore? contentStore = null,
        IOcrService? ocrService = null,
        IAiDiagnosticsCollector? aiDiagnosticsCollector = null,
        IDiagnosticsCollector? diagnosticsCollector = null,
        PluginsViewModel? plugins = null,
        PlatformDiagnosticsViewModel? platformDiagnostics = null,
        IMediaIntelligenceService? mediaIntelligenceService = null,
        IStateBackupService? stateBackupService = null,
        IOperationalHealthService? healthService = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _aiSuggestionService = aiSuggestionService;
        _aiRequestDiagnosticsStore = aiRequestDiagnosticsStore;
        _aiDiagnosticsCollector = aiDiagnosticsCollector;
        _diagnosticsCollector = diagnosticsCollector;
        _contentStore = contentStore;
        _ocrService = ocrService;
        _mediaIntelligenceService = mediaIntelligenceService;
        _stateBackupService = stateBackupService;
        _healthService = healthService;
        Plugins = plugins ?? new PluginsViewModel();
        PlatformDiagnostics = platformDiagnostics;
        ConfigureAdvancedDiagnostics(_configurationService.Current);
        _draft = SettingsDraft.FromSettings(_configurationService.Current);
        _draft.PropertyChanged += OnDraftPropertyChanged;
        _statusText = _configurationService.InitializationWarning ?? "Ready";
        AvailableAiModels = new ReadOnlyObservableCollection<string>(_availableAiModels);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        CancelCommand = new RelayCommand(DiscardChanges);
        TestAiConnectionCommand = new AsyncRelayCommand(TestAiConnectionAsync, CanStartAiOperation);
        DiscoverAiModelsCommand = new AsyncRelayCommand(DiscoverAiModelsAsync, CanStartAiOperation);
        RetryAiConnectionCommand = new AsyncRelayCommand(RetryAiConnectionAsync, CanStartAiOperation);
        CancelAiOperationCommand = new RelayCommand(CancelAiOperation, () => IsAiBusy);
        RequestPreferenceHistoryResetCommand = new RelayCommand(RequestPreferenceHistoryReset, CanRequestPreferenceHistoryReset);
        ConfirmPreferenceHistoryResetCommand = new AsyncRelayCommand(ConfirmPreferenceHistoryResetAsync, CanConfirmPreferenceHistoryReset);
        CancelPreferenceHistoryResetCommand = new RelayCommand(CancelPreferenceHistoryReset, () => IsPreferenceHistoryResetPending && !IsAiBusy);
        CheckOcrCapabilityCommand = new AsyncRelayCommand(CheckOcrCapabilityAsync, () => _ocrService is not null && !IsContentBusy);
        CheckMediaCapabilitiesCommand = new AsyncRelayCommand(CheckMediaCapabilitiesAsync, () => _mediaIntelligenceService is not null && !IsContentBusy);
        RequestContentCacheResetCommand = new RelayCommand(RequestContentCacheReset, () => _contentStore is not null && !IsContentBusy && !IsContentCacheResetPending);
        ConfirmContentCacheResetCommand = new AsyncRelayCommand(ConfirmContentCacheResetAsync, () => _contentStore is not null && !IsContentBusy && IsContentCacheResetPending);
        CancelContentCacheResetCommand = new RelayCommand(CancelContentCacheReset, () => !IsContentBusy && IsContentCacheResetPending);
        RefreshOperationalHealthCommand = new AsyncRelayCommand(RefreshOperationalHealthAsync, () => _healthService is not null && !IsStateTransferBusy);
        ConfirmStateRestoreCommand = new AsyncRelayCommand(ConfirmStateRestoreAsync, () => _pendingRestore is not null && !IsStateTransferBusy);
        CancelStateRestoreCommand = new RelayCommand(CancelStateRestore, () => _pendingRestore is not null && !IsStateTransferBusy);
    }

    /// <summary>
    /// Gets the supported editable configuration draft.
    /// </summary>
    public SettingsDraft Draft
    {
        get => _draft;
        private set
        {
            if (ReferenceEquals(_draft, value))
            {
                return;
            }

            _draft.PropertyChanged -= OnDraftPropertyChanged;
            if (SetProperty(ref _draft, value))
            {
                _draft.PropertyChanged += OnDraftPropertyChanged;
                OnPropertyChanged(nameof(SelectedInitialScanDepthOption));
                NotifyFeatureVisibilityChanged();
            }
        }
    }

    /// <summary>Occurs after validated settings have been persisted and made active.</summary>
    public event EventHandler<ApplicationSettings>? SettingsSaved;

    /// <summary>Gets local plugin discovery, review, lifecycle, and package-management state.</summary>
    public PluginsViewModel Plugins { get; }

    /// <summary>Gets current platform support, location, and limitation diagnostics.</summary>
    public PlatformDiagnosticsViewModel? PlatformDiagnostics { get; }

    /// <summary>Gets whether platform diagnostics were composed for this host.</summary>
    public bool HasPlatformDiagnostics => PlatformDiagnostics is not null;

    /// <summary>
    /// Synchronizes the two globally visible shell switches into the editable Settings draft.
    /// This does not test a provider, discover models, or send an AI request.
    /// </summary>
    public void SynchronizeShellFeatureSwitches(bool aiEnabled, bool showAdvancedFeatures)
    {
        Draft.AiEnabled = aiEnabled;
        Draft.ShowAdvancedFeatures = showAdvancedFeatures;
    }

    /// <summary>Gets whether AI capability switches are visible in the editable hierarchy.</summary>
    public bool IsAiCapabilitySettingsVisible => Draft.AiEnabled;

    /// <summary>Gets whether advanced non-AI settings are visible in the editable hierarchy.</summary>
    public bool IsAdvancedSettingsVisible => Draft.ShowAdvancedFeatures;

    /// <summary>Gets a plain-language capability snapshot without implying unavailable optional tools are failures.</summary>
    public string MediaCapabilityStatusText
    {
        get => _mediaCapabilityStatusText;
        private set => SetProperty(ref _mediaCapabilityStatusText, value);
    }

    /// <summary>Gets the latest bounded Data &amp; Index Health summary.</summary>
    public string OperationalHealthText
    {
        get => _operationalHealthText;
        private set => SetProperty(ref _operationalHealthText, value);
    }

    /// <summary>Gets the latest logical state export/restore status without exposing archive contents.</summary>
    public string StateTransferStatusText
    {
        get => _stateTransferStatusText;
        private set => SetProperty(ref _stateTransferStatusText, value);
    }

    /// <summary>Gets whether a validated restore awaits explicit confirmation.</summary>
    public bool IsStateRestorePending => _pendingRestore is not null;

    /// <summary>Gets whether logical state validation or application is active.</summary>
    public bool IsStateTransferBusy
    {
        get => _isStateTransferBusy;
        private set
        {
            if (SetProperty(ref _isStateTransferBusy, value))
            {
                RefreshOperationalHealthCommand.NotifyCanExecuteChanged();
                ConfirmStateRestoreCommand.NotifyCanExecuteChanged();
                CancelStateRestoreCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the supported durable indexing levels.</summary>
    public IReadOnlyList<IndexingLevel> AvailableIndexingLevels { get; } = Enum.GetValues<IndexingLevel>();

    /// <summary>Gets supported initial-scan scheduling policies.</summary>
    public IReadOnlyList<InitialScanDepthOption> AvailableInitialScanDepths { get; } = InitialScanDepthOptions.All;

    /// <summary>Gets or sets the plain-language initial indexing schedule.</summary>
    public InitialScanDepthOption SelectedInitialScanDepthOption
    {
        get => InitialScanDepthOptions.For(Draft.InitialScanDepth);
        set => Draft.InitialScanDepth = value.Value;
    }

    /// <summary>Gets the supported responsible-resource profiles.</summary>
    public IReadOnlyList<IndexingResourceMode> AvailableIndexingResourceModes { get; } =
        Enum.GetValues<IndexingResourceMode>();

    /// <summary>Gets whether essential provider setup is visible whenever AI is enabled.</summary>
    public bool IsAiProviderSettingsVisible => Draft.AiEnabled;

    /// <summary>Gets whether low-level AI diagnostics and history settings are visible.</summary>
    public bool IsAdvancedAiSettingsVisible => Draft.AiEnabled && Draft.ShowAdvancedFeatures;

    /// <summary>Gets whether model discovery has completed for the current endpoint.</summary>
    public bool HasCompletedModelDiscovery => _hasCompletedModelDiscovery;

    /// <summary>Gets whether discovery completed without finding an installed model.</summary>
    public bool HasNoDiscoveredModels => HasCompletedModelDiscovery && _installedAiModelIds.Count == 0;

    /// <summary>Gets whether the exact configured model appeared in the most recent discovery.</summary>
    public bool IsSelectedModelAvailable =>
        !string.IsNullOrWhiteSpace(Draft.SelectedAiModel) && _installedAiModelIds.Contains(Draft.SelectedAiModel);

    /// <summary>Gets a clear selected-model availability explanation.</summary>
    public string SelectedModelStatusText => string.IsNullOrWhiteSpace(Draft.SelectedAiModel)
        ? "No model is selected. Discover installed models, then choose one."
        : !HasCompletedModelDiscovery
            ? $"Configured model: {Draft.SelectedAiModel}. Refresh models to verify that it is installed."
            : IsSelectedModelAvailable
                ? _aiModelRuntimeStates.GetValueOrDefault(Draft.SelectedAiModel) == AiModelRuntimeState.Running
                    ? $"Selected model '{Draft.SelectedAiModel}' is installed and currently running."
                    : _isAiRuntimeStateAvailable
                        ? $"Selected model '{Draft.SelectedAiModel}' is installed and available; Ollama does not report it as currently running."
                        : $"Selected model '{Draft.SelectedAiModel}' is installed. Its current runtime state could not be checked."
                : $"Selected model '{Draft.SelectedAiModel}' is not in the discovered installed-model list. Select an installed model.";

    /// <summary>Gets whether normal AI setup is ready for at least one enabled capability.</summary>
    public bool IsAiSetupReady => Draft.AiEnabled && IsSelectedModelAvailable &&
        (Draft.FileRenameSuggestionsEnabled ||
            Draft.FolderStructureSuggestionsEnabled ||
            Draft.DocumentTextInterpretationEnabled ||
            Draft.SearchAssistanceEnabled);

    /// <summary>Gets concise setup readiness guidance.</summary>
    public string AiSetupReadinessText => IsAiSetupReady
        ? "AI setup is ready for the enabled suggestion capabilities."
        : "AI setup is incomplete. Check the connection, discover models, select an installed model, and enable a capability.";

    /// <summary>Gets timeout range guidance for predictable validation.</summary>
    public string AiRequestTimeoutValidation =>
        $"Enter a whole number from {AiSettings.MinimumRequestTimeoutSeconds} through {AiSettings.MaximumRequestTimeoutSeconds} seconds.";

    /// <summary>Gets a conservative privacy classification for the configured AI endpoint.</summary>
    public string AiEndpointPrivacyText
    {
        get
        {
            if (!Uri.TryCreate(Draft.AiEndpoint?.Trim(), UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme is not ("http" or "https") ||
                string.IsNullOrWhiteSpace(endpoint.Host) ||
                !string.IsNullOrEmpty(endpoint.UserInfo))
            {
                return "Enter a valid HTTP or HTTPS endpoint before using AI assistance.";
            }

            return endpoint.IsLoopback
                ? "Local endpoint — AI request information stays on this computer when Ollama is running locally."
                : "Remote endpoint — information supplied to AI requests may leave this computer.";
        }
    }

    /// <summary>
    /// Gets whether saved settings require application restart to reconfigure active services.
    /// </summary>
    public bool RestartRequired
    {
        get => _restartRequired;
        private set => SetProperty(ref _restartRequired, value);
    }

    /// <summary>
    /// Gets the current user-safe settings status.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets consistent settings-save status presentation.</summary>
    public StatusPresentation Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// Gets the documented log levels users may select.
    /// </summary>
    public IReadOnlyList<LogLevel> AvailableLogLevels { get; } = Enum.GetValues<LogLevel>();

    /// <summary>Gets models discovered from the current draft endpoint.</summary>
    public ReadOnlyObservableCollection<string> AvailableAiModels { get; }

    /// <summary>Gets the current optional AI availability state.</summary>
    public AiAvailabilityState AiAvailabilityState { get; private set; } = AiAvailabilityState.Disabled;

    /// <summary>Gets a concise explanation of the optional AI integration state.</summary>
    public string AiStatusText { get; private set; } = "AI assistance is disabled until enabled and configured.";

    /// <summary>Gets the plain-language local-AI readiness state.</summary>
    public AiReadinessState AiReadinessState
    {
        get => _aiReadinessState;
        private set
        {
            if (SetProperty(ref _aiReadinessState, value))
            {
                OnPropertyChanged(nameof(AiReadinessText));
            }
        }
    }

    /// <summary>Gets concise next-step guidance for the current local-AI state.</summary>
    public string AiReadinessText => AiReadinessState switch
    {
        AiReadinessState.NotConfigured => "Local AI is not configured yet.",
        AiReadinessState.NotChecked => "Settings changed. Retry the connection to check the selected model.",
        AiReadinessState.ServerUnavailable => "Your local AI is not running. Start Ollama, then retry.",
        AiReadinessState.ServerAvailable => "Ollama is available. Discover models and choose one.",
        AiReadinessState.ModelMissing => "The selected model is not installed. Choose an installed model.",
        AiReadinessState.Ready => $"Ready to use '{Draft.SelectedAiModel}'.",
        AiReadinessState.Running => "Checking your local AI...",
        AiReadinessState.Failed => "The last check failed safely. Retry when ready.",
        AiReadinessState.Cancelled => "The check was cancelled. Retry when ready.",
        _ => "Local AI status is unavailable.",
    };

    /// <summary>Gets consistent provider/setup status presentation.</summary>
    public StatusPresentation AiStatus
    {
        get => _aiStatus;
        private set => SetProperty(ref _aiStatus, value);
    }

    /// <summary>Gets whether an optional AI connection, discovery, or owned-history reset operation is active.</summary>
    public bool IsAiBusy
    {
        get => _isAiBusy;
        private set
        {
            if (SetProperty(ref _isAiBusy, value))
            {
                TestAiConnectionCommand.NotifyCanExecuteChanged();
                DiscoverAiModelsCommand.NotifyCanExecuteChanged();
                CancelAiOperationCommand.NotifyCanExecuteChanged();
                RequestPreferenceHistoryResetCommand.NotifyCanExecuteChanged();
                ConfirmPreferenceHistoryResetCommand.NotifyCanExecuteChanged();
                CancelPreferenceHistoryResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether the user has requested but not confirmed deletion of owned decision history.</summary>
    public bool IsPreferenceHistoryResetPending
    {
        get => _isPreferenceHistoryResetPending;
        private set
        {
            if (SetProperty(ref _isPreferenceHistoryResetPending, value))
            {
                RequestPreferenceHistoryResetCommand.NotifyCanExecuteChanged();
                ConfirmPreferenceHistoryResetCommand.NotifyCanExecuteChanged();
                CancelPreferenceHistoryResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether an explicit OCR capability or content-cache operation is running.</summary>
    public bool IsContentBusy
    {
        get => _isContentBusy;
        private set
        {
            if (SetProperty(ref _isContentBusy, value))
            {
                NotifyContentCommands();
            }
        }
    }

    /// <summary>Gets whether local content-cache deletion awaits confirmation.</summary>
    public bool IsContentCacheResetPending
    {
        get => _isContentCacheResetPending;
        private set
        {
            if (SetProperty(ref _isContentCacheResetPending, value))
            {
                NotifyContentCommands();
            }
        }
    }

    /// <summary>Gets user-safe OCR capability and local-cache status.</summary>
    public StatusPresentation ContentStatus
    {
        get => _contentStatus;
        private set => SetProperty(ref _contentStatus, value);
    }

    /// <summary>
    /// Gets the permanent user-facing label for daily diagnostic-log retention.
    /// </summary>
    public string DailyLogRetentionLabel => "Daily diagnostic log files to retain";

    /// <summary>
    /// Gets the user-facing explanation for the daily diagnostic-log retention setting.
    /// </summary>
    public string DailyLogRetentionDescription => "Controls how many OmniSorSe application diagnostic log files are kept. It does not affect scanned user files.";

    /// <summary>
    /// Gets the validation guidance for daily diagnostic-log retention.
    /// </summary>
    public string DailyLogRetentionValidation => "Enter a whole number of at least 1.";

    /// <summary>
    /// Gets the command that validates and persists the draft.
    /// </summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// Gets the command that restores an unsaved default draft.
    /// </summary>
    public IRelayCommand RestoreDefaultsCommand { get; }

    /// <summary>
    /// Gets the command that discards unsaved draft changes.
    /// </summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Gets the command that checks the current draft's Ollama endpoint.</summary>
    public IAsyncRelayCommand TestAiConnectionCommand { get; }

    /// <summary>Gets the command that retrieves installed models from the current draft endpoint.</summary>
    public IAsyncRelayCommand DiscoverAiModelsCommand { get; }

    /// <summary>Gets the command that retries connection, model discovery, and exact-model validation.</summary>
    public IAsyncRelayCommand RetryAiConnectionCommand { get; }

    /// <summary>Gets the command that explicitly cancels active optional AI work.</summary>
    public IRelayCommand CancelAiOperationCommand { get; }

    /// <summary>Gets the command that requests confirmation before clearing local preference decisions.</summary>
    public IRelayCommand RequestPreferenceHistoryResetCommand { get; }

    /// <summary>Gets the command that confirms clearing only local preference decisions.</summary>
    public IAsyncRelayCommand ConfirmPreferenceHistoryResetCommand { get; }

    /// <summary>Gets the command that cancels a pending preference-history reset.</summary>
    public IRelayCommand CancelPreferenceHistoryResetCommand { get; }

    /// <summary>Gets the explicit local OCR capability check command.</summary>
    public IAsyncRelayCommand CheckOcrCapabilityCommand { get; }

    /// <summary>Gets the explicit local media-capability check command.</summary>
    public IAsyncRelayCommand CheckMediaCapabilitiesCommand { get; }

    /// <summary>Gets the command that requests confirmation before clearing extracted content.</summary>
    public IRelayCommand RequestContentCacheResetCommand { get; }

    /// <summary>Gets the command that confirms clearing only the application-owned local content cache.</summary>
    public IAsyncRelayCommand ConfirmContentCacheResetCommand { get; }

    /// <summary>Gets the command that cancels a local content-cache reset.</summary>
    public IRelayCommand CancelContentCacheResetCommand { get; }

    /// <summary>Refreshes bounded non-destructive Data &amp; Index Health.</summary>
    public IAsyncRelayCommand RefreshOperationalHealthCommand { get; }

    /// <summary>Applies the unchanged validated logical-state archive.</summary>
    public IAsyncRelayCommand ConfirmStateRestoreCommand { get; }

    /// <summary>Discards the pending restore preview.</summary>
    public IRelayCommand CancelStateRestoreCommand { get; }

    /// <summary>Exports user-authored logical state to an explicitly selected archive.</summary>
    public async Task ExportStateAsync(string destinationPath)
    {
        if (_stateBackupService is null || IsStateTransferBusy)
        {
            return;
        }

        IsStateTransferBusy = true;
        try
        {
            await _stateBackupService.ExportAsync(destinationPath).ConfigureAwait(true);
            StateTransferStatusText = "State export completed. Treat the archive as sensitive because it contains settings, source definitions, Saved Views, recipes, User Tags, and decisions.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            StateTransferStatusText = $"State export failed safely ({exception.GetType().Name}). Existing state was not changed.";
        }
        finally
        {
            IsStateTransferBusy = false;
        }
    }

    /// <summary>Validates and previews an explicitly selected archive without applying it.</summary>
    public async Task PreviewStateRestoreAsync(string backupPath)
    {
        if (_stateBackupService is null || IsStateTransferBusy)
        {
            return;
        }

        IsStateTransferBusy = true;
        CancelStateRestore();
        try
        {
            _pendingRestore = await _stateBackupService.PreviewRestoreAsync(backupPath).ConfigureAwait(true);
            _pendingRestorePath = backupPath;
            OnPropertyChanged(nameof(IsStateRestorePending));
            ConfirmStateRestoreCommand.NotifyCanExecuteChanged();
            CancelStateRestoreCommand.NotifyCanExecuteChanged();
            StateTransferStatusText =
                $"Reviewed archive: {_pendingRestore.SourceCount} sources, {_pendingRestore.RecipeCount} recipes, {_pendingRestore.SavedViewCount} Saved Views, {_pendingRestore.SmartTagAuthorityCount} Smart Tag authority records, {_pendingRestore.RelationshipAuthorityCount} relationship authority records, and {_pendingRestore.SmartCollectionAuthorityCount} authored Smart Collection records. {_pendingRestore.Conflicts.Count} conflict summary item(s). Confirm Restore to replace reviewed libraries and merge source and exact file-authority records.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            StateTransferStatusText = $"Restore preview failed safely ({exception.GetType().Name}). The profile was not changed.";
        }
        finally
        {
            IsStateTransferBusy = false;
        }
    }

    /// <summary>
    /// Reloads the editable draft from the current persisted configuration.
    /// </summary>
    public void Load()
    {
        IsPreferenceHistoryResetPending = false;
        Draft = SettingsDraft.FromSettings(_configurationService.Current);
        RestartRequired = false;
        StatusText = "Settings loaded.";
        Status = StatusPresentation.Information(StatusText);
        SetAiStatus(_configurationService.Current.Ai.Enabled
            ? AiAvailabilityState.Connected
            : AiAvailabilityState.Disabled,
            _configurationService.Current.Ai.Enabled
                ? "AI settings loaded. Test the connection before requesting suggestions."
                : "AI assistance is disabled until enabled and configured.");
        AiReadinessState = !_configurationService.Current.Ai.Enabled ||
                           string.IsNullOrWhiteSpace(_configurationService.Current.Ai.SelectedModel)
            ? AiReadinessState.NotConfigured
            : AiReadinessState.NotChecked;
    }

    private async Task RefreshOperationalHealthAsync()
    {
        if (_healthService is null)
        {
            return;
        }

        IsStateTransferBusy = true;
        try
        {
            var health = await _healthService.CheckAsync().ConfigureAwait(true);
            OperationalHealthText = health.Issues.Count == 0
                ? "Healthy — schema, profile ownership, recovery state, stores, sources, and application-data storage passed bounded checks."
                : $"{health.State} — {health.Issues.Count} issue(s). {string.Join(" ", health.Issues.Take(3).Select(issue => issue.Summary))}";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            OperationalHealthText = $"Health check is unavailable ({exception.GetType().Name}). No repair was attempted.";
        }
        finally
        {
            IsStateTransferBusy = false;
        }
    }

    private async Task ConfirmStateRestoreAsync()
    {
        if (_stateBackupService is null || _pendingRestore is null || _pendingRestorePath is null)
        {
            return;
        }

        IsStateTransferBusy = true;
        try
        {
            var result = await _stateBackupService.RestoreAsync(
                _pendingRestorePath,
                _pendingRestore.Fingerprint,
                StateRestoreMode.Replace,
                new StateRestoreSelection()).ConfigureAwait(true);
            StateTransferStatusText = result.Message + " Restart OmniSorSe before relying on restored runtime configuration.";
            _pendingRestore = null;
            _pendingRestorePath = null;
            OnPropertyChanged(nameof(IsStateRestorePending));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            StateTransferStatusText = $"Restore failed safely ({exception.GetType().Name}). A pre-restore recovery archive was retained when creation completed.";
        }
        finally
        {
            IsStateTransferBusy = false;
            ConfirmStateRestoreCommand.NotifyCanExecuteChanged();
            CancelStateRestoreCommand.NotifyCanExecuteChanged();
        }
    }

    private void CancelStateRestore()
    {
        _pendingRestore = null;
        _pendingRestorePath = null;
        OnPropertyChanged(nameof(IsStateRestorePending));
        StateTransferStatusText = "Restore preview cancelled. No profile state was changed.";
        ConfirmStateRestoreCommand.NotifyCanExecuteChanged();
        CancelStateRestoreCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveAsync()
    {
        try
        {
            var previous = _configurationService.Current;
            var settings = Draft.ToSettings();
            settings.Validate();
            await _configurationService.SaveAsync(settings, CancellationToken.None);
            ConfigureAdvancedDiagnostics(settings);
            RestartRequired =
                LoggingChanged(previous.Logging, settings.Logging) ||
                ActiveIndexingWorkersChanged(previous.DeepIndexing, settings.DeepIndexing);
            StatusText = RestartRequired
                ? "Settings saved and feature visibility updated. Restart OmniSorSe to apply active logging or background-worker changes."
                : "Settings saved and feature visibility updated.";
            Status = StatusPresentation.Success(StatusText);
            SettingsSaved?.Invoke(this, settings);
        }
        catch (ConfigurationValidationException exception)
        {
            StatusText = exception.Message;
            Status = StatusPresentation.Error(StatusText);
        }
        catch (IOException)
        {
            StatusText = "Settings could not be saved.";
            Status = StatusPresentation.Error(StatusText);
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "Settings could not be saved.";
            Status = StatusPresentation.Error(StatusText);
        }
    }

    private void ConfigureAdvancedDiagnostics(ApplicationSettings settings)
    {
        if (_diagnosticsCollector is not null)
        {
            _aiRequestDiagnosticsStore?.SetEnabled(false);
            _diagnosticsCollector.Configure(settings.Diagnostics);
            return;
        }

        var legacyAiEnabled =
            settings.Ai.Enabled &&
            settings.Features.ShowAdvancedFeatures &&
            settings.Diagnostics.IsCategoryEnabled(DiagnosticCategory.Ai);
        _aiRequestDiagnosticsStore?.SetEnabled(
            legacyAiEnabled,
            settings.Diagnostics.ShowUnredactedDiagnosticContent);
        _aiDiagnosticsCollector?.Configure(
            legacyAiEnabled,
            settings.Diagnostics.ShowUnredactedDiagnosticContent);
    }

    private void RestoreDefaults()
    {
        IsPreferenceHistoryResetPending = false;
        Draft = SettingsDraft.FromSettings(new ApplicationSettings());
        RestartRequired = false;
        StatusText = "Default settings restored. Save to persist them.";
        Status = StatusPresentation.Information(StatusText);
        SetAiStatus(AiAvailabilityState.Disabled, "AI assistance is disabled until enabled and configured.");
        AiReadinessState = AiReadinessState.NotConfigured;
    }

    private void DiscardChanges()
    {
        IsPreferenceHistoryResetPending = false;
        Draft = SettingsDraft.FromSettings(_configurationService.Current);
        RestartRequired = false;
        StatusText = "Unsaved changes discarded.";
        Status = StatusPresentation.Information(StatusText);
    }

    private async Task CheckOcrCapabilityAsync()
    {
        if (_ocrService is null)
        {
            return;
        }

        IsContentBusy = true;
        try
        {
            var capability = await _ocrService.RefreshCapabilityAsync(CancellationToken.None);
            var details = capability.IsAvailable
                ? $"{capability.Message} Engine: {capability.EngineVersion ?? "version unavailable"}. " +
                  $"Languages: {string.Join(", ", capability.AvailableLanguages)}. " +
                  $"PDF renderer: {(capability.SupportsPdf ? $"{capability.RasterizerIdentifier} {capability.RasterizerVersion}".Trim() : "unavailable")}."
                : capability.Message;
            ContentStatus = capability.IsAvailable
                ? StatusPresentation.Success(details)
                : StatusPresentation.Warning(capability.Message);
        }
        catch (Exception)
        {
            ContentStatus = StatusPresentation.Warning("Local OCR capability could not be checked.");
        }
        finally
        {
            IsContentBusy = false;
        }
    }

    private async Task CheckMediaCapabilitiesAsync()
    {
        if (_mediaIntelligenceService is null)
        {
            return;
        }

        IsContentBusy = true;
        try
        {
            var capabilities = await _mediaIntelligenceService.GetCapabilitiesAsync(CancellationToken.None);
            MediaCapabilityStatusText = string.Join(
                Environment.NewLine,
                capabilities.Select(capability =>
                    $"{CapabilityLabel(capability.Kind)} — {CapabilityStateLabel(capability.State)}: {capability.Message}"));
        }
        catch (Exception)
        {
            MediaCapabilityStatusText = "Media capabilities could not be checked. Ordinary filename and document Search remains available.";
        }
        finally
        {
            IsContentBusy = false;
        }
    }

    private static string CapabilityLabel(MediaCapabilityKind kind) => kind switch
    {
        MediaCapabilityKind.ImageMetadata => "Image metadata",
        MediaCapabilityKind.ImageOcr => "Image OCR",
        MediaCapabilityKind.AudioMetadata => "Audio metadata",
        MediaCapabilityKind.Transcription => "Transcription",
        MediaCapabilityKind.VideoMetadata => "Video metadata",
        MediaCapabilityKind.VideoFrameSampling => "Video frame analysis",
        MediaCapabilityKind.VisualDescription => "Visual descriptions",
        MediaCapabilityKind.Thumbnail => "Thumbnails",
        _ => kind.ToString(),
    };

    private static string CapabilityStateLabel(MediaCapabilityState state) => state switch
    {
        MediaCapabilityState.NotConfigured => "Not configured",
        MediaCapabilityState.InvalidConfiguration => "Invalid configuration",
        MediaCapabilityState.Unavailable => "Unavailable",
        MediaCapabilityState.Available => "Available",
        MediaCapabilityState.Processing => "Processing",
        MediaCapabilityState.Error => "Error",
        _ => "Disabled",
    };

    private void RequestContentCacheReset()
    {
        IsContentCacheResetPending = true;
        ContentStatus = StatusPresentation.Warning("Confirm clearing the local OCR and extracted-content cache. Source files will not be changed.");
    }

    private async Task ConfirmContentCacheResetAsync()
    {
        if (_contentStore is null)
        {
            return;
        }

        IsContentBusy = true;
        try
        {
            await _contentStore.ClearAsync(CancellationToken.None);
            IsContentCacheResetPending = false;
            ContentStatus = StatusPresentation.Success("Local extracted content cleared. The next eligible scan will reprocess files.");
        }
        catch (Exception)
        {
            ContentStatus = StatusPresentation.Error("The local extracted-content cache could not be cleared.");
        }
        finally
        {
            IsContentBusy = false;
        }
    }

    private void CancelContentCacheReset()
    {
        IsContentCacheResetPending = false;
        ContentStatus = StatusPresentation.Information("Local content-cache reset cancelled.");
    }

    private void NotifyContentCommands()
    {
        CheckOcrCapabilityCommand.NotifyCanExecuteChanged();
        CheckMediaCapabilitiesCommand.NotifyCanExecuteChanged();
        RequestContentCacheResetCommand.NotifyCanExecuteChanged();
        ConfirmContentCacheResetCommand.NotifyCanExecuteChanged();
        CancelContentCacheResetCommand.NotifyCanExecuteChanged();
    }

    private bool CanStartAiOperation() =>
        _aiSuggestionService is not null && !IsAiBusy && Draft.AiEnabled;

    private bool CanRequestPreferenceHistoryReset() =>
        _aiSuggestionService is not null && !IsAiBusy && Draft.AiEnabled && Draft.ShowAdvancedFeatures && !IsPreferenceHistoryResetPending;

    private bool CanConfirmPreferenceHistoryReset() =>
        _aiSuggestionService is not null && !IsAiBusy && Draft.AiEnabled && Draft.ShowAdvancedFeatures && IsPreferenceHistoryResetPending;

    private async Task TestAiConnectionAsync()
    {
        if (_aiSuggestionService is null)
        {
            return;
        }

        var (cancellation, version) = BeginAiOperation();
        try
        {
            SetAiStatus(AiAvailabilityState.Connecting, "Testing the configured Ollama endpoint…");
            var result = await _aiSuggestionService.TestConnectionAsync(Draft.ToSettings(), cancellation.Token);
            if (IsCurrentAiOperation(cancellation, version))
            {
                PublishAiConnection(result, publishModels: false);
            }
        }
        catch (ConfigurationValidationException)
        {
            SetAiStatus(AiAvailabilityState.Unavailable, "AI settings are invalid. Correct the endpoint or timeout and try again.");
        }
        catch (OperationCanceledException)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                SetAiStatus(AiAvailabilityState.RequestCancelled, "The AI connection test was cancelled.");
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                SetAiStatus(AiAvailabilityState.Unavailable, "The AI connection test failed safely. Check the configured endpoint and try again.");
            }
        }
        finally
        {
            EndAiOperation(cancellation, version);
        }
    }

    private async Task DiscoverAiModelsAsync()
    {
        if (_aiSuggestionService is null)
        {
            return;
        }

        var (cancellation, version) = BeginAiOperation();
        try
        {
            SetAiStatus(AiAvailabilityState.Connecting, "Discovering installed Ollama models…");
            var result = await _aiSuggestionService.DiscoverModelsAsync(Draft.ToSettings(), cancellation.Token);
            if (IsCurrentAiOperation(cancellation, version))
            {
                PublishAiConnection(result, publishModels: true);
            }
        }
        catch (ConfigurationValidationException)
        {
            SetAiStatus(AiAvailabilityState.Unavailable, "AI settings are invalid. Correct the endpoint or timeout and try again.");
        }
        catch (OperationCanceledException)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                SetAiStatus(AiAvailabilityState.RequestCancelled, "The model discovery request was cancelled.");
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                SetAiStatus(AiAvailabilityState.Unavailable, "Model discovery failed safely. Check the configured endpoint and try again.");
            }
        }
        finally
        {
            EndAiOperation(cancellation, version);
        }
    }

    private async Task RetryAiConnectionAsync()
    {
        if (_aiSuggestionService is null)
        {
            return;
        }

        var (cancellation, version) = BeginAiOperation();
        try
        {
            SetAiStatus(AiAvailabilityState.Connecting, "Checking your local AI...");
            var settings = Draft.ToSettings();
            var connection = await _aiSuggestionService.TestConnectionAsync(settings, cancellation.Token);
            if (!IsCurrentAiOperation(cancellation, version))
            {
                return;
            }

            if (connection.State != AiAvailabilityState.Connected)
            {
                PublishAiConnection(connection, publishModels: false);
                return;
            }

            AiReadinessState = AiReadinessState.ServerAvailable;
            var models = await _aiSuggestionService.DiscoverModelsAsync(settings, cancellation.Token);
            if (IsCurrentAiOperation(cancellation, version))
            {
                PublishAiConnection(models, publishModels: true);
            }
        }
        catch (ConfigurationValidationException)
        {
            SetAiStatus(AiAvailabilityState.Unavailable, "AI settings are invalid. Correct the endpoint or timeout and retry.");
            AiReadinessState = AiReadinessState.NotConfigured;
        }
        catch (OperationCanceledException)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                SetAiStatus(AiAvailabilityState.RequestCancelled, "Connection check cancelled. You can retry.");
                AiReadinessState = AiReadinessState.Cancelled;
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                SetAiStatus(AiAvailabilityState.Unavailable, "Your local AI could not be checked. Start Ollama and retry.");
                AiReadinessState = AiReadinessState.Failed;
            }
        }
        finally
        {
            EndAiOperation(cancellation, version);
        }
    }

    private void RequestPreferenceHistoryReset()
    {
        IsPreferenceHistoryResetPending = true;
        StatusText = "Confirm reset to delete only OmniSorSe local AI decision history. Scanned files and other application data will not change.";
    }

    private void CancelPreferenceHistoryReset()
    {
        IsPreferenceHistoryResetPending = false;
        StatusText = "Local AI decision-history reset was cancelled.";
    }

    private async Task ConfirmPreferenceHistoryResetAsync()
    {
        if (_aiSuggestionService is null || !IsPreferenceHistoryResetPending)
        {
            return;
        }

        var (cancellation, version) = BeginAiOperation();
        try
        {
            var result = await _aiSuggestionService.ResetDecisionHistoryAsync(Draft.ToSettings(), cancellation.Token);
            if (IsCurrentAiOperation(cancellation, version))
            {
                IsPreferenceHistoryResetPending = false;
                StatusText = result.Message;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                StatusText = "Local AI decision-history reset was cancelled.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                StatusText = "Local AI decision history could not be reset. Existing application data was preserved.";
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _aiOperationVersion))
            {
                StatusText = "Local AI decision history could not be reset. Existing application data was preserved.";
            }
        }
        finally
        {
            EndAiOperation(cancellation, version);
        }
    }

    private void CancelAiOperation()
    {
        var cancellation = Interlocked.Exchange(ref _aiOperationCancellation, null);
        if (cancellation is null)
        {
            return;
        }

        Interlocked.Increment(ref _aiOperationVersion);
        cancellation.Cancel();
        IsAiBusy = false;
        SetAiStatus(AiAvailabilityState.RequestCancelled, "Connection check cancelled. You can retry.");
        AiReadinessState = AiReadinessState.Cancelled;
    }

    private (CancellationTokenSource Cancellation, long Version) BeginAiOperation()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _aiOperationCancellation, cancellation);
        previous?.Cancel();
        var version = Interlocked.Increment(ref _aiOperationVersion);
        IsAiBusy = true;
        AiReadinessState = AiReadinessState.Running;
        return (cancellation, version);
    }

    private bool IsCurrentAiOperation(CancellationTokenSource cancellation, long version) =>
        !cancellation.IsCancellationRequested &&
        ReferenceEquals(_aiOperationCancellation, cancellation) &&
        version == Volatile.Read(ref _aiOperationVersion);

    private void EndAiOperation(CancellationTokenSource cancellation, long version)
    {
        if (ReferenceEquals(_aiOperationCancellation, cancellation))
        {
            _aiOperationCancellation = null;
        }

        if (version == Volatile.Read(ref _aiOperationVersion))
        {
            IsAiBusy = false;
        }

        cancellation.Dispose();
    }

    /// <summary>Releases optional AI cancellation resources.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        var cancellation = Interlocked.Exchange(ref _aiOperationCancellation, null);
        Interlocked.Increment(ref _aiOperationVersion);
        cancellation?.Cancel();
        cancellation?.Dispose();
        _draft.PropertyChanged -= OnDraftPropertyChanged;
        _isDisposed = true;
    }

    private void PublishAiConnection(AiConnectionResult result, bool publishModels)
    {
        string? replacedModel = null;
        if (publishModels)
        {
            var previouslySelectedModel = Draft.SelectedAiModel;
            _installedAiModelIds.Clear();
            _aiModelRuntimeStates.Clear();
            _availableAiModels.Clear();
            foreach (var model in result.Models)
            {
                _installedAiModelIds.Add(model.Id);
                _aiModelRuntimeStates[model.Id] = model.RuntimeState;
                _availableAiModels.Add(model.Id);
            }

            _isAiRuntimeStateAvailable = result.RuntimeStateAvailable;
            _hasCompletedModelDiscovery = true;
            if (!string.IsNullOrWhiteSpace(previouslySelectedModel) &&
                !_installedAiModelIds.Contains(previouslySelectedModel) &&
                _availableAiModels.Count > 0)
            {
                replacedModel = previouslySelectedModel;
                Draft.SelectedAiModel = _availableAiModels[0];
            }
        }

        if (publishModels && result.State is AiAvailabilityState.Connected or AiAvailabilityState.NoModelsAvailable)
        {
            if (string.IsNullOrWhiteSpace(Draft.SelectedAiModel))
            {
                AiReadinessState = AiReadinessState.NotConfigured;
                SetAiStatus(AiAvailabilityState.Connected, "Ollama is available. Choose an installed model.");
            }
            else if (_installedAiModelIds.Contains(Draft.SelectedAiModel))
            {
                AiReadinessState = AiReadinessState.Ready;
                SetAiStatus(
                    AiAvailabilityState.ModelSelected,
                    replacedModel is null
                        ? $"Local AI is ready with '{Draft.SelectedAiModel}'."
                        : $"The previously selected model '{replacedModel}' is no longer installed. " +
                          $"Using '{Draft.SelectedAiModel}' for this draft; save Settings to keep the fallback.");
            }
            else
            {
                AiReadinessState = AiReadinessState.ModelMissing;
                SetAiStatus(AiAvailabilityState.ModelUnavailable, $"The selected model '{Draft.SelectedAiModel}' is not installed.");
            }
        }
        else
        {
            AiReadinessState = result.State switch
            {
                AiAvailabilityState.Disabled => AiReadinessState.NotConfigured,
                AiAvailabilityState.Connected => AiReadinessState.ServerAvailable,
                AiAvailabilityState.ModelSelected => AiReadinessState.Ready,
                AiAvailabilityState.NoModelsAvailable or AiAvailabilityState.ModelUnavailable => AiReadinessState.ModelMissing,
                AiAvailabilityState.RequestCancelled => AiReadinessState.Cancelled,
                AiAvailabilityState.Unavailable => AiReadinessState.ServerUnavailable,
                AiAvailabilityState.Connecting or AiAvailabilityState.RequestRunning => AiReadinessState.Running,
                _ => AiReadinessState.Failed,
            };
            SetAiStatus(result.State, FriendlyAiMessage(result.State, result.Message));
        }
        NotifyAiReadinessChanged();
    }

    private void SetAiStatus(AiAvailabilityState state, string message)
    {
        AiAvailabilityState = state;
        AiStatusText = message;
        AiStatus = state switch
        {
            AiAvailabilityState.Connecting or AiAvailabilityState.RequestRunning => StatusPresentation.Progress(message),
            AiAvailabilityState.Connected or AiAvailabilityState.ModelSelected => StatusPresentation.Success(message),
            AiAvailabilityState.NoModelsAvailable or AiAvailabilityState.ModelUnavailable or AiAvailabilityState.ResponseInvalid => StatusPresentation.Warning(message),
            AiAvailabilityState.Unavailable => StatusPresentation.Error(message),
            _ => StatusPresentation.Information(message),
        };
        OnPropertyChanged(nameof(AiAvailabilityState));
        OnPropertyChanged(nameof(AiStatusText));
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SettingsDraft.InitialScanDepth))
        {
            OnPropertyChanged(nameof(SelectedInitialScanDepthOption));
        }

        if (eventArgs.PropertyName is nameof(SettingsDraft.AiEnabled) or nameof(SettingsDraft.ShowAdvancedFeatures))
        {
            if (!Draft.AiEnabled && IsAiBusy)
            {
                CancelAiOperation();
            }

            NotifyFeatureVisibilityChanged();
            if (!Draft.AiEnabled)
            {
                AiReadinessState = AiReadinessState.NotConfigured;
            }
        }

        if (eventArgs.PropertyName is nameof(SettingsDraft.SelectedAiModel)
            or nameof(SettingsDraft.FileRenameSuggestionsEnabled)
            or nameof(SettingsDraft.FolderStructureSuggestionsEnabled)
            or nameof(SettingsDraft.DocumentTextInterpretationEnabled)
            or nameof(SettingsDraft.SearchAssistanceEnabled)
            or nameof(SettingsDraft.AiRequestTimeoutText))
        {
            if (eventArgs.PropertyName is nameof(SettingsDraft.SelectedAiModel))
            {
                AiReadinessState = string.IsNullOrWhiteSpace(Draft.SelectedAiModel)
                    ? AiReadinessState.NotConfigured
                    : AiReadinessState.NotChecked;
                SetAiStatus(
                    string.IsNullOrWhiteSpace(Draft.SelectedAiModel) ? AiAvailabilityState.Connected : AiAvailabilityState.Connected,
                    string.IsNullOrWhiteSpace(Draft.SelectedAiModel)
                        ? "Choose an installed model."
                        : $"Model changed to '{Draft.SelectedAiModel}'. Save settings, then retry the connection.");
            }

            NotifyAiReadinessChanged();
        }

        if (eventArgs.PropertyName == nameof(SettingsDraft.AiEndpoint))
        {
            _hasCompletedModelDiscovery = false;
            _isAiRuntimeStateAvailable = false;
            _installedAiModelIds.Clear();
            _aiModelRuntimeStates.Clear();
            _availableAiModels.Clear();
            AiReadinessState = Draft.AiEnabled ? AiReadinessState.NotChecked : AiReadinessState.NotConfigured;
            SetAiStatus(
                Draft.AiEnabled ? AiAvailabilityState.Unavailable : AiAvailabilityState.Disabled,
                Draft.AiEnabled
                    ? "AI endpoint changed. Retry the connection and refresh installed models."
                    : "AI assistance is disabled until enabled and configured.");
            OnPropertyChanged(nameof(AiEndpointPrivacyText));
            NotifyAiReadinessChanged();
        }
    }

    private void NotifyFeatureVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsAiCapabilitySettingsVisible));
        OnPropertyChanged(nameof(IsAdvancedSettingsVisible));
        OnPropertyChanged(nameof(IsAiProviderSettingsVisible));
        OnPropertyChanged(nameof(IsAdvancedAiSettingsVisible));
        TestAiConnectionCommand.NotifyCanExecuteChanged();
        DiscoverAiModelsCommand.NotifyCanExecuteChanged();
        RetryAiConnectionCommand.NotifyCanExecuteChanged();
        RequestPreferenceHistoryResetCommand.NotifyCanExecuteChanged();
        ConfirmPreferenceHistoryResetCommand.NotifyCanExecuteChanged();
        NotifyAiReadinessChanged();
    }

    private void NotifyAiReadinessChanged()
    {
        OnPropertyChanged(nameof(HasCompletedModelDiscovery));
        OnPropertyChanged(nameof(HasNoDiscoveredModels));
        OnPropertyChanged(nameof(IsSelectedModelAvailable));
        OnPropertyChanged(nameof(SelectedModelStatusText));
        OnPropertyChanged(nameof(IsAiSetupReady));
        OnPropertyChanged(nameof(AiSetupReadinessText));
        OnPropertyChanged(nameof(AiRequestTimeoutValidation));
        OnPropertyChanged(nameof(AiEndpointPrivacyText));
        OnPropertyChanged(nameof(AiReadinessText));
    }

    private static string FriendlyAiMessage(AiAvailabilityState state, string fallback) => state switch
    {
        AiAvailabilityState.Unavailable => string.IsNullOrWhiteSpace(fallback)
            ? "Ollama could not be reached at the configured endpoint. Search remains available without AI assistance."
            : fallback,
        AiAvailabilityState.RequestCancelled => "Connection check cancelled. You can retry.",
        AiAvailabilityState.NoModelsAvailable => "Ollama is running, but no installed models were found.",
        AiAvailabilityState.ModelUnavailable => "The selected model is not installed.",
        _ => fallback,
    };

    private static bool LoggingChanged(LoggingSettings previous, LoggingSettings current) =>
        previous.FileLoggingEnabled != current.FileLoggingEnabled ||
        previous.MinimumLevel != current.MinimumLevel ||
        previous.RetainedFileCount != current.RetainedFileCount ||
        !string.Equals(previous.LogDirectoryPath, current.LogDirectoryPath, StringComparison.Ordinal);

    private static bool ActiveIndexingWorkersChanged(DeepIndexingSettings previous, DeepIndexingSettings current) =>
        previous.Enabled != current.Enabled ||
        previous.ResourceMode != current.ResourceMode ||
        previous.MaximumConcurrency != current.MaximumConcurrency ||
        previous.OcrProcessingEnabled != current.OcrProcessingEnabled ||
        previous.AiProcessingEnabled != current.AiProcessingEnabled ||
        previous.SummaryProcessingEnabled != current.SummaryProcessingEnabled ||
        previous.SemanticProcessingEnabled != current.SemanticProcessingEnabled;
}
