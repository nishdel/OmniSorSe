using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Validates automatic, coalesced, observed background graph reconciliation.</summary>
public sealed class GraphBackgroundRuntimeTests
{
    /// <summary>Verifies enabled pending work reconciles automatically on application startup.</summary>
    [Fact]
    public async Task Start_EnabledGraph_ReconcilesAutomatically()
    {
        var coordinator = new RuntimeCoordinator();
        await using var runtime = Runtime(coordinator);

        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);

        Assert.Equal(1, coordinator.InitializeCount);
        await runtime.StopAsync(TimeSpan.Zero);
        Assert.Equal(1, coordinator.StopCount);
    }

    /// <summary>Verifies stop is idempotent and a terminal runtime cannot silently restart dead workers.</summary>
    [Fact]
    public async Task Stop_IsIdempotentAndRestartIsExplicitlyRejected()
    {
        var coordinator = new RuntimeCoordinator();
        await using var runtime = Runtime(coordinator);
        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);

        await runtime.StopAsync(TimeSpan.Zero);
        await runtime.StopAsync(TimeSpan.Zero);

        Assert.Equal(1, coordinator.StopCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());
    }

    /// <summary>Verifies a coordinator stop failure still cancels and observes both runtime workers.</summary>
    [Fact]
    public async Task Stop_CoordinatorFailure_StillTerminatesRuntime()
    {
        var coordinator = new RuntimeCoordinator { StopFailure = new GraphPersistenceException("stop-failed", "Synthetic stop failure.") };
        await using var runtime = Runtime(coordinator);
        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);

        await Assert.ThrowsAsync<GraphPersistenceException>(() => runtime.StopAsync(TimeSpan.Zero));

        await runtime.StopAsync(TimeSpan.Zero);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SignalAsync());
        Assert.Equal(1, coordinator.StopCount);
    }

    /// <summary>Verifies repeated asynchronous disposal does not repeat coordinator shutdown.</summary>
    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var coordinator = new RuntimeCoordinator();
        var runtime = Runtime(coordinator);
        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);

        await runtime.DisposeAsync();
        await runtime.DisposeAsync();

        Assert.Equal(1, coordinator.StopCount);
    }

    /// <summary>Verifies a terminal indexing transition triggers eventual graph projection.</summary>
    [Fact]
    public async Task IndexingTerminalState_TriggersReconciliation()
    {
        var coordinator = new RuntimeCoordinator();
        var indexing = new RuntimeIndexingService();
        await using var runtime = Runtime(coordinator, indexing);
        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);

        indexing.Raise(IndexingRunStatus.CompleteWithFailures);
        await coordinator.WaitForCountAsync(2);

        await runtime.StopAsync(TimeSpan.Zero);
        Assert.Equal(2, coordinator.ReconcileCount);
    }

    /// <summary>Verifies a low-frequency safety net reconciles decisions even when notifications are missed.</summary>
    [Fact]
    public async Task PeriodicBoundary_MissedNotificationStillReconciles()
    {
        var coordinator = new RuntimeCoordinator();
        var periodic = new ManualPeriodicScheduler();
        await using var runtime = Runtime(coordinator, periodicScheduler: periodic);
        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);
        await periodic.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        periodic.Trigger();
        await coordinator.WaitForCountAsync(2);

        await runtime.StopAsync(TimeSpan.Zero);
        Assert.Equal(2, coordinator.ReconcileCount);
    }

    /// <summary>Verifies the periodic safety net respects an explicit paused state.</summary>
    [Fact]
    public async Task PeriodicBoundary_PausedGraphDoesNotResumeItself()
    {
        var coordinator = new RuntimeCoordinator
        {
            Status = RuntimeCoordinator.NewStatus() with { RunControl = GraphRunControlState.Paused },
        };
        var periodic = new ManualPeriodicScheduler();
        await using var runtime = Runtime(coordinator, periodicScheduler: periodic);
        await runtime.StartAsync();
        await periodic.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        periodic.Trigger();
        await runtime.StopAsync(TimeSpan.Zero);

        Assert.Equal(0, coordinator.ReconcileCount);
    }

    /// <summary>Verifies burst signals collapse to one follow-up while a reconciliation is active.</summary>
    [Fact]
    public async Task SignalBurst_CoalescesWithoutConcurrentWorkers()
    {
        var coordinator = new RuntimeCoordinator();
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.ReconcileHandler = async (count, token) =>
        {
            if (count == 1)
            {
                await releaseFirst.Task.WaitAsync(token);
            }

            return new GraphOperationResult(true, "complete", 0);
        };
        await using var runtime = Runtime(coordinator);
        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);

        for (var index = 0; index < 50; index++)
        {
            await runtime.SignalAsync();
        }

        releaseFirst.TrySetResult(true);
        await coordinator.WaitForCountAsync(2);
        await runtime.StopAsync(TimeSpan.Zero);

        Assert.Equal(2, coordinator.ReconcileCount);
        Assert.Equal(1, coordinator.MaximumConcurrentReconciliations);
    }

    /// <summary>Verifies resource waits persist until a scheduler probe triggers another eligibility check.</summary>
    [Fact]
    public async Task ResourceWaiting_ProbeResumesReconciliation()
    {
        var coordinator = new RuntimeCoordinator();
        var probe = new ManualResourceProbeScheduler();
        coordinator.ReconcileHandler = (count, _) =>
        {
            coordinator.Status = coordinator.Status with { WaitingCount = count == 1 ? 1 : 0 };
            return Task.FromResult(new GraphOperationResult(true, "observed", 0));
        };
        await using var runtime = Runtime(coordinator, resourceProbeScheduler: probe);
        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);
        await probe.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        probe.Release();
        await coordinator.WaitForCountAsync(2);

        await runtime.StopAsync(TimeSpan.Zero);
        Assert.Equal(2, coordinator.ReconcileCount);
    }

    /// <summary>Verifies background failures are observed and logged by category without private exception text.</summary>
    [Fact]
    public async Task ReconciliationFailure_IsObservedAndRedacted()
    {
        var coordinator = new RuntimeCoordinator();
        coordinator.ReconcileHandler = (count, _) => count == 1
            ? Task.FromException<GraphOperationResult>(new PrivateGraphException("secret query C:\\private\\file.txt"))
            : Task.FromResult(new GraphOperationResult(true, "recovered", 0));
        var logging = new CapturingLoggingService();
        await using var runtime = Runtime(coordinator, loggingService: logging);
        await runtime.StartAsync();
        await coordinator.WaitForCountAsync(1);

        await runtime.SignalAsync();
        await coordinator.WaitForCountAsync(2);
        await runtime.StopAsync(TimeSpan.Zero);

        Assert.Contains(logging.Messages, message => message.Contains(nameof(PrivateGraphException), StringComparison.Ordinal));
        Assert.DoesNotContain(logging.Messages, message => message.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logging.Messages, message => message.Contains("private\\file", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies verified quota pressure schedules one bounded maintenance pass after reconciliation.</summary>
    [Fact]
    public async Task QuotaPressure_RunsReviewedMaintenanceAfterReconciliation()
    {
        var coordinator = new RuntimeCoordinator
        {
            Status = RuntimeCoordinator.NewStatus() with
            {
                IsProvisioned = true,
                StorageSizeBytes = 12L * 1024L * 1024L,
                MaximumStorageSizeBytes = GraphLimits.MinimumStorageQuotaBytes,
            },
        };
        await using var runtime = Runtime(coordinator);

        await runtime.StartAsync();
        await coordinator.MaintenanceCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, coordinator.MaintenanceCount);
        await runtime.StopAsync(TimeSpan.Zero);
    }

    private static GraphBackgroundRuntime Runtime(
        RuntimeCoordinator coordinator,
        RuntimeIndexingService? indexing = null,
        CapturingLoggingService? loggingService = null,
        IGraphResourceProbeScheduler? resourceProbeScheduler = null,
        IGraphPeriodicReconciliationScheduler? periodicScheduler = null) => new(
            coordinator,
            indexing ?? new RuntimeIndexingService(),
            loggingService ?? new CapturingLoggingService(),
            resourceProbeScheduler ?? new NeverResourceProbeScheduler(),
            periodicScheduler ?? new NeverPeriodicScheduler());

    private sealed class RuntimeCoordinator : IGraphProjectionCoordinator
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _waiters = new();
        private int _active;
        private int _reconcileCount;

        internal GraphCoordinatorStatus Status { get; set; } = NewStatus();
        internal int InitializeCount { get; private set; }
        internal int ReconcileCount => Volatile.Read(ref _reconcileCount);
        internal int StopCount { get; private set; }
        internal int MaintenanceCount { get; private set; }
        internal TaskCompletionSource<bool> MaintenanceCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int MaximumConcurrentReconciliations { get; private set; }
        internal Func<int, CancellationToken, Task<GraphOperationResult>>? ReconcileHandler { get; set; }
        internal Exception? StopFailure { get; set; }

        public event EventHandler<GraphCoordinatorStatus>? StatusChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCount++;
            return Task.CompletedTask;
        }

        public Task<GraphCoordinatorStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);

        public Task<GraphControlSettings> GetControlSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphControlSettings { IsEnabled = Status.IsEnabled, ConsentConfirmed = Status.IsEnabled });

        public Task<GraphControlSettings> UpdateResourceSettingsAsync(
            GraphResourceControlUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphControlSettings
            {
                IsEnabled = Status.IsEnabled,
                ConsentConfirmed = Status.IsEnabled,
                ResourceMode = update.ResourceMode,
                ProcessOnlyWhileIdle = update.ProcessOnlyWhileIdle,
                ProcessOnlyWhileConnectedToPower = update.ProcessOnlyWhileConnectedToPower,
                PauseBelowBatteryPercentage = update.PauseBelowBatteryPercentage,
                ProcessingWindowStartHour = update.ProcessingWindowStartHour,
                ProcessingWindowEndHour = update.ProcessingWindowEndHour,
                Revision = update.ExpectedRevision + 1,
            });

        public Task<GraphMaintenanceResult> MaintainAsync(
            GraphMaintenanceRequest request,
            CancellationToken cancellationToken = default)
        {
            MaintenanceCount++;
            Status = Status with
            {
                StorageSizeBytes = 8L * 1024L * 1024L,
                Maintenance = new GraphMaintenanceStatus(false, false, TestGraphData.Now, 0, "maintenance-complete"),
            };
            MaintenanceCalled.TrySetResult(true);
            return Task.FromResult(new GraphMaintenanceResult(0, 0, 0, false, TestGraphData.Now, "maintenance-complete"));
        }

        public Task<GraphOperationResult> EnableAsync(bool consentConfirmed, CancellationToken cancellationToken = default)
        {
            Status = Status with { IsEnabled = true };
            StatusChanged?.Invoke(this, Status);
            return Task.FromResult(new GraphOperationResult(true, "enabled", 1));
        }

        public Task<GraphOperationResult> DisableAsync(CancellationToken cancellationToken = default)
        {
            Status = Status with { IsEnabled = false };
            StatusChanged?.Invoke(this, Status);
            return Task.FromResult(new GraphOperationResult(true, "disabled", 1));
        }

        public async Task<GraphOperationResult> ReconcileAsync(CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrentReconciliations = Math.Max(MaximumConcurrentReconciliations, active);
            var count = Interlocked.Increment(ref _reconcileCount);
            CompleteWaiters(count);
            try
            {
                return ReconcileHandler is null
                    ? new GraphOperationResult(true, "complete", 0)
                    : await ReconcileHandler(count, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task<GraphOperationResult> PauseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphOperationResult(true, "paused", 0));

        public Task<GraphOperationResult> ResumeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphOperationResult(true, "resumed", 0));

        public Task<GraphOperationResult> CancelAsync(string reasonCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphOperationResult(true, reasonCode, 0));

        public Task<GraphOperationResult> RetryAsync(string? workId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphOperationResult(true, "retry", 0));

        public Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
        {
            StopCount++;
            return StopFailure is null ? Task.CompletedTask : Task.FromException(StopFailure);
        }

        internal Task WaitForCountAsync(int count)
        {
            if (Volatile.Read(ref _reconcileCount) >= count)
            {
                return Task.CompletedTask;
            }

            var waiter = _waiters.GetOrAdd(
                count,
                static _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            if (Volatile.Read(ref _reconcileCount) >= count)
            {
                waiter.TrySetResult(true);
            }

            return waiter.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void CompleteWaiters(int count)
        {
            foreach (var pair in _waiters.Where(pair => pair.Key <= count))
            {
                pair.Value.TrySetResult(true);
            }
        }

        internal static GraphCoordinatorStatus NewStatus() => new()
        {
            IsEnabled = true,
            RunControl = GraphRunControlState.Pending,
            PendingCount = 1,
            Coverage = TestGraphData.Coverage,
        };
    }

    private sealed class RuntimeIndexingService : IBackgroundIndexingService
    {
        public event EventHandler<IndexingProgressSnapshot>? ProgressChanged;

        internal void Raise(IndexingRunStatus status) =>
            ProgressChanged?.Invoke(this, new IndexingProgressSnapshot { Status = status });

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> QueueFolderAsync(string rootPath, IndexingLevel? level = null, bool includeSubfolders = true, IReadOnlyList<string>? exclusions = null, CancellationToken cancellationToken = default) => Task.FromResult("synthetic-source");
        public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IndexingSource>>([]);
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelAsync(string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task PrioritizeSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IndexingProgressSnapshot> GetProgressAsync(CancellationToken cancellationToken = default) => Task.FromResult(new IndexingProgressSnapshot());
        public Task<IndexStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default) => Task.FromResult(EmptyStorage());
        public Task<IReadOnlyList<IndexingFailure>> GetFailuresAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IndexingFailure>>([]);
        public Task<IndexMaintenanceResult> MaintainAsync(CancellationToken cancellationToken = default) => Task.FromResult(new IndexMaintenanceResult([], EmptyStorage(), true));
        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);
        public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SearchCoverage(0, 0, 0, 0, 0, 0));
        public Task<IReadOnlyList<string>> GetExcludedPathsAsync(int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(IReadOnlyList<string> fileIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static IndexStorageBreakdown EmptyStorage() => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private sealed class ManualResourceProbeScheduler : IGraphResourceProbeScheduler
    {
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitForNextProbeAsync(CancellationToken cancellationToken = default)
        {
            Waiting.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
        }

        internal void Release() => _release.TrySetResult(true);
    }

    private sealed class NeverResourceProbeScheduler : IGraphResourceProbeScheduler
    {
        public Task WaitForNextProbeAsync(CancellationToken cancellationToken = default) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class ManualPeriodicScheduler : IGraphPeriodicReconciliationScheduler
    {
        private readonly object _sync = new();
        private TaskCompletionSource<bool> _next = NewCompletion();
        internal TaskCompletionSource<bool> Waiting { get; } = NewCompletion();

        public Task WaitForNextReconciliationAsync(CancellationToken cancellationToken = default)
        {
            Waiting.TrySetResult(true);
            lock (_sync)
            {
                return _next.Task.WaitAsync(cancellationToken);
            }
        }

        internal void Trigger()
        {
            TaskCompletionSource<bool> current;
            lock (_sync)
            {
                current = _next;
                _next = NewCompletion();
            }

            current.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class NeverPeriodicScheduler : IGraphPeriodicReconciliationScheduler
    {
        public Task WaitForNextReconciliationAsync(CancellationToken cancellationToken = default) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class CapturingLoggingService : ILoggingService
    {
        internal List<string> Messages { get; } = [];
        public void Initialize(LogLevel minimumLevel) { }
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }

    private sealed class PrivateGraphException(string message) : Exception(message);
}
