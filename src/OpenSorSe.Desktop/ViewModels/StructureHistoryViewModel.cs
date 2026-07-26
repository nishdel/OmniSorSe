using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Structure;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Identifies the structure snapshot projected in the history diagram.</summary>
public enum StructureSnapshotKind
{
    /// <summary>The root when the proposal was created.</summary>
    Source,
    /// <summary>The review-only proposed structure.</summary>
    Proposed,
    /// <summary>The structure captured after a successful apply.</summary>
    Applied,
    /// <summary>A fresh metadata-only capture of the selected root.</summary>
    Current,
}

/// <summary>Presents one selectable history-status filter.</summary>
public sealed record StructureStatusFilterOption(RestructuringStatus? Status, string Label);

/// <summary>Presents one selectable structure snapshot.</summary>
public sealed record StructureSnapshotOption(StructureSnapshotKind Kind, string Label);

/// <summary>Presents one durable restructuring operation.</summary>
public sealed record StructureHistoryRow(RestructuringHistoryRecord Record)
{
    /// <summary>Gets the operation identity.</summary>
    public string OperationId => Record.OperationId;
    /// <summary>Gets the normalized root path.</summary>
    public string RootPath => Record.RootPath;
    /// <summary>Gets a local display timestamp.</summary>
    public string StartedText => Record.StartedAtUtc.ToLocalTime().ToString("g");
    /// <summary>Gets a readable terminal or preview state.</summary>
    public string StatusText => Record.Status switch
    {
        RestructuringStatus.PartiallyApplied => "Partial - review required",
        _ => Record.Status.ToString(),
    };
    /// <summary>Gets a bounded safe operation summary.</summary>
    public string Summary => Record.Summary;
    /// <summary>Gets whether this operation is the only state that activates repeat protection.</summary>
    public bool ActivatesProtection => Record.Status == RestructuringStatus.Applied;
}

/// <summary>Presents one accessible node in a bounded structure diagram.</summary>
public sealed record StructureDiagramRow(
    string RelativePath,
    string DisplayText,
    string KindText,
    string ChangeText);

/// <summary>
/// Owns advanced, non-AI folder restructuring previews, history, comparisons, and
/// read-only diagrams while keeping mutation behind a separate confirmation step.
/// </summary>
public sealed class StructureHistoryViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyList<StructureStatusFilterOption> StatusFiltersValue =
        Array.AsReadOnly<StructureStatusFilterOption>(
        [
            new(null, "All states"),
            new(RestructuringStatus.Previewed, "Previewed"),
            new(RestructuringStatus.Applied, "Applied"),
            new(RestructuringStatus.PartiallyApplied, "Partial"),
            new(RestructuringStatus.Failed, "Failed"),
            new(RestructuringStatus.Cancelled, "Cancelled"),
        ]);
    private static readonly IReadOnlyList<StructureSnapshotOption> SnapshotOptionsValue =
        Array.AsReadOnly<StructureSnapshotOption>(
        [
            new(StructureSnapshotKind.Source, "Source structure"),
            new(StructureSnapshotKind.Proposed, "Proposed structure"),
            new(StructureSnapshotKind.Applied, "Applied structure"),
            new(StructureSnapshotKind.Current, "Current structure"),
        ]);
    private readonly IStructureHistoryStore? _historyStore;
    private readonly IFolderRestructuringService? _restructuringService;
    private readonly IFolderStructureSnapshotService? _snapshotService;
    private readonly IStructureComparisonService _comparisonService;
    private readonly ObservableCollection<StructureHistoryRow> _history = [];
    private readonly ObservableCollection<StructureDiagramRow> _diagramRows = [];
    private IReadOnlyList<RestructuringHistoryRecord> _allRecords = [];
    private StructureHistoryRow? _selectedHistory;
    private StructureStatusFilterOption _selectedStatusFilter = StatusFiltersValue[0];
    private StructureSnapshotOption _selectedSnapshot = SnapshotOptionsValue[0];
    private FolderStructureSnapshot? _currentSnapshot;
    private RestructuringPlan? _currentPlan;
    private string? _rootFilter;
    private string? _rootPath;
    private string? _diagramSearch;
    private string _statusText = "Refresh to load local structure history.";
    private string _comparisonSummary = "Select a history record to inspect its structures.";
    private string _protectionText = "Repeat protection has not been evaluated.";
    private bool _isBusy;
    private bool _isApplyConfirmationPending;
    private bool _isClearConfirmationPending;
    private CancellationTokenSource? _cancellation;

    /// <summary>Initializes a designer-safe unavailable history surface.</summary>
    public StructureHistoryViewModel()
        : this(null, null, null, new StructureComparisonService())
    {
    }

    /// <summary>Initializes the history surface with application-owned services.</summary>
    public StructureHistoryViewModel(
        IStructureHistoryStore? historyStore,
        IFolderRestructuringService? restructuringService,
        IFolderStructureSnapshotService? snapshotService,
        IStructureComparisonService comparisonService)
    {
        _historyStore = historyStore;
        _restructuringService = restructuringService;
        _snapshotService = snapshotService;
        _comparisonService = comparisonService ?? throw new ArgumentNullException(nameof(comparisonService));
        History = new ReadOnlyObservableCollection<StructureHistoryRow>(_history);
        DiagramRows = new ReadOnlyObservableCollection<StructureDiagramRow>(_diagramRows);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanUseServices);
        PreviewCommand = new AsyncRelayCommand(
            () => PreviewAsync(false),
            () => CanUseServices() && !string.IsNullOrWhiteSpace(RootPath));
        ProposeAgainCommand = new AsyncRelayCommand(
            () => PreviewAsync(true),
            () => CanUseServices() && !string.IsNullOrWhiteSpace(RootPath));
        RequestApplyCommand = new RelayCommand(RequestApply, () => CurrentPlan is not null && !IsBusy);
        ConfirmApplyCommand = new AsyncRelayCommand(
            ConfirmApplyAsync,
            () => CurrentPlan is not null && IsApplyConfirmationPending && !IsBusy);
        CancelApplyConfirmationCommand = new RelayCommand(
            () => IsApplyConfirmationPending = false,
            () => IsApplyConfirmationPending && !IsBusy);
        CaptureCurrentCommand = new AsyncRelayCommand(
            CaptureCurrentAsync,
            () => SelectedHistory is not null && _snapshotService is not null && !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        RequestClearHistoryCommand = new RelayCommand(
            () => IsClearConfirmationPending = true,
            () => _historyStore is not null && _allRecords.Count > 0 && !IsBusy);
        ConfirmClearHistoryCommand = new AsyncRelayCommand(
            ClearHistoryAsync,
            () => _historyStore is not null && IsClearConfirmationPending && !IsBusy);
        CancelClearHistoryCommand = new RelayCommand(
            () => IsClearConfirmationPending = false,
            () => IsClearConfirmationPending && !IsBusy);
    }

    /// <summary>Gets filtered history rows.</summary>
    public ReadOnlyObservableCollection<StructureHistoryRow> History { get; }

    /// <summary>Gets the bounded accessible diagram rows.</summary>
    public ReadOnlyObservableCollection<StructureDiagramRow> DiagramRows { get; }

    /// <summary>Gets status filter choices.</summary>
    public IReadOnlyList<StructureStatusFilterOption> StatusFilters => StatusFiltersValue;

    /// <summary>Gets snapshot choices.</summary>
    public IReadOnlyList<StructureSnapshotOption> SnapshotOptions => SnapshotOptionsValue;

    /// <summary>Gets or sets the root path substring filter.</summary>
    public string? RootFilter
    {
        get => _rootFilter;
        set
        {
            if (SetProperty(ref _rootFilter, value))
            {
                ApplyHistoryFilters();
            }
        }
    }

    /// <summary>Gets or sets the selected status filter.</summary>
    public StructureStatusFilterOption SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                ApplyHistoryFilters();
            }
        }
    }

    /// <summary>Gets or sets the selected history row.</summary>
    public StructureHistoryRow? SelectedHistory
    {
        get => _selectedHistory;
        set
        {
            if (SetProperty(ref _selectedHistory, value))
            {
                _currentSnapshot = null;
                CurrentPlan = null;
                SelectedSnapshot = SnapshotOptionsValue[0];
                RebuildDiagram();
                CaptureCurrentCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedDetailsText));
            }
        }
    }

    /// <summary>Gets whether a record is selected.</summary>
    public bool HasSelection => SelectedHistory is not null;

    /// <summary>Gets readable selected-record details.</summary>
    public string SelectedDetailsText => SelectedHistory is null
        ? "No structure-history record is selected."
        : $"{SelectedHistory.RootPath} | {SelectedHistory.StatusText} | {SelectedHistory.Record.IncludedFiles.Count} reviewed move(s) | algorithm {SelectedHistory.Record.AlgorithmVersion}";

    /// <summary>Gets or sets the absolute root used for a new preview.</summary>
    public string? RootPath
    {
        get => _rootPath;
        set
        {
            if (SetProperty(ref _rootPath, value))
            {
                PreviewCommand.NotifyCanExecuteChanged();
                ProposeAgainCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the snapshot shown in the diagram.</summary>
    public StructureSnapshotOption SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedSnapshot, value))
            {
                RebuildDiagram();
            }
        }
    }

    /// <summary>Gets or sets a relative-path search applied to diagram rows.</summary>
    public string? DiagramSearch
    {
        get => _diagramSearch;
        set
        {
            if (SetProperty(ref _diagramSearch, value))
            {
                RebuildDiagram();
            }
        }
    }

    /// <summary>Gets a readable comparison summary.</summary>
    public string ComparisonSummary
    {
        get => _comparisonSummary;
        private set => SetProperty(ref _comparisonSummary, value);
    }

    /// <summary>Gets the latest repeat-protection evaluation.</summary>
    public string ProtectionText
    {
        get => _protectionText;
        private set => SetProperty(ref _protectionText, value);
    }

    /// <summary>Gets the current user-safe status.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets whether an operation is active.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandState();
            }
        }
    }

    /// <summary>Gets whether the second explicit apply step is visible.</summary>
    public bool IsApplyConfirmationPending
    {
        get => _isApplyConfirmationPending;
        private set
        {
            if (SetProperty(ref _isApplyConfirmationPending, value))
            {
                ConfirmApplyCommand.NotifyCanExecuteChanged();
                CancelApplyConfirmationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether history clearing awaits explicit confirmation.</summary>
    public bool IsClearConfirmationPending
    {
        get => _isClearConfirmationPending;
        private set
        {
            if (SetProperty(ref _isClearConfirmationPending, value))
            {
                ConfirmClearHistoryCommand.NotifyCanExecuteChanged();
                CancelClearHistoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether the filtered history list is empty.</summary>
    public bool HasNoHistory => History.Count == 0;

    /// <summary>Gets whether the selected diagram contains no visible rows.</summary>
    public bool HasNoDiagramRows => DiagramRows.Count == 0;

    /// <summary>Gets the refresh command.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }
    /// <summary>Gets the ordinary proposal command.</summary>
    public IAsyncRelayCommand PreviewCommand { get; }
    /// <summary>Gets the explicit repeat-protection override command.</summary>
    public IAsyncRelayCommand ProposeAgainCommand { get; }
    /// <summary>Gets the command that reveals exact-plan confirmation.</summary>
    public IRelayCommand RequestApplyCommand { get; }
    /// <summary>Gets the exact-plan apply command.</summary>
    public IAsyncRelayCommand ConfirmApplyCommand { get; }
    /// <summary>Gets the apply-confirmation cancellation command.</summary>
    public IRelayCommand CancelApplyConfirmationCommand { get; }
    /// <summary>Gets the current metadata-only capture command.</summary>
    public IAsyncRelayCommand CaptureCurrentCommand { get; }
    /// <summary>Gets the active-operation cancellation command.</summary>
    public IRelayCommand CancelCommand { get; }
    /// <summary>Gets the clear-history request command.</summary>
    public IRelayCommand RequestClearHistoryCommand { get; }
    /// <summary>Gets the explicit clear-history confirmation command.</summary>
    public IAsyncRelayCommand ConfirmClearHistoryCommand { get; }
    /// <summary>Gets the clear-history cancellation command.</summary>
    public IRelayCommand CancelClearHistoryCommand { get; }

    /// <summary>Loads bounded local history.</summary>
    public async Task RefreshAsync()
    {
        if (_historyStore is null)
        {
            StatusText = "Structure history is unavailable in this application context.";
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            _allRecords = await _historyStore.ListAsync(cancellationToken);
            ApplyHistoryFilters();
            StatusText = _allRecords.Count == 0
                ? "No folder restructuring history has been recorded."
                : $"Loaded {_allRecords.Count} bounded structure-history record(s).";
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private bool CanUseServices() =>
        _historyStore is not null && _restructuringService is not null && !IsBusy;

    private async Task PreviewAsync(bool explicitOverride)
    {
        if (_restructuringService is null || string.IsNullOrWhiteSpace(RootPath))
        {
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            var result = await _restructuringService.PreviewAsync(
                RootPath.Trim(),
                explicitOverride,
                cancellationToken);
            ProtectionText = result.ProtectionState switch
            {
                RestructuringProtectionState.AlreadyOrganized => "Already organized: the last successful applied structure still matches.",
                RestructuringProtectionState.NewFilesOnly => "Incremental mode: previously applied files are unchanged; only new files are proposed.",
                RestructuringProtectionState.MateriallyChanged => "Manual or material changes were detected; review a fresh proposal.",
                RestructuringProtectionState.PreviousIncomplete => "Earlier previews or failures do not activate repeat protection.",
                _ => "First run for this root.",
            };
            IsApplyConfirmationPending = false;
            StatusText = result.Message;
            await RefreshCoreAsync(cancellationToken, result.Plan?.OperationId);
            CurrentPlan = result.Plan;
            if (result.Plan is not null)
            {
                SelectedSnapshot = SnapshotOptionsValue.Single(option =>
                    option.Kind == StructureSnapshotKind.Proposed);
            }
        });
    }

    private void RequestApply()
    {
        IsApplyConfirmationPending = CurrentPlan is not null;
        StatusText = CurrentPlan is null
            ? "Create and review a proposal before applying."
            : $"Confirm only after reviewing all {CurrentPlan.Moves.Count} proposed move(s). No overwrite or deletion is allowed.";
    }

    private async Task ConfirmApplyAsync()
    {
        if (_restructuringService is null || CurrentPlan is null || !IsApplyConfirmationPending)
        {
            return;
        }

        var plan = CurrentPlan;
        await RunBusyAsync(async cancellationToken =>
        {
            var result = await _restructuringService.ApplyAsync(
                plan,
                plan.OperationId,
                cancellationToken);
            IsApplyConfirmationPending = false;
            CurrentPlan = null;
            StatusText = result.Message;
            await RefreshCoreAsync(cancellationToken, result.Record.OperationId);
            SelectedSnapshot = SnapshotOptionsValue.Single(option =>
                option.Kind == (result.Record.AppliedSnapshot is null
                    ? StructureSnapshotKind.Proposed
                    : StructureSnapshotKind.Applied));
        });
    }

    private async Task CaptureCurrentAsync()
    {
        if (_snapshotService is null || SelectedHistory is null)
        {
            return;
        }

        var selectedId = SelectedHistory.OperationId;
        await RunBusyAsync(async cancellationToken =>
        {
            _currentSnapshot = await _snapshotService.CaptureAsync(
                SelectedHistory!.RootPath,
                cancellationToken);
            SelectedSnapshot = SnapshotOptionsValue.Single(option =>
                option.Kind == StructureSnapshotKind.Current);
            StatusText = "Captured current structure metadata. No file content was read and no file was changed.";
            SelectedHistory = _history.FirstOrDefault(row => row.OperationId == selectedId) ?? SelectedHistory;
            RebuildDiagram();
        });
    }

    private async Task ClearHistoryAsync()
    {
        if (_historyStore is null || !IsClearConfirmationPending)
        {
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            await _historyStore.ClearAsync(cancellationToken);
            _allRecords = [];
            ApplyHistoryFilters();
            SelectedHistory = null;
            CurrentPlan = null;
            IsClearConfirmationPending = false;
            StatusText = "Application-owned structure history was cleared. No user file or folder was changed.";
        });
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken, string? selectOperationId)
    {
        if (_historyStore is null)
        {
            return;
        }

        _allRecords = await _historyStore.ListAsync(cancellationToken);
        ApplyHistoryFilters();
        if (selectOperationId is not null)
        {
            SelectedHistory = _history.FirstOrDefault(row =>
                string.Equals(row.OperationId, selectOperationId, StringComparison.Ordinal));
        }
    }

    private void ApplyHistoryFilters()
    {
        var selectedId = SelectedHistory?.OperationId;
        var filtered = _allRecords
            .Where(record =>
                string.IsNullOrWhiteSpace(RootFilter) ||
                record.RootPath.Contains(
                    RootFilter.Trim(),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            .Where(record =>
                SelectedStatusFilter.Status is null ||
                record.Status == SelectedStatusFilter.Status)
            .Select(record => new StructureHistoryRow(record))
            .ToArray();
        _history.Clear();
        foreach (var row in filtered)
        {
            _history.Add(row);
        }

        OnPropertyChanged(nameof(HasNoHistory));
        RequestClearHistoryCommand.NotifyCanExecuteChanged();
        SelectedHistory = selectedId is null
            ? _history.FirstOrDefault()
            : _history.FirstOrDefault(row => row.OperationId == selectedId);
    }

    private void RebuildDiagram()
    {
        _diagramRows.Clear();
        var record = SelectedHistory?.Record;
        if (record is null)
        {
            ComparisonSummary = "Select a history record to inspect its structures.";
            OnPropertyChanged(nameof(HasNoDiagramRows));
            return;
        }

        var selected = SelectedSnapshot.Kind switch
        {
            StructureSnapshotKind.Source => record.SourceSnapshot,
            StructureSnapshotKind.Proposed => record.ProposedSnapshot,
            StructureSnapshotKind.Applied => record.AppliedSnapshot,
            StructureSnapshotKind.Current => _currentSnapshot,
            _ => null,
        };
        if (selected is null)
        {
            ComparisonSummary = SelectedSnapshot.Kind == StructureSnapshotKind.Current
                ? "Capture current structure to compare it with the last applied state."
                : "That structure is unavailable because the operation did not apply successfully.";
            OnPropertyChanged(nameof(HasNoDiagramRows));
            return;
        }

        var baseline = SelectedSnapshot.Kind == StructureSnapshotKind.Current
            ? record.AppliedSnapshot ?? record.ProposedSnapshot
            : record.SourceSnapshot;
        var changes = _comparisonService.Compare(baseline, selected);
        var changeByPath = changes
            .Where(change => change.AfterRelativePath is not null)
            .GroupBy(change => change.AfterRelativePath!, PathComparer)
            .ToDictionary(group => group.Key, group => group.First().Kind, PathComparer);
        var query = DiagramSearch?.Trim();
        var nodes = selected.Nodes
            .Where(node =>
                string.IsNullOrWhiteSpace(query) ||
                node.RelativePath.Contains(query, PathComparison))
            .Take(StructureLimits.MaximumVisibleNodes)
            .ToArray();
        foreach (var node in nodes)
        {
            var depth = node.RelativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).Length - 1;
            var marker = node.IsDirectory ? "[Folder]" : "[File]";
            var change = changeByPath.TryGetValue(node.RelativePath, out var kind)
                ? kind.ToString()
                : "Unclassified";
            _diagramRows.Add(new StructureDiagramRow(
                node.RelativePath,
                $"{new string(' ', Math.Min(depth, 8) * 2)}{marker} {Path.GetFileName(node.RelativePath)}",
                node.IsDirectory ? "Folder" : "File",
                change));
        }

        var removed = changes
            .Where(change => change.Kind == StructureChangeKind.Removed &&
                             change.BeforeRelativePath is not null &&
                             (string.IsNullOrWhiteSpace(query) ||
                              change.BeforeRelativePath.Contains(query, PathComparison)))
            .Take(Math.Max(0, StructureLimits.MaximumVisibleNodes - _diagramRows.Count));
        foreach (var change in removed)
        {
            _diagramRows.Add(new StructureDiagramRow(
                change.BeforeRelativePath!,
                $"[Removed] {change.BeforeRelativePath}",
                change.IsDirectory ? "Folder" : "File",
                "Removed"));
        }

        ComparisonSummary = string.Join(
            ", ",
            Enum.GetValues<StructureChangeKind>().Select(kind =>
                $"{kind}: {changes.Count(change => change.Kind == kind)}")) +
            (selected.Nodes.Count > StructureLimits.MaximumVisibleNodes
                ? $". View bounded to {StructureLimits.MaximumVisibleNodes} nodes; use diagram search."
                : ".");
        OnPropertyChanged(nameof(HasNoDiagramRows));
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        IsBusy = true;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "Structure operation cancelled safely.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException)
        {
            StatusText = exception switch
            {
                DirectoryNotFoundException => "The selected root is unavailable.",
                UnauthorizedAccessException => "Access to the selected root or local history was denied.",
                InvalidDataException => exception.Message,
                _ => "The structure operation could not be completed safely.",
            };
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }

            cancellation.Dispose();
            IsBusy = false;
        }
    }

    private void Cancel() => _cancellation?.Cancel();

    private RestructuringPlan? CurrentPlan
    {
        get => _currentPlan;
        set
        {
            if (SetProperty(ref _currentPlan, value))
            {
                RequestApplyCommand.NotifyCanExecuteChanged();
                ConfirmApplyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private void NotifyCommandState()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        ProposeAgainCommand.NotifyCanExecuteChanged();
        RequestApplyCommand.NotifyCanExecuteChanged();
        ConfirmApplyCommand.NotifyCanExecuteChanged();
        CancelApplyConfirmationCommand.NotifyCanExecuteChanged();
        CaptureCurrentCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RequestClearHistoryCommand.NotifyCanExecuteChanged();
        ConfirmClearHistoryCommand.NotifyCanExecuteChanged();
        CancelClearHistoryCommand.NotifyCanExecuteChanged();
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
