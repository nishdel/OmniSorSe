#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Persistence;
using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Application.Watching;

/// <summary>
/// Shares bounded atomic JSON mechanics for the three independent watched-folder stores.
/// </summary>
/// <remarks>
/// A complete validated document is written to a unique sibling and moved over
/// only the owned target. Cancellation or serialization failure removes only
/// that temporary file. The helpers never touch watched user files.
/// </remarks>
internal static class WatchedStoreJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task SaveAtomicAsync<T>(
        string path,
        T value,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(
            path,
            value,
            Options,
            maximumBytes,
            cancellationToken,
            static (_, _) => new InvalidDataException(
                "The watched-folder store exceeds its supported size.")).ConfigureAwait(false);
    }

    public static void ValidateRootedStorePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute application-data store path is required.", parameterName);
        }
    }
}

public sealed class JsonWatchedFolderConfigurationStore : IWatchedFolderConfigurationStore
{
    private readonly string _path;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonWatchedFolderConfigurationStore(string path, ILoggingService loggingService)
    {
        WatchedStoreJson.ValidateRootedStorePath(path, nameof(path));
        _path = path;
        _fileAccess = new ApplicationFileAccessCoordinator(path);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonWatchedFolderConfigurationStore));
    }

    public async Task<IReadOnlyList<WatchedFolderConfiguration>> LoadAsync(CancellationToken cancellationToken)
    {
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<WatchedFolderConfiguration>();
            }

            if (new FileInfo(_path).Length > WatchedFolderLimits.MaximumConfigurationBytes)
            {
                throw new InvalidDataException("The watched-folder configuration file exceeds its supported size.");
            }

            try
            {
                await using var stream = File.OpenRead(_path);
                var envelope = await JsonSerializer.DeserializeAsync<ConfigurationEnvelope>(
                    stream,
                    WatchedStoreJson.Options,
                    cancellationToken).ConfigureAwait(false);
                if (envelope is null ||
                    envelope.SchemaVersion is < 1 or > WatchedFolderLimits.CurrentConfigurationSchemaVersion ||
                    envelope.Configurations is null)
                {
                    throw new InvalidDataException("The watched-folder configuration has an unsupported format.");
                }

                var migrated = envelope.Configurations.Select(MigrateAndValidate).ToArray();
                if (migrated.Length > WatchedFolderLimits.MaximumConfigurations ||
                    migrated.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != migrated.Length)
                {
                    throw new InvalidDataException("The watched-folder configuration contains invalid or duplicate identifiers.");
                }

                return Array.AsReadOnly(migrated);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Watched-folder configuration JSON is malformed; the file was preserved.");
                throw new InvalidDataException("The watched-folder configuration file is malformed and was not changed.", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<WatchedFolderConfiguration> configurations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        var validated = configurations.Select(MigrateAndValidate).ToArray();
        if (validated.Length > WatchedFolderLimits.MaximumConfigurations ||
            validated.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != validated.Length)
        {
            throw new InvalidDataException("The watched-folder configuration exceeds its supported bounds.");
        }

        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WatchedStoreJson.SaveAtomicAsync(
                _path,
                new ConfigurationEnvelope(WatchedFolderLimits.CurrentConfigurationSchemaVersion, validated),
                WatchedFolderLimits.MaximumConfigurationBytes,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static WatchedFolderConfiguration MigrateAndValidate(WatchedFolderConfiguration configuration)
    {
        if (configuration is null ||
            string.IsNullOrWhiteSpace(configuration.Id) ||
            !Path.IsPathFullyQualified(configuration.FolderPath) ||
            string.IsNullOrWhiteSpace(configuration.DisplayName) ||
            string.IsNullOrWhiteSpace(configuration.ScanProfileId) ||
            string.IsNullOrWhiteSpace(configuration.CatalogueId) ||
            configuration.QuietPeriod < WatchedFolderLimits.MinimumQuietPeriod ||
            configuration.QuietPeriod > WatchedFolderLimits.MaximumQuietPeriod ||
            configuration.MaximumFileSizeBytes <= 0 ||
            configuration.IgnoredPaths is null ||
            configuration.IgnorePatterns is null ||
            configuration.SortingRecipeIds is null ||
            configuration.Notifications is null ||
            configuration.IgnoredPaths.Count > WatchedFolderLimits.MaximumIgnoreRules ||
            configuration.IgnorePatterns.Count > WatchedFolderLimits.MaximumIgnoreRules ||
            configuration.IgnoredPaths.Any(path =>
                string.IsNullOrWhiteSpace(path) || path.Length > WatchedFolderLimits.MaximumPathLength) ||
            configuration.IgnorePatterns.Any(pattern =>
                string.IsNullOrWhiteSpace(pattern) || pattern.Length > WatchedFolderLimits.MaximumPatternLength) ||
            configuration.SortingRecipeIds.Count > WorkflowLibraryLimits.MaximumRecipes ||
            configuration.SortingRecipeIds.Any(id =>
                string.IsNullOrWhiteSpace(id) || id.Length > WorkflowLibraryLimits.MaximumIdentifierLength))
        {
            throw new InvalidDataException("A watched-folder configuration entry is invalid.");
        }

        return configuration with
        {
            Id = configuration.Id.Trim(),
            FolderPath = Path.GetFullPath(configuration.FolderPath),
            DisplayName = configuration.DisplayName.Trim(),
            ScanProfileId = configuration.ScanProfileId.Trim(),
            SortingRecipeId = string.IsNullOrWhiteSpace(configuration.SortingRecipeId)
                ? null
                : configuration.SortingRecipeId.Trim(),
            SortingRecipeIds = Array.AsReadOnly(configuration.SortingRecipeIds
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray()),
            IgnoredPaths = Array.AsReadOnly(configuration.IgnoredPaths
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(WatchedFolderPathPolicy.PathComparer)
                .ToArray()),
            IgnorePatterns = Array.AsReadOnly(configuration.IgnorePatterns
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()),
            Status = configuration.IsEnabled ? configuration.Status : WatchedFolderStatus.Paused,
        };
    }

    private sealed record ConfigurationEnvelope(
        int SchemaVersion,
        IReadOnlyList<WatchedFolderConfiguration>? Configurations);
}

public sealed class JsonWatchedFolderCatalogueStore : IWatchedFolderCatalogueStore
{
    private readonly string _path;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonWatchedFolderCatalogueStore(string path, ILoggingService loggingService)
    {
        WatchedStoreJson.ValidateRootedStorePath(path, nameof(path));
        _path = path;
        _fileAccess = new ApplicationFileAccessCoordinator(path);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonWatchedFolderCatalogueStore));
    }

    public async Task<WatchedFolderCatalogue?> GetAsync(string catalogueId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueId);
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalogues = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            return catalogues.FirstOrDefault(item => string.Equals(item.CatalogueId, catalogueId, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(WatchedFolderCatalogue catalogue, CancellationToken cancellationToken)
    {
        Validate(catalogue);
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalogues = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => !string.Equals(item.CatalogueId, catalogue.CatalogueId, StringComparison.Ordinal))
                .Append(Clone(catalogue))
                .OrderBy(item => item.ConfigurationId, StringComparer.Ordinal)
                .ToArray();
            await WatchedStoreJson.SaveAtomicAsync(
                _path,
                new CatalogueEnvelope(WatchedFolderLimits.CurrentCatalogueSchemaVersion, catalogues),
                WatchedFolderLimits.MaximumCatalogueBytes,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<WatchedFolderCatalogue>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<WatchedFolderCatalogue>();
        }

        if (new FileInfo(_path).Length > WatchedFolderLimits.MaximumCatalogueBytes)
        {
            throw new InvalidDataException("The watched catalogue store exceeds its supported size.");
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var envelope = await JsonSerializer.DeserializeAsync<CatalogueEnvelope>(
                stream,
                WatchedStoreJson.Options,
                cancellationToken).ConfigureAwait(false);
            if (envelope is null ||
                envelope.SchemaVersion is < 1 or > WatchedFolderLimits.CurrentCatalogueSchemaVersion ||
                envelope.Catalogues is null)
            {
                throw new InvalidDataException("The watched catalogue store has an unsupported format.");
            }

            var migrated = envelope.Catalogues
                .Select(catalogue => catalogue with
                {
                    SchemaVersion = WatchedFolderLimits.CurrentCatalogueSchemaVersion,
                })
                .ToArray();
            foreach (var catalogue in migrated)
            {
                Validate(catalogue);
            }

            if (migrated.Select(item => item.CatalogueId).Distinct(StringComparer.Ordinal).Count() !=
                migrated.Length)
            {
                throw new InvalidDataException("The watched catalogue store contains duplicate identities.");
            }

            return Array.AsReadOnly(migrated.Select(Clone).ToArray());
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The watched catalogue JSON is malformed; the file was preserved.");
            throw new InvalidDataException("The watched catalogue store is malformed and was not changed.", exception);
        }
    }

    private static void Validate(WatchedFolderCatalogue catalogue)
    {
        if (catalogue is null ||
            catalogue.SchemaVersion != WatchedFolderLimits.CurrentCatalogueSchemaVersion ||
            string.IsNullOrWhiteSpace(catalogue.CatalogueId) ||
            string.IsNullOrWhiteSpace(catalogue.ConfigurationId) ||
            !Path.IsPathFullyQualified(catalogue.RootPath) ||
            catalogue.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            catalogue.Files is null ||
            catalogue.Directories is null ||
            catalogue.Files.Count > WatchedFolderLimits.MaximumCatalogueFiles ||
            catalogue.Files.Any(file =>
                file is null ||
                string.IsNullOrWhiteSpace(file.StableId) ||
                !Path.IsPathFullyQualified(file.FullPath) ||
                file.SizeInBytes < 0 ||
                file.CreationTimeUtc.Offset != TimeSpan.Zero ||
                file.LastWriteTimeUtc.Offset != TimeSpan.Zero ||
                file.AnalysedAtUtc.Offset != TimeSpan.Zero) ||
            catalogue.Files.Select(file => file.StableId).Distinct(StringComparer.Ordinal).Count() != catalogue.Files.Count)
        {
            throw new InvalidDataException("A watched catalogue is invalid or exceeds its supported bounds.");
        }
    }

    private static WatchedFolderCatalogue Clone(WatchedFolderCatalogue value) => value with
    {
        Files = Array.AsReadOnly(value.Files.ToArray()),
        Directories = Array.AsReadOnly(value.Directories.ToArray()),
    };

    private sealed record CatalogueEnvelope(
        int SchemaVersion,
        IReadOnlyList<WatchedFolderCatalogue>? Catalogues);
}

public sealed class JsonWatchedActivityStore : IWatchedActivityStore
{
    private readonly string _path;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonWatchedActivityStore(string path, ILoggingService loggingService)
    {
        WatchedStoreJson.ValidateRootedStorePath(path, nameof(path));
        _path = path;
        _fileAccess = new ApplicationFileAccessCoordinator(path);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonWatchedActivityStore));
    }

    public async Task<IReadOnlyList<WatchedActivityEntry>> ListAsync(
        string? configurationId,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount is < 1 or > WatchedFolderLimits.MaximumActivityEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Array.AsReadOnly((await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => configurationId is null ||
                               string.Equals(item.ConfigurationId, configurationId, StringComparison.Ordinal))
                .OrderByDescending(item => item.TimestampUtc)
                .Take(maximumCount)
                .ToArray());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(WatchedActivityEntry activity, CancellationToken cancellationToken)
    {
        Validate(activity);
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => !string.Equals(item.Id, activity.Id, StringComparison.Ordinal))
                .Append(activity)
                .OrderByDescending(item => item.TimestampUtc)
                .Take(WatchedFolderLimits.MaximumActivityEntries)
                .ToArray();
            await WatchedStoreJson.SaveAtomicAsync(
                _path,
                new ActivityEnvelope(WatchedFolderLimits.CurrentActivitySchemaVersion, entries),
                WatchedFolderLimits.MaximumActivityBytes,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<WatchedActivityEntry>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<WatchedActivityEntry>();
        }

        if (new FileInfo(_path).Length > WatchedFolderLimits.MaximumActivityBytes)
        {
            throw new InvalidDataException("The watched activity store exceeds its supported size.");
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var envelope = await JsonSerializer.DeserializeAsync<ActivityEnvelope>(
                stream,
                WatchedStoreJson.Options,
                cancellationToken).ConfigureAwait(false);
            if (envelope is null ||
                envelope.SchemaVersion != WatchedFolderLimits.CurrentActivitySchemaVersion ||
                envelope.Entries is null)
            {
                throw new InvalidDataException("The watched activity store has an unsupported format.");
            }

            foreach (var entry in envelope.Entries)
            {
                Validate(entry);
            }

            return Array.AsReadOnly(envelope.Entries.ToArray());
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The watched activity JSON is malformed; the file was preserved.");
            throw new InvalidDataException("The watched activity store is malformed and was not changed.", exception);
        }
    }

    private static void Validate(WatchedActivityEntry entry)
    {
        if (entry is null ||
            string.IsNullOrWhiteSpace(entry.Id) ||
            string.IsNullOrWhiteSpace(entry.ConfigurationId) ||
            string.IsNullOrWhiteSpace(entry.Summary) ||
            entry.TimestampUtc.Offset != TimeSpan.Zero ||
            entry.ItemCount < 0 ||
            entry.Summary.Length > 1_024 ||
            entry.Detail?.Length > 4_096)
        {
            throw new InvalidDataException("A watched activity entry is invalid.");
        }
    }

    private sealed record ActivityEnvelope(
        int SchemaVersion,
        IReadOnlyList<WatchedActivityEntry>? Entries);
}
