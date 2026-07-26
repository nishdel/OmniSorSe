using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.CatalogSearch;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Performs bounded deterministic metadata search across opt-in, application-owned saved result snapshots.
/// </summary>
public sealed class CatalogSearchViewModel : ViewModelBase, IDisposable
{
    private const int MaximumPublishedHitCount = 200;
    private readonly IConfigurationService _configurationService;
    private readonly IResultsCatalogStore? _catalogStore;
    private readonly ISavedCatalogSearchStore? _savedSearchStore;
    private readonly ObservableCollection<CatalogSearchHitRow> _hits = [];
    private readonly ObservableCollection<SavedCatalogSearchRow> _savedSearches = [];
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _savedSearchCancellation;
    private long _searchVersion;
    private long _savedSearchVersion;
    private Dictionary<string, CatalogEntry> _entriesById = new(StringComparer.Ordinal);
    private string? _queryText;
    private CatalogSearchHitRow? _selectedHit;
    private bool _isSearching;
    private string _statusText = "Enter text to search saved catalog metadata locally.";
    private string? _savedSearchName;
    private SavedCatalogSearchRow? _selectedSavedSearch;
    private string? _savedSearchRenameText;
    private bool _isSavedSearchBusy;
    private bool _isSavedSearchResetPending;
    private string _savedSearchStatusText = "No saved searches have been loaded.";
    private bool _isSearchCompleted;
    private StatusKind _statusKind = StatusKind.Information;
    private bool _isDisposed;

    /// <summary>
    /// Initializes catalog-wide search over the active configuration and optional catalog store.
    /// </summary>
    public CatalogSearchViewModel(IConfigurationService configurationService, IResultsCatalogStore? catalogStore)
        : this(configurationService, catalogStore, null)
    {
    }

    /// <summary>
    /// Initializes catalog-wide search plus bounded application-owned named query persistence.
    /// </summary>
    public CatalogSearchViewModel(
        IConfigurationService configurationService,
        IResultsCatalogStore? catalogStore,
        ISavedCatalogSearchStore? savedSearchStore)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _catalogStore = catalogStore;
        _savedSearchStore = savedSearchStore;
        Hits = new ReadOnlyObservableCollection<CatalogSearchHitRow>(_hits);
        SavedSearches = new ReadOnlyObservableCollection<SavedCatalogSearchRow>(_savedSearches);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsSearching);
        ClearQueryCommand = new RelayCommand(ClearQuery, () => HasQuery || Hits.Count > 0);
        OpenSelectedHitCommand = new AsyncRelayCommand(OpenSelectedHitAsync, () => !IsSearching && SelectedHit is not null && IsEnabled);
        RefreshSavedSearchesCommand = new AsyncRelayCommand(RefreshSavedSearchesAsync, () => !IsSavedSearchBusy);
        SaveCurrentSearchCommand = new AsyncRelayCommand(SaveCurrentSearchAsync, CanSaveCurrentSearch);
        RunSelectedSavedSearchCommand = new AsyncRelayCommand(RunSelectedSavedSearchAsync, CanRunSelectedSavedSearch);
        RenameSelectedSavedSearchCommand = new AsyncRelayCommand(RenameSelectedSavedSearchAsync, CanRenameSelectedSavedSearch);
        RemoveSelectedSavedSearchCommand = new AsyncRelayCommand(RemoveSelectedSavedSearchAsync, CanRemoveSelectedSavedSearch);
        RequestResetSavedSearchesCommand = new RelayCommand(RequestResetSavedSearches, () => !IsSavedSearchBusy && _savedSearchStore is not null);
        ConfirmResetSavedSearchesCommand = new AsyncRelayCommand(ConfirmResetSavedSearchesAsync, () => !IsSavedSearchBusy && IsSavedSearchResetPending && _savedSearchStore is not null);
        CancelResetSavedSearchesCommand = new RelayCommand(CancelResetSavedSearches, () => IsSavedSearchResetPending);
    }

    /// <summary>Raised after a selected saved entry has been resolved for shell-owned results presentation.</summary>
    public event EventHandler<CatalogEntry>? EntryOpened;

    /// <summary>Gets the bounded local search-hit collection.</summary>
    public ReadOnlyObservableCollection<CatalogSearchHitRow> Hits { get; }

    /// <summary>Gets bounded named catalog query presets in newest-updated order.</summary>
    public ReadOnlyObservableCollection<SavedCatalogSearchRow> SavedSearches { get; }

    /// <summary>Gets or sets the catalog metadata query text.</summary>
    public string? QueryText
    {
        get => _queryText;
        set
        {
            if (SetProperty(ref _queryText, value))
            {
                IsSearchCompleted = false;
                OnPropertyChanged(nameof(HasQuery));
                OnPropertyChanged(nameof(HasNoHits));
                ClearQueryCommand.NotifyCanExecuteChanged();
                SaveCurrentSearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the display name used to save the current catalog query.</summary>
    public string? SavedSearchName
    {
        get => _savedSearchName;
        set
        {
            if (SetProperty(ref _savedSearchName, value))
            {
                SaveCurrentSearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the selected saved query preset.</summary>
    public SavedCatalogSearchRow? SelectedSavedSearch
    {
        get => _selectedSavedSearch;
        set
        {
            if (SetProperty(ref _selectedSavedSearch, value))
            {
                SavedSearchRenameText = value?.Name;
                OnPropertyChanged(nameof(HasSelectedSavedSearch));
                RunSelectedSavedSearchCommand.NotifyCanExecuteChanged();
                RenameSelectedSavedSearchCommand.NotifyCanExecuteChanged();
                RemoveSelectedSavedSearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether a saved query is selected for its secondary actions.</summary>
    public bool HasSelectedSavedSearch => SelectedSavedSearch is not null;

    /// <summary>Gets or sets the edited display name for the selected saved query.</summary>
    public string? SavedSearchRenameText
    {
        get => _savedSearchRenameText;
        set
        {
            if (SetProperty(ref _savedSearchRenameText, value))
            {
                RenameSelectedSavedSearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the selected bounded search hit.</summary>
    public CatalogSearchHitRow? SelectedHit
    {
        get => _selectedHit;
        set
        {
            if (SetProperty(ref _selectedHit, value))
            {
                OpenSelectedHitCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether catalog storage is enabled in the active settings.</summary>
    public bool IsEnabled => _configurationService.Current.Catalog.Enabled;

    /// <summary>Gets whether a non-empty search query is present.</summary>
    public bool HasQuery => !string.IsNullOrWhiteSpace(QueryText);

    /// <summary>Gets whether catalog search evaluation is running.</summary>
    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                OnPropertyChanged(nameof(Status));
                SearchCommand.NotifyCanExecuteChanged();
                OpenSelectedHitCommand.NotifyCanExecuteChanged();
                SaveCurrentSearchCommand.NotifyCanExecuteChanged();
                RunSelectedSavedSearchCommand.NotifyCanExecuteChanged();
                ClearQueryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether saved-query persistence work is running.</summary>
    public bool IsSavedSearchBusy
    {
        get => _isSavedSearchBusy;
        private set
        {
            if (SetProperty(ref _isSavedSearchBusy, value))
            {
                RefreshSavedSearchesCommand.NotifyCanExecuteChanged();
                SaveCurrentSearchCommand.NotifyCanExecuteChanged();
                RunSelectedSavedSearchCommand.NotifyCanExecuteChanged();
                RenameSelectedSavedSearchCommand.NotifyCanExecuteChanged();
                RemoveSelectedSavedSearchCommand.NotifyCanExecuteChanged();
                RequestResetSavedSearchesCommand.NotifyCanExecuteChanged();
                ConfirmResetSavedSearchesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether no named catalog queries are currently available.</summary>
    public bool HasNoSavedSearches => SavedSearches.Count == 0;

    /// <summary>Gets whether explicit reset confirmation is pending.</summary>
    public bool IsSavedSearchResetPending
    {
        get => _isSavedSearchResetPending;
        private set
        {
            if (SetProperty(ref _isSavedSearchResetPending, value))
            {
                ConfirmResetSavedSearchesCommand.NotifyCanExecuteChanged();
                CancelResetSavedSearchesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets a user-safe saved-query persistence status.</summary>
    public string SavedSearchStatusText
    {
        get => _savedSearchStatusText;
        private set => SetProperty(ref _savedSearchStatusText, value);
    }

    /// <summary>Gets whether the completed latest query has no matching saved metadata.</summary>
    public bool HasNoHits => IsSearchCompleted && !IsSearching && Hits.Count == 0;

    /// <summary>Gets whether the latest completed query produced visible results.</summary>
    public bool HasSearchResults => Hits.Count > 0;

    /// <summary>Gets whether the current query has completed at least once.</summary>
    public bool IsSearchCompleted
    {
        get => _isSearchCompleted;
        private set
        {
            if (SetProperty(ref _isSearchCompleted, value))
            {
                OnPropertyChanged(nameof(HasNoHits));
            }
        }
    }

    /// <summary>Gets a concise result count for the current bounded result set.</summary>
    public string ResultCountText => Hits.Count == 1 ? "1 result" : $"{Hits.Count} results";

    /// <summary>Gets current user-safe search status.</summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    /// <summary>Gets the accessible presentation for the current catalog-search status.</summary>
    public StatusPresentation Status => IsSearching
        ? StatusPresentation.Progress(StatusText)
        : new StatusPresentation(_statusKind, StatusText);

    /// <summary>Gets the command that evaluates the current local catalog query.</summary>
    public IAsyncRelayCommand SearchCommand { get; }

    /// <summary>Gets the command that clears the query and its transient results.</summary>
    public IRelayCommand ClearQueryCommand { get; }

    /// <summary>Gets the command that opens the saved entry containing the selected hit.</summary>
    public IAsyncRelayCommand OpenSelectedHitCommand { get; }

    /// <summary>Gets the command that refreshes bounded saved query presets.</summary>
    public IAsyncRelayCommand RefreshSavedSearchesCommand { get; }

    /// <summary>Gets the command that saves the current non-empty catalog query under a distinct name.</summary>
    public IAsyncRelayCommand SaveCurrentSearchCommand { get; }

    /// <summary>Gets the command that reruns the selected preset through the current catalog search workflow.</summary>
    public IAsyncRelayCommand RunSelectedSavedSearchCommand { get; }

    /// <summary>Gets the command that renames the selected preset without changing its query.</summary>
    public IAsyncRelayCommand RenameSelectedSavedSearchCommand { get; }

    /// <summary>Gets the command that removes one selected application-owned query preset.</summary>
    public IAsyncRelayCommand RemoveSelectedSavedSearchCommand { get; }

    /// <summary>Gets the first-step command that requests reset confirmation.</summary>
    public IRelayCommand RequestResetSavedSearchesCommand { get; }

    /// <summary>Gets the command that confirms reset of only saved-query application data.</summary>
    public IAsyncRelayCommand ConfirmResetSavedSearchesCommand { get; }

    /// <summary>Gets the command that cancels pending reset without storage access.</summary>
    public IRelayCommand CancelResetSavedSearchesCommand { get; }

    /// <summary>
    /// Loads named query presets without enumerating or opening catalog snapshots.
    /// </summary>
    public async Task RefreshSavedSearchesAsync()
    {
        OnPropertyChanged(nameof(IsEnabled));
        SaveCurrentSearchCommand.NotifyCanExecuteChanged();
        RunSelectedSavedSearchCommand.NotifyCanExecuteChanged();
        if (_savedSearchStore is null)
        {
            SavedSearchStatusText = "Saved catalog searches are unavailable in this application configuration.";
            return;
        }

        var selectedId = SelectedSavedSearch?.Id;
        var (cancellation, version) = BeginSavedSearchOperation();
        SavedSearchStatusText = "Loading saved catalog searches...";
        try
        {
            var searches = await _savedSearchStore.ListAsync(cancellation.Token);
            if (!IsCurrentSavedSearchOperation(cancellation, version))
            {
                return;
            }

            PublishSavedSearches(searches, selectedId);
            SavedSearchStatusText = SavedSearches.Count == 0
                ? "No catalog searches are saved. Enter a name and non-empty query to create one."
                : $"{SavedSearches.Count} saved catalog search(es) are available. Query text is stored locally; search hits are not.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "Saved-search refresh was cancelled. The last valid list remains available.";
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "Saved catalog searches could not be read. Existing ad hoc search remains available; use the separate reset confirmation to recover corrupted saved-query data.";
            }
        }
        finally
        {
            EndSavedSearchOperation(cancellation, version);
        }
    }

    /// <summary>Saves the current catalog query as a new named local preset.</summary>
    public async Task SaveCurrentSearchAsync()
    {
        var name = string.IsNullOrWhiteSpace(SavedSearchName) ? null : SavedSearchName.Trim();
        var query = string.IsNullOrWhiteSpace(QueryText) ? null : QueryText.Trim();
        if (!IsEnabled)
        {
            SavedSearchStatusText = "Enable local catalog storage before saving a catalog search. Existing saved query text can still be removed.";
            return;
        }

        if (_savedSearchStore is null)
        {
            SavedSearchStatusText = "Saved catalog searches are unavailable in this application configuration.";
            return;
        }

        if (name is null || query is null)
        {
            SavedSearchStatusText = "Enter both a saved-search name and a non-empty catalog query.";
            return;
        }

        if (name.Length > SavedCatalogSearchLimits.MaximumNameLength || name.Any(char.IsControl))
        {
            SavedSearchStatusText = $"Saved-search names must be no longer than {SavedCatalogSearchLimits.MaximumNameLength} characters and contain no control characters.";
            return;
        }

        if (query.Length > SavedCatalogSearchLimits.MaximumQueryLength || query.Any(character => char.IsControl(character) && character is not '\t' and not '\r' and not '\n'))
        {
            SavedSearchStatusText = $"Saved catalog queries must be no longer than {SavedCatalogSearchLimits.MaximumQueryLength} characters.";
            return;
        }

        if (SavedSearches.Any(search => string.Equals(search.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SavedSearchStatusText = "A saved catalog search already uses that name. Choose a distinct name or remove the existing preset.";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var search = new SavedCatalogSearch($"saved-search:{Guid.NewGuid():N}", name, query, now, now);
        var (cancellation, version) = BeginSavedSearchOperation();
        SavedSearchStatusText = "Saving catalog search...";
        try
        {
            var saved = await _savedSearchStore.SaveAsync(search, cancellation.Token);
            var searches = await _savedSearchStore.ListAsync(cancellation.Token);
            if (!IsCurrentSavedSearchOperation(cancellation, version))
            {
                return;
            }

            PublishSavedSearches(searches, saved.Id);
            SavedSearchName = null;
            SavedSearchStatusText = "Catalog search saved locally. Only its name and query text were stored; no hits or file contents were saved.";
        }
        catch (SavedCatalogSearchCapacityExceededException)
        {
            SavedSearchStatusText = $"At most {SavedCatalogSearchLimits.MaximumSearchCount} catalog searches can be saved. Remove one before adding another.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "Saving the catalog search was cancelled.";
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "The catalog search could not be saved. Existing saved searches and current hits remain available.";
            }
        }
        finally
        {
            EndSavedSearchOperation(cancellation, version);
        }
    }

    /// <summary>Runs the selected named query through the existing current-catalog search workflow.</summary>
    public async Task RunSelectedSavedSearchAsync()
    {
        if (SelectedSavedSearch is null)
        {
            SavedSearchStatusText = "Select a saved catalog search to run.";
            return;
        }

        if (!IsEnabled)
        {
            SavedSearchStatusText = "Local catalog storage is disabled. Saved query text can be reviewed or removed, but it cannot be run.";
            return;
        }

        var name = SelectedSavedSearch.Name;
        QueryText = SelectedSavedSearch.QueryText;
        SavedSearchStatusText = $"Running saved search “{name}” against current catalog metadata...";
        await SearchAsync();
        SavedSearchStatusText = $"Saved search “{name}” ran against the current catalog. Search hits were not persisted.";
    }

    /// <summary>Removes only the selected application-owned query preset.</summary>
    public async Task RemoveSelectedSavedSearchAsync()
    {
        var selected = SelectedSavedSearch;
        if (selected is null || _savedSearchStore is null)
        {
            return;
        }

        var (cancellation, version) = BeginSavedSearchOperation();
        SavedSearchStatusText = "Removing selected saved search...";
        try
        {
            var removed = await _savedSearchStore.RemoveAsync(selected.Id, cancellation.Token);
            var searches = await _savedSearchStore.ListAsync(cancellation.Token);
            if (!IsCurrentSavedSearchOperation(cancellation, version))
            {
                return;
            }

            PublishSavedSearches(searches, null);
            SavedSearchStatusText = removed
                ? "The selected saved query was removed. Catalog snapshots, current hits, and scanned files were not changed."
                : "The selected saved query was no longer available. The list has been refreshed.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "Removing the saved query was cancelled.";
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "The selected saved query could not be removed. Existing data remains available.";
            }
        }
        finally
        {
            EndSavedSearchOperation(cancellation, version);
        }
    }

    /// <summary>Renames the selected application-owned query while preserving its identity and query text.</summary>
    public async Task RenameSelectedSavedSearchAsync()
    {
        var selected = SelectedSavedSearch;
        var name = string.IsNullOrWhiteSpace(SavedSearchRenameText) ? null : SavedSearchRenameText.Trim();
        if (selected is null || _savedSearchStore is null || name is null)
        {
            SavedSearchStatusText = "Select a saved search and enter a new name.";
            return;
        }

        if (name.Length > SavedCatalogSearchLimits.MaximumNameLength || name.Any(char.IsControl))
        {
            SavedSearchStatusText = $"Saved-search names must be no longer than {SavedCatalogSearchLimits.MaximumNameLength} characters and contain no control characters.";
            return;
        }

        if (SavedSearches.Any(search =>
                !string.Equals(search.Id, selected.Id, StringComparison.Ordinal) &&
                string.Equals(search.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SavedSearchStatusText = "Another saved catalog search already uses that name.";
            return;
        }

        if (string.Equals(selected.Name, name, StringComparison.Ordinal))
        {
            SavedSearchStatusText = "The saved-search name is unchanged.";
            return;
        }

        var updated = new SavedCatalogSearch(
            selected.Id,
            name,
            selected.QueryText,
            selected.CreatedAtUtc,
            DateTimeOffset.UtcNow);
        var (cancellation, version) = BeginSavedSearchOperation();
        SavedSearchStatusText = "Renaming saved search...";
        try
        {
            var saved = await _savedSearchStore.SaveAsync(updated, cancellation.Token);
            var searches = await _savedSearchStore.ListAsync(cancellation.Token);
            if (!IsCurrentSavedSearchOperation(cancellation, version))
            {
                return;
            }

            PublishSavedSearches(searches, saved.Id);
            SavedSearchStatusText = "Saved search renamed. Its query and saved catalog data were not changed.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "Renaming the saved search was cancelled.";
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "The saved search could not be renamed. Existing data was preserved.";
            }
        }
        finally
        {
            EndSavedSearchOperation(cancellation, version);
        }
    }

    private bool CanSaveCurrentSearch() =>
        _savedSearchStore is not null && IsEnabled && !IsSavedSearchBusy && !IsSearching &&
        !string.IsNullOrWhiteSpace(SavedSearchName) && !string.IsNullOrWhiteSpace(QueryText);

    private bool CanRunSelectedSavedSearch() =>
        IsEnabled && !IsSavedSearchBusy && !IsSearching && SelectedSavedSearch is not null;

    private bool CanRenameSelectedSavedSearch() =>
        _savedSearchStore is not null &&
        !IsSavedSearchBusy &&
        SelectedSavedSearch is not null &&
        !string.IsNullOrWhiteSpace(SavedSearchRenameText);

    private bool CanRemoveSelectedSavedSearch() =>
        _savedSearchStore is not null && !IsSavedSearchBusy && SelectedSavedSearch is not null;

    private void ClearQuery()
    {
        CancelSearch();
        _hits.Clear();
        _entriesById = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        SelectedHit = null;
        QueryText = null;
        IsSearchCompleted = false;
        NotifyHitStateChanged();
        SetSearchStatus(
            StatusKind.Information,
            "Enter text to search filenames, paths, extensions, categories, and accepted tags in saved snapshots.");
    }

    private void RequestResetSavedSearches()
    {
        IsSavedSearchResetPending = true;
        SavedSearchStatusText = "Reset requested. Confirm separately to remove only OpenSorSe saved query text, including malformed saved-search data.";
    }

    private void CancelResetSavedSearches()
    {
        IsSavedSearchResetPending = false;
        SavedSearchStatusText = "Saved-search reset cancelled. No application data was changed.";
    }

    private async Task ConfirmResetSavedSearchesAsync()
    {
        if (_savedSearchStore is null || !IsSavedSearchResetPending)
        {
            return;
        }

        var (cancellation, version) = BeginSavedSearchOperation();
        SavedSearchStatusText = "Resetting saved catalog searches...";
        try
        {
            await _savedSearchStore.ClearAsync(cancellation.Token);
            if (!IsCurrentSavedSearchOperation(cancellation, version))
            {
                return;
            }

            PublishSavedSearches([], null);
            IsSavedSearchResetPending = false;
            SavedSearchStatusText = "All saved query text was removed from OpenSorSe application data. Catalog snapshots and scanned files were not changed.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "Saved-search reset was cancelled. Existing data was preserved.";
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _savedSearchVersion))
            {
                SavedSearchStatusText = "Saved catalog searches could not be reset. Existing application data was preserved.";
            }
        }
        finally
        {
            EndSavedSearchOperation(cancellation, version);
        }
    }

    /// <summary>
    /// Evaluates the current query across saved snapshots and publishes only the latest bounded result set.
    /// </summary>
    public async Task SearchAsync()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OpenSelectedHitCommand.NotifyCanExecuteChanged();
        var text = string.IsNullOrWhiteSpace(QueryText) ? null : QueryText.Trim();
        if (text is null)
        {
            CancelSearch();
            _hits.Clear();
            _entriesById = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
            SelectedHit = null;
            IsSearchCompleted = false;
            NotifyHitStateChanged();
            SetSearchStatus(
                StatusKind.Information,
                "Enter text to search filenames, paths, extensions, categories, and accepted tags in saved snapshots.");
            return;
        }

        if (!IsEnabled)
        {
            CancelSearch();
            _hits.Clear();
            _entriesById = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
            SelectedHit = null;
            IsSearchCompleted = false;
            NotifyHitStateChanged();
            SetSearchStatus(
                StatusKind.Warning,
                "Local catalog storage is disabled. Enable it in Settings before searching saved snapshots.");
            return;
        }

        if (_catalogStore is null)
        {
            IsSearchCompleted = false;
            SetSearchStatus(StatusKind.Error, "The local catalog is unavailable in this application configuration.");
            return;
        }

        var cancellation = ReplaceSearchCancellation();
        var version = Interlocked.Increment(ref _searchVersion);
        IsSearching = true;
        SetSearchStatus(StatusKind.Progress, "Searching saved catalog metadata…");
        try
        {
            var summaries = await _catalogStore.ListAsync(cancellation.Token);
            var entriesById = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
            var hits = new List<CatalogSearchHitRow>();
            foreach (var summary in summaries)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var entry = await _catalogStore.LoadAsync(summary.Id, cancellation.Token);
                if (entry is null)
                {
                    continue;
                }

                var tagsByFile = BuildTagsByFile(entry);
                var query = ResultsQuery.Default with { Text = text, PageSize = 500 };
                ResultsQueryResult result;
                do
                {
                    result = await Task.Run(
                        () => ResultsQueryEngine.Evaluate(entry.Snapshot, query, cancellation.Token, tagsByFile),
                        cancellation.Token);
                    foreach (var file in result.Page.Items)
                    {
                        if (result.Page.Matches.TryGetValue(file.Id, out var match))
                        {
                            hits.Add(CatalogSearchHitRow.Create(entry.Id, entry.SavedAtUtc, entry.DisplayName, file, match));
                        }
                    }

                    query = result.Query with { PageIndex = result.Page.PageIndex + 1 };
                }
                while (result.Page.PageIndex + 1 < result.Page.TotalPageCount);

                entriesById[entry.Id] = entry;
            }

            if (cancellation.IsCancellationRequested || version != Volatile.Read(ref _searchVersion))
            {
                return;
            }

            var published = hits
                .OrderByDescending(hit => hit.MatchScore)
                .ThenByDescending(hit => hit.SavedAtUtc)
                .ThenBy(hit => hit.FileName, StringComparer.Ordinal)
                .ThenBy(hit => hit.FullPath, StringComparer.Ordinal)
                .ThenBy(hit => hit.CatalogEntryId, StringComparer.Ordinal)
                .ThenBy(hit => hit.FileId, StringComparer.Ordinal)
                .Take(MaximumPublishedHitCount)
                .ToArray();
            _hits.Clear();
            foreach (var hit in published)
            {
                _hits.Add(hit);
            }

            _entriesById = published
                .Select(hit => hit.CatalogEntryId)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(id => id, id => entriesById[id], StringComparer.Ordinal);
            SelectedHit = null;
            IsSearchCompleted = true;
            NotifyHitStateChanged();
            var wasBounded = hits.Count > MaximumPublishedHitCount;
            SetSearchStatus(
                published.Length == 0
                    ? StatusKind.Information
                    : wasBounded
                        ? StatusKind.Warning
                        : StatusKind.Success,
                published.Length == 0
                    ? "No saved catalog metadata matches this query."
                    : wasBounded
                        ? $"Showing the top {MaximumPublishedHitCount} of {hits.Count} matching saved catalog files."
                        : $"{published.Length} saved catalog file match(es) found.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _searchVersion))
            {
                SetSearchStatus(StatusKind.Error, "The local catalog could not be searched. Existing results remain available.");
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _searchVersion))
            {
                IsSearching = false;
                NotifyHitStateChanged();
            }

            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>Releases pending catalog search work.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        CancelSearch();
        CancelSavedSearchOperation();
        _isDisposed = true;
    }

    /// <summary>
    /// Clears cached historical hits after another surface changes application-owned catalog data.
    /// </summary>
    public void InvalidateResults()
    {
        if (_isDisposed)
        {
            return;
        }

        CancelSearch();
        _hits.Clear();
        _entriesById = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        SelectedHit = null;
        IsSearchCompleted = false;
        NotifyHitStateChanged();
        SetSearchStatus(
            StatusKind.Information,
            "Saved catalog data changed. Run the search again to review current historical snapshots.");
    }

    private async Task OpenSelectedHitAsync()
    {
        if (SelectedHit is null || _catalogStore is null || !IsEnabled)
        {
            return;
        }

        CatalogEntry? entry;
        try
        {
            entry = await _catalogStore.LoadAsync(SelectedHit.CatalogEntryId, CancellationToken.None);
        }
        catch (Exception)
        {
            SetSearchStatus(StatusKind.Error, "The selected saved snapshot could not be opened. Existing in-memory results remain available.");
            return;
        }

        if (entry is null)
        {
            SetSearchStatus(StatusKind.Warning, "The selected saved snapshot is no longer available. Run the search again.");
            return;
        }

        EntryOpened?.Invoke(this, entry);
        SetSearchStatus(StatusKind.Success, "Saved snapshot opened. It has not been refreshed from the filesystem.");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TagAssociation>> BuildTagsByFile(CatalogEntry entry)
    {
        var tagsByFile = entry.AcceptedTags
            .Where(tag => tag.AcceptanceState == TagAcceptanceState.Accepted)
            .GroupBy(tag => tag.FileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TagAssociation>)Array.AsReadOnly(group.ToArray()), StringComparer.Ordinal);
        foreach (var file in entry.Snapshot.Files)
        {
            var extension = file.NormalizedExtension.TrimStart('.');
            if (extension.Length == 0)
            {
                continue;
            }

            var deterministic = new TagAssociation(
                $"tag:{file.Id}:extension:{extension}",
                file.Id,
                extension,
                extension.ToLowerInvariant(),
                "File type",
                TagSource.Deterministic,
                TagAcceptanceState.Accepted,
                "Derived from the saved file extension.",
                entry.SavedAtUtc);
            var existing = tagsByFile.TryGetValue(file.Id, out var tags) ? tags : Array.Empty<TagAssociation>();
            tagsByFile[file.Id] = Array.AsReadOnly(existing.Append(deterministic).ToArray());
        }

        return tagsByFile;
    }

    private CancellationTokenSource ReplaceSearchCancellation()
    {
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchCancellation, current);
        previous?.Cancel();
        return current;
    }

    private void CancelSearch()
    {
        Interlocked.Increment(ref _searchVersion);
        var cancellation = Interlocked.Exchange(ref _searchCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        IsSearching = false;
    }

    private void NotifyHitStateChanged()
    {
        OnPropertyChanged(nameof(HasNoHits));
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(ResultCountText));
        ClearQueryCommand.NotifyCanExecuteChanged();
    }

    private void SetSearchStatus(StatusKind kind, string message)
    {
        _statusKind = kind;
        StatusText = message;
        OnPropertyChanged(nameof(Status));
    }

    private (CancellationTokenSource Cancellation, long Version) BeginSavedSearchOperation()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _savedSearchCancellation, cancellation);
        previous?.Cancel();
        var version = Interlocked.Increment(ref _savedSearchVersion);
        IsSavedSearchBusy = true;
        return (cancellation, version);
    }

    private bool IsCurrentSavedSearchOperation(CancellationTokenSource cancellation, long version) =>
        !cancellation.IsCancellationRequested &&
        ReferenceEquals(_savedSearchCancellation, cancellation) &&
        version == Volatile.Read(ref _savedSearchVersion);

    private void EndSavedSearchOperation(CancellationTokenSource cancellation, long version)
    {
        if (ReferenceEquals(_savedSearchCancellation, cancellation))
        {
            _savedSearchCancellation = null;
        }

        if (version == Volatile.Read(ref _savedSearchVersion))
        {
            IsSavedSearchBusy = false;
        }

        cancellation.Dispose();
    }

    private void CancelSavedSearchOperation()
    {
        Interlocked.Increment(ref _savedSearchVersion);
        var cancellation = Interlocked.Exchange(ref _savedSearchCancellation, null);
        cancellation?.Cancel();
        IsSavedSearchBusy = false;
    }

    private void PublishSavedSearches(IReadOnlyList<SavedCatalogSearch> searches, string? selectedId)
    {
        _savedSearches.Clear();
        foreach (var search in searches)
        {
            _savedSearches.Add(SavedCatalogSearchRow.FromModel(search));
        }

        SelectedSavedSearch = selectedId is null
            ? null
            : SavedSearches.FirstOrDefault(search => string.Equals(search.Id, selectedId, StringComparison.Ordinal));
        OnPropertyChanged(nameof(HasNoSavedSearches));
        RequestResetSavedSearchesCommand.NotifyCanExecuteChanged();
    }
}
