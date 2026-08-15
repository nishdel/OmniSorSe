using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.Services;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents bounded local Search, persistent indexing progress, and explained results.</summary>
public sealed class SemanticSearchViewModel : ViewModelBase, IDisposable
{
    private readonly IBackgroundIndexingService? _backgroundIndexingService;
    private readonly IConfigurationService _configurationService;
    private readonly ISemanticIndexer? _indexer;
    private readonly ISemanticSearchService? _searchService;
    private readonly ISemanticIndexStore? _indexStore;
    private readonly IExternalFileLauncher? _launcher;
    private readonly IClipboardService? _clipboard;
    private readonly IIndexPrivacyService? _privacyService;
    private readonly IAdvancedDiagnosticsWindowService? _advancedDiagnosticsWindowService;
    private readonly IMediaThumbnailProvider? _mediaThumbnailProvider;
    private readonly ISavedDiscoveryViewStore? _savedViewStore;
    private readonly ObservableCollection<SemanticSearchHit> _hits = [];
    private readonly ObservableCollection<SearchFilter> _activeFilters = [];
    private readonly ObservableCollection<IndexingSource> _sources = [];
    private readonly ObservableCollection<IndexingFailure> _indexingFailures = [];
    private readonly ObservableCollection<DiscoveryFacetGroupRow> _facetGroups = [];
    private readonly ObservableCollection<SavedDiscoveryViewRow> _savedViews = [];
    private CancellationTokenSource? _operationCancellation;
    private string? _queryText;
    private bool _isBusy;
    private bool _isClearPending;
    private double _progressValue;
    private string _progressText = "Index not inspected.";
    private StatusPresentation _status = StatusPresentation.Information("Build or refresh the local index, then enter a search phrase.");
    private IndexingProgressSnapshot _backgroundProgress = new();
    private IndexingSource? _selectedSource;
    private string _coverageText = "Search coverage has not been inspected.";
    private string _storageText = "Index storage has not been inspected.";
    private string _storageBreakdownText = string.Empty;
    private string _topicText = string.Empty;
    private bool _areFiltersVisible;
    private SemanticSearchHit? _selectedHit;
    private string? _selectedMediaThumbnailPath;
    private IndexPrivacyItem? _privacyItem;
    private string _privacyText = "Select Inspect indexed data on a Search result to review retained categories.";
    private bool _isForgetFilePending;
    private bool _isForgetSourcePending;
    private bool _filtersWereEdited;
    private bool _includeRelationshipContext = true;
    private bool _includeGraphContext = true;
    private bool _useAiAssistance;
    private string _aiAssistanceText = "Deterministic local Search is active.";
    private string _graphCoverageText = "Knowledge Graph coverage has not been inspected for this query.";
    private long _queryVersion;
    private string _candidateCoverageText = "Query candidate coverage has not been inspected.";
    private string? _savedViewName;
    private SavedDiscoveryViewRow? _selectedSavedView;

    /// <summary>Initializes a preview instance with Search unavailable.</summary>
    public SemanticSearchViewModel()
        : this(new PreviewConfiguration(), null, null, null, null, null, null)
    {
    }

    /// <summary>Initializes the local semantic-search presentation model.</summary>
    public SemanticSearchViewModel(
        IConfigurationService configurationService,
        ISemanticIndexer? indexer,
        ISemanticSearchService? searchService,
        ISemanticIndexStore? indexStore,
        IExternalFileLauncher? launcher,
        IBackgroundIndexingService? backgroundIndexingService = null,
        IAdvancedDiagnosticsWindowService? advancedDiagnosticsWindowService = null,
        IIndexPrivacyService? privacyService = null,
        IClipboardService? clipboard = null,
        IMediaThumbnailProvider? mediaThumbnailProvider = null,
        ISmartTagService? smartTagService = null,
        ISavedDiscoveryViewStore? savedViewStore = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _indexer = indexer;
        _searchService = searchService;
        _indexStore = indexStore;
        _launcher = launcher;
        _clipboard = clipboard;
        _backgroundIndexingService = backgroundIndexingService;
        _privacyService = privacyService ?? backgroundIndexingService as IIndexPrivacyService;
        _advancedDiagnosticsWindowService = advancedDiagnosticsWindowService;
        _mediaThumbnailProvider = mediaThumbnailProvider;
        _ = smartTagService; // Retained for binary/source-compatible composition while v2.8 removes duplicate selector state.
        _savedViewStore = savedViewStore;
        Hits = new ReadOnlyObservableCollection<SemanticSearchHit>(_hits);
        ActiveFilters = new ReadOnlyObservableCollection<SearchFilter>(_activeFilters);
        Sources = new ReadOnlyObservableCollection<IndexingSource>(_sources);
        IndexingFailures = new ReadOnlyObservableCollection<IndexingFailure>(_indexingFailures);
        FacetGroups = new ReadOnlyObservableCollection<DiscoveryFacetGroupRow>(_facetGroups);
        SavedViews = new ReadOnlyObservableCollection<SavedDiscoveryViewRow>(_savedViews);
        SearchCommand = new AsyncRelayCommand(SearchAsync, CanSearch);
        BuildIndexCommand = new AsyncRelayCommand(() => BuildIndexAsync(false), CanIndex);
        RebuildIndexCommand = new AsyncRelayCommand(() => BuildIndexAsync(true), CanIndex);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ClearQueryCommand = new RelayCommand(ClearQuery, () => !string.IsNullOrWhiteSpace(QueryText));
        RequestClearIndexCommand = new RelayCommand(RequestClearIndex, () => _indexStore is not null && !IsBusy && !IsClearPending);
        ConfirmClearIndexCommand = new AsyncRelayCommand(ConfirmClearIndexAsync, () => _indexStore is not null && !IsBusy && IsClearPending);
        CancelClearIndexCommand = new RelayCommand(CancelClearIndex, () => !IsBusy && IsClearPending);
        OpenFileCommand = new AsyncRelayCommand<SemanticSearchHit>(OpenFileAsync, CanOpenHit);
        OpenContainingFolderCommand = new AsyncRelayCommand<SemanticSearchHit>(OpenFolderAsync, CanOpenHit);
        CopyFullPathCommand = new AsyncRelayCommand<SemanticSearchHit>(CopyFullPathAsync, CanCopyHit);
        OpenInFilesCommand = new RelayCommand<SemanticSearchHit>(OpenInFiles, CanOpenInFiles);
        ToggleFiltersCommand = new RelayCommand(() => AreFiltersVisible = !AreFiltersVisible);
        RemoveFilterCommand = new AsyncRelayCommand<SearchFilter>(RemoveFilterAsync, filter => filter is not null && !IsBusy);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync, () => _activeFilters.Count > 0 && !IsBusy);
        ToggleFacetCommand = new AsyncRelayCommand<DiscoveryFacetValueRow>(ToggleFacetAsync, value => value is not null && !IsBusy);
        ShowModerateSuggestionsCommand = new AsyncRelayCommand(ShowModerateSuggestionsAsync, () => !IsBusy);
        SaveViewCommand = new AsyncRelayCommand(SaveViewAsync, CanSaveView);
        OpenSavedViewCommand = new AsyncRelayCommand(OpenSavedViewAsync, () => SelectedSavedView is not null && !IsBusy);
        UpdateSavedViewCommand = new AsyncRelayCommand(UpdateSavedViewAsync, () => SelectedSavedView is not null && CanSaveView());
        DeleteSavedViewCommand = new AsyncRelayCommand(DeleteSavedViewAsync, () => SelectedSavedView is not null && !IsBusy);
        InspectIndexedDataCommand = new AsyncRelayCommand<SemanticSearchHit>(InspectIndexedDataAsync, CanInspectHit);
        RequestForgetFileCommand = new RelayCommand(
            () => IsForgetFilePending = true,
            () => PrivacyItem is not null && !IsBusy && !IsForgetFilePending);
        ConfirmForgetFileCommand = new AsyncRelayCommand(ForgetFileAsync, () => PrivacyItem is not null && !IsBusy && IsForgetFilePending);
        CancelForgetFileCommand = new RelayCommand(() => IsForgetFilePending = false, () => IsForgetFilePending && !IsBusy);
        RequestForgetSourceCommand = new RelayCommand(
            () => IsForgetSourcePending = true,
            () => SelectedSource is not null && !IsBusy && !IsForgetSourcePending);
        ConfirmForgetSourceCommand = new AsyncRelayCommand(ForgetSourceAsync, () => SelectedSource is not null && !IsBusy && IsForgetSourcePending);
        CancelForgetSourceCommand = new RelayCommand(() => IsForgetSourcePending = false, () => IsForgetSourcePending && !IsBusy);
        ReindexFileCommand = new AsyncRelayCommand(() => RepairFileAsync(IndexRepairKind.Rebuild), CanRepairFile);
        VerifyFileCommand = new AsyncRelayCommand(() => RepairFileAsync(IndexRepairKind.Verify), CanRepairFile);
        RetryFileCommand = new AsyncRelayCommand(() => RepairFileAsync(IndexRepairKind.RetryFailedStage), CanRepairFile);
        RefreshMetadataCommand = new AsyncRelayCommand(() => RepairFileAsync(IndexRepairKind.RefreshMetadata), CanRepairFile);
        RefreshTextCommand = new AsyncRelayCommand(() => RepairFileAsync(IndexRepairKind.RefreshText), CanRepairFile);
        RefreshOcrCommand = new AsyncRelayCommand(() => RepairFileAsync(IndexRepairKind.RefreshOcr), CanRepairFile);
        RegenerateSummaryCommand = new AsyncRelayCommand(
            () => RepairFileAsync(IndexRepairKind.RegenerateSummaryAndKeywords),
            CanRepairFile);
        RegenerateSemanticCommand = new AsyncRelayCommand(
            () => RepairFileAsync(IndexRepairKind.RegenerateSemanticData),
            CanRepairFile);
        RebuildSelectedSourceCommand = new AsyncRelayCommand(
            () => RepairSourceAsync(IndexRepairKind.Rebuild),
            () => _privacyService is not null && SelectedSource is not null && !IsBusy);
        ClearOcrDataCommand = new AsyncRelayCommand(
            () => ClearSelectedDataAsync(IndexedDataKind.OcrText),
            CanRepairFile);
        ClearMediaDataCommand = new AsyncRelayCommand(
            () => ClearSelectedDataAsync(IndexedDataKind.MediaDerived),
            CanRepairFile);
        ClearContentIntelligenceCommand = new AsyncRelayCommand(
            () => ClearSelectedDataAsync(IndexedDataKind.ContentIntelligence | IndexedDataKind.SummaryAndKeywords),
            CanRepairFile);
        ClearSemanticDataCommand = new AsyncRelayCommand(
            () => ClearSelectedDataAsync(IndexedDataKind.SemanticData | IndexedDataKind.Chunks),
            CanRepairFile);
        UseMetadataOnlyCommand = new AsyncRelayCommand(
            () => SetSelectedPolicyAsync(new IndexPrivacyPolicyChange(
                LevelOverride: IndexingLevel.Basic,
                SuppressOcr: true,
                SuppressSummary: true,
                SuppressSemantic: true)),
            CanRepairFile);
        ExcludeFileCommand = new AsyncRelayCommand(
            () => SetSelectedPolicyAsync(new IndexPrivacyPolicyChange(Excluded: true)),
            CanRepairFile);
        RefreshIndexingStatusCommand = new AsyncRelayCommand(RefreshIndexingStatusAsync, () => _backgroundIndexingService is not null);
        PauseIndexingCommand = new AsyncRelayCommand(PauseIndexingAsync, CanPauseIndexing);
        ResumeIndexingCommand = new AsyncRelayCommand(ResumeIndexingAsync, CanResumeIndexing);
        CancelIndexingCommand = new AsyncRelayCommand(CancelIndexingAsync, CanCancelIndexing);
        RetryFailedItemsCommand = new AsyncRelayCommand(RetryFailedItemsAsync, CanRetryFailedItems);
        PrioritizeSourceCommand = new AsyncRelayCommand(PrioritizeSourceAsync, () => _backgroundIndexingService is not null && SelectedSource is not null);
        RemoveSourceCommand = new AsyncRelayCommand(RemoveSourceAsync, () => _backgroundIndexingService is not null && SelectedSource is not null);
        RebuildBackgroundIndexCommand = new AsyncRelayCommand(RebuildBackgroundIndexAsync, () => _backgroundIndexingService is not null && !IsBusy);
        MaintainIndexCommand = new AsyncRelayCommand(MaintainIndexAsync, () => _backgroundIndexingService is not null && !IsBusy);
        OpenIndexingDiagnosticsCommand = new RelayCommand(
            () => _advancedDiagnosticsWindowService?.Show(),
            () => _advancedDiagnosticsWindowService is not null);
        if (_backgroundIndexingService is not null)
        {
            _backgroundIndexingService.ProgressChanged += OnBackgroundProgressChanged;
            _ = RefreshIndexingStatusAsync();
        }
        if (_savedViewStore is not null)
        {
            _ = LoadSavedViewsAsync();
        }
    }

    /// <summary>Gets or sets the bounded natural-language query.</summary>
    public string? QueryText
    {
        get => _queryText;
        set
        {
            if (SetProperty(ref _queryText, value))
            {
                Interlocked.Increment(ref _queryVersion);
                _operationCancellation?.Cancel();
                if (!_filtersWereEdited)
                {
                    _activeFilters.Clear();
                    _topicText = string.Empty;
                    OnPropertyChanged(nameof(HasActiveFilters));
                    ClearFiltersCommand.NotifyCanExecuteChanged();
                }
                SearchCommand.NotifyCanExecuteChanged();
                ClearQueryCommand.NotifyCanExecuteChanged();
                SaveViewCommand.NotifyCanExecuteChanged();
                UpdateSavedViewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets local explained results.</summary>
    public ReadOnlyObservableCollection<SemanticSearchHit> Hits { get; }

    /// <summary>Gets visible interpreted filters applied to the current Search.</summary>
    public ReadOnlyObservableCollection<SearchFilter> ActiveFilters { get; }

    /// <summary>Gets compact database-backed facet groups for the current canonical query.</summary>
    public ReadOnlyObservableCollection<DiscoveryFacetGroupRow> FacetGroups { get; }

    /// <summary>Gets local Saved View rules; result membership is always evaluated live.</summary>
    public ReadOnlyObservableCollection<SavedDiscoveryViewRow> SavedViews { get; }

    /// <summary>Gets whether current-context facet values are available.</summary>
    public bool HasFacetGroups => FacetGroups.Any(group => group.Values.Count > 0);

    /// <summary>Gets truthful query eligibility versus hydration coverage.</summary>
    public string CandidateCoverageText
    {
        get => _candidateCoverageText;
        private set => SetProperty(ref _candidateCoverageText, value);
    }

    /// <summary>Gets or sets the bounded name used to create or update a Saved View.</summary>
    public string? SavedViewName
    {
        get => _savedViewName;
        set
        {
            if (SetProperty(ref _savedViewName, value))
            {
                SaveViewCommand.NotifyCanExecuteChanged();
                UpdateSavedViewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the selected dynamic Saved View.</summary>
    public SavedDiscoveryViewRow? SelectedSavedView
    {
        get => _selectedSavedView;
        set
        {
            if (SetProperty(ref _selectedSavedView, value))
            {
                if (value is not null)
                {
                    SavedViewName = value.Name;
                }

                OpenSavedViewCommand.NotifyCanExecuteChanged();
                UpdateSavedViewCommand.NotifyCanExecuteChanged();
                DeleteSavedViewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether any visible interpreted filter is active.</summary>
    public bool HasActiveFilters => ActiveFilters.Count > 0;

    /// <summary>Gets or sets whether the contextual filter area is expanded.</summary>
    public bool AreFiltersVisible
    {
        get => _areFiltersVisible;
        set => SetProperty(ref _areFiltersVisible, value);
    }

    /// <summary>Gets or sets whether explainable direct relationships may expand Search results.</summary>
    public bool IncludeRelationshipContext
    {
        get => _includeRelationshipContext;
        set => SetProperty(ref _includeRelationshipContext, value);
    }

    /// <summary>Gets or sets whether bounded Knowledge Graph context may supplement Search.</summary>
    public bool IncludeGraphContext
    {
        get => _includeGraphContext;
        set => SetProperty(ref _includeGraphContext, value);
    }

    /// <summary>Gets or sets whether this Search may use optional bounded local-AI assistance.</summary>
    public bool UseAiAssistance
    {
        get => _useAiAssistance;
        set
        {
            if (SetProperty(ref _useAiAssistance, value && IsAiAssistanceAvailable))
            {
                OnPropertyChanged(nameof(SearchModeText));
            }
        }
    }

    /// <summary>Gets a plain-language description of the active Search composition.</summary>
    public string SearchModeText => UseAiAssistance
        ? "Search mode: Hybrid + AI assistance"
        : "Search mode: Hybrid";

    /// <summary>Gets whether settings provide an enabled local model for AI-assisted Search.</summary>
    public bool IsAiAssistanceAvailable =>
        _configurationService.Current.Ai.IsCapabilityEnabled(AiCapability.SearchAssistance) &&
        !string.IsNullOrWhiteSpace(_configurationService.Current.Ai.SelectedModel);

    /// <summary>Gets actionable availability guidance for the optional per-query control.</summary>
    public string AiAssistanceAvailabilityText => IsAiAssistanceAvailable
        ? "Optional local AI can refine only the order of known results for this Search."
        : "Enable AI-assisted Search and select an installed Ollama model in Settings to use this option.";

    /// <summary>Gets the safe outcome of the most recent optional AI-assisted ordering attempt.</summary>
    public string AiAssistanceText
    {
        get => _aiAssistanceText;
        private set => SetProperty(ref _aiAssistanceText, value);
    }

    /// <summary>Gets graph-projection coverage independently from deep-index Search coverage.</summary>
    public string GraphCoverageText
    {
        get => _graphCoverageText;
        private set => SetProperty(ref _graphCoverageText, value);
    }

    /// <summary>Gets the selected Search result for privacy inspection.</summary>
    public SemanticSearchHit? SelectedHit
    {
        get => _selectedHit;
        private set
        {
            if (SetProperty(ref _selectedHit, value))
            {
                if (value is null)
                {
                    SelectedMediaThumbnailPath = null;
                }

                NotifyPrivacyCommands();
            }
        }
    }

    /// <summary>Gets the lazily generated application-owned still-image preview for the inspected result.</summary>
    public string? SelectedMediaThumbnailPath
    {
        get => _selectedMediaThumbnailPath;
        private set
        {
            if (SetProperty(ref _selectedMediaThumbnailPath, value))
            {
                OnPropertyChanged(nameof(HasSelectedMediaThumbnail));
            }
        }
    }

    /// <summary>Gets whether a bounded cached preview is available for the inspected result.</summary>
    public bool HasSelectedMediaThumbnail => !string.IsNullOrWhiteSpace(SelectedMediaThumbnailPath);

    /// <summary>Gets the provider-neutral indexed-data inspection record.</summary>
    public IndexPrivacyItem? PrivacyItem
    {
        get => _privacyItem;
        private set
        {
            if (SetProperty(ref _privacyItem, value))
            {
                OnPropertyChanged(nameof(HasPrivacyItem));
                NotifyPrivacyCommands();
            }
        }
    }

    /// <summary>Gets whether indexed-data categories are available for inspection.</summary>
    public bool HasPrivacyItem => PrivacyItem is not null;

    /// <summary>Gets a content-free description of retained index categories.</summary>
    public string PrivacyText
    {
        get => _privacyText;
        private set => SetProperty(ref _privacyText, value);
    }

    /// <summary>Gets whether forgetting one file awaits explicit confirmation.</summary>
    public bool IsForgetFilePending
    {
        get => _isForgetFilePending;
        private set
        {
            if (SetProperty(ref _isForgetFilePending, value))
            {
                NotifyPrivacyCommands();
            }
        }
    }

    /// <summary>Gets whether forgetting one source awaits explicit confirmation.</summary>
    public bool IsForgetSourcePending
    {
        get => _isForgetSourcePending;
        private set
        {
            if (SetProperty(ref _isForgetSourcePending, value))
            {
                NotifyPrivacyCommands();
            }
        }
    }

    /// <summary>Gets configured durable indexing sources.</summary>
    public ReadOnlyObservableCollection<IndexingSource> Sources { get; }

    /// <summary>Gets privacy-minimized indexing failures, newest first.</summary>
    public ReadOnlyObservableCollection<IndexingFailure> IndexingFailures { get; }

    /// <summary>Gets whether inspectable indexing failures are available.</summary>
    public bool HasIndexingFailures => IndexingFailures.Count > 0;

    /// <summary>Gets a plain-language failure-list summary.</summary>
    public string FailureSummaryText => HasIndexingFailures
        ? $"{IndexingFailures.Count:N0} recent indexing failure(s). File contents and full paths are not shown."
        : "No indexing failures are currently retained.";

    /// <summary>Gets or sets the selected durable indexing source.</summary>
    public IndexingSource? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                PrioritizeSourceCommand.NotifyCanExecuteChanged();
                RemoveSourceCommand.NotifyCanExecuteChanged();
                RequestForgetSourceCommand.NotifyCanExecuteChanged();
                ConfirmForgetSourceCommand.NotifyCanExecuteChanged();
                RebuildSelectedSourceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether at least one result is available.</summary>
    public bool HasHits => Hits.Count > 0;

    /// <summary>Gets the current operation state.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether index deletion awaits explicit confirmation.</summary>
    public bool IsClearPending
    {
        get => _isClearPending;
        private set
        {
            if (SetProperty(ref _isClearPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets normalized indexing progress from zero through one.</summary>
    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    /// <summary>Gets an accessible indexing progress description.</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    /// <summary>Gets consistent local Search status.</summary>
    public StatusPresentation Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Gets whether a durable indexing run has been observed.</summary>
    public bool HasIndexingActivity => BackgroundProgress.RunId is not null;

    /// <summary>Gets the latest persistent indexing snapshot.</summary>
    public IndexingProgressSnapshot BackgroundProgress
    {
        get => _backgroundProgress;
        private set
        {
            if (SetProperty(ref _backgroundProgress, value))
            {
                OnPropertyChanged(nameof(HasIndexingActivity));
                OnPropertyChanged(nameof(BackgroundProgressValue));
                OnPropertyChanged(nameof(IsBackgroundProgressIndeterminate));
                OnPropertyChanged(nameof(BackgroundStateText));
                OnPropertyChanged(nameof(CurrentStageText));
                OnPropertyChanged(nameof(CurrentFileText));
                OnPropertyChanged(nameof(ProcessedCountText));
                OnPropertyChanged(nameof(RemainingCountText));
                OnPropertyChanged(nameof(OutcomeCountText));
                OnPropertyChanged(nameof(ThroughputText));
                OnPropertyChanged(nameof(EstimatedTimeText));
                OnPropertyChanged(nameof(HasEstimatedTime));
                NotifyBackgroundCommands();
            }
        }
    }

    /// <summary>Gets normalized persistent progress from zero through one.</summary>
    public double BackgroundProgressValue => BackgroundProgress.PhasePercentage / 100d;

    /// <summary>Gets whether discovery is active but a truthful total is not yet known.</summary>
    public bool IsBackgroundProgressIndeterminate =>
        BackgroundProgress.TotalDiscovered <= 0 &&
        BackgroundProgress.Status is IndexingRunStatus.Pending or
            IndexingRunStatus.Running or
            IndexingRunStatus.Waiting or
            IndexingRunStatus.Cancelling;

    /// <summary>Gets the durable run-state label.</summary>
    public string BackgroundStateText => BackgroundProgress.Phase switch
    {
        IndexingProgressPhase.DiscoveringFiles => "Indexing state: scanning files; Search coverage is appearing progressively",
        IndexingProgressPhase.BuildingBaseSearchCoverage => "Indexing state: building base Search coverage",
        IndexingProgressPhase.DeeperAnalysis => "Indexing state: files searchable; enabled deeper analysis is continuing",
        IndexingProgressPhase.Paused => "Indexing state: paused; completed Search coverage remains available",
        IndexingProgressPhase.Waiting => "Indexing state: waiting; completed Search coverage remains available",
        IndexingProgressPhase.Cancelled => "Indexing state: cancelled at a durable boundary",
        IndexingProgressPhase.Failed => "Indexing state: completed coverage retained; some work failed and can be retried",
        _ => "Indexing state: complete",
    };

    /// <summary>Gets the current durable stage label.</summary>
    public string CurrentStageText => BackgroundProgress.CurrentStage is { } stage
        ? $"Stage: {FormatStage(stage)}"
        : "Stage: no active stage";

    /// <summary>Gets the display-safe current file label.</summary>
    public string CurrentFileText => string.IsNullOrWhiteSpace(BackgroundProgress.CurrentFile)
        ? "Current file: none"
        : $"Current file: {BackgroundProgress.CurrentFile}";

    /// <summary>Gets processed and discovered counts.</summary>
    public string ProcessedCountText => BackgroundProgress.TotalDiscovered > 0
        ? $"Processed {BackgroundProgress.Processed:N0} of {BackgroundProgress.TotalDiscovered:N0} discovered files"
        : $"Processed {BackgroundProgress.Processed:N0} files; discovery is still determining the total";

    /// <summary>Gets the remaining-file count.</summary>
    public string RemainingCountText => BackgroundProgress.TotalDiscovered > 0
        ? $"{BackgroundProgress.Remaining:N0} files remaining"
        : "Files remaining: not yet known";

    /// <summary>Gets completed, skipped, failed, waiting, and retry counts.</summary>
    public string OutcomeCountText =>
        $"Completed {BackgroundProgress.Completed:N0} · Skipped {BackgroundProgress.Skipped:N0} · Failed {BackgroundProgress.Failed:N0} · Waiting {BackgroundProgress.Waiting:N0} · Retrying {BackgroundProgress.RetryScheduled:N0}";

    /// <summary>Gets recent throughput.</summary>
    public string ThroughputText => BackgroundProgress.FilesPerSecond > 0
        ? $"{BackgroundProgress.FilesPerSecond:N2} files/second"
        : "Processing speed will appear after work starts.";

    /// <summary>Gets whether an estimate has enough samples to be meaningful.</summary>
    public bool HasEstimatedTime =>
        BackgroundProgress.Phase == IndexingProgressPhase.DeeperAnalysis &&
        BackgroundProgress.EstimatedRemaining.HasValue;

    /// <summary>Gets an explicitly labelled estimated remaining-time value.</summary>
    public string EstimatedTimeText => BackgroundProgress.EstimatedRemaining is { } estimate
        ? $"Estimated time remaining: {FormatDuration(estimate)}"
        : string.Empty;

    /// <summary>Gets progressive Search coverage in plain language.</summary>
    public string CoverageText
    {
        get => _coverageText;
        private set => SetProperty(ref _coverageText, value);
    }

    /// <summary>Gets current and maximum physical index storage.</summary>
    public string StorageText
    {
        get => _storageText;
        private set => SetProperty(ref _storageText, value);
    }

    /// <summary>Gets the provider-neutral storage category breakdown.</summary>
    public string StorageBreakdownText
    {
        get => _storageBreakdownText;
        private set => SetProperty(ref _storageBreakdownText, value);
    }

    /// <summary>Gets the bounded local search command.</summary>
    public IAsyncRelayCommand SearchCommand { get; }

    /// <summary>Gets the incremental index command.</summary>
    public IAsyncRelayCommand BuildIndexCommand { get; }

    /// <summary>Gets the explicit full rebuild command.</summary>
    public IAsyncRelayCommand RebuildIndexCommand { get; }

    /// <summary>Gets the active operation cancellation command.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Gets the query reset command.</summary>
    public IRelayCommand ClearQueryCommand { get; }

    /// <summary>Gets the command that starts index-clear confirmation.</summary>
    public IRelayCommand RequestClearIndexCommand { get; }

    /// <summary>Gets the confirmed application-owned index deletion command.</summary>
    public IAsyncRelayCommand ConfirmClearIndexCommand { get; }

    /// <summary>Gets the index-clear cancellation command.</summary>
    public IRelayCommand CancelClearIndexCommand { get; }

    /// <summary>Gets the controlled shell-open command for one known hit.</summary>
    public IAsyncRelayCommand<SemanticSearchHit> OpenFileCommand { get; }

    /// <summary>Gets the controlled containing-folder command for one known hit.</summary>
    public IAsyncRelayCommand<SemanticSearchHit> OpenContainingFolderCommand { get; }

    /// <summary>Gets the cross-platform clipboard command for one known result path.</summary>
    public IAsyncRelayCommand<SemanticSearchHit> CopyFullPathCommand { get; }

    /// <summary>Gets the stable-ID handoff from Search into the richer Files surface.</summary>
    public IRelayCommand<SemanticSearchHit> OpenInFilesCommand { get; }

    /// <summary>Gets the command that expands or collapses contextual filters.</summary>
    public IRelayCommand ToggleFiltersCommand { get; }

    /// <summary>Gets the command that removes one visible interpreted filter.</summary>
    public IAsyncRelayCommand<SearchFilter> RemoveFilterCommand { get; }

    /// <summary>Gets the command that removes every visible filter while retaining topic terms.</summary>
    public IAsyncRelayCommand ClearFiltersCommand { get; }

    /// <summary>Gets the command that toggles one canonical facet value.</summary>
    public IAsyncRelayCommand<DiscoveryFacetValueRow> ToggleFacetCommand { get; }

    /// <summary>Gets the shortcut that filters to unresolved Moderate Smart Tag suggestions.</summary>
    public IAsyncRelayCommand ShowModerateSuggestionsCommand { get; }

    /// <summary>Gets the command that saves the current live query/filter rule.</summary>
    public IAsyncRelayCommand SaveViewCommand { get; }

    /// <summary>Gets the command that evaluates the selected Saved View against the current index.</summary>
    public IAsyncRelayCommand OpenSavedViewCommand { get; }

    /// <summary>Gets the command that replaces the selected Saved View rule.</summary>
    public IAsyncRelayCommand UpdateSavedViewCommand { get; }

    /// <summary>Gets the command that deletes only the selected Saved View rule.</summary>
    public IAsyncRelayCommand DeleteSavedViewCommand { get; }

    /// <summary>Gets the command that inspects retained index categories for one result.</summary>
    public IAsyncRelayCommand<SemanticSearchHit> InspectIndexedDataCommand { get; }

    /// <summary>Gets the command that requests file-forget confirmation.</summary>
    public IRelayCommand RequestForgetFileCommand { get; }

    /// <summary>Gets the confirmed index-only file-forget command.</summary>
    public IAsyncRelayCommand ConfirmForgetFileCommand { get; }

    /// <summary>Gets the file-forget cancellation command.</summary>
    public IRelayCommand CancelForgetFileCommand { get; }

    /// <summary>Gets the command that requests source-forget confirmation.</summary>
    public IRelayCommand RequestForgetSourceCommand { get; }

    /// <summary>Gets the confirmed index-only source-forget command.</summary>
    public IAsyncRelayCommand ConfirmForgetSourceCommand { get; }

    /// <summary>Gets the source-forget cancellation command.</summary>
    public IRelayCommand CancelForgetSourceCommand { get; }

    /// <summary>Gets the selective full-file re-index command.</summary>
    public IAsyncRelayCommand ReindexFileCommand { get; }

    /// <summary>Gets the selected-file consistency-verification command.</summary>
    public IAsyncRelayCommand VerifyFileCommand { get; }

    /// <summary>Gets the selected-file failed-stage retry command.</summary>
    public IAsyncRelayCommand RetryFileCommand { get; }

    /// <summary>Gets the selected-file metadata refresh command.</summary>
    public IAsyncRelayCommand RefreshMetadataCommand { get; }

    /// <summary>Gets the selected-file extracted-text refresh command.</summary>
    public IAsyncRelayCommand RefreshTextCommand { get; }

    /// <summary>Gets the selected-file OCR refresh command.</summary>
    public IAsyncRelayCommand RefreshOcrCommand { get; }

    /// <summary>Gets the selected-file summary/keyword regeneration command.</summary>
    public IAsyncRelayCommand RegenerateSummaryCommand { get; }

    /// <summary>Gets the selected-file related-concept regeneration command.</summary>
    public IAsyncRelayCommand RegenerateSemanticCommand { get; }

    /// <summary>Gets the selective selected-source rebuild command.</summary>
    public IAsyncRelayCommand RebuildSelectedSourceCommand { get; }

    /// <summary>Gets the index-only OCR-data clear command.</summary>
    public IAsyncRelayCommand ClearOcrDataCommand { get; }

    /// <summary>Gets the index-only structured media-data clear command.</summary>
    public IAsyncRelayCommand ClearMediaDataCommand { get; }

    /// <summary>Gets the command that clears topics, entities, and generated summaries without touching the source file.</summary>
    public IAsyncRelayCommand ClearContentIntelligenceCommand { get; }

    /// <summary>Gets the index-only related-concept data clear command.</summary>
    public IAsyncRelayCommand ClearSemanticDataCommand { get; }

    /// <summary>Gets the selected-file metadata-only policy command.</summary>
    public IAsyncRelayCommand UseMetadataOnlyCommand { get; }

    /// <summary>Gets the selected-file future deep-index exclusion command.</summary>
    public IAsyncRelayCommand ExcludeFileCommand { get; }

    /// <summary>Gets the persistent-progress refresh command.</summary>
    public IAsyncRelayCommand RefreshIndexingStatusCommand { get; }

    /// <summary>Gets the durable pause command.</summary>
    public IAsyncRelayCommand PauseIndexingCommand { get; }

    /// <summary>Gets the durable resume command.</summary>
    public IAsyncRelayCommand ResumeIndexingCommand { get; }

    /// <summary>Gets the cooperative background cancellation command.</summary>
    public IAsyncRelayCommand CancelIndexingCommand { get; }

    /// <summary>Gets the failed and dependency-waiting retry command.</summary>
    public IAsyncRelayCommand RetryFailedItemsCommand { get; }

    /// <summary>Gets the selected-source prioritization command.</summary>
    public IAsyncRelayCommand PrioritizeSourceCommand { get; }

    /// <summary>Gets the selected-source removal command.</summary>
    public IAsyncRelayCommand RemoveSourceCommand { get; }

    /// <summary>Gets the durable derived-index rebuild command.</summary>
    public IAsyncRelayCommand RebuildBackgroundIndexCommand { get; }

    /// <summary>Gets the retention, cleanup, and compaction command.</summary>
    public IAsyncRelayCommand MaintainIndexCommand { get; }

    /// <summary>Gets the command that opens shared redacted diagnostics for indexing runs.</summary>
    public IRelayCommand OpenIndexingDiagnosticsCommand { get; }

    /// <summary>Raised when Search requests a stable-ID transition into Files.</summary>
    public event EventHandler<DiscoveryFileOpenRequest>? OpenInFilesRequested;

    /// <summary>Refreshes command availability after persisted feature settings change.</summary>
    public void RefreshFeatureAvailability()
    {
        if (!IsAiAssistanceAvailable)
        {
            UseAiAssistance = false;
        }

        OnPropertyChanged(nameof(IsAiAssistanceAvailable));
        OnPropertyChanged(nameof(AiAssistanceAvailabilityText));
        NotifyCommands();
        _ = RefreshIndexingStatusAsync();
    }

    /// <summary>Refreshes persistent indexing progress and configured sources.</summary>
    public async Task RefreshAsync()
    {
        await RefreshIndexingStatusAsync();
        await LoadSavedViewsAsync(SelectedSavedView?.Id);
    }

    /// <summary>Captures one bounded canonical discovery context for a stable Search hit.</summary>
    public DiscoveryWorkflowContext? CaptureDiscoveryContext(SemanticSearchHit? hit)
    {
        if (hit is not { FileId.Length: > 0 } || !Hits.Contains(hit))
        {
            return null;
        }

        var resultIds = Hits
            .Select(item => item.FileId)
            .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(SearchLimits.MaximumRankedResults)
            .ToArray();
        return new DiscoveryWorkflowContext(
            new DiscoveryQueryState(QueryText?.Trim() ?? string.Empty, _activeFilters.ToArray()),
            SelectedSavedView?.Id,
            hit.FileId,
            _activeFilters.Any(filter => filter.Kind == SearchFilterKind.UnresolvedModerateSmartTag),
            _activeFilters.FirstOrDefault(filter => filter.Kind == SearchFilterKind.Source)?.Value,
            resultIds);
    }

    /// <summary>Restores the exact canonical Search, facet, Saved View, and review state after Files.</summary>
    public async Task RestoreDiscoveryContextAsync(DiscoveryWorkflowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _filtersWereEdited = true;
        QueryText = context.Query.QueryText;
        _activeFilters.Clear();
        foreach (var filter in context.Query.Filters.Take(SearchLimits.MaximumFilters))
        {
            _activeFilters.Add(filter);
        }

        _topicText = context.Query.QueryText;
        AreFiltersVisible = _activeFilters.Count > 0;
        SelectedSavedView = context.SavedViewId is null
            ? null
            : _savedViews.FirstOrDefault(view => string.Equals(view.Id, context.SavedViewId, StringComparison.Ordinal));
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        if (CanSearch())
        {
            await SearchAsync();
        }
    }

    /// <summary>Reports a bounded workflow-navigation failure without losing Search state.</summary>
    public void ReportWorkflowFailure(string message) => Status = StatusPresentation.Warning(message);

    /// <summary>Runs a filter-only Search from another first-party surface using a canonical filter.</summary>
    public async Task ApplyExternalFilterAsync(SearchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _activeFilters.Clear();
        _activeFilters.Add(filter);
        _topicText = string.Empty;
        _filtersWereEdited = true;
        AreFiltersVisible = true;
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        await SearchAsync();
    }

    /// <summary>Opens the canonical unresolved Moderate suggestion discovery state.</summary>
    public Task OpenModerateReviewAsync() => ShowModerateSuggestionsAsync();

    /// <summary>Evaluates one durable Saved View shortcut by stable identifier.</summary>
    public async Task<bool> OpenSavedViewByIdAsync(string savedViewId)
    {
        if (string.IsNullOrWhiteSpace(savedViewId))
        {
            return false;
        }

        await LoadSavedViewsAsync(savedViewId);
        if (SelectedSavedView is null)
        {
            ReportWorkflowFailure("The Saved View is no longer available. No Search state was changed.");
            return false;
        }

        await OpenSavedViewAsync();
        return true;
    }

    /// <summary>Re-evaluates the current canonical query after authoritative indexed metadata changes.</summary>
    public async Task RefreshCurrentQueryAsync()
    {
        if (CanSearch())
        {
            await SearchAsync();
        }
        else
        {
            await RefreshAsync();
        }
    }

    private async Task LoadSavedViewsAsync(string? selectedId = null)
    {
        if (_savedViewStore is null)
        {
            return;
        }

        try
        {
            var views = await _savedViewStore.ListAsync();
            await ApplyOnUiThreadAsync(() =>
            {
                _savedViews.Clear();
                foreach (var view in views)
                {
                    _savedViews.Add(SavedDiscoveryViewRow.FromModel(view));
                }

                SelectedSavedView = selectedId is null
                    ? SelectedSavedView is null
                        ? null
                        : _savedViews.FirstOrDefault(view => view.Id == SelectedSavedView.Id)
                    : _savedViews.FirstOrDefault(view => view.Id == selectedId);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            Status = StatusPresentation.Warning("Saved Views are temporarily unavailable. Search and files were not affected.");
        }
    }

    private async Task ToggleFacetAsync(DiscoveryFacetValueRow? value)
    {
        if (value is null)
        {
            return;
        }

        var filter = value.ToFilter();
        var existing = _activeFilters.FirstOrDefault(item =>
            item.Kind == filter.Kind && string.Equals(item.Value, filter.Value, StringComparison.Ordinal));
        if (existing is null)
        {
            _activeFilters.Add(filter);
        }
        else
        {
            _activeFilters.Remove(existing);
        }

        _filtersWereEdited = true;
        AreFiltersVisible = true;
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        SaveViewCommand.NotifyCanExecuteChanged();
        UpdateSavedViewCommand.NotifyCanExecuteChanged();
        await SearchAsync();
    }

    private async Task ShowModerateSuggestionsAsync()
    {
        if (!_activeFilters.Any(filter => filter.Kind == SearchFilterKind.UnresolvedModerateSmartTag))
        {
            _activeFilters.Add(new SearchFilter(
                "smart-tags:unresolved-moderate",
                SearchFilterKind.UnresolvedModerateSmartTag,
                "true",
                "Smart Tags: unresolved Moderate suggestions"));
        }

        _filtersWereEdited = true;
        AreFiltersVisible = true;
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        await SearchAsync();
    }

    private bool CanSaveView() =>
        _savedViewStore is not null &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(SavedViewName) &&
        (HasActiveFilters || !string.IsNullOrWhiteSpace(QueryText));

    private async Task SaveViewAsync()
    {
        if (_savedViewStore is null || !CanSaveView())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var view = new SavedDiscoveryView(
            $"saved-view:{Guid.NewGuid():N}",
            SavedViewName!.Trim(),
            new DiscoveryQueryState(QueryText?.Trim() ?? string.Empty, _activeFilters.ToArray()),
            1,
            now,
            now);
        try
        {
            var saved = await _savedViewStore.SaveAsync(view);
            await LoadSavedViewsAsync(saved.Id);
            Status = StatusPresentation.Success("Saved View created. It will always evaluate against the current local index.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            Status = StatusPresentation.Warning("The Saved View could not be stored. Search and files were not affected.");
        }
    }

    private async Task OpenSavedViewAsync()
    {
        var selected = SelectedSavedView?.Model;
        if (selected is null)
        {
            return;
        }

        _filtersWereEdited = true;
        QueryText = selected.Query.QueryText;
        _activeFilters.Clear();
        foreach (var filter in selected.Query.Filters)
        {
            _activeFilters.Add(filter);
        }

        _topicText = selected.Query.QueryText;
        AreFiltersVisible = _activeFilters.Count > 0;
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        await SearchAsync();
    }

    private async Task UpdateSavedViewAsync()
    {
        if (_savedViewStore is null || SelectedSavedView?.Model is not { } existing || !CanSaveView())
        {
            return;
        }

        var updated = existing with
        {
            Name = SavedViewName!.Trim(),
            Query = new DiscoveryQueryState(QueryText?.Trim() ?? string.Empty, _activeFilters.ToArray()),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        try
        {
            var saved = await _savedViewStore.SaveAsync(updated);
            await LoadSavedViewsAsync(saved.Id);
            Status = StatusPresentation.Success("Saved View updated. No result membership was copied.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            Status = StatusPresentation.Warning("The Saved View could not be updated. Its previous rule was preserved.");
        }
    }

    private async Task DeleteSavedViewAsync()
    {
        if (_savedViewStore is null || SelectedSavedView is null)
        {
            return;
        }

        try
        {
            var removed = await _savedViewStore.DeleteAsync(SelectedSavedView.Id);
            if (removed)
            {
                SelectedSavedView = null;
                SavedViewName = null;
                await LoadSavedViewsAsync();
                Status = StatusPresentation.Information("Saved View deleted. Indexed files and Search data were unchanged.");
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            Status = StatusPresentation.Warning("The Saved View could not be deleted. Search and files were not affected.");
        }
    }

    private void PublishFacetSnapshot(DiscoveryFacetSnapshot snapshot)
    {
        if (snapshot.IsAvailable)
        {
            var visibleGroups = snapshot.Groups.Where(group => group.Values.Count > 0).ToArray();
            var visibleKinds = visibleGroups.Select(group => group.Kind).ToHashSet();
            for (var index = _facetGroups.Count - 1; index >= 0; index--)
            {
                if (!visibleKinds.Contains(_facetGroups[index].Kind))
                {
                    _facetGroups.RemoveAt(index);
                }
            }

            foreach (var group in visibleGroups)
            {
                var existing = _facetGroups.FirstOrDefault(row => row.Kind == group.Kind);
                if (existing is null)
                {
                    _facetGroups.Add(new DiscoveryFacetGroupRow(
                        group.Kind,
                        group.DisplayName,
                        Array.AsReadOnly(group.Values
                            .Select(value => new DiscoveryFacetValueRow(
                                group.Kind,
                                value.CanonicalId,
                                value.DisplayName,
                                value.Count,
                                value.IsSelected))
                            .ToArray())));
                }
                else
                {
                    existing.Apply(group.Values);
                }
            }
        }
        else
        {
            _facetGroups.Clear();
        }

        OnPropertyChanged(nameof(HasFacetGroups));
    }

    private bool CanSearch() =>
        _searchService is not null &&
        _configurationService.Current.SemanticSearch.Enabled &&
        !IsBusy &&
        (!string.IsNullOrWhiteSpace(QueryText) || HasActiveFilters);

    private bool CanIndex() =>
        _indexer is not null &&
        _configurationService.Current.SemanticSearch.Enabled &&
        !IsBusy;

    private async Task SearchAsync()
    {
        if (_searchService is null)
        {
            return;
        }

        var queryVersion = Volatile.Read(ref _queryVersion);
        using var operation = BeginOperation();
        try
        {
            var request = (_filtersWereEdited
                ? new SearchRequest(
                    QueryText ?? string.Empty,
                    _activeFilters.ToArray(),
                    InterpretFilters: false,
                    TopicTextOverride: _topicText,
                    IncludeRelationshipContext: IncludeRelationshipContext)
                : new SearchRequest(QueryText ?? string.Empty, IncludeRelationshipContext: IncludeRelationshipContext)) with
            {
                IncludeGraphContext = IncludeGraphContext,
                UseAiAssistance = UseAiAssistance,
            };
            var result = await _searchService.SearchAsync(request, operation.Token);
            if (queryVersion != Volatile.Read(ref _queryVersion))
            {
                return;
            }

            _hits.Clear();
            foreach (var hit in result.Hits)
            {
                _hits.Add(hit);
            }

            _activeFilters.Clear();
            foreach (var filter in result.Interpretation.Filters)
            {
                _activeFilters.Add(filter);
            }

            _topicText = result.Interpretation.TopicText;
            AreFiltersVisible = _activeFilters.Count > 0 || AreFiltersVisible;
            OnPropertyChanged(nameof(HasActiveFilters));
            ClearFiltersCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasHits));
            GraphCoverageText = FormatGraphCoverage(result.GraphCoverage, IncludeGraphContext);
            AiAssistanceText = result.AiAssistance.Message;
            CandidateCoverageText = result.CandidateCoverage.Message;
            var facets = await _searchService.GetFacetCountsAsync(
                new SearchRequest(
                    QueryText ?? string.Empty,
                    _activeFilters.ToArray(),
                    InterpretFilters: false,
                    TopicTextOverride: _topicText,
                    IncludeRelationshipContext: false),
                operation.Token);
            if (queryVersion != Volatile.Read(ref _queryVersion))
            {
                return;
            }

            PublishFacetSnapshot(facets);
            Status = Present(result.State, result.Message);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task RemoveFilterAsync(SearchFilter? filter)
    {
        if (filter is null || !_activeFilters.Remove(filter))
        {
            return;
        }

        _filtersWereEdited = true;
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        ToggleFacetCommand.NotifyCanExecuteChanged();
        ShowModerateSuggestionsCommand.NotifyCanExecuteChanged();
        SaveViewCommand.NotifyCanExecuteChanged();
        OpenSavedViewCommand.NotifyCanExecuteChanged();
        UpdateSavedViewCommand.NotifyCanExecuteChanged();
        DeleteSavedViewCommand.NotifyCanExecuteChanged();
        await SearchAsync();
    }

    private async Task ClearFiltersAsync()
    {
        if (_activeFilters.Count == 0)
        {
            return;
        }

        _activeFilters.Clear();
        _filtersWereEdited = true;
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        await SearchAsync();
    }

    private async Task BuildIndexAsync(bool rebuild)
    {
        if (_indexer is null)
        {
            return;
        }

        using var operation = BeginOperation();
        ProgressValue = 0;
        ProgressText = rebuild ? "Rebuilding the local Search index..." : "Refreshing the local Search index...";
        var progress = new Progress<SemanticIndexProgress>(value =>
        {
            ProgressValue = value.TotalCount == 0 ? 0 : value.ProcessedCount / (double)value.TotalCount;
            ProgressText = value.Message;
        });
        try
        {
            var result = await _indexer.BuildAsync(rebuild, progress, operation.Token);
            Status = Present(result.State, result.Message);
            ProgressText = result.Message;
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private void ClearQuery()
    {
        QueryText = null;
        _hits.Clear();
        _activeFilters.Clear();
        _topicText = string.Empty;
        _filtersWereEdited = false;
        SelectedHit = null;
        _facetGroups.Clear();
        CandidateCoverageText = string.Empty;
        PrivacyItem = null;
        PrivacyText = "Select Inspect indexed data on a Search result to review retained categories.";
        IsForgetFilePending = false;
        OnPropertyChanged(nameof(HasHits));
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        Status = StatusPresentation.Information("Query cleared. The local index was not changed.");
    }

    private void RequestClearIndex()
    {
        IsClearPending = true;
        Status = StatusPresentation.Warning(
            "Confirm clearing application-owned generated Search data. Indexed sources remain registered and source files remain untouched.");
    }

    private async Task ConfirmClearIndexAsync()
    {
        if (_indexStore is null)
        {
            return;
        }

        using var operation = BeginOperation();
        try
        {
            await _indexStore.ClearAsync(operation.Token);
            if (_mediaThumbnailProvider is not null)
            {
                await _mediaThumbnailProvider.ClearAsync(operation.Token);
            }

            if (_privacyService is not null && _backgroundIndexingService is not null)
            {
                var sources = await _backgroundIndexingService.GetSourcesAsync(operation.Token);
                foreach (var source in sources)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    await _privacyService.ForgetSourceAsync(source.Id, operation.Token);
                }
            }

            _hits.Clear();
            OnPropertyChanged(nameof(HasHits));
            IsClearPending = false;
            ProgressValue = 0;
            ProgressText = "Generated local Search data is empty.";
            Status = StatusPresentation.Success(
                "Generated local Search data was cleared. Indexed sources remain registered and source files were not changed.");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            IsClearPending = false;
            Status = StatusPresentation.Warning(
                "Index clearing was cancelled safely. Any completed index-only clears remain applied; source files were not changed.");
        }
        catch (Exception)
        {
            IsClearPending = false;
            Status = StatusPresentation.Error(
                "Generated Search data could not be completely cleared. Review indexed sources and retry; source files were not changed.");
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private void CancelClearIndex()
    {
        IsClearPending = false;
        Status = StatusPresentation.Information("Generated Search-data clear cancelled.");
    }

    private bool CanOpenHit(SemanticSearchHit? hit) =>
        _launcher is not null &&
        !IsBusy &&
        hit is not null &&
        Hits.Any(candidate =>
            string.Equals(candidate.FullPath, hit.FullPath, StringComparison.Ordinal) &&
            string.Equals(candidate.FileName, hit.FileName, StringComparison.Ordinal));

    private bool CanOpenInFiles(SemanticSearchHit? hit) =>
        !IsBusy && CaptureDiscoveryContext(hit) is not null;

    private void OpenInFiles(SemanticSearchHit? hit)
    {
        var context = CaptureDiscoveryContext(hit);
        if (hit?.FileId is not { Length: > 0 } fileId || context is null)
        {
            return;
        }

        OpenInFilesRequested?.Invoke(this, new DiscoveryFileOpenRequest(fileId, context));
    }

    private Task OpenFileAsync(SemanticSearchHit? hit) => OpenAsync(hit, false);

    private Task OpenFolderAsync(SemanticSearchHit? hit) => OpenAsync(hit, true);

    private bool CanCopyHit(SemanticSearchHit? hit) =>
        _clipboard is not null &&
        !IsBusy &&
        hit is not null &&
        Hits.Contains(hit);

    private async Task CopyFullPathAsync(SemanticSearchHit? hit)
    {
        if (!CanCopyHit(hit) || _clipboard is null || hit is null)
        {
            return;
        }

        try
        {
            await _clipboard.SetTextAsync(hit.FullPath, CancellationToken.None);
            Status = StatusPresentation.Success("Full path copied to the clipboard.");
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            Status = StatusPresentation.Warning(
                "The full path could not be copied on this platform. Select the path text and copy it manually.");
        }
    }

    private async Task OpenAsync(SemanticSearchHit? hit, bool folder)
    {
        if (!CanOpenHit(hit) || _launcher is null || hit is null)
        {
            return;
        }

        var result = folder
            ? await _launcher.OpenContainingFolderAsync(hit.FullPath, CancellationToken.None)
            : await _launcher.OpenFileAsync(hit.FullPath, CancellationToken.None);
        Status = result.Succeeded
            ? StatusPresentation.Success(result.Message)
            : StatusPresentation.Warning(result.Message);
    }

    private bool CanInspectHit(SemanticSearchHit? hit) =>
        _privacyService is not null &&
        !IsBusy &&
        hit is { FileId.Length: > 0 } &&
        Hits.Contains(hit);

    private bool CanRepairFile() =>
        _privacyService is not null &&
        PrivacyItem is not null &&
        !IsBusy;

    private async Task InspectIndexedDataAsync(SemanticSearchHit? hit)
    {
        if (!CanInspectHit(hit) || _privacyService is null || hit?.FileId is null)
        {
            Status = StatusPresentation.Information(
                "Detailed index inspection is available after this result enters the background index.");
            return;
        }

        using var operation = BeginOperation();
        try
        {
            SelectedHit = hit;
            SelectedMediaThumbnailPath = hit.MediaEvidence is null || _mediaThumbnailProvider is null
                ? null
                : await _mediaThumbnailProvider
                    .GetThumbnailAsync(hit.FullPath, hit.MediaEvidence, operation.Token)
                    .ConfigureAwait(false);
            PrivacyItem = await _privacyService
                .InspectFileAsync(hit.FileId, operation.Token);
            IsForgetFilePending = false;
            PrivacyText = PrivacyItem is null
                ? "The selected indexed record is no longer available."
                : FormatPrivacyItem(PrivacyItem);
            Status = PrivacyItem is null
                ? StatusPresentation.Warning("The indexed record changed while it was being inspected.")
                : StatusPresentation.Information(
                    "Stored index categories are shown below. Original file contents are not displayed.");
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task ForgetFileAsync()
    {
        if (_privacyService is null || PrivacyItem is null)
        {
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var fileId = PrivacyItem.FileId;
            var result = await _privacyService.ForgetFileAsync(fileId, operation.Token);
            if (result.Applied && _mediaThumbnailProvider is not null)
            {
                await _mediaThumbnailProvider.ClearAsync(operation.Token);
            }

            if (result.Applied && SelectedHit is not null)
            {
                _hits.Remove(SelectedHit);
                OnPropertyChanged(nameof(HasHits));
            }

            SelectedHit = null;
            PrivacyItem = null;
            IsForgetFilePending = false;
            PrivacyText = result.Message;
            Status = result.Applied
                ? StatusPresentation.Success(result.Message)
                : StatusPresentation.Warning(result.Message);
            await RefreshIndexingStatusAsync();
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task ForgetSourceAsync()
    {
        if (_privacyService is null || SelectedSource is null)
        {
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var result = await _privacyService
                .ForgetSourceAsync(SelectedSource.Id, operation.Token);
            if (result.Applied && _mediaThumbnailProvider is not null)
            {
                await _mediaThumbnailProvider.ClearAsync(operation.Token);
            }

            _hits.Clear();
            OnPropertyChanged(nameof(HasHits));
            SelectedHit = null;
            PrivacyItem = null;
            IsForgetSourcePending = false;
            PrivacyText = result.Message;
            Status = result.Applied
                ? StatusPresentation.Success(result.Message)
                : StatusPresentation.Warning(result.Message);
            await RefreshIndexingStatusAsync();
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task RepairFileAsync(IndexRepairKind repair)
    {
        if (_privacyService is null || PrivacyItem is null)
        {
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var result = await _privacyService
                .RepairFileAsync(PrivacyItem.FileId, repair, operation.Token);
            PrivacyText = result.Message;
            Status = result.Applied
                ? StatusPresentation.Progress(result.Message)
                : StatusPresentation.Information(result.Message);
            await RefreshIndexingStatusAsync();
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task RepairSourceAsync(IndexRepairKind repair)
    {
        if (_privacyService is null || SelectedSource is null)
        {
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var result = await _privacyService
                .RepairSourceAsync(SelectedSource.Id, repair, operation.Token);
            Status = result.Applied
                ? StatusPresentation.Progress(result.Message)
                : StatusPresentation.Information(result.Message);
            await RefreshIndexingStatusAsync();
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task ClearSelectedDataAsync(IndexedDataKind data)
    {
        if (_privacyService is null || PrivacyItem is null)
        {
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var result = await _privacyService
                .ClearFileDataAsync(PrivacyItem.FileId, data, operation.Token);
            if (result.Applied && data.HasFlag(IndexedDataKind.MediaDerived) && _mediaThumbnailProvider is not null)
            {
                await _mediaThumbnailProvider.ClearAsync(operation.Token);
                SelectedMediaThumbnailPath = null;
            }

            PrivacyItem = await _privacyService
                .InspectFileAsync(PrivacyItem.FileId, operation.Token);
            PrivacyText = PrivacyItem is null ? result.Message : FormatPrivacyItem(PrivacyItem);
            Status = result.Applied
                ? StatusPresentation.Success(result.Message)
                : StatusPresentation.Warning(result.Message);
            await RefreshIndexingStatusAsync();
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task SetSelectedPolicyAsync(IndexPrivacyPolicyChange change)
    {
        if (_privacyService is null || PrivacyItem is null)
        {
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var fileId = PrivacyItem.FileId;
            var result = await _privacyService
                .SetFilePolicyAsync(fileId, change, operation.Token);
            PrivacyItem = await _privacyService.InspectFileAsync(fileId, operation.Token);
            PrivacyText = PrivacyItem is null ? result.Message : FormatPrivacyItem(PrivacyItem);
            Status = result.Applied
                ? StatusPresentation.Success(result.Message)
                : StatusPresentation.Warning(result.Message);
            await RefreshIndexingStatusAsync();
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private static string FormatPrivacyItem(IndexPrivacyItem item) =>
        $"Indexed data for {item.FileName}: source {item.SourceName} " +
        $"({(item.ManagedByWatchedFolders ? "watched-folder managed" : "manually managed")}); " +
        $"provider {item.ProviderName}; level {item.IndexingLevel}; processing version {item.ProcessorVersion}; " +
        $"metadata about {FormatBytes(item.MetadataBytes)}; extracted text {item.ExtractedTextCharacters:N0} characters; " +
        $"OCR text {item.OcrTextCharacters:N0} characters; summary {(item.HasSummary ? "stored" : "not stored")}; " +
        $"keywords {item.KeywordCount:N0}; related-concept data {(item.HasSemanticData ? "stored" : "not stored")}; " +
        $"selected chunks {item.ChunkCount:N0}; identical-content references {item.SharedContentReferenceCount:N0}; " +
        $"media evidence {(item.HasMediaDerivedData ? $"stored ({item.MediaKind}; transcript {(item.HasMediaTranscript ? "yes" : "no")}; media OCR {(item.HasMediaOcr ? "yes" : "no")}; visual description {(item.HasVisualDescription ? "yes" : "no")})" : "not stored")}; " +
        $"Content Intelligence {(item.HasContentIntelligence ? $"stored ({item.ContentTopicCount:N0} topics; {item.ContentEntityCount:N0} textual entities)" : "not stored")}; " +
        $"Smart Tags {item.SmartTagCount:N0} active assignment(s); " +
        $"relationships {item.RelationshipCount:N0}; collection memberships {item.CollectionCount:N0}; " +
        $"failures {item.FailureCount:N0}; stage-history records {item.StageHistoryCount:N0}; " +
        $"policy: {(item.IsExcluded ? "excluded" : "included")}, OCR {(item.OcrSuppressed ? "off" : "allowed")}, " +
        $"summaries {(item.SummarySuppressed ? "off" : "allowed")}, related concepts {(item.SemanticSuppressed ? "off" : "allowed")}, " +
        $"relationships {(item.RelationshipAnalysisSuppressed ? "off" : "allowed")}; " +
        $"last indexed {item.LastIndexedUtc.LocalDateTime:g}. Related-concept data is described by presence only; raw numeric vectors are never shown.";

    private CancellationTokenSource BeginOperation()
    {
        Cancel();
        var operation = new CancellationTokenSource();
        _operationCancellation = operation;
        IsBusy = true;
        return operation;
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(_operationCancellation, operation))
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void Cancel() => _operationCancellation?.Cancel();

    private void NotifyCommands()
    {
        SearchCommand.NotifyCanExecuteChanged();
        BuildIndexCommand.NotifyCanExecuteChanged();
        RebuildIndexCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RequestClearIndexCommand.NotifyCanExecuteChanged();
        ConfirmClearIndexCommand.NotifyCanExecuteChanged();
        CancelClearIndexCommand.NotifyCanExecuteChanged();
        OpenFileCommand.NotifyCanExecuteChanged();
        OpenContainingFolderCommand.NotifyCanExecuteChanged();
        CopyFullPathCommand.NotifyCanExecuteChanged();
        RemoveFilterCommand.NotifyCanExecuteChanged();
        ClearFiltersCommand.NotifyCanExecuteChanged();
        OpenInFilesCommand.NotifyCanExecuteChanged();
        InspectIndexedDataCommand.NotifyCanExecuteChanged();
        ToggleFacetCommand.NotifyCanExecuteChanged();
        ShowModerateSuggestionsCommand.NotifyCanExecuteChanged();
        SaveViewCommand.NotifyCanExecuteChanged();
        OpenSavedViewCommand.NotifyCanExecuteChanged();
        UpdateSavedViewCommand.NotifyCanExecuteChanged();
        DeleteSavedViewCommand.NotifyCanExecuteChanged();
        RebuildBackgroundIndexCommand.NotifyCanExecuteChanged();
        MaintainIndexCommand.NotifyCanExecuteChanged();
        NotifyPrivacyCommands();
        NotifyBackgroundCommands();
    }

    private void NotifyPrivacyCommands()
    {
        RequestForgetFileCommand.NotifyCanExecuteChanged();
        ConfirmForgetFileCommand.NotifyCanExecuteChanged();
        CancelForgetFileCommand.NotifyCanExecuteChanged();
        RequestForgetSourceCommand.NotifyCanExecuteChanged();
        ConfirmForgetSourceCommand.NotifyCanExecuteChanged();
        CancelForgetSourceCommand.NotifyCanExecuteChanged();
        ReindexFileCommand.NotifyCanExecuteChanged();
        VerifyFileCommand.NotifyCanExecuteChanged();
        RetryFileCommand.NotifyCanExecuteChanged();
        RefreshMetadataCommand.NotifyCanExecuteChanged();
        RefreshTextCommand.NotifyCanExecuteChanged();
        RefreshOcrCommand.NotifyCanExecuteChanged();
        RegenerateSummaryCommand.NotifyCanExecuteChanged();
        RegenerateSemanticCommand.NotifyCanExecuteChanged();
        RebuildSelectedSourceCommand.NotifyCanExecuteChanged();
        ClearOcrDataCommand.NotifyCanExecuteChanged();
        ClearMediaDataCommand.NotifyCanExecuteChanged();
        ClearContentIntelligenceCommand.NotifyCanExecuteChanged();
        ClearSemanticDataCommand.NotifyCanExecuteChanged();
        UseMetadataOnlyCommand.NotifyCanExecuteChanged();
        ExcludeFileCommand.NotifyCanExecuteChanged();
    }

    private bool CanPauseIndexing() =>
        _backgroundIndexingService is not null &&
        BackgroundProgress.Status == IndexingRunStatus.Running;

    private bool CanResumeIndexing() =>
        _backgroundIndexingService is not null &&
        BackgroundProgress.Status is IndexingRunStatus.Paused or IndexingRunStatus.Waiting;

    private bool CanCancelIndexing() =>
        _backgroundIndexingService is not null &&
        BackgroundProgress.Status is IndexingRunStatus.Running or IndexingRunStatus.Paused or IndexingRunStatus.Waiting;

    private bool CanRetryFailedItems() =>
        _backgroundIndexingService is not null &&
        BackgroundProgress.Failed + BackgroundProgress.Waiting + BackgroundProgress.RetryScheduled > 0;

    private async Task RefreshIndexingStatusAsync()
    {
        if (_backgroundIndexingService is null)
        {
            return;
        }

        try
        {
            var progress = await _backgroundIndexingService.GetProgressAsync(CancellationToken.None);
            var sources = await _backgroundIndexingService.GetSourcesAsync(CancellationToken.None);
            var storage = await _backgroundIndexingService.GetStorageBreakdownAsync(CancellationToken.None);
            var failures = await _backgroundIndexingService.GetFailuresAsync(CancellationToken.None);
            await ApplyOnUiThreadAsync(() =>
            {
                ApplyProgress(progress);
                _sources.Clear();
                foreach (var source in sources)
                {
                    _sources.Add(source);
                }

                if (SelectedSource is not null)
                {
                    SelectedSource = _sources.FirstOrDefault(source =>
                        string.Equals(source.Id, SelectedSource.Id, StringComparison.Ordinal));
                }

                _indexingFailures.Clear();
                foreach (var failure in failures)
                {
                    _indexingFailures.Add(failure);
                }

                OnPropertyChanged(nameof(HasIndexingFailures));
                OnPropertyChanged(nameof(FailureSummaryText));
                StorageBreakdownText =
                    $"Storage breakdown: metadata {FormatBytes(storage.MetadataBytes)}, document text {FormatBytes(storage.ExtractedTextBytes)}, OCR text {FormatBytes(storage.OcrTextBytes)}, media evidence {FormatBytes(storage.MediaDerivedDataBytes)}, Content Intelligence {FormatBytes(storage.ContentIntelligenceBytes)}, Smart Tags {FormatBytes(storage.SmartTagBytes)}, summaries and keywords {FormatBytes(storage.SummariesAndKeywordsBytes)}, related-concept data {FormatBytes(storage.SemanticDataBytes)}, relationships {FormatBytes(storage.RelationshipDataBytes)}, job history {FormatBytes(storage.JobHistoryBytes)}, diagnostics {FormatBytes(storage.DiagnosticsBytes)}.";
            });
        }
        catch (Exception)
        {
            await ApplyOnUiThreadAsync(() =>
                Status = StatusPresentation.Warning("Background indexing status could not be refreshed. Existing Search remains available."));
        }
    }

    private async Task PauseIndexingAsync()
    {
        if (_backgroundIndexingService is null)
        {
            return;
        }

        await _backgroundIndexingService.PauseAsync(CancellationToken.None);
        await RefreshIndexingStatusAsync();
    }

    private async Task ResumeIndexingAsync()
    {
        if (_backgroundIndexingService is null)
        {
            return;
        }

        await _backgroundIndexingService.ResumeAsync(CancellationToken.None);
        await RefreshIndexingStatusAsync();
    }

    private async Task CancelIndexingAsync()
    {
        if (_backgroundIndexingService is null)
        {
            return;
        }

        await _backgroundIndexingService.CancelAsync("Cancelled by the user from Search.", CancellationToken.None);
        await RefreshIndexingStatusAsync();
    }

    private async Task RetryFailedItemsAsync()
    {
        if (_backgroundIndexingService is null)
        {
            return;
        }

        var count = await _backgroundIndexingService.RetryFailedAsync(CancellationToken.None);
        Status = count > 0
            ? StatusPresentation.Progress($"{count:N0} indexing item(s) were queued for another attempt.")
            : StatusPresentation.Information("No eligible indexing items were available to retry.");
        await RefreshIndexingStatusAsync();
    }

    private async Task PrioritizeSourceAsync()
    {
        if (_backgroundIndexingService is null || SelectedSource is null)
        {
            return;
        }

        await _backgroundIndexingService.PrioritizeSourceAsync(SelectedSource.Id, CancellationToken.None);
        Status = StatusPresentation.Success($"{SelectedSource.DisplayName} is now prioritized.");
        await RefreshIndexingStatusAsync();
    }

    private async Task RemoveSourceAsync()
    {
        if (_backgroundIndexingService is null || SelectedSource is null)
        {
            return;
        }

        var displayName = SelectedSource.DisplayName;
        await _backgroundIndexingService.RemoveSourceAsync(SelectedSource.Id, CancellationToken.None);
        SelectedSource = null;
        Status = StatusPresentation.Success($"{displayName} was removed from background indexing. Source files were not changed.");
        await RefreshIndexingStatusAsync();
    }

    private async Task RebuildBackgroundIndexAsync()
    {
        if (_backgroundIndexingService is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _backgroundIndexingService.RebuildAsync(CancellationToken.None);
            Status = StatusPresentation.Progress("The durable Search index is rebuilding in the background.");
            await RefreshIndexingStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MaintainIndexAsync()
    {
        if (_backgroundIndexingService is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _backgroundIndexingService.MaintainAsync(CancellationToken.None);
            Status = result.IsWithinQuota
                ? StatusPresentation.Success($"Index maintenance completed with {result.Actions.Count:N0} cleanup action(s).")
                : StatusPresentation.Warning("The configured index storage limit is still reached. Further indexing is paused until the limit or policy changes.");
            await RefreshIndexingStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnBackgroundProgressChanged(object? sender, IndexingProgressSnapshot snapshot) =>
        ApplyOnUiThread(() => ApplyProgress(snapshot));

    private void ApplyProgress(IndexingProgressSnapshot snapshot)
    {
        BackgroundProgress = snapshot;
        StorageText = $"Index storage: {FormatBytes(snapshot.IndexSizeBytes)} of {FormatBytes(snapshot.MaximumIndexSizeBytes)}";
        var coverage = snapshot.Coverage;
        if (!coverage.IsAvailable)
        {
            CoverageText = "The deep Search index is temporarily unavailable. Existing filename and metadata Search remains available.";
            return;
        }

        var limitations = new List<string>();
        if (coverage.IsIncomplete)
        {
            limitations.Add("background indexing is incomplete");
        }

        if (coverage.ExcludedSourceCount > 0)
        {
            limitations.Add($"{coverage.ExcludedSourceCount:N0} source or file exclusion(s) affect coverage");
        }

        if (coverage.WaitingForOcrCount > 0)
        {
            limitations.Add($"{coverage.WaitingForOcrCount:N0} OCR stage(s) are waiting");
        }

        if (coverage.WaitingForAiCount > 0)
        {
            limitations.Add($"{coverage.WaitingForAiCount:N0} optional local-AI stage(s) are waiting");
        }

        if (coverage.FailedStageCount > 0)
        {
            limitations.Add($"{coverage.FailedStageCount:N0} stage(s) failed");
        }

        var phase = snapshot.Phase == IndexingProgressPhase.DeeperAnalysis
            ? " Names, paths, and metadata are searchable now; enabled deeper analysis is still arriving."
            : snapshot.Phase is IndexingProgressPhase.DiscoveringFiles or IndexingProgressPhase.BuildingBaseSearchCoverage
                ? " Base Search coverage is being published before expensive intelligence."
                : string.Empty;
        CoverageText =
            $"Search coverage: names and metadata {coverage.FilenameAndMetadataCount:N0}/{coverage.KnownFileCount:N0}, document text {coverage.ExtractedTextCount:N0}/{coverage.KnownFileCount:N0}, OCR {coverage.OcrCount:N0}/{coverage.KnownFileCount:N0}, related concepts {coverage.SemanticCount:N0}/{coverage.KnownFileCount:N0}, fully indexed {coverage.FullyIndexedCount:N0}/{coverage.KnownFileCount:N0}." +
            phase +
            (limitations.Count > 0
                ? $" Search coverage is still being built. Some files may not appear yet because {string.Join(", ", limitations)}."
                : " All known files have complete indexing coverage.");
    }

    private void NotifyBackgroundCommands()
    {
        PauseIndexingCommand.NotifyCanExecuteChanged();
        ResumeIndexingCommand.NotifyCanExecuteChanged();
        CancelIndexingCommand.NotifyCanExecuteChanged();
        RetryFailedItemsCommand.NotifyCanExecuteChanged();
        PrioritizeSourceCommand.NotifyCanExecuteChanged();
        RemoveSourceCommand.NotifyCanExecuteChanged();
    }

    private static void ApplyOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current is null)
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static Task ApplyOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current is null)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private static string FormatRunStatus(IndexingRunStatus status) => status switch
    {
        IndexingRunStatus.Pending => "preparing",
        IndexingRunStatus.Running => "processing",
        IndexingRunStatus.Paused => "paused",
        IndexingRunStatus.Waiting => "waiting",
        IndexingRunStatus.Cancelling => "cancelling safely",
        IndexingRunStatus.Cancelled => "cancelled",
        IndexingRunStatus.Complete => "complete",
        IndexingRunStatus.CompleteWithFailures => "complete with failures",
        IndexingRunStatus.Failed => "failed",
        _ => status.ToString(),
    };

    private static string FormatStage(IndexingStage stage) => stage switch
    {
        IndexingStage.FileDiscovered => "file discovered",
        IndexingStage.MetadataIndexed => "metadata",
        IndexingStage.ContentFingerprinted => "content fingerprint",
        IndexingStage.TextExtracted => "document text",
        IndexingStage.OcrProcessed => "OCR",
        IndexingStage.SummaryKeywordsGenerated => "summaries and keywords",
        IndexingStage.SmartTagsClassified => "Smart Tag classification",
        IndexingStage.SemanticRepresentationGenerated => "related concepts",
        IndexingStage.SearchIndexUpdated => "Search index update",
        IndexingStage.RelationshipAnalysisCompleted => "file relationships",
        IndexingStage.FileFullyIndexed => "finalization",
        _ => stage.ToString(),
    };

    private static string FormatGraphCoverage(GraphProjectionCoverage? coverage, bool enabled)
    {
        if (!enabled)
        {
            return "Knowledge Graph context is disabled for this Search.";
        }

        if (coverage is null || !coverage.IsAvailable)
        {
            return "Knowledge Graph context is temporarily unavailable. Ordinary Search remains available.";
        }

        var state = coverage.IsComplete && !coverage.IsStale
            ? "current"
            : "partial";
        return $"Knowledge Graph coverage is {state}: {coverage.ProjectedObservationCount:N0} of {coverage.TotalObservationCount:N0} eligible observations projected. Graph context may be incomplete while projection continues.";
    }

    private static string FormatBytes(long value)
    {
        var bytes = Math.Max(0, value);
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var unit = 0;
        var display = (double)bytes;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return $"{display:N1} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} hr {duration.Minutes} min"
            : duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes} min"
                : $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))} sec";

    private static StatusPresentation Present(SemanticState state, string message) => state switch
    {
        SemanticState.Ready => StatusPresentation.Success(message),
        SemanticState.Indexing => StatusPresentation.Progress(message),
        SemanticState.Disabled or SemanticState.Empty or SemanticState.Cancelled => StatusPresentation.Warning(message),
        SemanticState.Failed => StatusPresentation.Error(message),
        _ => StatusPresentation.Information(message),
    };

    /// <inheritdoc />
    public void Dispose()
    {
        var operation = Interlocked.Exchange(ref _operationCancellation, null);
        operation?.Cancel();
        operation?.Dispose();
        if (_backgroundIndexingService is not null)
        {
            _backgroundIndexingService.ProgressChanged -= OnBackgroundProgressChanged;
        }
    }

    private sealed class PreviewConfiguration : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new();
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }
}
