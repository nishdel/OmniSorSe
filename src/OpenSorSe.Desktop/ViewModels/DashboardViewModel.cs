using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Guidance;
using OpenSorSe.Application.Indexing;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents durable library readiness and task-oriented navigation without hydrating file projections.</summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly Action<NavigationDestination> _navigate;
    private readonly IProductReadinessService? _readinessService;
    private readonly ObservableCollection<HomeSavedViewRow> _savedViewShortcuts = [];
    private readonly ObservableCollection<HomeCapabilityRow> _capabilities = [];
    private DashboardStatistics _statistics = DashboardStatistics.Empty;
    private bool _hasCompletedScan;
    private long _knownFileCount;
    private int _sourceCount;
    private long _pendingReviewCount;
    private int _savedViewCount;
    private bool _isBaseSearchReady;
    private bool _isRefreshing;
    private string _statusText = "No indexed library is available yet.";
    private string _readinessText = "Scan a folder to make filenames and metadata searchable.";

    /// <summary>Initializes Home with optional durable readiness queries.</summary>
    public DashboardViewModel(Action<NavigationDestination> navigate, IProductReadinessService? readinessService = null)
    {
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _readinessService = readinessService;
        SavedViewShortcuts = new ReadOnlyObservableCollection<HomeSavedViewRow>(_savedViewShortcuts);
        Capabilities = new ReadOnlyObservableCollection<HomeCapabilityRow>(_capabilities);
        ScanFolderCommand = new RelayCommand(() => _navigate(NavigationDestination.Scan));
        ViewResultsCommand = new RelayCommand(() => _navigate(NavigationDestination.Results), () => HasCompletedScan);
        OpenSettingsCommand = new RelayCommand(() => _navigate(NavigationDestination.Settings));
        FindCommand = new RelayCommand(() => _navigate(NavigationDestination.SemanticSearch));
        UnderstandCommand = new RelayCommand(() => UnderstandRequested?.Invoke(this, EventArgs.Empty));
        ReviewCommand = new RelayCommand(() => ReviewRequested?.Invoke(this, EventArgs.Empty), () => PendingReviewCount > 0);
        OrganizeCommand = new RelayCommand(() => OrganizeRequested?.Invoke(this, EventArgs.Empty));
        OpenSavedViewCommand = new RelayCommand<HomeSavedViewRow>(
            row =>
            {
                if (row is not null && SavedViewShortcuts.Contains(row))
                {
                    SavedViewRequested?.Invoke(this, row.Id);
                }
            },
            row => row is not null && SavedViewShortcuts.Contains(row));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _readinessService is not null && !IsRefreshing);
    }

    /// <summary>Raised when Understand needs shell-aware selection of Files or Search.</summary>
    public event EventHandler? UnderstandRequested;
    /// <summary>Raised when pending Moderate suggestions should open in Search.</summary>
    public event EventHandler? ReviewRequested;
    /// <summary>Raised when Organize needs shell-aware prerequisite guidance.</summary>
    public event EventHandler? OrganizeRequested;
    /// <summary>Raised when a Saved View shortcut should be evaluated in Search.</summary>
    public event EventHandler<string>? SavedViewRequested;

    /// <summary>Gets latest in-session scan statistics when available.</summary>
    public DashboardStatistics Statistics { get => _statistics; private set => SetProperty(ref _statistics, value); }

    /// <summary>Gets whether the process retains a completed scan snapshot for Files.</summary>
    public bool HasCompletedScan
    {
        get => _hasCompletedScan;
        private set
        {
            if (SetProperty(ref _hasCompletedScan, value))
            {
                OnPropertyChanged(nameof(IsAwaitingFirstScan));
                ViewResultsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether neither durable nor in-session file state is available.</summary>
    public bool IsAwaitingFirstScan => !HasCompletedScan && !HasIndexedLibrary;
    /// <summary>Gets whether at least one source or active indexed file is durable.</summary>
    public bool HasIndexedLibrary => SourceCount > 0 || KnownFileCount > 0;

    /// <summary>Gets the active file count reported by durable indexing coverage.</summary>
    public long KnownFileCount
    {
        get => _knownFileCount;
        private set
        {
            if (SetProperty(ref _knownFileCount, value))
            {
                OnPropertyChanged(nameof(HasIndexedLibrary));
                OnPropertyChanged(nameof(IsAwaitingFirstScan));
            }
        }
    }

    /// <summary>Gets the number of registered durable sources.</summary>
    public int SourceCount
    {
        get => _sourceCount;
        private set
        {
            if (SetProperty(ref _sourceCount, value))
            {
                OnPropertyChanged(nameof(HasIndexedLibrary));
                OnPropertyChanged(nameof(IsAwaitingFirstScan));
            }
        }
    }

    /// <summary>Gets the complete-library count of files with unresolved Moderate suggestions.</summary>
    public long PendingReviewCount
    {
        get => _pendingReviewCount;
        private set
        {
            if (SetProperty(ref _pendingReviewCount, value))
            {
                ReviewCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasPendingReview));
            }
        }
    }

    /// <summary>Gets whether Review has actionable work.</summary>
    public bool HasPendingReview => PendingReviewCount > 0;
    /// <summary>Gets the durable Saved View rule count.</summary>
    public int SavedViewCount { get => _savedViewCount; private set => SetProperty(ref _savedViewCount, value); }
    /// <summary>Gets whether all known filenames and basic metadata are searchable.</summary>
    public bool IsBaseSearchReady { get => _isBaseSearchReady; private set => SetProperty(ref _isBaseSearchReady, value); }

    /// <summary>Gets whether Home is running its explicit bounded refresh.</summary>
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the durable library status announcement.</summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    /// <summary>Gets the truthful progressive-indexing phase explanation.</summary>
    public string ReadinessText { get => _readinessText; private set => SetProperty(ref _readinessText, value); }
    /// <summary>Gets at most three Saved View shortcuts without result membership.</summary>
    public ReadOnlyObservableCollection<HomeSavedViewRow> SavedViewShortcuts { get; }
    /// <summary>Gets whether any bounded Saved View shortcut is visible.</summary>
    public bool HasSavedViewShortcuts => SavedViewShortcuts.Count > 0;
    /// <summary>Gets compact optional-capability states from the most recent explicit refresh.</summary>
    public ReadOnlyObservableCollection<HomeCapabilityRow> Capabilities { get; }

    /// <summary>Gets the action that opens source selection.</summary>
    public IRelayCommand ScanFolderCommand { get; }
    /// <summary>Gets the action that opens the retained in-session Files snapshot.</summary>
    public IRelayCommand ViewResultsCommand { get; }
    /// <summary>Gets the action that opens Settings.</summary>
    public IRelayCommand OpenSettingsCommand { get; }
    /// <summary>Gets the Find task action.</summary>
    public IRelayCommand FindCommand { get; }
    /// <summary>Gets the Understand task action.</summary>
    public IRelayCommand UnderstandCommand { get; }
    /// <summary>Gets the Review task action.</summary>
    public IRelayCommand ReviewCommand { get; }
    /// <summary>Gets the Organize task action.</summary>
    public IRelayCommand OrganizeCommand { get; }
    /// <summary>Gets the action that opens one Saved View by stable ID.</summary>
    public IRelayCommand<HomeSavedViewRow> OpenSavedViewCommand { get; }
    /// <summary>Gets the explicit bounded readiness refresh action.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Refreshes Home using bounded durable counts; failures do not affect ordinary navigation.</summary>
    public async Task RefreshAsync()
    {
        if (_readinessService is null || IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var snapshot = await _readinessService.GetSnapshotAsync();
            KnownFileCount = snapshot.KnownFileCount;
            SourceCount = snapshot.SourceCount;
            PendingReviewCount = snapshot.PendingReviewCount;
            SavedViewCount = snapshot.SavedViewCount;
            IsBaseSearchReady = snapshot.IsBaseSearchReady;
            _savedViewShortcuts.Clear();
            foreach (var view in snapshot.SavedViewShortcuts)
            {
                _savedViewShortcuts.Add(new HomeSavedViewRow(view.Id, view.Name));
            }

            _capabilities.Clear();
            foreach (var capability in snapshot.Capabilities)
            {
                _capabilities.Add(HomeCapabilityRow.FromModel(capability));
            }

            OnPropertyChanged(nameof(HasSavedViewShortcuts));
            StatusText = snapshot.KnownFileCount == 0
                ? snapshot.SourceCount == 0
                    ? "No indexed library is available yet."
                    : "Indexed sources are registered; file discovery has not published searchable coverage yet."
                : $"{snapshot.KnownFileCount:N0} indexed file(s) are known across {snapshot.SourceCount:N0} source(s).";
            ReadinessText = FormatReadiness(snapshot);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Home readiness refresh was cancelled. The last durable summary remains visible.";
        }
        catch (Exception)
        {
            StatusText = "Home could not refresh durable library status. Search, Files, and source data were not affected.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Retains latest in-session scan totals while durable readiness remains authoritative after restart.</summary>
    public void UpdateFromCompletedScan(ResultsSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Statistics = new DashboardStatistics(summary.FilesScanned, summary.FoldersDiscovered, summary.ExactDuplicates, summary.Warnings);
        HasCompletedScan = true;
        StatusText = $"Latest scan completed: {summary.FilesScanned} file(s) and {summary.FoldersDiscovered} folder(s) discovered.";
    }

    private static string FormatReadiness(ProductReadinessSnapshot snapshot) => snapshot.Phase switch
    {
        IndexingProgressPhase.DiscoveringFiles => "Scanning files. Search coverage will grow as files are discovered.",
        IndexingProgressPhase.BuildingBaseSearchCoverage => "Building base Search coverage. Some indexed files are already searchable.",
        IndexingProgressPhase.DeeperAnalysis => "Files and base Search are ready. Enabled deeper intelligence and classification are still running.",
        IndexingProgressPhase.Paused => snapshot.IsBaseSearchReady ? "Base Search is ready; deeper indexing is paused." : "Indexing is paused before base Search coverage is complete.",
        IndexingProgressPhase.Waiting => snapshot.IsBaseSearchReady ? "Base Search is ready; optional deeper work is waiting for a capability or resource." : "Indexing is waiting for a capability or resource.",
        IndexingProgressPhase.Cancelled => snapshot.IsBaseSearchReady ? "Base Search remains available; the latest background run was cancelled." : "The latest indexing run was cancelled before base coverage completed.",
        IndexingProgressPhase.Failed => snapshot.IsBaseSearchReady ? $"Base Search remains available; {snapshot.FailedStageCount:N0} deeper stage failure(s) need attention." : "Indexing needs attention before complete base Search coverage is available.",
        _ => snapshot.IsBaseSearchReady ? "Base Search and all currently enabled indexing stages are complete." : "No complete base Search coverage is available yet.",
    };
}

/// <summary>Contains one bounded Home shortcut without evaluating its query.</summary>
public sealed record HomeSavedViewRow(string Id, string Name);

/// <summary>Contains one accessible optional-capability row.</summary>
public sealed record HomeCapabilityRow(string Id, string DisplayName, string State, string Explanation, string AccessibleName)
{
    /// <summary>Maps one application readiness value into accessible presentation text.</summary>
    public static HomeCapabilityRow FromModel(OptionalCapabilityReadiness readiness)
    {
        var state = readiness.State switch
        {
            OptionalCapabilityState.NotConfigured => "Not configured",
            OptionalCapabilityState.NeedsAttention => "Needs attention",
            _ => readiness.State.ToString(),
        };
        return new(
            readiness.Id,
            readiness.DisplayName,
            state,
            readiness.Explanation,
            $"{readiness.DisplayName}: {state}. {readiness.Explanation}");
    }
}
