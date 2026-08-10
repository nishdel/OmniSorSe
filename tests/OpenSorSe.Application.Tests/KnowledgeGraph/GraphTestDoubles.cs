using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

internal sealed class FixedGraphTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FakeGraphLegacyAuthorityBridge : IGraphLegacyAuthorityBridge
{
    internal List<(string FirstFileId, string SecondFileId, bool PreventRegeneration)> RelationshipUnlinks { get; } = [];
    internal List<(string CollectionId, string FileId)> CollectionSplits { get; } = [];
    internal GraphOperationResult Result { get; set; } = new(true, "Legacy authority updated.", 1);

    public Task<GraphOperationResult> UnlinkRelationshipAsync(
        string firstFileId,
        string secondFileId,
        bool preventRegeneration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RelationshipUnlinks.Add((firstFileId, secondFileId, preventRegeneration));
        return Task.FromResult(Result);
    }

    public Task<GraphOperationResult> SplitCollectionMemberAsync(
        string collectionId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CollectionSplits.Add((collectionId, fileId));
        return Task.FromResult(Result);
    }
}

internal sealed class FakeGraphStorageLifecycle : IGraphStorageLifecycle, IGraphDerivedStoreRecoveryProvider
{
    internal GraphStorageProvisioningState State { get; set; } = GraphStorageProvisioningState.Unprovisioned;
    internal GraphStorageBreakdown Storage { get; set; } = GraphStorageBreakdown.Empty;
    internal int ProvisionCount { get; private set; }
    internal int StorageReadCount { get; private set; }
    internal int DerivedRecoveryCount { get; private set; }
    internal string? DerivedRecoveryConfirmation { get; private set; }
    internal GraphOperationResult DerivedRecoveryResult { get; set; } =
        new(true, "The derived graph store was recovered.", 1);
    internal int DisposeCount { get; private set; }

    public Task<GraphStorageProvisioningState> GetProvisioningStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(State);
    }

    public Task ProvisionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProvisionCount++;
        State = GraphStorageProvisioningState.Provisioned;
        return Task.CompletedTask;
    }

    public Task<GraphStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StorageReadCount++;
        return Task.FromResult(Storage);
    }

    public Task<GraphOperationResult> RecoverDerivedStoreAsync(
        string confirmationText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DerivedRecoveryCount++;
        DerivedRecoveryConfirmation = confirmationText;
        return Task.FromResult(DerivedRecoveryResult);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeGraphProjectionSource : IGraphProjectionSource
{
    private readonly IReadOnlyList<GraphProjectionObservation> _observations;

    internal FakeGraphProjectionSource(IReadOnlyList<GraphProjectionObservation>? observations = null)
    {
        _observations = observations ?? [];
        Snapshot = new GraphProjectionSnapshot(
            "manifest-1",
            1,
            "legacy-1",
            1,
            TestGraphData.Now,
            "manifest-hash-1",
            _observations.Count,
            _observations.GroupBy(item => item.Kind)
                .Select(group => new GraphObservationKindCount(group.Key, group.LongCount()))
                .OrderBy(item => item.Kind)
                .ToArray());
    }

    internal GraphProjectionSnapshot Snapshot { get; set; }
    internal GraphAuthoritySnapshot Authority { get; set; } = new(true, true, 1, "legacy-1", "allowed")
    {
        CurrentSourceManifestId = "manifest-1",
        CurrentSourceRevision = 1,
    };
    internal int ReadCount { get; private set; }
    internal int AuthorityCheckCount { get; private set; }
    internal int DisposeCount { get; private set; }
    internal Action<int>? AuthorityReadHook { get; set; }

    public Task<GraphProjectionSnapshot> OpenCompletedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Snapshot);
    }

    public Task<GraphProjectionPage> ReadPageAsync(
        GraphProjectionSnapshot snapshot,
        GraphProjectionCursor? cursor,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        var offset = cursor is null ? 0 : int.Parse(cursor.Value, System.Globalization.CultureInfo.InvariantCulture);
        var items = _observations.Skip(offset).Take(maximumCount).ToArray();
        var next = offset + items.Length;
        var isLast = next >= _observations.Count;
        return Task.FromResult(new GraphProjectionPage(
            snapshot.ManifestId,
            snapshot.Revision,
            offset / maximumCount,
            items.Length,
            string.Concat("page-hash-", offset),
            items,
            isLast ? null : new GraphProjectionCursor(next.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            isLast));
    }

    public Task<GraphAuthoritySnapshot> ValidateAuthorityAsync(
        GraphAuthorityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthorityCheckCount++;
        AuthorityReadHook?.Invoke(AuthorityCheckCount);
        return Task.FromResult(Authority);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeGraphDecisionStore : IGraphDecisionStore
{
    private readonly List<GraphDecisionEntry> _entries = [];
    private readonly SortedDictionary<long, IReadOnlyList<GraphLegacyDecisionObservation>> _legacyMirrorPages = [];
    private int _generation;
    private string? _stagingLegacyManifestId;
    private long _stagingLegacyExpectedCount;

    internal bool Initialized { get; private set; }
    internal bool IsValid { get; set; } = true;
    internal int ClearCount { get; private set; }
    internal int SnapshotReadCount { get; private set; }
    internal int ControlSettingsReadCount { get; private set; }
    internal int DisposeCount { get; private set; }
    internal Action<int>? SnapshotReadHook { get; set; }
    internal Action<int>? ControlSettingsReadHook { get; set; }
    internal IReadOnlyList<GraphDecisionEntry> Entries => _entries;
    internal string? LegacyMirrorManifestId { get; private set; }
    internal int LegacyMirrorPublishCount { get; private set; }
    internal bool FailNextLegacyMirrorPageAfterStage { get; set; }
    internal List<GraphDecisionRecoveryPoint> RecoveryPoints { get; } = [];
    internal string? RestoredRecoveryPointId { get; private set; }
    internal GraphOperationResult RestoreResult { get; set; } = new(true, "restored", 1);
    internal GraphControlSettings ControlSettings { get; set; } = new()
    {
        IsEnabled = true,
        ConsentConfirmed = true,
    };

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Initialized = true;
        return Task.CompletedTask;
    }

    public Task<GraphDecisionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SnapshotReadCount++;
        SnapshotReadHook?.Invoke(SnapshotReadCount);
        return Task.FromResult(new GraphDecisionSnapshot(
            _entries.Count,
            string.Concat("checkpoint-", _generation, "-", _entries.Count),
            string.Concat("decision-hash-", _generation, "-", _entries.Count),
            IsValid));
    }

    public Task<GraphControlSettings> GetControlSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ControlSettingsReadCount++;
        ControlSettingsReadHook?.Invoke(ControlSettingsReadCount);
        return Task.FromResult(ControlSettings);
    }

    public Task<GraphControlSettings> SetControlSettingsAsync(
        GraphControlSettings settings,
        long expectedRevision,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (expectedRevision != ControlSettings.Revision)
        {
            throw new InvalidOperationException("settings-revision-conflict");
        }

        ControlSettings = settings with { Revision = expectedRevision + 1 };
        return Task.FromResult(ControlSettings);
    }

    public Task<string?> GetLegacyMirrorManifestIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LegacyMirrorManifestId);
    }

    public Task BeginLegacyMirrorAsync(
        string manifestId,
        long expectedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(_stagingLegacyManifestId, manifestId, StringComparison.Ordinal))
        {
            _legacyMirrorPages.Clear();
            _stagingLegacyManifestId = manifestId;
            _stagingLegacyExpectedCount = expectedCount;
        }
        else if (_stagingLegacyExpectedCount != expectedCount)
        {
            throw new InvalidDataException("legacy-mirror-count-changed");
        }

        return Task.CompletedTask;
    }

    public Task StageLegacyMirrorPageAsync(
        string manifestId,
        long pageSequence,
        IReadOnlyList<GraphLegacyDecisionObservation> observations,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(_stagingLegacyManifestId, manifestId, StringComparison.Ordinal) || pageSequence < 0)
        {
            throw new InvalidOperationException("legacy-mirror-generation-stale");
        }

        if (_legacyMirrorPages.TryGetValue(pageSequence, out var existing))
        {
            if (!existing.Select(item => item.CanonicalRowHash).SequenceEqual(
                    observations.Select(item => item.CanonicalRowHash),
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException("legacy-mirror-page-changed");
            }
        }
        else
        {
            if (pageSequence != _legacyMirrorPages.Count)
            {
                throw new InvalidDataException("legacy-mirror-page-gap");
            }

            _legacyMirrorPages.Add(pageSequence, observations.ToArray());
        }

        if (FailNextLegacyMirrorPageAfterStage)
        {
            FailNextLegacyMirrorPageAfterStage = false;
            throw new GraphPersistenceException(
                "synthetic-legacy-mirror-interruption",
                "Synthetic interruption after a durable legacy-mirror page.");
        }

        return Task.CompletedTask;
    }

    public Task CompleteLegacyMirrorAsync(
        string manifestId,
        long expectedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(_stagingLegacyManifestId, manifestId, StringComparison.Ordinal) ||
            expectedCount != _stagingLegacyExpectedCount ||
            _legacyMirrorPages.Values.Sum(page => (long)page.Count) != expectedCount)
        {
            throw new InvalidDataException("legacy-mirror-incomplete");
        }

        LegacyMirrorManifestId = manifestId;
        LegacyMirrorPublishCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GraphDecisionRecoveryPoint>> GetRecoveryPointsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GraphDecisionRecoveryPoint>>(RecoveryPoints.ToArray());
    }

    public Task<GraphOperationResult> RestoreAsync(
        string recoveryPointId,
        string confirmationText,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestoredRecoveryPointId = recoveryPointId;
        return Task.FromResult(RestoreResult);
    }

    public Task<GraphDecisionEntry> AppendAsync(
        GraphDecisionCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ControlSettings.IsEnabled || command.ExpectedControlSettingsRevision != ControlSettings.Revision)
        {
            throw new GraphAccessUnavailableException("control-settings-revision-stale");
        }
        if (command.ExpectedSequence != _entries.Count)
        {
            throw new InvalidOperationException("sequence-conflict");
        }

        var entry = new GraphDecisionEntry(
            string.Concat("decision-", _entries.Count + 1),
            _entries.Count + 1,
            command,
            nowUtc,
            string.Concat("hash-", _entries.Count + 1));
        _entries.Add(entry);
        return Task.FromResult(entry);
    }

    public Task<IReadOnlyList<GraphDecisionEntry>> ReadAsync(
        long afterSequence,
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GraphDecisionEntry>>(
            _entries.Where(item => item.Sequence > afterSequence).Take(maximumCount).ToArray());

    public Task<GraphOperationResult> ClearAsync(
        string confirmationText,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Clear();
        _generation++;
        ClearCount++;
        return Task.FromResult(new GraphOperationResult(true, "cleared", 1));
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeGraphStore : IGraphStore
{
    private readonly Queue<GraphProjectionWorkItem> _pending = [];
    private readonly Dictionary<string, GraphProjectionClaim> _claims = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    private GraphProjectionRun? _run;
    private long _epoch;

    internal bool Initialized { get; private set; }
    internal bool Enabled { get; set; } = true;
    internal bool RejectCommit { get; set; }
    internal Exception? CommitFailure { get; set; }
    internal bool RejectRenew { get; set; }
    internal GraphProjectionStage? RejectStageAdvanceAt { get; set; }
    internal Action<GraphProjectionStage>? StageAdvanceHook { get; set; }
    internal bool RejectManifestHash { get; set; }
    internal Exception? SetEnabledFailure { get; set; }
    internal int RecoveredClaimCount { get; set; }
    internal int InvalidatedDecisionCount { get; private set; }
    internal int DecisionProjectionPageCount { get; private set; }
    internal bool FailNextDecisionProjectionPageAfterApply { get; set; }
    internal GraphPrivacyChange? LastPrivacyChange { get; private set; }
    internal GraphRepairRequest? LastRepairRequest { get; private set; }
    internal GraphDiagnosticsSnapshot? DiagnosticsOverride { get; set; }
    internal List<GraphComponentProjection> Components { get; } = [];
    internal List<GraphSearchExpansion> SearchExpansions { get; } = [];
    internal List<GraphNode> Nodes { get; } = [];
    internal List<GraphNeighbor> Neighbors { get; } = [];
    internal List<GraphEvidenceReference> Evidence { get; } = [];
    internal List<GraphFact> Facts { get; } = [];
    internal List<GraphTimelineEntry> Timeline { get; } = [];
    internal List<GraphMention> Mentions { get; } = [];
    internal List<GraphAlias> Aliases { get; } = [];
    internal List<GraphDecisionProjection> DecisionProjections { get; } = [];
    internal List<GraphProjectionStage> StageHistory { get; } = [];
    internal GraphProjectionCoverage Coverage { get; set; } = TestGraphData.Coverage;
    internal GraphRunControlState RunControl { get; set; } = GraphRunControlState.Pending;
    internal int FailureCount { get; private set; }
    internal int ResourceWaitCount { get; private set; }
    internal int RenewCount { get; private set; }
    internal int DisposeCount { get; private set; }
    internal GraphJobExecutionState? LastFailureState { get; private set; }
    internal GraphProjectionFailure? LastFailure { get; private set; }
    internal string? CurrentStage { get; private set; }
    internal string? CurrentWorkLabel { get; private set; }
    internal GraphMaintenanceStatus MaintenanceStatus { get; private set; } = GraphMaintenanceStatus.Idle;
    internal TaskCompletionSource<bool> ClaimRenewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void ResetDerivedGraph()
    {
        Components.Clear();
        Nodes.Clear();
        Neighbors.Clear();
        Evidence.Clear();
        Aliases.Clear();
        Mentions.Clear();
        DecisionProjections.Clear();
        Coverage = TestGraphData.Coverage;
    }
    internal Action? BeforeNodePageReturn { get; set; }
    internal Action? BeforeNodeDetailReturn { get; set; }
    internal Action? BeforeSearchReturn { get; set; }
    internal Action? BeforePrivacyInspectionReturn { get; set; }
    internal Action? BeforeRepairReturn { get; set; }
    internal Exception? InvalidateDecisionFailure { get; set; }

    internal void SeedExpiredClaim(
        GraphProjectionObservation observation,
        GraphProjectionStage stage = GraphProjectionStage.ObservationCaptured,
        string? stageInputFingerprint = null)
    {
        _epoch = Math.Max(1, _epoch);
        var workId = string.Concat("work:", observation.Kind.ToString(), ":", observation.StableKey);
        _queued.Add(workId);
        var work = new GraphProjectionWorkItem(
            workId,
            "old-run",
            observation,
            new GraphStateVector(GraphRunControlState.Running, GraphJobExecutionState.Running, GraphFreshnessState.Current, GraphIntegrityState.Valid),
            1)
        {
            Stage = stage,
            StageInputFingerprint = stageInputFingerprint,
        };
        var claim = new GraphProjectionClaim(work, "expired-claim", "old-owner", _epoch, TestGraphData.Now.AddMinutes(-2), TestGraphData.Now.AddMinutes(-1));
        _claims[claim.ClaimToken] = claim;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Initialized = true;
        return Task.CompletedTask;
    }

    public Task<GraphCoordinatorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new GraphCoordinatorStatus
        {
            IsEnabled = Enabled,
            RunId = _run?.RunId,
            FencingEpoch = _epoch,
            RunControl = RunControl,
            ActiveJobState = _claims.Count > 0 ? GraphJobExecutionState.Running : null,
            CurrentStage = CurrentStage,
            CurrentWorkLabel = CurrentWorkLabel,
            InputManifestComplete = _run?.InputManifestComplete ?? false,
            PendingCount = _pending.Count,
            RunningCount = _claims.Count,
            CompletedCount = Components.Count,
            PermanentFailureCount = FailureCount,
            WaitingCount = ResourceWaitCount,
            ProcessedObservationCount = Components.Count,
            TotalObservationCount = _run?.Snapshot.TotalObservationCount ?? 0,
            RemainingObservationCount = _pending.Count + _claims.Count,
            Coverage = Coverage,
            Maintenance = MaintenanceStatus,
        };
        return Task.FromResult(status);
    }

    public Task<GraphRecoveryResult> RecoverAsync(
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        foreach (var claim in _claims.Values)
        {
            _pending.Enqueue(claim.WorkItem with
            {
                State = claim.WorkItem.State with { JobExecution = GraphJobExecutionState.Pending },
            });
        }

        RecoveredClaimCount += _claims.Count;
        _claims.Clear();
        return Task.FromResult(new GraphRecoveryResult(RecoveredClaimCount, 0, "recovered"));
    }

    public Task<GraphOperationResult> SetEnabledAsync(
        bool enabled,
        bool consentConfirmed,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (SetEnabledFailure is not null)
        {
            throw SetEnabledFailure;
        }

        if (enabled && !consentConfirmed)
        {
            return Task.FromResult(new GraphOperationResult(false, "consent-required", 0));
        }

        Enabled = enabled;
        return Task.FromResult(new GraphOperationResult(true, enabled ? "enabled" : "disabled", 1));
    }

    public Task<GraphOperationResult> SetResourceWaitingAsync(
        GraphProjectionRun run,
        string reasonCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ResourceWaitCount++;
        return Task.FromResult(new GraphOperationResult(true, reasonCode, 0));
    }

    public Task SetRunStageAsync(
        GraphProjectionRun run,
        GraphRunStageUpdate update,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentStage = update.StageCode;
        CurrentWorkLabel = update.CurrentWorkLabel;
        return Task.CompletedTask;
    }

    public Task<GraphMaintenanceResult> MaintainAsync(
        GraphMaintenanceRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new GraphMaintenanceResult(1, 256, 128, false, nowUtc, "maintenance-complete");
        MaintenanceStatus = new GraphMaintenanceStatus(false, false, nowUtc, result.RecordsRemoved, result.Message);
        return Task.FromResult(result);
    }

    public Task<GraphProjectionRun> BeginProjectionAsync(
        GraphProjectionSnapshot snapshot,
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (_run is null || _run.Snapshot.ManifestId != snapshot.ManifestId ||
            _run.Snapshot.Revision != snapshot.Revision ||
            _run.Snapshot.GraphDecisionSequence != snapshot.GraphDecisionSequence ||
            !string.Equals(
                _run.Snapshot.GraphDecisionCheckpointId,
                snapshot.GraphDecisionCheckpointId,
                StringComparison.Ordinal))
        {
            _epoch++;
            _run = new GraphProjectionRun("run-1", _epoch, snapshot, GraphRunControlState.Running, false);
            RunControl = GraphRunControlState.Running;
        }

        return Task.FromResult(_run);
    }

    public Task QueueProjectionPageAsync(
        GraphProjectionRun run,
        GraphProjectionPage page,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        foreach (var observation in page.Observations)
        {
            var workId = string.Concat("work:", observation.Kind.ToString(), ":", observation.StableKey);
            if (_queued.Add(workId))
            {
                _pending.Enqueue(new GraphProjectionWorkItem(
                    workId,
                    run.RunId,
                    observation,
                    new GraphStateVector(RunControl, GraphJobExecutionState.Pending, GraphFreshnessState.Current, GraphIntegrityState.Valid),
                    0));
            }
        }

        Coverage = Coverage with
        {
            IngestedManifestId = run.Snapshot.ManifestId,
            IngestedRevision = run.Snapshot.Revision,
            TotalObservationCount = run.Snapshot.TotalObservationCount,
            IsComplete = false,
        };
        return Task.CompletedTask;
    }

    public Task CompleteInputManifestAsync(
        GraphProjectionRun run,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (RejectManifestHash)
        {
            throw new InvalidDataException("manifest-hash-mismatch");
        }

        _run = run with { InputManifestComplete = true };
        return Task.CompletedTask;
    }

    public Task ApplyDecisionProjectionPageAsync(
        GraphProjectionRun run,
        GraphDecisionSnapshot decisionSnapshot,
        IReadOnlyList<GraphDecisionProjection> projections,
        bool isLastPage,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        DecisionProjectionPageCount++;
        foreach (var projection in projections)
        {
            DecisionProjections.Add(projection);
            ApplyDecisionProjection(projection);
        }

        Coverage = Coverage with
        {
            IngestedDecisionSequence = projections.Count == 0
                ? (isLastPage ? decisionSnapshot.Sequence : Coverage.IngestedDecisionSequence)
                : projections[projections.Count - 1].Decision.Sequence,
            IngestedDecisionCheckpointId = decisionSnapshot.CheckpointId,
        };
        if (FailNextDecisionProjectionPageAfterApply)
        {
            FailNextDecisionProjectionPageAfterApply = false;
            throw new GraphPersistenceException("synthetic-interruption", "Synthetic interruption after a durable decision page.");
        }

        return Task.CompletedTask;
    }

    public Task<GraphProjectionClaim?> TryClaimNextAsync(
        GraphProjectionRun run,
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        TimeSpan leaseTimeToLive,
        CancellationToken cancellationToken = default)
    {
        if (_pending.Count == 0)
        {
            return Task.FromResult<GraphProjectionClaim?>(null);
        }

        var nextPhase = ProjectionPhase(_pending.Peek().Observation.Kind);
        if (_claims.Count > 0 && _claims.Values.Any(item =>
                ProjectionPhase(item.WorkItem.Observation.Kind) < nextPhase))
        {
            return Task.FromResult<GraphProjectionClaim?>(null);
        }

        var work = _pending.Dequeue();
        ResourceWaitCount = 0;
        work = work with
        {
            Attempt = work.Attempt + 1,
            State = work.State with { RunControl = RunControl, JobExecution = GraphJobExecutionState.Running },
        };
        var claim = new GraphProjectionClaim(
            work,
            string.Concat("claim-", work.WorkId, "-", work.Attempt),
            ownerInstanceId,
            run.FencingEpoch,
            nowUtc,
            nowUtc + leaseTimeToLive);
        _claims[claim.ClaimToken] = claim;
        return Task.FromResult<GraphProjectionClaim?>(claim);
    }

    public Task<bool> RenewClaimAsync(
        GraphProjectionClaim claim,
        DateTimeOffset nowUtc,
        TimeSpan leaseTimeToLive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RenewCount++;
        ClaimRenewed.TrySetResult(true);
        return Task.FromResult(!RejectRenew && _claims.ContainsKey(claim.ClaimToken) && claim.FencingEpoch == _epoch);
    }

    public Task<GraphProjectionClaim?> AdvanceClaimStageAsync(
        GraphProjectionClaim claim,
        GraphProjectionStageTransition transition,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        StageAdvanceHook?.Invoke(transition.CompletedStage);
        cancellationToken.ThrowIfCancellationRequested();
        if (RejectStageAdvanceAt == transition.CompletedStage ||
            !_claims.TryGetValue(claim.ClaimToken, out var current) || current.FencingEpoch != _epoch ||
            current.WorkItem.Stage != transition.ExpectedStage ||
            (int)transition.CompletedStage != (int)transition.ExpectedStage + 1)
        {
            return Task.FromResult<GraphProjectionClaim?>(null);
        }

        var advanced = current with
        {
            WorkItem = current.WorkItem with
            {
                Stage = transition.CompletedStage,
                StageInputFingerprint = transition.InputFingerprint,
            },
        };
        _claims[claim.ClaimToken] = advanced;
        StageHistory.Add(transition.CompletedStage);
        return Task.FromResult<GraphProjectionClaim?>(advanced);
    }

    public Task<bool> CommitClaimAsync(
        GraphProjectionClaim claim,
        GraphComponentProjection projection,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (CommitFailure is { } failure)
        {
            CommitFailure = null;
            throw failure;
        }

        if (RejectCommit || claim.FencingEpoch != _epoch || claim.WorkItem.Stage != GraphProjectionStage.ComponentValidated ||
            !_claims.Remove(claim.ClaimToken))
        {
            return Task.FromResult(false);
        }

        Components.Add(projection);
        StageHistory.Add(GraphProjectionStage.ComponentPublished);
        return Task.FromResult(true);
    }

    public Task RecordClaimFailureAsync(
        GraphProjectionClaim claim,
        GraphProjectionFailure failure,
        GraphJobExecutionState state,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        _claims.Remove(claim.ClaimToken);
        FailureCount++;
        LastFailureState = state;
        LastFailure = failure;
        return Task.CompletedTask;
    }

    public Task<GraphOperationResult> SetRunControlAsync(
        GraphRunControlRequest request,
        CancellationToken cancellationToken = default)
    {
        RunControl = request.RequestedState;
        if (_run is not null)
        {
            _run = _run with { State = request.RequestedState };
        }

        return Task.FromResult(new GraphOperationResult(true, request.ReasonCode, 1));
    }

    public Task<GraphOperationResult> CompleteProjectionAsync(
        GraphProjectionRun run,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        RunControl = GraphRunControlState.Complete;
        CurrentStage = "search-visible";
        CurrentWorkLabel = null;
        StageHistory.Add(GraphProjectionStage.StaleRowsCleaned);
        _run = run with { State = GraphRunControlState.Complete, InputManifestComplete = true };
        Coverage = Coverage with
        {
            IsComplete = true,
            IsStale = false,
            ProjectedObservationCount = Components.Count,
            AppliedManifestId = run.Snapshot.ManifestId,
            AppliedRevision = run.Snapshot.Revision,
            AppliedDecisionSequence = run.Snapshot.GraphDecisionSequence,
            AppliedDecisionCheckpointId = run.Snapshot.GraphDecisionCheckpointId,
            AppliedPrivacySequence = run.Snapshot.PrivacySequence,
        };
        return Task.FromResult(new GraphOperationResult(true, "complete", Components.Count));
    }

    public Task<GraphOperationResult> RetryFailedAsync(
        string? workId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        FailureCount = 0;
        return Task.FromResult(new GraphOperationResult(true, "retry", 1));
    }

    public Task<GraphPage<GraphNode>> GetNodesAsync(GraphNodeQuery query, CancellationToken cancellationToken = default)
    {
        var result = new GraphPage<GraphNode>(Nodes.Take(query.PageSize).ToArray(), null, Nodes.Count);
        BeforeNodePageReturn?.Invoke();
        return Task.FromResult(result);
    }

    public Task<GraphNodeDetails?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var node = Nodes.FirstOrDefault(item => item.Identity.NodeId == nodeId);
        var aliases = node is null
            ? Array.Empty<string>()
            : Aliases.Where(item => item.NodeId == node.Identity.NodeId).Select(item => item.Label).ToArray();
        var result = node is null ? null : new GraphNodeDetails(node, aliases, 0, 0);
        BeforeNodeDetailReturn?.Invoke();
        return Task.FromResult(result);
    }

    public Task<GraphPage<GraphFact>> GetFactsAsync(
        GraphFactQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GraphPage<GraphFact>(
            Facts.Where(item => item.SubjectNodeId == query.NodeId &&
                                (query.Kind is null || item.Kind == query.Kind.Value))
                .Take(query.PageSize)
                .ToArray(),
            null,
            Facts.LongCount(item => item.SubjectNodeId == query.NodeId &&
                                    (query.Kind is null || item.Kind == query.Kind.Value))));

    public Task<GraphPage<GraphTimelineEntry>> GetTimelineAsync(
        GraphTimelineQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GraphPage<GraphTimelineEntry>(
            Timeline.Where(item => item.SubjectNodeId == query.NodeId &&
                                   (query.FromUtc is null || item.OccurredAtUtc >= query.FromUtc.Value) &&
                                   (query.ToUtc is null || item.OccurredAtUtc <= query.ToUtc.Value))
                .OrderByDescending(item => item.OccurredAtUtc)
                .ThenBy(item => item.FactId, StringComparer.Ordinal)
                .Take(query.PageSize)
                .ToArray(),
            null,
            Timeline.LongCount(item => item.SubjectNodeId == query.NodeId &&
                                       (query.FromUtc is null || item.OccurredAtUtc >= query.FromUtc.Value) &&
                                       (query.ToUtc is null || item.OccurredAtUtc <= query.ToUtc.Value))));

    public Task<GraphEdge?> GetEdgeAsync(string edgeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Neighbors.Select(item => item.Edge).FirstOrDefault(item => item.Id == edgeId));

    public Task<GraphMention?> GetMentionAsync(string mentionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Mentions.FirstOrDefault(item => item.Id == mentionId));

    public Task<GraphPage<GraphNeighbor>> GetNeighborsAsync(GraphNeighborQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GraphPage<GraphNeighbor>(Neighbors.Take(query.PageSize).ToArray(), null, Neighbors.Count));

    public Task<IReadOnlyList<GraphEvidenceReference>> GetEvidenceAsync(
        string edgeId,
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GraphEvidenceReference>>(Evidence.Take(maximumCount).ToArray());

    public Task<IReadOnlyList<GraphSearchExpansion>> GetSearchExpansionsAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GraphSearchExpansion> result = SearchExpansions.Take(maximumCount).ToArray();
        BeforeSearchReturn?.Invoke();
        return Task.FromResult(result);
    }

    public Task<GraphProjectionCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) => Task.FromResult(Coverage);

    public Task<GraphPrivacyInspection> InspectPrivacyAsync(
        GraphPrivacyScope scope,
        CancellationToken cancellationToken = default)
    {
        var result = new GraphPrivacyInspection(scope, Nodes.Count, Neighbors.Count, Evidence.Count, 0, 0, false, TestGraphData.Now, "inspection");
        BeforePrivacyInspectionReturn?.Invoke();
        return Task.FromResult(result);
    }

    public Task<GraphOperationResult> ApplyPrivacyAsync(
        GraphPrivacyChange change,
        GraphDecisionSnapshot decisionSnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        LastPrivacyChange = change;
        if (change.Action == GraphPrivacyAction.ForgetDerivedData)
        {
            var removedIds = Nodes
                .Where(item => change.Scope.Kind == GraphPrivacyScopeKind.All ||
                               item.Identity.NodeId == change.Scope.StableId ||
                               item.Identity.CanonicalKey == change.Scope.StableId ||
                               item.OwningSourceId == change.Scope.StableId)
                .Select(item => item.Identity.NodeId)
                .ToHashSet(StringComparer.Ordinal);
            Nodes.RemoveAll(item => removedIds.Contains(item.Identity.NodeId));
            Aliases.RemoveAll(item => removedIds.Contains(item.NodeId));
            Neighbors.RemoveAll(item => removedIds.Contains(item.Edge.SourceNodeId) || removedIds.Contains(item.Edge.TargetNodeId));
        }

        Coverage = Coverage with
        {
            IngestedDecisionSequence = decisionSnapshot.Sequence,
            IngestedDecisionCheckpointId = decisionSnapshot.CheckpointId,
            AppliedDecisionSequence = decisionSnapshot.Sequence,
            AppliedDecisionCheckpointId = decisionSnapshot.CheckpointId,
        };
        return Task.FromResult(new GraphOperationResult(true, "privacy-applied", 1));
    }

    public Task<GraphOperationResult> RepairAsync(
        GraphRepairRequest request,
        GraphDecisionSnapshot decisionSnapshot,
        GraphAuthoritySnapshot authoritySnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        LastRepairRequest = request;
        BeforeRepairReturn?.Invoke();
        return Task.FromResult(new GraphOperationResult(true, "repair-applied", 1));
    }

    public Task InvalidateDecisionAsync(
        GraphDecisionEntry decision,
        GraphDecisionSnapshot decisionSnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (InvalidateDecisionFailure is not null)
        {
            throw InvalidateDecisionFailure;
        }

        InvalidatedDecisionCount++;
        Coverage = Coverage with
        {
            IsStale = true,
        };
        return Task.CompletedTask;
    }

    public Task<GraphDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DiagnosticsOverride ?? new GraphDiagnosticsSnapshot
        {
            RunId = _run?.RunId,
            ProjectionRevision = Coverage.ProjectionRevision,
            NodeCount = Nodes.Count,
            EdgeCount = Neighbors.Count,
            EvidenceCount = Evidence.Count,
            RecoveredClaimCount = RecoveredClaimCount,
            Coverage = Coverage,
            StorageBreakdown = GraphStorageBreakdown.Empty,
            Maintenance = MaintenanceStatus,
        });

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    private static int ProjectionPhase(GraphProjectionObservationKind kind) => kind switch
    {
        GraphProjectionObservationKind.Source or GraphProjectionObservationKind.LegacyDecision => 0,
        GraphProjectionObservationKind.File or GraphProjectionObservationKind.Collection => 1,
        GraphProjectionObservationKind.Relationship or GraphProjectionObservationKind.CollectionMembership => 2,
        GraphProjectionObservationKind.Deletion => 3,
        _ => throw new InvalidDataException("The fake graph store received an unsupported projection kind."),
    };

    private void ApplyDecisionProjection(GraphDecisionProjection projection)
    {
        if (projection.Node is { } node)
        {
            Nodes.RemoveAll(item => item.Identity.NodeId == node.Identity.NodeId);
            Nodes.Add(node);
        }

        if (projection.ReplacementLabel is { } label)
        {
            var index = Nodes.FindIndex(item => item.Identity.NodeId == projection.SubjectId || item.Identity.CanonicalKey == projection.SubjectId);
            if (index >= 0)
            {
                Nodes[index] = Nodes[index] with { DisplayLabel = label };
            }
        }

        if (projection.Alias is { } alias)
        {
            Aliases.RemoveAll(item => item.Id == alias.Id);
            Aliases.Add(alias);
        }

        if (projection.Edge is { } edge)
        {
            var source = Nodes.FirstOrDefault(item => item.Identity.NodeId == edge.SourceNodeId);
            var target = Nodes.FirstOrDefault(item => item.Identity.NodeId == edge.TargetNodeId);
            if (source is not null && target is not null)
            {
                Neighbors.RemoveAll(item => item.Edge.Id == edge.Id);
                Neighbors.Add(new GraphNeighbor(target, edge, []));
            }
        }

        if (projection.Decision.Command.Kind is GraphDecisionKind.UnlinkNodes or GraphDecisionKind.NeverMerge)
        {
            Neighbors.RemoveAll(item => item.Edge.Id == projection.SubjectId);
        }

        if (projection.Decision.Command.Kind == GraphDecisionKind.RemoveAlias)
        {
            Aliases.RemoveAll(item => item.NodeId == projection.SubjectId && item.Label == projection.Decision.Command.Label);
        }

        if (projection.Decision.Command.Kind == GraphDecisionKind.Forget)
        {
            var removedIds = Nodes
                .Where(item => item.Identity.NodeId == projection.SubjectId || item.Identity.CanonicalKey == projection.SubjectId)
                .Select(item => item.Identity.NodeId)
                .ToHashSet(StringComparer.Ordinal);
            Nodes.RemoveAll(item => removedIds.Contains(item.Identity.NodeId));
            Aliases.RemoveAll(item => removedIds.Contains(item.NodeId));
            Neighbors.RemoveAll(item => removedIds.Contains(item.Edge.SourceNodeId) || removedIds.Contains(item.Edge.TargetNodeId));
        }
    }
}

internal sealed class FakeGraphReconciliationSignal : IGraphReconciliationSignal
{
    internal int Count { get; private set; }
    internal Exception? ExceptionToThrow { get; set; }

    public ValueTask SignalAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count++;
        return ExceptionToThrow is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(ExceptionToThrow);
    }
}

internal sealed class FakeGraphResourcePolicy : IGraphResourceAdmissionPolicy
{
    internal GraphResourceEligibility Eligibility { get; set; } = new(true, null);
    internal int CheckCount { get; private set; }

    public Task<GraphResourceEligibility> GetEligibilityAsync(
        GraphControlSettings settings,
        CancellationToken cancellationToken = default)
    {
        CheckCount++;
        return Task.FromResult(Eligibility);
    }
}

internal static class TestGraphData
{
    internal static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    internal static GraphProjectionCoverage Coverage { get; } = new(
        true,
        true,
        false,
        false,
        0,
        0,
        0,
        0,
        "manifest-1",
        1,
        "projection pending")
    {
        IngestedDecisionCheckpointId = "checkpoint-0-0",
        AppliedDecisionCheckpointId = "checkpoint-0-0",
        IngestedPrivacySequence = 1,
        AppliedPrivacySequence = 1,
    };

    internal static GraphSourceObservation Source(string id = "source-1") => new()
    {
        StableKey = string.Concat("source:", id),
        CanonicalRowHash = string.Concat("hash-source-", id),
        Revision = 1,
        ObservedAtUtc = Now,
        SourceId = id,
        DisplayName = "Synthetic source",
        PathSemanticsVersion = "path-v1",
        PathComparison = GraphPathComparison.CaseSensitive,
    };

    internal static GraphFileObservation File(string id = "file-1", string sourceId = "source-1") => new()
    {
        StableKey = string.Concat("file:", id),
        CanonicalRowHash = string.Concat("hash-file-", id),
        Revision = 1,
        ObservedAtUtc = Now,
        FileId = id,
        SourceId = sourceId,
        FileName = string.Concat(id, ".txt"),
        RelativePath = string.Concat("records/", id, ".txt"),
        FolderRelativePath = "records",
        PathSemanticsVersion = "path-v1",
        PathComparison = GraphPathComparison.CaseSensitive,
        Length = 42,
        CreationTimeUtc = Now.AddDays(-1),
        ModifiedTimeUtc = Now,
        HasBasicMetadata = true,
        ContentHash = new string('a', 64),
        ContentHashAlgorithmVersion = "sha256-v1",
    };
}
