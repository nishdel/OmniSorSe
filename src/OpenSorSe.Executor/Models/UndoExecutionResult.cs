namespace OpenSorSe.Executor.Models;
/// <summary>Contains undo outcomes and cancellation state.</summary>
/// <param name="Outcomes">Attempted outcomes.</param><param name="Statistics">Aggregate counts.</param><param name="WasCancelled">Whether cancellation stopped later records.</param>
public sealed record UndoExecutionResult(IReadOnlyList<UndoExecutionOutcome> Outcomes, UndoExecutionStatistics Statistics, bool WasCancelled);
