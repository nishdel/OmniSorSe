using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.Services;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents bounded local Semantic Search Beta indexing and explained hybrid results.</summary>
public sealed class SemanticSearchViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly ISemanticIndexer? _indexer;
    private readonly ISemanticSearchService? _searchService;
    private readonly ISemanticIndexStore? _indexStore;
    private readonly IExternalFileLauncher? _launcher;
    private readonly ObservableCollection<SemanticSearchHit> _hits = [];
    private CancellationTokenSource? _operationCancellation;
    private string? _queryText;
    private bool _isBusy;
    private bool _isClearPending;
    private double _progressValue;
    private string _progressText = "Index not inspected.";
    private StatusPresentation _status = StatusPresentation.Information("Build the local index, then enter a search phrase.");

    /// <summary>Initializes a preview instance with Semantic Search unavailable.</summary>
    public SemanticSearchViewModel()
        : this(new PreviewConfiguration(), null, null, null, null)
    {
    }

    /// <summary>Initializes the local semantic-search presentation model.</summary>
    public SemanticSearchViewModel(
        IConfigurationService configurationService,
        ISemanticIndexer? indexer,
        ISemanticSearchService? searchService,
        ISemanticIndexStore? indexStore,
        IExternalFileLauncher? launcher)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _indexer = indexer;
        _searchService = searchService;
        _indexStore = indexStore;
        _launcher = launcher;
        Hits = new ReadOnlyObservableCollection<SemanticSearchHit>(_hits);
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
    }

    /// <summary>Gets or sets the bounded natural-language query.</summary>
    public string? QueryText
    {
        get => _queryText;
        set
        {
            if (SetProperty(ref _queryText, value))
            {
                SearchCommand.NotifyCanExecuteChanged();
                ClearQueryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets local explained results.</summary>
    public ReadOnlyObservableCollection<SemanticSearchHit> Hits { get; }

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

    /// <summary>Gets consistent local semantic status.</summary>
    public StatusPresentation Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
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

    /// <summary>Refreshes command availability after persisted feature settings change.</summary>
    public void RefreshFeatureAvailability() => NotifyCommands();

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

        using var operation = BeginOperation();
        try
        {
            var result = await _searchService.SearchAsync(QueryText ?? string.Empty, operation.Token);
            _hits.Clear();
            foreach (var hit in result.Value)
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
        ProgressText = rebuild ? "Rebuilding local semantic index..." : "Refreshing local semantic index...";
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
        OnPropertyChanged(nameof(HasHits));
        Status = StatusPresentation.Information("Query cleared. The local index was not changed.");
    }

    private void RequestClearIndex()
    {
        IsClearPending = true;
        Status = StatusPresentation.Warning("Confirm clearing only the application-owned semantic index. Source files remain untouched.");
    }

    private async Task ConfirmClearIndexAsync()
    {
        if (_indexStore is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _indexStore.ClearAsync(CancellationToken.None);
            _hits.Clear();
            OnPropertyChanged(nameof(HasHits));
            IsClearPending = false;
            ProgressValue = 0;
            ProgressText = "Local semantic index is empty.";
            Status = StatusPresentation.Success("Local semantic index cleared. Source files were not changed.");
        }
        catch (Exception)
        {
            Status = StatusPresentation.Error("The local semantic index could not be cleared.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelClearIndex()
    {
        IsClearPending = false;
        Status = StatusPresentation.Information("Index clear cancelled.");
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
    }

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
