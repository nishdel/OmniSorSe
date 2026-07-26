using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Reviews explicit undo-record sessions and emits a confirmed undo request without executing it.
/// </summary>
public sealed class UndoHistoryViewModel : ViewModelBase
{
    private readonly ObservableCollection<UndoHistorySession> _sessions = [];
    private UndoExecutionResult? _lastUndoResult;
    private bool _isUndoConfirmationPending;
    private UndoHistorySession? _selectedSession;
    private string _statusText = "No file operations have been performed in this application session.";

    /// <summary>
    /// Initializes non-executing undo-review commands.
    /// </summary>
    public UndoHistoryViewModel()
    {
        Sessions = new ReadOnlyObservableCollection<UndoHistorySession>(_sessions);
        RequestUndoCommand = new RelayCommand(RequestUndoConfirmation, () => SelectedSession is not null);
        ConfirmUndoCommand = new RelayCommand(ConfirmUndo, () => IsUndoConfirmationPending && SelectedSession is not null);
        CancelUndoCommand = new RelayCommand(CancelUndoConfirmation, () => IsUndoConfirmationPending);
    }

    /// <summary>
    /// Occurs when a user confirms an explicit ordered undo-record request.
    /// </summary>
    public event EventHandler<IReadOnlyList<UndoRecord>>? UndoRequested;

    /// <summary>
    /// Gets caller-supplied undo sessions in caller-supplied order.
    /// </summary>
    public ReadOnlyObservableCollection<UndoHistorySession> Sessions { get; }

    /// <summary>
    /// Gets whether supplied operation-history sessions are available for review.
    /// </summary>
    public bool HasSessions => _sessions.Count > 0;

    /// <summary>
    /// Gets whether the current release should show its review-only operation-history explanation.
    /// </summary>
    public bool IsEmpty => !HasSessions;

    /// <summary>
    /// Gets the current-release explanation shown when no operation history exists.
    /// </summary>
    public string EmptyStateMessage => "OpenSorSe 1.0 does not expose generic rule execution or undo. Explicitly confirmed folder restructuring has separate records on Structure history; completed scan snapshots remain in Saved catalog.";

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
}
