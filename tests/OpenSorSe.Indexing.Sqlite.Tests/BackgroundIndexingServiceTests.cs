using System.Runtime.CompilerServices;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;
using OpenSorSe.Application.Watching;
using OpenSorSe.Indexing.Sqlite;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Validates the live durable coordinator with deterministic discovery and stage workers.</summary>
public sealed class BackgroundIndexingServiceTests
{
    /// <summary>Verifies a newly discovered Basic file completes every applicable durable stage.</summary>
    [Fact]
    public async Task NewFileCompletesDurableBasicPipeline()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        var completion = WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Complete && snapshot.Completed == 1);

        await fixture.Service.QueueFolderAsync(fixture.Root, IndexingLevel.Basic);
        var snapshot = await completion;

        Assert.Equal(100, snapshot.OverallPercentage);
        Assert.Equal(0, snapshot.Failed);
        Assert.Equal(
            [
                IndexingStage.FileDiscovered,
                IndexingStage.MetadataIndexed,
                IndexingStage.ContentFingerprinted,
                IndexingStage.SearchIndexUpdated,
                IndexingStage.RelationshipAnalysisCompleted,
                IndexingStage.FileFullyIndexed,
            ],
            fixture.Processor.ProcessedStages);
    }

    /// <summary>Verifies empty sources complete without a divide-by-zero or indeterminate stale run.</summary>
    [Fact]
    public async Task EmptyFolderCompletesWithZeroFiles()
    {
        await using var fixture = await ServiceFixture.CreateAsync(discovery: new FakeDiscovery([]));
        var completion = WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Complete && snapshot.RunId is not null);

        await fixture.Service.QueueFolderAsync(fixture.Root);
        var snapshot = await completion;

        Assert.Equal(0, snapshot.TotalDiscovered);
        Assert.Equal(100, snapshot.OverallPercentage);
        Assert.Empty(await fixture.Service.GetDocumentsAsync(10));
    }

    /// <summary>Verifies queuing returns while slow discovery remains off the caller thread.</summary>
    [Fact]
    public async Task QueueFolderDoesNotWaitForSlowDiscovery()
    {
        var discovery = new FakeDiscovery();
        discovery.Block();
        await using var fixture = await ServiceFixture.CreateAsync(discovery: discovery);

        var runId = await fixture.Service
            .QueueFolderAsync(fixture.Root)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotEmpty(runId);
        Assert.False(discovery.Completed);
        discovery.Release();
        _ = await WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Complete && snapshot.RunId == runId);
    }

    /// <summary>Verifies safe cancellation cooperatively stops an active source traversal.</summary>
    [Fact]
    public async Task CancellationStopsActiveDiscoveryBeforeFinalizingRun()
    {
        var discovery = new FakeDiscovery();
        discovery.Block();
        await using var fixture = await ServiceFixture.CreateAsync(discovery: discovery);
        await fixture.Service.QueueFolderAsync(fixture.Root);
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await fixture.Service.CancelAsync("cancel discovery").WaitAsync(TimeSpan.FromSeconds(2));

        var snapshot = await fixture.Service.GetProgressAsync();
        Assert.Equal(IndexingRunStatus.Cancelled, snapshot.Status);
        Assert.False(discovery.Completed);
        Assert.Equal(0, await fixture.Store.RecoverInterruptedWorkAsync(DateTimeOffset.UtcNow));
    }

    /// <summary>Verifies pause permits the active stage to finish but prevents a later claim.</summary>
    [Fact]
    public async Task PauseAndResumeAreCooperativeAndPersistent()
    {
        var processor = new FakeStageProcessor(IndexingStage.MetadataIndexed);
        await using var fixture = await ServiceFixture.CreateAsync(processor: processor);
        await fixture.Service.QueueFolderAsync(fixture.Root);
        await processor.WaitUntilBlockedAsync();

        await fixture.Service.PauseAsync();
        processor.Release();
        var paused = await WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Paused);
        Assert.True(paused.Remaining > 0);

        var completion = WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Complete);
        await fixture.Service.ResumeAsync();
        Assert.Equal(1, (await completion).Completed);
    }

    /// <summary>Verifies cancellation is prompt and leaves no stale running stage at each durable boundary.</summary>
    [Theory]
    [InlineData(IndexingStage.FileDiscovered)]
    [InlineData(IndexingStage.MetadataIndexed)]
    [InlineData(IndexingStage.ContentFingerprinted)]
    [InlineData(IndexingStage.TextExtracted)]
    [InlineData(IndexingStage.OcrProcessed)]
    [InlineData(IndexingStage.SummaryKeywordsGenerated)]
    [InlineData(IndexingStage.SemanticRepresentationGenerated)]
    [InlineData(IndexingStage.SearchIndexUpdated)]
    [InlineData(IndexingStage.RelationshipAnalysisCompleted)]
    [InlineData(IndexingStage.FileFullyIndexed)]
    public async Task CancellationAtEveryStageLeavesNoRunningWork(IndexingStage stage)
    {
        var processor = new FakeStageProcessor(stage);
        await using var fixture = await ServiceFixture.CreateAsync(
            processor: processor,
            settings: DeepSettings(IndexingLevel.Deep, ocr: true));
        await fixture.Service.QueueFolderAsync(fixture.Root, IndexingLevel.Deep);
        await processor.WaitUntilBlockedAsync();

        await fixture.Service.CancelAsync("test cancellation");
        var snapshot = await fixture.Service.GetProgressAsync();

        Assert.Equal(IndexingRunStatus.Cancelled, snapshot.Status);
        Assert.Equal(1, snapshot.Processed);
        Assert.Equal(0, await fixture.Store.RecoverInterruptedWorkAsync(DateTimeOffset.UtcNow));
    }

    /// <summary>Verifies application shutdown preserves a running stage for recovery instead of marking it complete.</summary>
    [Fact]
    public async Task ShutdownDuringProcessingRecoversAndResumesWithoutRepeatingDiscovery()
    {
        var processor = new FakeStageProcessor(IndexingStage.MetadataIndexed);
        var root = ServiceFixture.CreateRoot();
        var databasePath = Path.Combine(root, "index", "deep-index.db");
        try
        {
            await using (var first = await ServiceFixture.CreateAsync(
                             root,
                             databasePath,
                             processor: processor))
            {
                await first.Service.QueueFolderAsync(root);
                await processor.WaitUntilBlockedAsync();
            }

            var resumedProcessor = new FakeStageProcessor();
            await using var resumed = await ServiceFixture.CreateAsync(
                root,
                databasePath,
                processor: resumedProcessor);
            var completion = await WaitForProgressAsync(
                resumed.Service,
                snapshot => snapshot.Status == IndexingRunStatus.Complete && snapshot.Completed == 1);

            Assert.Equal(1, completion.Completed);
            Assert.DoesNotContain(IndexingStage.FileDiscovered, resumedProcessor.ProcessedStages);
            Assert.Equal(0, await resumed.Store.RecoverInterruptedWorkAsync(DateTimeOffset.UtcNow));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Verifies an explicit pause survives restart and incomplete stage work resumes only on request.</summary>
    [Fact]
    public async Task PausedRunSurvivesRestartWithoutReplacementDiscovery()
    {
        var processor = new FakeStageProcessor(IndexingStage.MetadataIndexed);
        var root = ServiceFixture.CreateRoot();
        var databasePath = Path.Combine(root, "index", "deep-index.db");
        try
        {
            await using (var first = await ServiceFixture.CreateAsync(
                             root,
                             databasePath,
                             processor: processor))
            {
                await first.Service.QueueFolderAsync(root);
                await processor.WaitUntilBlockedAsync();
                await first.Service.PauseAsync();
                processor.Release();
                _ = await WaitForProgressAsync(
                    first.Service,
                    snapshot => snapshot.Status == IndexingRunStatus.Paused);
            }

            var discovery = new FakeDiscovery();
            var resumedProcessor = new FakeStageProcessor();
            await using var resumed = await ServiceFixture.CreateAsync(
                root,
                databasePath,
                discovery,
                resumedProcessor);

            Assert.Equal(IndexingRunStatus.Paused, (await resumed.Service.GetProgressAsync()).Status);
            Assert.False(discovery.Started.Task.IsCompleted);
            var completion = WaitForProgressAsync(
                resumed.Service,
                snapshot => snapshot.Status == IndexingRunStatus.Complete);
            await resumed.Service.ResumeAsync();

            Assert.Equal(1, (await completion).Completed);
            Assert.DoesNotContain(IndexingStage.FileDiscovered, resumedProcessor.ProcessedStages);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Verifies a power-loss-equivalent interruption during discovery restarts the same durable run.</summary>
    [Fact]
    public async Task InterruptedDiscoveryResumesSameRunAfterRestart()
    {
        var blockedDiscovery = new FakeDiscovery();
        blockedDiscovery.Block();
        var root = ServiceFixture.CreateRoot();
        var databasePath = Path.Combine(root, "index", "deep-index.db");
        string runId;
        try
        {
            await using (var first = await ServiceFixture.CreateAsync(
                             root,
                             databasePath,
                             discovery: blockedDiscovery))
            {
                runId = await first.Service.QueueFolderAsync(root);
                await blockedDiscovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }

            var resumedDiscovery = new FakeDiscovery();
            await using var resumed = await ServiceFixture.CreateAsync(
                root,
                databasePath,
                discovery: resumedDiscovery);
            var completion = await WaitForProgressAsync(
                resumed.Service,
                snapshot => snapshot.Status == IndexingRunStatus.Complete);

            Assert.Equal(runId, completion.RunId);
            Assert.True(resumedDiscovery.Completed);
            Assert.Equal(1, completion.Completed);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Verifies a user-cancelled run remains cancelled across restart until retry is explicit.</summary>
    [Fact]
    public async Task CancelledRunRemainsCancelledAcrossRestartUntilExplicitRetry()
    {
        var blockedDiscovery = new FakeDiscovery();
        blockedDiscovery.Block();
        var root = ServiceFixture.CreateRoot();
        var databasePath = Path.Combine(root, "index", "deep-index.db");
        string cancelledRunId;
        try
        {
            await using (var first = await ServiceFixture.CreateAsync(
                             root,
                             databasePath,
                             discovery: blockedDiscovery))
            {
                cancelledRunId = await first.Service.QueueFolderAsync(root);
                await blockedDiscovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await first.Service.CancelAsync("explicit test cancellation");
            }

            var resumedDiscovery = new FakeDiscovery([]);
            await using var resumed = await ServiceFixture.CreateAsync(
                root,
                databasePath,
                discovery: resumedDiscovery);

            var cancelled = await resumed.Service.GetProgressAsync();
            Assert.Equal(cancelledRunId, cancelled.RunId);
            Assert.Equal(IndexingRunStatus.Cancelled, cancelled.Status);
            Assert.False(resumedDiscovery.Started.Task.IsCompleted);

            Assert.Equal(1, await resumed.Service.RetryFailedAsync());
            var complete = await WaitForProgressAsync(
                resumed.Service,
                snapshot => snapshot.Status == IndexingRunStatus.Complete);
            Assert.NotEqual(cancelledRunId, complete.RunId);
            Assert.True(resumedDiscovery.Completed);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Verifies resource-waiting work resumes automatically once the portable policy allows it.</summary>
    [Fact]
    public async Task ResourceWaitingAutomaticallyResumesWhenEligible()
    {
        var monitor = new ControllableResourceMonitor();
        await using var fixture = await ServiceFixture.CreateAsync(resourceMonitor: monitor);
        var waiting = WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Waiting);
        await fixture.Service.QueueFolderAsync(fixture.Root);
        _ = await waiting;

        var completion = WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Complete);
        monitor.MayProcess = true;
        await fixture.Service.PrioritizeSourceAsync(
            Assert.Single(await fixture.Service.GetSourcesAsync()).Id);

        Assert.Equal(1, (await completion).Completed);
    }

    /// <summary>Verifies optional OCR and local-AI dependencies wait durably and recover when available.</summary>
    [Theory]
    [InlineData(IndexingStage.OcrProcessed, true, false, "OCR")]
    [InlineData(IndexingStage.SummaryKeywordsGenerated, false, true, "local AI")]
    public async Task DependencyUnavailableThenAvailableResumes(
        IndexingStage dependencyStage,
        bool ocr,
        bool ai,
        string dependency)
    {
        var processor = new FakeStageProcessor(dependencyStage, dependency);
        await using var fixture = await ServiceFixture.CreateAsync(
            processor: processor,
            settings: DeepSettings(IndexingLevel.Deep, ocr, ai));
        await fixture.Service.QueueFolderAsync(fixture.Root, IndexingLevel.Deep);
        var waiting = await WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Waiting == 1);
        Assert.Equal(1, waiting.Waiting);

        processor.DependencyAvailable = true;
        var completion = WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Complete && snapshot.Completed == 1);
        Assert.Equal(1, await fixture.Service.RetryFailedAsync());

        Assert.Equal(1, (await completion).Completed);
        Assert.True(processor.AttemptsAt(dependencyStage) >= 2);
    }

    /// <summary>Verifies a retryable locked-file outcome is exposed and can be retried.</summary>
    [Fact]
    public async Task RetryableFileFailureRetriesWithoutLosingCompletedStages()
    {
        var processor = new FakeStageProcessor(
            IndexingStage.ContentFingerprinted,
            retryCategory: IndexingFailureCategory.FileLocked);
        await using var fixture = await ServiceFixture.CreateAsync(processor: processor);
        await fixture.Service.QueueFolderAsync(fixture.Root);
        var retrying = await WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.RetryScheduled == 1);
        Assert.Equal(1, retrying.RetryScheduled);

        processor.DependencyAvailable = true;
        var completion = WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Complete);
        await fixture.Service.RetryFailedAsync();

        Assert.Equal(1, (await completion).Completed);
        Assert.Equal(1, processor.AttemptsAt(IndexingStage.FileDiscovered));
        Assert.Equal(1, processor.AttemptsAt(IndexingStage.MetadataIndexed));
    }

    /// <summary>Verifies inaccessible discovery fails the run without deleting or mutating source data.</summary>
    [Fact]
    public async Task InaccessibleFolderFailureIsIsolatedAndActionable()
    {
        await using var fixture = await ServiceFixture.CreateAsync(
            discovery: new FakeDiscovery(new IOException("denied")));
        var failed = WaitForProgressAsync(
            fixture.Service,
            snapshot => snapshot.Status == IndexingRunStatus.Failed);

        await fixture.Service.QueueFolderAsync(fixture.Root);
        var snapshot = await failed;

        Assert.Equal(0, snapshot.Completed);
        Assert.True(Directory.Exists(fixture.Root));
    }

    /// <summary>Verifies source prioritization, removal, and maintenance remain provider neutral.</summary>
    [Fact]
    public async Task SourceControlsDoNotChangeSourceFiles()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        await fixture.Service.QueueFolderAsync(fixture.Root);
        _ = await WaitForProgressAsync(fixture.Service, snapshot => snapshot.Status == IndexingRunStatus.Complete);
        var source = Assert.Single(await fixture.Service.GetSourcesAsync());

        await fixture.Service.PrioritizeSourceAsync(source.Id);
        Assert.Equal(1000, Assert.Single(await fixture.Service.GetSourcesAsync()).Priority);
        await fixture.Service.RemoveSourceAsync(source.Id);
        var maintenance = await fixture.Service.MaintainAsync();

        Assert.Empty(await fixture.Service.GetSourcesAsync());
        Assert.True(Directory.Exists(fixture.Root));
        Assert.True(maintenance.IsWithinQuota);
    }

    /// <summary>Verifies disabled watched folders remove only their owned source and never an explicitly queued source.</summary>
    [Fact]
    public async Task WatchedSourceSynchronizationRemovesOnlyDisabledManagedSources()
    {
        var root = ServiceFixture.CreateRoot();
        var databasePath = Path.Combine(root, "index", "deep-index.db");
        var watchedRoot = Path.Combine(root, "watched");
        var manualRoot = Path.Combine(root, "manual");
        Directory.CreateDirectory(watchedRoot);
        Directory.CreateDirectory(manualRoot);
        try
        {
            await using (var seed = new SqliteDeepIndexStore(databasePath, PlatformServices.CurrentPathSemantics))
            {
                await seed.InitializeAsync();
                await seed.UpsertSourceAsync(new IndexingSource(
                    "watched-source",
                    watchedRoot,
                    "Watched",
                    IndexingLevel.Basic,
                    true,
                    true,
                    1000,
                    [],
                    ManagedByWatchedFolders: true));
                await seed.UpsertSourceAsync(new IndexingSource(
                    "manual-source",
                    manualRoot,
                    "Manual",
                    IndexingLevel.Basic,
                    true,
                    true,
                    1000,
                    []));
            }

            await using var store = new SqliteDeepIndexStore(databasePath, PlatformServices.CurrentPathSemantics);
            await using var service = new BackgroundIndexingService(
                new FakeConfiguration(DeepSettings(IndexingLevel.Basic)),
                store,
                new FakeDiscovery([]),
                new FakeStageProcessor(),
                new PortableBackgroundResourceMonitor(),
                PlatformServices.CurrentPathSemantics,
                new LoggingService(),
                watchedFolderManager: new FakeWatchedFolderManager([]));

            await service.InitializeAsync();

            var remaining = Assert.Single(await service.GetSourcesAsync());
            Assert.Equal("manual-source", remaining.Id);
            Assert.False(remaining.ManagedByWatchedFolders);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Verifies an enabled but temporarily inaccessible watched folder retains durable source state.</summary>
    [Fact]
    public async Task InaccessibleEnabledWatchedFolderRemainsRegisteredForRecovery()
    {
        var root = ServiceFixture.CreateRoot();
        var databasePath = Path.Combine(root, "index", "deep-index.db");
        var offlineRoot = Path.Combine(root, "offline");
        try
        {
            var watched = new WatchedFolderConfiguration(
                "watched",
                offlineRoot,
                "Offline documents",
                IsEnabled: true,
                IncludeSubfolders: true,
                IgnoredPaths: [],
                IgnorePatterns: [],
                ScanProfileId: "default",
                SortingRecipeId: null,
                DeterministicAnalysisEnabled: true,
                AiAnalysisEnabled: false,
                Notifications: new WatchedFolderNotificationPreferences(),
                QuietPeriod: TimeSpan.Zero,
                LastSuccessfulScanUtc: null,
                LastDetectedChangeUtc: null,
                Status: WatchedFolderStatus.Unavailable,
                CatalogueId: "catalog");
            await using var store = new SqliteDeepIndexStore(databasePath, PlatformServices.CurrentPathSemantics);
            await using var service = new BackgroundIndexingService(
                new FakeConfiguration(DeepSettings(IndexingLevel.Basic)),
                store,
                new FakeDiscovery([]),
                new FakeStageProcessor(),
                new PortableBackgroundResourceMonitor(),
                PlatformServices.CurrentPathSemantics,
                new LoggingService(),
                watchedFolderManager: new FakeWatchedFolderManager([watched]));

            await service.InitializeAsync();

            var source = Assert.Single(await service.GetSourcesAsync());
            Assert.Equal(offlineRoot, source.RootPath);
            Assert.True(source.ManagedByWatchedFolders);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Verifies a disabled subsystem rejects new work without creating a stranded run or source.</summary>
    [Fact]
    public async Task QueueFolderWhenDisabledIsRejectedWithoutPersistingSource()
    {
        await using var fixture = await ServiceFixture.CreateAsync(settings: new DeepIndexingSettings
        {
            Enabled = false,
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.QueueFolderAsync(fixture.Root));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Service.GetSourcesAsync());
    }

    /// <summary>Verifies corrupt derived storage degrades safely and explicit rebuild retains a recovery copy.</summary>
    [Fact]
    public async Task CorruptStorageDegradesWithoutBlockingSearchAndCanBeExplicitlyRebuilt()
    {
        var root = ServiceFixture.CreateRoot();
        var databasePath = Path.Combine(root, "index", "deep-index.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        File.WriteAllBytes(databasePath, "corrupt index"u8.ToArray());
        try
        {
            await using var store = new SqliteDeepIndexStore(databasePath, PlatformServices.CurrentPathSemantics);
            await using var service = new BackgroundIndexingService(
                new FakeConfiguration(DeepSettings(IndexingLevel.Basic)),
                store,
                new FakeDiscovery([]),
                new FakeStageProcessor(),
                new PortableBackgroundResourceMonitor(),
                PlatformServices.CurrentPathSemantics,
                new LoggingService());

            await service.InitializeAsync();

            Assert.Equal(IndexingRunStatus.Failed, (await service.GetProgressAsync()).Status);
            Assert.Empty(await service.GetDocumentsAsync(10));
            Assert.Equal(
                IndexingFailureCategory.StorageCorruption,
                Assert.Single(await service.GetFailuresAsync()).Category);

            await service.RebuildAsync();

            Assert.Equal(IndexingRunStatus.Complete, (await service.GetProgressAsync()).Status);
            var recovery = Assert.Single(Directory.EnumerateFiles(
                Path.Combine(root, "index", "backups"),
                "deep-index-recovery-*.db"));
            Assert.Equal("corrupt index", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(recovery)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static DeepIndexingSettings DeepSettings(
        IndexingLevel level,
        bool ocr = false,
        bool ai = false) => new()
        {
            Enabled = true,
            DefaultLevel = level,
            OcrProcessingEnabled = ocr,
            AiProcessingEnabled = ai,
            MaximumRetryCount = 3,
        };

    private static async Task<IndexingProgressSnapshot> WaitForProgressAsync(
        IBackgroundIndexingService service,
        Func<IndexingProgressSnapshot, bool> predicate)
    {
        var current = await service.GetProgressAsync();
        if (predicate(current))
        {
            return current;
        }

        var completion = new TaskCompletionSource<IndexingProgressSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, IndexingProgressSnapshot snapshot)
        {
            if (predicate(snapshot))
            {
                completion.TrySetResult(snapshot);
            }
        }

        service.ProgressChanged += Handler;
        try
        {
            current = await service.GetProgressAsync();
            if (predicate(current))
            {
                return current;
            }

            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            service.ProgressChanged -= Handler;
        }
    }

    private sealed class FakeDiscovery : IIndexFileDiscovery
    {
        private readonly Exception? _failure;
        private readonly IReadOnlyList<IndexingFileObservation>? _observations;
        private TaskCompletionSource? _release;

        public FakeDiscovery(IReadOnlyList<IndexingFileObservation>? observations = null)
        {
            _observations = observations;
        }

        public FakeDiscovery(Exception failure)
        {
            _failure = failure;
        }

        public bool Completed { get; private set; }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Block() =>
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release?.TrySetResult();

        public async IAsyncEnumerable<IndexingFileObservation> DiscoverAsync(
            IndexingSource source,
            DeepIndexingSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (_release is not null)
            {
                await _release.Task.WaitAsync(cancellationToken);
            }

            if (_failure is not null)
            {
                throw _failure;
            }

            var values = _observations ??
                [
                    new IndexingFileObservation(
                        Path.Combine(source.RootPath, "document.txt"),
                        "document.txt",
                        "stable-document",
                        "test-volume",
                        100,
                        DateTimeOffset.UnixEpoch,
                        DateTimeOffset.UnixEpoch,
                        FileAttributes.Normal,
                        "metadata"),
                ];
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return value with
                {
                    FullPath = Path.Combine(source.RootPath, value.RelativePath),
                };
            }

            Completed = true;
        }
    }

    private sealed class FakeStageProcessor : IIndexingStageProcessor, IIndexingProcessorFingerprint
    {
        private readonly IndexingStage? _blockedStage;
        private readonly string? _dependency;
        private readonly IndexingFailureCategory? _retryCategory;
        private readonly TaskCompletionSource _blocked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IndexingStage> _processedStages = [];
        private readonly Dictionary<IndexingStage, int> _attempts = [];

        public FakeStageProcessor(
            IndexingStage? blockedStage = null,
            string? dependency = null,
            IndexingFailureCategory? retryCategory = null)
        {
            _blockedStage = blockedStage;
            _dependency = dependency;
            _retryCategory = retryCategory;
        }

        public bool DependencyAvailable { get; set; }

        public IReadOnlyList<IndexingStage> ProcessedStages
        {
            get
            {
                lock (_processedStages)
                {
                    return _processedStages.ToArray();
                }
            }
        }

        public string CreateProcessorFingerprint(DeepIndexingSettings settings) => "fake-processor";

        public int AttemptsAt(IndexingStage stage)
        {
            lock (_attempts)
            {
                return _attempts.GetValueOrDefault(stage);
            }
        }

        public async Task<IndexingStageOutput> ProcessAsync(
            IndexingWorkItem workItem,
            DeepIndexingSettings settings,
            CancellationToken cancellationToken = default)
        {
            lock (_processedStages)
            {
                _processedStages.Add(workItem.Stage);
            }

            lock (_attempts)
            {
                _attempts[workItem.Stage] = _attempts.GetValueOrDefault(workItem.Stage) + 1;
            }

            if (workItem.Stage == _blockedStage && _dependency is null && _retryCategory is null)
            {
                _blocked.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            if (workItem.Stage == _blockedStage && !DependencyAvailable && _dependency is not null)
            {
                return new IndexingStageOutput
                {
                    Status = IndexingStageStatus.WaitingForDependency,
                    WaitingDependency = _dependency,
                    FailureCategory = IndexingFailureCategory.DependencyUnavailable,
                    ErrorCode = "dependency-unavailable",
                    IsRetryable = true,
                };
            }

            if (workItem.Stage == _blockedStage && !DependencyAvailable && _retryCategory is { } retryCategory)
            {
                return new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Failed,
                    FailureCategory = retryCategory,
                    ErrorCode = "retryable-file-failure",
                    IsRetryable = true,
                };
            }

            return new IndexingStageOutput
            {
                Status = IndexingStageStatus.Complete,
                ContentHash = workItem.Stage == IndexingStage.ContentFingerprinted ? "content-hash" : null,
                ExtractedText = workItem.Stage == IndexingStage.TextExtracted ? "document text" : null,
                OcrText = workItem.Stage == IndexingStage.OcrProcessed ? "ocr text" : null,
                Keywords = workItem.Stage == IndexingStage.SummaryKeywordsGenerated ? ["document"] : null,
                SemanticRepresentation = workItem.Stage == IndexingStage.SemanticRepresentationGenerated
                    ? [0.5f, 0.5f]
                    : null,
            };
        }

        public Task WaitUntilBlockedAsync() => _blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release() => _release.TrySetResult();
    }

    private sealed class FakeConfiguration : IConfigurationService
    {
        public FakeConfiguration(DeepIndexingSettings settings)
        {
            Current = new ApplicationSettings
            {
                DeepIndexing = settings,
            };
        }

        public ApplicationSettings Current { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class ControllableResourceMonitor : IBackgroundResourceMonitor
    {
        public bool MayProcess { get; set; }

        public Task<BackgroundResourceEligibility> GetEligibilityAsync(
            DeepIndexingSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BackgroundResourceEligibility(
                MayProcess,
                MayProcess ? null : "Waiting for test resources."));
    }

    private sealed class FakeWatchedFolderManager(
        IReadOnlyList<WatchedFolderConfiguration> configurations) : IWatchedFolderManager
    {
        public event EventHandler? ConfigurationsChanged;

        public Task<IReadOnlyList<WatchedFolderConfiguration>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(configurations);

        public Task<WatchedFolderConfiguration> AddAsync(
            WatchedFolderCreateRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WatchedFolderConfiguration> UpdateAsync(
            string id,
            WatchedFolderUpdateRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WatchedFolderConfiguration> PauseAsync(
            string id,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WatchedFolderConfiguration> ResumeAsync(
            string id,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WatchedFolderConfiguration> SetRuntimeStateAsync(
            string id,
            Func<WatchedFolderConfiguration, WatchedFolderConfiguration> update,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void RaiseConfigurationsChanged() => ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ServiceFixture : IAsyncDisposable
    {
        private readonly bool _ownsRoot;

        private ServiceFixture(
            string root,
            string databasePath,
            BackgroundIndexingService service,
            SqliteDeepIndexStore store,
            FakeStageProcessor processor,
            bool ownsRoot)
        {
            Root = root;
            DatabasePath = databasePath;
            Service = service;
            Store = store;
            Processor = processor;
            _ownsRoot = ownsRoot;
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public BackgroundIndexingService Service { get; }

        public SqliteDeepIndexStore Store { get; }

        public FakeStageProcessor Processor { get; }

        public static string CreateRoot()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "OpenSorSe-background-index-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        public static Task<ServiceFixture> CreateAsync(
            IIndexFileDiscovery? discovery = null,
            FakeStageProcessor? processor = null,
            DeepIndexingSettings? settings = null,
            IBackgroundResourceMonitor? resourceMonitor = null)
        {
            var root = CreateRoot();
            return CreateAsync(
                root,
                Path.Combine(root, "index", "deep-index.db"),
                discovery,
                processor,
                settings,
                resourceMonitor,
                ownsRoot: true);
        }

        public static Task<ServiceFixture> CreateAsync(
            string root,
            string databasePath,
            IIndexFileDiscovery? discovery = null,
            FakeStageProcessor? processor = null,
            DeepIndexingSettings? settings = null,
            IBackgroundResourceMonitor? resourceMonitor = null) =>
            CreateAsync(root, databasePath, discovery, processor, settings, resourceMonitor, ownsRoot: false);

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (_ownsRoot && Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static async Task<ServiceFixture> CreateAsync(
            string root,
            string databasePath,
            IIndexFileDiscovery? discovery,
            FakeStageProcessor? processor,
            DeepIndexingSettings? settings,
            IBackgroundResourceMonitor? resourceMonitor,
            bool ownsRoot)
        {
            Directory.CreateDirectory(root);
            var store = new SqliteDeepIndexStore(databasePath, PlatformServices.CurrentPathSemantics);
            var actualProcessor = processor ?? new FakeStageProcessor();
            var service = new BackgroundIndexingService(
                new FakeConfiguration(settings ?? DeepSettings(IndexingLevel.Basic)),
                store,
                discovery ?? new FakeDiscovery(),
                actualProcessor,
                resourceMonitor ?? new PortableBackgroundResourceMonitor(),
                PlatformServices.CurrentPathSemantics,
                new LoggingService());
            await service.InitializeAsync();
            return new ServiceFixture(root, databasePath, service, store, actualProcessor, ownsRoot);
        }
    }
}
