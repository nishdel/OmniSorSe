using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;
using OpenSorSe.Application.Watching;

namespace OpenSorSe.Application.Indexing;

/// <summary>
/// Coordinates durable discovery and staged work while keeping Views, ViewModels, and search logic provider independent.
/// </summary>
public sealed partial class BackgroundIndexingService : IBackgroundIndexingService, IIndexPrivacyService
{
    private static readonly HashSet<string> ArchiveExtensions = new(
        [".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BinaryAndExecutableExtensions = new(
        [".exe", ".dll", ".so", ".dylib", ".bin", ".msi", ".app", ".com", ".sys", ".class", ".o", ".obj"],
        StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ActiveStage> _activeStages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _discoveryCancellations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _discoverySourceIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _discoveryTasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _diagnosticSessions = new(StringComparer.Ordinal);
    private readonly IBackgroundResourceMonitor _backgroundResourceMonitor;
    private readonly IConfigurationService _configurationService;
    private readonly IDeepIndexStore _deepIndexStore;
    private readonly IDiagnosticsEventSink? _diagnostics;
    private readonly IIndexFileDiscovery _discovery;
    private readonly IIndexingStageProcessor _stageProcessor;
    private readonly ILogger _logger;
    private readonly IPathSemantics _pathSemantics;
    private readonly IWatchedFolderManager? _watchedFolderManager;
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly List<Task> _workers = [];
    private Exception? _initializationFailure;
    private int _initialized;
    private bool _disposed;

    /// <summary>Initializes the durable background coordinator.</summary>
    public BackgroundIndexingService(
        IConfigurationService configurationService,
        IDeepIndexStore deepIndexStore,
        IIndexFileDiscovery discovery,
        IIndexingStageProcessor stageProcessor,
        IBackgroundResourceMonitor backgroundResourceMonitor,
        IPathSemantics pathSemantics,
        ILoggingService loggingService,
        IDiagnosticsEventSink? diagnostics = null,
        IWatchedFolderManager? watchedFolderManager = null,
        TimeProvider? timeProvider = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _deepIndexStore = deepIndexStore ?? throw new ArgumentNullException(nameof(deepIndexStore));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _stageProcessor = stageProcessor ?? throw new ArgumentNullException(nameof(stageProcessor));
        _backgroundResourceMonitor = backgroundResourceMonitor ?? throw new ArgumentNullException(nameof(backgroundResourceMonitor));
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(BackgroundIndexingService));
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
        _watchedFolderManager = watchedFolderManager;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public event EventHandler<IndexingProgressSnapshot>? ProgressChanged;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        try
        {
            await _deepIndexStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var recovered = await _deepIndexStore
                .RecoverInterruptedWorkAsync(_timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            if (recovered > 0)
            {
                _logger.LogInformation("Recovered {RecoveredStageCount} interrupted indexing stages.", recovered);
                var recoverySession = _diagnostics?.BeginSession(
                    DiagnosticCategory.SearchAndIndexing,
                    "Background index resume",
                    [new DiagnosticField("Recovered stage count", recovered.ToString(CultureInfo.InvariantCulture))]);
                _diagnostics?.Complete(
                    recoverySession,
                    DiagnosticStatus.Succeeded,
                    TimeSpan.Zero,
                    "Interrupted durable work was returned to the queue without discarding completed stages.");
            }

            var settings = _configurationService.Current.DeepIndexing;
            if (!settings.Enabled)
            {
                await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_watchedFolderManager is not null)
            {
                _watchedFolderManager.ConfigurationsChanged += OnWatchedFolderConfigurationsChanged;
                await RegisterWatchedSourcesAsync(cancellationToken).ConfigureAwait(false);
            }

            var resumableRuns = await _deepIndexStore.GetResumableRunsAsync(cancellationToken).ConfigureAwait(false);
            var resumableSourceIds = resumableRuns
                .Select(item => item.Source.Id)
                .ToHashSet(StringComparer.Ordinal);
            var workerCount = EffectiveConcurrency(settings);
            for (var index = 0; index < workerCount; index++)
            {
                _workers.Add(Task.Run(
                    () => WorkerLoopAsync(index, _lifetimeCancellation.Token),
                    CancellationToken.None));
            }

            foreach (var run in resumableRuns.Where(item =>
                         !item.DiscoveryComplete &&
                         item.Status is IndexingRunStatus.Pending or
                             IndexingRunStatus.Running or
                             IndexingRunStatus.Waiting or
                             IndexingRunStatus.Cancelling))
            {
                StartDiscovery(run.Source, run.RunId);
            }

            var sources = await _deepIndexStore.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var source in sources.Where(item =>
                         item.Enabled && !resumableSourceIds.Contains(item.Id)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = await QueueSourceAsync(source, cancellationToken).ConfigureAwait(false);
            }

            Signal(workerCount);
            await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is DeepIndexCorruptException or DeepIndexUnsupportedSchemaException)
        {
            _initializationFailure = exception;
            _logger.LogError(
                exception,
                "Background index storage is unavailable but the application can continue. Category: {FailureCategory}.",
                exception.GetType().Name);
            var session = _diagnostics?.BeginSession(
                DiagnosticCategory.SearchAndIndexing,
                "Background index storage recovery",
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            _diagnostics?.Complete(
                session,
                DiagnosticStatus.Failed,
                TimeSpan.Zero,
                "The derived background index is unavailable. Existing Search data and source files remain unchanged; use Rebuild background index to create a reviewed recovery copy and start fresh.",
                DiagnosticSeverity.Error);
        }
        catch
        {
            Interlocked.Exchange(ref _initialized, 0);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> QueueFolderAsync(
        string rootPath,
        IndexingLevel? level = null,
        bool includeSubfolders = true,
        IReadOnlyList<string>? exclusions = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfStorageUnavailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var normalizedRoot = _pathSemantics.NormalizeAbsolutePath(rootPath);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException("The indexing folder is not currently accessible.");
        }

        var settings = _configurationService.Current.DeepIndexing;
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Background indexing is disabled in Settings.");
        }

        var source = new IndexingSource(
            CreateSourceId(normalizedRoot),
            normalizedRoot,
            Path.GetFileName(normalizedRoot) is { Length: > 0 } name ? name : normalizedRoot,
            level ?? settings.DefaultLevel,
            includeSubfolders,
            Enabled: true,
            Priority: 0,
            (exclusions ?? []).Take(128).ToArray());
        await _deepIndexStore.UpsertSourceAsync(source, cancellationToken).ConfigureAwait(false);
        return await QueueSourceAsync(source, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            return Task.FromResult<IReadOnlyList<IndexingSource>>([]);
        }

        return _deepIndexStore.GetSourcesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfStorageUnavailable();
        await _deepIndexStore
            .SetActiveRunsStatusAsync(IndexingRunStatus.Paused, null, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfStorageUnavailable();
        await _deepIndexStore
            .SetActiveRunsStatusAsync(IndexingRunStatus.Running, null, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        var resumableRuns = await _deepIndexStore.GetResumableRunsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var run in resumableRuns.Where(item =>
                     !item.DiscoveryComplete && item.Status == IndexingRunStatus.Running))
        {
            StartDiscovery(run.Source, run.RunId);
        }

        Signal(_workers.Count);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAsync(string reason, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfStorageUnavailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var activeRun = await _deepIndexStore
            .GetProgressAsync(
                MaximumIndexBytes(_configurationService.Current.DeepIndexing),
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        await _deepIndexStore
            .SetActiveRunsStatusAsync(IndexingRunStatus.Cancelling, reason, now, cancellationToken)
            .ConfigureAwait(false);
        var activeStages = _activeStages.Values.ToArray();
        var discoveryTasks = _discoveryTasks.Values.ToArray();
        foreach (var active in activeStages)
        {
            active.Cancellation.Cancel();
        }

        foreach (var discovery in _discoveryCancellations.Values)
        {
            discovery.Cancel();
        }

        await AwaitActiveStagesAsync(activeStages, cancellationToken).ConfigureAwait(false);
        await AwaitDiscoveryTasksAsync(discoveryTasks, cancellationToken).ConfigureAwait(false);
        await _deepIndexStore
            .SetActiveRunsStatusAsync(IndexingRunStatus.Cancelled, reason, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        _diagnostics?.Publish(
            activeRun.RunId is null ? null : SessionFor(activeRun.RunId),
            "Cancellation acknowledged",
            DiagnosticStatus.Cancelled,
            DiagnosticSeverity.Information,
            DiagnosticSection.Overview,
            "Background indexing cancellation completed at a safe durable boundary.",
            [new DiagnosticField("Cancellation reason", reason, DiagnosticDataClassification.Metadata)]);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> RetryFailedAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            return 0;
        }

        var count = await _deepIndexStore
            .RetryIncompleteAsync(_timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        var resumableRuns = await _deepIndexStore.GetResumableRunsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var run in resumableRuns.Where(item =>
                     !item.DiscoveryComplete && item.Status == IndexingRunStatus.Running))
        {
            StartDiscovery(run.Source, run.RunId);
        }

        foreach (var run in resumableRuns.Where(item =>
                     !item.DiscoveryComplete &&
                     item.Status is IndexingRunStatus.Cancelled or IndexingRunStatus.Failed))
        {
            _ = await QueueSourceAsync(run.Source, cancellationToken).ConfigureAwait(false);
            count++;
        }

        Signal(Math.Max(1, count));
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    /// <inheritdoc />
    public async Task PrioritizeSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfStorageUnavailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        await _deepIndexStore.SetSourcePriorityAsync(sourceId, 1000, cancellationToken).ConfigureAwait(false);
        Signal(_workers.Count);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfStorageUnavailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        await StopSourceInProcessWorkAsync(sourceId, cancellationToken).ConfigureAwait(false);
        await _deepIndexStore.RemoveSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            var recoveryPath = await _deepIndexStore
                .ResetStorageAsync(_timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            _logger.LogWarning(
                "The unreadable derived index was preserved and reset explicitly. Recovery copy created: {RecoveryCopyCreated}.",
                recoveryPath is not null);
            _initializationFailure = null;
            Interlocked.Exchange(ref _initialized, 0);
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await PauseAsync(cancellationToken).ConfigureAwait(false);
        var active = _activeStages.Values.ToArray();
        var discoveryTasks = _discoveryTasks.Values.ToArray();
        foreach (var item in active)
        {
            item.Cancellation.Cancel();
        }

        foreach (var discovery in _discoveryCancellations.Values)
        {
            discovery.Cancel();
        }

        await AwaitActiveStagesAsync(active, cancellationToken).ConfigureAwait(false);
        await AwaitDiscoveryTasksAsync(discoveryTasks, cancellationToken).ConfigureAwait(false);
        await _deepIndexStore.RebuildAsync(_timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var sources = await _deepIndexStore.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var source in sources.Where(item => item.Enabled))
        {
            _ = await QueueSourceAsync(source, cancellationToken).ConfigureAwait(false);
        }

        await ResumeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IndexingProgressSnapshot> GetProgressAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            return Task.FromResult(new IndexingProgressSnapshot
            {
                RunId = "storage-initialization",
                Status = IndexingRunStatus.Failed,
                MaximumIndexSizeBytes = MaximumIndexBytes(_configurationService.Current.DeepIndexing),
                Coverage = new SearchCoverage(0, 0, 0, 0, 0, 0)
                {
                    IsAvailable = false,
                },
            });
        }

        return _deepIndexStore.GetProgressAsync(
            MaximumIndexBytes(_configurationService.Current.DeepIndexing),
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexingFailure>> GetFailuresAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is { } failure)
        {
            var category = failure is DeepIndexUnsupportedSchemaException
                ? IndexingFailureCategory.UnsupportedSchema
                : IndexingFailureCategory.StorageCorruption;
            return Task.FromResult<IReadOnlyList<IndexingFailure>>(
            [
                new(
                    "storage-initialization",
                    "Background index storage",
                    IndexingStage.FileDiscovered,
                    category,
                    category == IndexingFailureCategory.UnsupportedSchema
                        ? "unsupported-newer-schema"
                        : "index-storage-corrupt",
                    1,
                    _timeProvider.GetUtcNow(),
                    CanRetry: false),
            ]);
        }

        return _deepIndexStore.GetFailuresAsync(1000, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            var maximum = MaximumIndexBytes(_configurationService.Current.DeepIndexing);
            return Task.FromResult(new IndexStorageBreakdown(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                maximum));
        }

        return _deepIndexStore.GetStorageBreakdownAsync(
            MaximumIndexBytes(_configurationService.Current.DeepIndexing),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);
        }

        return _deepIndexStore.GetSearchDocumentsAsync(maximumCount, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        EnsureInitialized();
        if (_initializationFailure is not null || fileIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);
        }

        return _deepIndexStore.GetSearchDocumentsByIdsAsync(fileIds, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetExcludedPathsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        return _deepIndexStore.GetExcludedSearchPathsAsync(maximumCount, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            return Task.FromResult(new SearchCoverage(0, 0, 0, 0, 0, 0)
            {
                IsAvailable = false,
            });
        }

        return _deepIndexStore.GetSearchCoverageAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IndexMaintenanceResult> MaintainAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_initializationFailure is not null)
        {
            var maximum = MaximumIndexBytes(_configurationService.Current.DeepIndexing);
            return new IndexMaintenanceResult(
                [],
                new IndexStorageBreakdown(0, 0, 0, 0, 0, 0, 0, 0, 0, maximum),
                IsWithinQuota: false);
        }

        var result = await _deepIndexStore.MaintainAsync(
                _configurationService.Current.DeepIndexing,
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);
        ReportMaintenance(result);
        return result;
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_watchedFolderManager is not null)
        {
            _watchedFolderManager.ConfigurationsChanged -= OnWatchedFolderConfigurationsChanged;
        }

        _lifetimeCancellation.Cancel();
        foreach (var active in _activeStages.Values)
        {
            active.Cancellation.Cancel();
        }

        var tasks = _workers.Concat(_discoveryTasks.Values).ToArray();
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Process shutdown deliberately leaves claimed work for startup recovery.
        }
        finally
        {
            foreach (var active in _activeStages.Values)
            {
                active.Cancellation.Dispose();
            }

            _signal.Dispose();
            _lifetimeCancellation.Dispose();
            await _deepIndexStore.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<string> QueueSourceAsync(IndexingSource source, CancellationToken cancellationToken)
    {
        await StopSourceInProcessWorkAsync(source.Id, cancellationToken).ConfigureAwait(false);
        var runId = await _deepIndexStore
            .BeginRunAsync(source.Id, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        StartDiscovery(source, runId);
        return runId;
    }

    private void StartDiscovery(IndexingSource source, string runId)
    {
        if (_discoveryTasks.ContainsKey(runId))
        {
            return;
        }

        var sessionId = _diagnostics?.BeginSession(
            DiagnosticCategory.SearchAndIndexing,
            "Durable background indexing",
            [
                new DiagnosticField("Indexing run ID", runId),
                new DiagnosticField("Source", source.RootPath, DiagnosticDataClassification.Path),
                new DiagnosticField("Indexing level", source.Level.ToString()),
            ]);
        _diagnosticSessions[runId] = sessionId;
        var discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var task = Task.Run(
            () => DiscoverSourceAsync(source, runId, discoveryCancellation.Token),
            CancellationToken.None);
        _discoveryCancellations[runId] = discoveryCancellation;
        _discoverySourceIds[runId] = source.Id;
        _discoveryTasks[runId] = task;
        _ = task.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
                _discoveryTasks.TryRemove(runId, out _);
                _discoverySourceIds.TryRemove(runId, out _);
                if (_discoveryCancellations.TryRemove(runId, out var cancellation))
                {
                    cancellation.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RegisterWatchedSourcesAsync(CancellationToken cancellationToken)
    {
        if (_watchedFolderManager is null)
        {
            return;
        }

        var settings = _configurationService.Current.DeepIndexing;
        var configured = await _watchedFolderManager.ListAsync(cancellationToken).ConfigureAwait(false);
        var desired = new Dictionary<string, WatchedFolderConfiguration>(StringComparer.Ordinal);
        foreach (var watched in configured.Where(item => item.IsEnabled))
        {
            var root = _pathSemantics.NormalizeAbsolutePath(watched.FolderPath);
            desired[CreateSourceId(root)] = watched;
        }

        var existing = await _deepIndexStore.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var stale in existing.Where(item =>
                     item.ManagedByWatchedFolders && !desired.ContainsKey(item.Id)))
        {
            await StopSourceInProcessWorkAsync(stale.Id, cancellationToken).ConfigureAwait(false);
            await _deepIndexStore.RemoveSourceAsync(stale.Id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (sourceId, watched) in desired)
        {
            var root = _pathSemantics.NormalizeAbsolutePath(watched.FolderPath);
            var exclusions = watched.IgnorePatterns
                .Concat(watched.IgnoredPaths.Select(path =>
                    Path.IsPathRooted(path) && _pathSemantics.IsWithinRoot(root, path)
                        ? Path.GetRelativePath(root, path)
                        : path))
                .Distinct(StringComparer.Ordinal)
                .Take(128)
                .ToArray();
            await _deepIndexStore.UpsertSourceAsync(
                new IndexingSource(
                    sourceId,
                    root,
                    watched.DisplayName,
                    settings.DefaultLevel,
                    watched.IncludeSubfolders,
                    Enabled: true,
                    Priority: 100,
                    exclusions,
                    ManagedByWatchedFolders: true),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async void OnWatchedFolderConfigurationsChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            await RegisterWatchedSourcesAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
            var sources = await _deepIndexStore.GetSourcesAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
            foreach (var source in sources.Where(item => item.Enabled && item.ManagedByWatchedFolders))
            {
                _ = await QueueSourceAsync(source, _lifetimeCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Watched-folder indexing source synchronization failed safely. Category: {FailureCategory}.",
                exception.GetType().Name);
        }
    }

    private async Task DiscoverSourceAsync(IndexingSource source, string runId, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var settings = _configurationService.Current.DeepIndexing;
            var fingerprint = CreateProcessorFingerprint(settings, source.Level);
            var batch = new List<IndexingFileObservation>(256);
            await foreach (var observation in _discovery
                               .DiscoverAsync(source, settings, cancellationToken)
                               .ConfigureAwait(false))
            {
                batch.Add(observation);
                if (batch.Count < 256)
                {
                    continue;
                }

                await _deepIndexStore
                    .EnqueueDiscoveredFilesAsync(
                        runId,
                        batch.ToArray(),
                        fingerprint,
                        settings.MaximumRetryCount,
                        cancellationToken)
                    .ConfigureAwait(false);
                batch.Clear();
                Signal(_workers.Count);
                await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
            }

            if (batch.Count > 0)
            {
                await _deepIndexStore
                    .EnqueueDiscoveredFilesAsync(
                        runId,
                        batch.ToArray(),
                        fingerprint,
                        settings.MaximumRetryCount,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await _deepIndexStore
                .CompleteDiscoveryAsync(runId, new HashSet<string>(), _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            Signal(_workers.Count);
            _diagnostics?.Publish(
                SessionFor(runId),
                "Discovery complete",
                DiagnosticStatus.Succeeded,
                DiagnosticSeverity.Information,
                DiagnosticSection.Performance,
                "Source discovery completed and durable work is available.",
                [new DiagnosticField("Discovery duration", Stopwatch.GetElapsedTime(started).ToString())]);
            await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _diagnostics?.Complete(
                SessionFor(runId),
                DiagnosticStatus.Cancelled,
                Stopwatch.GetElapsedTime(started),
                "Discovery stopped safely; completed durable stages will be reused after restart.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Index source discovery failed safely. Run ID: {RunId}; category: {FailureCategory}.",
                runId,
                exception.GetType().Name);
            await _deepIndexStore
                .MarkRunFailedAsync(runId, "source-discovery-failed", _timeProvider.GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);
            _diagnostics?.Complete(
                SessionFor(runId),
                DiagnosticStatus.Failed,
                Stopwatch.GetElapsedTime(started),
                "Source discovery failed safely. Previously completed index data was retained.",
                DiagnosticSeverity.Error,
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            await PublishProgressAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task WorkerLoopAsync(int workerNumber, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var settings = _configurationService.Current.DeepIndexing;
                var eligibility = await _backgroundResourceMonitor
                    .GetEligibilityAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
                if (!eligibility.MayProcess)
                {
                    await _deepIndexStore
                        .SetActiveRunsStatusAsync(
                            IndexingRunStatus.Waiting,
                            eligibility.WaitingReason,
                            _timeProvider.GetUtcNow(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
                    await _signal.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _ = await _deepIndexStore
                    .ResumeEligibleWaitingRunsAsync(_timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
                var work = await _deepIndexStore
                    .ClaimNextAsync(_timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
                if (work is null)
                {
                    await _signal.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await ProcessClaimAsync(work, settings, workerNumber, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "A background indexing worker isolated an unexpected failure. Worker: {WorkerNumber}; category: {FailureCategory}.",
                    workerNumber,
                    exception.GetType().Name);
                _diagnostics?.Publish(
                    null,
                    "Worker failure isolation",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error,
                    DiagnosticSection.WarningsAndErrors,
                    "A worker failure was isolated; durable work remains recoverable.",
                    [
                        new DiagnosticField("Worker", workerNumber.ToString(CultureInfo.InvariantCulture)),
                        new DiagnosticField("Failure category", exception.GetType().Name),
                    ]);
            }
        }
    }

    private async Task ProcessClaimAsync(
        IndexingWorkItem work,
        DeepIndexingSettings settings,
        int workerNumber,
        CancellationToken lifetimeToken)
    {
        using var stageCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = new ActiveStage(work, stageCancellation, completion.Task);
        if (!_activeStages.TryAdd(work.JobId, active))
        {
            throw new InvalidOperationException("The durable stage is already active.");
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var storage = await _deepIndexStore
                .GetStorageBreakdownAsync(MaximumIndexBytes(settings), stageCancellation.Token)
                .ConfigureAwait(false);
            if (storage.DatabaseBytes >= storage.MaximumBytes)
            {
                var maintenance = await _deepIndexStore
                    .MaintainAsync(settings, _timeProvider.GetUtcNow(), stageCancellation.Token)
                    .ConfigureAwait(false);
                ReportMaintenance(maintenance);
                if (!maintenance.IsWithinQuota)
                {
                    await _deepIndexStore
                        .SaveStageOutputAsync(
                            work,
                            new IndexingStageOutput
                            {
                                Status = IndexingStageStatus.Failed,
                                FailureCategory = IndexingFailureCategory.StorageQuota,
                                ErrorCode = "index-storage-limit-reached",
                            },
                            null,
                            _timeProvider.GetUtcNow(),
                            Stopwatch.GetElapsedTime(started),
                            null,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    await PublishProgressAsync(CancellationToken.None).ConfigureAwait(false);
                    return;
                }
            }

            var output = await _stageProcessor
                .ProcessAsync(work, settings, stageCancellation.Token)
                .ConfigureAwait(false);
            var completedAt = _timeProvider.GetUtcNow();
            if (work.Stage == IndexingStage.ContentFingerprinted &&
                output.Status == IndexingStageStatus.Complete &&
                !string.IsNullOrWhiteSpace(output.ContentHash) &&
                work.Level != IndexingLevel.Basic &&
                !work.SuppressOcr &&
                !work.SuppressSummary &&
                !work.SuppressSemantic &&
                !work.ForceReprocess)
            {
                var reusable = await _deepIndexStore
                    .GetReusableContentThroughStageAsync(
                        output.ContentHash,
                        work.Level,
                        work.ProcessorFingerprint,
                        stageCancellation.Token)
                    .ConfigureAwait(false);
                if (reusable.HasValue)
                {
                    await _deepIndexStore
                        .ReuseContentAsync(
                            work,
                            output.ContentHash,
                            reusable.Value,
                            IndexingStage.SearchIndexUpdated,
                            completedAt,
                            stageCancellation.Token)
                        .ConfigureAwait(false);
                    await ReportStageAsync(work, output with { ErrorCode = "shared-content-reused" }, started, workerNumber)
                        .ConfigureAwait(false);
                    Signal();
                    return;
                }
            }

            var nextStage = output.StopsFile ? null : GetNextStage(work, settings);
            var retryAt = output.Status switch
            {
                IndexingStageStatus.WaitingForDependency => completedAt.AddMinutes(5),
                IndexingStageStatus.Failed when output.IsRetryable =>
                    completedAt.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(work.Attempt, 8)))),
                _ => (DateTimeOffset?)null,
            };
            await _deepIndexStore
                .SaveStageOutputAsync(
                    work,
                    output,
                    nextStage,
                    completedAt,
                    Stopwatch.GetElapsedTime(started),
                    retryAt,
                    stageCancellation.Token)
                .ConfigureAwait(false);
            await ReportStageAsync(work, output, started, workerNumber).ConfigureAwait(false);
            Signal();
        }
        catch (OperationCanceledException) when (stageCancellation.IsCancellationRequested)
        {
            if (!lifetimeToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Indexing stage cancellation acknowledged. Run ID: {RunId}; stage: {Stage}.",
                    work.RunId,
                    work.Stage);
            }
        }
        finally
        {
            _activeStages.TryRemove(work.JobId, out _);
            completion.TrySetResult();
        }
    }

    private async Task ReportStageAsync(
        IndexingWorkItem work,
        IndexingStageOutput output,
        long started,
        int workerNumber)
    {
        var snapshot = await PublishProgressAsync(CancellationToken.None).ConfigureAwait(false);
        _diagnostics?.Publish(
            SessionFor(work.RunId),
            work.Stage.ToString(),
            ToDiagnosticStatus(output.Status),
            output.Status is IndexingStageStatus.Failed ? DiagnosticSeverity.Error :
            output.Status is IndexingStageStatus.WaitingForDependency or IndexingStageStatus.RetryScheduled
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Information,
            DiagnosticSection.Performance,
            "A durable indexing stage changed state.",
            [
                new DiagnosticField("Indexing run ID", work.RunId),
                new DiagnosticField("Stage", work.Stage.ToString()),
                new DiagnosticField("Stage state", output.Status.ToString()),
                new DiagnosticField("Worker", workerNumber.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField("Attempt", work.Attempt.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField("Stage duration", Stopwatch.GetElapsedTime(started).ToString()),
                new DiagnosticField("Queue remaining", snapshot.Remaining.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField("Retry count", snapshot.RetryScheduled.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField("Files per second", snapshot.FilesPerSecond.ToString("N2", CultureInfo.InvariantCulture)),
                new DiagnosticField("Index bytes", snapshot.IndexSizeBytes.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField("Current file", Path.GetFileName(work.FullPath), DiagnosticDataClassification.Path),
                new DiagnosticField("Failure category", output.FailureCategory.ToString()),
                new DiagnosticField("Waiting dependency", output.WaitingDependency ?? "available or not required"),
            ]);
        if (snapshot.Status is IndexingRunStatus.Complete or IndexingRunStatus.CompleteWithFailures)
        {
            _diagnostics?.Complete(
                SessionFor(work.RunId),
                snapshot.Status == IndexingRunStatus.Complete
                    ? DiagnosticStatus.Succeeded
                    : DiagnosticStatus.PartiallySucceeded,
                TimeSpan.Zero,
                "The durable indexing run reached a terminal state.",
                snapshot.Failed > 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information,
                [
                    new DiagnosticField("Completed files", snapshot.Completed.ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticField("Skipped files", snapshot.Skipped.ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticField("Failed files", snapshot.Failed.ToString(CultureInfo.InvariantCulture)),
                ]);
            _diagnosticSessions.TryRemove(work.RunId, out _);
        }
    }

    private async Task<IndexingProgressSnapshot> PublishProgressAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _deepIndexStore
            .GetProgressAsync(
                MaximumIndexBytes(_configurationService.Current.DeepIndexing),
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);
        var handlers = ProgressChanged?.GetInvocationList();
        if (handlers is null)
        {
            return snapshot;
        }

        foreach (var handler in handlers.Cast<EventHandler<IndexingProgressSnapshot>>())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "An indexing progress observer failed and was isolated. Category: {FailureCategory}.",
                    exception.GetType().Name);
            }
        }

        return snapshot;
    }

    private string CreateProcessorFingerprint(DeepIndexingSettings settings, IndexingLevel sourceLevel)
    {
        var baseFingerprint = _stageProcessor is IIndexingProcessorFingerprint provider
            ? provider.CreateProcessorFingerprint(settings)
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(settings) + "|" + _stageProcessor.GetType().FullName)))
                .ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{baseFingerprint}|{sourceLevel}"))))
            .ToLowerInvariant();
    }

    private static IndexingStage? GetNextStage(IndexingWorkItem work, DeepIndexingSettings settings)
    {
        var extension = Path.GetExtension(work.FullPath);
        var metadataOnly = work.Level == IndexingLevel.Basic ||
            settings.BinaryAndExecutableMetadataOnly && BinaryAndExecutableExtensions.Contains(extension) ||
            !settings.ArchiveIndexingEnabled && ArchiveExtensions.Contains(extension);
        return work.Stage switch
        {
            IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
            IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
            IndexingStage.ContentFingerprinted => metadataOnly
                ? IndexingStage.SearchIndexUpdated
                : IndexingStage.TextExtracted,
            IndexingStage.TextExtracted => work.Level == IndexingLevel.Deep &&
                settings.OcrProcessingEnabled &&
                !work.SuppressOcr
                ? IndexingStage.OcrProcessed
                : IndexingStage.SummaryKeywordsGenerated,
            IndexingStage.OcrProcessed => IndexingStage.SummaryKeywordsGenerated,
            IndexingStage.SummaryKeywordsGenerated =>
                !settings.SemanticProcessingEnabled || work.SuppressSemantic
                ? IndexingStage.SearchIndexUpdated
                : IndexingStage.SemanticRepresentationGenerated,
            IndexingStage.SemanticRepresentationGenerated => IndexingStage.SearchIndexUpdated,
            IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
            IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
            IndexingStage.FileFullyIndexed => null,
            _ => null,
        };
    }

    private async Task AwaitActiveStagesAsync(
        IReadOnlyCollection<ActiveStage> activeStages,
        CancellationToken cancellationToken)
    {
        if (activeStages.Count == 0)
        {
            return;
        }

        await Task.WhenAll(activeStages.Select(item => item.Completion))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StopSourceInProcessWorkAsync(
        string sourceId,
        CancellationToken cancellationToken)
    {
        var active = _activeStages.Values.Where(item => item.WorkItem.SourceId == sourceId).ToArray();
        var discoveryRunIds = _discoverySourceIds
            .Where(item => string.Equals(item.Value, sourceId, StringComparison.Ordinal))
            .Select(item => item.Key)
            .ToArray();
        var discoveryTasks = discoveryRunIds
            .Select(runId => _discoveryTasks.TryGetValue(runId, out var task) ? task : null)
            .OfType<Task>()
            .ToArray();
        foreach (var item in active)
        {
            item.Cancellation.Cancel();
        }

        foreach (var runId in discoveryRunIds)
        {
            if (_discoveryCancellations.TryGetValue(runId, out var discovery))
            {
                discovery.Cancel();
            }
        }

        await AwaitActiveStagesAsync(active, cancellationToken).ConfigureAwait(false);
        await AwaitDiscoveryTasksAsync(discoveryTasks, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AwaitDiscoveryTasksAsync(
        IReadOnlyCollection<Task> discoveryTasks,
        CancellationToken cancellationToken)
    {
        if (discoveryTasks.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(discoveryTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller requested safe discovery cancellation and the durable run will be finalized next.
        }
    }

    private string? SessionFor(string runId) =>
        _diagnosticSessions.TryGetValue(runId, out var value) ? value : null;

    private void ReportMaintenance(IndexMaintenanceResult maintenance)
    {
        _diagnostics?.Publish(
            null,
            "Index storage maintenance",
            maintenance.IsWithinQuota ? DiagnosticStatus.Succeeded : DiagnosticStatus.PartiallySucceeded,
            maintenance.IsWithinQuota ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
            DiagnosticSection.Performance,
            maintenance.IsWithinQuota
                ? "Index storage maintenance completed within the configured quota."
                : "Index storage maintenance completed, but the configured quota is still reached.",
            [
                new DiagnosticField("Database bytes", maintenance.Storage.DatabaseBytes.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField("Configured maximum bytes", maintenance.Storage.MaximumBytes.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField(
                    "Cleanup actions",
                    maintenance.Actions.Count == 0
                        ? "none"
                        : string.Join(", ", maintenance.Actions.Select(action => action.Code))),
            ]);
    }

    private void Signal(int count = 1)
    {
        for (var index = 0; index < Math.Max(1, count); index++)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                break;
            }
        }
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Volatile.Read(ref _initialized) == 0)
        {
            throw new InvalidOperationException("Background indexing has not been initialized.");
        }
    }

    private void ThrowIfStorageUnavailable()
    {
        if (_initializationFailure is not null)
        {
            throw new InvalidOperationException(
                "Background index storage is unavailable. Use Rebuild background index to preserve a recovery copy and create a fresh derived index.",
                _initializationFailure);
        }
    }

    private string CreateSourceId(string normalizedRoot)
    {
        var key = _pathSemantics.IsCaseSensitive ? normalizedRoot : normalizedRoot.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return $"folder-{hash[..24]}";
    }

    private static int EffectiveConcurrency(DeepIndexingSettings settings) => settings.ResourceMode switch
    {
        IndexingResourceMode.Eco => 1,
        IndexingResourceMode.Balanced => Math.Min(
            settings.MaximumConcurrency,
            Math.Max(1, Environment.ProcessorCount / 2)),
        IndexingResourceMode.Fast => settings.MaximumConcurrency,
        _ => 1,
    };

    private static long MaximumIndexBytes(DeepIndexingSettings settings) =>
        settings.MaximumIndexSizeMiB * 1024L * 1024L;

    private static DiagnosticStatus ToDiagnosticStatus(IndexingStageStatus status) => status switch
    {
        IndexingStageStatus.Complete => DiagnosticStatus.Succeeded,
        IndexingStageStatus.Skipped => DiagnosticStatus.Skipped,
        IndexingStageStatus.Cancelled => DiagnosticStatus.Cancelled,
        IndexingStageStatus.Failed => DiagnosticStatus.Failed,
        IndexingStageStatus.WaitingForDependency or IndexingStageStatus.RetryScheduled =>
            DiagnosticStatus.PartiallySucceeded,
        _ => DiagnosticStatus.Active,
    };

    private sealed record ActiveStage(
        IndexingWorkItem WorkItem,
        CancellationTokenSource Cancellation,
        Task Completion);
}
