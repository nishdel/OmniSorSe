using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
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
    private readonly IIndexPrivacyService? _privacyService;
    private readonly IAdvancedDiagnosticsWindowService? _advancedDiagnosticsWindowService;
    private readonly ObservableCollection<SemanticSearchHit> _hits = [];
    private readonly ObservableCollection<SearchFilter> _activeFilters = [];
    private readonly ObservableCollection<IndexingSource> _sources = [];
    private readonly ObservableCollection<IndexingFailure> _indexingFailures = [];
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
    private IndexPrivacyItem? _privacyItem;
    private string _privacyText = "Select Inspect indexed data on a Search result to review retained categories.";
    private bool _isForgetFilePending;
    private bool _isForgetSourcePending;
    private bool _filtersWereEdited;
    private long _queryVersion;

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
        IIndexPrivacyService? privacyService = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _indexer = indexer;
        _searchService = searchService;
        _indexStore = indexStore;
        _launcher = launcher;
        _backgroundIndexingService = backgroundIndexingService;
        _privacyService = privacyService ?? backgroundIndexingService as IIndexPrivacyService;
        _advancedDiagnosticsWindowService = advancedDiagnosticsWindowService;
        Hits = new ReadOnlyObservableCollection<SemanticSearchHit>(_hits);
        ActiveFilters = new ReadOnlyObservableCollection<SearchFilter>(_activeFilters);
        Sources = new ReadOnlyObservableCollection<IndexingSource>(_sources);
        IndexingFailures = new ReadOnlyObservableCollection<IndexingFailure>(_indexingFailures);
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
        ToggleFiltersCommand = new RelayCommand(() => AreFiltersVisible = !AreFiltersVisible);
        RemoveFilterCommand = new AsyncRelayCommand<SearchFilter>(RemoveFilterAsync, filter => filter is not null && !IsBusy);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync, () => _activeFilters.Count > 0 && !IsBusy);
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
                _activeFilters.Clear();
                _topicText = string.Empty;
                _filtersWereEdited = false;
                OnPropertyChanged(nameof(HasActiveFilters));
                ClearFiltersCommand.NotifyCanExecuteChanged();
                SearchCommand.NotifyCanExecuteChanged();
                ClearQueryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets local explained results.</summary>
    public ReadOnlyObservableCollection<SemanticSearchHit> Hits { get; }

    /// <summary>Gets visible interpreted filters applied to the current Search.</summary>
    public ReadOnlyObservableCollection<SearchFilter> ActiveFilters { get; }

    /// <summary>Gets whether any visible interpreted filter is active.</summary>
    public bool HasActiveFilters => ActiveFilters.Count > 0;

    /// <summary>Gets or sets whether the contextual filter area is expanded.</summary>
    public bool AreFiltersVisible
    {
        get => _areFiltersVisible;
        set => SetProperty(ref _areFiltersVisible, value);
    }

    /// <summary>Gets the selected Search result for privacy inspection.</summary>
    public SemanticSearchHit? SelectedHit
    {
        get => _selectedHit;
        private set
        {
            if (SetProperty(ref _selectedHit, value))
            {
                NotifyPrivacyCommands();
            }
        }
    }

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
    public double BackgroundProgressValue => BackgroundProgress.OverallPercentage / 100d;

    /// <summary>Gets the durable run-state label.</summary>
    public string BackgroundStateText => $"Indexing state: {FormatRunStatus(BackgroundProgress.Status)}";

    /// <summary>Gets the current durable stage label.</summary>
    public string CurrentStageText => BackgroundProgress.CurrentStage is { } stage
        ? $"Stage: {FormatStage(stage)}"
        : "Stage: no active stage";

    /// <summary>Gets the display-safe current file label.</summary>
    public string CurrentFileText => string.IsNullOrWhiteSpace(BackgroundProgress.CurrentFile)
        ? "Current file: none"
        : $"Current file: {BackgroundProgress.CurrentFile}";

    /// <summary>Gets processed and discovered counts.</summary>
    public string ProcessedCountText =>
        $"Processed {BackgroundProgress.Processed:N0} of {BackgroundProgress.TotalDiscovered:N0} discovered files";

    /// <summary>Gets the remaining-file count.</summary>
    public string RemainingCountText => $"{BackgroundProgress.Remaining:N0} files remaining";

    /// <summary>Gets completed, skipped, failed, waiting, and retry counts.</summary>
    public string OutcomeCountText =>
        $"Completed {BackgroundProgress.Completed:N0} · Skipped {BackgroundProgress.Skipped:N0} · Failed {BackgroundProgress.Failed:N0} · Waiting {BackgroundProgress.Waiting:N0} · Retrying {BackgroundProgress.RetryScheduled:N0}";

    /// <summary>Gets recent throughput.</summary>
    public string ThroughputText => BackgroundProgress.FilesPerSecond > 0
        ? $"{BackgroundProgress.FilesPerSecond:N2} files/second"
        : "Processing speed will appear after work starts.";

    /// <summary>Gets whether an estimate has enough samples to be meaningful.</summary>
    public bool HasEstimatedTime => BackgroundProgress.EstimatedRemaining.HasValue;

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

    /// <summary>Gets the command that expands or collapses contextual filters.</summary>
    public IRelayCommand ToggleFiltersCommand { get; }

    /// <summary>Gets the command that removes one visible interpreted filter.</summary>
    public IAsyncRelayCommand<SearchFilter> RemoveFilterCommand { get; }

    /// <summary>Gets the command that removes every visible filter while retaining topic terms.</summary>
    public IAsyncRelayCommand ClearFiltersCommand { get; }

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

    /// <summary>Refreshes command availability after persisted feature settings change.</summary>
    public void RefreshFeatureAvailability()
    {
        NotifyCommands();
        _ = RefreshIndexingStatusAsync();
    }

    /// <summary>Refreshes persistent indexing progress and configured sources.</summary>
    public Task RefreshAsync() => RefreshIndexingStatusAsync();

    private bool CanSearch() =>
        _searchService is not null &&
        _configurationService.Current.SemanticSearch.Enabled &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(QueryText);

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
            var request = _filtersWereEdited
                ? new SearchRequest(
                    QueryText ?? string.Empty,
                    _activeFilters.ToArray(),
                    InterpretFilters: false,
                    TopicTextOverride: _topicText)
                : new SearchRequest(QueryText ?? string.Empty);
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
        if (_searchService is null || string.IsNullOrWhiteSpace(QueryText))
        {
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var result = await _searchService.SearchAsync(
                new SearchRequest(
                    QueryText,
                    [],
                    InterpretFilters: false,
                    TopicTextOverride: _topicText),
                operation.Token);
            _hits.Clear();
            foreach (var hit in result.Hits)
            {
                _hits.Add(hit);
            }

            OnPropertyChanged(nameof(HasHits));
            Status = Present(result.State, result.Message);
        }
        finally
        {
            EndOperation(operation);
        }
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

    private Task OpenFileAsync(SemanticSearchHit? hit) => OpenAsync(hit, false);

    private Task OpenFolderAsync(SemanticSearchHit? hit) => OpenAsync(hit, true);

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
        $"failures {item.FailureCount:N0}; stage-history records {item.StageHistoryCount:N0}; " +
        $"policy: {(item.IsExcluded ? "excluded" : "included")}, OCR {(item.OcrSuppressed ? "off" : "allowed")}, " +
        $"summaries {(item.SummarySuppressed ? "off" : "allowed")}, related concepts {(item.SemanticSuppressed ? "off" : "allowed")}; " +
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
        RemoveFilterCommand.NotifyCanExecuteChanged();
        ClearFiltersCommand.NotifyCanExecuteChanged();
        InspectIndexedDataCommand.NotifyCanExecuteChanged();
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
                    $"Storage breakdown: metadata {FormatBytes(storage.MetadataBytes)}, document text {FormatBytes(storage.ExtractedTextBytes)}, OCR text {FormatBytes(storage.OcrTextBytes)}, summaries and keywords {FormatBytes(storage.SummariesAndKeywordsBytes)}, related-concept data {FormatBytes(storage.SemanticDataBytes)}, relationships {FormatBytes(storage.RelationshipDataBytes)}, job history {FormatBytes(storage.JobHistoryBytes)}, diagnostics {FormatBytes(storage.DiagnosticsBytes)}.";
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

        CoverageText =
            $"Search coverage: names and metadata {coverage.FilenameAndMetadataCount:N0}/{coverage.KnownFileCount:N0}, document text {coverage.ExtractedTextCount:N0}/{coverage.KnownFileCount:N0}, OCR {coverage.OcrCount:N0}/{coverage.KnownFileCount:N0}, related concepts {coverage.SemanticCount:N0}/{coverage.KnownFileCount:N0}, fully indexed {coverage.FullyIndexedCount:N0}/{coverage.KnownFileCount:N0}." +
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
        IndexingStage.SemanticRepresentationGenerated => "related concepts",
        IndexingStage.SearchIndexUpdated => "Search index update",
        IndexingStage.RelationshipAnalysisCompleted => "file relationships",
        IndexingStage.FileFullyIndexed => "finalization",
        _ => stage.ToString(),
    };

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
