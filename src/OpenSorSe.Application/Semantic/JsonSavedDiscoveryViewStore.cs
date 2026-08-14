using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.Application.Semantic;

/// <summary>Persists bounded local Saved View rules atomically without materializing membership.</summary>
public sealed class JsonSavedDiscoveryViewStore : ISavedDiscoveryViewStore
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumViewCount = 100;
    private const int MaximumNameCharacters = 128;
    private const long MaximumFileBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _filePath;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>Initializes a local Saved View store at an application-owned absolute path.</summary>
    public JsonSavedDiscoveryViewStore(string filePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
        {
            throw new ArgumentException("An absolute Saved View path is required.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _fileAccess = new ApplicationFileAccessCoordinator(_filePath);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonSavedDiscoveryViewStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedDiscoveryView>> ListAsync(CancellationToken cancellationToken = default)
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
    public async Task<SavedDiscoveryView> SaveAsync(
        SavedDiscoveryView view,
        CancellationToken cancellationToken = default)
    {
        var valid = Validate(view);
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var views = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var existingIndex = views.FindIndex(item => string.Equals(item.Id, valid.Id, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                views[existingIndex] = valid;
            }
            else
            {
                if (views.Count >= MaximumViewCount)
                {
                    throw new InvalidOperationException($"At most {MaximumViewCount} Saved Views can be retained.");
                }

                views.Add(valid);
            }

            await SaveCoreAsync(views, cancellationToken).ConfigureAwait(false);
            return valid;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var views = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var removed = views.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                await SaveCoreAsync(views, cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<IReadOnlyList<SavedDiscoveryView>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            if (new FileInfo(_filePath).Length > MaximumFileBytes)
            {
                throw new InvalidDataException("The Saved View store exceeds its supported size.");
            }

            await using var stream = File.OpenRead(_filePath);
            var envelope = await JsonSerializer.DeserializeAsync<SavedViewEnvelope>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (envelope is null || envelope.SchemaVersion != CurrentSchemaVersion ||
                envelope.Views is null || envelope.Views.Count > MaximumViewCount)
            {
                throw new InvalidDataException("The Saved View store is unsupported.");
            }

            var validated = envelope.Views.Select(Validate).ToArray();
            if (validated.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != validated.Length)
            {
                throw new InvalidDataException("The Saved View store contains duplicate identifiers.");
            }

            return Order(validated);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            _logger.LogWarning(exception, "Saved View rules were invalid and were ignored without affecting the index or files.");
            return [];
        }
    }

    private Task SaveCoreAsync(IReadOnlyList<SavedDiscoveryView> views, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(
            _filePath,
            new SavedViewEnvelope(CurrentSchemaVersion, Order(views)),
            JsonOptions,
            MaximumFileBytes,
            cancellationToken,
            static (_, _) => new InvalidDataException("Saved View rules exceed their supported encoded size."));

    private static SavedDiscoveryView Validate(SavedDiscoveryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ValidateId(view.Id);
        if (string.IsNullOrWhiteSpace(view.Name) || view.Name.Trim().Length > MaximumNameCharacters ||
            view.Name.Any(char.IsControl) || view.Version != CurrentSchemaVersion ||
            view.CreatedAtUtc.Offset != TimeSpan.Zero || view.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            view.UpdatedAtUtc < view.CreatedAtUtc || view.Query is null ||
            view.Query.QueryText is null || view.Query.QueryText.Length > SearchLimits.MaximumQueryCharacters ||
            view.Query.QueryText.Any(character => char.IsControl(character) && !char.IsWhiteSpace(character)) ||
            view.Query.Filters is null || view.Query.Filters.Count > SearchLimits.MaximumFilters)
        {
            throw new InvalidDataException("A Saved View rule is invalid.");
        }

        var filters = view.Query.Filters.Select(ValidateFilter).ToArray();
        if (filters.Select(filter => filter.Id).Distinct(StringComparer.Ordinal).Count() != filters.Length)
        {
            throw new InvalidDataException("A Saved View contains duplicate filter identifiers.");
        }

        return view with
        {
            Name = view.Name.Trim(),
            Query = new DiscoveryQueryState(view.Query.QueryText.Trim(), Array.AsReadOnly(filters)),
        };
    }

    private static SearchFilter ValidateFilter(SearchFilter filter)
    {
        if (filter is null || !Enum.IsDefined(filter.Kind) ||
            string.IsNullOrWhiteSpace(filter.Id) || filter.Id.Length > 128 || filter.Id.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(filter.Value) || filter.Value.Length > SearchLimits.MaximumQueryCharacters || filter.Value.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(filter.DisplayName) || filter.DisplayName.Length > 256 || filter.DisplayName.Any(char.IsControl))
        {
            throw new InvalidDataException("A Saved View filter is invalid.");
        }

        return filter with
        {
            Id = filter.Id.Trim(),
            Value = filter.Value.Trim(),
            DisplayName = filter.DisplayName.Trim(),
        };
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded Saved View identifier is required.", nameof(id));
        }
    }

    private static IReadOnlyList<SavedDiscoveryView> Order(IEnumerable<SavedDiscoveryView> views) =>
        Array.AsReadOnly(views
            .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(view => view.Id, StringComparer.Ordinal)
            .ToArray());

    private sealed record SavedViewEnvelope(int SchemaVersion, IReadOnlyList<SavedDiscoveryView>? Views);
}
