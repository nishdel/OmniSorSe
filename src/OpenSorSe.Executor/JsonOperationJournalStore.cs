#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
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
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public JsonOperationJournalStore(string filePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An Operation Journal path is required.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonOperationJournalStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationJournalRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
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
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            _mutex.Release();
        }
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
            return [];
        }
    }

    private async Task SaveCoreAsync(
        IReadOnlyList<OperationJournalRecord> operations,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidDataException("The Operation Journal path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Access = FileAccess.Write,
                                 Mode = FileMode.CreateNew,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                             }))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new Envelope(OperationJournalSchema.CurrentVersion, operations),
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (new FileInfo(temporaryPath).Length > OperationJournalSchema.MaximumFileBytes)
            {
                throw new InvalidDataException("The Operation Journal exceeds its supported encoded size.");
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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
            operation.Summary.Length > OperationJournalSchema.MaximumMessageLength)
        {
            throw new InvalidDataException("An Operation Journal record is invalid.");
        }
    }

    private sealed record Envelope(
        int SchemaVersion,
        IReadOnlyList<OperationJournalRecord>? Operations);
}
