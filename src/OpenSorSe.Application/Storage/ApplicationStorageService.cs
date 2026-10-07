using OpenSorSe.Application.Content;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Storage;

/// <summary>Describes measured active-profile bytes. Logical database categories are not added to physical totals.</summary>
public sealed record ApplicationStorageCategory(string Name, long Bytes, bool IncludedInTotal);

/// <summary>Contains an inventory of registered application-owned storage.</summary>
public sealed record ApplicationStorageUsage(
    long TotalBytes,
    long CacheBytes,
    long MaximumCacheBytes,
    IReadOnlyList<ApplicationStorageCategory> Categories);

/// <summary>Describes conservative cache and provider-managed index maintenance.</summary>
public sealed record ApplicationStorageMaintenance(ApplicationStorageUsage Usage, long ReclaimedBytes, string Message);

/// <summary>Measures owned storage and reclaims reproducible data through its existing stores.</summary>
public interface IApplicationStorageService
{
    /// <summary>Measures active storage without reading document contents.</summary>
    Task<ApplicationStorageUsage> GetUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes abandoned temporary files, then caches, then asks the durable provider to maintain its quota.</summary>
    Task<ApplicationStorageMaintenance> ReclaimAsync(CancellationToken cancellationToken = default);
}

/// <summary>Uses registered paths and existing persistence authorities; never deletes a database or journal file.</summary>
public sealed class ApplicationStorageService : IApplicationStorageService
{
    private readonly IApplicationPathProvider _paths;
    private readonly IConfigurationService _configuration;
    private readonly IDeepIndexStore _index;
    private readonly IContentStore _content;
    private readonly ISemanticIndexStore _semantic;
    private readonly TimeProvider _time;
    private readonly IVectorSearchStore? _vectors;
    private readonly VectorIndexCoordinator? _vectorCoordinator;

    /// <summary>Creates storage maintenance for the active profile.</summary>
    public ApplicationStorageService(
        IApplicationPathProvider paths,
        IConfigurationService configuration,
        IDeepIndexStore index,
        IContentStore content,
        ISemanticIndexStore semantic,
        TimeProvider? timeProvider = null,
        IVectorSearchStore? vectors = null,
        VectorIndexCoordinator? vectorCoordinator = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
        _time = timeProvider ?? TimeProvider.System;
        _vectors = vectors ?? index as IVectorSearchStore;
        _vectorCoordinator = vectorCoordinator;
    }

    /// <inheritdoc />
    public async Task<ApplicationStorageUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var settings = _configuration.Current;
        var index = await _index.GetStorageBreakdownAsync(
            settings.DeepIndexing.MaximumIndexSizeMiB * 1024L * 1024L, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => Inventory(index, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApplicationStorageMaintenance> ReclaimAsync(CancellationToken cancellationToken = default)
    {
        var before = await GetUsageAsync(cancellationToken).ConfigureAwait(false);
        var settings = _configuration.Current;
        // Stop the optional writer before reclaiming disposable vectors. Resuming is explicit
        // so cleanup cannot immediately regenerate the storage the user just reclaimed.
        if (_vectorCoordinator is not null)
        {
            await _vectorCoordinator.ReclaimAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_vectors is not null)
        {
            await _vectors.ClearVectorsAsync(cancellationToken).ConfigureAwait(false);
        }
        await Task.Run(() =>
        {
            DeleteEligibleFiles("media-temporary", _time.GetUtcNow().UtcDateTime.AddDays(-settings.Storage.TemporaryRetentionDays), false, cancellationToken);
            DeleteEligibleFiles("media-thumbnails", DateTime.MaxValue, true, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);

        // Legacy content records can carry accepted/rejected tags. Their stores own
        // pruning eligibility and writer locks; blanket cache deletion would lose authority.
        ApplicationStorageFiles.RequireUnlinkedPath(Path.Combine(_paths.Paths.CacheDirectory, "content-index.json"));
        await _content.PruneRebuildableAsync(cancellationToken).ConfigureAwait(false);
        ApplicationStorageFiles.RequireUnlinkedPath(Path.Combine(_paths.Paths.CacheDirectory, "semantic-index.json"));
        await _semantic.PruneRebuildableAsync(cancellationToken).ConfigureAwait(false);
        var maintained = await _index.MaintainAsync(settings.DeepIndexing, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var after = await GetUsageAsync(cancellationToken).ConfigureAwait(false);
        return new ApplicationStorageMaintenance(after, Math.Max(0, before.TotalBytes - after.TotalBytes),
            maintained.IsWithinQuota && after.CacheBytes <= after.MaximumCacheBytes
                ? "Rebuildable caches and learned vectors were reclaimed. User decisions, accepted tags, relationships and operation history were preserved. Resume embeddings under Search when ready to rebuild."
                : "Safe cleanup completed. Some storage remains above its limit; durable decisions and history were preserved. Increase the relevant limit or review the remaining storage.");
    }

    private ApplicationStorageUsage Inventory(IndexStorageBreakdown index, CancellationToken cancellationToken)
    {
        var paths = _paths.Paths;
        var rows = new List<ApplicationStorageCategory>();
        var seen = new HashSet<string>(PlatformServices.CurrentPathSemantics.Comparer);
        long Add(string name, IEnumerable<string> entries)
        {
            long bytes = 0;
            foreach (var entry in entries)
            {
                foreach (var file in ApplicationStorageFiles.Enumerate(entry, cancellationToken))
                {
                    if (seen.Add(file.FullName))
                    {
                        bytes = checked(bytes + file.Length);
                    }
                }
            }

            rows.Add(new ApplicationStorageCategory(name, bytes, true));
            return bytes;
        }

        Add("Temporary data (separate per-job extraction limit)", [Path.Combine(paths.CacheDirectory, "media-temporary")]);
        var cacheBytes = Add("Content and OCR compatibility cache", [Path.Combine(paths.CacheDirectory, "content-index.json")]);
        cacheBytes += Add("Search compatibility cache", [Path.Combine(paths.CacheDirectory, "semantic-index.json")]);
        cacheBytes += Add("Image previews", [Path.Combine(paths.CacheDirectory, "media-thumbnails")]);
        Add("Library index, connections and database recovery copies", [Path.Combine(paths.DataDirectory, "index")]);
        Add("Saved libraries, organization preferences and structure history", ApplicationStorageFiles.DataEntries
            .Where(entry => entry != "index").Select(entry => Path.Combine(paths.DataDirectory, entry)));
        Add("Operation history, settings and state recovery copies", new[]
        {
            Path.Combine(paths.StateDirectory, "operation-journal.json"), Path.Combine(paths.StateDirectory, "change-plans.json"),
            Path.Combine(paths.StateDirectory, "watched-activity.json"), Path.Combine(paths.StateDirectory, "plugins-state.json"),
            Path.Combine(paths.StateDirectory, "state-backups"), _paths.SettingsFilePath,
            Path.Combine(paths.ConfigurationDirectory, "watched-folders.json"),
        });
        Add("Diagnostic logs", [paths.DiagnosticsDirectory]);
        var total = rows.Sum(row => row.Bytes);
        rows.AddRange(new[]
        {
            new ApplicationStorageCategory("Inside library: extracted content", index.ExtractedTextBytes, false),
            new ApplicationStorageCategory("Inside library: OCR text", index.OcrTextBytes, false),
            new ApplicationStorageCategory("Inside library: summaries, topics and AI metadata", index.SummariesAndKeywordsBytes + index.ContentIntelligenceBytes, false),
            new ApplicationStorageCategory("Inside library: search representations", index.SemanticDataBytes, false),
            new ApplicationStorageCategory("Inside library: disposable learned vectors", index.VectorDataBytes, false),
            new ApplicationStorageCategory("Inside library: relationship data", index.RelationshipDataBytes, false),
            new ApplicationStorageCategory("Inside library: Smart Tags and decisions", index.SmartTagBytes, false),
        });
        return new ApplicationStorageUsage(total, cacheBytes, _configuration.Current.Storage.MaximumCacheSizeMiB * 1024L * 1024L, rows.AsReadOnly());
    }

    private void DeleteEligibleFiles(string entry, DateTime cutoff, bool thumbnailsOnly, CancellationToken cancellationToken)
    {
        var root = Path.Combine(_paths.Paths.CacheDirectory, entry);
        foreach (var file in ApplicationStorageFiles.Enumerate(root, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc >= cutoff ||
                thumbnailsOnly && (!string.Equals(file.Extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetFileNameWithoutExtension(file.Name).Length != 64 ||
                                   !Path.GetFileNameWithoutExtension(file.Name).All(char.IsAsciiHexDigit)))
            {
                continue;
            }

            ApplicationStorageFiles.RequireUnlinkedPath(file.FullName);
            // A busy file is left for a later pass. No directory or source-file traversal is permitted.
            try
            {
                using (new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                }

                File.Delete(file.FullName);
            }
            catch (IOException)
            {
            }
        }
    }
}
