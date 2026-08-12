using System.Text;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Produces a bounded human-readable operation report without file contents.</summary>
public sealed class OperationReportExporter : IOperationReportExporter
{
    /// <inheritdoc />
    public string Export(OperationJournalRecord operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var builder = new StringBuilder();
        builder.AppendLine("OmniSorSe Operation Report");
        builder.AppendLine($"Operation: {operation.OperationId}");
        builder.AppendLine($"Change Plan: {operation.SourcePlanId}");
        builder.AppendLine($"Version: {operation.OpenSorSeVersion}");
        builder.AppendLine($"Status: {operation.Status}");
        builder.AppendLine($"Started (UTC): {operation.StartedAtUtc:O}");
        builder.AppendLine($"Completed (UTC): {operation.CompletedAtUtc:O}");
        builder.AppendLine($"Initiated from: {operation.InitiatingFeature}");
        builder.AppendLine($"Root: {operation.AffectedRootFolder}");
        builder.AppendLine($"Summary: {operation.Summary}");
        builder.AppendLine(
            $"Counts: succeeded={operation.SucceededCount}, failed={operation.FailedCount}, skipped={operation.SkippedCount}, rolledBack={operation.RolledBackCount}");
        builder.AppendLine();
        builder.AppendLine("Actions");
        foreach (var action in operation.Actions)
        {
            builder.AppendLine($"- {action.ActionId}: {action.ActionType} / {action.ExecutionResult}");
            builder.AppendLine($"  Original: {action.OriginalPath ?? "(none)"}");
            builder.AppendLine($"  Intended: {action.IntendedDestinationPath}");
            builder.AppendLine($"  Actual: {action.ActualResultingPath ?? "(none)"}");
            builder.AppendLine($"  Source: {action.SuggestionSource}");
            builder.AppendLine($"  Validation: {action.ValidationState}");
            builder.AppendLine($"  Error: {action.ErrorCategory} {action.ErrorDetails}".TrimEnd());
            builder.AppendLine($"  Rollback: {action.RollbackResult}");
            builder.AppendLine($"  Undo: {action.UndoStatus} {action.UndoConflictDetails}".TrimEnd());
            if (!string.IsNullOrWhiteSpace(action.AiModel) ||
                !string.IsNullOrWhiteSpace(action.AiRequestCorrelationId))
            {
                builder.AppendLine(
                    $"  AI correlation: model={action.AiModel ?? "(none)"}, request={action.AiRequestCorrelationId ?? "(none)"}");
            }
        }

        return builder.ToString();
    }
}
