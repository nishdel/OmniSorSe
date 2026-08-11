using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Application.Models;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Presents exact-hash groups and routes explicit open or safe-removal requests through controlled services.
/// </summary>
public sealed class DuplicateReviewViewModel : ViewModelBase, IDisposable
{
    private const int MaximumOpenCount = 5;
    private readonly IExternalFileLauncher? _externalFileLauncher;
    private readonly IDuplicateRemovalPlanFactory? _removalPlanFactory;
    private readonly ObservableCollection<ResultDuplicateGroup> _visibleGroups = [];
    private readonly ObservableCollection<DuplicateReviewGroupRow> _visibleGroupRows = [];
    private readonly ObservableCollection<ResultFile> _selectedMembers = [];
    private readonly ObservableCollection<DuplicateFileRow> _memberRows = [];
    private ResultsSnapshot? _snapshot;
    private string? _filterText;
    private ResultDuplicateGroup? _selectedGroup;
    private DuplicateReviewGroupRow? _selectedGroupRow;
    private string _statusText = "No completed scan results are available.";
    private StatusPresentation _status = StatusPresentation.Information("No completed scan results are available.");
    private bool _isDuplicateDataAvailable;
    private bool _isOpening;
    private bool _isCreatingRemovalPlan;
    private CancellationTokenSource? _openCancellation;
    private CancellationTokenSource? _removalCancellation;
    private readonly HashSet<string> _removedMemberIds = new(StringComparer.Ordinal);

    /// <summary>Initializes a preview instance without operating-system launch support.</summary>
    public DuplicateReviewViewModel()
        : this(null, null)
    {
    }

    /// <summary>Initializes Duplicate View with an optional controlled desktop launcher.</summary>
    public DuplicateReviewViewModel(
        IExternalFileLauncher? externalFileLauncher,
        IDuplicateRemovalPlanFactory? removalPlanFactory = null)
    {
        _externalFileLauncher = externalFileLauncher;
        _removalPlanFactory = removalPlanFactory;
        VisibleGroups = new ReadOnlyObservableCollection<ResultDuplicateGroup>(_visibleGroups);
        VisibleGroupRows = new ReadOnlyObservableCollection<DuplicateReviewGroupRow>(_visibleGroupRows);
        SelectedMembers = new ReadOnlyObservableCollection<ResultFile>(_selectedMembers);
        MemberRows = new ReadOnlyObservableCollection<DuplicateFileRow>(_memberRows);
        ShowGroupFilesCommand = new RelayCommand(ShowGroupFiles, CanShowGroupFiles);
        BackToExplorerCommand = new RelayCommand(RequestBackToExplorer);
        OpenBothFilesCommand = new AsyncRelayCommand(OpenBothFilesAsync, () => CanOpenBothFiles);
        OpenSelectedFilesCommand = new AsyncRelayCommand(OpenSelectedFilesAsync, () => CanOpenSelectedFiles);
        OpenSelectedFoldersCommand = new AsyncRelayCommand(OpenSelectedFoldersAsync, () => CanOpenSelectedFolders);
        OpenFileCommand = new AsyncRelayCommand<DuplicateFileRow>(OpenOneFileAsync, CanOpenOne);
        OpenContainingFolderCommand = new AsyncRelayCommand<DuplicateFileRow>(OpenOneFolderAsync, CanOpenOne);
        CancelOpenCommand = new RelayCommand(CancelOpen, () => IsOpening);
        RequestRemovalPlanCommand = new AsyncRelayCommand(RequestRemovalPlanAsync, () => CanRequestRemovalPlan);
        CancelRemovalPlanCommand = new RelayCommand(CancelRemovalPlan, () => IsCreatingRemovalPlan);
        CloseDrawerCommand = new RelayCommand(CloseDrawer, () => IsDrawerOpen);
    }

    /// <summary>Occurs when the selected group's known members should be shown in the Results explorer.</summary>
    public event EventHandler<string>? ShowGroupFilesRequested;

    /// <summary>Occurs when the user requests return to the Results explorer.</summary>
    public event EventHandler? BackToExplorerRequested;

    /// <summary>Occurs when an explicit safe-removal request becomes a reviewable Change Plan.</summary>
    public event EventHandler<ChangePlan>? ChangePlanCreated;

    /// <summary>Gets visible duplicate groups in detector order.</summary>
    public ReadOnlyObservableCollection<ResultDuplicateGroup> VisibleGroups { get; }

    /// <summary>Gets display summaries for visible duplicate groups.</summary>
    public ReadOnlyObservableCollection<DuplicateReviewGroupRow> VisibleGroupRows { get; }

    /// <summary>Gets immutable members for compatibility with the existing read-only result workflow.</summary>
    public ReadOnlyObservableCollection<ResultFile> SelectedMembers { get; }

    /// <summary>Gets selectable display rows for the selected duplicate group.</summary>
    public ReadOnlyObservableCollection<DuplicateFileRow> MemberRows { get; }

    /// <summary>Gets or sets the local group-member text filter.</summary>
    public string? FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Gets or sets the currently selected duplicate group.</summary>
    public ResultDuplicateGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                var matchingRow = value is null
                    ? null
                    : _visibleGroupRows.FirstOrDefault(row =>
                        string.Equals(row.Group.GroupId, value.GroupId, StringComparison.Ordinal));
                if (!ReferenceEquals(_selectedGroupRow, matchingRow))
                {
                    _selectedGroupRow = matchingRow;
                    OnPropertyChanged(nameof(SelectedGroupRow));
                }

                UpdateSelectedMembers();
                NotifySelectionStateChanged();
            }
        }
    }

    /// <summary>Gets or sets the selected group summary row.</summary>
    public DuplicateReviewGroupRow? SelectedGroupRow
    {
        get => _selectedGroupRow;
        set
        {
            if (SetProperty(ref _selectedGroupRow, value))
            {
                SelectedGroup = value?.Group;
            }
        }
    }

    /// <summary>Gets a user-safe description of the current Duplicate View state.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets the accessible status banner model.</summary>
    public StatusPresentation Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                StatusText = value.Message;
            }
        }
    }

    /// <summary>Gets whether a snapshot with duplicate data is available.</summary>
    public bool IsDuplicateDataAvailable
    {
        get => _isDuplicateDataAvailable;
        private set => SetProperty(ref _isDuplicateDataAvailable, value);
    }

    /// <summary>Gets whether the active snapshot has any duplicate groups.</summary>
    public bool HasDuplicateGroups => IsDuplicateDataAvailable && _snapshot?.DuplicateGroups.Any(group =>
        group.MemberFileIds.Count(memberId => !_removedMemberIds.Contains(memberId)) >= 2) == true;

    /// <summary>Gets whether filtering leaves one or more groups visible.</summary>
    public bool HasVisibleGroups => _visibleGroups.Count > 0;

    /// <summary>Gets whether no duplicate groups are currently visible.</summary>
    public bool HasNoVisibleGroups => !HasVisibleGroups;

    /// <summary>Gets whether a duplicate group is selected.</summary>
    public bool HasSelectedGroup => SelectedGroup is not null;

    /// <summary>Gets whether the read-only duplicate detail drawer is open.</summary>
    public bool IsDrawerOpen => HasSelectedGroup;

    /// <summary>Gets whether the selected group contains exactly two files.</summary>
    public bool IsExactPair => SelectedGroup?.MemberCount == 2;

    /// <summary>Gets whether the selected group requires explicit member selection.</summary>
    public bool IsLargeGroup => SelectedGroup?.MemberCount > 2;

    /// <summary>Gets a clear group-size label.</summary>
    public string SelectedGroupCountText => SelectedGroup is null
        ? string.Empty
        : $"{SelectedGroup.MemberCount} identical files";

    /// <summary>Gets display text for the common known size of each file.</summary>
    public string SelectedGroupCommonSizeText => SelectedGroup?.CommonFileSizeInBytes is { } size
        ? ResultsFileRow.FormatSize(size)
        : "Unavailable";

    /// <summary>Gets display text for the theoretical space saved by keeping one copy.</summary>
    public string SelectedGroupPotentialReclaimableText => SelectedGroup?.PotentialReclaimableBytes is { } bytes
        ? ResultsFileRow.FormatSize(bytes)
        : "Unavailable";

    /// <summary>Gets the number of explicitly selected member rows.</summary>
    public int SelectedMemberCount => _memberRows.Count(row => row.IsSelected);

    /// <summary>Gets selection guidance for bounded open and safe-removal operations.</summary>
    public string SelectedMemberCountText =>
        $"{SelectedMemberCount} selected (maximum {MaximumOpenCount}); at least one copy must remain";

    /// <summary>Gets whether selected unwanted copies can become a reviewable safe-removal plan.</summary>
    public bool CanRequestRemovalPlan =>
        _removalPlanFactory is not null &&
        SelectedGroup is not null &&
        SelectedMemberCount is > 0 and <= MaximumOpenCount &&
        SelectedMemberCount < _memberRows.Count &&
        !IsOpening &&
        !IsCreatingRemovalPlan;

    /// <summary>Gets whether a safe-removal Change Plan is being created.</summary>
    public bool IsCreatingRemovalPlan
    {
        get => _isCreatingRemovalPlan;
        private set
        {
            if (SetProperty(ref _isCreatingRemovalPlan, value))
            {
                OnPropertyChanged(nameof(CanRequestRemovalPlan));
                RequestRemovalPlanCommand.NotifyCanExecuteChanged();
                CancelRemovalPlanCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether a shell-open operation is active.</summary>
    public bool IsOpening
    {
        get => _isOpening;
        private set
        {
            if (SetProperty(ref _isOpening, value))
            {
                NotifyLaunchCommands();
                CancelOpenCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether the exact two known files can be opened.</summary>
    public bool CanOpenBothFiles =>
        IsExactPair && _memberRows.Count == 2 && _externalFileLauncher is not null && !IsOpening;

    /// <summary>Gets whether the explicit selection can be opened safely.</summary>
    public bool CanOpenSelectedFiles =>
        IsLargeGroup && SelectedMemberCount is > 0 and <= MaximumOpenCount &&
        _externalFileLauncher is not null && !IsOpening;

    /// <summary>Gets whether containing folders can be opened for the bounded explicit selection.</summary>
    public bool CanOpenSelectedFolders =>
        SelectedMemberCount is > 0 and <= MaximumOpenCount &&
        _externalFileLauncher is not null && !IsOpening;

    /// <summary>Gets the command that routes the selected group back to filtered Results.</summary>
    public IRelayCommand ShowGroupFilesCommand { get; }

    /// <summary>Gets the command that returns to Results.</summary>
    public IRelayCommand BackToExplorerCommand { get; }

    /// <summary>Gets the command that opens both files in a two-file group.</summary>
    public IAsyncRelayCommand OpenBothFilesCommand { get; }

    /// <summary>Gets the command that opens up to five explicitly selected files.</summary>
    public IAsyncRelayCommand OpenSelectedFilesCommand { get; }

    /// <summary>Gets the command that opens containing folders for up to five selected files.</summary>
    public IAsyncRelayCommand OpenSelectedFoldersCommand { get; }

    /// <summary>Gets the command that opens one explicitly chosen known file.</summary>
    public IAsyncRelayCommand<DuplicateFileRow> OpenFileCommand { get; }

    /// <summary>Gets the command that opens one explicitly chosen known containing folder.</summary>
    public IAsyncRelayCommand<DuplicateFileRow> OpenContainingFolderCommand { get; }

    /// <summary>Gets the command that cancels the current bounded launch loop.</summary>
    public IRelayCommand CancelOpenCommand { get; }

    /// <summary>Gets the command that creates a reviewable Change Plan for selected unwanted copies.</summary>
    public IAsyncRelayCommand RequestRemovalPlanCommand { get; }

    /// <summary>Gets the command that cancels Change Plan creation before any filesystem change occurs.</summary>
    public IRelayCommand CancelRemovalPlanCommand { get; }

    /// <summary>Gets the command that closes the duplicate detail drawer.</summary>
    public IRelayCommand CloseDrawerCommand { get; }

    /// <summary>Replaces review state when a genuinely different completed snapshot is loaded.</summary>
    public void LoadSnapshot(ResultsSnapshot? snapshot)
    {
        var isNewSnapshot = !string.Equals(_snapshot?.SessionId, snapshot?.SessionId, StringComparison.Ordinal);
        if (isNewSnapshot)
        {
            CancelOpen();
            CancelRemovalPlan();
            _removedMemberIds.Clear();
        }

        _snapshot = snapshot;
        if (isNewSnapshot)
        {
            _filterText = null;
            OnPropertyChanged(nameof(FilterText));
            SelectedGroup = null;
        }

        IsDuplicateDataAvailable = snapshot?.IsDuplicateDataAvailable == true;
        ApplyFilter();
        OnPropertyChanged(nameof(HasDuplicateGroups));
    }

    /// <summary>Applies an opaque group selection only when it belongs to the current snapshot.</summary>
    public void SelectGroup(string? groupId)
    {
        SelectedGroup = string.IsNullOrWhiteSpace(groupId)
            ? null
            : _visibleGroups.FirstOrDefault(group =>
                string.Equals(group.GroupId, groupId, StringComparison.Ordinal));
    }

    /// <summary>Removes successfully moved copies from the active duplicate-review projection.</summary>
    public void ApplyExecutedChangePlan(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var removedIds = plan.Actions
            .Where(action =>
                action.ActionType == ChangeActionType.MoveFile &&
                action.SuggestionSource == ChangeSuggestionSource.DuplicateAnalysis &&
                action.SourceIdentity is not null)
            .Select(action => action.SourceIdentity!.Identity)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        if (removedIds.Length == 0)
        {
            return;
        }

        foreach (var id in removedIds)
        {
            _removedMemberIds.Add(id);
        }

        SelectedGroup = null;
        ApplyFilter();
        Status = StatusPresentation.Success(
            $"{removedIds.Length} selected duplicate copy or copies were moved to the recovery area. Original remaining copies were unchanged.");
    }

    /// <summary>Restores duplicate-review members after the corresponding recovery moves are fully undone.</summary>
    public void RevertExecutedChangePlan(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var restored = plan.Actions
            .Where(action =>
                action.ActionType == ChangeActionType.MoveFile &&
                action.SuggestionSource == ChangeSuggestionSource.DuplicateAnalysis &&
                action.SourceIdentity is not null)
            .Select(action => action.SourceIdentity!.Identity)
            .Count(_removedMemberIds.Remove);
        if (restored == 0)
        {
            return;
        }

        ApplyFilter();
        Status = StatusPresentation.Success(
            $"Undo restored {restored} duplicate copy or copies to this review.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancelOpen();
        CancelRemovalPlan();
        ClearMemberRows();
    }

    private void ApplyFilter()
    {
        _visibleGroups.Clear();
        _visibleGroupRows.Clear();
        if (_snapshot is null)
        {
            Status = StatusPresentation.Information("No completed scan results are available.");
        }
        else if (!IsDuplicateDataAvailable)
        {
            var issue = _snapshot.Issues.FirstOrDefault(item => item.SourceStage == "Exact duplicates");
            Status = StatusPresentation.Warning(
                issue?.Message ?? "Duplicate View was unavailable for this completed scan.");
        }
        else
        {
            var memberById = _snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
            var filter = string.IsNullOrWhiteSpace(FilterText) ? null : FilterText.Trim();
            foreach (var originalGroup in _snapshot.DuplicateGroups)
            {
                var members = originalGroup.MemberFileIds
                    .Where(memberId => !_removedMemberIds.Contains(memberId))
                    .ToArray();
                if (members.Length < 2)
                {
                    continue;
                }

                var group = originalGroup with
                {
                    MemberFileIds = members,
                    MemberCount = members.Length,
                    PotentialReclaimableBytes = originalGroup.CommonFileSizeInBytes is { } commonSize
                        ? commonSize * (members.Length - 1)
                        : null,
                };
                if (filter is null || group.MemberFileIds
                        .Where(memberById.ContainsKey)
                        .Select(memberId => memberById[memberId])
                        .Any(file => MatchesFilter(file, filter)))
                {
                    _visibleGroups.Add(group);
                    _visibleGroupRows.Add(CreateGroupRow(group, memberById));
                }
            }

            Status = _snapshot.DuplicateGroups.Count == 0
                ? StatusPresentation.Information("No identical-file groups were found in this completed scan.")
                : _removedMemberIds.Count > 0 && _visibleGroups.Count == 0 && filter is null
                    ? StatusPresentation.Success("Duplicate review is up to date after the completed removal plan.")
                : _visibleGroups.Count == 0
                    ? StatusPresentation.Information("No duplicate groups match the active filter.")
                    : StatusPresentation.Success($"{_visibleGroups.Count} duplicate group(s) available.");
        }

        var matchingRow = SelectedGroup is null
            ? null
            : _visibleGroupRows.FirstOrDefault(row =>
                string.Equals(row.Group.GroupId, SelectedGroup.GroupId, StringComparison.Ordinal));
        if (!ReferenceEquals(_selectedGroupRow, matchingRow))
        {
            _selectedGroupRow = matchingRow;
            OnPropertyChanged(nameof(SelectedGroupRow));
        }

        OnPropertyChanged(nameof(HasVisibleGroups));
        OnPropertyChanged(nameof(HasNoVisibleGroups));
        OnPropertyChanged(nameof(HasDuplicateGroups));
    }

    private static bool MatchesFilter(ResultFile file, string filter) =>
        file.DisplayFileName.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || file.FullPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || file.NormalizedExtension.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || file.ClassificationDisplay.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static DuplicateReviewGroupRow CreateGroupRow(
        ResultDuplicateGroup group,
        IReadOnlyDictionary<string, ResultFile> memberById)
    {
        var memberNames = group.MemberFileIds
            .Where(memberById.ContainsKey)
            .Select(memberId => memberById[memberId].DisplayFileName)
            .Take(2)
            .ToArray();
        var summary = memberNames.Length == 0
            ? "Member details unavailable"
            : string.Join(", ", memberNames) +
              (group.MemberCount > memberNames.Length
                  ? $" and {group.MemberCount - memberNames.Length} more"
                  : string.Empty);
        return new DuplicateReviewGroupRow(
            group,
            summary,
            group.CommonFileSizeInBytes is { } size ? ResultsFileRow.FormatSize(size) : "Unavailable",
            group.PotentialReclaimableBytes is { } bytes ? ResultsFileRow.FormatSize(bytes) : "Unavailable");
    }

    private void UpdateSelectedMembers()
    {
        ClearMemberRows();
        _selectedMembers.Clear();
        if (SelectedGroup is not null && _snapshot is not null)
        {
            var memberById = _snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
            foreach (var memberId in SelectedGroup.MemberFileIds)
            {
                if (!memberById.TryGetValue(memberId, out var member))
                {
                    continue;
                }

                _selectedMembers.Add(member);
                var row = new DuplicateFileRow(member);
                row.PropertyChanged += OnMemberRowPropertyChanged;
                _memberRows.Add(row);
            }

            if (_selectedMembers.Count != SelectedGroup.MemberCount)
            {
                Status = StatusPresentation.Warning("Some duplicate members could not be shown from this completed scan.");
            }
        }

        NotifySelectionStateChanged();
    }

    private void ClearMemberRows()
    {
        foreach (var row in _memberRows)
        {
            row.PropertyChanged -= OnMemberRowPropertyChanged;
        }

        _memberRows.Clear();
    }

    private void OnMemberRowPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(DuplicateFileRow.IsSelected))
        {
            return;
        }

        if (sender is DuplicateFileRow row && row.IsSelected && SelectedMemberCount > MaximumOpenCount)
        {
            row.IsSelected = false;
            Status = StatusPresentation.Warning($"Select at most {MaximumOpenCount} files at a time.");
            return;
        }

        NotifySelectionStateChanged();
    }

    private void NotifySelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(IsDrawerOpen));
        OnPropertyChanged(nameof(IsExactPair));
        OnPropertyChanged(nameof(IsLargeGroup));
        OnPropertyChanged(nameof(SelectedGroupCountText));
        OnPropertyChanged(nameof(SelectedGroupCommonSizeText));
        OnPropertyChanged(nameof(SelectedGroupPotentialReclaimableText));
        OnPropertyChanged(nameof(SelectedMemberCount));
        OnPropertyChanged(nameof(SelectedMemberCountText));
        OnPropertyChanged(nameof(CanOpenBothFiles));
        OnPropertyChanged(nameof(CanOpenSelectedFiles));
        OnPropertyChanged(nameof(CanOpenSelectedFolders));
        OnPropertyChanged(nameof(CanRequestRemovalPlan));
        RequestRemovalPlanCommand.NotifyCanExecuteChanged();
        ShowGroupFilesCommand.NotifyCanExecuteChanged();
        CloseDrawerCommand.NotifyCanExecuteChanged();
        NotifyLaunchCommands();
    }

    private void CloseDrawer() => SelectedGroupRow = null;

    private void NotifyLaunchCommands()
    {
        OpenBothFilesCommand.NotifyCanExecuteChanged();
        OpenSelectedFilesCommand.NotifyCanExecuteChanged();
        OpenSelectedFoldersCommand.NotifyCanExecuteChanged();
        OpenFileCommand.NotifyCanExecuteChanged();
        OpenContainingFolderCommand.NotifyCanExecuteChanged();
    }

    private bool CanShowGroupFiles() => SelectedGroup is not null;

    private void ShowGroupFiles()
    {
        if (SelectedGroup is not null)
        {
            ShowGroupFilesRequested?.Invoke(this, SelectedGroup.GroupId);
        }
    }

    private void RequestBackToExplorer() => BackToExplorerRequested?.Invoke(this, EventArgs.Empty);

    private Task OpenBothFilesAsync() => OpenAsync(_memberRows.ToArray(), openFolders: false);

    private Task OpenSelectedFilesAsync() =>
        OpenAsync(_memberRows.Where(row => row.IsSelected).ToArray(), openFolders: false);

    private Task OpenSelectedFoldersAsync() =>
        OpenAsync(_memberRows.Where(row => row.IsSelected).ToArray(), openFolders: true);

    private Task OpenOneFileAsync(DuplicateFileRow? row) =>
        row is null ? Task.CompletedTask : OpenAsync([row], openFolders: false);

    private Task OpenOneFolderAsync(DuplicateFileRow? row) =>
        row is null ? Task.CompletedTask : OpenAsync([row], openFolders: true);

    private bool CanOpenOne(DuplicateFileRow? row) =>
        row is not null &&
        _externalFileLauncher is not null &&
        !IsOpening &&
        _memberRows.Contains(row);

    private async Task RequestRemovalPlanAsync()
    {
        if (_removalPlanFactory is null || SelectedGroup is null)
        {
            Status = StatusPresentation.Warning("Safe duplicate removal is unavailable in this application context.");
            return;
        }

        var filesToRemove = _memberRows.Where(row => row.IsSelected).Select(row => row.File).ToArray();
        var filesToKeep = _memberRows.Where(row => !row.IsSelected).Select(row => row.File).ToArray();
        if (filesToRemove.Length == 0 || filesToKeep.Length == 0)
        {
            Status = StatusPresentation.Warning("Select unwanted copies while leaving at least one known copy unselected.");
            return;
        }

        CancelRemovalPlan();
        var cancellation = new CancellationTokenSource();
        _removalCancellation = cancellation;
        IsCreatingRemovalPlan = true;
        Status = StatusPresentation.Progress("Preparing a safe duplicate-removal Change Plan...");
        try
        {
            var plan = await _removalPlanFactory.CreateDuplicateRemovalPlanAsync(
                filesToRemove,
                filesToKeep,
                _snapshot?.SessionId,
                cancellation.Token);
            Status = StatusPresentation.Success(
                "The removal plan is ready for review. No file has been changed yet.");
            ChangePlanCreated?.Invoke(this, plan);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = StatusPresentation.Information("Duplicate-removal plan creation was cancelled. No file was changed.");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Status = StatusPresentation.Error(
                "A safe duplicate-removal plan could not be created. Review the selected paths and try again; no file was changed.");
        }
        finally
        {
            if (ReferenceEquals(_removalCancellation, cancellation))
            {
                _removalCancellation = null;
            }

            cancellation.Dispose();
            IsCreatingRemovalPlan = false;
        }
    }

    private async Task OpenAsync(IReadOnlyList<DuplicateFileRow> selectedRows, bool openFolders)
    {
        if (_externalFileLauncher is null || selectedRows.Count is < 1 or > MaximumOpenCount)
        {
            Status = StatusPresentation.Warning($"Select between 1 and {MaximumOpenCount} known files.");
            return;
        }

        if (SelectedGroup is null || selectedRows.Any(row => !_memberRows.Contains(row)))
        {
            Status = StatusPresentation.Error("The selection no longer belongs to the active duplicate group.");
            return;
        }

        CancelOpen();
        var cancellation = new CancellationTokenSource();
        _openCancellation = cancellation;
        IsOpening = true;
        Status = StatusPresentation.Progress(
            openFolders ? "Opening selected containing folders..." : "Opening selected files...");
        var succeeded = 0;
        var failed = 0;
        try
        {
            foreach (var row in selectedRows)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var result = openFolders
                    ? await _externalFileLauncher.OpenContainingFolderAsync(row.FullPath, cancellation.Token)
                    : await _externalFileLauncher.OpenFileAsync(row.FullPath, cancellation.Token);
                if (result.Succeeded)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }

            Status = failed == 0
                ? StatusPresentation.Success(
                    openFolders
                        ? $"{succeeded} containing folder(s) opened."
                        : $"{succeeded} file(s) opened.")
                : StatusPresentation.Warning(
                    $"{succeeded} opened; {failed} unavailable or could not be opened. No files were changed.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = StatusPresentation.Information(
                $"{succeeded} opened before cancellation. No files were changed.");
        }
        finally
        {
            if (ReferenceEquals(_openCancellation, cancellation))
            {
                _openCancellation = null;
            }

            cancellation.Dispose();
            IsOpening = false;
        }
    }

    private void CancelOpen()
    {
        var cancellation = Interlocked.Exchange(ref _openCancellation, null);
        cancellation?.Cancel();
    }

    private void CancelRemovalPlan()
    {
        var cancellation = Interlocked.Exchange(ref _removalCancellation, null);
        cancellation?.Cancel();
    }
}
