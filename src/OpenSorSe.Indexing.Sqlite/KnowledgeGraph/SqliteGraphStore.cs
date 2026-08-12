using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>Persists rebuildable graph projections, durable jobs, and privacy-bounded queries.</summary>
public sealed partial class SqliteGraphStore : IGraphStore
{
    private const int MaximumConcurrentReaders = 4;
    /// <summary>Gets the conservative default graph-store ceiling.</summary>
    public const long DefaultMaximumDatabaseBytes = 512L * 1024L * 1024L;
    private const long MaximumInboxRows = 250_000;
    private const long MaximumJobRows = 250_000;
    private const long MaximumInboxPayloadBytes = 384L * 1024L * 1024L;
    private readonly string _databasePath;
    private readonly string _lifecycleLockPath;
    private readonly string _decisionDatabasePath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _readerGate = new(MaximumConcurrentReaders, MaximumConcurrentReaders);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private SqliteConnection? _activeWriterConnection;
    private CancellationToken _writerCancellationToken;
    private int _disposeState;
    private bool _initialized;
    private long _recoveredClaims;

    /// <summary>Creates a rebuildable graph store at an application-owned path.</summary>
    public SqliteGraphStore(string databasePath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(_databasePath)
            ?? throw new ArgumentException("The graph database must have a parent directory.", nameof(databasePath));
        _lifecycleLockPath = Path.Combine(directory, ".knowledge-data.lifecycle.lock");
        _decisionDatabasePath = Path.Combine(directory, "knowledge-decisions.db");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the fully qualified application-owned database path.</summary>
    public string DatabasePath => _databasePath;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                return;
            }

            await SqliteKnowledgeInfrastructure.InitializeAsync(
                    _databasePath,
                    _lifecycleLockPath,
                    SqliteKnowledgeGraphSchema.ApplicationId,
                    SqliteKnowledgeGraphSchema.Version,
                    SqliteKnowledgeGraphSchema.RequiredTables,
                    SqliteKnowledgeGraphSchema.CreateVersionOne,
                    "graph_meta",
                    "graph_migration_history",
                    _timeProvider,
                    cancellationToken,
                    SqliteKnowledgeGraphSchema.RequiredColumns,
                    SqliteKnowledgeGraphSchema.RequiredIndexes)
                .ConfigureAwait(false);
            using var connection = OpenConnection();
            ValidateAndRecoverGraphState(connection, _timeProvider.GetUtcNow());
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<GraphCoordinatorStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                return ReadStatus(connection);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphOperationResult> SetEnabledAsync(
        bool enabled,
        bool consentConfirmed,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (enabled && !consentConfirmed)
        {
            return Task.FromResult(new GraphOperationResult(false, "Knowledge Graph requires explicit local consent before it is enabled.", 0));
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "INSERT INTO graph_meta(key, value) VALUES ('enabled', $enabled) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                    ("$enabled", enabled ? "1" : "0"));
                var affected = 0;
                if (!enabled)
                {
                    var running = Convert.ToInt32(
                        ExecuteScalar(connection, transaction, "SELECT COUNT(*) FROM graph_jobs WHERE execution_state = $running;", ("$running", State(GraphJobExecutionState.Running))),
                        CultureInfo.InvariantCulture);
                    affected = ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE graph_runs
                        SET control_state = $state, control_sequence = control_sequence + 1, updated_utc_ticks = $now
                        WHERE control_state IN ($pending, $active, $pauseRequested);
                        """,
                        ("$state", State(running == 0 ? GraphRunControlState.Paused : GraphRunControlState.PauseRequested)),
                        ("$now", nowUtc.UtcTicks), ("$pending", State(GraphRunControlState.Pending)),
                        ("$active", State(GraphRunControlState.Running)), ("$pauseRequested", State(GraphRunControlState.PauseRequested)));
                }

                transaction.Commit();
                return new GraphOperationResult(true, enabled ? "Knowledge Graph is enabled." : "Knowledge Graph is disabled; retained graph data was not deleted.", Math.Max(affected, 1));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphOperationResult> SetResourceWaitingAsync(
        GraphProjectionRun run,
        string reasonCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (reasonCode.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                var affected = SqliteKnowledgeInfrastructure.ExecuteNonQuery(
                    connection,
                    "UPDATE graph_runs SET current_stage = $stage, current_work_label = $reason, updated_utc_ticks = $now WHERE run_id = $run AND coordinator_epoch = $epoch AND control_state = $running;",
                    ("$stage", State(GraphJobExecutionState.WaitingForResources)), ("$reason", reasonCode),
                    ("$now", nowUtc.UtcTicks), ("$run", run.RunId), ("$epoch", run.FencingEpoch),
                    ("$running", State(GraphRunControlState.Running)));
                return new GraphOperationResult(affected == 1, "Graph processing is waiting for the configured resource policy.", affected);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetRunStageAsync(
        GraphProjectionRun run,
        GraphRunStageUpdate update,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(update);
        ValidateOperationalStage(update.StageCode, nameof(update));
        if (update.CurrentWorkLabel is not null)
        {
            ValidateOperationalStage(update.CurrentWorkLabel, nameof(update), maximumLength: 256, allowSpaces: true);
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                var affected = SqliteKnowledgeInfrastructure.ExecuteNonQuery(
                    connection,
                    "UPDATE graph_runs SET current_stage = $stage, current_work_label = $work, updated_utc_ticks = $now WHERE run_id = $run AND coordinator_epoch = $epoch AND control_state NOT IN ($complete, $cancelled);",
                    ("$stage", update.StageCode), ("$work", update.CurrentWorkLabel),
                    ("$now", nowUtc.UtcTicks), ("$run", run.RunId), ("$epoch", run.FencingEpoch),
                    ("$complete", State(GraphRunControlState.Complete)),
                    ("$cancelled", State(GraphRunControlState.Cancelled)));
                if (affected != 1)
                {
                    throw new InvalidOperationException("The graph projection run is no longer current for progress reporting.");
                }

                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphRecoveryResult> RecoverAsync(
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateStableId(ownerInstanceId, nameof(ownerInstanceId));
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var epoch = AcquireOrRenewCoordinator(connection, transaction, ownerInstanceId, nowUtc);
                var expiryBoundary = (nowUtc - GraphLimits.ShutdownGracePeriod).UtcTicks;
                var recoverable = new List<(
                    string JobId,
                    string AttemptId,
                    int Attempt,
                    int Maximum,
                    GraphRunControlState RunControl,
                    bool WasFenced,
                    bool WasExpired)>();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        SELECT j.job_id, a.attempt_id, j.current_attempt, j.maximum_attempts,
                               r.control_state,
                               j.claim_fencing_epoch < $epoch,
                               j.claim_expires_utc_ticks IS NOT NULL AND
                                   j.claim_expires_utc_ticks <= $expired
                        FROM graph_jobs j
                        JOIN graph_runs r ON r.run_id = j.run_id
                        JOIN graph_job_attempts a
                          ON a.job_id = j.job_id AND a.attempt_number = j.current_attempt
                        WHERE j.execution_state = $running
                          AND (r.control_state IN ($pauseRequested, $cancelRequested) OR
                               j.claim_fencing_epoch < $epoch OR
                               (j.claim_expires_utc_ticks IS NOT NULL AND
                                j.claim_expires_utc_ticks <= $expired));
                        """;
                    AddParameters(
                        command,
                        ("$running", State(GraphJobExecutionState.Running)),
                        ("$pauseRequested", State(GraphRunControlState.PauseRequested)),
                        ("$cancelRequested", State(GraphRunControlState.CancelRequested)),
                        ("$epoch", epoch),
                        ("$expired", expiryBoundary));
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        recoverable.Add((
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetInt32(2),
                            reader.GetInt32(3),
                            Parse<GraphRunControlState>(reader.GetString(4)),
                            reader.GetBoolean(5),
                            reader.GetBoolean(6)));
                    }
                }

                var repairRequired = 0;
                foreach (var item in recoverable)
                {
                    var cancellationWins = item.RunControl == GraphRunControlState.CancelRequested;
                    var nextState = cancellationWins
                        ? GraphJobExecutionState.Cancelled
                        : item.Attempt < item.Maximum
                            ? GraphJobExecutionState.RetryableFailure
                            : GraphJobExecutionState.PermanentFailure;
                    var recoveryCategory = cancellationWins
                        ? "cancel-recovered"
                        : item.WasFenced
                            ? "fenced-claim"
                            : item.WasExpired
                                ? "expired-claim"
                                : "control-drain";
                    var recoveryOutcome = cancellationWins
                        ? "RecoveredCancelled"
                        : item.WasFenced
                            ? "RecoveredFenced"
                            : item.WasExpired
                                ? "RecoveredExpired"
                                : "RecoveredControlDrain";
                    if (nextState == GraphJobExecutionState.PermanentFailure)
                    {
                        repairRequired++;
                    }

                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE graph_jobs
                        SET execution_state = $state,
                            integrity_state = $integrity,
                            failure_category = $failure,
                            next_eligible_utc_ticks = $nextEligible,
                            claim_owner_instance_id = NULL,
                            claim_token = NULL,
                            claim_fencing_epoch = NULL,
                            claim_heartbeat_utc_ticks = NULL,
                            claim_expires_utc_ticks = NULL,
                            updated_utc_ticks = $now
                        WHERE job_id = $job AND execution_state = $running;
                        """,
                        ("$state", State(nextState)),
                        ("$integrity", State(nextState == GraphJobExecutionState.PermanentFailure
                            ? GraphIntegrityState.RepairRequired
                            : GraphIntegrityState.Valid)),
                        ("$failure", recoveryCategory),
                        ("$nextEligible", cancellationWins ? null : nowUtc.UtcTicks),
                        ("$now", nowUtc.UtcTicks),
                        ("$job", item.JobId),
                        ("$running", State(GraphJobExecutionState.Running)));
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE graph_job_attempts
                        SET completed_utc_ticks = $now,
                            outcome = $outcome,
                            failure_category = $failure,
                            recovery_count = recovery_count + 1
                        WHERE attempt_id = $attempt AND completed_utc_ticks IS NULL;
                        """,
                        ("$now", nowUtc.UtcTicks),
                        ("$outcome", recoveryOutcome),
                        ("$failure", recoveryCategory),
                        ("$attempt", item.AttemptId));
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_job_attempts
                    SET completed_utc_ticks = $now,
                        outcome = 'RecoveredCancelled',
                        failure_category = 'cancel-recovered',
                        recovery_count = recovery_count + 1
                    WHERE completed_utc_ticks IS NULL
                      AND job_id IN (
                          SELECT j.job_id
                          FROM graph_jobs j
                          JOIN graph_runs r ON r.run_id = j.run_id
                          WHERE r.control_state = $cancelRequested);
                    """,
                    ("$now", nowUtc.UtcTicks),
                    ("$cancelRequested", State(GraphRunControlState.CancelRequested)));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_jobs
                    SET execution_state = $cancelledJob,
                        failure_category = 'cancel-recovered',
                        claim_owner_instance_id = NULL,
                        claim_token = NULL,
                        claim_fencing_epoch = NULL,
                        claim_heartbeat_utc_ticks = NULL,
                        claim_expires_utc_ticks = NULL,
                        next_eligible_utc_ticks = NULL,
                        updated_utc_ticks = $now
                    WHERE run_id IN (
                        SELECT run_id FROM graph_runs WHERE control_state = $cancelRequested)
                      AND execution_state NOT IN ($completeJob, $cancelledJob, $permanentJob);
                    """,
                    ("$cancelledJob", State(GraphJobExecutionState.Cancelled)),
                    ("$completeJob", State(GraphJobExecutionState.Complete)),
                    ("$permanentJob", State(GraphJobExecutionState.PermanentFailure)),
                    ("$cancelRequested", State(GraphRunControlState.CancelRequested)),
                    ("$now", nowUtc.UtcTicks));
                var acknowledgedCancellationCount = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_runs
                    SET control_state = $cancelled,
                        completed_utc_ticks = $now,
                        updated_utc_ticks = $now
                    WHERE control_state = $cancelRequested
                      AND NOT EXISTS (
                          SELECT 1 FROM graph_jobs j
                          WHERE j.run_id = graph_runs.run_id AND j.execution_state = $runningJob);
                    """,
                    ("$cancelled", State(GraphRunControlState.Cancelled)),
                    ("$cancelRequested", State(GraphRunControlState.CancelRequested)),
                    ("$runningJob", State(GraphJobExecutionState.Running)),
                    ("$now", nowUtc.UtcTicks));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_repair_operations
                    SET state = 'Cancelled',
                        completed_utc_ticks = $now,
                        records_repaired = (
                            SELECT COUNT(*) FROM graph_jobs
                            WHERE run_id = graph_repair_operations.bounded_detail
                              AND execution_state = $completeJob),
                        bounded_detail = 'Staged graph repair cancellation was recovered after restart; previously published component generations remain available.'
                    WHERE state = 'Running'
                      AND bounded_detail IN (
                          SELECT run_id FROM graph_runs
                          WHERE control_state = $cancelled
                            AND cancellation_reason IS NOT NULL);
                    """,
                    ("$now", nowUtc.UtcTicks),
                    ("$completeJob", State(GraphJobExecutionState.Complete)),
                    ("$cancelled", State(GraphRunControlState.Cancelled)));
                var acknowledgedPauseCount = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_runs
                    SET control_state = $paused,
                        updated_utc_ticks = $now
                    WHERE control_state = $pauseRequested
                      AND NOT EXISTS (
                          SELECT 1 FROM graph_jobs j
                          WHERE j.run_id = graph_runs.run_id AND j.execution_state = $runningJob);
                    """,
                    ("$paused", State(GraphRunControlState.Paused)),
                    ("$pauseRequested", State(GraphRunControlState.PauseRequested)),
                    ("$runningJob", State(GraphJobExecutionState.Running)),
                    ("$now", nowUtc.UtcTicks));

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_runs
                    SET coordinator_epoch = $epoch,
                        owner_instance_id = $owner,
                        updated_utc_ticks = $now
                    WHERE control_state NOT IN ($cancelled, $complete);
                    """,
                    ("$epoch", epoch), ("$owner", ownerInstanceId), ("$now", nowUtc.UtcTicks),
                    ("$cancelled", State(GraphRunControlState.Cancelled)), ("$complete", State(GraphRunControlState.Complete)));
                if (acknowledgedCancellationCount > 0)
                {
                    PruneProjectionRecoveryHistory(connection, transaction);
                }

                transaction.Commit();
                Interlocked.Add(ref _recoveredClaims, recoverable.Count);
                var acknowledgedIntentCount = acknowledgedCancellationCount + acknowledgedPauseCount;
                return new GraphRecoveryResult(
                    recoverable.Count,
                    repairRequired,
                    recoverable.Count == 0 && acknowledgedIntentCount == 0
                        ? "No stale graph claims or pending run-control acknowledgements required recovery."
                        : "Stale graph claims were fenced and durable run-control intent was recovered.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphProjectionRun> BeginProjectionAsync(
        GraphProjectionSnapshot snapshot,
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        ValidateStableId(ownerInstanceId, nameof(ownerInstanceId));
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                EnsureEnabled(connection, transaction);
                var epoch = AcquireOrRenewCoordinator(connection, transaction, ownerInstanceId, nowUtc);
                using (var existing = connection.CreateCommand())
                {
                    existing.Transaction = transaction;
                    existing.CommandText =
                        """
                        SELECT run_id, control_state, input_manifest_complete
                        FROM graph_runs
                        WHERE snapshot_manifest_id = $manifest
                          AND control_state NOT IN ($cancelled, $complete)
                        ORDER BY created_utc_ticks DESC, run_id DESC
                        LIMIT 1;
                        """;
                    AddParameters(
                        existing,
                        ("$manifest", snapshot.ManifestId),
                        ("$cancelled", State(GraphRunControlState.Cancelled)),
                        ("$complete", State(GraphRunControlState.Complete)));
                    using var reader = existing.ExecuteReader();
                    if (reader.Read())
                    {
                        var runId = reader.GetString(0);
                        var state = Parse<GraphRunControlState>(reader.GetString(1));
                        var complete = reader.GetBoolean(2);
                        reader.Close();
                        ExecuteNonQuery(
                            connection,
                            transaction,
                            "UPDATE graph_runs SET coordinator_epoch = $epoch, owner_instance_id = $owner, updated_utc_ticks = $now WHERE run_id = $run;",
                            ("$epoch", epoch), ("$owner", ownerInstanceId), ("$now", nowUtc.UtcTicks), ("$run", runId));
                        transaction.Commit();
                        return new GraphProjectionRun(runId, epoch, snapshot, state, complete);
                    }
                }

                var priorActiveRuns = Convert.ToInt32(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM graph_runs WHERE control_state NOT IN ($cancelled, $complete);",
                        ("$cancelled", State(GraphRunControlState.Cancelled)),
                        ("$complete", State(GraphRunControlState.Complete))),
                    CultureInfo.InvariantCulture);
                if (priorActiveRuns > 0)
                {
                    epoch = checked(epoch + 1);
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "UPDATE graph_coordinator_lease SET fencing_epoch = $epoch, lease_token = $token, heartbeat_utc_ticks = $now, expires_utc_ticks = $expiry, heartbeat_sequence = heartbeat_sequence + 1 WHERE singleton_id = 1 AND owner_instance_id = $owner;",
                        ("$epoch", epoch), ("$token", $"coordinator-{Guid.NewGuid():N}"),
                        ("$now", nowUtc.UtcTicks), ("$expiry", (nowUtc + GraphLimits.ClaimLeaseTimeToLive).UtcTicks),
                        ("$owner", ownerInstanceId));
                    CancelSupersededProjectionRuns(connection, transaction, nowUtc);
                }

                var runIdNew = $"graph-run-{Guid.NewGuid():N}";
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_runs(
                        run_id, control_state, control_sequence, freshness_state, integrity_state,
                        reason, settings_fingerprint, coordinator_epoch, owner_instance_id,
                        snapshot_manifest_id, snapshot_revision, legacy_decision_manifest_id,
                        privacy_sequence, graph_decision_sequence, graph_decision_checkpoint_id,
                        expected_observation_count, expected_manifest_hash,
                        input_manifest_complete, created_utc_ticks, updated_utc_ticks)
                    VALUES ($run, $control, 0, $freshness, $integrity, 'projection', 'v2-stable',
                            $epoch, $owner, $manifest, $revision, $legacy, $privacy, $decisionSequence,
                            $decisionCheckpoint, $count, $hash, 0, $now, $now);
                    """,
                    ("$run", runIdNew), ("$control", State(GraphRunControlState.Running)),
                    ("$freshness", State(GraphFreshnessState.Stale)), ("$integrity", State(GraphIntegrityState.Valid)),
                    ("$epoch", epoch), ("$owner", ownerInstanceId), ("$manifest", snapshot.ManifestId),
                    ("$revision", snapshot.Revision), ("$legacy", snapshot.LegacyDecisionManifestId),
                    ("$privacy", snapshot.PrivacySequence), ("$decisionSequence", snapshot.GraphDecisionSequence),
                    ("$decisionCheckpoint", snapshot.GraphDecisionCheckpointId), ("$count", snapshot.TotalObservationCount),
                    ("$hash", snapshot.CanonicalManifestHash), ("$now", nowUtc.UtcTicks));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_manifests(
                        manifest_id, source_id, scope, state, source_schema_version,
                        terminal_row_count, canonical_aggregate_hash, started_utc_ticks, is_active)
                    VALUES ($manifest, 'deep-index', 'complete-schema3', 'Capturing', 3, $count, $hash, $now, 0)
                    ON CONFLICT(manifest_id) DO NOTHING;
                    """,
                    ("$manifest", snapshot.ManifestId), ("$count", snapshot.TotalObservationCount),
                    ("$hash", snapshot.CanonicalManifestHash), ("$now", nowUtc.UtcTicks));
                PruneProjectionRecoveryHistory(connection, transaction);
                transaction.Commit();
                return new GraphProjectionRun(runIdNew, epoch, snapshot, GraphRunControlState.Running, false);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphProjectionClaim?> TryClaimNextAsync(
        GraphProjectionRun run,
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        TimeSpan leaseTimeToLive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ValidateStableId(ownerInstanceId, nameof(ownerInstanceId));
        ValidateLease(leaseTimeToLive);
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                EnsureRunAndCoordinator(connection, transaction, run, ownerInstanceId, nowUtc, requireRunning: true);
                string? jobId = null;
                using (var select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandText =
                        """
                        SELECT candidate.job_id
                        FROM graph_jobs candidate
                        WHERE candidate.run_id = $run
                          AND candidate.execution_state IN ($pending, $retry)
                          AND (candidate.next_eligible_utc_ticks IS NULL OR candidate.next_eligible_utc_ticks <= $now)
                          AND NOT EXISTS (
                              SELECT 1
                              FROM graph_jobs prerequisite
                              WHERE prerequisite.run_id = candidate.run_id
                                AND prerequisite.execution_state NOT IN ($complete, $cancelled, $permanent)
                                AND (
                                    (candidate.observation_kind IN ('File', 'Collection')
                                     AND prerequisite.observation_kind IN ('Source', 'LegacyDecision'))
                                    OR
                                    (candidate.observation_kind IN ('Relationship', 'CollectionMembership')
                                     AND prerequisite.observation_kind IN ('Source', 'LegacyDecision', 'File', 'Collection'))
                                    OR
                                    (candidate.observation_kind = 'Deletion'
                                     AND prerequisite.observation_kind <> 'Deletion')))
                        ORDER BY candidate.priority DESC, candidate.created_utc_ticks, candidate.job_id
                        LIMIT 1;
                        """;
                    AddParameters(
                        select,
                        ("$run", run.RunId), ("$pending", State(GraphJobExecutionState.Pending)),
                        ("$retry", State(GraphJobExecutionState.RetryableFailure)), ("$now", nowUtc.UtcTicks),
                        ("$complete", State(GraphJobExecutionState.Complete)),
                        ("$cancelled", State(GraphJobExecutionState.Cancelled)),
                        ("$permanent", State(GraphJobExecutionState.PermanentFailure)));
                    jobId = select.ExecuteScalar() as string;
                }

                if (jobId is null)
                {
                    transaction.Commit();
                    return null;
                }

                var token = $"claim-{Guid.NewGuid():N}";
                var expiry = nowUtc + leaseTimeToLive;
                var updated = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_jobs
                    SET execution_state = $running,
                        current_attempt = current_attempt + 1,
                        claim_owner_instance_id = $owner,
                        claim_token = $token,
                        claim_fencing_epoch = $epoch,
                        claim_heartbeat_utc_ticks = $now,
                        claim_expires_utc_ticks = $expiry,
                        failure_category = NULL,
                        next_eligible_utc_ticks = NULL,
                        updated_utc_ticks = $now
                    WHERE job_id = $job AND execution_state IN ($pending, $retry);
                    """,
                    ("$running", State(GraphJobExecutionState.Running)), ("$owner", ownerInstanceId),
                    ("$token", token), ("$epoch", run.FencingEpoch), ("$now", nowUtc.UtcTicks),
                    ("$expiry", expiry.UtcTicks), ("$job", jobId),
                    ("$pending", State(GraphJobExecutionState.Pending)), ("$retry", State(GraphJobExecutionState.RetryableFailure)));
                if (updated != 1)
                {
                    transaction.Rollback();
                    return null;
                }

                var item = ReadWorkItem(connection, transaction, jobId, GraphRunControlState.Running);
                var attemptId = $"attempt-{Guid.NewGuid():N}";
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_job_attempts(
                        attempt_id, job_id, attempt_number, owner_instance_id, claim_token,
                        fencing_epoch, heartbeat_sequence, started_utc_ticks, heartbeat_utc_ticks, expires_utc_ticks)
                    VALUES ($attempt, $job, $number, $owner, $token, $epoch, 0, $now, $now, $expiry);
                    """,
                    ("$attempt", attemptId), ("$job", jobId), ("$number", item.Attempt),
                    ("$owner", ownerInstanceId), ("$token", token), ("$epoch", run.FencingEpoch),
                    ("$now", nowUtc.UtcTicks), ("$expiry", expiry.UtcTicks));
                HeartbeatCoordinator(connection, transaction, ownerInstanceId, run.FencingEpoch, nowUtc);
                transaction.Commit();
                return new GraphProjectionClaim(item, token, ownerInstanceId, run.FencingEpoch, nowUtc, expiry);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RenewClaimAsync(
        GraphProjectionClaim claim,
        DateTimeOffset nowUtc,
        TimeSpan leaseTimeToLive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateLease(leaseTimeToLive);
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (!CoordinatorIsCurrent(connection, transaction, claim.OwnerInstanceId, claim.FencingEpoch, nowUtc))
                {
                    transaction.Rollback();
                    return false;
                }

                var expiry = nowUtc + leaseTimeToLive;
                var updated = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_jobs
                    SET claim_heartbeat_utc_ticks = $now,
                        claim_expires_utc_ticks = $expiry,
                        updated_utc_ticks = $now
                    WHERE job_id = $job AND execution_state = $running
                      AND claim_owner_instance_id = $owner AND claim_token = $token
                      AND claim_fencing_epoch = $epoch AND claim_expires_utc_ticks >= $now;
                    """,
                    ("$now", nowUtc.UtcTicks), ("$expiry", expiry.UtcTicks),
                    ("$job", claim.WorkItem.WorkId), ("$running", State(GraphJobExecutionState.Running)),
                    ("$owner", claim.OwnerInstanceId), ("$token", claim.ClaimToken), ("$epoch", claim.FencingEpoch));
                if (updated == 1)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE graph_job_attempts
                        SET heartbeat_sequence = heartbeat_sequence + 1,
                            heartbeat_utc_ticks = $now,
                            expires_utc_ticks = $expiry
                        WHERE job_id = $job AND attempt_number = $attempt
                          AND owner_instance_id = $owner AND claim_token = $token AND fencing_epoch = $epoch
                          AND completed_utc_ticks IS NULL;
                        """,
                        ("$now", nowUtc.UtcTicks), ("$expiry", expiry.UtcTicks),
                        ("$job", claim.WorkItem.WorkId), ("$attempt", claim.WorkItem.Attempt),
                        ("$owner", claim.OwnerInstanceId), ("$token", claim.ClaimToken), ("$epoch", claim.FencingEpoch));
                    HeartbeatCoordinator(connection, transaction, claim.OwnerInstanceId, claim.FencingEpoch, nowUtc);
                }

                transaction.Commit();
                return updated == 1;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphProjectionClaim?> AdvanceClaimStageAsync(
        GraphProjectionClaim claim,
        GraphProjectionStageTransition transition,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(transition);
        ValidateStableId(transition.InputFingerprint, nameof(transition));
        if (!IsAllowedPureStageTransition(transition.ExpectedStage, transition.CompletedStage))
        {
            throw new ArgumentException("A projection claim can advance by exactly one pure stage before publication.", nameof(transition));
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (!ClaimIsCurrent(connection, transaction, claim, nowUtc) ||
                    claim.WorkItem.Stage != transition.ExpectedStage ||
                    transition.ExpectedStage != GraphProjectionStage.ObservationCaptured &&
                    !string.Equals(claim.WorkItem.StageInputFingerprint, transition.InputFingerprint, StringComparison.Ordinal))
                {
                    transaction.Rollback();
                    return null;
                }

                var previousStageTicks = Convert.ToInt64(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT updated_utc_ticks FROM graph_jobs WHERE job_id = $job;",
                        ("$job", claim.WorkItem.WorkId))
                    ?? throw SqliteKnowledgeInfrastructure.Corrupt("The claimed graph job is missing."),
                    CultureInfo.InvariantCulture);

                var updated = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_jobs
                    SET stage = $completed,
                        stage_input_fingerprint = $fingerprint,
                        updated_utc_ticks = $now
                    WHERE job_id = $job
                      AND execution_state = $running
                      AND claim_owner_instance_id = $owner
                      AND claim_token = $token
                      AND claim_fencing_epoch = $epoch
                      AND claim_expires_utc_ticks >= $now
                      AND stage = $expected
                      AND (($expected = $captured AND stage_input_fingerprint IS NULL)
                           OR ($expected <> $captured AND stage_input_fingerprint = $fingerprint));
                    """,
                    ("$completed", State(transition.CompletedStage)),
                    ("$fingerprint", transition.InputFingerprint),
                    ("$now", nowUtc.UtcTicks),
                    ("$job", claim.WorkItem.WorkId),
                    ("$running", State(GraphJobExecutionState.Running)),
                    ("$owner", claim.OwnerInstanceId),
                    ("$token", claim.ClaimToken),
                    ("$epoch", claim.FencingEpoch),
                    ("$expected", State(transition.ExpectedStage)),
                    ("$captured", State(GraphProjectionStage.ObservationCaptured)));
                if (updated != 1)
                {
                    transaction.Rollback();
                    return null;
                }

                var workItem = ReadWorkItem(connection, transaction, claim.WorkItem.WorkId, GraphRunControlState.Running);
                RecordProjectionStageDuration(
                    connection,
                    transaction,
                    claim.WorkItem.RunId,
                    transition.CompletedStage,
                    previousStageTicks,
                    nowUtc);
                transaction.Commit();
                return claim with { WorkItem = workItem };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordClaimFailureAsync(
        GraphProjectionClaim claim,
        GraphProjectionFailure failure,
        GraphJobExecutionState state,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(failure);
        if (state is GraphJobExecutionState.Pending or GraphJobExecutionState.Running or GraphJobExecutionState.Complete)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "A claim failure requires a failed, waiting, or cancelled state.");
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (!ClaimIsCurrent(connection, transaction, claim, nowUtc))
                {
                    transaction.Rollback();
                    return 0;
                }

                var effectiveState = state == GraphJobExecutionState.RetryableFailure &&
                                     claim.WorkItem.Attempt >= GraphLimits.MaximumRetryCount
                    ? GraphJobExecutionState.PermanentFailure
                    : state;
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_jobs
                    SET execution_state = $state,
                        integrity_state = $integrity,
                        failure_category = $category,
                        waiting_reason = $waiting,
                        next_eligible_utc_ticks = $next,
                        claim_owner_instance_id = NULL,
                        claim_token = NULL,
                        claim_fencing_epoch = NULL,
                        claim_heartbeat_utc_ticks = NULL,
                        claim_expires_utc_ticks = NULL,
                        updated_utc_ticks = $now
                    WHERE job_id = $job;
                    """,
                    ("$state", State(effectiveState)),
                    ("$integrity", State(effectiveState == GraphJobExecutionState.PermanentFailure
                        ? GraphIntegrityState.RepairRequired
                        : GraphIntegrityState.Valid)),
                    ("$category", Bound(failure.Category, 128)),
                    ("$waiting", effectiveState is GraphJobExecutionState.WaitingForDependency or GraphJobExecutionState.WaitingForResources
                        ? Bound(failure.ErrorCode, 256)
                        : null),
                    ("$next", effectiveState == GraphJobExecutionState.RetryableFailure ? nowUtc.UtcTicks : null),
                    ("$now", nowUtc.UtcTicks), ("$job", claim.WorkItem.WorkId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_job_attempts
                    SET completed_utc_ticks = $now, outcome = $outcome, failure_category = $category
                    WHERE job_id = $job AND attempt_number = $attempt
                      AND owner_instance_id = $owner AND claim_token = $token AND fencing_epoch = $epoch
                      AND completed_utc_ticks IS NULL;
                    """,
                    ("$now", nowUtc.UtcTicks), ("$outcome", State(effectiveState)),
                    ("$category", Bound(failure.Category, 128)), ("$job", claim.WorkItem.WorkId),
                    ("$attempt", claim.WorkItem.Attempt), ("$owner", claim.OwnerInstanceId),
                    ("$token", claim.ClaimToken), ("$epoch", claim.FencingEpoch));
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphOperationResult> SetRunControlAsync(
        GraphRunControlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var runId = request.RunId ?? ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT run_id FROM graph_runs ORDER BY created_utc_ticks DESC, run_id DESC LIMIT 1;") as string;
                if (runId is null)
                {
                    return new GraphOperationResult(false, "No graph projection run exists.", 0);
                }

                var runningCount = Convert.ToInt32(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM graph_jobs WHERE run_id = $run AND execution_state = $running;",
                        ("$run", runId), ("$running", State(GraphJobExecutionState.Running))),
                    CultureInfo.InvariantCulture);
                var requested = request.RequestedState;
                var current = Parse<GraphRunControlState>(Convert.ToString(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT control_state FROM graph_runs WHERE run_id = $run;",
                        ("$run", runId)),
                    CultureInfo.InvariantCulture) ?? throw SqliteKnowledgeInfrastructure.Corrupt(
                        "A graph run is missing its control state."));

                // Cancellation is a durable terminal intent. A later Pause or
                // Resume request must not revive a cancelling/cancelled run.
                if (current is GraphRunControlState.CancelRequested or GraphRunControlState.Cancelled &&
                    requested is not GraphRunControlState.CancelRequested and not GraphRunControlState.Cancelled)
                {
                    transaction.Commit();
                    return new GraphOperationResult(
                        false,
                        $"Graph run cancellation already has precedence ({current}).",
                        0);
                }

                var effective = requested switch
                {
                    GraphRunControlState.PauseRequested when runningCount == 0 => GraphRunControlState.Paused,
                    GraphRunControlState.CancelRequested when runningCount == 0 => GraphRunControlState.Cancelled,
                    GraphRunControlState.Paused when runningCount > 0 => GraphRunControlState.PauseRequested,
                    GraphRunControlState.Cancelled when runningCount > 0 => GraphRunControlState.CancelRequested,
                    _ => requested,
                };
                if (effective is GraphRunControlState.CancelRequested or GraphRunControlState.Cancelled)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "UPDATE graph_jobs SET execution_state = $cancelled, updated_utc_ticks = $now WHERE run_id = $run AND execution_state <> $running AND execution_state <> $complete;",
                        ("$cancelled", State(GraphJobExecutionState.Cancelled)), ("$now", request.RequestedAtUtc.UtcTicks),
                        ("$run", runId), ("$running", State(GraphJobExecutionState.Running)),
                        ("$complete", State(GraphJobExecutionState.Complete)));
                }

                var affected = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_runs
                    SET control_state = $state,
                        control_sequence = control_sequence + 1,
                        cancellation_reason = $reason,
                        updated_utc_ticks = $now
                    WHERE run_id = $run
                      AND control_state <> $complete
                      AND NOT (
                          control_state IN ($cancelRequested, $cancelled)
                          AND $state NOT IN ($cancelRequested, $cancelled));
                    """,
                    ("$state", State(effective)), ("$reason", Bound(request.ReasonCode, 512)),
                    ("$now", request.RequestedAtUtc.UtcTicks), ("$run", runId),
                    ("$complete", State(GraphRunControlState.Complete)),
                    ("$cancelRequested", State(GraphRunControlState.CancelRequested)),
                    ("$cancelled", State(GraphRunControlState.Cancelled)));
                if (affected == 1 && effective == GraphRunControlState.Cancelled)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE graph_repair_operations
                        SET state = 'Cancelled',
                            completed_utc_ticks = $now,
                            records_repaired = (
                                SELECT COUNT(*) FROM graph_jobs
                                WHERE run_id = $run AND execution_state = $completeJob),
                            bounded_detail = 'Staged graph repair was cancelled; previously published component generations remain available.'
                        WHERE state = 'Running' AND bounded_detail = $run;
                        """,
                        ("$now", request.RequestedAtUtc.UtcTicks), ("$run", runId),
                        ("$completeJob", State(GraphJobExecutionState.Complete)));
                    PruneProjectionRecoveryHistory(connection, transaction);
                }
                transaction.Commit();
                return new GraphOperationResult(affected == 1, affected == 1 ? $"Graph run is {effective}." : "The graph run is already complete.", affected);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphOperationResult> CompleteProjectionAsync(
        GraphProjectionRun run,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                EnsureRunEpoch(connection, transaction, run, nowUtc, requireRunning: false);
                var remaining = Convert.ToInt64(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM graph_jobs WHERE run_id = $run AND execution_state <> $complete;",
                        ("$run", run.RunId), ("$complete", State(GraphJobExecutionState.Complete))),
                    CultureInfo.InvariantCulture);
                var manifestComplete = Convert.ToInt32(
                    ExecuteScalar(connection, transaction, "SELECT input_manifest_complete FROM graph_runs WHERE run_id = $run;", ("$run", run.RunId)) ?? 0,
                    CultureInfo.InvariantCulture) == 1;
                if (!manifestComplete || remaining != 0)
                {
                    return new GraphOperationResult(false, "Graph projection still has an incomplete manifest or active work.", 0);
                }
                var sourceIsCurrent = Convert.ToInt32(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM graph_watermarks WHERE source_id = 'deep-index' AND latest_complete_manifest_id = $manifest AND latest_complete_revision = $revision AND ingested_privacy_sequence = $privacy;",
                        ("$manifest", run.Snapshot.ManifestId), ("$revision", run.Snapshot.Revision),
                        ("$privacy", run.Snapshot.PrivacySequence)),
                    CultureInfo.InvariantCulture) == 1;
                if (!sourceIsCurrent)
                {
                    return new GraphOperationResult(false, "A newer source or privacy checkpoint superseded this projection run.", 0);
                }

                var decisionCanonicalHash = ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT ingested_decision_canonical_hash FROM graph_watermarks WHERE source_id = 'deep-index' AND ingested_decision_sequence = $sequence AND ingested_decision_checkpoint_id = $checkpoint;",
                    ("$sequence", run.Snapshot.GraphDecisionSequence),
                    ("$checkpoint", run.Snapshot.GraphDecisionCheckpointId)) as string;
                if (string.IsNullOrWhiteSpace(decisionCanonicalHash))
                {
                    return new GraphOperationResult(false, "Graph-native decisions have not been durably staged through the run checkpoint.", 0);
                }

                var retired = ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM graph_components WHERE component_key <> 'graph-native-decision-overlay' AND (source_manifest_id IS NULL OR source_manifest_id <> $manifest);",
                    ("$manifest", run.Snapshot.ManifestId));

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_jobs
                    SET stage = $cleaned, updated_utc_ticks = $now
                    WHERE run_id = $run AND execution_state = $complete AND stage = $published;
                    """,
                    ("$cleaned", State(GraphProjectionStage.StaleRowsCleaned)),
                    ("$now", nowUtc.UtcTicks), ("$run", run.RunId),
                    ("$complete", State(GraphJobExecutionState.Complete)),
                    ("$published", State(GraphProjectionStage.ComponentPublished)));

                PublishDecisionOverlay(
                    connection,
                    transaction,
                    new GraphDecisionSnapshot(
                        run.Snapshot.GraphDecisionSequence,
                        run.Snapshot.GraphDecisionCheckpointId,
                        decisionCanonicalHash,
                        true),
                    nowUtc,
                    cancellationToken);

                var affected = ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE graph_runs SET control_state = $complete, freshness_state = $current, completed_utc_ticks = $now, updated_utc_ticks = $now WHERE run_id = $run AND coordinator_epoch = $epoch;",
                    ("$complete", State(GraphRunControlState.Complete)), ("$current", State(GraphFreshnessState.Current)),
                    ("$now", nowUtc.UtcTicks), ("$run", run.RunId), ("$epoch", run.FencingEpoch));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_watermarks
                    SET applied_manifest_id = $manifest,
                        applied_revision = $revision,
                        applied_decision_sequence = $decisionSequence,
                        applied_decision_checkpoint_id = $decisionCheckpoint,
                        applied_privacy_sequence = $privacy,
                        updated_utc_ticks = $now
                    WHERE source_id = 'deep-index'
                      AND latest_complete_manifest_id = $manifest
                      AND latest_complete_revision = $revision;
                    """,
                    ("$manifest", run.Snapshot.ManifestId), ("$revision", run.Snapshot.Revision),
                    ("$decisionSequence", run.Snapshot.GraphDecisionSequence),
                    ("$decisionCheckpoint", run.Snapshot.GraphDecisionCheckpointId),
                    ("$privacy", run.Snapshot.PrivacySequence), ("$now", nowUtc.UtcTicks));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM graph_decision_projection_staging WHERE checkpoint_id <> $checkpoint;",
                    ("$checkpoint", run.Snapshot.GraphDecisionCheckpointId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_repair_operations
                    SET state = 'Complete',
                        completed_utc_ticks = $now,
                        records_repaired = (
                            SELECT COUNT(*) FROM graph_jobs
                            WHERE run_id = $run AND execution_state = $completeJob),
                        bounded_detail = 'Staged graph repair completed; validated replacement generations were published atomically.'
                    WHERE state = 'Running' AND bounded_detail = $run;
                    """,
                    ("$now", nowUtc.UtcTicks), ("$run", run.RunId),
                    ("$completeJob", State(GraphJobExecutionState.Complete)));
                var pruned = PruneProjectionRecoveryHistory(connection, transaction);
                transaction.Commit();
                return new GraphOperationResult(
                    affected == 1,
                    affected == 1 ? "Graph projection completed." : "The graph run fencing epoch is stale.",
                    affected + retired + pruned);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphOperationResult> RetryFailedAsync(
        string? workId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                var affected = SqliteKnowledgeInfrastructure.ExecuteNonQuery(
                    connection,
                    """
                    UPDATE graph_jobs
                    SET execution_state = $pending,
                        failure_category = NULL,
                        waiting_reason = NULL,
                        next_eligible_utc_ticks = $now,
                        updated_utc_ticks = $now
                    WHERE execution_state IN ($retry, $dependency, $resources)
                      AND ($work IS NULL OR job_id = $work);
                    """,
                    ("$pending", State(GraphJobExecutionState.Pending)), ("$now", nowUtc.UtcTicks),
                    ("$retry", State(GraphJobExecutionState.RetryableFailure)),
                    ("$dependency", State(GraphJobExecutionState.WaitingForDependency)),
                    ("$resources", State(GraphJobExecutionState.WaitingForResources)), ("$work", workId));
                return new GraphOperationResult(true, "Eligible graph work was returned to the pending queue.", affected);
            },
            cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposeState, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        var readersAcquired = 0;
        for (var index = 0; index < MaximumConcurrentReaders; index++)
        {
            await _readerGate.WaitAsync().ConfigureAwait(false);
            readersAcquired++;
        }

        // Keep the managed admission semaphores alive. A caller may have passed
        // its initial disposal check immediately before disposal began and still
        // be about to wait. Releasing admission lets that caller reach the second
        // disposal check and fail instead of remaining stranded on a disposed gate.
        _readerGate.Release(readersAcquired);
        _gate.Release();
    }

    private GraphProjectionWorkItem ReadWorkItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        GraphRunControlState runState)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT j.run_id, j.execution_state, j.freshness_state, j.integrity_state,
                   j.current_attempt, i.payload_json, j.stage, j.stage_input_fingerprint
            FROM graph_jobs j
            JOIN graph_observation_inbox i ON i.observation_sequence = j.observation_sequence
            WHERE j.job_id = $job;
            """;
        command.Parameters.AddWithValue("$job", jobId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("A claimed graph job has no durable observation.");
        }

        return new GraphProjectionWorkItem(
            jobId,
            reader.GetString(0),
            GraphCanonicalSerializer.DeserializeObservation(reader.GetString(5)),
            new GraphStateVector(
                runState,
                Parse<GraphJobExecutionState>(reader.GetString(1)),
                Parse<GraphFreshnessState>(reader.GetString(2)),
                Parse<GraphIntegrityState>(reader.GetString(3))),
            reader.GetInt32(4))
        {
            Stage = Parse<GraphProjectionStage>(reader.GetString(6)),
            StageInputFingerprint = reader.IsDBNull(7) ? null : reader.GetString(7),
        };
    }

    private static bool IsAllowedPureStageTransition(
        GraphProjectionStage expected,
        GraphProjectionStage completed) =>
        (expected, completed) is
            (GraphProjectionStage.ObservationCaptured, GraphProjectionStage.CandidatesExtracted) or
            (GraphProjectionStage.CandidatesExtracted, GraphProjectionStage.CandidatesNormalized) or
            (GraphProjectionStage.CandidatesNormalized, GraphProjectionStage.IdentityResolved) or
            (GraphProjectionStage.IdentityResolved, GraphProjectionStage.EdgesPrepared) or
            (GraphProjectionStage.EdgesPrepared, GraphProjectionStage.ComponentValidated);

    private static bool ClaimStageIsCurrent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphProjectionClaim claim,
        GraphProjectionStage expectedStage,
        string expectedFingerprint)
    {
        var count = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*) FROM graph_jobs
                WHERE job_id = $job AND execution_state = $running
                  AND claim_owner_instance_id = $owner AND claim_token = $token
                  AND claim_fencing_epoch = $epoch AND stage = $stage
                  AND stage_input_fingerprint = $fingerprint;
                """,
                ("$job", claim.WorkItem.WorkId), ("$running", State(GraphJobExecutionState.Running)),
                ("$owner", claim.OwnerInstanceId), ("$token", claim.ClaimToken),
                ("$epoch", claim.FencingEpoch), ("$stage", State(expectedStage)),
                ("$fingerprint", expectedFingerprint)),
            CultureInfo.InvariantCulture);
        return count == 1 && claim.WorkItem.Stage == expectedStage &&
               string.Equals(claim.WorkItem.StageInputFingerprint, expectedFingerprint, StringComparison.Ordinal);
    }

    private long AcquireOrRenewCoordinator(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string ownerInstanceId,
        DateTimeOffset nowUtc)
    {
        string? existingOwner = null;
        long existingEpoch = 0;
        long existingExpiry = 0;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT owner_instance_id, fencing_epoch, expires_utc_ticks FROM graph_coordinator_lease WHERE singleton_id = 1;";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                existingOwner = reader.GetString(0);
                existingEpoch = reader.GetInt64(1);
                existingExpiry = reader.GetInt64(2);
            }
        }

        if (existingOwner is not null && !string.Equals(existingOwner, ownerInstanceId, StringComparison.Ordinal) && existingExpiry > nowUtc.UtcTicks)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Busy,
                "Another OmniSorSe process owns the knowledge-graph coordinator lease.");
        }

        var epoch = existingOwner is null || string.Equals(existingOwner, ownerInstanceId, StringComparison.Ordinal)
            ? Math.Max(existingEpoch, 1)
            : checked(existingEpoch + 1);
        var token = $"coordinator-{Guid.NewGuid():N}";
        var expiry = nowUtc + GraphLimits.ClaimLeaseTimeToLive;
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO graph_coordinator_lease(
                singleton_id, owner_instance_id, fencing_epoch, lease_token, process_start_identity,
                acquired_utc_ticks, heartbeat_utc_ticks, expires_utc_ticks, heartbeat_sequence)
            VALUES (1, $owner, $epoch, $token, $process, $now, $now, $expiry, 0)
            ON CONFLICT(singleton_id) DO UPDATE SET
                owner_instance_id = excluded.owner_instance_id,
                fencing_epoch = excluded.fencing_epoch,
                lease_token = excluded.lease_token,
                process_start_identity = excluded.process_start_identity,
                acquired_utc_ticks = excluded.acquired_utc_ticks,
                heartbeat_utc_ticks = excluded.heartbeat_utc_ticks,
                expires_utc_ticks = excluded.expires_utc_ticks,
                heartbeat_sequence = graph_coordinator_lease.heartbeat_sequence + 1;
            """,
            ("$owner", ownerInstanceId), ("$epoch", epoch), ("$token", token),
            ("$process", ProcessIdentity), ("$now", nowUtc.UtcTicks), ("$expiry", expiry.UtcTicks));
        return epoch;
    }

    private static string ProcessIdentity =>
        $"{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}:{Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)}";

    private static bool CoordinatorIsCurrent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string owner,
        long epoch,
        DateTimeOffset nowUtc) =>
        Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM graph_coordinator_lease WHERE singleton_id = 1 AND owner_instance_id = $owner AND fencing_epoch = $epoch AND expires_utc_ticks >= $now;",
                ("$owner", owner), ("$epoch", epoch), ("$now", nowUtc.UtcTicks)),
            CultureInfo.InvariantCulture) == 1;

    private static bool ClaimIsCurrent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphProjectionClaim claim,
        DateTimeOffset nowUtc) =>
        CoordinatorIsCurrent(connection, transaction, claim.OwnerInstanceId, claim.FencingEpoch, nowUtc) &&
        Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*) FROM graph_jobs
                WHERE job_id = $job AND execution_state = $running
                  AND claim_owner_instance_id = $owner AND claim_token = $token
                  AND claim_fencing_epoch = $epoch AND claim_expires_utc_ticks >= $now;
                """,
                ("$job", claim.WorkItem.WorkId), ("$running", State(GraphJobExecutionState.Running)),
                ("$owner", claim.OwnerInstanceId), ("$token", claim.ClaimToken),
                ("$epoch", claim.FencingEpoch), ("$now", nowUtc.UtcTicks)),
            CultureInfo.InvariantCulture) == 1;

    private static void HeartbeatCoordinator(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string owner,
        long epoch,
        DateTimeOffset nowUtc) =>
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE graph_coordinator_lease
            SET heartbeat_utc_ticks = $now,
                expires_utc_ticks = $expiry,
                heartbeat_sequence = heartbeat_sequence + 1
            WHERE singleton_id = 1 AND owner_instance_id = $owner AND fencing_epoch = $epoch;
            """,
            ("$now", nowUtc.UtcTicks), ("$expiry", (nowUtc + GraphLimits.ClaimLeaseTimeToLive).UtcTicks),
            ("$owner", owner), ("$epoch", epoch));

    private static void EnsureRunAndCoordinator(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphProjectionRun run,
        string owner,
        DateTimeOffset nowUtc,
        bool requireRunning)
    {
        if (!CoordinatorIsCurrent(connection, transaction, owner, run.FencingEpoch, nowUtc))
        {
            throw new SqliteKnowledgeStoreException(SqliteKnowledgeFailureKind.Busy, "The graph coordinator fencing epoch is stale.");
        }

        var requiredState = requireRunning ? State(GraphRunControlState.Running) : null;
        var count = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM graph_runs WHERE run_id = $run AND coordinator_epoch = $epoch AND owner_instance_id = $owner AND ($state IS NULL OR control_state = $state);",
                ("$run", run.RunId), ("$epoch", run.FencingEpoch), ("$owner", owner), ("$state", requiredState)),
            CultureInfo.InvariantCulture);
        if (count != 1)
        {
            throw new InvalidOperationException("The graph projection run is no longer claimable.");
        }
    }

    private GraphCoordinatorStatus ReadStatus(SqliteConnection connection)
    {
        string? runId = null;
        long epoch = 0;
        var control = GraphRunControlState.Pending;
        var manifestComplete = false;
        string? currentStage = null;
        string? currentWork = null;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT run_id, coordinator_epoch, control_state, input_manifest_complete, current_stage, current_work_label FROM graph_runs ORDER BY created_utc_ticks DESC, run_id DESC LIMIT 1;";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                runId = reader.GetString(0);
                epoch = reader.GetInt64(1);
                control = Parse<GraphRunControlState>(reader.GetString(2));
                manifestComplete = reader.GetBoolean(3);
                currentStage = reader.IsDBNull(4) ? null : reader.GetString(4);
                currentWork = reader.IsDBNull(5) ? null : reader.GetString(5);
            }
        }

        var counts = new Dictionary<GraphJobExecutionState, long>();
        if (runId is not null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT execution_state, COUNT(*) FROM graph_jobs WHERE run_id = $run GROUP BY execution_state;";
            command.Parameters.AddWithValue("$run", runId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                counts[Parse<GraphJobExecutionState>(reader.GetString(0))] = reader.GetInt64(1);
            }
        }

        var coverage = ReadCoverage(connection, runId);
        var quotaBlocked = string.Equals(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT value FROM graph_meta WHERE key = 'quota_blocked';") as string,
            "1",
            StringComparison.Ordinal);
        var quotaWaiting = runId is not null && quotaBlocked &&
            (string.Equals(currentStage, State(GraphJobExecutionState.WaitingForResources), StringComparison.Ordinal) ||
             string.Equals(currentWork, "graph-storage-quota", StringComparison.Ordinal) ||
             string.Equals(currentWork, "graph-decision-quota", StringComparison.Ordinal));
        var repairRequired = Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT COUNT(*) FROM graph_components WHERE integrity_state = $repair;",
                ("$repair", State(GraphIntegrityState.RepairRequired))),
            CultureInfo.InvariantCulture);
        var processed = counts.GetValueOrDefault(GraphJobExecutionState.Complete);
        var retainedWaiting = counts.GetValueOrDefault(GraphJobExecutionState.WaitingForDependency) +
                              counts.GetValueOrDefault(GraphJobExecutionState.WaitingForResources);
        var effectiveWaiting = retainedWaiting + (quotaWaiting && retainedWaiting == 0 ? 1 : 0);
        return new GraphCoordinatorStatus
        {
            IsEnabled = ReadEnabled(connection),
            RunId = runId,
            FencingEpoch = epoch,
            RunControl = control,
            ActiveJobState = counts.GetValueOrDefault(GraphJobExecutionState.Running) > 0
                ? GraphJobExecutionState.Running
                : quotaWaiting
                    ? GraphJobExecutionState.WaitingForResources
                    : null,
            Freshness = coverage.IsStale ? GraphFreshnessState.Stale : GraphFreshnessState.Current,
            Integrity = repairRequired > 0 ? GraphIntegrityState.RepairRequired : GraphIntegrityState.Valid,
            CurrentStage = currentStage,
            InputManifestComplete = manifestComplete,
            PendingCount = counts.GetValueOrDefault(GraphJobExecutionState.Pending),
            RunningCount = counts.GetValueOrDefault(GraphJobExecutionState.Running),
            CompletedCount = counts.GetValueOrDefault(GraphJobExecutionState.Complete),
            RetryableFailureCount = counts.GetValueOrDefault(GraphJobExecutionState.RetryableFailure),
            PermanentFailureCount = counts.GetValueOrDefault(GraphJobExecutionState.PermanentFailure),
            CancelledCount = counts.GetValueOrDefault(GraphJobExecutionState.Cancelled),
            WaitingCount = effectiveWaiting,
            ProcessedObservationCount = processed,
            TotalObservationCount = coverage.TotalObservationCount,
            RemainingObservationCount = Math.Max(0, coverage.TotalObservationCount - processed),
            StorageSizeBytes = PhysicalSize,
            MaximumStorageSizeBytes = ReadMaximumDatabaseBytes(connection),
            StorageBreakdown = ReadStorageBreakdown(connection),
            Maintenance = ReadMaintenanceStatus(connection),
            CurrentWorkLabel = currentWork,
            Coverage = coverage,
            Message = quotaWaiting
                ? "Knowledge Graph ingestion is waiting for storage quota or recovery reserve."
                : runId is null
                    ? "Knowledge Graph is ready for its first completed manifest."
                    : $"Knowledge Graph run is {control}.",
        };
    }

    private GraphProjectionCoverage ReadCoverage(SqliteConnection connection, string? selectedRunId = null)
    {
        var runId = selectedRunId ?? SqliteKnowledgeInfrastructure.ExecuteScalar(
            connection,
            "SELECT run_id FROM graph_runs ORDER BY created_utc_ticks DESC, run_id DESC LIMIT 1;") as string;
        if (runId is null)
        {
            var enabled = ReadEnabled(connection);
            return new GraphProjectionCoverage(enabled, enabled, false, false, 0, 0, 0, 0, null, 0, enabled ? "Graph projection has not started." : "Knowledge Graph is disabled.");
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.snapshot_manifest_id, r.snapshot_revision, r.input_manifest_complete,
                   r.control_state, r.expected_observation_count,
                   SUM(CASE WHEN j.execution_state = $complete THEN 1 ELSE 0 END),
                   SUM(CASE WHEN j.execution_state IN ($retry, $permanent) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN j.execution_state IN ($dependency, $resources) THEN 1 ELSE 0 END),
                   r.privacy_sequence, r.graph_decision_sequence, r.graph_decision_checkpoint_id,
                   r.reason
            FROM graph_runs r
            LEFT JOIN graph_jobs j ON j.run_id = r.run_id
            WHERE r.run_id = $run
            GROUP BY r.run_id;
            """;
        AddParameters(
            command,
            ("$complete", State(GraphJobExecutionState.Complete)),
            ("$retry", State(GraphJobExecutionState.RetryableFailure)),
            ("$permanent", State(GraphJobExecutionState.PermanentFailure)),
            ("$dependency", State(GraphJobExecutionState.WaitingForDependency)),
            ("$resources", State(GraphJobExecutionState.WaitingForResources)),
            ("$run", runId));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new GraphProjectionCoverage(true, false, false, true, 0, 0, 0, 0, null, 0, "Graph projection state is unavailable.");
        }

        var completed = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
        var failed = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
        var waiting = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
        var control = Parse<GraphRunControlState>(reader.GetString(3));
        var inputComplete = reader.GetBoolean(2);
        var manifestId = reader.GetString(0);
        var revision = reader.GetInt64(1);
        var total = reader.GetInt64(4);
        var runPrivacy = reader.GetInt64(8);
        var runDecision = reader.GetInt64(9);
        var runCheckpoint = reader.GetString(10);
        var runReason = reader.GetString(11);
        reader.Close();
        var watermark = ReadWatermark(connection);
        var isEnabled = ReadEnabled(connection);
        var authorityMatchesAppliedGraph =
            string.Equals(watermark.AppliedManifestId, manifestId, StringComparison.Ordinal) &&
            watermark.AppliedRevision == revision && watermark.AppliedPrivacySequence == runPrivacy &&
            watermark.AppliedDecisionSequence == runDecision &&
            string.Equals(watermark.AppliedDecisionCheckpointId, runCheckpoint, StringComparison.Ordinal);
        var exactCoverage = inputComplete && control == GraphRunControlState.Complete && authorityMatchesAppliedGraph;
        var stagedRepairRetainsAppliedGraph = inputComplete &&
            runReason.StartsWith("repair:", StringComparison.Ordinal) &&
            authorityMatchesAppliedGraph;
        return new GraphProjectionCoverage(
            isEnabled,
            isEnabled,
            exactCoverage,
            !exactCoverage && !stagedRepairRetainsAppliedGraph,
            completed,
            total,
            failed,
            waiting,
            manifestId,
            revision,
            exactCoverage
                ? "Graph coverage is based on the exact source, decision, and privacy checkpoints."
                : stagedRepairRetainsAppliedGraph
                    ? "A staged graph repair is in progress; the last validated generation remains available until each replacement is published."
                    : "Graph coverage is still reconciling an authoritative checkpoint.")
        {
            IngestedManifestId = watermark.IngestedManifestId,
            IngestedRevision = watermark.IngestedRevision,
            AppliedManifestId = watermark.AppliedManifestId,
            AppliedRevision = watermark.AppliedRevision,
            IngestedDecisionSequence = watermark.IngestedDecisionSequence,
            IngestedDecisionCheckpointId = watermark.IngestedDecisionCheckpointId,
            AppliedDecisionSequence = watermark.AppliedDecisionSequence,
            AppliedDecisionCheckpointId = watermark.AppliedDecisionCheckpointId,
            IngestedPrivacySequence = watermark.IngestedPrivacySequence,
            AppliedPrivacySequence = watermark.AppliedPrivacySequence,
        };
    }

    private static GraphWatermark ReadWatermark(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT latest_complete_manifest_id, latest_complete_revision,
                   applied_manifest_id, applied_revision,
                   ingested_decision_sequence, ingested_decision_checkpoint_id,
                   applied_decision_sequence, applied_decision_checkpoint_id,
                   ingested_privacy_sequence, applied_privacy_sequence
            FROM graph_watermarks WHERE source_id = 'deep-index';
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new GraphWatermark(null, 0, null, 0, 0, null, 0, null, 0, 0);
        }

        return new GraphWatermark(
            reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3),
            reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt64(8), reader.GetInt64(9));
    }

    private sealed record GraphWatermark(
        string? IngestedManifestId,
        long IngestedRevision,
        string? AppliedManifestId,
        long AppliedRevision,
        long IngestedDecisionSequence,
        string? IngestedDecisionCheckpointId,
        long AppliedDecisionSequence,
        string? AppliedDecisionCheckpointId,
        long IngestedPrivacySequence,
        long AppliedPrivacySequence);

    private SqliteConnection OpenConnection()
    {
        var cancellationToken = _writerCancellationToken;
        var connection = SqliteKnowledgeInfrastructure.OpenConnection(
            _databasePath,
            cancellationToken: cancellationToken);
        if (cancellationToken.CanBeCanceled)
        {
            Interlocked.Exchange(ref _activeWriterConnection, connection);
            if (cancellationToken.IsCancellationRequested)
            {
                Interlocked.CompareExchange(ref _activeWriterConnection, null, connection);
                connection.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        connection.CreateFunction<string, string, string, string, string>(
            "graph_relationship_key",
            RelationshipSuppressionKey,
            isDeterministic: true);
        return connection;
    }

    private SqliteConnection OpenReadConnection()
    {
        var connection = SqliteKnowledgeInfrastructure.OpenConnection(
            _databasePath,
            readOnly: true,
            pooling: false);
        connection.CreateFunction<string, string, string, string, string>(
            "graph_relationship_key",
            RelationshipSuppressionKey,
            isDeterministic: true);
        SqliteKnowledgeInfrastructure.ExecuteNonQuery(connection, "BEGIN;");
        return connection;
    }

    private static string RelationshipSuppressionKey(
        string sourceNodeId,
        string targetNodeId,
        string relationshipKind,
        string relationshipScope)
    {
        var first = string.CompareOrdinal(sourceNodeId, targetNodeId) <= 0 ? sourceNodeId : targetNodeId;
        var second = string.Equals(first, sourceNodeId, StringComparison.Ordinal) ? targetNodeId : sourceNodeId;
        return string.Concat(
            "relationship:",
            GraphCanonicalSerializer.Hash($"{first}|{second}|{relationshipKind}|{relationshipScope}").ToLowerInvariant());
    }

    private bool ReadEnabled(SqliteConnection connection) =>
        string.Equals(
            SqliteKnowledgeInfrastructure.ExecuteScalar(connection, "SELECT value FROM graph_meta WHERE key = 'enabled';") as string,
            "1",
            StringComparison.Ordinal) && ReadAuthoritativeDecisionControlEnabled();

    private bool ReadAuthoritativeDecisionControlEnabled()
    {
        if (!File.Exists(_decisionDatabasePath))
        {
            return false;
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _decisionDatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT settings_version, settings_json, settings_fingerprint FROM graph_settings WHERE singleton_id = 1;";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            var revision = reader.GetInt64(0);
            var json = reader.GetString(1);
            var fingerprint = reader.GetString(2);
            var settings = JsonSerializer.Deserialize<GraphControlSettings>(json, ProjectionJsonOptions);
            return settings is not null && settings.IsEnabled && settings.ConsentConfirmed &&
                   settings.Revision == revision && revision >= 0 &&
                   string.Equals(fingerprint, GraphCanonicalSerializer.Hash(json), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          SqliteException or JsonException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private bool ReadQueriesAllowed(SqliteConnection connection)
    {
        if (!ReadEnabled(connection))
        {
            return false;
        }

        var coverage = ReadCoverage(connection);
        return coverage.IsAvailable && !coverage.IsStale;
    }

    private static void ValidateAndRecoverGraphState(SqliteConnection connection, DateTimeOffset nowUtc)
    {
        using var transaction = connection.BeginTransaction();
        var invalidWatermarks = Convert.ToInt64(
            ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*) FROM graph_watermarks
                WHERE applied_observation_sequence > ingested_observation_sequence
                   OR applied_privacy_sequence > ingested_privacy_sequence
                   OR applied_revision > latest_complete_revision
                   OR (applied_manifest_id IS NULL) <> (applied_revision = 0)
                   OR (ingested_decision_checkpoint_id IS NULL AND ingested_decision_sequence <> 0)
                   OR (applied_decision_checkpoint_id IS NULL AND applied_decision_sequence <> 0);
                """),
            CultureInfo.InvariantCulture);
        var missingActiveGenerations = Convert.ToInt64(
            ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM graph_components c
                LEFT JOIN graph_generations g
                  ON g.component_key = c.component_key AND g.generation = c.active_generation
                WHERE c.active_generation IS NULL OR g.generation IS NULL OR g.state <> 'Published';
                """),
            CultureInfo.InvariantCulture);
        var brokenDecisionStaging = Convert.ToInt64(
            ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*) FROM (
                    SELECT checkpoint_id
                    FROM graph_decision_projection_staging
                    GROUP BY checkpoint_id
                    HAVING MIN(decision_sequence) <> 1 OR COUNT(*) <> MAX(decision_sequence));
                """),
            CultureInfo.InvariantCulture);
        if (invalidWatermarks != 0 || missingActiveGenerations != 0 || brokenDecisionStaging != 0)
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The graph store contains invalid watermarks, generations, or decision staging state.");
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM graph_generations WHERE state = 'Staging' AND NOT EXISTS (SELECT 1 FROM graph_components c WHERE c.component_key = graph_generations.component_key AND c.active_generation = graph_generations.generation);");
        transaction.Commit();
    }

    private static long ReadMaximumDatabaseBytes(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        var value = ExecuteScalar(
            connection,
            transaction,
            "SELECT value FROM graph_meta WHERE key = 'maximum_database_bytes';") as string;
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
               parsed is >= 16L * 1024L * 1024L and <= 16L * 1024L * 1024L * 1024L
            ? parsed
            : DefaultMaximumDatabaseBytes;
    }

    private long ReadAllocatedDatabaseBytes(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        var pageSize = Convert.ToInt64(ExecuteScalar(connection, transaction, "PRAGMA page_size;"), CultureInfo.InvariantCulture);
        var freePages = Convert.ToInt64(ExecuteScalar(connection, transaction, "PRAGMA freelist_count;"), CultureInfo.InvariantCulture);
        return Math.Max(0, PhysicalSize - checked(pageSize * freePages));
    }

    private static void EnsureEnabled(SqliteConnection connection, SqliteTransaction transaction)
    {
        var value = ExecuteScalar(connection, transaction, "SELECT value FROM graph_meta WHERE key = 'enabled';") as string;
        if (!string.Equals(value, "1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Knowledge Graph is disabled and cannot admit projection work.");
        }
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureInitialized();
            _writerCancellationToken = cancellationToken;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((SqliteGraphStore)state!).InterruptActiveWriter(),
                this);
            return await Task.Run(
                    () => SqliteKnowledgeInfrastructure.ExecuteWithBusyRetry(
                        action,
                        cancellationToken,
                        "The graph store operation failed"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _activeWriterConnection, null);
            _writerCancellationToken = default;
            _gate.Release();
        }
    }

    private void InterruptActiveWriter()
    {
        var connection = Volatile.Read(ref _activeWriterConnection);
        if (connection is not null)
        {
            SqliteKnowledgeInfrastructure.TryInterrupt(connection);
        }
    }

    private async Task<T> RunReadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureInitialized();
            return await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            return action();
                        }
                        catch (SqliteKnowledgeStoreException)
                        {
                            throw;
                        }
                        catch (SqliteException exception)
                        {
                            throw SqliteKnowledgeInfrastructure.Map(exception, "The graph read operation failed.");
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _readerGate.Release();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "The Knowledge Graph store has not been initialized through the consent-gated storage lifecycle.");
        }
    }

    private static string State<T>(T value) where T : struct, Enum => value.ToString();
    private static T Parse<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw SqliteKnowledgeInfrastructure.Corrupt($"Persisted graph state '{Bound(value, 64)}' is invalid.");

    private static void ValidateSnapshot(GraphProjectionSnapshot snapshot)
    {
        ValidateStableId(snapshot.ManifestId, nameof(snapshot));
        ValidateStableId(snapshot.LegacyDecisionManifestId, nameof(snapshot));
        ValidateStableId(snapshot.GraphDecisionCheckpointId, nameof(snapshot));
        if (snapshot.Revision < 0 || snapshot.PrivacySequence < 0 || snapshot.TotalObservationCount < 0 ||
            snapshot.GraphDecisionSequence < 0 || string.IsNullOrWhiteSpace(snapshot.CanonicalManifestHash))
        {
            throw new ArgumentException("The completed graph projection snapshot is invalid.", nameof(snapshot));
        }

        if (snapshot.ObservationCounts.Any(item => item.Count < 0) ||
            snapshot.ObservationCounts.Sum(item => item.Count) != snapshot.TotalObservationCount)
        {
            throw new ArgumentException("The completed graph projection snapshot has invalid terminal counts.", nameof(snapshot));
        }
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > GraphLimits.MaximumStableIdCharacters || ContainsInvalidProjectionText(value) ||
            value.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(value) ||
            value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
        {
            throw new ArgumentOutOfRangeException(parameterName, "A graph stable identifier is unsafe or exceeds its bounded limit.");
        }
    }

    private static void ValidateLease(TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A graph claim lease must be positive and bounded to ten minutes.");
        }
    }

    private static void ValidateOperationalStage(
        string value,
        string parameterName,
        int maximumLength = 64,
        bool allowSpaces = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(character =>
                char.IsControl(character) ||
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '(' or ')' ||
                  allowSpaces && character == ' ')))
        {
            throw new ArgumentException(
                "Graph progress labels must be bounded privacy-safe operational codes, never paths or source content.",
                parameterName);
        }
    }

    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
    private static void AddParameters(SqliteCommand command, params (string Name, object? Value)[] parameters) =>
        SqliteKnowledgeInfrastructure.AddParameters(command, parameters);
    private static int ExecuteNonQuery(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters) =>
        SqliteKnowledgeInfrastructure.ExecuteNonQuery(connection, transaction, sql, parameters);
    private static object? ExecuteScalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        return command.ExecuteScalar();
    }
}
