using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Indexing.Sqlite;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Validates the embedded provider's durability, migration, incremental, and recovery behavior.</summary>
public sealed class SqliteDeepIndexStoreTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    /// <summary>Verifies fresh schema initialization and integrity.</summary>
    [Fact]
    public async Task InitializeCreatesVersionedIntegrityCheckedDatabase()
    {
        using var fixture = new IndexFixture();
        await using var store = fixture.CreateStore();

        await store.InitializeAsync();

        Assert.True(File.Exists(fixture.DatabasePath));
        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        Assert.Equal("ok", ReadScalar(fixture.DatabasePath, "PRAGMA quick_check;"));
    }

    /// <summary>Verifies a newer schema fails closed without mutation.</summary>
    [Fact]
    public async Task InitializeRejectsUnsupportedNewerSchemaWithoutChangingIt()
    {
        using var fixture = new IndexFixture();
        CreateDatabase(fixture.DatabasePath, "PRAGMA user_version = 99;");
        await using var store = fixture.CreateStore();

        var exception = await Assert.ThrowsAsync<DeepIndexUnsupportedSchemaException>(
            () => store.InitializeAsync());

        Assert.Equal(99, exception.FoundVersion);
        Assert.Equal(DeepIndexingVersion.SchemaVersion, exception.SupportedVersion);
        Assert.Equal(99, ReadUserVersion(fixture.DatabasePath));
    }

    /// <summary>Verifies corrupt storage produces actionable recovery guidance.</summary>
    [Fact]
    public async Task InitializeReportsCorruptDatabaseWithRecoveryGuidance()
    {
        using var fixture = new IndexFixture();
        File.WriteAllBytes(fixture.DatabasePath, "not a sqlite database"u8.ToArray());
        await using var store = fixture.CreateStore();

        var exception = await Assert.ThrowsAsync<DeepIndexCorruptException>(
            () => store.InitializeAsync());

        Assert.Contains("rebuild", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("not a sqlite database", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath)));
    }

    /// <summary>Verifies explicit corrupt-storage recovery preserves evidence and creates a clean usable schema.</summary>
    [Fact]
    public async Task ExplicitStorageResetPreservesRecoveryCopyAndCreatesFreshSchema()
    {
        using var fixture = new IndexFixture();
        var original = "not a sqlite database"u8.ToArray();
        File.WriteAllBytes(fixture.DatabasePath, original);
        await using var store = fixture.CreateStore();
        _ = await Assert.ThrowsAsync<DeepIndexCorruptException>(() => store.InitializeAsync());

        var recoveryPath = Assert.IsType<string>(await store.ResetStorageAsync(Epoch));

        Assert.True(File.Exists(recoveryPath));
        Assert.Equal(original, File.ReadAllBytes(recoveryPath));
        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        Assert.Equal("ok", ReadScalar(fixture.DatabasePath, "PRAGMA quick_check;"));
        Assert.Empty(await store.GetSourcesAsync());
    }

    /// <summary>Verifies recovery copies and their SQLite sidecars obey the bounded backup policy.</summary>
    [Fact]
    public async Task ExplicitStorageResetPrunesOldRecoveryCopiesAndSidecars()
    {
        using var fixture = new IndexFixture();
        var backupDirectory = Path.Combine(fixture.Root, "backups");
        Directory.CreateDirectory(backupDirectory);
        for (var index = 0; index < 4; index++)
        {
            var path = Path.Combine(backupDirectory, $"deep-index-seed-{index}.db");
            File.WriteAllText(path, "seed");
            File.WriteAllText(path + ".wal", "wal");
            File.WriteAllText(path + ".shm", "shm");
            File.SetLastWriteTimeUtc(path, Epoch.UtcDateTime.AddMinutes(index));
        }

        var oldest = Path.Combine(backupDirectory, "deep-index-seed-0.db");
        File.WriteAllBytes(fixture.DatabasePath, "not a sqlite database"u8.ToArray());
        await using var store = fixture.CreateStore();

        _ = await store.ResetStorageAsync(Epoch.AddHours(1));

        Assert.Equal(3, Directory.EnumerateFiles(backupDirectory, "deep-index-*.db").Count());
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(oldest + ".wal"));
        Assert.False(File.Exists(oldest + ".shm"));
    }

    /// <summary>Verifies interrupted version-zero state migrates with a recovery copy.</summary>
    [Fact]
    public async Task InterruptedVersionZeroMigrationKeepsBackupAndCompletesAtomically()
    {
        using var fixture = new IndexFixture();
        CreateDatabase(
            fixture.DatabasePath,
            "CREATE TABLE legacy_partial(id TEXT PRIMARY KEY); INSERT INTO legacy_partial(id) VALUES ('retained'); PRAGMA user_version = 0;");
        await using var store = fixture.CreateStore();

        await store.InitializeAsync();

        Assert.Equal(1, ReadUserVersion(fixture.DatabasePath));
        Assert.Equal("retained", ReadScalar(fixture.DatabasePath, "SELECT id FROM legacy_partial LIMIT 1;"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));
    }

    /// <summary>Verifies bounded consistent manual backups.</summary>
    [Fact]
    public async Task ManualBackupIsConsistentAndRetainsAtMostThreeCopies()
    {
        using var fixture = new IndexFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();

        var backups = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            backups.Add(await store.CreateBackupAsync());
        }

        Assert.True(File.Exists(backups[^1]));
        Assert.Equal(3, Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "*.db").Count());
        Assert.Equal(1, ReadUserVersion(backups[^1]));
    }

    /// <summary>Verifies provider-neutral source persistence.</summary>
    [Fact]
    public async Task SourceRoundTripsWithoutProviderDetailsInContract()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Deep) with
        {
            Priority = 42,
            Exclusions = ["*.tmp", "private/*"],
            ManagedByWatchedFolders = true,
        };

        await store.UpsertSourceAsync(source);
        var actual = Assert.Single(await store.GetSourcesAsync());

        Assert.Equal(source.Id, actual.Id);
        Assert.Equal(source.RootPath, actual.RootPath);
        Assert.Equal(source.DisplayName, actual.DisplayName);
        Assert.Equal(source.Level, actual.Level);
        Assert.Equal(source.IncludeSubfolders, actual.IncludeSubfolders);
        Assert.Equal(source.Enabled, actual.Enabled);
        Assert.Equal(source.Priority, actual.Priority);
        Assert.Equal(source.Exclusions, actual.Exclusions);
        Assert.True(actual.ManagedByWatchedFolders);
    }

    /// <summary>Verifies new files enter the durable discovery stage.</summary>
    [Fact]
    public async Task NewFileStartsAtDurableDiscoveryStage()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        var observation = fixture.Observation("new.txt", stableIdentity: "file-1");

        await QueueAsync(store, source, [observation]);
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(1)));

        Assert.Equal(IndexingStage.FileDiscovered, claim.Stage);
        Assert.Equal(observation.FullPath, claim.FullPath);
        Assert.Equal(1, claim.Attempt);
    }

    /// <summary>Verifies unchanged completed files avoid repeated stage work.</summary>
    [Fact]
    public async Task UnchangedFileIndexedTwiceDoesNotRepeatCompletedStages()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        var observation = fixture.Observation("same.txt", stableIdentity: "file-1");
        await QueueAsync(store, source, [observation]);
        await CompleteBasicRunAsync(store, "hash-a");

        await QueueAsync(store, source, [observation], Epoch.AddHours(1));

        Assert.Null(await store.ClaimNextAsync(Epoch.AddHours(2)));
        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddHours(2));
        Assert.Equal(1, progress.Completed);
        Assert.Equal(IndexingRunStatus.Complete, progress.Status);
    }

    /// <summary>Verifies stable rename and move identity reuse.</summary>
    [Theory]
    [InlineData("renamed.txt")]
    [InlineData("subfolder/moved.txt")]
    public async Task StableRenameOrMoveUpdatesPathWithoutRepeatingContent(string relativePath)
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        await QueueAsync(store, source, [fixture.Observation("original.txt", stableIdentity: "stable")]);
        await CompleteBasicRunAsync(store, "hash-a");

        await QueueAsync(store, source, [fixture.Observation(relativePath, stableIdentity: "stable")], Epoch.AddHours(1));

        Assert.Null(await store.ClaimNextAsync(Epoch.AddHours(2)));
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10));
        Assert.Equal(Path.GetFileName(relativePath), document.FileName);
        Assert.EndsWith(relativePath.Replace('/', Path.DirectorySeparatorChar), document.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies metadata-only changes restart only the affected stages.</summary>
    [Fact]
    public async Task MetadataOnlyChangeRestartsAtMetadataAndRetainsContentHash()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        var initial = fixture.Observation("document.txt", stableIdentity: "stable");
        await QueueAsync(store, source, [initial]);
        await CompleteBasicRunAsync(store, "hash-a");
        var changed = initial with
        {
            LastWriteTimeUtc = initial.LastWriteTimeUtc.AddMinutes(1),
            MetadataFingerprint = "metadata-b",
        };

        await QueueAsync(store, source, [changed], Epoch.AddHours(1));
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));

        Assert.Equal(IndexingStage.MetadataIndexed, claim.Stage);
        Assert.Equal("hash-a", claim.ContentHash);
    }

    /// <summary>Verifies processor changes invalidate derived work.</summary>
    [Fact]
    public async Task ProcessorConfigurationChangeInvalidatesCompletedFile()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        var observation = fixture.Observation("document.txt", stableIdentity: "stable");
        await QueueAsync(store, source, [observation], processor: "processor-a");
        await CompleteBasicRunAsync(store, "hash-a");

        await QueueAsync(store, source, [observation], Epoch.AddHours(1), processor: "processor-b");
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));

        Assert.Equal(IndexingStage.MetadataIndexed, claim.Stage);
        Assert.Equal("processor-b", claim.ProcessorFingerprint);
    }

    /// <summary>Verifies duplicate content shares compatible derived data.</summary>
    [Fact]
    public async Task DuplicateContentCanReuseStandardDerivedData()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Standard);
        var first = fixture.Observation("first.txt", stableIdentity: "one");
        await QueueAsync(store, source, [first]);
        await CompleteStandardRunAsync(store, "shared-hash");
        var copy = fixture.Observation("copy.txt", stableIdentity: "two");
        await QueueAsync(store, source, [first, copy], Epoch.AddHours(1));
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));
        Assert.Equal(IndexingStage.FileDiscovered, claim.Stage);
        await SaveCompleteAsync(store, claim, IndexingStage.MetadataIndexed);
        claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));
        await SaveCompleteAsync(store, claim, IndexingStage.ContentFingerprinted);
        claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));

        var reusable = await store.GetReusableContentThroughStageAsync(
            "shared-hash",
            IndexingLevel.Standard,
            "processor");

        Assert.Equal(IndexingStage.SemanticRepresentationGenerated, reusable);
        await store.ReuseContentAsync(
            claim,
            "shared-hash",
            reusable!.Value,
            IndexingStage.SearchIndexUpdated,
            Epoch.AddHours(2));
        Assert.Equal(2, (await store.GetSearchDocumentsAsync(10)).Count);
    }

    /// <summary>Verifies deleted-record retention and explicit cleanup.</summary>
    [Fact]
    public async Task MissingFileIsRetainedThenRemovedByExplicitRetentionPolicy()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        await QueueAsync(store, source, [fixture.Observation("deleted.txt")]);
        await CompleteBasicRunAsync(store, "hash-a");

        await QueueAsync(store, source, [], Epoch.AddDays(1));
        Assert.Equal(0, (await store.GetSearchCoverageAsync()).KnownFileCount);
        var maintenance = await store.MaintainAsync(
            new DeepIndexingSettings { DeletedFileRetentionDays = 0 },
            Epoch.AddDays(2));

        Assert.Contains(maintenance.Actions, action => action.Code == "expired-deleted-files");
        Assert.Empty(await store.GetSearchDocumentsAsync(10));
    }

    /// <summary>Verifies source removal cleans orphaned derived data only.</summary>
    [Fact]
    public async Task RemovingSourceCleansOrphanedDerivedDataWithoutTouchingFolder()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Standard);
        await QueueAsync(store, source, [fixture.Observation("document.txt")]);
        await CompleteStandardRunAsync(store, "hash-a");

        await store.RemoveSourceAsync(source.Id);
        var maintenance = await store.MaintainAsync(new DeepIndexingSettings(), Epoch.AddDays(1));

        Assert.True(Directory.Exists(fixture.Root));
        Assert.Empty(await store.GetSourcesAsync());
        Assert.Empty(await store.GetSearchDocumentsAsync(10));
        Assert.True(maintenance.IsWithinQuota);
    }

    /// <summary>Verifies pause and resume are durable claim controls.</summary>
    [Fact]
    public async Task PausePreventsClaimAndResumeMakesWorkEligible()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("pause.txt")]);

        await store.SetActiveRunsStatusAsync(IndexingRunStatus.Paused, null, Epoch.AddMinutes(1));
        Assert.Null(await store.ClaimNextAsync(Epoch.AddMinutes(2)));
        await store.SetActiveRunsStatusAsync(IndexingRunStatus.Running, null, Epoch.AddMinutes(3));

        Assert.NotNull(await store.ClaimNextAsync(Epoch.AddMinutes(4)));
    }

    /// <summary>Verifies resource waiting never overwrites an explicit durable user pause.</summary>
    [Fact]
    public async Task ResourceWaitingDoesNotOverrideExplicitPause()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("paused.txt")]);
        await store.SetActiveRunsStatusAsync(IndexingRunStatus.Paused, null, Epoch.AddMinutes(1));

        await store.SetActiveRunsStatusAsync(
            IndexingRunStatus.Waiting,
            "waiting for resource policy",
            Epoch.AddMinutes(2));

        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(3));
        Assert.Equal(IndexingRunStatus.Paused, progress.Status);
        Assert.All(
            await store.GetResumableRunsAsync(),
            run => Assert.Equal(IndexingRunStatus.Paused, run.Status));
    }

    /// <summary>Verifies replacing an active refresh cancels both its job and durable stage state.</summary>
    [Fact]
    public async Task NewRefreshLeavesNoSupersededRunningStageState()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        await QueueAsync(store, source, [fixture.Observation("active.txt")]);
        Assert.NotNull(await store.ClaimNextAsync(Epoch.AddMinutes(1)));

        _ = await store.BeginRunAsync(source.Id, Epoch.AddMinutes(2));

        Assert.Equal(
            "0",
            ReadScalar(
                fixture.DatabasePath,
                $"SELECT COUNT(*) FROM index_stage_states WHERE status = {(int)IndexingStageStatus.Running};"));
        var resumable = Assert.Single(await store.GetResumableRunsAsync());
        Assert.Equal(IndexingRunStatus.Running, resumable.Status);
    }

    /// <summary>Verifies cancellation leaves no stale running claim.</summary>
    [Fact]
    public async Task SafeCancellationLeavesNoRunningJobs()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("cancel.txt")]);
        Assert.NotNull(await store.ClaimNextAsync(Epoch.AddMinutes(1)));

        await store.SetActiveRunsStatusAsync(IndexingRunStatus.Cancelled, "test", Epoch.AddMinutes(2));

        Assert.Null(await store.ClaimNextAsync(Epoch.AddMinutes(3)));
        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(3));
        Assert.Equal(IndexingRunStatus.Cancelled, progress.Status);
        Assert.Equal(progress.TotalDiscovered, progress.Processed);
        Assert.Equal(
            "0",
            ReadScalar(
                fixture.DatabasePath,
                $"SELECT COUNT(*) FROM index_stage_states WHERE status = {(int)IndexingStageStatus.Running};"));
    }

    /// <summary>Verifies process restart recovers a running stage.</summary>
    [Fact]
    public async Task RestartRecoveryRequeuesStaleRunningStage()
    {
        using var fixture = new IndexFixture();
        await using (var first = await fixture.CreateInitializedStoreAsync())
        {
            await QueueAsync(first, fixture.Source(), [fixture.Observation("restart.txt")]);
            Assert.NotNull(await first.ClaimNextAsync(Epoch.AddMinutes(1)));
        }

        await using var recovered = fixture.CreateStore();
        await recovered.InitializeAsync();
        Assert.Equal(1, await recovered.RecoverInterruptedWorkAsync(Epoch.AddMinutes(2)));
        var claim = Assert.IsType<IndexingWorkItem>(await recovered.ClaimNextAsync(Epoch.AddMinutes(3)));

        Assert.Equal(IndexingStage.FileDiscovered, claim.Stage);
        Assert.Equal(2, claim.Attempt);
    }

    /// <summary>Verifies retryable work stops at the configured attempt bound.</summary>
    [Fact]
    public async Task RetryableFailureStopsAtConfiguredMaximumAttempts()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(),
            [fixture.Observation("locked.txt")],
            maximumRetries: 1);
        var first = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(1)));
        await store.SaveStageOutputAsync(
            first,
            Retryable("locked"),
            IndexingStage.MetadataIndexed,
            Epoch.AddMinutes(1),
            TimeSpan.Zero,
            Epoch.AddMinutes(2));
        Assert.Equal(1, await store.ResumeEligibleWaitingRunsAsync(Epoch.AddMinutes(3)));
        var second = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(3)));
        await store.SaveStageOutputAsync(
            second,
            Retryable("locked"),
            IndexingStage.MetadataIndexed,
            Epoch.AddMinutes(3),
            TimeSpan.Zero,
            Epoch.AddMinutes(4));

        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(5));
        Assert.Equal(1, progress.Failed);
        Assert.Equal(0, progress.RetryScheduled);
        Assert.Null(await store.ClaimNextAsync(Epoch.AddMinutes(5)));
    }

    /// <summary>Verifies dependency work becomes eligible at its retry time.</summary>
    [Fact]
    public async Task WaitingDependencyBecomesEligibleAtPersistedRetryTime()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("ocr.pdf")]);
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(1)));
        await store.SaveStageOutputAsync(
            claim,
            new IndexingStageOutput
            {
                Status = IndexingStageStatus.WaitingForDependency,
                WaitingDependency = "OCR",
                FailureCategory = IndexingFailureCategory.DependencyUnavailable,
                ErrorCode = "ocr-unavailable",
                IsRetryable = true,
            },
            IndexingStage.MetadataIndexed,
            Epoch.AddMinutes(1),
            TimeSpan.Zero,
            Epoch.AddMinutes(5));

        Assert.Null(await store.ClaimNextAsync(Epoch.AddMinutes(4)));
        var waiting = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(4));
        Assert.Equal(IndexingRunStatus.Waiting, waiting.Status);
        Assert.Equal(0, await store.ResumeEligibleWaitingRunsAsync(Epoch.AddMinutes(4)));
        Assert.Equal(1, await store.ResumeEligibleWaitingRunsAsync(Epoch.AddMinutes(6)));
        var retry = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(6)));
        Assert.Equal(2, retry.Attempt);
    }

    /// <summary>Verifies permanent failures are counted and privacy-safe.</summary>
    [Theory]
    [InlineData(IndexingFailureCategory.PermissionDenied, "access-denied")]
    [InlineData(IndexingFailureCategory.NotFound, "file-not-found")]
    [InlineData(IndexingFailureCategory.Permanent, "unsupported-input")]
    public async Task PermanentFailuresAreCountedAndReviewable(
        IndexingFailureCategory category,
        string code)
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation($"{code}.txt")]);
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(1)));
        await store.SaveStageOutputAsync(
            claim,
            new IndexingStageOutput
            {
                Status = IndexingStageStatus.Failed,
                FailureCategory = category,
                ErrorCode = code,
            },
            null,
            Epoch.AddMinutes(1),
            TimeSpan.FromMilliseconds(5),
            null);

        var failure = Assert.Single(await store.GetFailuresAsync(10));
        Assert.Equal(category, failure.Category);
        Assert.Equal(Path.GetFileName(claim.FullPath), failure.FileName);
        Assert.DoesNotContain(fixture.Root, failure.FileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies progress monotonicity and terminal counts.</summary>
    [Fact]
    public async Task ProgressIsMonotonicAndCountsEveryTerminalOutcome()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(),
            [fixture.Observation("one.txt"), fixture.Observation("two.txt", stableIdentity: "two")]);
        var percentages = new List<double>();
        while (await store.ClaimNextAsync(Epoch.AddMinutes(1)) is { } claim)
        {
            await store.SaveStageOutputAsync(
                claim,
                new IndexingStageOutput { Status = IndexingStageStatus.Skipped, StopsFile = true },
                null,
                Epoch.AddMinutes(2),
                TimeSpan.Zero,
                null);
            percentages.Add((await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(2))).OverallPercentage);
        }

        Assert.Equal([50d, 100d], percentages);
        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(2));
        Assert.Equal(2, progress.Skipped);
        Assert.Equal(0, progress.Failed);
        Assert.Equal(0, progress.Waiting);
    }

    /// <summary>Verifies estimated time is hidden until enough samples exist.</summary>
    [Fact]
    public async Task EstimateAppearsOnlyAfterMeaningfulSample()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var files = Enumerable.Range(0, 10).Select(index => fixture.Observation($"{index}.txt", $"id-{index}")).ToArray();
        await QueueAsync(store, fixture.Source(), files);
        for (var index = 0; index < 5; index++)
        {
            var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddSeconds(index + 1)));
            await store.SaveStageOutputAsync(
                claim,
                new IndexingStageOutput { Status = IndexingStageStatus.Skipped, StopsFile = true },
                null,
                Epoch.AddSeconds(index + 1),
                TimeSpan.Zero,
                null);
        }

        var early = await store.GetProgressAsync(1024 * 1024, Epoch.AddSeconds(1));
        var sampled = await store.GetProgressAsync(1024 * 1024, Epoch.AddSeconds(10));

        Assert.Null(early.EstimatedRemaining);
        Assert.NotNull(sampled.EstimatedRemaining);
    }

    /// <summary>Verifies partial metadata remains searchable.</summary>
    [Fact]
    public async Task PartialIndexDocumentsRemainAvailableToSearch()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("partial.txt")]);

        var document = Assert.Single(await store.GetSearchDocumentsAsync(10));
        var coverage = await store.GetSearchCoverageAsync();

        Assert.Equal("partial.txt", document.FileName);
        Assert.False(document.IsFullyIndexed);
        Assert.True(coverage.IsIncomplete);
        Assert.Equal(1, coverage.FilenameAndMetadataCount);
    }

    /// <summary>Verifies provider-neutral storage accounting.</summary>
    [Fact]
    public async Task StorageBreakdownReportsBoundedCategoriesAndPhysicalTotal()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("storage.txt")]);
        await CompleteStandardRunAsync(store, "hash-a");

        var storage = await store.GetStorageBreakdownAsync(16 * 1024 * 1024);

        Assert.True(storage.MetadataBytes > 0);
        Assert.True(storage.ExtractedTextBytes > 0);
        Assert.True(storage.SemanticDataBytes > 0);
        Assert.True(storage.DatabaseBytes > 0);
        Assert.Equal(16 * 1024 * 1024, storage.MaximumBytes);
    }

    /// <summary>Verifies near-quota cleanup prunes only explicit rebuildable chunk data before blocking work.</summary>
    [Fact]
    public async Task QuotaCleanupPrunesRebuildableChunksAndReturnsWithinLimit()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("quota.txt")]);
        await CompleteStandardRunAsync(store, "quota-hash");
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE index_chunks SET chunk_text = zeroblob(18 * 1024 * 1024) WHERE content_hash = 'quota-hash';";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        SqliteConnection.ClearAllPools();
        var result = await store.MaintainAsync(
            new DeepIndexingSettings { MaximumIndexSizeMiB = 16 },
            Epoch.AddDays(1));

        Assert.Contains(result.Actions, action => action.Code == "quota-pruned-rebuildable-chunks");
        Assert.True(result.IsWithinQuota);
        Assert.True(result.Storage.DatabaseBytes <= result.Storage.MaximumBytes);
        Assert.Single(await store.GetSearchDocumentsAsync(10));
    }

    /// <summary>Verifies concurrent callers retain a consistent database.</summary>
    [Fact]
    public async Task ConcurrentReadersAndWritersRemainConsistent()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var tasks = Enumerable.Range(0, 24).Select(async index =>
        {
            if (index % 3 == 0)
            {
                await store.UpsertSourceAsync(fixture.Source() with
                {
                    Id = $"source-{index}",
                    RootPath = Path.Combine(fixture.Root, $"source-{index}"),
                    DisplayName = $"Source {index}",
                });
            }
            else if (index % 3 == 1)
            {
                _ = await store.GetSourcesAsync();
            }
            else
            {
                _ = await store.GetSearchCoverageAsync();
            }
        });

        await Task.WhenAll(tasks);

        Assert.Equal(8, (await store.GetSourcesAsync()).Count);
        Assert.Equal("ok", ReadScalar(fixture.DatabasePath, "PRAGMA quick_check;"));
    }

    /// <summary>Verifies case-sensitive and insensitive path policies.</summary>
    [Fact]
    public async Task CaseInsensitivePathsCoalesceWhileCaseSensitivePathsRemainDistinct()
    {
        using var insensitiveFixture = new IndexFixture();
        await using (var insensitive = await insensitiveFixture.CreateInitializedStoreAsync(new WindowsPathSemantics()))
        {
            var source = insensitiveFixture.Source();
            await QueueAsync(
                insensitive,
                source,
                [
                    insensitiveFixture.Observation("Case.txt", stableIdentity: null),
                    insensitiveFixture.Observation("case.txt", stableIdentity: null),
                ]);
            Assert.Single(await insensitive.GetSearchDocumentsAsync(10));
        }

        using var sensitiveFixture = new IndexFixture();
        await using var sensitive = await sensitiveFixture.CreateInitializedStoreAsync(new LinuxPathSemantics());
        await QueueAsync(
            sensitive,
            sensitiveFixture.Source(),
            [
                sensitiveFixture.Observation("Case.txt", stableIdentity: null),
                sensitiveFixture.Observation("case.txt", stableIdentity: null),
            ]);
        Assert.Equal(2, (await sensitive.GetSearchDocumentsAsync(10)).Count);
    }

    /// <summary>Verifies unusual valid filenames persist exactly.</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("résumé 2026 #final.txt")]
    [InlineData("trailing.period.valid-on-linux.")]
    public async Task UnusualValidNamesPersistWithoutNormalizationLoss(string fileName)
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync(new LinuxPathSemantics());
        await QueueAsync(store, fixture.Source(), [fixture.Observation(fileName)]);

        Assert.Equal(fileName, Assert.Single(await store.GetSearchDocumentsAsync(10)).FileName);
    }

    /// <summary>Verifies long paths do not use fixed Windows buffers.</summary>
    [Fact]
    public async Task LongRelativePathPersistsWithoutFixedWindowsBuffer()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync(new LinuxPathSemantics());
        var relative = string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat(new string('a', 40), 8)) + ".txt";
        await QueueAsync(store, fixture.Source(), [fixture.Observation(relative)]);

        Assert.Equal(relative, Assert.Single(await store.GetSearchDocumentsAsync(10)).FullPath[(fixture.Root.Length + 1)..]);
    }

    /// <summary>Verifies rebuild preserves sources while clearing derived values.</summary>
    [Fact]
    public async Task RebuildPreservesSourcesButClearsDerivedDocuments()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("rebuild.txt")]);
        await CompleteStandardRunAsync(store, "hash-a");

        await store.RebuildAsync(Epoch.AddDays(1));

        Assert.Single(await store.GetSourcesAsync());
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10));
        Assert.Null(document.ExtractedText);
        Assert.False(document.IsFullyIndexed);
    }

    private static async Task<string> QueueAsync(
        SqliteDeepIndexStore store,
        IndexingSource source,
        IReadOnlyList<IndexingFileObservation> files,
        DateTimeOffset? at = null,
        string processor = "processor",
        int maximumRetries = 3)
    {
        await store.UpsertSourceAsync(source);
        var runId = await store.BeginRunAsync(source.Id, at ?? Epoch);
        await store.EnqueueDiscoveredFilesAsync(runId, files, processor, maximumRetries);
        await store.CompleteDiscoveryAsync(runId, new HashSet<string>(), at ?? Epoch);
        return runId;
    }

    private static async Task CompleteBasicRunAsync(SqliteDeepIndexStore store, string hash)
    {
        while (await store.ClaimNextAsync(Epoch.AddDays(10)) is { } claim)
        {
            var next = claim.Stage switch
            {
                IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
                IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
                IndexingStage.ContentFingerprinted => IndexingStage.SearchIndexUpdated,
                IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
                IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
                IndexingStage.FileFullyIndexed => (IndexingStage?)null,
                _ => throw new InvalidOperationException($"Unexpected Basic stage {claim.Stage}."),
            };
            await store.SaveStageOutputAsync(
                claim,
                new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    ContentHash = claim.Stage == IndexingStage.ContentFingerprinted ? hash : null,
                },
                next,
                Epoch.AddDays(10),
                TimeSpan.FromMilliseconds(1),
                null);
        }
    }

    private static async Task CompleteStandardRunAsync(SqliteDeepIndexStore store, string hash)
    {
        while (await store.ClaimNextAsync(Epoch.AddDays(10)) is { } claim)
        {
            var next = claim.Stage switch
            {
                IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
                IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
                IndexingStage.ContentFingerprinted => IndexingStage.TextExtracted,
                IndexingStage.TextExtracted => IndexingStage.SummaryKeywordsGenerated,
                IndexingStage.SummaryKeywordsGenerated => IndexingStage.SemanticRepresentationGenerated,
                IndexingStage.SemanticRepresentationGenerated => IndexingStage.SearchIndexUpdated,
                IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
                IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
                IndexingStage.FileFullyIndexed => (IndexingStage?)null,
                _ => throw new InvalidOperationException($"Unexpected Standard stage {claim.Stage}."),
            };
            var output = claim.Stage switch
            {
                IndexingStage.ContentFingerprinted => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    ContentHash = hash,
                },
                IndexingStage.TextExtracted => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    ExtractedText = "bounded document text",
                },
                IndexingStage.SummaryKeywordsGenerated => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    Summary = "bounded summary",
                    Keywords = ["bounded", "document"],
                    SelectedChunks = ["bounded document text"],
                },
                IndexingStage.SemanticRepresentationGenerated => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    SemanticRepresentation = [0.5f, 0.5f],
                },
                _ => new IndexingStageOutput { Status = IndexingStageStatus.Complete },
            };
            await store.SaveStageOutputAsync(
                claim,
                output,
                next,
                Epoch.AddDays(10),
                TimeSpan.FromMilliseconds(1),
                null);
        }
    }

    private static Task SaveCompleteAsync(
        SqliteDeepIndexStore store,
        IndexingWorkItem claim,
        IndexingStage next) =>
        store.SaveStageOutputAsync(
            claim,
            new IndexingStageOutput { Status = IndexingStageStatus.Complete },
            next,
            Epoch.AddDays(10),
            TimeSpan.Zero,
            null);

    private static IndexingStageOutput Retryable(string code) => new()
    {
        Status = IndexingStageStatus.Failed,
        FailureCategory = IndexingFailureCategory.TransientIo,
        ErrorCode = code,
        IsRetryable = true,
    };

    private static void CreateDatabase(string path, string sql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ReadUserVersion(string path) =>
        Convert.ToInt32(ReadScalar(path, "PRAGMA user_version;"), System.Globalization.CultureInfo.InvariantCulture);

    private static string ReadScalar(string path, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private sealed class IndexFixture : IDisposable
    {
        public IndexFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "OpenSorSe-index-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "deep-index.db");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public SqliteDeepIndexStore CreateStore(IPathSemantics? semantics = null) =>
            new(DatabasePath, semantics ?? PlatformServices.CurrentPathSemantics);

        public async Task<SqliteDeepIndexStore> CreateInitializedStoreAsync(IPathSemantics? semantics = null)
        {
            var store = CreateStore(semantics);
            await store.InitializeAsync();
            return store;
        }

        public IndexingSource Source(IndexingLevel level = IndexingLevel.Basic) =>
            new("source", Root, "Test source", level, true, true, 0, []);

        public IndexingFileObservation Observation(string relativePath, string? stableIdentity = "stable") =>
            new(
                Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                relativePath.Replace('/', Path.DirectorySeparatorChar),
                stableIdentity,
                stableIdentity is null ? null : "volume",
                10,
                Epoch,
                Epoch,
                FileAttributes.Normal,
                "metadata-a");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
