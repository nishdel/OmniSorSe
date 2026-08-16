using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.Application.Semantic;

/// <summary>Stores the bounded local semantic index in an atomic versioned JSON file.</summary>
public sealed class JsonSemanticIndexStore : ISemanticIndexStore
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumEntries = 10_000;
    private const long MaximumFileBytes = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _filePath;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>Initializes the index store at an explicit absolute application-data path.</summary>
    public JsonSemanticIndexStore(string filePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
        {
            throw new ArgumentException("An absolute semantic-index path is required.", nameof(filePath));
        }

        _filePath = filePath;
        _fileAccess = new ApplicationFileAccessCoordinator(filePath);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonSemanticIndexStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken)
    {
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
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
    public async Task ReplaceAsync(
        IReadOnlyList<SemanticIndexEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("The semantic index exceeds its supported capacity.");
        }

        var normalized = entries.Select(Validate).OrderBy(entry => entry.FullPath, PathComparer).ToArray();
        if (normalized.Select(entry => entry.FullPath).Distinct(PathComparer).Count() != normalized.Length)
        {
            throw new InvalidDataException("The semantic index contains duplicate paths.");
        }

        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicJsonFile.WriteAsync(
                _filePath,
                new SemanticEnvelope(CurrentSchemaVersion, normalized),
                JsonOptions,
                MaximumFileBytes,
                cancellationToken,
                static (_, _) => new InvalidDataException(
                    "The semantic index exceeds its supported encoded size.")).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        IReadOnlyCollection<string> fullPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);
        var removed = new HashSet<string>(fullPaths.Select(Path.GetFullPath), PathComparer);
        if (removed.Count == 0)
        {
            return;
        }

        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var remaining = entries.Where(entry => !removed.Contains(entry.FullPath)).ToArray();
            if (remaining.Length != entries.Count)
            {
                await SaveCoreAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
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

    private Task SaveCoreAsync(
        IReadOnlyList<SemanticIndexEntry> entries,
        CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(
            _filePath,
            new SemanticEnvelope(CurrentSchemaVersion, entries),
            JsonOptions,
            MaximumFileBytes,
            cancellationToken,
            static (_, _) => new InvalidDataException(
                "The semantic index exceeds its supported encoded size."));

    private async Task<IReadOnlyList<SemanticIndexEntry>> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            if (new FileInfo(_filePath).Length > MaximumFileBytes)
            {
                throw new InvalidDataException("The semantic index exceeds its supported encoded size.");
            }

            await using var stream = File.OpenRead(_filePath);
            var envelope = await JsonSerializer.DeserializeAsync<SemanticEnvelope>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (envelope is null ||
                envelope.SchemaVersion != CurrentSchemaVersion ||
                envelope.Entries is null ||
                envelope.Entries.Count > MaximumEntries)
            {
                throw new InvalidDataException("The semantic index format is unsupported.");
            }

            var entries = envelope.Entries.Select(Validate).OrderBy(entry => entry.FullPath, PathComparer).ToArray();
            return Array.AsReadOnly(entries);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            _logger.LogWarning(exception, "The local semantic index is malformed or unsupported and will be rebuilt.");
            return [];
        }
    }

    private static SemanticIndexEntry Validate(SemanticIndexEntry entry)
    {
        if (entry is null ||
            string.IsNullOrWhiteSpace(entry.FullPath) ||
            !Path.IsPathRooted(entry.FullPath) ||
            string.IsNullOrWhiteSpace(entry.SourceFingerprint) ||
            string.IsNullOrWhiteSpace(entry.IndexFingerprint) ||
            string.IsNullOrWhiteSpace(entry.FileName) ||
            entry.Tags is null ||
            entry.Tags.Count > 32 ||
            entry.MetadataTerms is null ||
            entry.MetadataTerms.Count > 256 ||
            entry.NativeTextTerms is null ||
            entry.NativeTextTerms.Count > 256 ||
            entry.OcrTextTerms is null ||
            entry.OcrTextTerms.Count > 256 ||
            entry.Vector is null ||
            entry.Vector.Count is < 1 or > 1024 ||
            entry.Vector.Any(value => !float.IsFinite(value)) ||
            entry.IndexedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("A semantic index entry is invalid.");
        }

        return entry with { FullPath = Path.GetFullPath(entry.FullPath) };
    }

    private static StringComparer PathComparer =>
        OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparer;

    private sealed record SemanticEnvelope(int SchemaVersion, IReadOnlyList<SemanticIndexEntry>? Entries);
}
