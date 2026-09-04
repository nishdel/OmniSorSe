using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.CatalogComparison;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Coordinates bounded comparison of two explicitly selected historical catalog snapshots.
/// </summary>
public sealed class CatalogComparisonViewModel : ViewModelBase, IDisposable
{
    private const int MaximumPublishedRowCount = 500;
    private const int MaximumFilterTextLength = 512;
    private static readonly IReadOnlyList<CatalogComparisonFilter> ComparisonFilters = Enum.GetValues<CatalogComparisonFilter>();
    private readonly IConfigurationService _configurationService;
    private readonly IResultsCatalogStore? _catalogStore;
    private readonly ICatalogComparisonService _comparisonService;
    private readonly ObservableCollection<CatalogEntryRow> _entries = [];
    private readonly ObservableCollection<CatalogComparisonChangeRow> _changes = [];
    private CatalogEntryRow? _baselineSelection;
    private CatalogEntryRow? _currentSelection;
    private CatalogComparisonChangeRow? _selectedChange;
    private CatalogComparisonFilter _selectedFilter = CatalogComparisonFilter.Changed;
    private string? _filterText;
    private string _statusText = "Select two saved snapshots to compare stored historical metadata.";
    private string _statisticsText = "No comparison has been run.";
    private string _scopeStatusText = "Source-scope compatibility is not available until comparison.";
    private bool _isBusy;
    private CatalogComparisonResult? _result;
    private CatalogEntry? _baselineEntry;
    private CatalogEntry? _currentEntry;
    private CancellationTokenSource? _operationCancellation;
    private long _operationVersion;
    private bool _isDisposed;
    private bool _isPublishingSelections;

    /// <summary>Initializes comparison state over the optional local catalog.</summary>
    public CatalogComparisonViewModel(
        IConfigurationService configurationService,
        IResultsCatalogStore? catalogStore,
        ICatalogComparisonService comparisonService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _catalogStore = catalogStore;
        _comparisonService = comparisonService ?? throw new ArgumentNullException(nameof(comparisonService));
        Entries = new ReadOnlyObservableCollection<CatalogEntryRow>(_entries);
        Changes = new ReadOnlyObservableCollection<CatalogComparisonChangeRow>(_changes);
        RefreshCommand = new AsyncRelayCommand(RefreshEntriesAsync, () => !IsBusy);
        CompareCommand = new AsyncRelayCommand(CompareAsync, CanCompare);
        CancelCommand = new RelayCommand(CancelActiveOperation, () => IsBusy);
        OpenBaselineCommand = new RelayCommand(OpenBaseline, () => !IsBusy && _baselineEntry is not null);
        OpenCurrentCommand = new RelayCommand(OpenCurrent, () => !IsBusy && _currentEntry is not null);
    }

    /// <summary>Raised when the shell should present one complete historical snapshot in Results.</summary>
    public event EventHandler<CatalogEntry>? EntryOpened;

    /// <summary>Gets catalog summaries available for baseline/current selection.</summary>
    public ReadOnlyObservableCollection<CatalogEntryRow> Entries { get; }

    /// <summary>Gets the bounded comparison rows for the current filters.</summary>
    public ReadOnlyObservableCollection<CatalogComparisonChangeRow> Changes { get; }

    /// <summary>Gets all supported comparison filters.</summary>
    public IReadOnlyList<CatalogComparisonFilter> Filters => ComparisonFilters;

    /// <summary>Gets whether local catalog access is enabled.</summary>
    public bool IsEnabled => _configurationService.Current.Catalog.Enabled;

    /// <summary>Gets or sets the older/reference snapshot selection.</summary>
    public CatalogEntryRow? BaselineSelection
    {
        get => _baselineSelection;
        set
        {
            if (SetProperty(ref _baselineSelection, value))
            {
                CancelForSelectionChange();
                ClearResultForSelectionChange();
                CompareCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the current/comparison snapshot selection.</summary>
    public CatalogEntryRow? CurrentSelection
    {
        get => _currentSelection;
        set
        {
            if (SetProperty(ref _currentSelection, value))
            {
                CancelForSelectionChange();
                ClearResultForSelectionChange();
                CompareCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the selected visible change row.</summary>
    public CatalogComparisonChangeRow? SelectedChange
    {
        get => _selectedChange;
        set => SetProperty(ref _selectedChange, value);
    }

    /// <summary>Gets or sets the active change-kind filter.</summary>
    public CatalogComparisonFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (SetProperty(ref _selectedFilter, value))
            {
                PublishFilteredRows();
            }
        }
    }

    /// <summary>Gets or sets optional case-insensitive stored filename/path filter text.</summary>
    public string? FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                PublishFilteredRows();
            }
        }
    }

    /// <summary>Gets whether a refresh or comparison is active.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                CompareCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                OpenBaselineCommand.NotifyCanExecuteChanged();
                OpenCurrentCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether no saved entries are available.</summary>
    public bool HasNoEntries => Entries.Count == 0;

    /// <summary>Gets whether no rows match the active filters.</summary>
    public bool HasNoChanges => Changes.Count == 0;

    /// <summary>Gets current operation/filter status.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets complete comparison statistics independent of presentation filtering.</summary>
    public string StatisticsText
    {
        get => _statisticsText;
        private set => SetProperty(ref _statisticsText, value);
    }

    /// <summary>Gets source-scope compatibility and duplicate-input warning text.</summary>
    public string ScopeStatusText
    {
        get => _scopeStatusText;
        private set => SetProperty(ref _scopeStatusText, value);
    }

    /// <summary>Gets the command that refreshes saved snapshot choices.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that compares two distinct selections.</summary>
    public IAsyncRelayCommand CompareCommand { get; }

    /// <summary>Gets the command that explicitly cancels active comparison work.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Gets the command that opens the loaded baseline snapshot in historical Results.</summary>
    public IRelayCommand OpenBaselineCommand { get; }

    /// <summary>Gets the command that opens the loaded current snapshot in historical Results.</summary>
    public IRelayCommand OpenCurrentCommand { get; }

    /// <summary>Refreshes bounded catalog summaries without loading snapshots.</summary>
    public async Task RefreshEntriesAsync()
    {
        OnPropertyChanged(nameof(IsEnabled));
        CompareCommand.NotifyCanExecuteChanged();
        if (!IsEnabled)
        {
            _entries.Clear();
            BaselineSelection = null;
            CurrentSelection = null;
            ClearResult();
            NotifyCollectionState();
            StatusText = "Local catalog storage is disabled. Enable it in Settings before comparing saved snapshots.";
            return;
        }

        if (_catalogStore is null)
        {
            StatusText = "Historical snapshot comparison is unavailable in this application configuration.";
            return;
        }

        var baselineId = BaselineSelection?.Id;
        var currentId = CurrentSelection?.Id;
        ClearResult();
        var (cancellation, version) = BeginOperation();
        StatusText = "Refreshing saved snapshot choices...";
        try
        {
            var summaries = await _catalogStore.ListAsync(cancellation.Token);
            if (!IsCurrentOperation(cancellation, version))
            {
                return;
            }

            _entries.Clear();
            foreach (var summary in summaries)
            {
                _entries.Add(CatalogEntryRow.FromSummary(summary));
            }

            _isPublishingSelections = true;
            try
            {
                BaselineSelection = baselineId is null ? null : Entries.FirstOrDefault(entry => entry.Id == baselineId);
                CurrentSelection = currentId is null ? null : Entries.FirstOrDefault(entry => entry.Id == currentId);
            }
            finally
            {
                _isPublishingSelections = false;
            }
            if (_result is not null &&
                (!Entries.Any(entry => entry.Id == _result.BaselineEntryId) || !Entries.Any(entry => entry.Id == _result.CurrentEntryId)))
            {
                ClearResult();
            }

            NotifyCollectionState();
            StatusText = Entries.Count switch
            {
                0 => "No saved snapshots are available for comparison.",
                1 => "At least two saved snapshots are required for comparison.",
                _ => "Select two distinct historical snapshots. Comparison reads stored metadata only and does not inspect the filesystem.",
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "Refreshing comparison choices was cancelled.";
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "Saved snapshots could not be listed for comparison. Existing catalog data was preserved.";
            }
        }
        finally
        {
            EndOperation(cancellation, version);
        }
    }

    /// <summary>Loads and compares the two distinct selected historical snapshots.</summary>
    public async Task CompareAsync()
    {
        var baselineRow = BaselineSelection;
        var currentRow = CurrentSelection;
        if (baselineRow is null || currentRow is null)
        {
            StatusText = "Select both a baseline and a current saved snapshot.";
            return;
        }

        if (string.Equals(baselineRow.Id, currentRow.Id, StringComparison.Ordinal))
        {
            StatusText = "Choose two distinct saved snapshots to compare.";
            return;
        }

        if (!IsEnabled || _catalogStore is null)
        {
            StatusText = "Local catalog storage must be enabled before comparing snapshots.";
            return;
        }

        ClearResult();
        var (cancellation, version) = BeginOperation();
        StatusText = "Comparing stored historical metadata...";
        try
        {
            var baseline = await _catalogStore.LoadAsync(baselineRow.Id, cancellation.Token);
            var current = await _catalogStore.LoadAsync(currentRow.Id, cancellation.Token);
            if (baseline is null || current is null)
            {
                if (IsCurrentOperation(cancellation, version))
                {
                    StatusText = "One selected snapshot is no longer available. Refresh the choices and compare again.";
                }

                return;
            }

            var result = await RunComparisonAsync(baseline, current, cancellation.Token);
            if (!IsCurrentOperation(cancellation, version) ||
                !string.Equals(BaselineSelection?.Id, baselineRow.Id, StringComparison.Ordinal) ||
                !string.Equals(CurrentSelection?.Id, currentRow.Id, StringComparison.Ordinal))
            {
                return;
            }

            _baselineEntry = baseline;
            _currentEntry = current;
            _result = result;
            StatisticsText = FormatStatistics(result.Statistics);
            ScopeStatusText = FormatScopeStatus(result);
            PublishFilteredRows();
            OpenBaselineCommand.NotifyCanExecuteChanged();
            OpenCurrentCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "Snapshot comparison was cancelled. No stored path was accessed.";
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "The selected snapshots could not be compared. Existing catalog data and Results state were preserved.";
            }
        }
        finally
        {
            EndOperation(cancellation, version);
        }
    }

    /// <summary>Clears stale comparison state after catalog maintenance or identity changes.</summary>
    public void InvalidateCatalog()
    {
        if (_isDisposed)
        {
            return;
        }

        CancelActiveOperation();
        ClearResult();
        StatusText = "Saved catalog data changed. Refresh the snapshot choices before comparing again.";
    }

    /// <summary>Releases active comparison cancellation resources.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        var cancellation = Interlocked.Exchange(ref _operationCancellation, null);
        Interlocked.Increment(ref _operationVersion);
        cancellation?.Cancel();
        _isDisposed = true;
    }

    private bool CanCompare() =>
        !IsBusy && IsEnabled && _catalogStore is not null &&
        BaselineSelection is not null && CurrentSelection is not null &&
        !string.Equals(BaselineSelection.Id, CurrentSelection.Id, StringComparison.Ordinal);

    private Task<CatalogComparisonResult> RunComparisonAsync(
        CatalogEntry baseline,
        CatalogEntry current,
        CancellationToken cancellationToken)
    {
        // Comparison is synchronous and may run to the 2,000-file bound. A dedicated worker keeps
        // UI/test cancellation control from competing with that work for a shared ThreadPool slot.
        return Task.Factory.StartNew(
            () => _comparisonService.Compare(baseline, current, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
    }

    private void PublishFilteredRows()
    {
        if (_result is null)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(FilterText) ? null : FilterText.Trim();
        if (text is not null && text.Length > MaximumFilterTextLength)
        {
            _changes.Clear();
            SelectedChange = null;
            NotifyCollectionState();
            StatusText = $"Comparison filter text must be no longer than {MaximumFilterTextLength} characters.";
            return;
        }

        var matching = _result.Changes
            .Where(change => MatchesFilter(change.Kind))
            .Where(change => text is null || MatchesText(change, text))
            .ToArray();
        var published = matching.Take(MaximumPublishedRowCount).Select(CatalogComparisonChangeRow.FromModel).ToArray();
        _changes.Clear();
        foreach (var change in published)
        {
            _changes.Add(change);
        }

        SelectedChange = null;
        NotifyCollectionState();
        StatusText = published.Length == 0
            ? "No historical comparison rows match the active filters. Aggregate statistics remain complete."
            : matching.Length > MaximumPublishedRowCount
                ? $"Showing the first {MaximumPublishedRowCount} of {matching.Length} matching historical rows. No stored path was accessed."
                : $"Showing {published.Length} historical comparison row(s). No stored path was accessed.";
    }

    private bool MatchesFilter(CatalogComparisonChangeKind kind) => SelectedFilter switch
    {
        CatalogComparisonFilter.Changed => kind != CatalogComparisonChangeKind.Unchanged,
        CatalogComparisonFilter.Added => kind == CatalogComparisonChangeKind.Added,
        CatalogComparisonFilter.Removed => kind == CatalogComparisonChangeKind.Removed,
        CatalogComparisonFilter.Modified => kind == CatalogComparisonChangeKind.Modified,
        CatalogComparisonFilter.Unchanged => kind == CatalogComparisonChangeKind.Unchanged,
        CatalogComparisonFilter.All => true,
        _ => throw new InvalidOperationException("The comparison filter is unsupported."),
    };

    private static bool MatchesText(CatalogFileChange change, string text)
    {
        var file = change.CurrentFile ?? change.BaselineFile;
        return file is not null &&
               (file.DisplayFileName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                file.FullPath.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearResultForSelectionChange()
    {
        if (_result is null && _baselineEntry is null && _currentEntry is null)
        {
            return;
        }

        ClearResult();
        StatusText = "Snapshot selection changed. Run comparison again to review current selections.";
    }

    private void CancelForSelectionChange()
    {
        if (!_isPublishingSelections && IsBusy)
        {
            CancelActiveOperation();
            StatusText = "Snapshot selection changed, so the active historical operation was cancelled.";
        }
    }

    private void ClearResult()
    {
        _result = null;
        _baselineEntry = null;
        _currentEntry = null;
        _changes.Clear();
        SelectedChange = null;
        StatisticsText = "No comparison has been run.";
        ScopeStatusText = "Source-scope compatibility is not available until comparison.";
        NotifyCollectionState();
        OpenBaselineCommand.NotifyCanExecuteChanged();
        OpenCurrentCommand.NotifyCanExecuteChanged();
    }

    private void OpenBaseline()
    {
        if (_baselineEntry is null)
        {
            return;
        }

        EntryOpened?.Invoke(this, _baselineEntry);
        StatusText = "Baseline snapshot opened in historical Results. It was not refreshed from the filesystem.";
    }

    private void OpenCurrent()
    {
        if (_currentEntry is null)
        {
            return;
        }

        EntryOpened?.Invoke(this, _currentEntry);
        StatusText = "Current snapshot opened in historical Results. It was not refreshed from the filesystem.";
    }

    private void CancelActiveOperation()
    {
        var cancellation = Interlocked.Exchange(ref _operationCancellation, null);
        if (cancellation is null)
        {
            return;
        }

        Interlocked.Increment(ref _operationVersion);
        cancellation.Cancel();
        IsBusy = false;
        StatusText = "Snapshot comparison work was cancelled. Existing catalog data was preserved.";
    }

    private (CancellationTokenSource Cancellation, long Version) BeginOperation()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _operationCancellation, cancellation);
        previous?.Cancel();
        var version = Interlocked.Increment(ref _operationVersion);
        IsBusy = true;
        return (cancellation, version);
    }

    private bool IsCurrentOperation(CancellationTokenSource cancellation, long version) =>
        !cancellation.IsCancellationRequested &&
        ReferenceEquals(_operationCancellation, cancellation) &&
        version == Volatile.Read(ref _operationVersion);

    private void EndOperation(CancellationTokenSource cancellation, long version)
    {
        if (ReferenceEquals(_operationCancellation, cancellation))
        {
            _operationCancellation = null;
        }

        if (version == Volatile.Read(ref _operationVersion))
        {
            IsBusy = false;
        }

        cancellation.Dispose();
    }

    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(HasNoChanges));
        CompareCommand.NotifyCanExecuteChanged();
    }

    private static string FormatStatistics(CatalogComparisonStatistics statistics) =>
        $"Baseline {statistics.BaselineFileCount:N0}; current {statistics.CurrentFileCount:N0}; " +
        $"added {statistics.AddedCount:N0}; removed {statistics.RemovedCount:N0}; " +
        $"modified {statistics.ModifiedCount:N0}; unchanged {statistics.UnchangedCount:N0}.";

    private static string FormatScopeStatus(CatalogComparisonResult result)
    {
        var scope = result.ScopeMatch switch
        {
            CatalogScopeMatch.Same => "Captured source scopes match.",
            CatalogScopeMatch.Different => "Warning: captured source scopes differ, so added and removed counts may reflect scope rather than filesystem change.",
            CatalogScopeMatch.Unknown => "Source-scope compatibility is unknown because at least one snapshot predates captured v0.8 scope.",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        return result.Statistics.IgnoredDuplicateRecordCount == 0
            ? scope
            : $"{scope} {result.Statistics.IgnoredDuplicateRecordCount:N0} duplicate stored path record(s) were ignored deterministically.";
    }
}
