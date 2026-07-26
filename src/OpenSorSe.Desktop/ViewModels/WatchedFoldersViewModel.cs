#pragma warning disable CS1591

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.ViewModels;

public sealed class WatchedFolderRow : ViewModelBase
{
    public WatchedFolderRow(WatchedFolderConfiguration configuration)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public WatchedFolderConfiguration Configuration { get; }
    public string Id => Configuration.Id;
    public string DisplayName => Configuration.DisplayName;
    public string FolderPath => Configuration.FolderPath;
    public string StateText => Configuration.IsEnabled
        ? Configuration.Status.ToString()
        : "Paused";
    public string AvailabilityText => Configuration.Status switch
    {
        WatchedFolderStatus.Unavailable => "Unavailable",
        WatchedFolderStatus.Inaccessible => "Access denied",
        WatchedFolderStatus.ProfileUnavailable => "Profile unavailable — review configuration",
        _ => "Available",
    };
    public string QueueText => Configuration.QueuedChangeCount == 0
        ? "Queue empty"
        : $"{Configuration.QueuedChangeCount} queued";
    public string LastDetectedText => FormatTime(Configuration.LastDetectedChangeUtc, "No changes detected");
    public string LastScanText => FormatTime(Configuration.LastSuccessfulScanUtc, "Not scanned yet");
    public string LastReconciliationText => FormatTime(Configuration.LastReconciliationUtc, "Not reconciled yet");
    public string Summary => Configuration.LatestSummary ?? "No activity summary is available.";
    public string? Error => Configuration.LastError;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsEnabled => Configuration.IsEnabled;
    public int PendingPlanCount => Configuration.PendingChangePlanCount;
    public string PendingPlanText => PendingPlanCount == 1
        ? "1 pending Change Plan"
        : $"{PendingPlanCount} pending Change Plans";

    private static string FormatTime(DateTimeOffset? value, string fallback) =>
        value is null ? fallback : value.Value.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
}

public sealed class WatchedRecipeChoice : ViewModelBase
{
    private bool _isSelected;

    public WatchedRecipeChoice(SortingRecipe recipe, bool isSelected)
    {
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        _isSelected = isSelected;
    }

    public SortingRecipe Recipe { get; }
    public string Id => Recipe.Id;
    public string Name => Recipe.Name;
    public string StateText => Recipe.IsBuiltIn ? "Built-in" : $"Revision {Recipe.Revision}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class WatchedFoldersViewModel : ViewModelBase, IDisposable
{
    private readonly IWatchedFolderManager? _manager;
    private readonly IWatchedFolderCoordinator? _coordinator;
    private readonly IExternalFileLauncher? _externalLauncher;
    private readonly IChangePlanStore? _changePlanStore;
    private readonly IWorkflowLibraryService? _workflowLibrary;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly ObservableCollection<WatchedFolderRow> _folders = [];
    private readonly ObservableCollection<WatchedActivityEntry> _recentActivity = [];
    private readonly ObservableCollection<WorkflowProfile> _workflowProfiles = [];
    private readonly ObservableCollection<WatchedRecipeChoice> _workflowRecipes = [];
    private WatchedFolderRow? _selectedFolder;
    private WorkflowProfile? _selectedWorkflowProfile;
    private bool _isBusy;
    private bool _isRemoveConfirmationPending;
    private string _statusText = "Add a folder to monitor changes without automatic file modification.";
    private string _folderPath = string.Empty;
    private string _displayName = string.Empty;
    private bool _includeSubfolders = true;
    private bool _deterministicAnalysisEnabled = true;
    private bool _aiAnalysisEnabled;
    private bool _ignoreHiddenFiles = true;
    private double _quietPeriodSeconds = WatchedFolderLimits.DefaultQuietPeriod.TotalSeconds;
    private double _maximumFileSizeMiB = 1024;
    private string _scanProfileId = "default";
    private string _sortingRecipeId = string.Empty;
    private string _ignoredPathsText = string.Empty;
    private string _ignorePatternsText = string.Empty;
    private WatchedFolderNotificationLevel _notificationLevel = WatchedFolderNotificationLevel.Summaries;
    private bool _notifyWhenPlanReady = true;
    private bool _notifyWhenUnavailable = true;
    private readonly Dictionary<string, string> _lastNotificationKeys = new(StringComparer.Ordinal);

    public WatchedFoldersViewModel(
        IWatchedFolderManager? manager = null,
        IWatchedFolderCoordinator? coordinator = null,
        IExternalFileLauncher? externalLauncher = null,
        IChangePlanStore? changePlanStore = null,
        IWorkflowLibraryService? workflowLibrary = null)
    {
        _manager = manager;
        _coordinator = coordinator;
        _externalLauncher = externalLauncher;
        _changePlanStore = changePlanStore;
        _workflowLibrary = workflowLibrary;
        _synchronizationContext = SynchronizationContext.Current;
        Folders = new ReadOnlyObservableCollection<WatchedFolderRow>(_folders);
        RecentActivity = new ReadOnlyObservableCollection<WatchedActivityEntry>(_recentActivity);
        WorkflowProfiles = new ReadOnlyObservableCollection<WorkflowProfile>(_workflowProfiles);
        WorkflowRecipes = new ReadOnlyObservableCollection<WatchedRecipeChoice>(_workflowRecipes);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && _manager is not null);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync, CanAdd);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanActOnSelection);
        PauseCommand = new AsyncRelayCommand(PauseAsync, () => CanActOnSelection() && SelectedFolder!.IsEnabled);
        ResumeCommand = new AsyncRelayCommand(ResumeAsync, () => CanActOnSelection() && !SelectedFolder!.IsEnabled);
        ScanChangesNowCommand = new AsyncRelayCommand(ScanChangesNowAsync, CanActOnEnabledSelection);
        FullReconciliationCommand = new AsyncRelayCommand(ReconcileAsync, CanActOnEnabledSelection);
        RetryAiCommand = new AsyncRelayCommand(RetryAiAsync, CanRetryAi);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, CanActOnSelection);
        ReviewSuggestionsCommand = new AsyncRelayCommand(ReviewSuggestionsAsync, CanReviewSuggestions);
        RequestRemoveCommand = new RelayCommand(
            () => IsRemoveConfirmationPending = true,
            CanActOnSelection);
        ConfirmRemoveCommand = new AsyncRelayCommand(RemoveAsync, () => CanActOnSelection() && IsRemoveConfirmationPending);
        CancelRemoveCommand = new RelayCommand(
            () => IsRemoveConfirmationPending = false,
            () => IsRemoveConfirmationPending && !IsBusy);
        if (_coordinator is not null)
        {
            _coordinator.StateChanged += OnCoordinatorStateChanged;
            _coordinator.ActivityPublished += OnActivityPublished;
        }
    }

    public event EventHandler<ChangePlan>? ReviewPlanRequested;
    public event EventHandler<NotificationRequest>? NotificationRequested;
    public ReadOnlyObservableCollection<WatchedFolderRow> Folders { get; }
    public ReadOnlyObservableCollection<WatchedActivityEntry> RecentActivity { get; }
    public ReadOnlyObservableCollection<WorkflowProfile> WorkflowProfiles { get; }
    public ReadOnlyObservableCollection<WatchedRecipeChoice> WorkflowRecipes { get; }
    public IReadOnlyList<WatchedFolderNotificationLevel> NotificationLevels { get; } =
        Enum.GetValues<WatchedFolderNotificationLevel>();
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand AddFolderCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand PauseCommand { get; }
    public IAsyncRelayCommand ResumeCommand { get; }
    public IAsyncRelayCommand ScanChangesNowCommand { get; }
    public IAsyncRelayCommand FullReconciliationCommand { get; }
    public IAsyncRelayCommand RetryAiCommand { get; }
    public IAsyncRelayCommand OpenFolderCommand { get; }
    public IAsyncRelayCommand ReviewSuggestionsCommand { get; }
    public IRelayCommand RequestRemoveCommand { get; }
    public IAsyncRelayCommand ConfirmRemoveCommand { get; }
    public IRelayCommand CancelRemoveCommand { get; }

    public WatchedFolderRow? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                IsRemoveConfirmationPending = false;
                LoadEditor(value?.Configuration);
                RefreshCommandStates();
            }
        }
    }

    public bool HasFolders => Folders.Count > 0;
    public bool HasSelection => SelectedFolder is not null;
    public bool IsConfigurationAvailable => _manager is not null && _coordinator is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public bool IsRemoveConfirmationPending
    {
        get => _isRemoveConfirmationPending;
        private set
        {
            if (SetProperty(ref _isRemoveConfirmationPending, value))
            {
                ConfirmRemoveCommand.NotifyCanExecuteChanged();
                CancelRemoveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetProperty(ref _folderPath, value))
            {
                AddFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public bool DeterministicAnalysisEnabled
    {
        get => _deterministicAnalysisEnabled;
        set => SetProperty(ref _deterministicAnalysisEnabled, value);
    }

    public bool AiAnalysisEnabled
    {
        get => _aiAnalysisEnabled;
        set => SetProperty(ref _aiAnalysisEnabled, value);
    }

    public bool IgnoreHiddenFiles
    {
        get => _ignoreHiddenFiles;
        set => SetProperty(ref _ignoreHiddenFiles, value);
    }

    public double QuietPeriodSeconds
    {
        get => _quietPeriodSeconds;
        set => SetProperty(ref _quietPeriodSeconds, value);
    }

    public double MaximumFileSizeMiB
    {
        get => _maximumFileSizeMiB;
        set => SetProperty(ref _maximumFileSizeMiB, value);
    }

    public string ScanProfileId
    {
        get => _scanProfileId;
        set
        {
            if (SetProperty(ref _scanProfileId, value))
            {
                _selectedWorkflowProfile = _workflowProfiles.FirstOrDefault(profile =>
                    string.Equals(
                        profile.Id,
                        WatchedWorkflowUsageInspector.NormalizeLegacyProfileId(value),
                        StringComparison.Ordinal));
                OnPropertyChanged(nameof(SelectedWorkflowProfile));
            }
        }
    }

    public WorkflowProfile? SelectedWorkflowProfile
    {
        get => _selectedWorkflowProfile;
        set
        {
            if (SetProperty(ref _selectedWorkflowProfile, value) && value is not null)
            {
                ScanProfileId = value.Id;
            }
        }
    }

    public string SortingRecipeId
    {
        get => _sortingRecipeId;
        set => SetProperty(ref _sortingRecipeId, value);
    }

    public string IgnoredPathsText
    {
        get => _ignoredPathsText;
        set => SetProperty(ref _ignoredPathsText, value);
    }

    public string IgnorePatternsText
    {
        get => _ignorePatternsText;
        set => SetProperty(ref _ignorePatternsText, value);
    }

    public WatchedFolderNotificationLevel NotificationLevel
    {
        get => _notificationLevel;
        set => SetProperty(ref _notificationLevel, value);
    }

    public bool NotifyWhenPlanReady
    {
        get => _notifyWhenPlanReady;
        set => SetProperty(ref _notifyWhenPlanReady, value);
    }

    public bool NotifyWhenUnavailable
    {
        get => _notifyWhenUnavailable;
        set => SetProperty(ref _notifyWhenUnavailable, value);
    }

    public async Task RefreshAsync()
    {
        if (_manager is null)
        {
            StatusText = "Watched-folder services are unavailable in this preview context.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            await RefreshWorkflowChoicesCoreAsync();
            var selectedId = SelectedFolder?.Id;
            var configurations = await WithCurrentPlanCountsAsync(
                await _manager.ListAsync(CancellationToken.None));
            _folders.Clear();
            foreach (var configuration in configurations)
            {
                _folders.Add(new WatchedFolderRow(configuration));
            }

            SelectedFolder = selectedId is null
                ? _folders.FirstOrDefault()
                : _folders.FirstOrDefault(row => string.Equals(row.Id, selectedId, StringComparison.Ordinal)) ??
                  _folders.FirstOrDefault();
            OnPropertyChanged(nameof(HasFolders));
            StatusText = configurations.Count == 0
                ? "No watched folders are configured. Detection and analysis are automatic only after you add one."
                : $"{configurations.Count} watched folder configuration(s) loaded.";
        });
    }

    public async Task RefreshWorkflowChoicesAsync()
    {
        if (_workflowLibrary is null)
        {
            return;
        }

        await RefreshWorkflowChoicesCoreAsync();
    }

    public void SelectProfileForEditor(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ScanProfileId = profileId;
        SelectedWorkflowProfile = _workflowProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (_coordinator is not null)
        {
            _coordinator.StateChanged -= OnCoordinatorStateChanged;
            _coordinator.ActivityPublished -= OnActivityPublished;
        }
    }

    private bool CanAdd() =>
        !IsBusy &&
        _manager is not null &&
        _coordinator is not null &&
        !string.IsNullOrWhiteSpace(FolderPath);

    private bool CanActOnSelection() => !IsBusy && SelectedFolder is not null && _manager is not null;
    private bool CanActOnEnabledSelection() =>
        CanActOnSelection() && SelectedFolder!.IsEnabled && _coordinator is not null;
    private bool CanRetryAi() => CanActOnEnabledSelection() && SelectedFolder!.Configuration.AiAnalysisEnabled;
    private bool CanReviewSuggestions() =>
        CanActOnSelection() && SelectedFolder!.PendingPlanCount > 0 && _changePlanStore is not null;

    private async Task AddFolderAsync()
    {
        if (_manager is null || _coordinator is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var quiet = TimeSpan.FromSeconds(QuietPeriodSeconds);
            var selectedRecipes = SelectedRecipeIds();
            var created = await _manager.AddAsync(
                new WatchedFolderCreateRequest(
                    FolderPath,
                    string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName,
                    IncludeSubfolders,
                    ParseLines(IgnoredPathsText),
                    ParseLines(IgnorePatternsText),
                    string.IsNullOrWhiteSpace(ScanProfileId) ? "default" : ScanProfileId,
                    string.IsNullOrWhiteSpace(SortingRecipeId) ? null : SortingRecipeId,
                    DeterministicAnalysisEnabled,
                    AiAnalysisEnabled,
                    new WatchedFolderNotificationPreferences(
                        NotificationLevel,
                        NotifyWhenPlanReady,
                        NotifyWhenUnavailable),
                    quiet,
                    checked((long)(MaximumFileSizeMiB * 1024 * 1024)),
                    IgnoreHiddenFiles)
                {
                    SortingRecipeIds = selectedRecipes,
                },
                CancellationToken.None);
            await _coordinator.RefreshAsync(CancellationToken.None);
            await RefreshCoreWithoutBusyAsync(created.Id);
            _workflowLibrary?.RecordDiagnostic(
                WorkflowDiagnosticKind.Assignment,
                $"Workflow profile assigned to watched-folder configuration {created.Id} with {selectedRecipes.Count} persistent recipe(s).",
                created.ScanProfileId);
            StatusText = "Watched folder added. Startup reconciliation is queued; no file will be modified automatically.";
        });
    }

    private async Task SaveSettingsAsync()
    {
        if (_manager is null || _coordinator is null || SelectedFolder is null)
        {
            return;
        }

        var id = SelectedFolder.Id;
        await RunBusyAsync(async () =>
        {
            var selectedRecipes = SelectedRecipeIds();
            await _manager.UpdateAsync(
                id,
                new WatchedFolderUpdateRequest(
                    DisplayName,
                    IncludeSubfolders,
                    ParseLines(IgnoredPathsText),
                    ParseLines(IgnorePatternsText),
                    string.IsNullOrWhiteSpace(ScanProfileId) ? "default" : ScanProfileId,
                    string.IsNullOrWhiteSpace(SortingRecipeId) ? null : SortingRecipeId,
                    DeterministicAnalysisEnabled,
                    AiAnalysisEnabled,
                    new WatchedFolderNotificationPreferences(
                        NotificationLevel,
                        NotifyWhenPlanReady,
                        NotifyWhenUnavailable),
                    TimeSpan.FromSeconds(QuietPeriodSeconds),
                    checked((long)(MaximumFileSizeMiB * 1024 * 1024)),
                    IgnoreHiddenFiles)
                {
                    SortingRecipeIds = selectedRecipes,
                },
                CancellationToken.None);
            await _coordinator.RefreshAsync(CancellationToken.None);
            await RefreshCoreWithoutBusyAsync(id);
            _workflowLibrary?.RecordDiagnostic(
                WorkflowDiagnosticKind.Assignment,
                $"Workflow profile assignment updated for watched-folder configuration {id} with {selectedRecipes.Count} persistent recipe(s).",
                string.IsNullOrWhiteSpace(ScanProfileId) ? "default" : ScanProfileId);
            StatusText = "Watched-folder settings saved. File modification remains review-only.";
        });
    }

    private async Task PauseAsync()
    {
        if (_manager is null || _coordinator is null || SelectedFolder is null)
        {
            return;
        }

        var id = SelectedFolder.Id;
        await RunBusyAsync(async () =>
        {
            await _manager.PauseAsync(id, CancellationToken.None);
            await _coordinator.RefreshAsync(CancellationToken.None);
            await RefreshCoreWithoutBusyAsync(id);
            StatusText = "Watching paused. The folder, files, catalogue, and history were not deleted.";
        });
    }

    private async Task ResumeAsync()
    {
        if (_manager is null || _coordinator is null || SelectedFolder is null)
        {
            return;
        }

        var id = SelectedFolder.Id;
        await RunBusyAsync(async () =>
        {
            await _manager.ResumeAsync(id, CancellationToken.None);
            await _coordinator.RefreshAsync(CancellationToken.None);
            await RefreshCoreWithoutBusyAsync(id);
            StatusText = "Watching resumed. Changes made while paused are being reconciled.";
        });
    }

    private Task ScanChangesNowAsync() => RunSelectedCoordinatorActionAsync(
        (coordinator, id) => coordinator.ScanChangesNowAsync(id, CancellationToken.None),
        "Incremental scan queued. Only changed items will be analysed.");

    private Task ReconcileAsync() => RunSelectedCoordinatorActionAsync(
        (coordinator, id) => coordinator.ReconcileNowAsync(id, CancellationToken.None),
        "Full metadata reconciliation queued. Unchanged content will not be reanalysed.");

    private Task RetryAiAsync() => RunSelectedCoordinatorActionAsync(
        (coordinator, id) => coordinator.RetryAiAsync(id, CancellationToken.None),
        "Retry of pending or failed optional AI analysis queued.");

    private async Task RunSelectedCoordinatorActionAsync(
        Func<IWatchedFolderCoordinator, string, Task> action,
        string success)
    {
        if (_coordinator is null || SelectedFolder is null)
        {
            return;
        }

        var id = SelectedFolder.Id;
        await RunBusyAsync(async () =>
        {
            await action(_coordinator, id);
            StatusText = success;
        });
    }

    private async Task OpenFolderAsync()
    {
        if (_externalLauncher is null || SelectedFolder is null)
        {
            StatusText = "Opening folders is unavailable in this context.";
            return;
        }

        var placeholder = Path.Combine(SelectedFolder.FolderPath, ".opensorse-folder");
        var result = await _externalLauncher.OpenContainingFolderAsync(placeholder, CancellationToken.None);
        StatusText = result.Message;
    }

    private async Task ReviewSuggestionsAsync()
    {
        if (_changePlanStore is null || SelectedFolder is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var plans = await _changePlanStore.ListAsync(CancellationToken.None);
            var plan = plans
                .Where(plan =>
                    plan.SourceScanId?.StartsWith($"watched:{SelectedFolder.Id}:", StringComparison.Ordinal) == true &&
                    plan.Status is not ChangePlanStatus.Applied and not ChangePlanStatus.Rejected)
                .OrderByDescending(plan => plan.CreatedAtUtc)
                .FirstOrDefault();
            if (plan is null)
            {
                StatusText = "No pending reviewable Change Plan could be found.";
                return;
            }

            ReviewPlanRequested?.Invoke(this, plan);
        });
    }

    private async Task RemoveAsync()
    {
        if (_manager is null || _coordinator is null || SelectedFolder is null)
        {
            return;
        }

        var id = SelectedFolder.Id;
        await RunBusyAsync(async () =>
        {
            var removed = await _manager.RemoveAsync(id, CancellationToken.None);
            await _coordinator.RefreshAsync(CancellationToken.None);
            await RefreshCoreWithoutBusyAsync(null);
            IsRemoveConfirmationPending = false;
            StatusText = removed
                ? "Removed from the watch list. The real folder, its files, saved catalogue, and activity history were not deleted."
                : "The watched-folder configuration had already been removed.";
        });
    }

    private async Task RefreshCoreWithoutBusyAsync(string? selectedId)
    {
        if (_manager is null)
        {
            return;
        }

        var configurations = await WithCurrentPlanCountsAsync(
            await _manager.ListAsync(CancellationToken.None));
        _folders.Clear();
        foreach (var configuration in configurations)
        {
            _folders.Add(new WatchedFolderRow(configuration));
        }

        SelectedFolder = selectedId is null
            ? _folders.FirstOrDefault()
            : _folders.FirstOrDefault(row => string.Equals(row.Id, selectedId, StringComparison.Ordinal));
        OnPropertyChanged(nameof(HasFolders));
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or InvalidDataException or
            IOException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
        {
            StatusText = exception.Message;
            NotificationRequested?.Invoke(
                this,
                new NotificationRequest(NotificationSeverity.Warning, exception.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadEditor(WatchedFolderConfiguration? configuration)
    {
        if (configuration is null)
        {
            return;
        }

        FolderPath = configuration.FolderPath;
        DisplayName = configuration.DisplayName;
        IncludeSubfolders = configuration.IncludeSubfolders;
        DeterministicAnalysisEnabled = configuration.DeterministicAnalysisEnabled;
        AiAnalysisEnabled = configuration.AiAnalysisEnabled;
        IgnoreHiddenFiles = configuration.IgnoreHiddenFiles;
        QuietPeriodSeconds = configuration.QuietPeriod.TotalSeconds;
        MaximumFileSizeMiB = configuration.MaximumFileSizeBytes / 1024d / 1024d;
        ScanProfileId = configuration.ScanProfileId;
        SortingRecipeId = string.Join(", ", configuration.SortingRecipeIds.Count > 0
            ? configuration.SortingRecipeIds
            : configuration.SortingRecipeId is null
                ? []
                : [configuration.SortingRecipeId]);
        ApplySelectedRecipes(ParseRecipeIds(SortingRecipeId));
        IgnoredPathsText = string.Join(Environment.NewLine, configuration.IgnoredPaths);
        IgnorePatternsText = string.Join(Environment.NewLine, configuration.IgnorePatterns);
        NotificationLevel = configuration.Notifications.Level;
        NotifyWhenPlanReady = configuration.Notifications.NotifyWhenPlanReady;
        NotifyWhenUnavailable = configuration.Notifications.NotifyWhenUnavailable;
        _recentActivity.Clear();
    }

    private void OnCoordinatorStateChanged(object? sender, WatchedFolderRuntimeSnapshot snapshot) =>
        Dispatch(() =>
        {
            var index = _folders
                .Select((row, index) => (row, index))
                .FirstOrDefault(item => string.Equals(item.row.Id, snapshot.Configuration.Id, StringComparison.Ordinal))
                .index;
            var found = index >= 0 && index < _folders.Count &&
                        string.Equals(_folders[index].Id, snapshot.Configuration.Id, StringComparison.Ordinal);
            var wasSelected = string.Equals(SelectedFolder?.Id, snapshot.Configuration.Id, StringComparison.Ordinal);
            var row = new WatchedFolderRow(snapshot.Configuration);
            if (found)
            {
                _folders[index] = row;
            }
            else
            {
                _folders.Add(row);
            }

            if (wasSelected)
            {
                SelectedFolder = row;
                _recentActivity.Clear();
                foreach (var activity in snapshot.RecentActivity)
                {
                    _recentActivity.Add(activity);
                }
            }

            OnPropertyChanged(nameof(HasFolders));
            var preferences = snapshot.Configuration.Notifications;
            var unavailable = snapshot.Configuration.Status is
                WatchedFolderStatus.Unavailable or
                WatchedFolderStatus.Inaccessible or
                WatchedFolderStatus.ProfileUnavailable;
            var hasError = !string.IsNullOrWhiteSpace(snapshot.Configuration.LastError);
            var planReady = snapshot.Configuration.PendingChangePlanCount > 0;
            var shouldNotify = preferences.Level switch
            {
                WatchedFolderNotificationLevel.None => false,
                WatchedFolderNotificationLevel.ErrorsOnly =>
                    hasError && (!unavailable || preferences.NotifyWhenUnavailable),
                WatchedFolderNotificationLevel.Summaries =>
                    (unavailable && preferences.NotifyWhenUnavailable) ||
                    (planReady && preferences.NotifyWhenPlanReady) ||
                    snapshot.Configuration.Status == WatchedFolderStatus.ReconciliationRequired,
                _ => false,
            };
            var notificationKey =
                $"{snapshot.Configuration.Status}|{snapshot.Configuration.PendingChangePlanCount}|{snapshot.Configuration.LatestSummary}";
            if (shouldNotify &&
                snapshot.Configuration.LatestSummary is { } summary &&
                (!_lastNotificationKeys.TryGetValue(snapshot.Configuration.Id, out var previousKey) ||
                 !string.Equals(previousKey, notificationKey, StringComparison.Ordinal)))
            {
                _lastNotificationKeys[snapshot.Configuration.Id] = notificationKey;
                NotificationRequested?.Invoke(
                    this,
                    new NotificationRequest(
                        snapshot.Configuration.Status is
                            WatchedFolderStatus.Unavailable or
                            WatchedFolderStatus.ProfileUnavailable
                            ? NotificationSeverity.Warning
                            : NotificationSeverity.Information,
                        summary));
            }
        });

    private async Task<IReadOnlyList<WatchedFolderConfiguration>> WithCurrentPlanCountsAsync(
        IReadOnlyList<WatchedFolderConfiguration> configurations)
    {
        if (_changePlanStore is null || configurations.Count == 0)
        {
            return configurations;
        }

        var plans = await _changePlanStore.ListAsync(CancellationToken.None);
        return Array.AsReadOnly(configurations.Select(configuration => configuration with
        {
            PendingChangePlanCount = plans.Count(plan =>
                plan.SourceScanId?.StartsWith($"watched:{configuration.Id}:", StringComparison.Ordinal) == true &&
                plan.Status is not ChangePlanStatus.Applied and not ChangePlanStatus.Rejected),
        }).ToArray());
    }

    private void OnActivityPublished(object? sender, WatchedActivityEntry activity) =>
        Dispatch(() =>
        {
            if (!string.Equals(SelectedFolder?.Id, activity.ConfigurationId, StringComparison.Ordinal))
            {
                return;
            }

            _recentActivity.Insert(0, activity);
            while (_recentActivity.Count > 25)
            {
                _recentActivity.RemoveAt(_recentActivity.Count - 1);
            }
        });

    private void Dispatch(Action action)
    {
        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            action();
        }
        else
        {
            _synchronizationContext.Post(static state => ((Action)state!).Invoke(), action);
        }
    }

    private static IReadOnlyList<string> ParseLines(string value) =>
        Array.AsReadOnly((value ?? string.Empty)
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());

    private async Task RefreshWorkflowChoicesCoreAsync()
    {
        if (_workflowLibrary is null)
        {
            return;
        }

        var selectedProfileId = WatchedWorkflowUsageInspector.NormalizeLegacyProfileId(ScanProfileId);
        var selectedRecipeIds = SelectedRecipeIds();
        var profiles = await _workflowLibrary.ListProfilesAsync(false, CancellationToken.None);
        var recipes = await _workflowLibrary.ListRecipesAsync(false, CancellationToken.None);

        _workflowProfiles.Clear();
        foreach (var profile in profiles.Where(profile => profile.IsEnabled && !profile.IsArchived))
        {
            _workflowProfiles.Add(profile);
        }

        _workflowRecipes.Clear();
        foreach (var recipe in recipes.Where(recipe => recipe.IsEnabled && !recipe.IsArchived))
        {
            _workflowRecipes.Add(new WatchedRecipeChoice(
                recipe,
                selectedRecipeIds.Contains(recipe.Id, StringComparer.Ordinal)));
        }

        SelectedWorkflowProfile = _workflowProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, selectedProfileId, StringComparison.Ordinal));
        if (SelectedWorkflowProfile is null && _workflowProfiles.Count > 0)
        {
            SelectedWorkflowProfile = _workflowProfiles[0];
        }

        OnPropertyChanged(nameof(WorkflowProfiles));
        OnPropertyChanged(nameof(WorkflowRecipes));
    }

    private IReadOnlyList<string> SelectedRecipeIds()
    {
        var checkedIds = _workflowRecipes
            .Where(choice => choice.IsSelected)
            .Select(choice => choice.Id);
        return Array.AsReadOnly(checkedIds
            .Concat(ParseRecipeIds(SortingRecipeId))
            .Distinct(StringComparer.Ordinal)
            .ToArray());
    }

    private static IReadOnlyList<string> ParseRecipeIds(string value) =>
        Array.AsReadOnly((value ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray());

    private void ApplySelectedRecipes(IReadOnlyList<string> selectedIds)
    {
        foreach (var choice in _workflowRecipes)
        {
            choice.IsSelected = selectedIds.Contains(choice.Id, StringComparer.Ordinal);
        }
    }

    private void RefreshCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        ScanChangesNowCommand.NotifyCanExecuteChanged();
        FullReconciliationCommand.NotifyCanExecuteChanged();
        RetryAiCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        ReviewSuggestionsCommand.NotifyCanExecuteChanged();
        RequestRemoveCommand.NotifyCanExecuteChanged();
        ConfirmRemoveCommand.NotifyCanExecuteChanged();
        CancelRemoveCommand.NotifyCanExecuteChanged();
    }
}
