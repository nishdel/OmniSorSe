using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.Application.CatalogSearch;

/// <summary>
/// Stores bounded named catalog queries in an atomic, versioned application-owned JSON file.
/// </summary>
public sealed class JsonSavedCatalogSearchStore : ISavedCatalogSearchStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>Initializes a store at an explicit absolute application-data path.</summary>
    public JsonSavedCatalogSearchStore(string filePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
        {
            throw new ArgumentException("An absolute saved-search file path is required.", nameof(filePath));
        }

        _filePath = filePath;
        _fileAccess = new ApplicationFileAccessCoordinator(filePath);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService))).CreateLogger(nameof(JsonSavedCatalogSearchStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedCatalogSearch>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Order(await LoadCoreAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SavedCatalogSearch> SaveAsync(SavedCatalogSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var sanitized = Sanitize(search);
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var searches = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var isReplacement = searches.Any(candidate => string.Equals(candidate.Id, sanitized.Id, StringComparison.Ordinal));
            if (!isReplacement && searches.Count >= SavedCatalogSearchLimits.MaximumSearchCount)
            {
                throw new SavedCatalogSearchCapacityExceededException(
                    $"At most {SavedCatalogSearchLimits.MaximumSearchCount} catalog searches can be saved.");
            }

            var updated = searches
                .Where(candidate => !string.Equals(candidate.Id, sanitized.Id, StringComparison.Ordinal))
                .Append(sanitized)
                .ToArray();
            await SaveCoreAsync(updated, cancellationToken).ConfigureAwait(false);
            return sanitized;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string searchId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchId);
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var searches = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var remaining = searches.Where(search => !string.Equals(search.Id, searchId, StringComparison.Ordinal)).ToArray();
            if (remaining.Length == searches.Count)
            {
                return false;
            }

            await SaveCoreAsync(remaining, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    private async Task<IReadOnlyList<SavedCatalogSearch>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<SavedCatalogSearch>();
        }

        try
        {
            if (new FileInfo(_filePath).Length > SavedCatalogSearchLimits.MaximumStoreFileBytes)
            {
                throw new InvalidDataException("The saved catalog searches exceed their supported encoded size.");
            }

            await using var stream = File.OpenRead(_filePath);
            var envelope = await JsonSerializer.DeserializeAsync<SavedSearchEnvelope>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (envelope is null || envelope.SchemaVersion != CurrentSchemaVersion || envelope.Searches is null)
            {
                throw new InvalidDataException("The saved catalog searches have an unsupported format.");
            }

            if (envelope.Searches.Count > SavedCatalogSearchLimits.MaximumSearchCount)
            {
                throw new InvalidDataException("The saved catalog searches exceed the supported capacity.");
            }

            var searches = envelope.Searches.Select(Sanitize).ToArray();
            if (searches.Select(search => search.Id).Distinct(StringComparer.Ordinal).Count() != searches.Length)
            {
                throw new InvalidDataException("The saved catalog searches contain duplicate identifiers.");
            }

            return Array.AsReadOnly(searches);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The saved catalog searches are malformed and will not be used.");
            throw new InvalidDataException("The saved catalog searches are malformed.", exception);
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(exception, "The saved catalog searches have an unsupported format and will not be used.");
            throw;
        }
    }

    private async Task SaveCoreAsync(IReadOnlyList<SavedCatalogSearch> searches, CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(
            _filePath,
            new SavedSearchEnvelope(CurrentSchemaVersion, Order(searches)),
            JsonOptions,
            SavedCatalogSearchLimits.MaximumStoreFileBytes,
            cancellationToken,
            static (_, _) => new InvalidDataException(
                "The saved catalog searches exceed their supported encoded size.")).ConfigureAwait(false);
    }

    private static SavedCatalogSearch Sanitize(SavedCatalogSearch search)
    {
        if (search is null ||
            string.IsNullOrWhiteSpace(search.Id) || search.Id.Length > 128 || search.Id.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(search.Name) || search.Name.Trim().Length > SavedCatalogSearchLimits.MaximumNameLength || search.Name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(search.QueryText) || search.QueryText.Trim().Length > SavedCatalogSearchLimits.MaximumQueryLength || HasUnsupportedQueryControl(search.QueryText) ||
            search.CreatedAtUtc.Offset != TimeSpan.Zero || search.UpdatedAtUtc.Offset != TimeSpan.Zero || search.UpdatedAtUtc < search.CreatedAtUtc)
        {
            throw new InvalidDataException("The saved catalog search is invalid.");
        }

        return search with
        {
            Id = search.Id.Trim(),
            Name = search.Name.Trim(),
            QueryText = search.QueryText.Trim(),
        };
    }

    private static bool HasUnsupportedQueryControl(string text) => text.Any(character => char.IsControl(character) && character is not '\t' and not '\r' and not '\n');

    private static IReadOnlyList<SavedCatalogSearch> Order(IEnumerable<SavedCatalogSearch> searches) => Array.AsReadOnly(searches
        .OrderByDescending(search => search.UpdatedAtUtc)
        .ThenBy(search => search.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(search => search.Id, StringComparer.Ordinal)
        .ToArray());

    private sealed record SavedSearchEnvelope(int SchemaVersion, IReadOnlyList<SavedCatalogSearch>? Searches);
}
