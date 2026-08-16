#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Persistence;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Persists attempted operations after every durable state transition.</summary>
public sealed class JsonOperationJournalStore : IOperationJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };
    private readonly string _filePath;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly IRecoverySafetyState _recoverySafety;

    public JsonOperationJournalStore(
        string filePath,
        ILoggingService loggingService,
        IRecoverySafetyState? recoverySafety = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An Operation Journal path is required.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _fileAccess = new ApplicationFileAccessCoordinator(_filePath);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonOperationJournalStore));
        _recoverySafety = recoverySafety ?? RecoverySafetyState.Unmanaged;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationJournalRecord>> ListAsync(CancellationToken cancellationToken)
    {
        using var access = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationJournalRecord?> GetAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return null;
        }

        var operations = await ListAsync(cancellationToken).ConfigureAwait(false);
        return operations.FirstOrDefault(operation =>
            string.Equals(operation.OperationId, operationId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(OperationJournalRecord operation, CancellationToken cancellationToken)
    {
        Validate(operation);
        using var access = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var operations = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
        operations.RemoveAll(candidate =>
            string.Equals(candidate.OperationId, operation.OperationId, StringComparison.Ordinal));
        operations.Add(operation);
        var retained = operations
            .OrderByDescending(candidate => candidate.StartedAtUtc)
            .ThenBy(candidate => candidate.OperationId, StringComparer.Ordinal)
            .Take(OperationJournalSchema.MaximumOperations)
            .ToArray();
        await SaveCoreAsync(retained, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OperationJournalRecord>> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            if (new FileInfo(_filePath).Length > OperationJournalSchema.MaximumFileBytes)
            {
                throw new InvalidDataException("The Operation Journal exceeds its supported size.");
            }

            await using var stream = File.OpenRead(_filePath);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            IReadOnlyList<OperationJournalRecord> records;
            var migrated = false;
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                records = document.RootElement.Deserialize<IReadOnlyList<OperationJournalRecord>>(JsonOptions) ?? [];
                migrated = true;
            }
            else
            {
                var envelope = document.RootElement.Deserialize<Envelope>(JsonOptions);
                if (envelope is null ||
                    envelope.SchemaVersion is < 0 or > OperationJournalSchema.CurrentVersion ||
                    envelope.Operations is null)
                {
                    throw new InvalidDataException("The Operation Journal format is unsupported.");
                }

                records = envelope.Operations;
                migrated = envelope.SchemaVersion < OperationJournalSchema.CurrentVersion;
            }

            if (records.Count > OperationJournalSchema.MaximumOperations)
            {
                throw new InvalidDataException("The Operation Journal contains too many records.");
            }

            var normalized = records
                .Select(record => record with { SchemaVersion = OperationJournalSchema.CurrentVersion })
                .ToArray();
            foreach (var record in normalized)
            {
                Validate(record);
            }

            if (migrated)
            {
                _logger.LogInformation(
                    "A legacy Operation Journal envelope was read as schema {SchemaVersion}.",
                    OperationJournalSchema.CurrentVersion);
            }

            return Array.AsReadOnly(normalized
                .OrderByDescending(record => record.StartedAtUtc)
                .ThenBy(record => record.OperationId, StringComparer.Ordinal)
                .ToArray());
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            _logger.LogWarning(exception, "The Operation Journal is malformed or unsupported.");
            var corruption = JsonStoreCorruption.Preserve(
                "Operation Journal",
                _filePath,
                JsonStoreAuthority.MutationRecovery,
                exception);
            _recoverySafety.Block(corruption);
            throw corruption;
        }
    }

    private async Task SaveCoreAsync(
        IReadOnlyList<OperationJournalRecord> operations,
        CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(
            _filePath,
            new Envelope(OperationJournalSchema.CurrentVersion, operations),
            JsonOptions,
            OperationJournalSchema.MaximumFileBytes,
            cancellationToken,
            static (_, _) => new InvalidDataException(
                "The Operation Journal exceeds its supported encoded size.")).ConfigureAwait(false);
    }

    private static void Validate(OperationJournalRecord operation)
    {
        if (operation is null ||
            operation.SchemaVersion != OperationJournalSchema.CurrentVersion ||
            string.IsNullOrWhiteSpace(operation.OperationId) ||
            string.IsNullOrWhiteSpace(operation.SourcePlanId) ||
            operation.StartedAtUtc.Offset != TimeSpan.Zero ||
            operation.CompletedAtUtc is { } completedAtUtc && completedAtUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(operation.OpenSorSeVersion) ||
            string.IsNullOrWhiteSpace(operation.InitiatingFeature) ||
            string.IsNullOrWhiteSpace(operation.AffectedRootFolder) ||
            !Path.IsPathRooted(operation.AffectedRootFolder) ||
            operation.AffectedRootFolder.Length > ChangePlanSchema.MaximumPathLength ||
            !Enum.IsDefined(operation.Status) ||
            operation.Actions is null ||
            operation.Actions.Count > ChangePlanSchema.MaximumActions ||
            operation.Actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() != operation.Actions.Count ||
            operation.Actions.Any(action =>
                action is null ||
                string.IsNullOrWhiteSpace(action.ActionId) ||
                action.ActionId.Length > 256 ||
                !Enum.IsDefined(action.ActionType) ||
                !Enum.IsDefined(action.SuggestionSource) ||
                !Enum.IsDefined(action.ValidationState) ||
                !Enum.IsDefined(action.ExecutionResult) ||
                !Enum.IsDefined(action.ErrorCategory) ||
                !Enum.IsDefined(action.RollbackResult) ||
                !Enum.IsDefined(action.UndoStatus) ||
                action.OriginalPath is { } original &&
                    (!Path.IsPathRooted(original) || original.Length > ChangePlanSchema.MaximumPathLength) ||
                string.IsNullOrWhiteSpace(action.IntendedDestinationPath) ||
                !Path.IsPathRooted(action.IntendedDestinationPath) ||
                action.IntendedDestinationPath.Length > ChangePlanSchema.MaximumPathLength ||
                action.ActualResultingPath is { } actual &&
                    (!Path.IsPathRooted(actual) || actual.Length > ChangePlanSchema.MaximumPathLength) ||
                action.ErrorDetails?.Length > OperationJournalSchema.MaximumMessageLength ||
                action.UndoConflictDetails?.Length > OperationJournalSchema.MaximumMessageLength ||
                action.AiModel?.Length > 256 ||
                action.AiRequestCorrelationId?.Length > 256 ||
                action.WarningDetails is null ||
                action.WarningDetails.Count > 1_000 ||
                action.WarningDetails.Any(warning =>
                    warning is null || warning.Length > OperationJournalSchema.MaximumMessageLength)) ||
            operation.Summary is null ||
            operation.Summary.Length > OperationJournalSchema.MaximumMessageLength ||
            !HasConsistentLifecycle(operation))
        {
            throw new InvalidDataException("An Operation Journal record is invalid.");
        }
    }

    private static bool HasConsistentLifecycle(OperationJournalRecord operation)
    {
        var inProgress = operation.Status is OperationStatus.Pending or OperationStatus.Running;
        if (inProgress != (operation.CompletedAtUtc is null))
        {
            return false;
        }

        return operation.Actions.All(action =>
            action.WasSkipped == (action.ExecutionResult == JournalActionResult.Skipped) &&
            (!action.RollbackAttempted || action.RollbackResult != JournalRollbackResult.NotRequired) &&
            (!action.UndoAvailable || action.UndoStatus != JournalUndoStatus.NotAvailable) &&
            (action.UndoTimestampUtc is null || action.UndoTimestampUtc.Value.Offset == TimeSpan.Zero));
    }

    private sealed record Envelope(
        int SchemaVersion,
        IReadOnlyList<OperationJournalRecord>? Operations);
}
