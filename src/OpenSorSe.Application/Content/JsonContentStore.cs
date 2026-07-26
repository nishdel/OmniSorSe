using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Content;

/// <summary>Stores bounded extracted content in an atomic versioned application-data JSON file.</summary>
public sealed class JsonContentStore : IContentStore
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumRecordCount = 2_000;
    private const long MaximumStoreBytes = 128L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Dictionary<string, string> _diagnosticSessions = new(PathComparer);

    /// <summary>Initializes the content store at an explicit absolute application-data path.</summary>
    public JsonContentStore(string filePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
        {
            throw new ArgumentException("An absolute local content-store path is required.", nameof(filePath));
        }

        _filePath = filePath;
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonContentStore));
    }

    /// <inheritdoc />
    public async Task<ContentRecord?> GetAsync(string fullPath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(fullPath);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(record => PathComparer.Equals(record.FullPath, normalizedPath));
            return AttachDiagnosticSession(record);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContentRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Order((await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .Select(AttachDiagnosticSession)
                .Where(record => record is not null)
                .Select(record => record!));
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ContentRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var normalized = NormalizeRecord(record);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(normalized.DiagnosticSessionId))
            {
                _diagnosticSessions[normalized.FullPath] = normalized.DiagnosticSessionId;
            }
            var updated = records
                .Where(candidate => !PathComparer.Equals(candidate.FullPath, normalized.FullPath))
                .Append(normalized)
                .OrderByDescending(candidate => candidate.IndexedAtUtc)
                .Take(MaximumRecordCount)
                .ToArray();
            await SaveCoreAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveMissingAsync(
        IReadOnlyCollection<string> knownPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(knownPaths);
        var known = new HashSet<string>(knownPaths.Select(NormalizePath), PathComparer);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var remaining = records.Where(record => known.Contains(record.FullPath)).ToArray();
            foreach (var path in _diagnosticSessions.Keys.Where(path => !known.Contains(path)).ToArray())
            {
                _diagnosticSessions.Remove(path);
            }
            if (remaining.Length != records.Count)
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
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
            _diagnosticSessions.Clear();
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<IReadOnlyList<ContentRecord>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            if (new FileInfo(_filePath).Length > MaximumStoreBytes)
            {
                throw new InvalidDataException("The local content store exceeds its supported bound.");
            }

            await using var stream = File.OpenRead(_filePath);
            var envelope = await JsonSerializer.DeserializeAsync<ContentEnvelope>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (envelope is null ||
                envelope.SchemaVersion != CurrentSchemaVersion ||
                envelope.Records is null ||
                envelope.Records.Count > MaximumRecordCount)
            {
                throw new InvalidDataException("The local content store has an unsupported format.");
            }

            var normalized = envelope.Records.Select(NormalizeRecord).ToArray();
            if (normalized.Select(record => record.FullPath).Distinct(PathComparer).Count() != normalized.Length)
            {
                throw new InvalidDataException("The local content store contains duplicate paths.");
            }

            return Array.AsReadOnly(normalized);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            _logger.LogWarning(exception, "The local content cache is malformed or unsupported and will be rebuilt.");
            return [];
        }
    }

    private async Task SaveCoreAsync(
        IReadOnlyList<ContentRecord> records,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidDataException("The local content-store path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new ContentEnvelope(CurrentSchemaVersion, Order(records)),
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporaryPath).Length > MaximumStoreBytes)
            {
                throw new InvalidDataException("The local content cache exceeds its supported encoded size.");
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

    private static ContentRecord NormalizeRecord(ContentRecord record)
    {
        if (record.SourceLength < 0 ||
            record.SourceLastWriteTimeUtc.Offset != TimeSpan.Zero ||
            record.IndexedAtUtc.Offset != TimeSpan.Zero ||
            record.Metadata is null ||
            record.Metadata.Count > 64 ||
            record.Tags is null ||
            record.Tags.Count > 32 ||
            record.OcrPages is null ||
            record.OcrPages.Count > 500 ||
            record.Warnings is null ||
            record.Warnings.Count > 16 ||
            record.NativeText?.Length > ContentText.MaximumTextCharacters ||
            record.OcrText?.Length > ContentText.MaximumTextCharacters ||
            record.ExtractionFingerprint is { Length: > 128 } ||
            !Enum.IsDefined(record.OcrStatus))
        {
            throw new InvalidDataException("A local content record is invalid.");
        }

        var metadata = record.Metadata.Select(field =>
        {
            if (field is null ||
                string.IsNullOrWhiteSpace(field.Name) ||
                string.IsNullOrWhiteSpace(field.Value) ||
                field.Name.Length > 64 ||
                field.Value.Length > 2048 ||
                field.Confidence is < 0 or > 1 ||
                !Enum.IsDefined(field.Provenance))
            {
                throw new InvalidDataException("A local content metadata field is invalid.");
            }

            return field;
        }).ToArray();
        var tags = record.Tags.Select(tag =>
        {
            if (tag is null ||
                string.IsNullOrWhiteSpace(tag.TagId) ||
                tag.TagId.Length > 256 ||
                string.IsNullOrWhiteSpace(tag.FileId) ||
                string.IsNullOrWhiteSpace(tag.DisplayName) ||
                tag.DisplayName.Length > 64 ||
                string.IsNullOrWhiteSpace(tag.NormalizedValue) ||
                tag.NormalizedValue.Length > 64 ||
                !Enum.IsDefined(tag.Source) ||
                !Enum.IsDefined(tag.AcceptanceState) ||
                tag.Confidence is < 0 or > 1 ||
                tag.CreatedAtUtc.Offset != TimeSpan.Zero ||
                tag.UpdatedAtUtc is { Offset: not { Ticks: 0 } })
            {
                throw new InvalidDataException("A local content tag is invalid.");
            }

            return tag with
            {
                Explanation = ContentText.NormalizeField(tag.Explanation, 256),
                ProvenanceDetails = ContentText.NormalizeField(tag.ProvenanceDetails, 256),
            };
        }).ToArray();
        var pages = record.OcrPages.Select(page =>
        {
            if (page is null ||
                page.PageNumber < 1 ||
                !Enum.IsDefined(page.TextSource) ||
                !Enum.IsDefined(page.Status) ||
                page.Text?.Length > ContentText.MaximumTextCharacters ||
                page.Confidence is < 0 or > 1 ||
                string.IsNullOrWhiteSpace(page.Message) ||
                page.Message.Length > 256)
            {
                throw new InvalidDataException("A local content page result is invalid.");
            }

            return page with
            {
                Text = NullIfEmpty(ContentText.Normalize(page.Text)),
                Message = ContentText.NormalizeField(page.Message, 256),
            };
        }).OrderBy(page => page.PageNumber).ToArray();
        if (pages.Select(page => page.PageNumber).Distinct().Count() != pages.Length)
        {
            throw new InvalidDataException("A local content record contains duplicate page results.");
        }

        return record with
        {
            FullPath = NormalizePath(record.FullPath),
            Metadata = Array.AsReadOnly(metadata),
            NativeText = NullIfEmpty(ContentText.Normalize(record.NativeText)),
            OcrText = NullIfEmpty(ContentText.Normalize(record.OcrText)),
            OcrEngineIdentifier = ContentText.NormalizeField(record.OcrEngineIdentifier, 128),
            ExtractionFingerprint = NullIfEmpty(ContentText.NormalizeField(record.ExtractionFingerprint, 128)),
            OcrPages = Array.AsReadOnly(pages),
            Tags = Array.AsReadOnly(tags),
            Warnings = Array.AsReadOnly(record.Warnings
                .Select(warning => ContentText.NormalizeField(warning, 256))
                .Where(warning => warning.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray()),
        };
    }

    private static string NormalizePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathRooted(fullPath))
        {
            throw new InvalidDataException("An absolute content-record path is required.");
        }

        return Path.GetFullPath(fullPath);
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private ContentRecord? AttachDiagnosticSession(ContentRecord? record) =>
        record is not null && _diagnosticSessions.TryGetValue(record.FullPath, out var sessionId)
            ? record with { DiagnosticSessionId = sessionId }
            : record;

    private static IReadOnlyList<ContentRecord> Order(IEnumerable<ContentRecord> records) =>
        Array.AsReadOnly(records.OrderBy(record => record.FullPath, PathComparer).ToArray());

    private sealed record ContentEnvelope(int SchemaVersion, IReadOnlyList<ContentRecord>? Records);
}
