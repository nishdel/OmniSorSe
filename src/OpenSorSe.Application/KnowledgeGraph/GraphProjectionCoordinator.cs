using System.Runtime.ExceptionServices;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Coordinates restart-safe bounded graph projection without joining the indexing critical path.</summary>
public sealed class GraphProjectionCoordinator : IGraphProjectionCoordinator
{
    private readonly IGraphProjectionSource _source;
    private readonly IGraphStorageLifecycle _storageLifecycle;
    private readonly IGraphStore _store;
    private readonly IGraphDecisionStore _decisionStore;
    private readonly IGraphProjectionBuilder _builder;
    private readonly IGraphDecisionProjectionBuilder _decisionProjectionBuilder;
    private readonly IGraphResourceAdmissionPolicy _resourceAdmissionPolicy;
    private readonly IGraphClaimHeartbeatScheduler _heartbeatScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly string _ownerInstanceId;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _initialized;
    private bool _provisioned;
    private bool _disposed;

    /// <summary>Initializes the graph coordinator with provider-neutral dependencies.</summary>
    public GraphProjectionCoordinator(
        IGraphProjectionSource source,
        IGraphStore store,
        IGraphDecisionStore decisionStore,
        IGraphProjectionBuilder builder,
        IGraphDecisionProjectionBuilder decisionProjectionBuilder,
        IGraphResourceAdmissionPolicy resourceAdmissionPolicy,
        TimeProvider? timeProvider = null,
        string? ownerInstanceId = null,
        IGraphClaimHeartbeatScheduler? heartbeatScheduler = null,
        IGraphStorageLifecycle? storageLifecycle = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _storageLifecycle = storageLifecycle ?? new AlwaysProvisionedGraphStorageLifecycle();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _decisionStore = decisionStore ?? throw new ArgumentNullException(nameof(decisionStore));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _decisionProjectionBuilder = decisionProjectionBuilder ?? throw new ArgumentNullException(nameof(decisionProjectionBuilder));
        _resourceAdmissionPolicy = resourceAdmissionPolicy ?? throw new ArgumentNullException(nameof(resourceAdmissionPolicy));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _heartbeatScheduler = heartbeatScheduler ?? new GraphClaimHeartbeatScheduler(_timeProvider);
        _ownerInstanceId = string.IsNullOrWhiteSpace(ownerInstanceId)
            ? string.Concat("graph-worker-", Guid.NewGuid().ToString("N"))
            : ownerInstanceId;
        GraphQueryService.ValidateId(_ownerInstanceId);
    }

    /// <inheritdoc />
    public event EventHandler<GraphCoordinatorStatus>? StatusChanged;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var provisioning = await _storageLifecycle.GetProvisioningStateAsync(cancellationToken).ConfigureAwait(false);
            if (!Enum.IsDefined(provisioning) || provisioning == GraphStorageProvisioningState.RepairRequired)
            {
                throw new GraphAccessUnavailableException("graph-storage-provisioning-repair-required");
            }

            if (provisioning == GraphStorageProvisioningState.Provisioned)
            {
                await InitializeProvisionedStoresAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
            await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphCoordinatorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (!_provisioned)
        {
            return UnprovisionedStatus();
        }

        return await GetProvisionedStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GraphControlSettings> GetControlSettingsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (!_provisioned)
        {
            return new GraphControlSettings();
        }

        var settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
        GraphResourceAdmissionPolicy.Validate(settings);
        return settings;
    }

    /// <inheritdoc />
    public async Task<GraphControlSettings> UpdateResourceSettingsAsync(
        GraphResourceControlUpdate update,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        EnsureProvisioned();
        ArgumentNullException.ThrowIfNull(update);
        var current = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (current.Revision != update.ExpectedRevision)
        {
            throw new GraphAccessUnavailableException("resource-settings-revision-stale");
        }

        var requested = current with
        {
            ResourceMode = update.ResourceMode,
            MaximumConcurrency = GraphResourceAdmissionPolicy.ConcurrencyFor(update.ResourceMode),
            ProcessOnlyWhileIdle = update.ProcessOnlyWhileIdle,
            ProcessOnlyWhileConnectedToPower = update.ProcessOnlyWhileConnectedToPower,
            PauseBelowBatteryPercentage = update.PauseBelowBatteryPercentage,
            ProcessingWindowStartHour = update.ProcessingWindowStartHour,
            ProcessingWindowEndHour = update.ProcessingWindowEndHour,
        };
        GraphResourceAdmissionPolicy.Validate(requested);
        var saved = await _decisionStore.SetControlSettingsAsync(
            requested,
            current.Revision,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        GraphResourceAdmissionPolicy.Validate(saved);
        return saved;
    }

    /// <inheritdoc />
    public async Task<GraphMaintenanceResult> MaintainAsync(
        GraphMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        EnsureProvisioned();
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Trigger) ||
            request.MaximumStorageSizeBytes is < GraphLimits.MinimumStorageQuotaBytes or > GraphLimits.MaximumStorageQuotaBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
            GraphResourceAdmissionPolicy.Validate(settings);
            if (!settings.IsEnabled)
            {
                throw new GraphAccessUnavailableException("graph-disabled");
            }

            var eligibility = await _resourceAdmissionPolicy.GetEligibilityAsync(settings, cancellationToken).ConfigureAwait(false);
            if (!eligibility.MayProcess)
            {
                throw new GraphAccessUnavailableException("graph-maintenance-resource-policy");
            }

            var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!status.IsEnabled)
            {
                throw new GraphAccessUnavailableException("graph-disabled");
            }

            if (status.RunningCount > 0 || status.RunControl is GraphRunControlState.PauseRequested or
                GraphRunControlState.CancelRequested)
            {
                throw new GraphAccessUnavailableException("graph-maintenance-active-work");
            }

            var result = await _store
                .MaintainAsync(request, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            ValidateMaintenanceResult(result);
            await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> EnableAsync(bool consentConfirmed, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (!consentConfirmed)
        {
            throw new InvalidOperationException("Knowledge Graph enablement requires explicit local consent.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_provisioned)
            {
                await _storageLifecycle.ProvisionAsync(cancellationToken).ConfigureAwait(false);
                var provisioned = await _storageLifecycle.GetProvisioningStateAsync(cancellationToken).ConfigureAwait(false);
                if (provisioned != GraphStorageProvisioningState.Provisioned)
                {
                    throw new GraphAccessUnavailableException("graph-storage-provisioning-incomplete");
                }

                await InitializeProvisionedStoresAsync(cancellationToken).ConfigureAwait(false);
            }

            var settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
            var saved = await _decisionStore.SetControlSettingsAsync(
                settings with { IsEnabled = true, ConsentConfirmed = true },
                settings.Revision,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            var result = await _store
                .SetEnabledAsync(saved.IsEnabled, saved.ConsentConfirmed, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> DisableAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (!_provisioned)
        {
            return new GraphOperationResult(true, "Knowledge Graph is already disabled and no sidecars were created.", 0);
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
            var saved = await _decisionStore.SetControlSettingsAsync(
                settings with { IsEnabled = false },
                settings.Revision,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            var result = await _store
                .SetEnabledAsync(saved.IsEnabled, saved.ConsentConfirmed, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_provisioned)
            {
                return new GraphOperationResult(false, "Knowledge Graph is disabled and unprovisioned.", 0);
            }
            var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            var controlSettings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
            GraphResourceAdmissionPolicy.Validate(controlSettings);
            if (!controlSettings.IsEnabled || !status.IsEnabled)
            {
                return new GraphOperationResult(false, "Knowledge Graph is disabled.", 0);
            }

            if (status.RunControl == GraphRunControlState.PauseRequested)
            {
                return await RequestControlAsync(
                    GraphRunControlState.Paused,
                    "pause-acknowledged-before-reconciliation",
                    cancellationToken).ConfigureAwait(false);
            }

            if (status.RunControl == GraphRunControlState.CancelRequested)
            {
                return await RequestControlAsync(
                    GraphRunControlState.Cancelled,
                    "cancel-acknowledged-before-reconciliation",
                    cancellationToken).ConfigureAwait(false);
            }

            if (status.RunControl == GraphRunControlState.Paused ||
                (status.RunControl == GraphRunControlState.Cancelled && status.PendingCount == 0))
            {
                return new GraphOperationResult(true, "Graph projection remains suspended by user control.", 0);
            }

            var snapshot = await _source.OpenCompletedSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var decisions = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(decisions))
            {
                throw new GraphAccessUnavailableException("decision-store-invalid");
            }

            snapshot = snapshot with
            {
                GraphDecisionSequence = decisions.Sequence,
                GraphDecisionCheckpointId = decisions.CheckpointId,
            };
            ValidateSnapshot(snapshot);
            var run = await _store
                .BeginProjectionAsync(snapshot, _ownerInstanceId, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            var legacyMirrorStage = await SynchronizeLegacyDecisionMirrorAsync(run, cancellationToken).ConfigureAwait(false);
            if (legacyMirrorStage is not null)
            {
                await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
                return legacyMirrorStage;
            }

            if (!run.InputManifestComplete)
            {
                var capture = await CaptureManifestAsync(run, cancellationToken).ConfigureAwait(false);
                if (capture is not null)
                {
                    await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
                    return capture;
                }
            }

            var decisionStage = await StageDecisionLedgerAsync(run, decisions, cancellationToken).ConfigureAwait(false);
            if (decisionStage is not null)
            {
                await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
                return decisionStage;
            }

            var processed = await ProcessClaimsAsync(run, cancellationToken).ConfigureAwait(false);
            await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
            return processed;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        EnsureProvisioned();
        var current = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (current.RunControl == GraphRunControlState.Paused)
        {
            return new GraphOperationResult(true, "Graph projection is already paused.", 0);
        }

        if (current.RunControl is GraphRunControlState.Cancelled or GraphRunControlState.Complete)
        {
            return new GraphOperationResult(true, "The terminal graph run has no active work to pause.", 0);
        }

        var requested = await RequestControlAsync(GraphRunControlState.PauseRequested, "user-pause", cancellationToken)
            .ConfigureAwait(false);
        var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.RunningCount == 0
            ? await RequestControlAsync(GraphRunControlState.Paused, "pause-acknowledged", cancellationToken).ConfigureAwait(false)
            : requested;
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> ResumeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        EnsureProvisioned();
        var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.RunControl == GraphRunControlState.Running)
        {
            return new GraphOperationResult(true, "Graph projection is already running.", 0);
        }

        if (status.RunControl == GraphRunControlState.Cancelled)
        {
            throw new InvalidOperationException("A cancelled graph run must be retried as new durable work, not resumed in place.");
        }

        return await RequestControlAsync(GraphRunControlState.Running, "user-resume", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> CancelAsync(string reasonCode, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        EnsureProvisioned();
        GraphQueryService.ValidateBounded(reasonCode, 64, allowEmpty: false);
        var requested = await RequestControlAsync(GraphRunControlState.CancelRequested, reasonCode, cancellationToken)
            .ConfigureAwait(false);
        var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.RunningCount == 0
            ? await RequestControlAsync(GraphRunControlState.Cancelled, "cancel-acknowledged", cancellationToken).ConfigureAwait(false)
            : requested;
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> RetryAsync(string? workId = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        EnsureProvisioned();
        if (workId is not null)
        {
            GraphQueryService.ValidateId(workId);
        }

        var result = await _store
            .RetryFailedAsync(workId, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_initialized)
        {
            return;
        }

        if (!_provisioned)
        {
            return;
        }

        if (gracePeriod < TimeSpan.Zero || gracePeriod > GraphLimits.ClaimLeaseTimeToLive)
        {
            throw new ArgumentOutOfRangeException(nameof(gracePeriod));
        }

        var initialStatus = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if ((initialStatus.RunControl is GraphRunControlState.Paused or
             GraphRunControlState.Cancelled or GraphRunControlState.Complete) &&
            initialStatus.RunningCount == 0)
        {
            return;
        }

        await PauseAsync(cancellationToken).ConfigureAwait(false);
        var deadline = _timeProvider.GetUtcNow() + gracePeriod;
        while (_timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.RunningCount == 0)
            {
                if (status.RunControl == GraphRunControlState.PauseRequested)
                {
                    await RequestControlAsync(GraphRunControlState.Paused, "shutdown-boundary", cancellationToken)
                        .ConfigureAwait(false);
                }

                return;
            }

            var remaining = deadline - _timeProvider.GetUtcNow();
            await Task.Delay(
                remaining < GraphLimits.HeartbeatInterval ? remaining : GraphLimits.HeartbeatInterval,
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        var failures = new List<Exception>();
        if (_initialized)
        {
            using var cancellation = new CancellationTokenSource(GraphLimits.ShutdownGracePeriod);
            try
            {
                await StopAsync(GraphLimits.ShutdownGracePeriod, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Claims remain durable and are recovered by fencing on the next startup.
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        _disposed = true;
        await DisposeOwnedAsync(_storageLifecycle, failures).ConfigureAwait(false);
        await DisposeOwnedAsync(_source, failures).ConfigureAwait(false);
        await DisposeOwnedAsync(_store, failures).ConfigureAwait(false);
        await DisposeOwnedAsync(_decisionStore, failures).ConfigureAwait(false);
        _operationGate.Dispose();
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private static async Task DisposeOwnedAsync(IAsyncDisposable owned, List<Exception> failures)
    {
        try
        {
            await owned.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private async Task<GraphOperationResult?> CaptureManifestAsync(
        GraphProjectionRun run,
        CancellationToken cancellationToken)
    {
        await SetRunStageAndPublishAsync(run, "capturing-manifest", "Source observation page", cancellationToken)
            .ConfigureAwait(false);
        GraphProjectionCursor? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var perKind = new Dictionary<GraphProjectionObservationKind, long>();
        long observed = 0;
        long expectedPageSequence = 0;
        do
        {
            var resourceWait = await CheckResourceAdmissionAsync(run, cancellationToken).ConfigureAwait(false);
            if (resourceWait is not null)
            {
                return resourceWait;
            }

            var control = await ObserveControlBoundaryAsync(run, cancellationToken).ConfigureAwait(false);
            if (control is not null)
            {
                return control;
            }

            var page = await _source
                .ReadPageAsync(run.Snapshot, cursor, GraphLimits.MaximumProjectionPageSize, cancellationToken)
                .ConfigureAwait(false);
            ValidatePage(page, run.Snapshot, expectedPageSequence);
            await _store
                .QueueProjectionPageAsync(run, page, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            observed += page.ObservationCount;
            foreach (var group in page.Observations.GroupBy(item => item.Kind))
            {
                perKind[group.Key] = perKind.GetValueOrDefault(group.Key) + group.LongCount();
            }

            expectedPageSequence++;
            cursor = page.NextCursor;
            if (cursor is not null && !seenCursors.Add(cursor.Value))
            {
                throw new InvalidDataException("The graph projection source repeated a continuation cursor.");
            }

            if (!page.IsLastPage && cursor is null)
            {
                throw new InvalidDataException("A non-terminal graph page omitted its continuation cursor.");
            }

            await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        while (cursor is not null);

        if (observed != run.Snapshot.TotalObservationCount ||
            run.Snapshot.ObservationCounts.Any(expected => perKind.GetValueOrDefault(expected.Kind) != expected.Count))
        {
            throw new InvalidDataException("The completed graph source manifest terminal counts do not match its pages.");
        }

        await _store
            .CompleteInputManifestAsync(run, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        return null;
    }

    private async Task<GraphOperationResult?> SynchronizeLegacyDecisionMirrorAsync(
        GraphProjectionRun run,
        CancellationToken cancellationToken)
    {
        var currentManifest = await _decisionStore
            .GetLegacyMirrorManifestIdAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(currentManifest, run.Snapshot.LegacyDecisionManifestId, StringComparison.Ordinal))
        {
            return null;
        }

        var expectedLegacyCount = run.Snapshot.ObservationCounts
            .Where(item => item.Kind == GraphProjectionObservationKind.LegacyDecision)
            .Sum(item => item.Count);
        if (expectedLegacyCount is < 0 or > GraphLimits.MaximumLegacyDecisionMirrorRows)
        {
            throw new InvalidDataException("The completed legacy-decision manifest exceeds the stable mirror ceiling.");
        }

        await SetRunStageAndPublishAsync(
            run,
            "mirroring-legacy-decisions",
            "Schema-3 decision page",
            cancellationToken).ConfigureAwait(false);
        await _decisionStore.BeginLegacyMirrorAsync(
            run.Snapshot.LegacyDecisionManifestId,
            expectedLegacyCount,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

        GraphProjectionCursor? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var perKind = new Dictionary<GraphProjectionObservationKind, long>();
        long observed = 0;
        long mirrored = 0;
        long pageSequence = 0;
        do
        {
            var resourceWait = await CheckResourceAdmissionAsync(run, cancellationToken).ConfigureAwait(false);
            if (resourceWait is not null)
            {
                return resourceWait;
            }

            var control = await ObserveControlBoundaryAsync(run, cancellationToken).ConfigureAwait(false);
            if (control is not null)
            {
                return control;
            }

            var page = await _source
                .ReadPageAsync(run.Snapshot, cursor, GraphLimits.MaximumProjectionPageSize, cancellationToken)
                .ConfigureAwait(false);
            ValidatePage(page, run.Snapshot, pageSequence);
            var decisions = page.Observations
                .OfType<GraphLegacyDecisionObservation>()
                .OrderBy(item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            await _decisionStore.StageLegacyMirrorPageAsync(
                run.Snapshot.LegacyDecisionManifestId,
                pageSequence,
                decisions,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);

            observed += page.ObservationCount;
            mirrored += decisions.LongLength;
            foreach (var group in page.Observations.GroupBy(item => item.Kind))
            {
                perKind[group.Key] = perKind.GetValueOrDefault(group.Key) + group.LongCount();
            }

            pageSequence++;
            cursor = page.NextCursor;
            if (cursor is not null && !seenCursors.Add(cursor.Value))
            {
                throw new InvalidDataException("The graph projection source repeated a continuation cursor.");
            }

            if (!page.IsLastPage && cursor is null)
            {
                throw new InvalidDataException("A non-terminal graph page omitted its continuation cursor.");
            }

            await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        while (cursor is not null);

        if (observed != run.Snapshot.TotalObservationCount || mirrored != expectedLegacyCount ||
            run.Snapshot.ObservationCounts.Any(expected => perKind.GetValueOrDefault(expected.Kind) != expected.Count))
        {
            throw new InvalidDataException("The completed legacy-decision mirror scan did not match its immutable source manifest.");
        }

        await _decisionStore.CompleteLegacyMirrorAsync(
            run.Snapshot.LegacyDecisionManifestId,
            expectedLegacyCount,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async Task<GraphOperationResult?> StageDecisionLedgerAsync(
        GraphProjectionRun run,
        GraphDecisionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await SetRunStageAndPublishAsync(run, "replaying-decisions", "Decision ledger page", cancellationToken)
            .ConfigureAwait(false);
        const int pageSize = GraphLimits.MaximumPageSize;
        var coverage = await _store.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
        if (coverage.AppliedDecisionSequence == snapshot.Sequence &&
            string.Equals(coverage.AppliedDecisionCheckpointId, snapshot.CheckpointId, StringComparison.Ordinal))
        {
            return null;
        }

        long afterSequence = string.Equals(
            coverage.IngestedDecisionCheckpointId,
            snapshot.CheckpointId,
            StringComparison.Ordinal)
            ? Math.Clamp(coverage.IngestedDecisionSequence, 0, snapshot.Sequence)
            : 0;
        if (snapshot.Sequence == 0)
        {
            await _store.ApplyDecisionProjectionPageAsync(
                run,
                snapshot,
                [],
                true,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (afterSequence == snapshot.Sequence)
        {
            await _store.ApplyDecisionProjectionPageAsync(
                run,
                snapshot,
                [],
                true,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return null;
        }

        while (afterSequence < snapshot.Sequence)
        {
            var resourceWait = await CheckResourceAdmissionAsync(run, cancellationToken).ConfigureAwait(false);
            if (resourceWait is not null)
            {
                return resourceWait;
            }

            var control = await ObserveControlBoundaryAsync(run, cancellationToken).ConfigureAwait(false);
            if (control is not null)
            {
                return control;
            }

            var entries = await _decisionStore.ReadAsync(afterSequence, pageSize, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0 || entries.Count > pageSize)
            {
                throw new InvalidDataException("The graph-native decision ledger ended before its validated checkpoint.");
            }

            var projections = new List<GraphDecisionProjection>(entries.Count);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Sequence != afterSequence + 1 || entry.Sequence > snapshot.Sequence)
                {
                    throw new InvalidDataException("The graph-native decision ledger contains a sequence gap or crossed its checkpoint.");
                }

                projections.Add(_decisionProjectionBuilder.Build(entry, snapshot, _timeProvider.GetUtcNow()));
                afterSequence = entry.Sequence;
            }

            await _store.ApplyDecisionProjectionPageAsync(
                run,
                snapshot,
                projections,
                afterSequence == snapshot.Sequence,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<GraphOperationResult> ProcessClaimsAsync(
        GraphProjectionRun run,
        CancellationToken cancellationToken)
    {
        var committed = 0;
        while (true)
        {
            var resourceWait = await CheckResourceAdmissionAsync(run, cancellationToken).ConfigureAwait(false);
            if (resourceWait is not null)
            {
                return resourceWait with { AffectedCount = committed };
            }

            var control = await ObserveControlBoundaryAsync(run, cancellationToken).ConfigureAwait(false);
            if (control is not null)
            {
                return control with { AffectedCount = committed };
            }

            if (!await ValidateRunAuthorityAsync(run, [], cancellationToken).ConfigureAwait(false))
            {
                await FenceChangedAuthorityAsync(run, cancellationToken).ConfigureAwait(false);
                return new GraphOperationResult(false, "Projection inputs changed; a newer reconciliation is required.", committed);
            }

            var settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
            GraphResourceAdmissionPolicy.Validate(settings);
            if (!settings.IsEnabled)
            {
                return new GraphOperationResult(false, "Knowledge Graph is disabled.", committed);
            }

            var claims = await ClaimBatchAsync(run, settings.MaximumConcurrency, cancellationToken).ConfigureAwait(false);
            if (claims.Count == 0)
            {
                await SetRunStageAndPublishAsync(
                    run,
                    "stale-row-cleanup",
                    "Retiring superseded graph records",
                    cancellationToken).ConfigureAwait(false);
                return await _store
                    .CompleteProjectionAsync(run, _timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
            }

            var workLabel = claims.Count == 1
                ? string.Concat(claims[0].WorkItem.Observation.Kind.ToString(), " observation")
                : string.Concat(claims.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), " bounded observations");
            await SetRunStageAndPublishAsync(run, "projecting-graph", workLabel, cancellationToken)
                .ConfigureAwait(false);

            var builds = await Task.WhenAll(claims.Select(claim => BuildClaimSafelyAsync(claim, run, cancellationToken)))
                .ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                await CancelClaimsAsync(claims, "projection-cancelled", "Projection cancellation was acknowledged at its latest durable stage boundary.")
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var index = 0; index < builds.Length; index++)
            {
                var build = builds[index];
                var claim = build.Claim;
                if (build.Exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    await CancelClaimsAsync(
                            claims.Skip(index),
                            "projection-cancelled",
                            "Projection cancellation was acknowledged at its latest durable stage boundary.")
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (build.Exception is GraphClaimFencedException fencedBuild)
                {
                    await CancelClaimsAsync(
                            claims.Skip(index),
                            "projection-claim-fenced",
                            "Projection ownership changed; the durable provider will recover or retry the work.",
                            fencedBuild.GetType().Name)
                        .ConfigureAwait(false);
                    return new GraphOperationResult(false, "Projection ownership changed before publication; no stale output was committed.", committed);
                }

                if (build.Exception is not null)
                {
                    await RecordClaimExceptionAsync(claim, build.Exception, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var projection = build.Projection!;
                    claim = await AdvancePureProjectionStagesAsync(claim, projection.InputFingerprint, cancellationToken)
                        .ConfigureAwait(false);
                    if (!await ValidateRunAuthorityAsync(
                            run,
                            [claim.WorkItem.Observation.StableKey],
                            cancellationToken).ConfigureAwait(false))
                    {
                        await CancelClaimsAsync(
                                claims.Skip(index),
                                "authority-changed",
                                "Projection inputs changed before publication.",
                                "AuthorityChanged")
                            .ConfigureAwait(false);
                        await FenceChangedAuthorityAsync(run, cancellationToken).ConfigureAwait(false);
                        return new GraphOperationResult(false, "Projection inputs changed before publication; no stale output was committed.", committed);
                    }

                    var published = await _store
                        .CommitClaimAsync(claim, projection, _timeProvider.GetUtcNow(), cancellationToken)
                        .ConfigureAwait(false);
                    if (!published)
                    {
                        throw new GraphClaimFencedException("The graph provider rejected an obsolete projection fencing token.");
                    }

                    committed++;
                    await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await CancelClaimsAsync(
                            claims.Skip(index),
                            "projection-cancelled",
                            "Projection cancellation was acknowledged at its latest durable stage boundary.")
                        .ConfigureAwait(false);
                    throw;
                }
                catch (GraphClaimFencedException exception)
                {
                    await CancelClaimsAsync(
                            claims.Skip(index),
                            "projection-claim-fenced",
                            "Projection ownership changed; the durable provider will recover or retry the work.",
                            exception.GetType().Name)
                        .ConfigureAwait(false);
                    return new GraphOperationResult(false, "Projection ownership changed before publication; no stale output was committed.", committed);
                }
                catch (Exception exception)
                {
                    await RecordClaimExceptionAsync(claim, exception, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task<IReadOnlyList<GraphProjectionClaim>> ClaimBatchAsync(
        GraphProjectionRun run,
        int maximumConcurrency,
        CancellationToken cancellationToken)
    {
        if (maximumConcurrency is < 1 or > GraphLimits.MaximumWorkerConcurrency)
        {
            throw new InvalidDataException("Graph worker concurrency exceeds the stable bounded policy.");
        }

        var claims = new List<GraphProjectionClaim>(maximumConcurrency);
        while (claims.Count < maximumConcurrency)
        {
            var claim = await _store
                .TryClaimNextAsync(
                    run,
                    _ownerInstanceId,
                    _timeProvider.GetUtcNow(),
                    GraphLimits.ClaimLeaseTimeToLive,
                    cancellationToken)
                .ConfigureAwait(false);
            if (claim is null)
            {
                break;
            }

            claims.Add(claim);
        }

        return claims;
    }

    private async Task<GraphClaimBuildResult> BuildClaimSafelyAsync(
        GraphProjectionClaim claim,
        GraphProjectionRun run,
        CancellationToken cancellationToken)
    {
        try
        {
            return new GraphClaimBuildResult(
                claim,
                await BuildWithHeartbeatAsync(claim, run, cancellationToken).ConfigureAwait(false),
                null);
        }
        catch (Exception exception)
        {
            return new GraphClaimBuildResult(claim, null, exception);
        }
    }

    private async Task RecordClaimExceptionAsync(
        GraphProjectionClaim claim,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var disposition = exception switch
        {
            GraphPersistenceException persistence => persistence.Disposition,
            IOException or TimeoutException => GraphPersistenceFailureDisposition.Retryable,
            _ => GraphPersistenceFailureDisposition.Permanent,
        };
        var retryable = disposition == GraphPersistenceFailureDisposition.Retryable &&
                        claim.WorkItem.Attempt < GraphLimits.MaximumRetryCount;
        var state = disposition == GraphPersistenceFailureDisposition.WaitingForResources
            ? GraphJobExecutionState.WaitingForResources
            : retryable
                ? GraphJobExecutionState.RetryableFailure
                : GraphJobExecutionState.PermanentFailure;
        await _store.RecordClaimFailureAsync(
            claim,
            Failure(exception, state),
            state,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CancelClaimsAsync(
        IEnumerable<GraphProjectionClaim> claims,
        string errorCode,
        string message,
        string category = "OperationCanceledException")
    {
        using var cancellation = new CancellationTokenSource(GraphLimits.ShutdownGracePeriod);
        foreach (var claim in claims)
        {
            await _store.RecordClaimFailureAsync(
                claim,
                new GraphProjectionFailure(category, errorCode, true, message),
                GraphJobExecutionState.Cancelled,
                _timeProvider.GetUtcNow(),
                cancellation.Token).ConfigureAwait(false);
        }
    }

    private async Task<GraphProjectionClaim> AdvancePureProjectionStagesAsync(
        GraphProjectionClaim claim,
        string inputFingerprint,
        CancellationToken cancellationToken)
    {
        GraphQueryService.ValidateId(inputFingerprint);
        var orderedStages = new[]
        {
            GraphProjectionStage.ObservationCaptured,
            GraphProjectionStage.CandidatesExtracted,
            GraphProjectionStage.CandidatesNormalized,
            GraphProjectionStage.IdentityResolved,
            GraphProjectionStage.EdgesPrepared,
            GraphProjectionStage.ComponentValidated,
        };
        var currentIndex = Array.IndexOf(orderedStages, claim.WorkItem.Stage);
        if (currentIndex < 0 ||
            currentIndex > 0 && !string.Equals(claim.WorkItem.StageInputFingerprint, inputFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The claimed projection stage or its input fingerprint is invalid.");
        }

        for (var nextIndex = currentIndex + 1; nextIndex < orderedStages.Length; nextIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transition = new GraphProjectionStageTransition(
                orderedStages[nextIndex - 1],
                orderedStages[nextIndex],
                inputFingerprint);
            var advanced = await _store.AdvanceClaimStageAsync(
                claim,
                transition,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            if (advanced is null || advanced.ClaimToken != claim.ClaimToken ||
                advanced.FencingEpoch != claim.FencingEpoch || advanced.WorkItem.WorkId != claim.WorkItem.WorkId ||
                advanced.WorkItem.Stage != transition.CompletedStage ||
                !string.Equals(advanced.WorkItem.StageInputFingerprint, inputFingerprint, StringComparison.Ordinal))
            {
                throw new GraphClaimFencedException("The graph provider rejected a pure-stage checkpoint.");
            }

            claim = advanced;
        }

        return claim;
    }

    private async Task<GraphComponentProjection> BuildWithHeartbeatAsync(
        GraphProjectionClaim claim,
        GraphProjectionRun run,
        CancellationToken cancellationToken)
    {
        using var buildCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buildTask = Task.Run(
            () => _builder.Build(
                claim.WorkItem.Observation,
                run.Snapshot,
                _timeProvider.GetUtcNow(),
                buildCancellation.Token),
            CancellationToken.None);

        try
        {
            while (!buildTask.IsCompleted)
            {
                using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeat = _heartbeatScheduler.WaitForHeartbeatAsync(heartbeatCancellation.Token);
                if (await Task.WhenAny(buildTask, heartbeat).ConfigureAwait(false) == buildTask)
                {
                    heartbeatCancellation.Cancel();
                    try
                    {
                        await heartbeat.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested)
                    {
                        // The losing scheduler wait is cancelled and observed before the next claim.
                    }

                    break;
                }

                await heartbeat.ConfigureAwait(false);
                if (buildTask.IsCompleted)
                {
                    break;
                }

                var renewed = await _store.RenewClaimAsync(
                    claim,
                    _timeProvider.GetUtcNow(),
                    GraphLimits.ClaimLeaseTimeToLive,
                    cancellationToken).ConfigureAwait(false);
                if (!renewed)
                {
                    throw new GraphClaimFencedException("The graph provider rejected an obsolete projection lease heartbeat.");
                }
            }

            return await buildTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!buildTask.IsCompleted)
            {
                buildCancellation.Cancel();
                try
                {
                    await buildTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (buildCancellation.IsCancellationRequested)
                {
                    // Cooperative builder cancellation is the expected response to an outer failure.
                }
                catch (Exception buildException)
                {
                    throw new AggregateException(exception, buildException);
                }
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    private async Task<GraphOperationResult?> CheckResourceAdmissionAsync(
        GraphProjectionRun run,
        CancellationToken cancellationToken)
    {
        var settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
        GraphResourceAdmissionPolicy.Validate(settings);
        if (!settings.IsEnabled)
        {
            return new GraphOperationResult(false, "Knowledge Graph is disabled.", 0);
        }

        var eligibility = await _resourceAdmissionPolicy.GetEligibilityAsync(settings, cancellationToken).ConfigureAwait(false);
        if (eligibility.MayProcess)
        {
            return null;
        }

        var reason = string.IsNullOrWhiteSpace(eligibility.WaitingReason)
            ? "resource-policy"
            : eligibility.WaitingReason;
        GraphQueryService.ValidateBounded(reason, 128, allowEmpty: false);
        return await _store
            .SetResourceWaitingAsync(run, reason, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<GraphOperationResult?> ObserveControlBoundaryAsync(
        GraphProjectionRun run,
        CancellationToken cancellationToken)
    {
        var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsEnabled)
        {
            return new GraphOperationResult(false, "Knowledge Graph was disabled at a durable boundary.", 0);
        }

        if (status.RunControl == GraphRunControlState.PauseRequested)
        {
            await RequestControlAsync(GraphRunControlState.Paused, "pause-acknowledged", cancellationToken).ConfigureAwait(false);
            return new GraphOperationResult(true, "Graph projection paused at a durable boundary.", 0);
        }

        if (status.RunControl == GraphRunControlState.CancelRequested)
        {
            await RequestControlAsync(GraphRunControlState.Cancelled, "cancel-acknowledged", cancellationToken).ConfigureAwait(false);
            return new GraphOperationResult(true, "Graph projection cancellation was durably acknowledged.", 0);
        }

        if (status.RunControl == GraphRunControlState.Paused)
        {
            return new GraphOperationResult(true, "Graph projection remains paused.", 0);
        }

        return null;
    }

    private async Task<bool> ValidateRunAuthorityAsync(
        GraphProjectionRun run,
        IReadOnlyList<string> stableKeys,
        CancellationToken cancellationToken)
    {
        var authority = await _source
            .ValidateAuthorityAsync(new GraphAuthorityRequest(stableKeys, "projection-publish"), cancellationToken)
            .ConfigureAwait(false);
        GraphDecisionSnapshot decisions;
        GraphControlSettings settings;
        try
        {
            decisions = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
            GraphResourceAdmissionPolicy.Validate(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        return settings.IsEnabled && GraphBoundaryValidator.IsValid(authority) && authority.IsAvailable && authority.IsAllowed &&
            GraphBoundaryValidator.IsValid(decisions) && decisions.Sequence == run.Snapshot.GraphDecisionSequence &&
            string.Equals(decisions.CheckpointId, run.Snapshot.GraphDecisionCheckpointId, StringComparison.Ordinal) &&
            authority.PrivacySequence == run.Snapshot.PrivacySequence &&
            string.Equals(authority.LegacyDecisionManifestId, run.Snapshot.LegacyDecisionManifestId, StringComparison.Ordinal) &&
            string.Equals(authority.CurrentSourceManifestId, run.Snapshot.ManifestId, StringComparison.Ordinal) &&
            authority.CurrentSourceRevision == run.Snapshot.Revision;
    }

    private async Task FenceChangedAuthorityAsync(GraphProjectionRun run, CancellationToken cancellationToken)
    {
        await _store.SetRunControlAsync(
            new GraphRunControlRequest(run.RunId, GraphRunControlState.CancelRequested, "authority-changed", _timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        await _store.SetRunControlAsync(
            new GraphRunControlRequest(run.RunId, GraphRunControlState.Cancelled, "authority-change-acknowledged", _timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GraphOperationResult> RequestControlAsync(
        GraphRunControlState requested,
        string reason,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var result = await _store.SetRunControlAsync(
            new GraphRunControlRequest(status.RunId, requested, reason, _timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task PublishStatusAsync(CancellationToken cancellationToken)
    {
        var status = !_provisioned
            ? UnprovisionedStatus()
            : await GetProvisionedStatusAsync(cancellationToken).ConfigureAwait(false);
        StatusChanged?.Invoke(this, status);
    }

    private async Task SetRunStageAndPublishAsync(
        GraphProjectionRun run,
        string stageCode,
        string? currentWorkLabel,
        CancellationToken cancellationToken)
    {
        await _store.SetRunStageAsync(
            run,
            new GraphRunStageUpdate(stageCode, currentWorkLabel),
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        await PublishStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<GraphCoordinatorStatus> GetProvisionedStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var storage = await _storageLifecycle.GetStorageBreakdownAsync(cancellationToken).ConfigureAwait(false);
        ValidateStorageBreakdown(storage);
        if (_storageLifecycle is AlwaysProvisionedGraphStorageLifecycle && storage.TotalBytes == 0 &&
            (status.StorageSizeBytes > 0 || status.MaximumStorageSizeBytes > 0))
        {
            storage = new GraphStorageBreakdown
            {
                DerivedStoreBytes = status.StorageSizeBytes,
                TotalBytes = status.StorageSizeBytes,
                MaximumBytes = status.MaximumStorageSizeBytes,
                IsInventoryVerified = true,
            };
            ValidateStorageBreakdown(storage);
        }

        return status with
        {
            IsProvisioned = true,
            StorageSizeBytes = storage.TotalBytes,
            MaximumStorageSizeBytes = storage.MaximumBytes == 0 ? status.MaximumStorageSizeBytes : storage.MaximumBytes,
            StorageBreakdown = storage,
        };
    }

    private async Task InitializeProvisionedStoresAsync(CancellationToken cancellationToken)
    {
        await _decisionStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var decisions = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(decisions))
        {
            throw new GraphAccessUnavailableException("decision-store-invalid");
        }

        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _store.RecoverAsync(_ownerInstanceId, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
        GraphResourceAdmissionPolicy.Validate(settings);
        await _store.SetEnabledAsync(
            settings.IsEnabled,
            settings.ConsentConfirmed,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        _provisioned = true;
    }

    private static GraphCoordinatorStatus UnprovisionedStatus() => new()
    {
        IsProvisioned = false,
        IsEnabled = false,
        RunControl = GraphRunControlState.Paused,
        Coverage = new GraphProjectionCoverage(
            false,
            false,
            false,
            false,
            0,
            0,
            0,
            0,
            null,
            0,
            "Knowledge Graph is disabled and has not created local sidecars."),
        Message = "Enable Knowledge Graph to create its local sidecars.",
    };

    private static GraphProjectionFailure Failure(Exception exception, GraphJobExecutionState state) => new(
        exception.GetType().Name,
        exception is GraphPersistenceException persistence
            ? persistence.ReasonCode
            : state == GraphJobExecutionState.RetryableFailure
                ? "projection-transient"
                : "projection-invalid-input",
        state == GraphJobExecutionState.RetryableFailure,
        state switch
        {
            GraphJobExecutionState.RetryableFailure => "Projection will retry under the configured policy.",
            GraphJobExecutionState.WaitingForResources => "Projection is waiting for storage resources and may be retried after conditions improve.",
            _ => "Projection requires changed input or selective repair.",
        });

    private static void ValidateMaintenanceResult(GraphMaintenanceResult result)
    {
        if (result is null || result.RecordsRemoved < 0 || result.BytesBefore < 0 || result.BytesAfter < 0 ||
            result.CompletedAtUtc == default || string.IsNullOrWhiteSpace(result.Message) ||
            result.Message.Length > GraphLimits.MaximumDecisionReasonCharacters ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(result.Message))
        {
            throw new GraphAccessUnavailableException("graph-maintenance-result-invalid");
        }
    }

    internal static void ValidateStorageBreakdown(GraphStorageBreakdown storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (storage.DerivedStoreBytes < 0 || storage.DecisionLedgerBytes < 0 || storage.VerifiedBackupBytes < 0 ||
            storage.TotalBytes < 0 || storage.MaximumBytes < 0 || storage.RequiredReserveBytes < 0)
        {
            throw new GraphAccessUnavailableException("graph-storage-breakdown-invalid");
        }

        long expected;
        try
        {
            expected = checked(storage.DerivedStoreBytes + storage.DecisionLedgerBytes + storage.VerifiedBackupBytes);
        }
        catch (OverflowException)
        {
            throw new GraphAccessUnavailableException("graph-storage-breakdown-invalid");
        }

        if (!storage.IsInventoryVerified || expected != storage.TotalBytes ||
            storage.MaximumBytes is > 0 and < GraphLimits.MinimumStorageQuotaBytes ||
            storage.MaximumBytes > GraphLimits.MaximumStorageQuotaBytes ||
            storage.RequiredReserveBytes > storage.MaximumBytes && storage.MaximumBytes > 0)
        {
            throw new GraphAccessUnavailableException("graph-storage-breakdown-invalid");
        }
    }

    private static void ValidateSnapshot(GraphProjectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ObservationCounts is null || snapshot.TotalObservationCount < 0 ||
            snapshot.Revision < 0 || snapshot.PrivacySequence < 0 ||
            snapshot.GraphDecisionSequence < 0 || snapshot.ObservationCounts.Count > 16 ||
            snapshot.ObservationCounts.Any(item => item.Count < 0 || !Enum.IsDefined(item.Kind)) ||
            snapshot.ObservationCounts.Select(item => item.Kind).Distinct().Count() != snapshot.ObservationCounts.Count ||
            snapshot.ObservationCounts.Sum(item => item.Count) != snapshot.TotalObservationCount ||
            string.IsNullOrWhiteSpace(snapshot.ManifestId) ||
            string.IsNullOrWhiteSpace(snapshot.LegacyDecisionManifestId) ||
            string.IsNullOrWhiteSpace(snapshot.CanonicalManifestHash) ||
            string.IsNullOrWhiteSpace(snapshot.GraphDecisionCheckpointId) ||
            snapshot.ManifestId.Length > GraphLimits.MaximumStableIdCharacters ||
            snapshot.LegacyDecisionManifestId.Length > GraphLimits.MaximumStableIdCharacters ||
            snapshot.CanonicalManifestHash.Length > GraphLimits.MaximumStableIdCharacters ||
            snapshot.GraphDecisionCheckpointId.Length > GraphLimits.MaximumStableIdCharacters ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(snapshot.ManifestId) ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(snapshot.LegacyDecisionManifestId) ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(snapshot.CanonicalManifestHash) ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(snapshot.GraphDecisionCheckpointId))
        {
            throw new InvalidDataException("The projection source did not provide a valid completed manifest.");
        }
    }

    private static void ValidatePage(
        GraphProjectionPage page,
        GraphProjectionSnapshot snapshot,
        long expectedSequence)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Observations is null || !string.Equals(page.ManifestId, snapshot.ManifestId, StringComparison.Ordinal) ||
            page.SnapshotRevision != snapshot.Revision || page.PageSequence != expectedSequence ||
            page.ObservationCount != page.Observations.Count ||
            page.ObservationCount is < 0 or > GraphLimits.MaximumProjectionPageSize ||
            string.IsNullOrWhiteSpace(page.CanonicalPageHash) ||
            page.CanonicalPageHash.Length > GraphLimits.MaximumStableIdCharacters ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(page.CanonicalPageHash) ||
            (page.NextCursor is { } next &&
             (string.IsNullOrWhiteSpace(next.Value) ||
              next.Value.Length > GraphLimits.MaximumStableIdCharacters ||
              ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(next.Value))))
        {
            throw new InvalidDataException("A graph source page does not belong to the completed manifest or failed its bounds.");
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The graph coordinator has not been initialized.");
        }
    }

    private void EnsureProvisioned()
    {
        if (!_provisioned)
        {
            throw new GraphAccessUnavailableException("graph-storage-unprovisioned");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class GraphClaimFencedException(string message) : InvalidOperationException(message);

    private sealed record GraphClaimBuildResult(
        GraphProjectionClaim Claim,
        GraphComponentProjection? Projection,
        Exception? Exception);
}
