using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Tags;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Owns read-only exploration state for one immutable completed-results snapshot.
/// </summary>
public sealed class ResultsViewModel : ViewModelBase, IDisposable
{
    /// <summary>Gets the minimum usable file-table width in device-independent pixels.</summary>
    public const double MinimumFileTableWidth = 450;

    /// <summary>Gets the minimum usable selected-file details width in device-independent pixels.</summary>
    public const double MinimumDetailsPanelWidth = 320;

    private static readonly IReadOnlyList<int> PageSizes = [50, 100, 200, 500];
    private static readonly IReadOnlyList<ResultDuplicateFilter> DuplicateFilters = Enum.GetValues<ResultDuplicateFilter>();
    private static readonly IReadOnlyList<ResultPlannedOperationFilter> PlannedOperationFilters = Enum.GetValues<ResultPlannedOperationFilter>();
    private static readonly IReadOnlyList<ResultsSortField> SortFields = Enum.GetValues<ResultsSortField>();
    private static readonly IReadOnlyList<SortDirection> SortDirections = Enum.GetValues<SortDirection>();
    private readonly ObservableCollection<ResultsFileRow> _pageRows = [];
    private readonly ObservableCollection<ResultDirectory> _directories = [];
    private readonly ObservableCollection<ResultPlannedOperation> _plannedOperations = [];
    private readonly ObservableCollection<string> _warnings = [];
    private readonly ObservableCollection<ResultsExtensionFilterOption> _extensionOptions = [];
    private readonly ObservableCollection<ResultsCategoryFilterOption> _categoryOptions = [];
    private readonly ObservableCollection<ResultTagRow> _userTags = [];
    private readonly ObservableCollection<ExtractedMetadataField> _contentMetadata = [];
    private readonly IContentStore? _contentStore;
    private readonly IConfigurationService _configurationService;
    private readonly SemaphoreSlim _panelPreferenceSaveGate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<TagAssociation>> _tagsByFile = new(StringComparer.Ordinal);
    private CancellationTokenSource? _queryCancellation;
    private CancellationTokenSource? _contentDetailsCancellation;
    private long _queryVersion;
    private ResultsSnapshot? _snapshot;
    private ResultsQuery _query = ResultsQuery.Default;
    private ResultsPage _page = ResultsPage.Empty;
    private ResultsFileRow? _selectedRow;
    private SelectedResultDetails? _selectedDetails;
    private ResultsSummary _summary = ResultsSummary.Empty;
    private string _statusText = "No completed scan results are available.";
    private string? _userTagText;
    private string _userTagStatusText = "Select a result file to manage application-owned tags.";
    private ResultTagRow? _selectedUserTag;
    private bool _isLoading;
    private ResultsDisplayMode _displayMode = ResultsDisplayMode.Explorer;
    private string _contentDetailsStatus = "Select a result to inspect local extracted metadata.";
    private bool _areFiltersVisible;
    private double _detailsPanelWidthRatio;

    /// <summary>
    /// Initializes the result explorer and its non-mutating navigation commands.
    /// </summary>
    public ResultsViewModel()
        : this(new PreviewConfigurationService(), null, null, null)
    {
    }

    /// <summary>
    /// Initializes the result explorer with optional application-owned AI suggestion review.
    /// </summary>
    /// <param name="configurationService">The centralized configuration source used only by the optional suggestion workflow.</param>
    /// <param name="aiSuggestionService">The optional application-owned suggestion service.</param>
    public ResultsViewModel(IConfigurationService configurationService, IAiSuggestionService? aiSuggestionService)
        : this(configurationService, aiSuggestionService, null, null)
    {
    }

    /// <summary>
    /// Initializes Results with optional AI suggestions and controlled Duplicate View shell-open support.
    /// </summary>
    public ResultsViewModel(
        IConfigurationService configurationService,
        IAiSuggestionService? aiSuggestionService,
        IExternalFileLauncher? externalFileLauncher,
        IContentStore? contentStore = null)
    {
        ArgumentNullException.ThrowIfNull(configurationService);
        _configurationService = configurationService;
        _detailsPanelWidthRatio = configurationService.Current.Features.FilesPageDetailsPanelWidthRatio;
        PageRows = new ReadOnlyObservableCollection<ResultsFileRow>(_pageRows);
        Directories = new ReadOnlyObservableCollection<ResultDirectory>(_directories);
        PlannedOperations = new ReadOnlyObservableCollection<ResultPlannedOperation>(_plannedOperations);
        Warnings = new ReadOnlyObservableCollection<string>(_warnings);
        ExtensionOptions = new ReadOnlyObservableCollection<ResultsExtensionFilterOption>(_extensionOptions);
        CategoryOptions = new ReadOnlyObservableCollection<ResultsCategoryFilterOption>(_categoryOptions);
        UserTags = new ReadOnlyObservableCollection<ResultTagRow>(_userTags);
        ContentMetadata = new ReadOnlyObservableCollection<ExtractedMetadataField>(_contentMetadata);
        _contentStore = contentStore;
        DuplicateReview = new DuplicateReviewViewModel(externalFileLauncher);
        DuplicateReview.ShowGroupFilesRequested += OnShowGroupFilesRequested;
        DuplicateReview.BackToExplorerRequested += OnBackToExplorerRequested;
        AiSuggestions = new AiSuggestionsViewModel(configurationService, aiSuggestionService, contentStore);
        ClearFiltersCommand = new RelayCommand(ClearFilters, CanClearFilters);
        ToggleFiltersCommand = new RelayCommand(() => AreFiltersVisible = !AreFiltersVisible);
        PreviousPageCommand = new RelayCommand(GoToPreviousPage, () => CanGoPreviousPage);
        NextPageCommand = new RelayCommand(GoToNextPage, () => CanGoNextPage);
        OpenDuplicateReviewCommand = new RelayCommand(OpenDuplicateReview, () => CanOpenDuplicateReview);
        BackToExplorerCommand = new RelayCommand(BackToExplorer);
        OpenMeaningSearchCommand = new RelayCommand(
            () => MeaningSearchRequested?.Invoke(this, EventArgs.Empty),
            () => IsMeaningSearchEnabled);
        AddUserTagsCommand = new AsyncRelayCommand(AddUserTagsAsync, CanAddUserTags);
        RemoveSelectedTagCommand = new AsyncRelayCommand(RemoveSelectedTagAsync, CanRemoveSelectedTag);
        AcceptSuggestedTagCommand = new AsyncRelayCommand(
            AcceptSuggestedTagAsync,
            () => SelectedUserTag?.CanAccept == true);
        RejectSuggestedTagCommand = new AsyncRelayCommand(
            RejectSuggestedTagAsync,
            () => SelectedUserTag?.CanReject == true);
        NarrowDetailsPanelCommand = new AsyncRelayCommand(() => AdjustDetailsPanelWidthAsync(-0.05));
        WidenDetailsPanelCommand = new AsyncRelayCommand(() => AdjustDetailsPanelWidthAsync(0.05));
        ResetDetailsPanelWidthCommand = new AsyncRelayCommand(
            () => SetDetailsPanelWidthRatioAsync(FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio));
    }

    /// <summary>
    /// Raised after accepted non-deterministic tags change for the loaded snapshot.
    /// </summary>
    public event EventHandler? PersistedTagsChanged;

    /// <summary>Occurs when the user switches from name search to local Meaning Search.</summary>
    public event EventHandler? MeaningSearchRequested;

    /// <summary>Gets the immutable snapshot currently owned by this review surface.</summary>
    public ResultsSnapshot? Snapshot => _snapshot;

    /// <summary>Gets bounded, display-safe file rows for the active page.</summary>
    public ReadOnlyObservableCollection<ResultsFileRow> PageRows { get; }

    /// <summary>Gets display-safe directories from the current snapshot.</summary>
    public ReadOnlyObservableCollection<ResultDirectory> Directories { get; }

    /// <summary>Gets display-only accepted planned operations from the current snapshot.</summary>
    public ReadOnlyObservableCollection<ResultPlannedOperation> PlannedOperations { get; }

    /// <summary>Gets user-safe issues from the current snapshot.</summary>
    public ReadOnlyObservableCollection<string> Warnings { get; }

    /// <summary>Gets supported extension filter choices.</summary>
    public ReadOnlyObservableCollection<ResultsExtensionFilterOption> ExtensionOptions { get; }

    /// <summary>Gets supported deterministic-category filter choices.</summary>
    public ReadOnlyObservableCollection<ResultsCategoryFilterOption> CategoryOptions { get; }

    /// <summary>Gets accepted application-owned tags for the selected result file.</summary>
    public ReadOnlyObservableCollection<ResultTagRow> UserTags { get; }

    /// <summary>Gets provenance-aware local metadata for the selected known result.</summary>
    public ReadOnlyObservableCollection<ExtractedMetadataField> ContentMetadata { get; }

    /// <summary>Gets a user-safe local content-details status.</summary>
    public string ContentDetailsStatus
    {
        get => _contentDetailsStatus;
        private set => SetProperty(ref _contentDetailsStatus, value);
    }

    /// <summary>Gets whether extracted metadata is available for the selected result.</summary>
    public bool HasContentMetadata => ContentMetadata.Count > 0;

    /// <summary>Gets or sets whether secondary filters are progressively disclosed.</summary>
    public bool AreFiltersVisible
    {
        get => _areFiltersVisible;
        set => SetProperty(ref _areFiltersVisible, value);
    }

    /// <summary>Gets fixed page-size choices that keep result rendering bounded.</summary>
    public IReadOnlyList<int> AvailablePageSizes => PageSizes;

    /// <summary>Gets duplicate-state filter choices.</summary>
    public IReadOnlyList<ResultDuplicateFilter> AvailableDuplicateFilters => DuplicateFilters;

    /// <summary>Gets planned-operation filter choices.</summary>
    public IReadOnlyList<ResultPlannedOperationFilter> AvailablePlannedOperationFilters => PlannedOperationFilters;

    /// <summary>Gets sort-field choices.</summary>
    public IReadOnlyList<ResultsSortField> AvailableSortFields => SortFields;

    /// <summary>Gets sort-direction choices.</summary>
    public IReadOnlyList<SortDirection> AvailableSortDirections => SortDirections;

    /// <summary>Gets the child view model for exact-duplicate review.</summary>
    public DuplicateReviewViewModel DuplicateReview { get; }

    /// <summary>Gets the preview-only optional AI suggestion workflow for the selected result.</summary>
    public AiSuggestionsViewModel AiSuggestions { get; }

    /// <summary>Refreshes feature presentation after active settings are saved.</summary>
    public void RefreshFeatureAvailability()
    {
        AiSuggestions.RefreshFeatureAvailability();
        DetailsPanelWidthRatio = _configurationService.Current.Features.FilesPageDetailsPanelWidthRatio;
        OnPropertyChanged(nameof(IsMeaningSearchEnabled));
        OnPropertyChanged(nameof(MeaningSearchAvailabilityText));
        OpenMeaningSearchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Gets whether local Meaning Search Beta is enabled.</summary>
    public bool IsMeaningSearchEnabled => _configurationService.Current.SemanticSearch.Enabled;

    /// <summary>Gets plain-language Meaning Search availability guidance.</summary>
    public string MeaningSearchAvailabilityText => IsMeaningSearchEnabled
        ? "Meaning Search is ready to open. It finds related ideas using a local index."
        : "Meaning Search is off. Enable it in Settings to search by related ideas.";

    /// <summary>Gets the current normalized explorer query.</summary>
    public ResultsQuery Query
    {
        get => _query;
        private set
        {
            if (SetProperty(ref _query, value))
            {
                OnPropertyChanged(nameof(QueryText));
                OnPropertyChanged(nameof(SelectedDuplicateFilter));
                OnPropertyChanged(nameof(SelectedExtensionOption));
                OnPropertyChanged(nameof(SelectedCategoryOption));
                OnPropertyChanged(nameof(SelectedPlannedOperationFilter));
                OnPropertyChanged(nameof(SelectedSortField));
                OnPropertyChanged(nameof(SelectedSortDirection));
                OnPropertyChanged(nameof(SelectedPageSize));
                OnPropertyChanged(nameof(ActiveQueryDescription));
                ClearFiltersCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the local text query.</summary>
    public string? QueryText
    {
        get => Query.Text;
        set => ChangeQuery(Query with { Text = value, PageIndex = 0 });
    }

    /// <summary>Gets or sets the duplicate-state filter.</summary>
    public ResultDuplicateFilter SelectedDuplicateFilter
    {
        get => Query.DuplicateFilter;
        set => ChangeQuery(Query with { DuplicateFilter = value, PageIndex = 0 });
    }

    /// <summary>Gets or sets the extension filter option.</summary>
    public ResultsExtensionFilterOption? SelectedExtensionOption
    {
        get => ExtensionOptions.FirstOrDefault(option => string.Equals(option.Value, Query.Extension, StringComparison.Ordinal));
        set => ChangeQuery(Query with { Extension = value?.Value, PageIndex = 0 });
    }

    /// <summary>Gets or sets the deterministic-category filter option.</summary>
    public ResultsCategoryFilterOption? SelectedCategoryOption
    {
        get => CategoryOptions.FirstOrDefault(option => option.Value == Query.Category);
        set => ChangeQuery(Query with { Category = value?.Value, PageIndex = 0 });
    }

    /// <summary>Gets or sets the planned-operation filter.</summary>
    public ResultPlannedOperationFilter SelectedPlannedOperationFilter
    {
        get => Query.PlannedOperationFilter;
        set => ChangeQuery(Query with { PlannedOperationFilter = value, PageIndex = 0 });
    }

    /// <summary>Gets or sets the active sort field.</summary>
    public ResultsSortField SelectedSortField
    {
        get => Query.SortField;
        set => ChangeQuery(Query with { SortField = value, PageIndex = 0 });
    }

    /// <summary>Gets or sets the active sort direction.</summary>
    public SortDirection SelectedSortDirection
    {
        get => Query.SortDirection;
        set => ChangeQuery(Query with { SortDirection = value, PageIndex = 0 });
    }

    /// <summary>Gets or sets the active bounded page size.</summary>
    public int SelectedPageSize
    {
        get => Query.PageSize;
        set => ChangeQuery(Query with { PageSize = value, PageIndex = 0 });
    }

    /// <summary>Gets the current bounded page.</summary>
    public ResultsPage Page
    {
        get => _page;
        private set
        {
            if (SetProperty(ref _page, value))
            {
                OnPropertyChanged(nameof(PageRangeText));
                OnPropertyChanged(nameof(CanGoPreviousPage));
                OnPropertyChanged(nameof(CanGoNextPage));
                OnPropertyChanged(nameof(HasFilterResults));
                OnPropertyChanged(nameof(HasNoFilterResults));
                PreviousPageCommand.NotifyCanExecuteChanged();
                NextPageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the selected bounded page row.</summary>
    public ResultsFileRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                UserTagText = null;
                UserTagStatusText = value is null
                    ? "Select a result file to manage application-owned tags."
                    : "Tags are OpenSorSe metadata and never change the selected file.";
                SelectedUserTag = null;
                UpdateSelectedDetails();
                UpdateAiSuggestionContext();
                _ = LoadContentDetailsAsync();
            }
        }
    }

    /// <summary>Gets or sets comma-, semicolon-, or line-separated tags to add to the selected result.</summary>
    public string? UserTagText
    {
        get => _userTagText;
        set
        {
            if (SetProperty(ref _userTagText, value))
            {
                AddUserTagsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets a user-safe status for selected-file tag editing.</summary>
    public string UserTagStatusText
    {
        get => _userTagStatusText;
        private set => SetProperty(ref _userTagStatusText, value);
    }

    /// <summary>Gets or sets the accepted tag selected for explicit removal.</summary>
    public ResultTagRow? SelectedUserTag
    {
        get => _selectedUserTag;
        set
        {
            if (SetProperty(ref _selectedUserTag, value))
            {
                RemoveSelectedTagCommand.NotifyCanExecuteChanged();
                AcceptSuggestedTagCommand.NotifyCanExecuteChanged();
                RejectSuggestedTagCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets details derived only from the selected immutable result row.</summary>
    public SelectedResultDetails? SelectedDetails
    {
        get => _selectedDetails;
        private set
        {
            if (SetProperty(ref _selectedDetails, value))
            {
                OnPropertyChanged(nameof(HasSelectedDetails));
            }
        }
    }

    /// <summary>Gets aggregate snapshot values for the existing results summary.</summary>
    public ResultsSummary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Gets user-safe local-review state text.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets whether local query work is pending.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                AddUserTagsCommand.NotifyCanExecuteChanged();
                RemoveSelectedTagCommand.NotifyCanExecuteChanged();
                AcceptSuggestedTagCommand.NotifyCanExecuteChanged();
                RejectSuggestedTagCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the active child review surface.</summary>
    public ResultsDisplayMode DisplayMode
    {
        get => _displayMode;
        private set
        {
            if (SetProperty(ref _displayMode, value))
            {
                OnPropertyChanged(nameof(IsExplorerVisible));
                OnPropertyChanged(nameof(IsDuplicateReviewVisible));
            }
        }
    }

    /// <summary>Gets whether a completed snapshot is available.</summary>
    public bool HasSnapshot => Snapshot is not null;

    /// <summary>Gets whether no completed snapshot is available.</summary>
    public bool HasNoSnapshot => !HasSnapshot;

    /// <summary>Gets whether the completed snapshot contains files.</summary>
    public bool HasResults => Snapshot?.Files.Count > 0;

    /// <summary>Gets whether active filters match one or more files.</summary>
    public bool HasFilterResults => Page.TotalItemCount > 0;

    /// <summary>Gets whether active filters match no file in an otherwise populated snapshot.</summary>
    public bool HasNoFilterResults => HasSnapshot && HasResults && !HasFilterResults;

    /// <summary>Gets whether current state represents an empty completed scan.</summary>
    public bool IsEmptyCompletedScan => HasSnapshot && !HasResults;

    /// <summary>Gets whether source issues are available.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Gets whether no source issues are available.</summary>
    public bool HasNoWarnings => !HasWarnings;

    /// <summary>Gets whether display-only planned operations are available.</summary>
    public bool HasPlannedOperations => PlannedOperations.Count > 0;

    /// <summary>Gets whether selected result details are available.</summary>
    public bool HasSelectedDetails => SelectedDetails is not null;

    /// <summary>Gets the persisted proportion of the Files workspace assigned to selected-file details.</summary>
    public double DetailsPanelWidthRatio
    {
        get => _detailsPanelWidthRatio;
        private set => SetProperty(ref _detailsPanelWidthRatio, value);
    }

    /// <summary>Gets whether the previous bounded page is available.</summary>
    public bool CanGoPreviousPage => Page.PageIndex > 0;

    /// <summary>Gets whether the next bounded page is available.</summary>
    public bool CanGoNextPage => Page.TotalPageCount > 0 && Page.PageIndex < Page.TotalPageCount - 1;

    /// <summary>Gets whether exact duplicate review can open for the current snapshot.</summary>
    public bool CanOpenDuplicateReview => Snapshot?.IsDuplicateDataAvailable == true;

    /// <summary>Gets whether the explorer child surface is active.</summary>
    public bool IsExplorerVisible => DisplayMode == ResultsDisplayMode.Explorer;

    /// <summary>Gets whether the exact-duplicate child surface is active.</summary>
    public bool IsDuplicateReviewVisible => DisplayMode == ResultsDisplayMode.ExactDuplicates;

    /// <summary>Gets the current range/count text for the bounded file page.</summary>
    public string PageRangeText => Page.TotalItemCount == 0
        ? "No matching files"
        : $"Showing {Page.PageIndex * Page.PageSize + 1}–{Page.PageIndex * Page.PageSize + Page.Items.Count} of {Page.TotalItemCount} files";

    /// <summary>Gets a concise description of active result filters.</summary>
    public string ActiveQueryDescription => !HasActiveFilters()
        ? "All completed-scan results"
        : "Active filters are applied to this completed scan.";

    /// <summary>Gets the command that clears every non-default local filter.</summary>
    public IRelayCommand ClearFiltersCommand { get; }

    /// <summary>Gets the command that opens or closes secondary filters.</summary>
    public IRelayCommand ToggleFiltersCommand { get; }

    /// <summary>Gets the command that opens the previous bounded page.</summary>
    public IRelayCommand PreviousPageCommand { get; }

    /// <summary>Gets the command that opens the next bounded page.</summary>
    public IRelayCommand NextPageCommand { get; }

    /// <summary>Gets the command that opens the exact-duplicate child surface.</summary>
    public IRelayCommand OpenDuplicateReviewCommand { get; }

    /// <summary>Gets the command that returns from duplicate review to the file explorer.</summary>
    public IRelayCommand BackToExplorerCommand { get; }

    /// <summary>Gets the command that opens local Meaning Search Beta.</summary>
    public IRelayCommand OpenMeaningSearchCommand { get; }

    /// <summary>Shows the ordinary Files explorer.</summary>
    public void ShowFiles() => BackToExplorer();

    /// <summary>Shows the existing exact-duplicate review surface.</summary>
    public void ShowDuplicates() => OpenDuplicateReview();

    /// <summary>Gets the command that adds validated application-owned tags to the selected result.</summary>
    public IAsyncRelayCommand AddUserTagsCommand { get; }

    /// <summary>Gets the command that removes the selected non-deterministic application-owned tag.</summary>
    public IAsyncRelayCommand RemoveSelectedTagCommand { get; }

    /// <summary>Gets the command that explicitly accepts one generated candidate tag.</summary>
    public IAsyncRelayCommand AcceptSuggestedTagCommand { get; }

    /// <summary>Gets the command that explicitly rejects one generated candidate tag.</summary>
    public IAsyncRelayCommand RejectSuggestedTagCommand { get; }

    /// <summary>Gets the keyboard-accessible command that narrows selected-file details.</summary>
    public IAsyncRelayCommand NarrowDetailsPanelCommand { get; }

    /// <summary>Gets the keyboard-accessible command that widens selected-file details.</summary>
    public IAsyncRelayCommand WidenDetailsPanelCommand { get; }

    /// <summary>Gets the command that restores the default selected-file details width.</summary>
    public IAsyncRelayCommand ResetDetailsPanelWidthCommand { get; }

    /// <summary>
    /// Validates and persists the selected-file details proportion without changing source files.
    /// </summary>
    /// <param name="ratio">The requested details-panel proportion.</param>
    public async Task SetDetailsPanelWidthRatioAsync(double ratio)
    {
        var boundedRatio = double.IsFinite(ratio)
            ? Math.Clamp(
                ratio,
                FeatureSettings.MinimumFilesPageDetailsPanelWidthRatio,
                FeatureSettings.MaximumFilesPageDetailsPanelWidthRatio)
            : FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio;

        DetailsPanelWidthRatio = boundedRatio;
        await _panelPreferenceSaveGate.WaitAsync();
        try
        {
            var settings = _configurationService.Current.WithFilesPageDetailsPanelWidthRatio(boundedRatio);
            settings.Validate();
            await _configurationService.SaveAsync(settings, CancellationToken.None);
        }
        catch (IOException)
        {
            StatusText = "The Files layout could not be saved. The current layout remains available for this session.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "The Files layout could not be saved. Check access to the OpenSorSe settings folder.";
        }
        finally
        {
            _panelPreferenceSaveGate.Release();
        }
    }

    /// <summary>
    /// Replaces the current in-memory review state with a completed immutable snapshot.
    /// </summary>
    /// <param name="snapshot">The completed snapshot to own until a later completed scan replaces it.</param>
    /// <returns>A task that completes once the initial bounded page has been published.</returns>
    public Task LoadSnapshotAsync(ResultsSnapshot snapshot) => LoadSnapshotAsync(snapshot, null);

    /// <summary>
    /// Replaces the current review state with a completed immutable snapshot and accepted tags restored from application-owned storage.
    /// </summary>
    /// <param name="snapshot">The completed snapshot to own until a later completed scan replaces it.</param>
    /// <param name="persistedTags">Accepted non-deterministic tags previously stored for this exact snapshot.</param>
    /// <returns>A task that completes once the initial bounded page has been published.</returns>
    public Task LoadSnapshotAsync(ResultsSnapshot snapshot, IReadOnlyList<TagAssociation>? persistedTags)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        Summary = new ResultsSummary(
            snapshot.Statistics.FilesDiscovered,
            snapshot.Statistics.DirectoriesDiscovered,
            snapshot.Statistics.PlannedOperationCount,
            snapshot.Statistics.ExactDuplicateFileCount,
            snapshot.Statistics.WarningCount + snapshot.Statistics.ErrorCount);
        ReplaceSnapshotCollections(snapshot, persistedTags);
        Query = ResultsQuery.Default;
        Page = ResultsPage.Empty;
        SelectedRow = null;
        DisplayMode = ResultsDisplayMode.Explorer;
        DuplicateReview.LoadSnapshot(snapshot);
        OnSnapshotStateChanged();
        return RefreshAsync();
    }

    /// <summary>
    /// Returns accepted non-deterministic tags eligible for application-owned catalog persistence.
    /// </summary>
    public IReadOnlyList<TagAssociation> GetPersistableTags() => Array.AsReadOnly(_tagsByFile.Values
        .SelectMany(tags => tags)
        .Where(tag => tag.AcceptanceState == TagAcceptanceState.Accepted &&
                      tag.Source is TagSource.UserApproved or TagSource.AiSuggestion or TagSource.Preference)
        .OrderBy(tag => tag.FileId, StringComparer.Ordinal)
        .ThenBy(tag => tag.NormalizedValue, StringComparer.Ordinal)
        .ToArray());

    /// <summary>
    /// Marks the current explorer state as a historical local catalog snapshot rather than live filesystem information.
    /// </summary>
    public void MarkSnapshotAsSavedCatalogEntry()
    {
        if (Snapshot is not null)
        {
            StatusText = "Viewing a saved local catalog snapshot. It has not been refreshed from the filesystem.";
        }
    }

    /// <summary>
    /// Starts a versioned local query evaluation and publishes only the newest result.
    /// </summary>
    /// <returns>A task that completes after the latest invocation publishes or is superseded.</returns>
    public async Task RefreshAsync()
    {
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            ResetPageForNoSnapshot();
            return;
        }

        var cancellation = ReplaceQueryCancellation();
        var version = Interlocked.Increment(ref _queryVersion);
        var tagsSnapshot = _tagsByFile.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        IsLoading = true;
        StatusText = "Updating results…";
        try
        {
            var result = await Task.Run(
                () => ResultsQueryEngine.Evaluate(snapshot, Query, cancellation.Token, tagsSnapshot),
                cancellation.Token);
            if (cancellation.IsCancellationRequested || version != Volatile.Read(ref _queryVersion))
            {
                return;
            }

            Query = result.Query;
            Page = result.Page;
            _pageRows.Clear();
            foreach (var item in result.Page.Items)
            {
                _pageRows.Add(ResultsFileRow.FromResultFile(
                    item,
                    GetTags(item.Id),
                    result.Page.Matches.TryGetValue(item.Id, out var match) ? match : null));
            }

            if (SelectedRow is not null && !_pageRows.Any(row => row.FileId == SelectedRow.FileId))
            {
                SelectedRow = null;
            }

            StatusText = result.Page.TotalItemCount == 0
                ? HasResults
                    ? "No files match the active filters."
                    : "This completed scan found no files."
                : PageRangeText;
            AiSuggestions.SetContext(
                SelectedRow is null ? null : snapshot.Files.FirstOrDefault(file => file.Id == SelectedRow.FileId),
                snapshot,
                Page.Items);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _queryVersion))
            {
                StatusText = "Results could not be updated. The last valid page remains available.";
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _queryVersion))
            {
                IsLoading = false;
            }

            if (ReferenceEquals(_queryCancellation, cancellation))
            {
                _queryCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Releases pending local query work.
    /// </summary>
    public void Dispose()
    {
        DuplicateReview.ShowGroupFilesRequested -= OnShowGroupFilesRequested;
        DuplicateReview.BackToExplorerRequested -= OnBackToExplorerRequested;
        DuplicateReview.Dispose();
        var contentCancellation = Interlocked.Exchange(ref _contentDetailsCancellation, null);
        contentCancellation?.Cancel();
        contentCancellation?.Dispose();
        AiSuggestions.Dispose();
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _panelPreferenceSaveGate.Dispose();
    }

    private Task AdjustDetailsPanelWidthAsync(double delta) =>
        SetDetailsPanelWidthRatioAsync(DetailsPanelWidthRatio + delta);

    private void ChangeQuery(ResultsQuery query)
    {
        Query = ResultsQueryEngine.Normalize(query);
        SelectedRow = null;
        _ = RefreshAsync();
    }

    private void ReplaceSnapshotCollections(ResultsSnapshot snapshot, IReadOnlyList<TagAssociation>? persistedTags)
    {
        _tagsByFile.Clear();
        foreach (var file in snapshot.Files)
        {
            var extensionTag = file.NormalizedExtension.TrimStart('.');
            if (extensionTag.Length > 0 && AiSuggestionValidator.TryNormalizeTags([extensionTag], out var tags, out _))
            {
                _tagsByFile[file.Id] = Array.AsReadOnly(tags.Select(tag => new TagAssociation(
                    $"tag:{file.Id}:extension:{tag.NormalizedValue}",
                    file.Id,
                    tag.DisplayName,
                    tag.NormalizedValue,
                    "File type",
                    TagSource.Deterministic,
                    TagAcceptanceState.Accepted,
                    "Derived from the scanned file extension.",
                    snapshot.ProjectedAtUtc)).ToArray());
            }
        }

        if (persistedTags is not null)
        {
            var fileIds = snapshot.Files.Select(file => file.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var tag in persistedTags.Where(tag => tag is not null &&
                                                           tag.AcceptanceState == TagAcceptanceState.Accepted &&
                                                           tag.Source != TagSource.Deterministic &&
                                                           fileIds.Contains(tag.FileId)))
            {
                var existing = GetTags(tag.FileId);
                _tagsByFile[tag.FileId] = Array.AsReadOnly(existing
                    .Append(tag)
                    .GroupBy(candidate => candidate.NormalizedValue, StringComparer.Ordinal)
                    .Select(group => group.Last())
                    .OrderBy(candidate => candidate.NormalizedValue, StringComparer.Ordinal)
                    .ToArray());
            }
        }
        _directories.Clear();
        foreach (var directory in snapshot.Directories)
        {
            _directories.Add(directory);
        }

        _plannedOperations.Clear();
        foreach (var operation in snapshot.PlannedOperations)
        {
            _plannedOperations.Add(operation);
        }

        _warnings.Clear();
        foreach (var issue in snapshot.Issues)
        {
            _warnings.Add(issue.Message);
        }

        _extensionOptions.Clear();
        _extensionOptions.Add(new ResultsExtensionFilterOption(null, "All extensions"));
        foreach (var extension in snapshot.Files.Select(file => file.NormalizedExtension).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            _extensionOptions.Add(new ResultsExtensionFilterOption(extension, string.IsNullOrEmpty(extension) ? "No extension" : extension));
        }

        _categoryOptions.Clear();
        _categoryOptions.Add(new ResultsCategoryFilterOption(null, "All categories"));
        _categoryOptions.Add(new ResultsCategoryFilterOption(FileCategory.Unknown, "Unknown or unclassified"));
        foreach (var category in snapshot.Files
                     .Select(file => file.Category)
                     .OfType<FileCategory>()
                     .Where(category => category != FileCategory.Unknown)
                     .Distinct()
                     .Order())
        {
            _categoryOptions.Add(new ResultsCategoryFilterOption(category, category.ToString()));
        }
    }

    private void OnSnapshotStateChanged()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(HasNoSnapshot));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoFilterResults));
        OnPropertyChanged(nameof(IsEmptyCompletedScan));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasNoWarnings));
        OnPropertyChanged(nameof(HasPlannedOperations));
        OnPropertyChanged(nameof(CanOpenDuplicateReview));
        OpenDuplicateReviewCommand.NotifyCanExecuteChanged();
    }

    private void ResetPageForNoSnapshot()
    {
        Page = ResultsPage.Empty;
        _pageRows.Clear();
        SelectedRow = null;
        StatusText = "No completed scan results are available.";
        IsLoading = false;
    }

    private CancellationTokenSource ReplaceQueryCancellation()
    {
        var previous = Interlocked.Exchange(ref _queryCancellation, new CancellationTokenSource());
        previous?.Cancel();
        return _queryCancellation!;
    }

    private void UpdateSelectedDetails()
    {
        var selected = SelectedRow is null || Snapshot is null
            ? null
            : Snapshot.Files.FirstOrDefault(file => file.Id == SelectedRow.FileId);
        SelectedDetails = selected is null
            ? null
            : SelectedResultDetails.From(
                selected,
                Array.AsReadOnly(Snapshot!.PlannedOperations.Where(operation => operation.SourceFileId == selected.Id).ToArray()),
                GetTags(selected.Id));
        RebuildSelectedTagRows(selected?.Id);
        AddUserTagsCommand.NotifyCanExecuteChanged();
        RemoveSelectedTagCommand.NotifyCanExecuteChanged();
        AcceptSuggestedTagCommand.NotifyCanExecuteChanged();
        RejectSuggestedTagCommand.NotifyCanExecuteChanged();
    }

    private void UpdateAiSuggestionContext()
    {
        var snapshot = Snapshot;
        AiSuggestions.SetContext(
            SelectedRow is null || snapshot is null
                ? null
                : snapshot.Files.FirstOrDefault(file => file.Id == SelectedRow.FileId),
            snapshot,
            Page.Items);
    }

    private async Task LoadContentDetailsAsync()
    {
        var previous = Interlocked.Exchange(ref _contentDetailsCancellation, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();
        var cancellation = _contentDetailsCancellation!;
        _contentMetadata.Clear();
        OnPropertyChanged(nameof(HasContentMetadata));
        if (SelectedRow is null || Snapshot is null)
        {
            ContentDetailsStatus = "Select a result to inspect local extracted metadata.";
            return;
        }

        if (_contentStore is null)
        {
            ContentDetailsStatus = "Local extracted metadata is unavailable in this application context.";
            return;
        }

        var selected = Snapshot.Files.FirstOrDefault(file => file.Id == SelectedRow.FileId);
        if (selected is null)
        {
            ContentDetailsStatus = "The selected result is no longer available.";
            return;
        }

        try
        {
            var record = await _contentStore.GetAsync(selected.FullPath, cancellation.Token);
            if (!ReferenceEquals(_contentDetailsCancellation, cancellation))
            {
                return;
            }

            foreach (var field in record?.Metadata ?? [])
            {
                _contentMetadata.Add(field);
            }

            if (record is not null && SelectedRow is not null)
            {
                var existing = GetTags(SelectedRow.FileId)
                    .Where(tag => tag.Source is
                        TagSource.Deterministic or
                        TagSource.UserApproved or
                        TagSource.AiSuggestion or
                        TagSource.Preference)
                    .ToArray();
                var generated = record.Tags.Select(tag => tag with { FileId = SelectedRow.FileId });
                _tagsByFile[SelectedRow.FileId] = Array.AsReadOnly(existing
                    .Concat(generated)
                    .DistinctBy(tag => tag.TagId, StringComparer.Ordinal)
                    .ToArray());
                RebuildSelectedTagRows(SelectedRow.FileId);
            }

            ContentDetailsStatus = record is null
                ? "No extracted metadata is cached for this result."
                : $"OCR state: {record.OcrStatus}. {record.Metadata.Count} provenance-aware field(s).";
            OnPropertyChanged(nameof(HasContentMetadata));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ContentDetailsStatus = "Extracted metadata could not be loaded.";
        }
    }

    private bool CanAddUserTags() => SelectedRow is not null && !IsLoading && !string.IsNullOrWhiteSpace(UserTagText);

    private async Task AddUserTagsAsync()
    {
        if (SelectedRow is null)
        {
            UserTagStatusText = "Select a result file before adding tags.";
            return;
        }

        var values = (UserTagText ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!UserTagFactory.TryCreate(SelectedRow.FileId, values, DateTimeOffset.UtcNow, out var candidates, out var error))
        {
            UserTagStatusText = error;
            return;
        }

        var existing = GetTags(SelectedRow.FileId);
        var existingIdentities = existing.Select(tag => tag.NormalizedValue).ToHashSet(StringComparer.Ordinal);
        var additions = candidates.Where(tag => !existingIdentities.Contains(tag.NormalizedValue)).ToArray();
        if (additions.Length == 0)
        {
            UserTagStatusText = "Those tags are already associated with the selected result.";
            return;
        }

        var acceptedCount = existing.Count(tag => tag.Source != TagSource.Deterministic && tag.AcceptanceState == TagAcceptanceState.Accepted);
        if (acceptedCount + additions.Length > UserTagLimits.MaximumAcceptedTagsPerFile)
        {
            UserTagStatusText = $"A result can have at most {UserTagLimits.MaximumAcceptedTagsPerFile} user-managed tags.";
            return;
        }

        _tagsByFile[SelectedRow.FileId] = Array.AsReadOnly(existing
            .Concat(additions)
            .OrderBy(tag => tag.NormalizedValue, StringComparer.Ordinal)
            .ToArray());
        UserTagText = null;
        var status = additions.Length == 1
            ? "Tag added as OpenSorSe metadata. The selected file was not changed."
            : $"{additions.Length} tags added as OpenSorSe metadata. The selected file was not changed.";
        await PublishTagChangeAsync(status);
    }

    private bool CanRemoveSelectedTag() => SelectedRow is not null && !IsLoading && SelectedUserTag?.IsRemovable == true;

    private async Task RemoveSelectedTagAsync()
    {
        if (SelectedRow is null || SelectedUserTag is null || !SelectedUserTag.IsRemovable)
        {
            return;
        }

        var existing = GetTags(SelectedRow.FileId);
        var remaining = existing.Where(tag => !string.Equals(tag.TagId, SelectedUserTag.TagId, StringComparison.Ordinal) || tag.Source == TagSource.Deterministic).ToArray();
        if (remaining.Length == existing.Count)
        {
            UserTagStatusText = "The selected tag is no longer available.";
            RebuildSelectedTagRows(SelectedRow.FileId);
            return;
        }

        _tagsByFile[SelectedRow.FileId] = Array.AsReadOnly(remaining);
        SelectedUserTag = null;
        await PublishTagChangeAsync("Tag removed from OpenSorSe metadata. The selected file was not changed.");
    }

    private Task AcceptSuggestedTagAsync() =>
        UpdateGeneratedTagStateAsync(TagAcceptanceState.Accepted);

    private Task RejectSuggestedTagAsync() =>
        UpdateGeneratedTagStateAsync(TagAcceptanceState.Rejected);

    private async Task UpdateGeneratedTagStateAsync(TagAcceptanceState state)
    {
        if (_contentStore is null ||
            SelectedRow is null ||
            SelectedUserTag is null ||
            state is not (TagAcceptanceState.Accepted or TagAcceptanceState.Rejected))
        {
            return;
        }

        var selectedFile = Snapshot?.Files.FirstOrDefault(file => file.Id == SelectedRow.FileId);
        if (selectedFile is null)
        {
            return;
        }

        var record = await _contentStore.GetAsync(selectedFile.FullPath, CancellationToken.None);
        var tags = record?.Tags.ToArray();
        var index = tags is null
            ? -1
            : Array.FindIndex(
                tags,
                tag => string.Equals(tag.TagId, SelectedUserTag.TagId, StringComparison.Ordinal));
        if (record is null || tags is null || index < 0)
        {
            UserTagStatusText = "The generated tag is no longer available.";
            return;
        }

        tags[index] = tags[index] with
        {
            AcceptanceState = state,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await _contentStore.UpsertAsync(
            record with { Tags = Array.AsReadOnly(tags) },
            CancellationToken.None);
        var selectedTagId = tags[index].TagId;
        await LoadContentDetailsAsync();
        SelectedUserTag = UserTags.FirstOrDefault(tag =>
            string.Equals(tag.TagId, selectedTagId, StringComparison.Ordinal));
        UserTagStatusText = state == TagAcceptanceState.Accepted
            ? "Generated tag accepted as local OpenSorSe metadata."
            : "Generated tag rejected until source content changes or generated tags are reset.";
    }

    private async Task PublishTagChangeAsync(string status)
    {
        UpdateSelectedDetails();
        PersistedTagsChanged?.Invoke(this, EventArgs.Empty);
        await RefreshAsync();
        UserTagStatusText = status;
    }

    private void RebuildSelectedTagRows(string? fileId)
    {
        var selectedTagId = SelectedUserTag?.TagId;
        _userTags.Clear();
        if (fileId is not null)
        {
            foreach (var tag in GetTags(fileId).OrderBy(tag => tag.Source == TagSource.Deterministic ? 0 : 1).ThenBy(tag => tag.NormalizedValue, StringComparer.Ordinal))
            {
                _userTags.Add(ResultTagRow.FromAssociation(tag));
            }
        }

        SelectedUserTag = selectedTagId is null
            ? null
            : UserTags.FirstOrDefault(tag => string.Equals(tag.TagId, selectedTagId, StringComparison.Ordinal));
    }

    private bool HasActiveFilters() => Query != ResultsQuery.Default;

    private bool CanClearFilters() => HasSnapshot && HasActiveFilters();

    private void ClearFilters()
    {
        var pageSize = Query.PageSize;
        ChangeQuery(ResultsQuery.Default with { PageSize = pageSize });
    }

    private void GoToPreviousPage()
    {
        if (CanGoPreviousPage)
        {
            ChangeQuery(Query with { PageIndex = Query.PageIndex - 1 });
        }
    }

    private void GoToNextPage()
    {
        if (CanGoNextPage)
        {
            ChangeQuery(Query with { PageIndex = Query.PageIndex + 1 });
        }
    }

    private void OpenDuplicateReview() => DisplayMode = ResultsDisplayMode.ExactDuplicates;

    private void BackToExplorer() => DisplayMode = ResultsDisplayMode.Explorer;

    private void OnShowGroupFilesRequested(object? sender, string groupId)
    {
        if (Snapshot?.DuplicateGroups.Any(group => string.Equals(group.GroupId, groupId, StringComparison.Ordinal)) != true)
        {
            return;
        }

        var query = ResultsQuery.Default with
        {
            PageSize = Query.PageSize,
            DuplicateGroupId = groupId,
        };
        DisplayMode = ResultsDisplayMode.Explorer;
        ChangeQuery(query);
    }

    private void OnBackToExplorerRequested(object? sender, EventArgs eventArgs) => BackToExplorer();

    private IReadOnlyList<TagAssociation> GetTags(string fileId) => _tagsByFile.TryGetValue(fileId, out var tags)
        ? tags
        : Array.Empty<TagAssociation>();

    private sealed class PreviewConfigurationService : IConfigurationService
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
