#pragma warning disable CS1591

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Application.Watching;

/// <summary>
/// Owns the process-lifetime watcher registrations, debounce queue, reconciliation triggers, and grouped activity.
/// </summary>
/// <remarks>
/// Operating-system events are untrusted hints. The coordinator root-checks and
/// batches them through a bounded channel, marks reconciliation on overflow or
/// backpressure, and serializes lifecycle changes with a semaphore. It can
/// publish a reviewable Change Plan, but never invokes the execution service.
/// Dispose cancels the lifetime token, stops sources, and drains owned tasks.
/// </remarks>
public sealed class WatchedFolderCoordinator : IWatchedFolderCoordinator, IDisposable
{
    private static readonly TimeSpan PeriodicReconciliationInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan AvailabilityCheckInterval = TimeSpan.FromMinutes(1);
    private readonly IWatchedFolderManager _manager;
    private readonly IWatchedFolderEventSourceFactory _eventSourceFactory;
    private readonly IWatchedFolderProcessor _processor;
    private readonly IWatchedActivityStore _activityStore;
    private readonly IWatchedExecutionCorrelation _executionCorrelation;
    private readonly IWatchedFileSystem _fileSystem;
    private readonly IChangePlanStore? _changePlanStore;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<WatchedChangeBatch> _queue;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _debounceGate = new();
    private readonly Dictionary<string, WatchRegistration> _registrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DebounceState> _debounceStates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _reconciliationRequired = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _pausedConfigurations = new(StringComparer.Ordinal);
    private Task? _consumerTask;
    private Task? _availabilityTask;
    private bool _initialized;
    private bool _disposed;

    public WatchedFolderCoordinator(
        IWatchedFolderManager manager,
        IWatchedFolderEventSourceFactory eventSourceFactory,
        IWatchedFolderProcessor processor,
        IWatchedActivityStore activityStore,
        IWatchedExecutionCorrelation executionCorrelation,
        IWatchedFileSystem fileSystem,
        ILoggingService loggingService,
        IChangePlanStore? changePlanStore = null,
        TimeProvider? timeProvider = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _eventSourceFactory = eventSourceFactory ?? throw new ArgumentNullException(nameof(eventSourceFactory));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _activityStore = activityStore ?? throw new ArgumentNullException(nameof(activityStore));
        _executionCorrelation = executionCorrelation ?? throw new ArgumentNullException(nameof(executionCorrelation));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _changePlanStore = changePlanStore;
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(WatchedFolderCoordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _queue = Channel.CreateBounded<WatchedChangeBatch>(new BoundedChannelOptions(
            WatchedFolderLimits.MaximumQueuedBatches)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _manager.ConfigurationsChanged += OnConfigurationsChanged;
    }

    public event EventHandler<WatchedFolderRuntimeSnapshot>? StateChanged;
    public event EventHandler<WatchedActivityEntry>? ActivityPublished;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_initialized)
        {
            return;
        }

        _consumerTask = ConsumeAsync(_lifetimeCancellation.Token);
        try
        {
            await RefreshCoreAsync(
                WatchedScanReason.StartupOfflineReconciliation,
                queueReconciliationForNewSources: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            _logger.LogError(
                exception,
                "Watched-folder configuration could not be loaded. The invalid file was preserved and watching was not started.");
        }
        _availabilityTask = AvailabilityLoopAsync(_lifetimeCancellation.Token);
        _initialized = true;
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RefreshCoreAsync(
            WatchedScanReason.ResumeReconciliation,
            queueReconciliationForNewSources: true,
            cancellationToken);
    }

    public Task ScanChangesNowAsync(string configurationId, CancellationToken cancellationToken) =>
        QueueManualAsync(
            configurationId,
            WatchedScanReason.UserIncrementalScan,
            requiresFullReconciliation: true,
            cancellationToken);

    public Task ReconcileNowAsync(string configurationId, CancellationToken cancellationToken) =>
        QueueManualAsync(
            configurationId,
            WatchedScanReason.UserFullReconciliation,
            requiresFullReconciliation: true,
            cancellationToken);

    public Task RetryAiAsync(string configurationId, CancellationToken cancellationToken) =>
        QueueManualAsync(
            configurationId,
            WatchedScanReason.AiRetry,
            requiresFullReconciliation: false,
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.ConfigurationsChanged -= OnConfigurationsChanged;
        _lifetimeCancellation.Cancel();
        _queue.Writer.TryComplete();
        List<WatchRegistration> registrations;
        lock (_debounceGate)
        {
            foreach (var state in _debounceStates.Values)
            {
                state.Cancellation.Cancel();
                state.Cancellation.Dispose();
            }

            _debounceStates.Clear();
            registrations = _registrations.Values.ToList();
            _registrations.Clear();
        }

        foreach (var registration in registrations)
        {
            registration.Source.HintReceived -= OnHintReceived;
            registration.Source.Error -= OnWatcherError;
            registration.Source.Dispose();
        }

        var tasks = new[] { _consumerTask, _availabilityTask }.Where(task => task is not null).Cast<Task>().ToArray();
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during controlled application shutdown.
        }

        _lifecycleGate.Dispose();
        _lifetimeCancellation.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task RefreshCoreAsync(
        WatchedScanReason newSourceReason,
        bool queueReconciliationForNewSources,
        CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configurations = await _manager.ListAsync(cancellationToken).ConfigureAwait(false);
            var byId = configurations.ToDictionary(configuration => configuration.Id, StringComparer.Ordinal);
            List<WatchRegistration> toDispose;
            lock (_debounceGate)
            {
                toDispose = _registrations.Values
                    .Where(registration =>
                        !byId.TryGetValue(registration.Configuration.Id, out var current) ||
                        !current.IsEnabled ||
                        !SameRegistrationSettings(registration.Configuration, current) ||
                        !_fileSystem.DirectoryExists(current.FolderPath))
                    .ToList();
                foreach (var registration in toDispose)
                {
                    _registrations.Remove(registration.Configuration.Id);
                    if (_debounceStates.Remove(registration.Configuration.Id, out var debounce))
                    {
                        debounce.Cancellation.Cancel();
                        debounce.Cancellation.Dispose();
                    }
                }
            }

            foreach (var registration in toDispose)
            {
                registration.Source.HintReceived -= OnHintReceived;
                registration.Source.Error -= OnWatcherError;
                registration.Source.Dispose();
                var currentExists = byId.TryGetValue(registration.Configuration.Id, out var current);
                var paused = currentExists && !current!.IsEnabled;
                var settingsChanged = currentExists &&
                                      current!.IsEnabled &&
                                      SameRegistrationSettings(registration.Configuration, current) == false;
                if (paused)
                {
                    _pausedConfigurations.TryAdd(registration.Configuration.Id, 0);
                }

                await RecordActivityAsync(
                    registration.Configuration.Id,
                    paused
                        ? WatchedActivityKind.WatcherPaused
                        : settingsChanged || !currentExists
                            ? WatchedActivityKind.ConfigurationChanged
                            : WatchedActivityKind.WatcherDisposed,
                    paused
                        ? "Watcher paused; files and history were preserved."
                        : settingsChanged
                            ? "Watched-folder configuration changed; the old watcher was disposed."
                            : !currentExists
                                ? "Removed from the watch list; the watcher was disposed without deleting files or history."
                                : "The watcher was disposed after an availability change.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            foreach (var configuration in configurations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!configuration.IsEnabled)
                {
                    await PublishStateAsync(configuration, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!_fileSystem.DirectoryExists(configuration.FolderPath))
                {
                    if (configuration.Status != WatchedFolderStatus.Unavailable)
                    {
                        var unavailable = await _manager.SetRuntimeStateAsync(
                            configuration.Id,
                            current => current with
                            {
                                Status = WatchedFolderStatus.Unavailable,
                                LastError = "The watched folder is unavailable or disconnected.",
                                LatestSummary = "The configuration and scan history were retained.",
                            },
                            cancellationToken).ConfigureAwait(false);
                        await RecordActivityAsync(
                            configuration.Id,
                            WatchedActivityKind.FolderUnavailable,
                            "Watched folder unavailable.",
                            detail: "The configuration and catalogue were retained; no file was removed.",
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        await PublishStateAsync(unavailable, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                WatchRegistration? existingRegistration;
                lock (_debounceGate)
                {
                    _registrations.TryGetValue(configuration.Id, out existingRegistration);
                }

                if (existingRegistration is not null)
                {
                    var processingSettingsChanged =
                        !SameProcessingSettings(existingRegistration.Configuration, configuration);
                    if (processingSettingsChanged)
                    {
                        lock (_debounceGate)
                        {
                            _registrations[configuration.Id] = existingRegistration with
                            {
                                Configuration = configuration,
                            };
                        }

                        await RecordActivityAsync(
                            configuration.Id,
                            WatchedActivityKind.ConfigurationChanged,
                            "Watched-folder processing or notification settings changed.",
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }

                    await PublishStateAsync(configuration, cancellationToken).ConfigureAwait(false);
                    if (processingSettingsChanged)
                    {
                        await EnqueueBatchAsync(
                            NewBatch(
                                configuration.Id,
                                WatchedScanReason.ConfigurationChangedReconciliation,
                                requiresFullReconciliation: true),
                            cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                IWatchedFolderEventSource source;
                try
                {
                    source = _eventSourceFactory.Create(configuration);
                    source.HintReceived += OnHintReceived;
                    source.Error += OnWatcherError;
                    source.Start();
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                {
                    _logger.LogWarning(
                        exception,
                        "Watcher registration failed for configuration {ConfigurationId}.",
                        configuration.Id);
                    var inaccessible = await _manager.SetRuntimeStateAsync(
                        configuration.Id,
                        current => current with
                        {
                            Status = WatchedFolderStatus.Inaccessible,
                            LastError = "The folder exists but watching could not be started.",
                            LatestSummary = "Use Full reconciliation after access is restored.",
                        },
                        cancellationToken).ConfigureAwait(false);
                    await PublishStateAsync(inaccessible, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                lock (_debounceGate)
                {
                    _registrations[configuration.Id] = new WatchRegistration(configuration, source);
                }

                var availableAgain = configuration.Status is WatchedFolderStatus.Unavailable or WatchedFolderStatus.Inaccessible;
                var resumed = _pausedConfigurations.TryRemove(configuration.Id, out _);
                var watching = await _manager.SetRuntimeStateAsync(
                    configuration.Id,
                    current => current with
                    {
                        Status = WatchedFolderStatus.Watching,
                        LastError = null,
                        LatestSummary = availableAgain
                            ? "The folder is available again; reconciliation is queued."
                            : "Watching for external changes.",
                    },
                    cancellationToken).ConfigureAwait(false);
                await RecordActivityAsync(
                    configuration.Id,
                    availableAgain
                        ? WatchedActivityKind.FolderAvailableAgain
                        : resumed
                            ? WatchedActivityKind.WatcherResumed
                            : WatchedActivityKind.WatcherStarted,
                    availableAgain
                        ? "Watched folder available again."
                        : resumed
                            ? "Watcher resumed; reconciliation is queued."
                            : "Watcher started.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await PublishStateAsync(watching, cancellationToken).ConfigureAwait(false);
                if (queueReconciliationForNewSources)
                {
                    await EnqueueBatchAsync(
                        NewBatch(configuration.Id, newSourceReason, requiresFullReconciliation: true),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var configurationId in _pausedConfigurations.Keys.Where(id => !byId.ContainsKey(id)))
            {
                _pausedConfigurations.TryRemove(configurationId, out _);
            }

            foreach (var configurationId in _reconciliationRequired.Keys.Where(id => !byId.ContainsKey(id)))
            {
                _reconciliationRequired.TryRemove(configurationId, out _);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task QueueManualAsync(
        string configurationId,
        WatchedScanReason reason,
        bool requiresFullReconciliation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationId);
        var configuration = await FindConfigurationAsync(configurationId, cancellationToken).ConfigureAwait(false);
        if (!configuration.IsEnabled)
        {
            throw new InvalidOperationException("Resume the watched folder before scanning it.");
        }

        if (!_fileSystem.DirectoryExists(configuration.FolderPath))
        {
            throw new DirectoryNotFoundException("The watched folder is unavailable.");
        }

        await RecordActivityAsync(
            configurationId,
            WatchedActivityKind.ManualScanRequested,
            reason == WatchedScanReason.UserFullReconciliation
                ? "User-triggered full reconciliation requested."
                : reason == WatchedScanReason.AiRetry
                    ? "User-triggered retry of pending or failed AI analysis requested."
                    : "User-triggered incremental scan requested.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await EnqueueBatchAsync(
            NewBatch(configurationId, reason, requiresFullReconciliation),
            cancellationToken).ConfigureAwait(false);
    }

    private async void OnHintReceived(object? sender, WatchedFolderHint hint)
    {
        try
        {
            var configuration = await FindConfigurationAsync(
                hint.ConfigurationId,
                _lifetimeCancellation.Token).ConfigureAwait(false);
            if (!configuration.IsEnabled ||
                hint.Kind != WatchedPathChangeKind.Overflow &&
                (string.IsNullOrWhiteSpace(hint.Path) ||
                 !_pathPolicySafeWithin(configuration.FolderPath, hint.Path)))
            {
                return;
            }

            await AddDebouncedHintAsync(configuration, hint).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Application shutdown or configuration removal.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "A watcher hint could not be queued; a reconciliation will be required.");
            _reconciliationRequired.TryAdd(hint.ConfigurationId, 0);
        }
    }

    private async Task AddDebouncedHintAsync(
        WatchedFolderConfiguration configuration,
        WatchedFolderHint hint)
    {
        DebounceState state;
        lock (_debounceGate)
        {
            if (_debounceStates.TryGetValue(configuration.Id, out var previous))
            {
                previous.Cancellation.Cancel();
                previous.Cancellation.Dispose();
                previous.Hints.Add(hint);
                state = previous with { Cancellation = new CancellationTokenSource() };
            }
            else
            {
                state = new DebounceState(
                    [hint],
                    new CancellationTokenSource());
            }

            _debounceStates[configuration.Id] = state;
        }

        var detected = configuration with
        {
            Status = hint.Kind == WatchedPathChangeKind.Overflow
                ? WatchedFolderStatus.ReconciliationRequired
                : WatchedFolderStatus.Debouncing,
            LastDetectedChangeUtc = hint.DetectedAtUtc.ToUniversalTime(),
            QueuedChangeCount = state.Hints.Count,
            LatestSummary = hint.Kind == WatchedPathChangeKind.Overflow
                ? "Watcher overflow detected; reconciliation is required."
                : "Waiting for the configured quiet period before analysis.",
        };
        await PublishStateAsync(detected, _lifetimeCancellation.Token).ConfigureAwait(false);
        _ = FlushAfterQuietPeriodAsync(configuration.Id, configuration.QuietPeriod, state);
    }

    private async Task FlushAfterQuietPeriodAsync(
        string configurationId,
        TimeSpan quietPeriod,
        DebounceState scheduledState)
    {
        try
        {
            await Task.Delay(
                quietPeriod,
                _timeProvider,
                scheduledState.Cancellation.Token).ConfigureAwait(false);
            IReadOnlyList<WatchedFolderHint> hints;
            lock (_debounceGate)
            {
                if (!_debounceStates.TryGetValue(configurationId, out var current) ||
                    !ReferenceEquals(current.Hints, scheduledState.Hints) ||
                    !ReferenceEquals(current.Cancellation, scheduledState.Cancellation))
                {
                    return;
                }

                hints = Array.AsReadOnly(current.Hints.ToArray());
                _debounceStates.Remove(configurationId);
            }

            scheduledState.Cancellation.Dispose();
            var normalized = hints
                .GroupBy(
                    item => $"{item.Kind}|{item.OldPath}|{item.Path}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.DetectedAtUtc).First())
                .OrderBy(item => item.DetectedAtUtc)
                .ToArray();
            var configuration = await FindConfigurationAsync(
                configurationId,
                _lifetimeCancellation.Token).ConfigureAwait(false);
            var normalizedHints = Array.AsReadOnly(normalized.ToArray());
            var correlation = await _executionCorrelation.ClassifyBatchAsync(
                configuration,
                normalizedHints,
                _lifetimeCancellation.Token).ConfigureAwait(false);
            var allSelfGenerated = normalized.Length > 0 &&
                                   correlation.Count == normalized.Length &&
                                   correlation.All(value => value);
            if (allSelfGenerated)
            {
                _logger.LogInformation(
                    "Suppressed recursive suggestion generation for {HintCount} journal-correlated watcher hint(s).",
                    normalized.Length);
            }

            var overflow = normalized.Any(item => item.Kind == WatchedPathChangeKind.Overflow);
            var directoryChange = normalized.Any(item => item.IsDirectory);
            var uncertainChange = normalized.Any(item => item.Kind == WatchedPathChangeKind.Unknown);
            var batch = new WatchedChangeBatch(
                $"watch-batch:{Guid.NewGuid():N}",
                configurationId,
                allSelfGenerated ? WatchedScanReason.OpenSorSeExecution :
                overflow ? WatchedScanReason.OverflowRecovery :
                WatchedScanReason.WatcherBatch,
                normalized.Min(item => item.DetectedAtUtc).ToUniversalTime(),
                normalized.Max(item => item.DetectedAtUtc).ToUniversalTime(),
                normalizedHints,
                overflow || directoryChange || uncertainChange,
                allSelfGenerated);
            await RecordActivityAsync(
                configurationId,
                overflow ? WatchedActivityKind.WatcherOverflow : WatchedActivityKind.ChangeBatchDetected,
                overflow
                    ? "Watcher overflow detected; reconciliation queued."
                    : $"{normalized.Length} normalized change hint(s) grouped into one batch.",
                batch.BatchId,
                normalized.Length,
                cancellationToken: _lifetimeCancellation.Token).ConfigureAwait(false);
            if (!TryEnqueueBatch(batch))
            {
                await MarkBackpressureAsync(configurationId, _lifetimeCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by another event or application shutdown.
        }
    }

    private void OnWatcherError(object? sender, Exception exception)
    {
        if (sender is not IWatchedFolderEventSource source)
        {
            return;
        }

        TryEnqueueBatch(NewBatch(
            source.ConfigurationId,
            WatchedScanReason.OverflowRecovery,
            requiresFullReconciliation: true));
        _ = RecordActivityAsync(
            source.ConfigurationId,
            WatchedActivityKind.WatcherOverflow,
            "The operating-system watcher reported an error; reconciliation is required.",
            detail: exception.GetType().Name,
            cancellationToken: _lifetimeCancellation.Token);
    }

    private async Task EnqueueBatchAsync(WatchedChangeBatch batch, CancellationToken cancellationToken)
    {
        if (!TryEnqueueBatch(batch))
        {
            await MarkBackpressureAsync(batch.ConfigurationId, cancellationToken).ConfigureAwait(false);
            await _queue.Writer.WriteAsync(batch with
            {
                RequiresFullReconciliation = true,
                Reason = WatchedScanReason.OverflowRecovery,
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TryEnqueueBatch(WatchedChangeBatch batch)
    {
        if (!_queue.Writer.TryWrite(batch))
        {
            _reconciliationRequired.TryAdd(batch.ConfigurationId, 0);
            _logger.LogWarning(
                "Watched-folder queue reached its bound of {QueueLimit}; reconciliation was marked required.",
                WatchedFolderLimits.MaximumQueuedBatches);
            return false;
        }

        return true;
    }

    private async Task MarkBackpressureAsync(string configurationId, CancellationToken cancellationToken)
    {
        var busy = await _manager.SetRuntimeStateAsync(
            configurationId,
            current => current with
            {
                Status = WatchedFolderStatus.Busy,
                LatestSummary = "Processing is delayed because the bounded watcher queue is busy; reconciliation is required.",
            },
            cancellationToken).ConfigureAwait(false);
        await RecordActivityAsync(
            configurationId,
            WatchedActivityKind.ProcessingDeferred,
            "Processing deferred because the bounded queue is busy.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await PublishStateAsync(busy, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var batch in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await ProcessBatchSafelyAsync(batch, cancellationToken).ConfigureAwait(false);
                if (_reconciliationRequired.TryRemove(batch.ConfigurationId, out _))
                {
                    TryEnqueueBatch(NewBatch(
                        batch.ConfigurationId,
                        WatchedScanReason.OverflowRecovery,
                        requiresFullReconciliation: true));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Controlled shutdown.
        }
    }

    private async Task ProcessBatchSafelyAsync(
        WatchedChangeBatch batch,
        CancellationToken cancellationToken)
    {
        WatchedFolderConfiguration configuration;
        try
        {
            configuration = await FindConfigurationAsync(batch.ConfigurationId, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        if (!configuration.IsEnabled)
        {
            return;
        }

        var reconciliation = batch.RequiresFullReconciliation || IsReconciliationReason(batch.Reason);
        var started = await _manager.SetRuntimeStateAsync(
            configuration.Id,
            current => current with
            {
                Status = current.IsEnabled
                    ? WatchedFolderStatus.Processing
                    : WatchedFolderStatus.Paused,
                QueuedChangeCount = 0,
                LastDetectedChangeUtc = batch.Hints.Count > 0
                    ? batch.LastDetectedAtUtc
                    : current.LastDetectedChangeUtc,
                LatestSummary = reconciliation
                    ? "Reconciling the real folder with the saved catalogue."
                    : "Processing a verified incremental change batch.",
            },
            cancellationToken).ConfigureAwait(false);
        if (!started.IsEnabled)
        {
            return;
        }

        await RecordActivityAsync(
            configuration.Id,
            reconciliation ? WatchedActivityKind.ReconciliationStarted : WatchedActivityKind.IncrementalScanStarted,
            reconciliation ? "Reconciliation started." : "Incremental scan started.",
            batch.BatchId,
            batch.Hints.Count,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await PublishStateAsync(started, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await _processor.ProcessAsync(configuration, batch, cancellationToken).ConfigureAwait(false);
            var summary = result.Summary.ToString();
            if (result.CreatedChangePlans.Count > 0)
            {
                summary += $" {result.CreatedChangePlans.Count} reviewable Change Plan(s) are ready; no file was modified.";
            }

            var completedAt = _timeProvider.GetUtcNow().ToUniversalTime();
            var pendingPlanCount = await CountPendingPlansAsync(
                configuration.Id,
                cancellationToken).ConfigureAwait(false);
            var completed = await _manager.SetRuntimeStateAsync(
                configuration.Id,
                current => current with
                {
                    Status = !current.IsEnabled
                        ? WatchedFolderStatus.Paused
                        : result.Summary.IsComplete
                            ? WatchedFolderStatus.Watching
                            : WatchedFolderStatus.ReconciliationRequired,
                    LastSuccessfulScanUtc = result.Summary.IsComplete ? completedAt : current.LastSuccessfulScanUtc,
                    LastReconciliationUtc = reconciliation ? completedAt : current.LastReconciliationUtc,
                    LatestSummary = summary,
                    LastError = result.Summary.IsComplete
                        ? null
                        : "Some changes remain unresolved and will be retried.",
                    PendingChangePlanCount = pendingPlanCount,
                },
                cancellationToken).ConfigureAwait(false);
            await RecordActivityAsync(
                configuration.Id,
                reconciliation ? WatchedActivityKind.ReconciliationCompleted : WatchedActivityKind.IncrementalScanCompleted,
                summary,
                batch.BatchId,
                result.Summary.ChangedCount,
                result.Warnings.Count == 0 ? null : string.Join(" ", result.Warnings),
                cancellationToken).ConfigureAwait(false);
            if (result.AiAttempted)
            {
                await RecordActivityAsync(
                    configuration.Id,
                    WatchedActivityKind.AiAnalysisAttempted,
                    result.AiFailed
                        ? "Optional AI analysis failed or remained pending; deterministic catalogue updates succeeded."
                        : "Optional AI analysis completed.",
                    batch.BatchId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            foreach (var plan in result.CreatedChangePlans)
            {
                await RecordActivityAsync(
                    configuration.Id,
                    WatchedActivityKind.ChangePlanCreated,
                    $"A reviewable Change Plan with {plan.Actions.Count} suggestion(s) is ready.",
                    batch.BatchId,
                    plan.Actions.Count,
                    "The plan was not applied and requires explicit user review and approval.",
                    cancellationToken).ConfigureAwait(false);
            }

            await PublishStateAsync(completed, cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            await MarkUnavailableAsync(configuration.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkflowProfileUnavailableException exception)
        {
            var unavailable = await _manager.SetRuntimeStateAsync(
                configuration.Id,
                current => current with
                {
                    Status = WatchedFolderStatus.ProfileUnavailable,
                    LastError = exception.Message,
                    LatestSummary = "Profile unavailable — review configuration. No unrelated fallback profile was used.",
                },
                cancellationToken).ConfigureAwait(false);
            await RecordActivityAsync(
                configuration.Id,
                WatchedActivityKind.Error,
                "Watched-folder processing was blocked because its workflow profile or recipe is unavailable.",
                batch.BatchId,
                detail: exception.Message,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await PublishStateAsync(unavailable, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            var inaccessible = await _manager.SetRuntimeStateAsync(
                configuration.Id,
                current => current with
                {
                    Status = WatchedFolderStatus.Inaccessible,
                    LastError = "Access to the watched folder was denied.",
                    LatestSummary = "The catalogue and history were retained. Reconcile after access is restored.",
                },
                cancellationToken).ConfigureAwait(false);
            await RecordActivityAsync(
                configuration.Id,
                WatchedActivityKind.Error,
                "The watched folder could not be scanned because access was denied.",
                batch.BatchId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await PublishStateAsync(inaccessible, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Watched-folder processing failed safely for configuration {ConfigurationId}.",
                configuration.Id);
            var failed = await _manager.SetRuntimeStateAsync(
                configuration.Id,
                current => current with
                {
                    Status = WatchedFolderStatus.Error,
                    LastError = "The change batch could not be completed.",
                    LatestSummary = "No automatic file operation was attempted. Use Full reconciliation to retry.",
                },
                cancellationToken).ConfigureAwait(false);
            await RecordActivityAsync(
                configuration.Id,
                WatchedActivityKind.Error,
                "Watched-folder processing failed safely.",
                batch.BatchId,
                detail: exception.GetType().Name,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await PublishStateAsync(failed, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AvailabilityLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(AvailabilityCheckInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                var configurations = await _manager.ListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var configuration in configurations.Where(configuration => configuration.IsEnabled))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_fileSystem.DirectoryExists(configuration.FolderPath))
                    {
                        if (configuration.Status != WatchedFolderStatus.Unavailable)
                        {
                            await MarkUnavailableAsync(configuration.Id, cancellationToken).ConfigureAwait(false);
                        }

                        continue;
                    }

                    var hasRegistration = false;
                    lock (_debounceGate)
                    {
                        hasRegistration = _registrations.ContainsKey(configuration.Id);
                    }

                    if (!hasRegistration)
                    {
                        await RefreshCoreAsync(
                            WatchedScanReason.ReconnectReconciliation,
                            queueReconciliationForNewSources: true,
                            cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    if (configuration.LastReconciliationUtc is null ||
                        _timeProvider.GetUtcNow() - configuration.LastReconciliationUtc >=
                        PeriodicReconciliationInterval)
                    {
                        TryEnqueueBatch(NewBatch(
                            configuration.Id,
                            WatchedScanReason.StartupOfflineReconciliation,
                            requiresFullReconciliation: true));
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "The watched-folder availability check failed safely and will retry.");
            }
        }
    }

    private async Task MarkUnavailableAsync(string configurationId, CancellationToken cancellationToken)
    {
        var unavailable = await _manager.SetRuntimeStateAsync(
            configurationId,
            current => current with
            {
                Status = WatchedFolderStatus.Unavailable,
                LastError = "The watched folder is unavailable or disconnected.",
                LatestSummary = "The configuration, catalogue, and history were retained.",
            },
            cancellationToken).ConfigureAwait(false);
        await RecordActivityAsync(
            configurationId,
            WatchedActivityKind.FolderUnavailable,
            "Watched folder unavailable.",
            detail: "No catalogue entries were erased.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await PublishStateAsync(unavailable, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WatchedFolderConfiguration> FindConfigurationAsync(
        string configurationId,
        CancellationToken cancellationToken)
    {
        var configurations = await _manager.ListAsync(cancellationToken).ConfigureAwait(false);
        return configurations.FirstOrDefault(configuration =>
                   string.Equals(configuration.Id, configurationId, StringComparison.Ordinal))
               ?? throw new KeyNotFoundException("The watched-folder configuration no longer exists.");
    }

    private async Task RecordActivityAsync(
        string configurationId,
        WatchedActivityKind kind,
        string summary,
        string? batchId = null,
        int itemCount = 0,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var activity = new WatchedActivityEntry(
            $"watch-activity:{Guid.NewGuid():N}",
            configurationId,
            kind,
            _timeProvider.GetUtcNow().ToUniversalTime(),
            summary,
            batchId,
            itemCount,
            detail);
        try
        {
            await _activityStore.AppendAsync(activity, cancellationToken).ConfigureAwait(false);
            ActivityPublished?.Invoke(this, activity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Watched activity history could not be persisted; primary processing continued.");
        }
    }

    private async Task PublishStateAsync(
        WatchedFolderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchedActivityEntry> activity;
        try
        {
            activity = await _activityStore.ListAsync(
                configuration.Id,
                25,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            activity = Array.Empty<WatchedActivityEntry>();
        }

        StateChanged?.Invoke(this, new WatchedFolderRuntimeSnapshot(configuration, activity));
    }

    private void OnConfigurationsChanged(object? sender, EventArgs eventArgs)
    {
        if (!_disposed && _initialized)
        {
            _ = RefreshAsync(_lifetimeCancellation.Token);
        }
    }

    private static WatchedChangeBatch NewBatch(
        string configurationId,
        WatchedScanReason reason,
        bool requiresFullReconciliation)
    {
        var now = DateTimeOffset.UtcNow;
        return new WatchedChangeBatch(
            $"watch-batch:{Guid.NewGuid():N}",
            configurationId,
            reason,
            now,
            now,
            Array.Empty<WatchedFolderHint>(),
            requiresFullReconciliation,
            reason == WatchedScanReason.OpenSorSeExecution);
    }

    private static bool SameRegistrationSettings(
        WatchedFolderConfiguration first,
        WatchedFolderConfiguration second) =>
        WatchedFolderPathPolicy.PathComparer.Equals(first.FolderPath, second.FolderPath) &&
        first.IncludeSubfolders == second.IncludeSubfolders &&
        first.IsEnabled == second.IsEnabled;

    private static bool SameProcessingSettings(
        WatchedFolderConfiguration first,
        WatchedFolderConfiguration second) =>
        SameRegistrationSettings(first, second) &&
        string.Equals(first.DisplayName, second.DisplayName, StringComparison.Ordinal) &&
        first.IgnoredPaths.SequenceEqual(second.IgnoredPaths, WatchedFolderPathPolicy.PathComparer) &&
        first.IgnorePatterns.SequenceEqual(second.IgnorePatterns, StringComparer.OrdinalIgnoreCase) &&
        string.Equals(first.ScanProfileId, second.ScanProfileId, StringComparison.Ordinal) &&
        string.Equals(first.SortingRecipeId, second.SortingRecipeId, StringComparison.Ordinal) &&
        first.SortingRecipeIds.SequenceEqual(second.SortingRecipeIds, StringComparer.Ordinal) &&
        first.ProfileOverride == second.ProfileOverride &&
        first.DeterministicAnalysisEnabled == second.DeterministicAnalysisEnabled &&
        first.AiAnalysisEnabled == second.AiAnalysisEnabled &&
        first.Notifications == second.Notifications &&
        first.QuietPeriod == second.QuietPeriod &&
        first.MaximumFileSizeBytes == second.MaximumFileSizeBytes &&
        first.IgnoreHiddenFiles == second.IgnoreHiddenFiles;

    private async Task<int> CountPendingPlansAsync(
        string configurationId,
        CancellationToken cancellationToken)
    {
        if (_changePlanStore is null)
        {
            var configuration = await FindConfigurationAsync(configurationId, cancellationToken).ConfigureAwait(false);
            return configuration.PendingChangePlanCount;
        }

        var plans = await _changePlanStore.ListAsync(cancellationToken).ConfigureAwait(false);
        return plans.Count(plan =>
            plan.SourceScanId?.StartsWith($"watched:{configurationId}:", StringComparison.Ordinal) == true &&
            plan.Status is not ChangePlanStatus.Applied and not ChangePlanStatus.Rejected);
    }

    private static bool IsReconciliationReason(WatchedScanReason reason) => reason is
        WatchedScanReason.UserFullReconciliation or
        WatchedScanReason.StartupOfflineReconciliation or
        WatchedScanReason.ResumeReconciliation or
        WatchedScanReason.OverflowRecovery or
        WatchedScanReason.ReconnectReconciliation or
        WatchedScanReason.ConfigurationChangedReconciliation;

    private static bool _pathPolicySafeWithin(string root, string candidate)
    {
        try
        {
            var canonicalRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var canonicalCandidate = Path.GetFullPath(candidate);
            return WatchedFolderPathPolicy.PathComparer.Equals(canonicalRoot, canonicalCandidate) ||
                   canonicalCandidate.StartsWith(
                       canonicalRoot + Path.DirectorySeparatorChar,
                       WatchedFolderPathPolicy.PathComparison);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record WatchRegistration(
        WatchedFolderConfiguration Configuration,
        IWatchedFolderEventSource Source);

    private sealed record DebounceState(
        List<WatchedFolderHint> Hints,
        CancellationTokenSource Cancellation);
}
