using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Rules.Models;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Rules;

/// <summary>
/// Resolves deterministic lexical conflicts within a plan without filesystem access.
/// </summary>
public sealed class ConflictResolver : IConflictResolver
{
    private const string LoggerCategory = "Rules";
    private readonly IErrorHandler _errorHandler;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a conflict resolver using shared diagnostics infrastructure.
    /// </summary>
    /// <param name="loggingService">The centralized logging service.</param>
    /// <param name="errorHandler">The handler for unexpected operation failures.</param>
    public ConflictResolver(ILoggingService loggingService, IErrorHandler errorHandler)
    {
        ArgumentNullException.ThrowIfNull(loggingService);
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _logger = loggingService.CreateLogger(LoggerCategory);
    }

    /// <inheritdoc />
    public Task<ConflictResolutionResult> ResolveAsync(
        IReadOnlyCollection<PlannedOperation> operations,
        ConflictResolutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOptions(options ?? ConflictResolutionOptions.Default);
        ValidateCollection(operations, cancellationToken);

        try
        {
            return Task.FromResult(Resolve(operations, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Conflict resolution could not be completed due to an unexpected error.");
            _errorHandler.Report(new ApplicationError(
                LoggerCategory,
                "Conflict resolution could not be completed due to an unexpected error.",
                ApplicationErrorSeverity.Error,
                exception));
            throw;
        }
    }

    private ConflictResolutionResult Resolve(IReadOnlyCollection<PlannedOperation> operations, CancellationToken cancellationToken)
    {
        var pathComparer = PlatformServices.CurrentPathSemantics.Comparer;
        var operationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var signatures = new Dictionary<OperationSignature, string>(new OperationSignatureComparer(pathComparer));
        var destinationOwners = new Dictionary<string, string>(pathComparer);
        var sourceOwners = new Dictionary<string, SourceOwnership>(pathComparer);
        var accepted = new List<PlannedOperation>();
        var issues = new List<ConflictResolutionIssue>();
        long rejected = 0;
        long moves = 0;
        long copies = 0;
        long renames = 0;
        long deletes = 0;
        long duplicateIds = 0;
        long duplicates = 0;
        long destinationConflicts = 0;
        long sourceConflicts = 0;
        var index = 0;

        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryNormalize(operation, pathComparer, out var normalized, out var invalidMessage) || normalized is null)
            {
                rejected++;
                AddIssue(issues, new ConflictResolutionIssue(index, operation.OperationId ?? string.Empty, ConflictResolutionIssueKind.InvalidOperation, invalidMessage ?? "The operation is invalid."));
                index++;
                continue;
            }

            var operationId = operation.OperationId;
            if (string.IsNullOrWhiteSpace(operationId))
            {
                rejected++;
                AddIssue(issues, new ConflictResolutionIssue(index, string.Empty, ConflictResolutionIssueKind.InvalidOperation, "The operation does not contain a valid identifier."));
                index++;
                continue;
            }

            if (operationIds.TryGetValue(operationId, out var duplicateId))
            {
                rejected++;
                duplicateIds++;
                AddIssue(issues, new ConflictResolutionIssue(index, operationId, ConflictResolutionIssueKind.DuplicateOperationId, "The operation identifier is already used in this plan.", duplicateId));
                index++;
                continue;
            }

            operationIds.Add(operationId, operationId);
            var signature = new OperationSignature(operation.Kind, normalized.SourcePath, normalized.DestinationPath);
            if (signatures.TryGetValue(signature, out var duplicateOperation))
            {
                rejected++;
                duplicates++;
                AddIssue(issues, new ConflictResolutionIssue(index, operationId, ConflictResolutionIssueKind.DuplicateOperation, "The operation duplicates an earlier accepted operation.", duplicateOperation));
                index++;
                continue;
            }

            if (normalized.DestinationPath is not null && destinationOwners.TryGetValue(normalized.DestinationPath, out var destinationOwner))
            {
                rejected++;
                destinationConflicts++;
                AddIssue(issues, new ConflictResolutionIssue(index, operationId, ConflictResolutionIssueKind.DestinationConflict, "The operation targets a destination already owned by an earlier operation.", destinationOwner));
                index++;
                continue;
            }

            if (sourceOwners.TryGetValue(normalized.SourcePath, out var sourceOwner) &&
                (normalized.MutatesSource || sourceOwner.FirstMutatingOperationId is not null))
            {
                rejected++;
                sourceConflicts++;
                var conflictingId = normalized.MutatesSource ? sourceOwner.FirstOperationId : sourceOwner.FirstMutatingOperationId;
                AddIssue(issues, new ConflictResolutionIssue(index, operationId, ConflictResolutionIssueKind.SourceConflict, "The operation has an unsafe source relationship with an earlier operation.", conflictingId));
                index++;
                continue;
            }

            accepted.Add(operation);
            signatures.Add(signature, operationId);
            if (normalized.DestinationPath is not null)
            {
                destinationOwners.Add(normalized.DestinationPath, operationId);
            }

            if (!sourceOwners.TryGetValue(normalized.SourcePath, out sourceOwner))
            {
                sourceOwner = new SourceOwnership(operationId, normalized.MutatesSource ? operationId : null);
                sourceOwners.Add(normalized.SourcePath, sourceOwner);
            }
            else if (normalized.MutatesSource)
            {
                sourceOwners[normalized.SourcePath] = sourceOwner with { FirstMutatingOperationId = operationId };
            }

            switch (operation.Kind)
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

            index++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ConflictResolutionResult(
            accepted.ToArray(),
            new ConflictResolutionStatistics(operations.Count, accepted.Count, rejected, moves, copies, renames, deletes, duplicateIds, duplicates, destinationConflicts, sourceConflicts, issues.Count),
            issues.ToArray());
    }

    private static bool TryNormalize(
        PlannedOperation operation,
        StringComparer pathComparer,
        out NormalizedOperation? normalized,
        out string? message)
    {
        normalized = null;
        message = null;
        if (operation is null || string.IsNullOrWhiteSpace(operation.OperationId) || operation.File is null ||
            string.IsNullOrWhiteSpace(operation.SourcePath) || string.IsNullOrWhiteSpace(operation.File.FullPath) ||
            !Enum.IsDefined(operation.Kind))
        {
            message = "The operation does not contain required planning values.";
            return false;
        }

        var requiresDestination = operation.Kind is PlannedOperationKind.Move or PlannedOperationKind.Copy or PlannedOperationKind.Rename;
        if ((requiresDestination && (string.IsNullOrWhiteSpace(operation.DestinationPath) || !Path.IsPathRooted(operation.DestinationPath))) ||
            (!requiresDestination && operation.DestinationPath is not null) || !Path.IsPathRooted(operation.SourcePath) || !Path.IsPathRooted(operation.File.FullPath))
        {
            message = "The operation contains an invalid required path.";
            return false;
        }

        try
        {
            var sourcePath = Path.GetFullPath(operation.SourcePath);
            var filePath = Path.GetFullPath(operation.File.FullPath);
            if (!pathComparer.Equals(sourcePath, filePath))
            {
                message = "The operation source path does not match its file path.";
                return false;
            }

            var destinationPath = requiresDestination ? Path.GetFullPath(operation.DestinationPath!) : null;
            if (destinationPath is not null && pathComparer.Equals(sourcePath, destinationPath))
            {
                message = "The operation source and destination paths must differ.";
                return false;
            }

            normalized = new NormalizedOperation(sourcePath, destinationPath, operation.Kind is not PlannedOperationKind.Copy);
            return true;
        }
        catch (ArgumentException)
        {
            message = "The operation contains an invalid path.";
            return false;
        }
    }

    private void AddIssue(List<ConflictResolutionIssue> issues, ConflictResolutionIssue issue)
    {
        issues.Add(issue);
        _logger.LogWarning("Conflict-resolution issue {IssueKind}: {Message}", issue.Kind, issue.Message);
    }

    private static void ValidateCollection(IReadOnlyCollection<PlannedOperation> operations, CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation is null)
            {
                throw new ArgumentException("The operation collection cannot contain null entries.", nameof(operations));
            }
        }
    }

    private static void ValidateOptions(ConflictResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Strategy))
        {
            throw new ArgumentException("The conflict-resolution strategy is unsupported.", nameof(options));
        }
    }

    private sealed record NormalizedOperation(string SourcePath, string? DestinationPath, bool MutatesSource);

    private sealed record OperationSignature(PlannedOperationKind Kind, string SourcePath, string? DestinationPath);

    private sealed class OperationSignatureComparer : IEqualityComparer<OperationSignature>
    {
        private readonly StringComparer _pathComparer;

        public OperationSignatureComparer(StringComparer pathComparer)
        {
            _pathComparer = pathComparer;
        }

        public bool Equals(OperationSignature? left, OperationSignature? right) =>
            ReferenceEquals(left, right) ||
            left is not null && right is not null && left.Kind == right.Kind &&
            _pathComparer.Equals(left.SourcePath, right.SourcePath) &&
            _pathComparer.Equals(left.DestinationPath, right.DestinationPath);

        public int GetHashCode(OperationSignature operation) =>
            HashCode.Combine(
                operation.Kind,
                _pathComparer.GetHashCode(operation.SourcePath),
                operation.DestinationPath is null ? 0 : _pathComparer.GetHashCode(operation.DestinationPath));
    }

    private sealed record SourceOwnership(string FirstOperationId, string? FirstMutatingOperationId);
}
