using System.Threading.Channels;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Coalesces indexing and lifecycle signals into one observed graph reconciliation worker.</summary>
public sealed class GraphBackgroundRuntime : IGraphBackgroundRuntime
{
    private readonly IGraphProjectionCoordinator _coordinator;
    private readonly IBackgroundIndexingService _indexingService;
    private readonly ILogger _logger;
    private readonly IGraphResourceProbeScheduler _resourceProbeScheduler;
    private readonly IGraphPeriodicReconciliationScheduler _periodicScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<bool> _signals;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private Task? _worker;
    private Task? _periodicWorker;
    private GraphCoordinatorStatus? _lastStatus;
    private bool _started;
    private bool _stopped;
    private bool _disposed;

    /// <summary>Initializes an observed bounded background runtime.</summary>
    public GraphBackgroundRuntime(
        IGraphProjectionCoordinator coordinator,
        IBackgroundIndexingService indexingService,
        ILoggingService loggingService,
        IGraphResourceProbeScheduler? resourceProbeScheduler = null,
        IGraphPeriodicReconciliationScheduler? periodicScheduler = null,
        TimeProvider? timeProvider = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(GraphBackgroundRuntime));
        _resourceProbeScheduler = resourceProbeScheduler ?? new GraphResourceProbeScheduler();
        _periodicScheduler = periodicScheduler ?? new GraphPeriodicReconciliationScheduler();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return;
            }

            if (_stopped)
            {
                throw new InvalidOperationException("A stopped graph background runtime cannot be restarted; create a new runtime instance.");
            }

            _coordinator.StatusChanged += OnCoordinatorStatusChanged;
            _indexingService.ProgressChanged += OnIndexingProgressChanged;
            try
            {
                await _coordinator.InitializeAsync(cancellationToken).ConfigureAwait(false);
                _lastStatus = await _coordinator.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                _worker = RunWorkerAsync(_shutdown.Token);
                _periodicWorker = RunPeriodicWorkerAsync(_shutdown.Token);
                _started = true;
                if (_lastStatus.IsEnabled &&
                    _lastStatus.RunControl is not (GraphRunControlState.Paused or GraphRunControlState.PauseRequested or
                        GraphRunControlState.Cancelled or GraphRunControlState.CancelRequested))
                {
                    _signals.Writer.TryWrite(true);
                }
            }
            catch
            {
                _coordinator.StatusChanged -= OnCoordinatorStatusChanged;
                _indexingService.ProgressChanged -= OnIndexingProgressChanged;
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask SignalAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_started)
        {
            throw new InvalidOperationException("The graph background runtime has not started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _signals.Writer.TryWrite(true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                return;
            }

            _coordinator.StatusChanged -= OnCoordinatorStatusChanged;
            _indexingService.ProgressChanged -= OnIndexingProgressChanged;
            _signals.Writer.TryComplete();
            Exception? failure = null;
            try
            {
                await _coordinator.StopAsync(gracePeriod, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _shutdown.Cancel();
                failure = await ObserveShutdownTaskAsync(_worker, failure).ConfigureAwait(false);
                failure = await ObserveShutdownTaskAsync(_periodicWorker, failure).ConfigureAwait(false);
                _started = false;
                _stopped = true;
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        Exception? failure = null;
        if (_started)
        {
            using var cancellation = new CancellationTokenSource(GraphLimits.ShutdownGracePeriod);
            try
            {
                await StopAsync(GraphLimits.ShutdownGracePeriod, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _shutdown.Cancel();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        _disposed = true;
        _shutdown.Dispose();
        _lifecycleGate.Dispose();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var signal in _signals.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _ = signal;
                while (_signals.Reader.TryRead(out _))
                {
                    // One reconciliation observes the newest completed manifest, so duplicate signals coalesce.
                }

                try
                {
                    await _coordinator.ReconcileAsync(cancellationToken).ConfigureAwait(false);
                    var status = await _coordinator.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                    if (ShouldRunQuotaMaintenance(status))
                    {
                        await _coordinator.MaintainAsync(
                            new GraphMaintenanceRequest(
                                status.MaximumStorageSizeBytes,
                                GraphMaintenanceTrigger.AutomaticQuotaPressure),
                            cancellationToken).ConfigureAwait(false);
                        status = await _coordinator.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (status.WaitingCount > 0)
                    {
                        await _resourceProbeScheduler.WaitForNextProbeAsync(cancellationToken).ConfigureAwait(false);
                        _signals.Writer.TryWrite(true);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        "Knowledge Graph background reconciliation failed in category {FailureCategory}; no query, path, or source content was logged.",
                        exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // StopAsync observes this worker and durable claims recover on restart.
        }
    }

    private async Task RunPeriodicWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _periodicScheduler.WaitForNextReconciliationAsync(cancellationToken).ConfigureAwait(false);
                if (_lastStatus is { IsEnabled: true } status &&
                    status.RunControl is not (GraphRunControlState.Paused or GraphRunControlState.PauseRequested or
                        GraphRunControlState.Cancelled or GraphRunControlState.CancelRequested))
                {
                    _signals.Writer.TryWrite(true);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // StopAsync observes this task.
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Knowledge Graph periodic reconciliation scheduling failed in category {FailureCategory}; no private content was logged.",
                exception.GetType().Name);
        }
    }

    private void OnIndexingProgressChanged(object? sender, IndexingProgressSnapshot snapshot)
    {
        if (snapshot.Status is IndexingRunStatus.Complete or IndexingRunStatus.CompleteWithFailures or
            IndexingRunStatus.Cancelled or IndexingRunStatus.Failed)
        {
            _signals.Writer.TryWrite(true);
        }
    }

    private void OnCoordinatorStatusChanged(object? sender, GraphCoordinatorStatus status)
    {
        var previous = _lastStatus;
        _lastStatus = status;
        if (!status.IsEnabled)
        {
            return;
        }

        var enabledNow = previous is not null && !previous.IsEnabled;
        var resumedNow = previous is not null &&
            previous.RunControl is GraphRunControlState.Paused or GraphRunControlState.Cancelled &&
            status.RunControl == GraphRunControlState.Running;
        var retryQueued = previous is not null && status.PendingCount > previous.PendingCount;
        if (enabledNow || resumedNow || retryQueued)
        {
            _signals.Writer.TryWrite(true);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private bool ShouldRunQuotaMaintenance(GraphCoordinatorStatus status) =>
        status.IsProvisioned && status.IsEnabled && status.RunningCount == 0 &&
        status.RunControl is not (GraphRunControlState.PauseRequested or GraphRunControlState.CancelRequested) &&
        status.MaximumStorageSizeBytes >= GraphLimits.MinimumStorageQuotaBytes &&
        status.StorageSizeBytes >= status.MaximumStorageSizeBytes * 3 / 4 &&
        (status.Maintenance.LastCompletedAtUtc is null ||
         status.Maintenance.QuotaBlocked ||
         _timeProvider.GetUtcNow() - status.Maintenance.LastCompletedAtUtc >= GraphLimits.PeriodicReconciliationInterval);

    private async Task<Exception?> ObserveShutdownTaskAsync(Task? task, Exception? priorFailure)
    {
        if (task is null)
        {
            return priorFailure;
        }

        try
        {
            await task.ConfigureAwait(false);
            return priorFailure;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return priorFailure;
        }
        catch (Exception exception)
        {
            return priorFailure is null ? exception : new AggregateException(priorFailure, exception);
        }
    }
}
