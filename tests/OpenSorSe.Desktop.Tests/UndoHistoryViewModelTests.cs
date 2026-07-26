using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies explicit undo-session review and confirmation behavior.
/// </summary>
public sealed class UndoHistoryViewModelTests
{
    /// <summary>
    /// Verifies sessions retain caller order and never reverse records automatically.
    /// </summary>
    [Fact]
    public void Load_PreservesCallerSessionAndRecordOrder()
    {
        var first = CreateRecord("undo:0", "operation:0");
        var second = CreateRecord("undo:1", "operation:1");
        var firstSession = CreateSession("session:0", [first, second]);
        var secondSession = CreateSession("session:1", [second]);
        var viewModel = new UndoHistoryViewModel();

        viewModel.Load([firstSession, secondSession]);

        Assert.Equal([firstSession, secondSession], viewModel.Sessions);
        Assert.Equal([first, second], viewModel.Sessions[0].Records);
        Assert.Equal("2 operation-history session(s) available.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies undo is emitted only after explicit confirmation and uses the selected record order.
    /// </summary>
    [Fact]
    public void ConfirmUndo_EmitsSelectedOrderedRecordsOnlyAfterConfirmation()
    {
        var first = CreateRecord("undo:0", "operation:0");
        var second = CreateRecord("undo:1", "operation:1");
        var session = CreateSession("session:0", [first, second]);
        var viewModel = new UndoHistoryViewModel();
        viewModel.Load([session]);
        viewModel.SelectedSession = session;
        IReadOnlyList<UndoRecord>? requestedRecords = null;
        viewModel.UndoRequested += (_, records) => requestedRecords = records;

        viewModel.ConfirmUndoCommand.Execute(null);
        Assert.Null(requestedRecords);
        viewModel.RequestUndoCommand.Execute(null);
        viewModel.ConfirmUndoCommand.Execute(null);

        var records = Assert.IsAssignableFrom<IReadOnlyList<UndoRecord>>(requestedRecords);
        Assert.Equal([first, second], records);
        Assert.False(viewModel.IsUndoConfirmationPending);
        Assert.Equal("Undo requested.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies invalid session input is rejected before it replaces the visible history.
    /// </summary>
    [Fact]
    public void Load_InvalidSessions_ThrowsWithoutMutatingExistingHistory()
    {
        var session = CreateSession("session:0", [CreateRecord("undo:0", "operation:0")]);
        var viewModel = new UndoHistoryViewModel();
        viewModel.Load([session]);

        Assert.Throws<ArgumentException>(() => viewModel.Load([session, session]));

        Assert.Equal([session], viewModel.Sessions);
    }

    /// <summary>
    /// Verifies an externally supplied undo result is displayed without modifying sessions.
    /// </summary>
    [Fact]
    public void PresentUndoResult_PreservesReviewedSessions()
    {
        var session = CreateSession("session:0", [CreateRecord("undo:0", "operation:0")]);
        var viewModel = new UndoHistoryViewModel();
        viewModel.Load([session]);
        var result = new UndoExecutionResult([], new UndoExecutionStatistics(1, 0, 0, 0, 0, 0, 0, 0), true);

        viewModel.PresentUndoResult(result);

        Assert.Same(result, viewModel.LastUndoResult);
        Assert.Equal([session], viewModel.Sessions);
        Assert.Equal("Undo was cancelled.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies v0.1 does not fabricate scan history when no file operation session has been supplied.
    /// </summary>
    [Fact]
    public void Constructor_ExplainsReviewOnlyEmptyOperationHistory()
    {
        var viewModel = new UndoHistoryViewModel();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasSessions);
        Assert.Contains("does not expose generic rule execution or undo", viewModel.EmptyStateMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Structure history", viewModel.EmptyStateMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Saved catalog", viewModel.EmptyStateMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.RequestUndoCommand.CanExecute(null));
    }

    private static UndoHistorySession CreateSession(string identifier, IReadOnlyList<UndoRecord> records) =>
        new(identifier, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), records);

    private static UndoRecord CreateRecord(string undoIdentifier, string operationIdentifier) =>
        new(undoIdentifier, operationIdentifier, UndoOperationKind.Move, "C:\\Original.txt", "C:\\Result.txt", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
}
