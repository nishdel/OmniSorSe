using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Reviews explicit undo-record sessions and emits a confirmed undo request without executing it.
/// </summary>
public sealed class UndoHistoryViewModel : ViewModelBase
{
    private readonly ObservableCollection<UndoHistorySession> _sessions = [];
    private readonly ObservableCollection<OperationJournalRecord> _operations = [];
    private readonly IOperationJournalStore? _journalStore;
    private readonly IChangePlanExecutionService? _executionService;
    private readonly IOperationReportExporter? _reportExporter;
    private readonly IClipboardService? _clipboardService;
    private UndoExecutionResult? _lastUndoResult;
    private bool _isUndoConfirmationPending;
    private UndoHistorySession? _selectedSession;
    private OperationJournalRecord? _selectedOperation;
    private bool _isBusy;
    private string _statusText = "No file operations have been performed in this application session.";

    /// <summary>
    /// Initializes non-executing undo-review commands.
    /// </summary>
    public UndoHistoryViewModel()
        : this(null, null, null, null)
    {
    }

    /// <summary>Initializes persistent Operation History and safe Undo support.</summary>
    public UndoHistoryViewModel(
        IOperationJournalStore? journalStore,
        IChangePlanExecutionService? executionService,
        IOperationReportExporter? reportExporter,
        IClipboardService? clipboardService)
    {
        _journalStore = journalStore;
        _executionService = executionService;
        _reportExporter = reportExporter;
        _clipboardService = clipboardService;
        Sessions = new ReadOnlyObservableCollection<UndoHistorySession>(_sessions);
        Operations = new ReadOnlyObservableCollection<OperationJournalRecord>(_operations);
        RequestUndoCommand = new RelayCommand(RequestUndoConfirmation, () => SelectedSession is not null);
        ConfirmUndoCommand = new RelayCommand(ConfirmUndo, () => IsUndoConfirmationPending && SelectedSession is not null);
        CancelUndoCommand = new RelayCommand(CancelUndoConfirmation, () => IsUndoConfirmationPending);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        RequestOperationUndoCommand = new RelayCommand(
            RequestOperationUndo,
            () => SelectedOperation?.UndoAvailable == true && !IsBusy);
        ConfirmOperationUndoCommand = new AsyncRelayCommand(
            UndoSelectedOperationAsync,
            () => IsUndoConfirmationPending &&
                  SelectedOperation?.UndoAvailable == true &&
                  !IsBusy);
        CopyReportCommand = new AsyncRelayCommand(
            CopySelectedReportAsync,
            () => SelectedOperation is not null &&
                  _reportExporter is not null &&
                  _clipboardService is not null &&
                  !IsBusy);
    }

    /// <summary>
    /// Occurs when a user confirms an explicit ordered undo-record request.
    /// </summary>
    public event EventHandler<IReadOnlyList<UndoRecord>>? UndoRequested;

    /// <summary>
    /// Gets caller-supplied undo sessions in caller-supplied order.
    /// </summary>
    public ReadOnlyObservableCollection<UndoHistorySession> Sessions { get; }

    /// <summary>Gets persistent operations newest first.</summary>
    public ReadOnlyObservableCollection<OperationJournalRecord> Operations { get; }

    /// <summary>Gets whether persistent Operation Journal entries exist.</summary>
    public bool HasOperations => _operations.Count > 0;

    /// <summary>
    /// Gets whether supplied operation-history sessions are available for review.
    /// </summary>
    public bool HasSessions => _sessions.Count > 0;

    /// <summary>
    /// Gets whether the current release should show its review-only operation-history explanation.
    /// </summary>
    public bool IsEmpty => !HasSessions && !HasOperations;

    /// <summary>
    /// Gets the current-release explanation shown when no operation history exists.
    /// </summary>
    public string EmptyStateMessage => "No v1.1 Operation Journal entries exist yet. OpenSorSe 1.0 does not expose generic rule execution or undo; legacy Structure history and Saved catalog records remain separate and readable.";

    /// <summary>
    /// Gets or sets the session currently selected for review.
    /// </summary>
    public UndoHistorySession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                RequestUndoCommand.NotifyCanExecuteChanged();
                ConfirmUndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the selected persistent operation.</summary>
    public OperationJournalRecord? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (SetProperty(ref _selectedOperation, value))
            {
                OnPropertyChanged(nameof(SelectedOperationSummary));
                OnPropertyChanged(nameof(SelectedOperationDetails));
                RequestOperationUndoCommand.NotifyCanExecuteChanged();
                ConfirmOperationUndoCommand.NotifyCanExecuteChanged();
                CopyReportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets a concise selected-operation summary.</summary>
    public string SelectedOperationSummary => SelectedOperation is null
        ? "Select an operation to inspect details."
        : $"{SelectedOperation.StartedAtUtc.LocalDateTime:g} · {SelectedOperation.Status} · " +
          $"{SelectedOperation.SucceededCount} succeeded, {SelectedOperation.FailedCount} failed, " +
          $"{SelectedOperation.SkippedCount} skipped, {SelectedOperation.RolledBackCount} rolled back.";

    /// <summary>Gets bounded action-level details for the selected operation.</summary>
    public string SelectedOperationDetails => SelectedOperation is null
        ? string.Empty
        : string.Join(
            Environment.NewLine + Environment.NewLine,
            SelectedOperation.Actions.Select(action =>
                $"{action.ActionType} · {action.ExecutionResult} · Undo {action.UndoStatus}" +
                $"{Environment.NewLine}Suggestion source: {action.SuggestionSource}" +
                $"{Environment.NewLine}Validation: {action.ValidationState}" +
                $"{Environment.NewLine}Rollback: {action.RollbackResult}" +
                $"{Environment.NewLine}{action.OriginalPath ?? "(new directory)"}" +
                $"{Environment.NewLine}→ {action.ActualResultingPath ?? action.IntendedDestinationPath}" +
                (string.IsNullOrWhiteSpace(action.AiModel) &&
                 string.IsNullOrWhiteSpace(action.AiRequestCorrelationId)
                    ? string.Empty
                    : $"{Environment.NewLine}AI correlation: {action.AiModel ?? "(none)"} / {action.AiRequestCorrelationId ?? "(none)"}") +
                (string.IsNullOrWhiteSpace(action.ErrorDetails)
                    ? string.Empty
                    : $"{Environment.NewLine}{action.ErrorCategory}: {action.ErrorDetails}") +
                (string.IsNullOrWhiteSpace(action.UndoConflictDetails)
                    ? string.Empty
                    : $"{Environment.NewLine}Undo conflict: {action.UndoConflictDetails}")));

    /// <summary>Gets whether persistent history work is active.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                RequestOperationUndoCommand.NotifyCanExecuteChanged();
                ConfirmOperationUndoCommand.NotifyCanExecuteChanged();
                CopyReportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether an explicit confirmation is required before emitting the current undo request.
    /// </summary>
    public bool IsUndoConfirmationPending
    {
        get => _isUndoConfirmationPending;
        private set
        {
            if (SetProperty(ref _isUndoConfirmationPending, value))
            {
                ConfirmUndoCommand.NotifyCanExecuteChanged();
                CancelUndoCommand.NotifyCanExecuteChanged();
                ConfirmOperationUndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets the last externally supplied undo result for display.
    /// </summary>
    public UndoExecutionResult? LastUndoResult
    {
        get => _lastUndoResult;
        private set => SetProperty(ref _lastUndoResult, value);
    }

    /// <summary>
    /// Gets the user-safe history status.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// Gets the command that begins explicit undo confirmation.
    /// </summary>
    public IRelayCommand RequestUndoCommand { get; }

    /// <summary>
    /// Gets the command that emits the confirmed undo request.
    /// </summary>
    public IRelayCommand ConfirmUndoCommand { get; }

    /// <summary>
    /// Gets the command that dismisses pending undo confirmation.
    /// </summary>
    public IRelayCommand CancelUndoCommand { get; }

    /// <summary>Gets the persistent history refresh command.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that requests whole-operation Undo confirmation.</summary>
    public IRelayCommand RequestOperationUndoCommand { get; }

    /// <summary>Gets the command that executes confirmed safe Undo.</summary>
    public IAsyncRelayCommand ConfirmOperationUndoCommand { get; }

    /// <summary>Gets the command that copies a human-readable debugging report.</summary>
    public IAsyncRelayCommand CopyReportCommand { get; }

    /// <summary>Loads persistent Operation Journal entries newest first.</summary>
    public async Task RefreshAsync()
    {
        if (_journalStore is null)
        {
            StatusText = EmptyStateMessage;
            return;
        }

        IsBusy = true;
        try
        {
            var selectedId = SelectedOperation?.OperationId;
            var operations = await _journalStore.ListAsync(CancellationToken.None);
            _operations.Clear();
            foreach (var operation in operations)
            {
                _operations.Add(operation);
            }

            SelectedOperation = _operations.FirstOrDefault(operation =>
                                    operation.OperationId == selectedId) ??
                                _operations.FirstOrDefault();
            StatusText = _operations.Count == 0
                ? EmptyStateMessage
                : $"{_operations.Count} persistent operation(s) available.";
            OnPropertyChanged(nameof(HasOperations));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            StatusText = "Operation History could not be loaded safely. Existing visible records were preserved.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Replaces the displayed history with validated caller-supplied sessions.
    /// </summary>
    /// <param name="sessions">Explicit sessions in caller-supplied order.</param>
    public void Load(IReadOnlyList<UndoHistorySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            if (session is null || string.IsNullOrWhiteSpace(session.SessionId) || session.CompletedAtUtc.Offset != TimeSpan.Zero ||
                session.Records is null || session.Records.Any(record => record is null) || !identifiers.Add(session.SessionId))
            {
                throw new ArgumentException("Undo sessions are invalid.", nameof(sessions));
            }
        }

        _sessions.Clear();
        foreach (var session in sessions)
        {
            _sessions.Add(session);
        }

        SelectedSession = null;
        LastUndoResult = null;
        IsUndoConfirmationPending = false;
        StatusText = _sessions.Count == 0 ? EmptyStateMessage : $"{_sessions.Count} operation-history session(s) available.";
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Presents an externally produced undo result without altering session history.
    /// </summary>
    /// <param name="result">The result returned by a later undo-execution stage.</param>
    public void PresentUndoResult(UndoExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        LastUndoResult = result;
        StatusText = result.WasCancelled ? "Undo was cancelled." : "Undo result received.";
    }

    private void RequestUndoConfirmation()
    {
        if (SelectedSession is not null)
        {
            IsUndoConfirmationPending = true;
            StatusText = "Confirm undo to continue.";
        }
    }

    private void ConfirmUndo()
    {
        if (SelectedSession is null || !IsUndoConfirmationPending)
        {
            return;
        }

        UndoRequested?.Invoke(this, Array.AsReadOnly(SelectedSession.Records.ToArray()));
        IsUndoConfirmationPending = false;
        StatusText = "Undo requested.";
    }

    private void CancelUndoConfirmation()
    {
        IsUndoConfirmationPending = false;
        StatusText = "Undo confirmation cancelled.";
    }

    private void RequestOperationUndo()
    {
        if (SelectedOperation?.UndoAvailable != true)
        {
            return;
        }

        IsUndoConfirmationPending = true;
        StatusText = "Confirm Undo. Current paths and identities will be revalidated; newer data will never be overwritten.";
    }

    private async Task UndoSelectedOperationAsync()
    {
        if (_executionService is null ||
            SelectedOperation is null ||
            !IsUndoConfirmationPending)
        {
            return;
        }

        var operationId = SelectedOperation.OperationId;
        IsUndoConfirmationPending = false;
        IsBusy = true;
        try
        {
            var result = await _executionService.UndoAsync(
                operationId,
                null,
                null,
                CancellationToken.None);
            StatusText = result.Summary;
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
            SelectedOperation = _operations.FirstOrDefault(operation =>
                operation.OperationId == operationId);
        }
    }

    private async Task CopySelectedReportAsync()
    {
        if (SelectedOperation is null ||
            _reportExporter is null ||
            _clipboardService is null)
        {
            return;
        }

        var report = _reportExporter.Export(SelectedOperation);
        try
        {
            await _clipboardService.SetTextAsync(report, CancellationToken.None);
            StatusText = "The operation report was copied. Inspect it before sharing because it contains file paths.";
        }
        catch (InvalidOperationException)
        {
            StatusText = "The operation report could not be copied.";
        }
    }
}
