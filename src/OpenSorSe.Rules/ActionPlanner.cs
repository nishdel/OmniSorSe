using System.Text;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Rules;

/// <summary>
/// Produces deterministic lexical operation plans without inspecting or changing the filesystem.
/// </summary>
public sealed class ActionPlanner : IActionPlanner
{
    private const string LoggerCategory = "Rules";
    private readonly IErrorHandler _errorHandler;
    private readonly ILogger _logger;
    private readonly IPathSemantics _pathSemantics;

    /// <summary>
    /// Initializes an action planner using shared diagnostics infrastructure.
    /// </summary>
    /// <param name="loggingService">The centralized logging service.</param>
    /// <param name="errorHandler">The handler for unexpected operation failures.</param>
    /// <param name="pathSemantics">The optional platform path comparison policy.</param>
    public ActionPlanner(
        ILoggingService loggingService,
        IErrorHandler errorHandler,
        IPathSemantics? pathSemantics = null)
    {
        ArgumentNullException.ThrowIfNull(loggingService);
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _logger = loggingService.CreateLogger(LoggerCategory);
        _pathSemantics = pathSemantics ?? PlatformServices.CurrentPathSemantics;
    }

    /// <inheritdoc />
    public Task<ActionPlanResult> PlanAsync(
        IReadOnlyCollection<RuleDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDecisions(decisions, cancellationToken);

        try
        {
            return Task.FromResult(Plan(decisions, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Action planning could not be completed due to an unexpected error.");
            _errorHandler.Report(new ApplicationError(
                LoggerCategory,
                "Action planning could not be completed due to an unexpected error.",
                ApplicationErrorSeverity.Error,
                exception));
            throw;
        }
    }

    private ActionPlanResult Plan(IReadOnlyCollection<RuleDecision> decisions, CancellationToken cancellationToken)
    {
        var operations = new List<PlannedOperation>();
        var issues = new List<ActionPlanningIssue>();
        long noAction = 0;
        long failed = 0;
        long moves = 0;
        long copies = 0;
        long renames = 0;
        long deletes = 0;
        var index = 0;

        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decision.Action is null)
            {
                failed++;
                var invalidActionIssue = Issue(
                    index,
                    decision.File?.FullPath ?? string.Empty,
                    ActionPlanningIssueKind.InvalidDecision,
                    "The decision does not contain an action.");
                issues.Add(invalidActionIssue);
                _logger.LogWarning("Action-planning issue {IssueKind}: {Message}", invalidActionIssue.Kind, invalidActionIssue.Message);
                index++;
                continue;
            }

            if (decision.Action.Kind == RuleActionKind.NoAction)
            {
                noAction++;
                index++;
                continue;
            }

            if (TryCreateOperation(decision, index, cancellationToken, out var operation, out var issue))
            {
                operations.Add(operation!);
                switch (operation!.Kind)
                {
                    case PlannedOperationKind.Move:
                        moves++;
                        break;
                    case PlannedOperationKind.Copy:
                        copies++;
                        break;
                    case PlannedOperationKind.Rename:
                        renames++;
                        break;
                    case PlannedOperationKind.Delete:
                        deletes++;
                        break;
                }
            }
            else
            {
                failed++;
                issues.Add(issue!);
                _logger.LogWarning("Action-planning issue {IssueKind}: {Message}", issue!.Kind, issue.Message);
            }

            index++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ActionPlanResult(
            operations.ToArray(),
            new ActionPlanningStatistics(decisions.Count, operations.Count, noAction, failed, moves, copies, renames, deletes, issues.Count),
            issues.ToArray());
    }

    private bool TryCreateOperation(
        RuleDecision decision,
        int index,
        CancellationToken cancellationToken,
        out PlannedOperation? operation,
        out ActionPlanningIssue? issue)
    {
        operation = null;
        issue = null;
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = decision.File?.FullPath ?? string.Empty;
        if (decision.File is null || decision.Action is null || string.IsNullOrWhiteSpace(filePath))
        {
            issue = Issue(index, filePath, ActionPlanningIssueKind.InvalidDecision, "The decision does not contain a valid source file.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(decision.SelectedRuleId))
        {
            issue = Issue(index, filePath, ActionPlanningIssueKind.InvalidDecision, "An actionable decision must identify its selected rule.");
            return false;
        }

        bool pathsMatch;
        switch (decision.Action.Kind)
        {
            case RuleActionKind.Move:
            case RuleActionKind.Copy:
                if (!TryCreateMoveOrCopyDestination(decision, index, out var destination, out issue))
                {
                    return false;
                }

                if (!TryComparePaths(filePath, destination!, out pathsMatch))
                {
                    issue = Issue(index, filePath, ActionPlanningIssueKind.InvalidDestinationPath, "The source or destination path is invalid.");
                    return false;
                }

                if (pathsMatch)
                {
                    issue = Issue(index, filePath, ActionPlanningIssueKind.SourceEqualsDestination, "The source and destination paths are the same.");
                    return false;
                }

                operation = CreateOperation(decision, index, decision.Action.Kind == RuleActionKind.Move ? PlannedOperationKind.Move : PlannedOperationKind.Copy, destination);
                return true;
            case RuleActionKind.Rename:
                if (!TryCreateRenameDestination(decision, index, cancellationToken, out destination, out issue))
                {
                    return false;
                }

                if (!TryComparePaths(filePath, destination!, out pathsMatch))
                {
                    issue = Issue(index, filePath, ActionPlanningIssueKind.InvalidDestinationPath, "The source or destination path is invalid.");
                    return false;
                }

                if (pathsMatch)
                {
                    issue = Issue(index, filePath, ActionPlanningIssueKind.SourceEqualsDestination, "The source and destination paths are the same.");
                    return false;
                }

                operation = CreateOperation(decision, index, PlannedOperationKind.Rename, destination);
                return true;
            case RuleActionKind.Delete:
                operation = CreateOperation(decision, index, PlannedOperationKind.Delete, null);
                return true;
            default:
                issue = Issue(index, filePath, ActionPlanningIssueKind.UnsupportedAction, "The proposed action is unsupported.");
                return false;
        }
    }

    private static bool TryCreateMoveOrCopyDestination(
        RuleDecision decision,
        int index,
        out string? destination,
        out ActionPlanningIssue? issue)
    {
        destination = null;
        issue = null;
        if (decision.File.Metadata is null || string.IsNullOrWhiteSpace(decision.File.Metadata.FileName))
        {
            issue = Issue(index, decision.File.FullPath, ActionPlanningIssueKind.MissingMetadata, "File metadata with a filename is required for this action.");
            return false;
        }

        var destinationDirectory = decision.Action.DestinationPath;
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Path.IsPathRooted(destinationDirectory))
        {
            issue = Issue(index, decision.File.FullPath, ActionPlanningIssueKind.InvalidDestinationPath, "The destination directory must be a non-empty rooted path.");
            return false;
        }

        try
        {
            destination = Path.Combine(destinationDirectory, decision.File.Metadata.FileName);
            return true;
        }
        catch (ArgumentException)
        {
            issue = Issue(index, decision.File.FullPath, ActionPlanningIssueKind.InvalidDestinationPath, "The destination path is invalid.");
            return false;
        }
    }

    private static bool TryCreateRenameDestination(
        RuleDecision decision,
        int index,
        CancellationToken cancellationToken,
        out string? destination,
        out ActionPlanningIssue? issue)
    {
        destination = null;
        issue = null;
        if (decision.File.Metadata is null || string.IsNullOrWhiteSpace(decision.File.Metadata.FileName))
        {
            issue = Issue(index, decision.File.FullPath, ActionPlanningIssueKind.MissingMetadata, "File metadata with a filename is required for rename.");
            return false;
        }

        string? sourceDirectory;
        try
        {
            sourceDirectory = Path.GetDirectoryName(decision.File.FullPath);
        }
        catch (ArgumentException)
        {
            issue = Issue(index, decision.File.FullPath, ActionPlanningIssueKind.InvalidDestinationPath, "The source path is invalid.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            issue = Issue(index, decision.File.FullPath, ActionPlanningIssueKind.InvalidDestinationPath, "The source directory cannot be determined.");
            return false;
        }

        if (!TryResolveNameTemplate(decision.File, decision.Action.NameTemplate, cancellationToken, out var name, out var kind, out var message))
        {
            issue = Issue(index, decision.File.FullPath, kind, message);
            return false;
        }

        try
        {
            destination = Path.Combine(sourceDirectory, name!);
            return true;
        }
        catch (ArgumentException)
        {
            issue = Issue(index, decision.File.FullPath, ActionPlanningIssueKind.InvalidDestinationPath, "The renamed destination path is invalid.");
            return false;
        }
    }

    private static bool TryResolveNameTemplate(
        FileEntry file,
        string? template,
        CancellationToken cancellationToken,
        out string? resolvedName,
        out ActionPlanningIssueKind kind,
        out string message)
    {
        resolvedName = null;
        kind = ActionPlanningIssueKind.InvalidNameTemplate;
        message = "The rename template is invalid.";
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var metadata = file.Metadata!;
        var builder = new StringBuilder();
        for (var position = 0; position < template.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (template[position] == '}')
            {
                return false;
            }

            if (template[position] != '{')
            {
                builder.Append(template[position++]);
                continue;
            }

            var closing = template.IndexOf('}', position + 1);
            if (closing < 0)
            {
                return false;
            }

            var token = template[(position + 1)..closing];
            switch (token)
            {
                case "name":
                    builder.Append(Path.GetFileNameWithoutExtension(metadata.FileName));
                    break;
                case "extension":
                    builder.Append(metadata.Extension.ToLowerInvariant());
                    break;
                case "category":
                    if (file.Classification is null)
                    {
                        kind = ActionPlanningIssueKind.MissingMetadata;
                        message = "A file classification is required by the category token.";
                        return false;
                    }

                    builder.Append(file.Classification.Category);
                    break;
                default:
                    return false;
            }

            position = closing + 1;
        }

        resolvedName = builder.ToString();
        if (string.IsNullOrWhiteSpace(resolvedName) || resolvedName is "." or ".." || ContainsDirectorySeparator(resolvedName))
        {
            return false;
        }

        if (resolvedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return true;
    }

    private static PlannedOperation CreateOperation(RuleDecision decision, int index, PlannedOperationKind kind, string? destination) =>
        new($"plan:{index}", kind, decision.File, decision.File.FullPath, destination, decision.SelectedRuleId, decision.SelectedRuleName, decision.SelectedRulePriority);

    private bool TryComparePaths(string source, string destination, out bool pathsMatch)
    {
        try
        {
            pathsMatch = _pathSemantics.Comparer.Equals(
                Path.GetFullPath(source),
                Path.GetFullPath(destination));
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            pathsMatch = false;
            return false;
        }
    }

    private static bool ContainsDirectorySeparator(string name) =>
        name.Contains('/') || name.Contains('\\') || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar);

    private static ActionPlanningIssue Issue(int index, string filePath, ActionPlanningIssueKind kind, string message) =>
        new(index, filePath, kind, message);

    private static void ValidateDecisions(IReadOnlyCollection<RuleDecision> decisions, CancellationToken cancellationToken)
    {
        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decision is null)
            {
                throw new ArgumentException("The decision collection cannot contain null entries.", nameof(decisions));
            }
        }
    }
}
