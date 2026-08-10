#pragma warning disable CS1591

using System.Collections.Concurrent;
using OpenSorSe.Application.Watching;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

public sealed class WatchedFolderCoordinatorTests
{
    [Fact]
    public async Task Initialize_ConcurrentCallers_StartExactlyOneWatcherAndStartupBatch()
    {
        await using var context = await CoordinatorContext.CreateAsync();

        await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => context.Coordinator.InitializeAsync(CancellationToken.None)));
        await WaitForInitialReconciliationAsync(context);

        Assert.Single(context.SourceFactory.Sources);
        Assert.Single(context.Processor.Batches);
        Assert.True(context.SourceFactory.Sources[0].IsStarted);
        Assert.Equal(
            WatchedScanReason.StartupOfflineReconciliation,
            context.Processor.Batches[0].Reason);
    }

    [Fact]
    public async Task Dispose_ConcurrentCallers_StopWatcherExactlyOnceWithoutRaces()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        var source = Assert.Single(context.SourceFactory.Sources);

        await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => context.Coordinator.DisposeAsync().AsTask()));

        Assert.True(source.IsDisposed);
        await context.Coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PublishedObservers_ThrowingHandlers_DoNotSuppressLaterHandlers()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        var stateNotifications = 0;
        var activityNotifications = 0;
        context.Coordinator.StateChanged += (_, _) =>
            throw new InvalidOperationException("state observer failure");
        context.Coordinator.StateChanged += (_, _) => stateNotifications++;
        context.Coordinator.ActivityPublished += (_, _) =>
            throw new InvalidOperationException("activity observer failure");
        context.Coordinator.ActivityPublished += (_, _) => activityNotifications++;

        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        await WaitUntilAsync(() => stateNotifications > 0 && activityNotifications > 0);

        Assert.True(stateNotifications > 0);
        Assert.True(activityNotifications > 0);
        Assert.Equal(
            WatchedFolderStatus.Watching,
            Assert.Single(await context.Manager.ListAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Initialize_StartsWatcherAndQueuesOfflineReconciliation()
    {
        await using var context = await CoordinatorContext.CreateAsync();

        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);

        Assert.Single(context.SourceFactory.Sources);
        Assert.True(context.SourceFactory.Sources[0].IsStarted);
        Assert.Equal(
            WatchedScanReason.StartupOfflineReconciliation,
            context.Processor.Batches[0].Reason);
        Assert.True(context.Processor.Batches[0].RequiresFullReconciliation);
        var configuration = Assert.Single(await context.Manager.ListAsync(CancellationToken.None));
        Assert.Equal(WatchedFolderStatus.Watching, configuration.Status);
        Assert.NotNull(configuration.LastReconciliationUtc);
    }

    [Fact]
    public async Task EventBurst_IsDebouncedDeduplicatedAndProcessedAsOneLogicalBatch()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        context.Processor.Batches.Clear();
        var path = Path.Combine(context.Root, "new.txt");
        var detected = DateTimeOffset.UtcNow;

        context.SourceFactory.Sources[0].Raise(
            new WatchedFolderHint(context.Configuration.Id, WatchedPathChangeKind.FileCreated, path, null, detected));
        context.SourceFactory.Sources[0].Raise(
            new WatchedFolderHint(context.Configuration.Id, WatchedPathChangeKind.FileModified, path, null, detected.AddMilliseconds(10)));
        context.SourceFactory.Sources[0].Raise(
            new WatchedFolderHint(context.Configuration.Id, WatchedPathChangeKind.FileCreated, path, null, detected.AddMilliseconds(20)));
        await WaitForInitialReconciliationAsync(context);

        var batch = Assert.Single(context.Processor.Batches);
        Assert.Equal(WatchedScanReason.WatcherBatch, batch.Reason);
        Assert.Equal(2, batch.Hints.Count);
        Assert.False(batch.RequiresFullReconciliation);
        var activity = await context.ActivityStore.ListAsync(context.Configuration.Id, 50, CancellationToken.None);
        Assert.Contains(activity, item => item.Kind == WatchedActivityKind.ChangeBatchDetected);
        Assert.DoesNotContain(activity, item => item.Summary.Contains("raw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SameKindPathsDifferingOnlyByCase_FollowHostPathSemantics()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        context.Processor.Batches.Clear();
        var lowerPath = Path.Combine(context.Root, "case.txt");
        var upperPath = Path.Combine(context.Root, "CASE.txt");
        var detected = DateTimeOffset.UtcNow;

        context.SourceFactory.Sources[0].Raise(new WatchedFolderHint(
            context.Configuration.Id,
            WatchedPathChangeKind.FileCreated,
            lowerPath,
            null,
            detected));
        context.SourceFactory.Sources[0].Raise(new WatchedFolderHint(
            context.Configuration.Id,
            WatchedPathChangeKind.FileCreated,
            upperPath,
            null,
            detected.AddMilliseconds(10)));
        await WaitForBatchCountAsync(context, 1);

        var expectedCount = WatchedFolderPathPolicy.PathComparer.Equals(lowerPath, upperPath)
            ? 1
            : 2;
        Assert.Equal(expectedCount, Assert.Single(context.Processor.Batches).Hints.Count);
    }

    [Fact]
    public async Task DirectoryEventAndOverflow_RequireReconciliationAndRemainVisibleInHistory()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        context.Processor.Batches.Clear();
        var detected = DateTimeOffset.UtcNow;
        context.SourceFactory.Sources[0].Raise(
            new WatchedFolderHint(
                context.Configuration.Id,
                WatchedPathChangeKind.DirectoryMoved,
                Path.Combine(context.Root, "New"),
                Path.Combine(context.Root, "Old"),
                detected,
                true));
        await WaitForBatchCountAsync(context, 1);
        Assert.True(Assert.Single(context.Processor.Batches).RequiresFullReconciliation);
        await WaitForWatchingAsync(context);
        context.Processor.Batches.Clear();

        context.SourceFactory.Sources[0].Raise(
            new WatchedFolderHint(
                context.Configuration.Id,
                WatchedPathChangeKind.Overflow,
                string.Empty,
                null,
                detected.AddSeconds(1)));
        await WaitForInitialReconciliationAsync(context);

        var overflow = Assert.Single(context.Processor.Batches);
        Assert.Equal(WatchedScanReason.OverflowRecovery, overflow.Reason);
        Assert.True(overflow.RequiresFullReconciliation);
        var activity = await context.ActivityStore.ListAsync(context.Configuration.Id, 50, CancellationToken.None);
        Assert.Contains(activity, item => item.Kind == WatchedActivityKind.WatcherOverflow);
    }

    [Fact]
    public async Task UnknownDeleteHint_ConservativelyRequiresReconciliation()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        context.Processor.Batches.Clear();

        context.SourceFactory.Sources[0].Raise(new WatchedFolderHint(
            context.Configuration.Id,
            WatchedPathChangeKind.Unknown,
            Path.Combine(context.Root, "removed-path"),
            null,
            DateTimeOffset.UtcNow,
            false));
        await WaitForInitialReconciliationAsync(context);

        Assert.True(Assert.Single(context.Processor.Batches).RequiresFullReconciliation);
    }

    [Fact]
    public async Task WatcherError_ImmediatelyQueuesVisibleOverflowRecovery()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        context.Processor.Batches.Clear();

        context.SourceFactory.Sources[0].RaiseError(new InternalBufferOverflowException("Overflow."));
        await WaitForInitialReconciliationAsync(context);

        var batch = Assert.Single(context.Processor.Batches);
        Assert.Equal(WatchedScanReason.OverflowRecovery, batch.Reason);
        Assert.True(batch.RequiresFullReconciliation);
        var activity = await context.ActivityStore.ListAsync(
            context.Configuration.Id,
            50,
            CancellationToken.None);
        Assert.Contains(activity, item => item.Kind == WatchedActivityKind.WatcherOverflow);
    }

    [Fact]
    public async Task CorrelatedExecutionEvents_ReconcileWithoutRecursiveSuggestions()
    {
        await using var context = await CoordinatorContext.CreateAsync(correlationResult: true);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        context.Processor.Batches.Clear();
        var detected = DateTimeOffset.UtcNow;
        context.SourceFactory.Sources[0].Raise(
            new WatchedFolderHint(
                context.Configuration.Id,
                WatchedPathChangeKind.FileRenamed,
                Path.Combine(context.Root, "new.txt"),
                Path.Combine(context.Root, "old.txt"),
                detected));
        await WaitForBatchCountAsync(context, 1);

        var batch = Assert.Single(context.Processor.Batches);
        Assert.Equal(WatchedScanReason.OpenSorSeExecution, batch.Reason);
        Assert.True(batch.SuppressSuggestions);
    }

    [Fact]
    public async Task PauseResume_DisposesWatcherAndQueuesResumeReconciliation()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        context.Processor.Batches.Clear();
        var firstSource = Assert.Single(context.SourceFactory.Sources);

        await context.Manager.PauseAsync(context.Configuration.Id, CancellationToken.None);
        await context.Coordinator.RefreshAsync(CancellationToken.None);
        Assert.True(firstSource.IsDisposed);
        Assert.Equal(
            WatchedFolderStatus.Paused,
            Assert.Single(await context.Manager.ListAsync(CancellationToken.None)).Status);

        await context.Manager.ResumeAsync(context.Configuration.Id, CancellationToken.None);
        await context.Coordinator.RefreshAsync(CancellationToken.None);
        await WaitForBatchCountAsync(context, 1);

        Assert.Equal(2, context.SourceFactory.Sources.Count);
        Assert.Equal(WatchedScanReason.ResumeReconciliation, context.Processor.Batches[0].Reason);
    }

    [Fact]
    public async Task MissingThenReconnectedFolder_RetainsConfigurationAndQueuesReconnect()
    {
        await using var context = await CoordinatorContext.CreateAsync(available: false);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        var unavailable = Assert.Single(await context.Manager.ListAsync(CancellationToken.None));
        Assert.Equal(WatchedFolderStatus.Unavailable, unavailable.Status);
        Assert.Empty(context.SourceFactory.Sources);
        Assert.Empty(context.Processor.Batches);

        context.FileSystem.Available = true;
        await context.Coordinator.RefreshAsync(CancellationToken.None);
        await WaitForBatchCountAsync(context, 1);

        Assert.Single(context.SourceFactory.Sources);
        Assert.Equal(
            WatchedScanReason.ResumeReconciliation,
            context.Processor.Batches[0].Reason);
        Assert.Single(await context.Manager.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UserCommands_UsePreciseIncrementalFullAndAiRetryReasons()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await WaitForInitialReconciliationAsync(context);
        context.Processor.Batches.Clear();

        await context.Coordinator.ScanChangesNowAsync(context.Configuration.Id, CancellationToken.None);
        await context.Coordinator.ReconcileNowAsync(context.Configuration.Id, CancellationToken.None);
        await context.Coordinator.RetryAiAsync(context.Configuration.Id, CancellationToken.None);
        await WaitForBatchCountAsync(context, 3);

        Assert.Equal(
            [
                WatchedScanReason.UserIncrementalScan,
                WatchedScanReason.UserFullReconciliation,
                WatchedScanReason.AiRetry,
            ],
            context.Processor.Batches.Select(batch => batch.Reason));
        Assert.True(context.Processor.Batches[0].RequiresFullReconciliation);
        Assert.True(context.Processor.Batches[1].RequiresFullReconciliation);
        Assert.False(context.Processor.Batches[2].RequiresFullReconciliation);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The watched-folder test did not reach the expected state.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task WaitForBatchCountAsync(CoordinatorContext context, int expectedCount)
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await context.Processor.Batches.WaitForCountAsync(expectedCount, testTimeout.Token);
    }

    private static async Task WaitForInitialReconciliationAsync(CoordinatorContext context)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var configuration = Assert.Single(
                await context.Manager.ListAsync(CancellationToken.None));
            if (context.Processor.Batches.Count >= 1 &&
                configuration.Status == WatchedFolderStatus.Watching &&
                configuration.LastReconciliationUtc is not null &&
                configuration.LastSuccessfulScanUtc is not null)
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The initial watched-folder reconciliation did not complete.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task WaitForWatchingAsync(CoordinatorContext context)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Assert.Single(await context.Manager.ListAsync(CancellationToken.None)).Status !=
               WatchedFolderStatus.Watching)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The watched-folder batch did not return to the watching state.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class CoordinatorContext : IAsyncDisposable
    {
        private readonly LoggingService _logging;

        private CoordinatorContext(
            string workspace,
            string root,
            WatchedFolderConfiguration configuration,
            WatchedFolderManager manager,
            RecordingSourceFactory sourceFactory,
            RecordingProcessor processor,
            JsonWatchedActivityStore activityStore,
            AvailabilityFileSystem fileSystem,
            WatchedFolderCoordinator coordinator,
            LoggingService logging)
        {
            Workspace = workspace;
            Root = root;
            Configuration = configuration;
            Manager = manager;
            SourceFactory = sourceFactory;
            Processor = processor;
            ActivityStore = activityStore;
            FileSystem = fileSystem;
            Coordinator = coordinator;
            _logging = logging;
        }

        public string Workspace { get; }
        public string Root { get; }
        public WatchedFolderConfiguration Configuration { get; }
        public WatchedFolderManager Manager { get; }
        public RecordingSourceFactory SourceFactory { get; }
        public RecordingProcessor Processor { get; }
        public JsonWatchedActivityStore ActivityStore { get; }
        public AvailabilityFileSystem FileSystem { get; }
        public WatchedFolderCoordinator Coordinator { get; }

        public static async Task<CoordinatorContext> CreateAsync(
            bool available = true,
            bool correlationResult = false)
        {
            var workspace = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                $"opensorse-watched-coordinator-{Guid.NewGuid():N}"));
            var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
            var logging = new LoggingService();
            var manager = new WatchedFolderManager(
                new JsonWatchedFolderConfigurationStore(
                    Path.Combine(workspace, "data", "watched.json"),
                    logging),
                new WatchedFolderPathPolicy());
            var configuration = await manager.AddAsync(
                new WatchedFolderCreateRequest(
                    root,
                    "Root",
                    QuietPeriod: TimeSpan.FromMilliseconds(250),
                    AiAnalysisEnabled: true),
                CancellationToken.None);
            var sourceFactory = new RecordingSourceFactory();
            var processor = new RecordingProcessor();
            var activity = new JsonWatchedActivityStore(
                Path.Combine(workspace, "data", "activity.json"),
                logging);
            var fileSystem = new AvailabilityFileSystem { Available = available };
            var coordinator = new WatchedFolderCoordinator(
                manager,
                sourceFactory,
                processor,
                activity,
                new FixedCorrelation(correlationResult),
                fileSystem,
                logging);
            return new CoordinatorContext(
                workspace,
                root,
                configuration,
                manager,
                sourceFactory,
                processor,
                activity,
                fileSystem,
                coordinator,
                logging);
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            _logging.Dispose();
            var fullPath = Path.GetFullPath(Workspace);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), fullPath, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }

    private sealed class RecordingSourceFactory : IWatchedFolderEventSourceFactory
    {
        public List<RecordingSource> Sources { get; } = [];
        public IWatchedFolderEventSource Create(WatchedFolderConfiguration configuration)
        {
            var source = new RecordingSource(configuration.Id);
            Sources.Add(source);
            return source;
        }
    }

    private sealed class RecordingSource(string configurationId) : IWatchedFolderEventSource
    {
        public event EventHandler<WatchedFolderHint>? HintReceived;
        public event EventHandler<Exception>? Error;
        public string ConfigurationId { get; } = configurationId;
        public bool IsStarted { get; private set; }
        public bool IsDisposed { get; private set; }
        public void Start() => IsStarted = true;
        public void Stop() => IsStarted = false;
        public void Raise(WatchedFolderHint hint) => HintReceived?.Invoke(this, hint);
        public void RaiseError(Exception error) => Error?.Invoke(this, error);
        public void Dispose()
        {
            IsStarted = false;
            IsDisposed = true;
        }
    }

    private sealed class RecordingProcessor : IWatchedFolderProcessor
    {
        public RecordingBatchCollection Batches { get; } = new();
        public Task<WatchedFolderProcessResult> ProcessAsync(
            WatchedFolderConfiguration configuration,
            WatchedChangeBatch batch,
            CancellationToken cancellationToken)
        {
            Batches.Add(batch);

            var now = DateTimeOffset.UtcNow;
            var catalogue = new WatchedFolderCatalogue(
                WatchedFolderLimits.CurrentCatalogueSchemaVersion,
                configuration.CatalogueId,
                configuration.Id,
                configuration.FolderPath,
                now,
                [],
                [],
                batch.RequiresFullReconciliation ? now : configuration.LastReconciliationUtc,
                false);
            return Task.FromResult(new WatchedFolderProcessResult(
                configuration.Id,
                batch.BatchId,
                batch.Reason,
                new WatchedChangeSummary(0, 0, 0, 0, 0, 0, 0),
                catalogue,
                [],
                batch.Reason == WatchedScanReason.AiRetry,
                false,
                []));
        }
    }

    private sealed class RecordingBatchCollection : IReadOnlyList<WatchedChangeBatch>
    {
        private readonly object _sync = new();
        private readonly List<WatchedChangeBatch> _items = [];
        private TaskCompletionSource _changed = NewSignal();

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _items.Count;
                }
            }
        }

        public WatchedChangeBatch this[int index]
        {
            get
            {
                lock (_sync)
                {
                    return _items[index];
                }
            }
        }

        public void Add(WatchedChangeBatch batch)
        {
            TaskCompletionSource changed;
            lock (_sync)
            {
                _items.Add(batch);
                changed = _changed;
                _changed = NewSignal();
            }

            changed.TrySetResult();
        }

        public void Clear()
        {
            lock (_sync)
            {
                _items.Clear();
            }
        }

        public async Task WaitForCountAsync(int expectedCount, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task changed;
                lock (_sync)
                {
                    if (_items.Count >= expectedCount)
                    {
                        return;
                    }

                    changed = _changed.Task;
                }

                await changed.WaitAsync(cancellationToken);
            }
        }

        public IEnumerator<WatchedChangeBatch> GetEnumerator()
        {
            lock (_sync)
            {
                return _items.ToArray().AsEnumerable().GetEnumerator();
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class AvailabilityFileSystem : IWatchedFileSystem
    {
        public bool Available { get; set; }
        public bool DirectoryExists(string path) => Available;
        public Task<IReadOnlyList<WatchedFileProbe>> EnumerateAsync(
            WatchedFolderConfiguration configuration,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WatchedFileProbe>>([]);
        public Task<WatchedFileProbe?> ProbeAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<WatchedFileProbe?>(null);
    }

    private sealed class FixedCorrelation(bool value) : IWatchedExecutionCorrelation
    {
        public Task<bool> IsOpenSorSeGeneratedAsync(
            WatchedFolderConfiguration configuration,
            WatchedFolderHint hint,
            CancellationToken cancellationToken) =>
            Task.FromResult(value);
    }
}
