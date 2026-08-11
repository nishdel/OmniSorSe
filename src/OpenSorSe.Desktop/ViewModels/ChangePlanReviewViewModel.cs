#pragma warning disable CS1591

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.ViewModels;

public enum ChangePlanIssueFilter
{
    All,
    Warnings,
    Conflicts,
    InvalidOrStale,
}

public sealed record ChangeActionTypeFilterOption(ChangeActionType? Type, string Label);

public sealed class ChangePlanActionRow : ViewModelBase
{
    private ProposedChangeAction _action;
    private string _editedDestinationPath;
    private string? _editedFileName;

    public ChangePlanActionRow(ProposedChangeAction action)
    {
        _action = action;
        _editedDestinationPath = action.DestinationPath;
        _editedFileName = action.ProposedFileName;
    }

    public ProposedChangeAction Action => _action;
    public string ActionId => _action.ActionId;
    public string CurrentPath => _action.SourcePath ?? "(new directory)";
    public string ProposedPath => _action.DestinationPath;
    public ChangeActionType ActionType => _action.ActionType;
    public string ActionTypeText => _action.ActionType switch
    {
        ChangeActionType.RenameFile => "Rename file",
        ChangeActionType.MoveFile => "Move file",
        ChangeActionType.CreateDirectory => "Create directory",
        _ => _action.ActionType.ToString(),
    };
    public ChangeSuggestionSource SuggestionSource => _action.SuggestionSource;
    public string Reason => _action.Reason;
    public ChangeApprovalState ApprovalState => _action.ApprovalState;
    public ChangeValidationState ValidationState => _action.ValidationState;
    public bool HasWarnings => _action.Warnings.Count > 0;
    public bool HasConflicts => _action.Conflicts.Count > 0;
    public bool HasBlockingConflict => _action.Conflicts.Any(conflict => conflict.IsBlocking);
    public string WarningText => string.Join(Environment.NewLine, _action.Warnings);
    public string ConflictText => string.Join(
        Environment.NewLine,
        _action.Conflicts.Select(conflict => $"{conflict.Category}: {conflict.Message}"));
    public bool CanEditFileName => _action.ActionType == ChangeActionType.RenameFile;
    public bool WasUserEdited => _action.WasUserEdited;

    public string EditedDestinationPath
    {
        get => _editedDestinationPath;
        set => SetProperty(ref _editedDestinationPath, value);
    }

    public string? EditedFileName
    {
        get => _editedFileName;
        set => SetProperty(ref _editedFileName, value);
    }

    public void Replace(ProposedChangeAction action)
    {
        _action = action;
        _editedDestinationPath = action.DestinationPath;
        _editedFileName = action.ProposedFileName;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>Owns the explicit review, validation, confirmation, apply, result, and Undo workflow.</summary>
public sealed class ChangePlanReviewViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyList<ChangeActionTypeFilterOption> TypeFilterOptions =
    [
        new(null, "All actions"),
        new(ChangeActionType.RenameFile, "Renames"),
        new(ChangeActionType.MoveFile, "Moves"),
        new(ChangeActionType.CreateDirectory, "Folders"),
    ];
    private readonly IChangePlanValidator? _validator;
    private readonly IChangePlanExecutionService? _executionService;
    private readonly IChangePlanStore? _planStore;
    private readonly ObservableCollection<ChangePlanActionRow> _actions = [];
    private ChangePlan? _currentPlan;
    private ChangePlanActionRow? _selectedAction;
    private ChangeActionTypeFilterOption _selectedTypeFilter = TypeFilterOptions[0];
    private ChangePlanIssueFilter _selectedIssueFilter;
    private ChangePlanValidationResult? _validation;
    private ChangePlanExecutionResult? _lastExecution;
    private ChangePlanUndoResult? _lastUndo;
    private CancellationTokenSource? _operationCancellation;
    private bool _isBusy;
    private bool _isApplyConfirmationPending;
    private string _statusText = "No Change Plan is loaded.";
    private string _progressText = "No operation is active.";

    public ChangePlanReviewViewModel(
        IChangePlanValidator? validator = null,
        IChangePlanExecutionService? executionService = null,
        IChangePlanStore? planStore = null)
    {
        _validator = validator;
        _executionService = executionService;
        _planStore = planStore;
        Actions = new ReadOnlyObservableCollection<ChangePlanActionRow>(_actions);
        AvailableTypeFilters = TypeFilterOptions;
        AvailableIssueFilters = Enum.GetValues<ChangePlanIssueFilter>();
        ApproveAllSafeCommand = new RelayCommand(ApproveAllSafe, () => CurrentPlan is not null && !IsBusy);
        DeselectAllCommand = new RelayCommand(DeselectAll, () => CurrentPlan is not null && !IsBusy);
        ApproveSelectedCommand = new RelayCommand(
            () => SetSelectedApproval(ChangeApprovalState.Approved),
            () => SelectedAction is not null && !IsBusy);
        RejectSelectedCommand = new RelayCommand(
            () => SetSelectedApproval(ChangeApprovalState.Rejected),
            () => SelectedAction is not null && !IsBusy);
        SaveEditCommand = new AsyncRelayCommand(SaveSelectedEditAsync, () => SelectedAction is not null && !IsBusy);
        ValidatePlanCommand = new AsyncRelayCommand(ValidateAsync, () => CurrentPlan is not null && !IsBusy);
        RequestApplyCommand = new RelayCommand(RequestApply, () => CanApply);
        ConfirmApplyCommand = new AsyncRelayCommand(
            ApplyAsync,
            () => IsApplyConfirmationPending && CanApply && !IsBusy);
        CancelApplyCommand = new RelayCommand(
            CancelApplyConfirmation,
            () => IsApplyConfirmationPending && !IsBusy);
        CancelOperationCommand = new RelayCommand(
            () => _operationCancellation?.Cancel(),
            () => IsBusy);
        UndoCommand = new AsyncRelayCommand(
            UndoAsync,
            () => LastExecution?.Operation.UndoAvailable == true && !IsBusy);
        ReturnCommand = new RelayCommand(() => ReturnRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? ReturnRequested;
    public ReadOnlyObservableCollection<ChangePlanActionRow> Actions { get; }
    public IReadOnlyList<ChangeActionTypeFilterOption> AvailableTypeFilters { get; }
    public IReadOnlyList<ChangePlanIssueFilter> AvailableIssueFilters { get; }

    public ChangePlan? CurrentPlan
    {
        get => _currentPlan;
        private set
        {
            if (SetProperty(ref _currentPlan, value))
            {
                NotifyPlanProperties();
            }
        }
    }

    public bool HasPlan => CurrentPlan is not null;
    public string PlanId => CurrentPlan?.PlanId ?? "No plan";
    public string RootPath => CurrentPlan?.RootPath ?? string.Empty;
    public string PlanStatus => CurrentPlan?.Status.ToString() ?? "Unavailable";
    public int ApprovedCount => CurrentPlan?.ApprovedActionCount ?? 0;
    public int RejectedCount => CurrentPlan?.RejectedActionCount ?? 0;
    public int PendingCount => CurrentPlan?.Actions.Count(action => action.ApprovalState == ChangeApprovalState.Pending) ?? 0;
    public int InvalidCount => CurrentPlan?.Actions.Count(action => action.ValidationState is ChangeValidationState.Invalid or ChangeValidationState.Stale) ?? 0;
    public int ConflictCount => CurrentPlan?.Actions.Count(action => action.Conflicts.Any(conflict => conflict.IsBlocking)) ?? 0;
    public int WarningCount => CurrentPlan?.Actions.Sum(action => action.Warnings.Count) ?? 0;
    public int RenameCount => ApprovedActions(ChangeActionType.RenameFile);
    public int MoveCount => ApprovedActions(ChangeActionType.MoveFile);
    public int DirectoryCount => ApprovedActions(ChangeActionType.CreateDirectory);
    public int ExcludedCount => CurrentPlan?.Actions.Count(action => action.ApprovalState != ChangeApprovalState.Approved) ?? 0;
    public bool CouldOverwrite => CurrentPlan?.Actions.Any(action =>
        action.Conflicts.Any(conflict => conflict.Category == ChangeConflictCategory.DestinationOccupied)) == true;
    public string ConfirmationSummary =>
        $"{RenameCount} rename(s), {MoveCount} move(s), {DirectoryCount} folder(s), " +
        $"{ExcludedCount} excluded, {WarningCount} warning(s). Overwrite: {(CouldOverwrite ? "blocked conflict" : "no")}.";
    public bool CanApply =>
        CurrentPlan is not null &&
        _validation?.CanApply == true &&
        CurrentPlan.ValidatedAtUtc is not null &&
        !CurrentPlan.HasBlockingConflicts &&
        !IsBusy;

    public ChangePlanActionRow? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (SetProperty(ref _selectedAction, value))
            {
                NotifyCommands();
            }
        }
    }

    public ChangeActionTypeFilterOption SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetProperty(ref _selectedTypeFilter, value))
            {
                RebuildRows();
            }
        }
    }

    public ChangePlanIssueFilter SelectedIssueFilter
    {
        get => _selectedIssueFilter;
        set
        {
            if (SetProperty(ref _selectedIssueFilter, value))
            {
                RebuildRows();
            }
        }
    }

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

    public bool IsApplyConfirmationPending
    {
        get => _isApplyConfirmationPending;
        private set
        {
            if (SetProperty(ref _isApplyConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public ChangePlanExecutionResult? LastExecution
    {
        get => _lastExecution;
        private set
        {
            if (SetProperty(ref _lastExecution, value))
            {
                OnPropertyChanged(nameof(ResultSummary));
                OnPropertyChanged(nameof(UndoAvailable));
                UndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ChangePlanUndoResult? LastUndo
    {
        get => _lastUndo;
        private set
        {
            if (SetProperty(ref _lastUndo, value))
            {
                OnPropertyChanged(nameof(ResultSummary));
            }
        }
    }

    public bool UndoAvailable => LastExecution?.Operation.UndoAvailable == true;
    public string ResultSummary => LastUndo?.Summary ?? LastExecution?.Summary ?? string.Empty;

    public IRelayCommand ApproveAllSafeCommand { get; }
    public IRelayCommand DeselectAllCommand { get; }
    public IRelayCommand ApproveSelectedCommand { get; }
    public IRelayCommand RejectSelectedCommand { get; }
    public IAsyncRelayCommand SaveEditCommand { get; }
    public IAsyncRelayCommand ValidatePlanCommand { get; }
    public IRelayCommand RequestApplyCommand { get; }
    public IAsyncRelayCommand ConfirmApplyCommand { get; }
    public IRelayCommand CancelApplyCommand { get; }
    public IRelayCommand CancelOperationCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    public IRelayCommand ReturnCommand { get; }

    public async Task LoadAsync(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        CurrentPlan = plan;
        _validation = null;
        LastExecution = null;
        LastUndo = null;
        IsApplyConfirmationPending = false;
        SelectedTypeFilter = TypeFilterOptions[0];
        SelectedIssueFilter = ChangePlanIssueFilter.All;
        RebuildRows();
        StatusText = "Review every proposed action, approve or reject it, then validate the plan.";
        if (_planStore is not null)
        {
            await _planStore.UpsertAsync(plan, CancellationToken.None);
        }
    }

    private void ApproveAllSafe()
    {
        UpdateActions(action =>
            (action.ValidationState is ChangeValidationState.Valid or ChangeValidationState.Warning) &&
            action.Conflicts.All(conflict => !conflict.IsBlocking)
                ? action with { ApprovalState = ChangeApprovalState.Approved }
                : action);
        StatusText = "All currently safe actions were approved. Validate Plan before applying.";
    }

    private void DeselectAll()
    {
        UpdateActions(action => action with { ApprovalState = ChangeApprovalState.Rejected });
        StatusText = "All actions were excluded from this apply.";
    }

    private void SetSelectedApproval(ChangeApprovalState approval)
    {
        if (SelectedAction is null)
        {
            return;
        }

        var id = SelectedAction.ActionId;
        UpdateActions(action => action.ActionId == id ? action with { ApprovalState = approval } : action);
        SelectedAction = _actions.FirstOrDefault(action => action.ActionId == id);
        StatusText = approval == ChangeApprovalState.Approved
            ? "The selected action is approved. Validate Plan before applying."
            : "The selected action is excluded.";
    }

    private async Task SaveSelectedEditAsync()
    {
        if (CurrentPlan is null || SelectedAction is null)
        {
            return;
        }

        var row = SelectedAction;
        string destination;
        try
        {
            destination = row.Action.ActionType == ChangeActionType.RenameFile &&
                          !string.IsNullOrWhiteSpace(row.EditedFileName)
                ? Path.Combine(
                    Path.GetDirectoryName(row.Action.SourcePath!)
                    ?? throw new InvalidDataException("The rename source has no parent folder."),
                    row.EditedFileName)
                : Path.IsPathRooted(row.EditedDestinationPath)
                    ? Path.GetFullPath(row.EditedDestinationPath)
                    : Path.GetFullPath(Path.Combine(CurrentPlan.RootPath, row.EditedDestinationPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusText = "The edited destination path is invalid.";
            return;
        }

        var id = row.ActionId;
        UpdateActions(action => action.ActionId == id
            ? action with
            {
                DestinationPath = destination,
                ProposedFileName = action.ActionType == ChangeActionType.RenameFile
                    ? Path.GetFileName(destination)
                    : action.ProposedFileName,
                SuggestionSource = ChangeSuggestionSource.ManualUserEdit,
                WasUserEdited = true,
                ValidationState = ChangeValidationState.NotValidated,
                Conflicts = [],
                Warnings = [],
            }
            : action);
        SelectedAction = _actions.FirstOrDefault(action => action.ActionId == id);
        StatusText = "The edit was saved. Validate Plan again before applying.";
        await PersistPlanAsync();
    }

    private async Task ValidateAsync()
    {
        if (CurrentPlan is null || _validator is null)
        {
            StatusText = "Plan validation is unavailable in this application context.";
            return;
        }

        IsBusy = true;
        try
        {
            _validation = await _validator.ValidateAsync(
                CurrentPlan,
                ChangePlanValidationPhase.Review,
                CancellationToken.None);
            CurrentPlan = _validation.Plan;
            RebuildRows();
            StatusText = _validation.Summary;
            await PersistPlanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RequestApply()
    {
        if (!CanApply)
        {
            return;
        }

        IsApplyConfirmationPending = true;
        StatusText = $"Final confirmation: {ConfirmationSummary}";
    }

    private void CancelApplyConfirmation()
    {
        IsApplyConfirmationPending = false;
        StatusText = "Apply confirmation was cancelled. The Change Plan remains reviewable.";
    }

    private async Task ApplyAsync()
    {
        if (CurrentPlan is null || _executionService is null || !IsApplyConfirmationPending || !CanApply)
        {
            return;
        }

        IsApplyConfirmationPending = false;
        IsBusy = true;
        _operationCancellation = new CancellationTokenSource();
        // Progress<T> may dispatch a queued callback after the producer has
        // returned. Serialize callbacks with the terminal transition so stale
        // progress can never replace the verified final operation state.
        var progressGate = new object();
        var progressFinished = false;
        var progress = new Progress<ChangeExecutionProgress>(value =>
        {
            lock (progressGate)
            {
                if (progressFinished)
                {
                    return;
                }

                ProgressText = value.Message;
                StatusText = $"{value.AttemptedActions}/{value.TotalActions} action(s) attempted.";
            }
        });
        try
        {
            var execution = await _executionService.ExecuteAsync(
                CurrentPlan,
                "Review Changes",
                progress,
                _operationCancellation.Token);
            lock (progressGate)
            {
                progressFinished = true;
                LastExecution = execution;
                StatusText = execution.Summary;
                ProgressText = execution.Operation.Status.ToString();
            }

            CurrentPlan = CurrentPlan with
            {
                Status = execution.Succeeded
                    ? ChangePlanStatus.Applied
                    : execution.Operation.Status == OperationStatus.RollbackPartiallyFailed
                        ? ChangePlanStatus.PartiallyApplied
                        : ChangePlanStatus.Failed,
            };
        }
        catch (OperationCanceledException)
        {
            lock (progressGate)
            {
                progressFinished = true;
                StatusText = "Cancellation was requested. Review Operation Details for the verified final state.";
            }
        }
        finally
        {
            lock (progressGate)
            {
                progressFinished = true;
            }

            _operationCancellation.Dispose();
            _operationCancellation = null;
            IsBusy = false;
            await PersistPlanAsync();
        }
    }

    private async Task UndoAsync()
    {
        if (_executionService is null || LastExecution is null)
        {
            return;
        }

        IsBusy = true;
        _operationCancellation = new CancellationTokenSource();
        try
        {
            LastUndo = await _executionService.UndoAsync(
                LastExecution.Operation.OperationId,
                null,
                null,
                _operationCancellation.Token);
            LastExecution = LastExecution with { Operation = LastUndo.Operation };
            StatusText = LastUndo.Summary;
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void UpdateActions(Func<ProposedChangeAction, ProposedChangeAction> update)
    {
        if (CurrentPlan is null)
        {
            return;
        }

        CurrentPlan = CurrentPlan with
        {
            Actions = Array.AsReadOnly(CurrentPlan.Actions.Select(update).ToArray()),
            Status = ChangePlanStatus.AwaitingReview,
            ValidatedAtUtc = null,
        };
        _validation = null;
        RebuildRows();
        _ = PersistPlanAsync();
    }

    private void RebuildRows()
    {
        var selectedId = SelectedAction?.ActionId;
        _actions.Clear();
        if (CurrentPlan is not null)
        {
            IEnumerable<ProposedChangeAction> filtered = CurrentPlan.Actions;
            if (SelectedTypeFilter.Type is { } type)
            {
                filtered = filtered.Where(action => action.ActionType == type);
            }

            filtered = SelectedIssueFilter switch
            {
                ChangePlanIssueFilter.Warnings => filtered.Where(action => action.Warnings.Count > 0),
                ChangePlanIssueFilter.Conflicts => filtered.Where(action => action.Conflicts.Count > 0),
                ChangePlanIssueFilter.InvalidOrStale => filtered.Where(action =>
                    action.ValidationState is ChangeValidationState.Invalid or ChangeValidationState.Stale),
                _ => filtered,
            };
            foreach (var action in filtered)
            {
                _actions.Add(new ChangePlanActionRow(action));
            }
        }

        SelectedAction = _actions.FirstOrDefault(action => action.ActionId == selectedId) ?? _actions.FirstOrDefault();
        NotifyPlanProperties();
    }

    private int ApprovedActions(ChangeActionType type) =>
        CurrentPlan?.Actions.Count(action =>
            action.ApprovalState == ChangeApprovalState.Approved &&
            action.ActionType == type) ?? 0;

    private async Task PersistPlanAsync()
    {
        if (_planStore is not null && CurrentPlan is not null)
        {
            try
            {
                await _planStore.UpsertAsync(CurrentPlan, CancellationToken.None);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
            {
                StatusText = "The Change Plan could not be saved. Apply is blocked until the plan can be persisted.";
                _validation = null;
                OnPropertyChanged(nameof(CanApply));
                NotifyCommands();
            }
        }
    }

    private void NotifyPlanProperties()
    {
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(PlanId));
        OnPropertyChanged(nameof(RootPath));
        OnPropertyChanged(nameof(PlanStatus));
        OnPropertyChanged(nameof(ApprovedCount));
        OnPropertyChanged(nameof(RejectedCount));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(InvalidCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(RenameCount));
        OnPropertyChanged(nameof(MoveCount));
        OnPropertyChanged(nameof(DirectoryCount));
        OnPropertyChanged(nameof(ExcludedCount));
        OnPropertyChanged(nameof(CouldOverwrite));
        OnPropertyChanged(nameof(ConfirmationSummary));
        OnPropertyChanged(nameof(CanApply));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        ApproveAllSafeCommand.NotifyCanExecuteChanged();
        DeselectAllCommand.NotifyCanExecuteChanged();
        ApproveSelectedCommand.NotifyCanExecuteChanged();
        RejectSelectedCommand.NotifyCanExecuteChanged();
        SaveEditCommand.NotifyCanExecuteChanged();
        ValidatePlanCommand.NotifyCanExecuteChanged();
        RequestApplyCommand.NotifyCanExecuteChanged();
        ConfirmApplyCommand.NotifyCanExecuteChanged();
        CancelApplyCommand.NotifyCanExecuteChanged();
        CancelOperationCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanApply));
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }
}
