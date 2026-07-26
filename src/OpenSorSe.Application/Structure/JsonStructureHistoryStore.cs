using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Structure;

/// <summary>Stores bounded restructuring history as atomic versioned local JSON.</summary>
public sealed class JsonStructureHistoryStore : IStructureHistoryStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>Initializes the store at an explicit absolute application-data path.</summary>
    public JsonStructureHistoryStore(string filePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
        {
            throw new ArgumentException("An absolute structure-history path is required.", nameof(filePath));
        }

        _filePath = filePath;
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonStructureHistoryStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RestructuringHistoryRecord>> ListAsync(CancellationToken cancellationToken)
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
    public async Task UpsertAsync(
        RestructuringHistoryRecord record,
        CancellationToken cancellationToken)
    {
        var validated = ValidateRecord(record);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .Where(candidate => !string.Equals(candidate.OperationId, validated.OperationId, StringComparison.Ordinal))
                .Append(validated)
                .OrderByDescending(candidate => candidate.StartedAtUtc)
                .ThenByDescending(candidate => candidate.OperationId, StringComparer.Ordinal)
                .Take(StructureLimits.MaximumHistoryRecords)
                .ToArray();
            await SaveCoreAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<IReadOnlyList<RestructuringHistoryRecord>> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            if (new FileInfo(_filePath).Length > StructureLimits.MaximumHistoryFileBytes)
            {
                throw new InvalidDataException("The structure-history file exceeds its supported size.");
            }

            await using var stream = File.OpenRead(_filePath);
            var envelope = await JsonSerializer.DeserializeAsync<HistoryEnvelope>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (envelope is null ||
                envelope.SchemaVersion != CurrentSchemaVersion ||
                envelope.Records is null ||
                envelope.Records.Count > StructureLimits.MaximumHistoryRecords)
            {
                throw new InvalidDataException("The structure-history format is unsupported.");
            }

            var records = envelope.Records.Select(ValidateRecord).ToArray();
            if (records.Select(record => record.OperationId).Distinct(StringComparer.Ordinal).Count() != records.Length)
            {
                throw new InvalidDataException("The structure history contains duplicate operation identities.");
            }

            return Array.AsReadOnly(records
                .OrderByDescending(record => record.StartedAtUtc)
                .ThenByDescending(record => record.OperationId, StringComparer.Ordinal)
                .ToArray());
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            _logger.LogWarning(
                exception,
                "The local structure history is malformed or unsupported and will not activate repeat protection.");
            return [];
        }
    }

    private async Task SaveCoreAsync(
        IReadOnlyList<RestructuringHistoryRecord> records,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidDataException("The structure-history path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new HistoryEnvelope(CurrentSchemaVersion, records),
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporaryPath).Length > StructureLimits.MaximumHistoryFileBytes)
            {
                throw new InvalidDataException("The structure history exceeds its supported encoded size.");
            }

            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static RestructuringHistoryRecord ValidateRecord(RestructuringHistoryRecord record)
    {
        if (record is null ||
            !IsBounded(record.OperationId, 128) ||
            !IsBounded(record.RootIdentity, 128) ||
            !IsBoundedPath(record.RootPath) ||
            !IsBounded(record.RootFingerprint, 128) ||
            record.StartedAtUtc.Offset != TimeSpan.Zero ||
            (record.CompletedAtUtc is { Offset: var completedOffset } &&
             completedOffset != TimeSpan.Zero) ||
            record.SourceSnapshot is null ||
            record.ProposedSnapshot is null ||
            record.IncludedFiles is null ||
            record.ItemOutcomes is null ||
            record.IncludedFiles.Count > StructureLimits.MaximumMovesPerOperation ||
            record.ItemOutcomes.Count > StructureLimits.MaximumMovesPerOperation ||
            !IsBounded(record.Summary, StructureLimits.MaximumMessageLength) ||
            !IsBounded(record.AlgorithmVersion, 64) ||
            !Enum.IsDefined(record.ApprovalState) ||
            !Enum.IsDefined(record.Status))
        {
            throw new InvalidDataException("A structure-history record is invalid.");
        }

        ValidateSnapshot(record.SourceSnapshot);
        ValidateSnapshot(record.ProposedSnapshot);
        if (record.AppliedSnapshot is not null)
        {
            ValidateSnapshot(record.AppliedSnapshot);
        }

        if (record.IncludedFiles.Any(move =>
                move is null ||
                !IsSafeRelativePath(move.SourceRelativePath) ||
                !IsSafeRelativePath(move.DestinationRelativePath)) ||
            record.ItemOutcomes.Any(outcome =>
                outcome is null ||
                !IsSafeRelativePath(outcome.SourceRelativePath) ||
                !IsSafeRelativePath(outcome.DestinationRelativePath) ||
                !Enum.IsDefined(outcome.Status) ||
                !IsBounded(outcome.Message, StructureLimits.MaximumMessageLength)))
        {
            throw new InvalidDataException("A structure-history item is invalid.");
        }

        return record with
        {
            RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(record.RootPath)),
        };
    }

    private static void ValidateSnapshot(FolderStructureSnapshot snapshot)
    {
        if (!IsBoundedPath(snapshot.RootPath) ||
            !IsBounded(snapshot.RootIdentity, 128) ||
            !IsBounded(snapshot.StructureFingerprint, 128) ||
            snapshot.CapturedAtUtc.Offset != TimeSpan.Zero ||
            snapshot.Nodes is null ||
            snapshot.Nodes.Count > StructureLimits.MaximumSnapshotNodes ||
            snapshot.Nodes.Any(node =>
                node is null ||
                !IsSafeRelativePath(node.RelativePath) ||
                node.Length < 0 ||
                node.LastWriteTimeUtc.Offset != TimeSpan.Zero ||
                !IsBounded(node.IdentityFingerprint, 128)))
        {
            throw new InvalidDataException("A structure snapshot is invalid.");
        }
    }

    internal static bool IsSafeRelativePath(string value)
    {
        if (!IsBounded(value, StructureLimits.MaximumStoredPathLength) ||
            Path.IsPathRooted(value))
        {
            return false;
        }

        var parts = value.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 &&
               parts.All(part => part is not "." and not ".." && !part.Any(char.IsControl));
    }

    private static bool IsBoundedPath(string value) =>
        IsBounded(value, StructureLimits.MaximumStoredPathLength) && Path.IsPathRooted(value);

    private static bool IsBounded(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl);

    private sealed record HistoryEnvelope(
        int SchemaVersion,
        IReadOnlyList<RestructuringHistoryRecord>? Records);
}
