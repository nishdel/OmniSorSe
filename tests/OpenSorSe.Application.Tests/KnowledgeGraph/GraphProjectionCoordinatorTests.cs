using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Validates restart-safe projection orchestration, manifest gates, control, and fencing.</summary>
public sealed class GraphProjectionCoordinatorTests
{
    /// <summary>Verifies initialization validates both stores and recovers expired claims.</summary>
    [Fact]
    public async Task Initialize_RecoversExpiredClaim()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore();
        store.SeedExpiredClaim(TestGraphData.Source());
        var decisions = new FakeGraphDecisionStore();
        await using var coordinator = Create(source, store, decisions);

        await coordinator.InitializeAsync();

        Assert.True(store.Initialized);
        Assert.True(decisions.Initialized);
        Assert.Equal(1, store.RecoveredClaimCount);
    }

    /// <summary>Verifies invalid graph-native decision authority prevents initialization.</summary>
    [Fact]
    public async Task Initialize_InvalidDecisionStore_FailsClosed()
    {
        var decisions = new FakeGraphDecisionStore { IsValid = false };
        await using var coordinator = Create(new FakeGraphProjectionSource(), new FakeGraphStore(), decisions);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() => coordinator.InitializeAsync());

        Assert.Equal("decision-store-invalid", error.ReasonCode);
    }

    /// <summary>Verifies a complete source manifest is queued, projected, and atomically applied.</summary>
    [Fact]
    public async Task Reconcile_CompleteManifest_ProjectsEveryObservation()
    {
        var observations = new GraphProjectionObservation[] { TestGraphData.Source(), TestGraphData.File() };
        var source = new FakeGraphProjectionSource(observations);
        var store = new FakeGraphStore();
        await using var coordinator = Create(source, store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, store.Components.Count);
        Assert.Equal("manifest-1", store.Coverage.IngestedManifestId);
        Assert.Equal("manifest-1", store.Coverage.AppliedManifestId);
        Assert.Equal(1, store.Coverage.IngestedRevision);
        Assert.Equal(1, store.Coverage.AppliedRevision);
        Assert.Equal(new[]
        {
            GraphProjectionStage.CandidatesExtracted,
            GraphProjectionStage.CandidatesNormalized,
            GraphProjectionStage.IdentityResolved,
            GraphProjectionStage.EdgesPrepared,
            GraphProjectionStage.ComponentValidated,
            GraphProjectionStage.ComponentPublished,
            GraphProjectionStage.CandidatesExtracted,
            GraphProjectionStage.CandidatesNormalized,
            GraphProjectionStage.IdentityResolved,
            GraphProjectionStage.EdgesPrepared,
            GraphProjectionStage.ComponentValidated,
            GraphProjectionStage.ComponentPublished,
            GraphProjectionStage.StaleRowsCleaned,
        }, store.StageHistory);
    }

    /// <summary>Verifies a fenced intermediate stage never advances later watermarks or leaves a running claim.</summary>
    [Fact]
    public async Task Reconcile_IntermediateStageFenced_DoesNotPublishOrAdvanceLaterStages()
    {
        var store = new FakeGraphStore { RejectStageAdvanceAt = GraphProjectionStage.IdentityResolved };
        await using var coordinator = Create(
            new FakeGraphProjectionSource([TestGraphData.Source()]),
            store,
            new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync();

        Assert.False(result.Succeeded);
        Assert.Empty(store.Components);
        Assert.Equal(new[]
        {
            GraphProjectionStage.CandidatesExtracted,
            GraphProjectionStage.CandidatesNormalized,
        }, store.StageHistory);
        Assert.Equal(GraphJobExecutionState.Cancelled, store.LastFailureState);
        Assert.Equal(0, (await store.GetStatusAsync()).RunningCount);
    }

    /// <summary>Verifies restart recovery may recompute pure work while resuming from its last truthful durable stage.</summary>
    [Fact]
    public async Task Initialize_ExpiredIntermediateStage_RecoversAndPublishesOnce()
    {
        var observation = TestGraphData.Source();
        var snapshot = new FakeGraphProjectionSource([observation]).Snapshot with
        {
            GraphDecisionSequence = 0,
            GraphDecisionCheckpointId = "checkpoint-0-0",
        };
        var fingerprint = new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver())
            .Build(observation, snapshot, TestGraphData.Now).InputFingerprint;
        var store = new FakeGraphStore();
        store.SeedExpiredClaim(observation, GraphProjectionStage.CandidatesNormalized, fingerprint);
        await using var coordinator = Create(
            new FakeGraphProjectionSource([observation]),
            store,
            new FakeGraphDecisionStore());

        await coordinator.InitializeAsync();
        var result = await coordinator.ReconcileAsync();

        Assert.True(result.Succeeded);
        Assert.Single(store.Components);
        Assert.DoesNotContain(GraphProjectionStage.CandidatesExtracted, store.StageHistory);
        Assert.Contains(GraphProjectionStage.IdentityResolved, store.StageHistory);
        Assert.Equal(0, (await store.GetStatusAsync()).RunningCount);
    }

    /// <summary>Verifies cancellation at a pure-stage boundary is durably acknowledged without publication.</summary>
    [Fact]
    public async Task Reconcile_CancelledAtIntermediateStage_RecordsCancelledClaim()
    {
        var store = new FakeGraphStore();
        using var cancellation = new CancellationTokenSource();
        store.StageAdvanceHook = stage =>
        {
            if (stage == GraphProjectionStage.IdentityResolved)
            {
                cancellation.Cancel();
            }
        };
        await using var coordinator = Create(
            new FakeGraphProjectionSource([TestGraphData.Source()]),
            store,
            new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.ReconcileAsync(cancellation.Token));

        Assert.Empty(store.Components);
        Assert.Equal(GraphJobExecutionState.Cancelled, store.LastFailureState);
        Assert.Equal(0, (await store.GetStatusAsync()).RunningCount);
    }

    /// <summary>Verifies completed work is not repeated when the same run is reconciled again.</summary>
    [Fact]
    public async Task Reconcile_UnchangedCompletedManifest_DoesNotRepeatWork()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore();
        await using var coordinator = Create(source, store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();
        await coordinator.ReconcileAsync();

        await coordinator.ReconcileAsync();

        Assert.Single(store.Components);
    }

    /// <summary>Verifies one complete schema-3 decision manifest is mirrored once and unchanged reconciliation reuses it.</summary>
    [Fact]
    public async Task Reconcile_CompleteLegacyDecisionManifest_PublishesMirrorOnce()
    {
        var decisions = new FakeGraphDecisionStore();
        var observation = LegacyDecision("relationship:reject:file-1:file-2");
        await using var coordinator = Create(
            new FakeGraphProjectionSource([observation]),
            new FakeGraphStore(),
            decisions);
        await coordinator.InitializeAsync();

        await coordinator.ReconcileAsync();
        await coordinator.ReconcileAsync();

        Assert.Equal("legacy-1", decisions.LegacyMirrorManifestId);
        Assert.Equal(1, decisions.LegacyMirrorPublishCount);
    }

    /// <summary>Verifies an interrupted durable mirror page replays idempotently and publishes only after the complete manifest.</summary>
    [Fact]
    public async Task Reconcile_InterruptedLegacyDecisionMirror_ResumesWithoutDuplicatePublication()
    {
        var decisions = new FakeGraphDecisionStore { FailNextLegacyMirrorPageAfterStage = true };
        var observation = LegacyDecision("relationship:reject:file-1:file-2");
        await using var coordinator = Create(
            new FakeGraphProjectionSource([observation]),
            new FakeGraphStore(),
            decisions);
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<GraphPersistenceException>(() => coordinator.ReconcileAsync());
        Assert.Null(decisions.LegacyMirrorManifestId);

        var result = await coordinator.ReconcileAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("legacy-1", decisions.LegacyMirrorManifestId);
        Assert.Equal(1, decisions.LegacyMirrorPublishCount);
    }

    /// <summary>Verifies terminal manifest count disagreement blocks publication.</summary>
    [Fact]
    public async Task Reconcile_TerminalCountMismatch_RejectsManifest()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        source.Snapshot = source.Snapshot with
        {
            TotalObservationCount = 2,
            ObservationCounts = [new GraphObservationKindCount(GraphProjectionObservationKind.Source, 2)],
        };
        var store = new FakeGraphStore();
        await using var coordinator = Create(source, store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.ReconcileAsync());

        Assert.Empty(store.Components);
        Assert.Null(store.Coverage.AppliedManifestId);
    }

    /// <summary>Verifies provider terminal-hash rejection leaves the applied watermark unchanged.</summary>
    [Fact]
    public async Task Reconcile_TerminalHashRejected_LeavesAppliedWatermark()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore { RejectManifestHash = true };
        await using var coordinator = Create(source, store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.ReconcileAsync());

        Assert.Equal("manifest-1", store.Coverage.IngestedManifestId);
        Assert.Null(store.Coverage.AppliedManifestId);
        Assert.Empty(store.Components);
    }

    /// <summary>Verifies an obsolete fencing epoch cannot publish a component.</summary>
    [Fact]
    public async Task Reconcile_StaleFencingToken_IsRejected()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore { RejectCommit = true };
        await using var coordinator = Create(source, store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync();

        Assert.False(result.Succeeded);
        Assert.Empty(store.Components);
        Assert.Null(store.Coverage.AppliedManifestId);
        Assert.Equal(0, (await store.GetStatusAsync()).RunningCount);
    }

    /// <summary>Verifies a source/privacy change during pure projection fences publication.</summary>
    [Fact]
    public async Task Reconcile_AuthorityChangesDuringClaim_DoesNotPublish()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore();
        var builder = new CallbackBuilder(new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()), () =>
        {
            source.Authority = source.Authority with { CurrentSourceManifestId = "manifest-2", CurrentSourceRevision = 2 };
        });
        await using var coordinator = Create(source, store, new FakeGraphDecisionStore(), builder);
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync();

        Assert.False(result.Succeeded);
        Assert.Empty(store.Components);
        Assert.Equal(GraphRunControlState.Cancelled, store.RunControl);
        Assert.Equal(1, store.FailureCount);
    }

    /// <summary>Verifies a new graph-native decision during a claim fences stale publication.</summary>
    [Fact]
    public async Task Reconcile_DecisionAdvancesDuringClaim_DoesNotPublish()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore();
        var decisions = new FakeGraphDecisionStore();
        var builder = new CallbackBuilder(new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()), () =>
        {
            decisions.AppendAsync(new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.RejectSuggestion,
                SubjectId = "suggestion-1",
                ExpectedSequence = 0,
            }, TestGraphData.Now).GetAwaiter().GetResult();
        });
        await using var coordinator = Create(source, store, decisions, builder);
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync();

        Assert.False(result.Succeeded);
        Assert.Empty(store.Components);
        Assert.Equal(GraphRunControlState.Cancelled, store.RunControl);
    }

    /// <summary>Verifies malformed work is isolated as a permanent failure while the run drains.</summary>
    [Fact]
    public async Task Reconcile_InvalidObservation_RecordsPermanentFailure()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.File() with { Length = -1 }]);
        var store = new FakeGraphStore();
        await using var coordinator = Create(source, store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await coordinator.ReconcileAsync();

        Assert.Equal(1, store.FailureCount);
        Assert.Empty(store.Components);
        Assert.Equal(GraphRunControlState.Complete, store.RunControl);
    }

    /// <summary>Verifies a provider-classified transient publication failure remains bounded and retryable.</summary>
    [Fact]
    public async Task Reconcile_TransientPersistenceFailure_RecordsRetryableFailure()
    {
        var store = new FakeGraphStore
        {
            CommitFailure = new GraphPersistenceException(
                "provider-busy",
                "Synthetic provider contention.",
                disposition: GraphPersistenceFailureDisposition.Retryable),
        };
        await using var coordinator = Create(
            new FakeGraphProjectionSource([TestGraphData.Source()]),
            store,
            new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await coordinator.ReconcileAsync();

        Assert.Equal(GraphJobExecutionState.RetryableFailure, store.LastFailureState);
        Assert.Equal("provider-busy", store.LastFailure?.ErrorCode);
        Assert.True(store.LastFailure?.Retryable);
        Assert.Empty(store.Components);
    }

    /// <summary>Verifies a storage-capacity failure waits for resources instead of becoming corrupt input.</summary>
    [Fact]
    public async Task Reconcile_StorageCapacityFailure_WaitsForResources()
    {
        var store = new FakeGraphStore
        {
            CommitFailure = new GraphPersistenceException(
                "provider-full",
                "Synthetic storage exhaustion.",
                disposition: GraphPersistenceFailureDisposition.WaitingForResources),
        };
        await using var coordinator = Create(
            new FakeGraphProjectionSource([TestGraphData.Source()]),
            store,
            new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await coordinator.ReconcileAsync();

        Assert.Equal(GraphJobExecutionState.WaitingForResources, store.LastFailureState);
        Assert.Equal("provider-full", store.LastFailure?.ErrorCode);
        Assert.False(store.LastFailure?.Retryable);
        Assert.Empty(store.Components);
    }

    /// <summary>Verifies pause and resume persist distinct durable states.</summary>
    [Fact]
    public async Task PauseAndResume_NoActiveClaim_AcknowledgeImmediately()
    {
        var store = new FakeGraphStore();
        await using var coordinator = Create(new FakeGraphProjectionSource(), store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await coordinator.PauseAsync();
        Assert.Equal(GraphRunControlState.Paused, store.RunControl);

        await coordinator.ResumeAsync();
        Assert.Equal(GraphRunControlState.Running, store.RunControl);
    }

    /// <summary>Verifies cancellation is durable and distinct from successful completion.</summary>
    [Fact]
    public async Task Cancel_NoActiveClaim_AcknowledgesCancelledNotComplete()
    {
        var store = new FakeGraphStore();
        await using var coordinator = Create(new FakeGraphProjectionSource(), store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await coordinator.CancelAsync("user-cancel");

        Assert.Equal(GraphRunControlState.Cancelled, store.RunControl);
        Assert.NotEqual(GraphRunControlState.Complete, store.RunControl);
    }

    /// <summary>Verifies cancellation is not mutated into an in-place resume.</summary>
    [Fact]
    public async Task Resume_CancelledRun_RequiresRetryInstead()
    {
        var store = new FakeGraphStore();
        await using var coordinator = Create(new FakeGraphProjectionSource(), store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();
        await coordinator.CancelAsync("user-cancel");

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ResumeAsync());

        Assert.Equal(GraphRunControlState.Cancelled, store.RunControl);
    }

    /// <summary>Verifies bounded shutdown reaches a durable pause boundary without sleeping.</summary>
    [Fact]
    public async Task Stop_NoActiveClaim_CompletesAtPauseBoundary()
    {
        var store = new FakeGraphStore();
        await using var coordinator = Create(new FakeGraphProjectionSource(), store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        await coordinator.StopAsync(TimeSpan.Zero);
        await coordinator.StopAsync(TimeSpan.Zero);

        Assert.Equal(GraphRunControlState.Paused, store.RunControl);
    }

    /// <summary>Verifies coordinator disposal is idempotent and owns each provider exactly once.</summary>
    [Fact]
    public async Task DisposeAsync_IsIdempotentAndDisposesOwnedProvidersOnce()
    {
        var source = new FakeGraphProjectionSource();
        var store = new FakeGraphStore();
        var decisions = new FakeGraphDecisionStore();
        var coordinator = Create(source, store, decisions);
        await coordinator.InitializeAsync();

        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, store.DisposeCount);
        Assert.Equal(1, decisions.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.GetStatusAsync());
    }

    /// <summary>Verifies explicit enablement requires consent and disable retains provider state.</summary>
    [Fact]
    public async Task EnableAndDisable_RequireConsentAndToggleAdmission()
    {
        var store = new FakeGraphStore { Enabled = false };
        var decisions = new FakeGraphDecisionStore
        {
            ControlSettings = new GraphControlSettings(),
        };
        await using var coordinator = Create(new FakeGraphProjectionSource(), store, decisions);
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EnableAsync(false));
        Assert.False(store.Enabled);

        await coordinator.EnableAsync(true);
        Assert.True(store.Enabled);

        await coordinator.DisableAsync();
        Assert.False(store.Enabled);
    }

    /// <summary>Verifies startup inspection creates no stores until explicit consent provisions both sidecars.</summary>
    [Fact]
    public async Task Initialize_Unprovisioned_DoesNotOpenStoresUntilEnable()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore();
        var decisions = new FakeGraphDecisionStore();
        var lifecycle = new FakeGraphStorageLifecycle();
        await using var coordinator = Create(source, store, decisions, storageLifecycle: lifecycle);

        await coordinator.InitializeAsync();
        var status = await coordinator.GetStatusAsync();
        var reconcile = await coordinator.ReconcileAsync();

        Assert.False(status.IsProvisioned);
        Assert.False(store.Initialized);
        Assert.False(decisions.Initialized);
        Assert.False(reconcile.Succeeded);
        Assert.Equal(0, source.ReadCount);

        await coordinator.EnableAsync(true);

        Assert.Equal(1, lifecycle.ProvisionCount);
        Assert.True(store.Initialized);
        Assert.True(decisions.Initialized);
        Assert.True((await coordinator.GetStatusAsync()).IsProvisioned);
    }

    /// <summary>Verifies status reports the verified total across derived, decision, and backup sidecars.</summary>
    [Fact]
    public async Task GetStatus_UsesVerifiedCombinedStorageBreakdown()
    {
        var lifecycle = new FakeGraphStorageLifecycle
        {
            State = GraphStorageProvisioningState.Provisioned,
            Storage = new GraphStorageBreakdown
            {
                DerivedStoreBytes = 64,
                DecisionLedgerBytes = 32,
                VerifiedBackupBytes = 16,
                TotalBytes = 112,
                MaximumBytes = GraphLimits.MinimumStorageQuotaBytes,
                RequiredReserveBytes = 1024,
                IsInventoryVerified = true,
            },
        };
        await using var coordinator = Create(
            new FakeGraphProjectionSource(),
            new FakeGraphStore(),
            new FakeGraphDecisionStore(),
            storageLifecycle: lifecycle);
        await coordinator.InitializeAsync();

        var status = await coordinator.GetStatusAsync();

        Assert.Equal(112, status.StorageSizeBytes);
        Assert.Equal(32, status.StorageBreakdown.DecisionLedgerBytes);
        Assert.Equal(16, status.StorageBreakdown.VerifiedBackupBytes);
    }

    /// <summary>Verifies authoritative disable fails reads closed even if the derived mirror cannot be updated.</summary>
    [Fact]
    public async Task Disable_DerivedMirrorFailure_LeavesAuthoritativeControlDisabled()
    {
        var store = new FakeGraphStore { SetEnabledFailure = new IOException("synthetic") };
        var decisions = new FakeGraphDecisionStore();
        await using var coordinator = Create(new FakeGraphProjectionSource(), store, decisions);
        await Assert.ThrowsAsync<IOException>(() => coordinator.InitializeAsync());

        store.SetEnabledFailure = null;
        await coordinator.InitializeAsync();
        store.SetEnabledFailure = new IOException("synthetic");

        await Assert.ThrowsAsync<IOException>(() => coordinator.DisableAsync());

        Assert.False(decisions.ControlSettings.IsEnabled);
    }

    /// <summary>Verifies all stable modes and eligibility controls are authoritatively user-configurable.</summary>
    [Theory]
    [InlineData(IndexingResourceMode.Eco)]
    [InlineData(IndexingResourceMode.Balanced)]
    [InlineData(IndexingResourceMode.Fast)]
    public async Task UpdateResourceSettings_PersistsAuthoritativePolicy(IndexingResourceMode mode)
    {
        var decisions = new FakeGraphDecisionStore();
        await using var coordinator = Create(new FakeGraphProjectionSource(), new FakeGraphStore(), decisions);
        await coordinator.InitializeAsync();
        var before = await coordinator.GetControlSettingsAsync();

        var saved = await coordinator.UpdateResourceSettingsAsync(new GraphResourceControlUpdate
        {
            ResourceMode = mode,
            ProcessOnlyWhileIdle = true,
            ProcessOnlyWhileConnectedToPower = true,
            PauseBelowBatteryPercentage = 35,
            ProcessingWindowStartHour = 21,
            ProcessingWindowEndHour = 6,
            ExpectedRevision = before.Revision,
        });

        Assert.Equal(mode, saved.ResourceMode);
        Assert.Equal(mode switch
        {
            IndexingResourceMode.Eco => 1,
            IndexingResourceMode.Balanced => 2,
            _ => 4,
        }, saved.MaximumConcurrency);
        Assert.True(saved.ProcessOnlyWhileIdle);
        Assert.True(saved.ProcessOnlyWhileConnectedToPower);
        Assert.Equal(35, saved.PauseBelowBatteryPercentage);
        Assert.Equal(21, saved.ProcessingWindowStartHour);
        Assert.Equal(6, saved.ProcessingWindowEndHour);
        Assert.Equal(before.IsEnabled, saved.IsEnabled);
        Assert.Equal(before.ConsentConfirmed, saved.ConsentConfirmed);
        Assert.Equal(saved, decisions.ControlSettings);
    }

    /// <summary>Verifies malformed battery or one-sided processing windows never reach the authoritative store.</summary>
    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(101, 1, 2)]
    [InlineData(50, 1, null)]
    [InlineData(50, 2, 2)]
    public async Task UpdateResourceSettings_InvalidEligibilityValues_FailClosed(
        int battery,
        int? startHour,
        int? endHour)
    {
        var decisions = new FakeGraphDecisionStore();
        await using var coordinator = Create(new FakeGraphProjectionSource(), new FakeGraphStore(), decisions);
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.UpdateResourceSettingsAsync(new GraphResourceControlUpdate
        {
            ResourceMode = IndexingResourceMode.Balanced,
            PauseBelowBatteryPercentage = battery,
            ProcessingWindowStartHour = startHour,
            ProcessingWindowEndHour = endHour,
            ExpectedRevision = decisions.ControlSettings.Revision,
        }));

        Assert.Equal(0, decisions.ControlSettings.Revision);
    }

    /// <summary>Verifies a long bounded projection renews its lease before publication.</summary>
    [Fact]
    public async Task Reconcile_LongBuild_RenewsClaimAndObservesWorker()
    {
        var store = new FakeGraphStore();
        using var builder = new BlockingBuilder(new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()));
        await using var coordinator = Create(
            new FakeGraphProjectionSource([TestGraphData.Source()]),
            store,
            new FakeGraphDecisionStore(),
            builder,
            new SingleImmediateHeartbeatScheduler());
        await coordinator.InitializeAsync();

        var reconcile = coordinator.ReconcileAsync();
        await builder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.ClaimRenewed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        builder.Release();
        var result = await reconcile.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Equal(1, store.RenewCount);
        Assert.Single(store.Components);
    }

    /// <summary>Verifies every losing heartbeat wait is cancelled and observed when pure builds finish first.</summary>
    [Fact]
    public async Task Reconcile_FastBuilds_CancelAndObserveEveryHeartbeatWait()
    {
        using var scheduler = new TrackingHeartbeatScheduler();
        var builder = new HeartbeatGatedBuilder(
            new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()),
            scheduler);
        var observations = Enumerable.Range(0, 24)
            .Select(index => (GraphProjectionObservation)TestGraphData.Source(string.Concat("source-", index)))
            .ToArray();
        await using var coordinator = Create(
            new FakeGraphProjectionSource(observations),
            new FakeGraphStore(),
            new FakeGraphDecisionStore(),
            builder,
            scheduler);
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(observations.Length, scheduler.WaitCount);
        Assert.Equal(scheduler.WaitCount, scheduler.CancelledAndObservedCount);
        Assert.Equal(0, scheduler.ActiveWaitCount);
    }

    /// <summary>Verifies Eco, Balanced, and Fast modes bound actual pure projection concurrency to 1, 2, and 4 workers.</summary>
    [Theory]
    [InlineData(IndexingResourceMode.Eco, 1)]
    [InlineData(IndexingResourceMode.Balanced, 2)]
    [InlineData(IndexingResourceMode.Fast, 4)]
    public async Task Reconcile_ResourceModeControlsActualBoundedBuildConcurrency(
        IndexingResourceMode mode,
        int expectedConcurrency)
    {
        var observations = Enumerable.Range(0, 8)
            .Select(index => (GraphProjectionObservation)TestGraphData.Source(string.Concat("source-", index)))
            .ToArray();
        var decisions = new FakeGraphDecisionStore
        {
            ControlSettings = new GraphControlSettings
            {
                IsEnabled = true,
                ConsentConfirmed = true,
                ResourceMode = mode,
                MaximumConcurrency = expectedConcurrency,
            },
        };
        using var builder = new ConcurrencyMeasuringBuilder(
            new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()),
            expectedConcurrency);
        await using var coordinator = Create(
            new FakeGraphProjectionSource(observations),
            new FakeGraphStore(),
            decisions,
            builder);
        await coordinator.InitializeAsync();

        var reconciliation = coordinator.ReconcileAsync();
        await builder.TargetReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(expectedConcurrency, builder.PeakConcurrency);
        builder.Release();
        var result = await reconciliation.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Succeeded);
        Assert.Equal(expectedConcurrency, builder.PeakConcurrency);
    }

    /// <summary>Verifies a rejected heartbeat cancels and observes the builder without leaving running work.</summary>
    [Fact]
    public async Task Reconcile_HeartbeatRejected_FencesWithoutStaleRunningClaim()
    {
        var store = new FakeGraphStore { RejectRenew = true };
        using var builder = new BlockingBuilder(new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()));
        await using var coordinator = Create(
            new FakeGraphProjectionSource([TestGraphData.Source()]),
            store,
            new FakeGraphDecisionStore(),
            builder,
            new SingleImmediateHeartbeatScheduler());
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Empty(store.Components);
        Assert.Equal(0, (await store.GetStatusAsync()).RunningCount);
        Assert.True(builder.Started.Task.IsCompletedSuccessfully);
    }

    /// <summary>Verifies resource ineligibility is durably exposed before source pages are captured.</summary>
    [Fact]
    public async Task Reconcile_ResourceIneligible_WaitsWithoutReadingSource()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore();
        var policy = new FakeGraphResourcePolicy
        {
            Eligibility = new GraphResourceEligibility(false, "battery-threshold"),
        };
        await using var coordinator = Create(source, store, new FakeGraphDecisionStore(), resourcePolicy: policy);
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync();

        Assert.True(result.Succeeded);
        Assert.Contains("battery", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, store.ResourceWaitCount);
        Assert.Equal(0, source.ReadCount);
        Assert.Empty(store.Components);
    }

    /// <summary>Verifies page and claim boundaries publish durable monotonic progress instead of only terminal status.</summary>
    [Fact]
    public async Task Reconcile_PublishesIntermediateDurableProgress()
    {
        var store = new FakeGraphStore();
        await using var coordinator = Create(
            new FakeGraphProjectionSource([TestGraphData.Source(), TestGraphData.File()]),
            store,
            new FakeGraphDecisionStore());
        var statuses = new List<GraphCoordinatorStatus>();
        coordinator.StatusChanged += (_, status) => statuses.Add(status);
        await coordinator.InitializeAsync();

        await coordinator.ReconcileAsync();

        Assert.Contains(statuses, status => status.CurrentStage == "capturing-manifest");
        Assert.Contains(statuses, status => status.CurrentStage == "replaying-decisions");
        Assert.Contains(statuses, status => status.CurrentStage == "projecting-graph");
        Assert.Contains(statuses, status => status.CompletedCount == 1);
        Assert.Equal(statuses.Select(status => status.CompletedCount).Order().ToArray(),
            statuses.Select(status => status.CompletedCount).ToArray());
    }

    /// <summary>Verifies reviewed maintenance runs only through the bounded coordinator contract.</summary>
    [Fact]
    public async Task Maintain_EligibleSafeBoundary_ReturnsValidatedResult()
    {
        await using var coordinator = Create(new FakeGraphProjectionSource(), new FakeGraphStore(), new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();

        var result = await coordinator.MaintainAsync(new GraphMaintenanceRequest(
            GraphLimits.MinimumStorageQuotaBytes,
            GraphMaintenanceTrigger.UserRequested));

        Assert.Equal(1, result.RecordsRemoved);
        Assert.False(result.QuotaBlocked);
    }

    /// <summary>Verifies caller cancellation propagates and no partial component is returned.</summary>
    [Fact]
    public async Task Reconcile_CancelledBeforeStart_DoesNotPublish()
    {
        var store = new FakeGraphStore();
        await using var coordinator = Create(new FakeGraphProjectionSource([TestGraphData.Source()]), store, new FakeGraphDecisionStore());
        await coordinator.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.ReconcileAsync(cancellation.Token));

        Assert.Empty(store.Components);
    }

    private static GraphProjectionCoordinator Create(
        FakeGraphProjectionSource source,
        FakeGraphStore store,
        FakeGraphDecisionStore decisions,
        IGraphProjectionBuilder? builder = null,
        IGraphClaimHeartbeatScheduler? heartbeatScheduler = null,
        IGraphResourceAdmissionPolicy? resourcePolicy = null,
        IGraphStorageLifecycle? storageLifecycle = null) => new(
            source,
            store,
            decisions,
            builder ?? new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()),
            new DeterministicGraphDecisionProjectionBuilder(new ConservativeGraphIdentityResolver()),
            resourcePolicy ?? new FakeGraphResourcePolicy(),
            new FixedGraphTimeProvider(TestGraphData.Now),
            "test-owner",
            heartbeatScheduler,
            storageLifecycle);

    private static GraphLegacyDecisionObservation LegacyDecision(string stableKey) => new()
    {
        StableKey = stableKey,
        CanonicalRowHash = string.Concat("hash-", stableKey),
        Revision = 1,
        ObservedAtUtc = TestGraphData.Now,
        DecisionNamespace = "relationship",
        LegacyDecisionKey = "file-1:file-2",
        ActionCode = "reject",
    };

    private sealed class CallbackBuilder(IGraphProjectionBuilder inner, Action callback) : IGraphProjectionBuilder
    {
        public GraphComponentProjection Build(
            GraphProjectionObservation observation,
            GraphProjectionSnapshot snapshot,
            DateTimeOffset validatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var projection = inner.Build(observation, snapshot, validatedAtUtc, cancellationToken);
            callback();
            return projection;
        }
    }

    private sealed class BlockingBuilder(IGraphProjectionBuilder inner) : IGraphProjectionBuilder, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);

        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GraphComponentProjection Build(
            GraphProjectionObservation observation,
            GraphProjectionSnapshot snapshot,
            DateTimeOffset validatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            _release.Wait(cancellationToken);
            return inner.Build(observation, snapshot, validatedAtUtc, cancellationToken);
        }

        internal void Release() => _release.Set();

        public void Dispose() => _release.Dispose();
    }

    private sealed class HeartbeatGatedBuilder(
        IGraphProjectionBuilder inner,
        TrackingHeartbeatScheduler scheduler) : IGraphProjectionBuilder
    {
        public GraphComponentProjection Build(
            GraphProjectionObservation observation,
            GraphProjectionSnapshot snapshot,
            DateTimeOffset validatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            scheduler.WaitForBuildPermit(cancellationToken);
            return inner.Build(observation, snapshot, validatedAtUtc, cancellationToken);
        }
    }

    private sealed class ConcurrencyMeasuringBuilder(
        IGraphProjectionBuilder inner,
        int targetConcurrency) : IGraphProjectionBuilder, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private int _active;
        private int _peak;

        internal int PeakConcurrency => Volatile.Read(ref _peak);
        internal TaskCompletionSource<bool> TargetReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GraphComponentProjection Build(
            GraphProjectionObservation observation,
            GraphProjectionSnapshot snapshot,
            DateTimeOffset validatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            UpdatePeak(active);
            if (active >= targetConcurrency)
            {
                TargetReached.TrySetResult(true);
            }

            try
            {
                _release.Wait(cancellationToken);
                return inner.Build(observation, snapshot, validatedAtUtc, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        internal void Release() => _release.Set();

        public void Dispose() => _release.Dispose();

        private void UpdatePeak(int candidate)
        {
            var current = Volatile.Read(ref _peak);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref _peak, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class SingleImmediateHeartbeatScheduler : IGraphClaimHeartbeatScheduler
    {
        private int _waitCount;

        public Task WaitForHeartbeatAsync(CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _waitCount) == 1
                ? Task.CompletedTask
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class TrackingHeartbeatScheduler : IGraphClaimHeartbeatScheduler, IDisposable
    {
        private int _active;
        private int _cancelledAndObserved;
        private int _waitCount;
        private readonly SemaphoreSlim _buildPermits = new(0, int.MaxValue);

        internal int ActiveWaitCount => Volatile.Read(ref _active);
        internal int CancelledAndObservedCount => Volatile.Read(ref _cancelledAndObserved);
        internal int WaitCount => Volatile.Read(ref _waitCount);

        internal void WaitForBuildPermit(CancellationToken cancellationToken) =>
            _buildPermits.Wait(cancellationToken);

        public async Task WaitForHeartbeatAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _waitCount);
            Interlocked.Increment(ref _active);
            _buildPermits.Release();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _cancelledAndObserved);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Dispose() => _buildPermits.Dispose();
    }

}
