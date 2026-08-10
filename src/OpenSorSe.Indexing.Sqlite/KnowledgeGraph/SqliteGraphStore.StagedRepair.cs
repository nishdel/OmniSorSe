using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

public sealed partial class SqliteGraphStore
{
    private const long RebuildFilesystemReserveBytes = 32L * 1024L * 1024L;

    /// <summary>
    /// Creates a new durable repair run and fresh logical jobs without changing any active
    /// component pointer. Each completed job therefore publishes one validated replacement
    /// generation atomically, while interruption leaves every unprocessed component readable.
    /// </summary>
    private StagedRepairSchedule ScheduleStagedRepair(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphRepairRequest request,
        GraphDecisionSnapshot decisionSnapshot,
        GraphAuthoritySnapshot authoritySnapshot,
        string repairId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activeRuns = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM graph_runs WHERE control_state NOT IN ($cancelled, $complete);",
                ("$cancelled", State(GraphRunControlState.Cancelled)),
                ("$complete", State(GraphRunControlState.Complete))),
            CultureInfo.InvariantCulture);
        if (activeRuns != 0)
        {
            throw new InvalidOperationException(
                "Wait for the current graph projection or repair run to finish before scheduling another repair.");
        }

        var sourceManifestId = authoritySnapshot.CurrentSourceManifestId;
        if (string.IsNullOrWhiteSpace(sourceManifestId))
        {
            throw new InvalidOperationException("A staged graph repair requires a current completed source manifest.");
        }

        var prior = ReadCompletedRepairBasis(
            connection,
            transaction,
            sourceManifestId,
            authoritySnapshot,
            decisionSnapshot);
        ValidateAppliedRepairAuthority(connection, transaction, prior, authoritySnapshot, decisionSnapshot);

        IReadOnlyList<string>? selectedComponents = request.Kind switch
        {
            GraphRepairKind.RebuildDerivedGraph => null,
            GraphRepairKind.ReprojectComponent => ResolveRepairComponents(connection, transaction, request.StableId!),
            GraphRepairKind.ReprojectFile => ResolveScopeComponents(
                connection,
                transaction,
                new GraphPrivacyScope(GraphPrivacyScopeKind.File, request.StableId!)),
            GraphRepairKind.ReprojectSource => ResolveScopeComponents(
                connection,
                transaction,
                new GraphPrivacyScope(GraphPrivacyScopeKind.Source, request.StableId!)),
            GraphRepairKind.RepairEvidence => ReadEvidenceDeficientComponents(
                connection,
                transaction,
                request.StableId is null ? null : ResolveRepairComponents(connection, transaction, request.StableId)),
            GraphRepairKind.ReconcileLegacyDecisions => null,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        if (selectedComponents is { Count: > GraphLimits.MaximumComponentNodes })
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Full,
                "The selective repair scope exceeds the stable component ceiling. Use the reviewed full rebuild instead.");
        }

        if (request.Kind == GraphRepairKind.RebuildDerivedGraph)
        {
            EnsureFullRebuildCapacity(connection, transaction);
        }

        var predicate = BuildRepairManifestPredicate(
            request.Kind,
            selectedComponents,
            out var predicateParameters);
        var countSql = string.Concat(
            "SELECT COUNT(*) FROM graph_manifest_rows m ",
            "JOIN graph_observation_inbox i ON i.manifest_id = m.manifest_id ",
            "AND i.row_kind = m.row_kind AND i.stable_primary_key = m.stable_primary_key ",
            "WHERE m.manifest_id = $manifest AND (",
            predicate,
            ");");
        var countParameters = new List<(string Name, object? Value)>
        {
            ("$manifest", sourceManifestId),
        };
        countParameters.AddRange(predicateParameters);
        var jobCount = Convert.ToInt32(
            ExecuteScalar(connection, transaction, countSql, countParameters.ToArray()),
            CultureInfo.InvariantCulture);

        var includesDecisionOverlay = selectedComponents?.Contains(
            "graph-native-decision-overlay",
            StringComparer.Ordinal) == true || request.Kind == GraphRepairKind.RebuildDerivedGraph;
        if (jobCount == 0 && !includesDecisionOverlay)
        {
            return new StagedRepairSchedule(
                null,
                0,
                "No current graph component matched the requested staged repair scope.");
        }

        var runId = string.Concat("graph-run-repair-", Guid.NewGuid().ToString("N"));
        var (ownerInstanceId, fencingEpoch) = ReadCurrentCoordinatorIdentity(
            connection,
            transaction,
            prior.OwnerInstanceId,
            prior.FencingEpoch);
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO graph_runs(
                run_id, control_state, control_sequence, freshness_state, integrity_state,
                reason, settings_fingerprint, coordinator_epoch, owner_instance_id,
                snapshot_manifest_id, snapshot_revision, legacy_decision_manifest_id,
                privacy_sequence, graph_decision_sequence, graph_decision_checkpoint_id,
                expected_observation_count, expected_manifest_hash, input_manifest_complete,
                current_stage, current_work_label, created_utc_ticks, updated_utc_ticks)
            VALUES ($run, $running, 0, $stale, $valid, $reason, $settings, $epoch, $owner,
                    $manifest, $revision, $legacy, $privacy, $decisionSequence, $decisionCheckpoint,
                    $count, $hash, 1, 'repair-queued', $work, $now, $now);
            """,
            ("$run", runId), ("$running", State(GraphRunControlState.Running)),
            ("$stale", State(GraphFreshnessState.Stale)), ("$valid", State(GraphIntegrityState.Valid)),
            ("$reason", string.Concat("repair:", repairId)), ("$settings", prior.SettingsFingerprint),
            ("$epoch", fencingEpoch), ("$owner", ownerInstanceId),
            ("$manifest", sourceManifestId), ("$revision", authoritySnapshot.CurrentSourceRevision),
            ("$legacy", authoritySnapshot.LegacyDecisionManifestId), ("$privacy", authoritySnapshot.PrivacySequence),
            ("$decisionSequence", decisionSnapshot.Sequence), ("$decisionCheckpoint", decisionSnapshot.CheckpointId),
            ("$count", jobCount), ("$hash", prior.ExpectedManifestHash),
            ("$work", Bound(request.Kind.ToString(), 256)), ("$now", nowUtc.UtcTicks));

        var insertSql = string.Concat(
            """
            INSERT INTO graph_jobs(
                job_id, logical_key, run_id, component_key, stage, execution_state,
                freshness_state, integrity_state, priority, current_attempt, maximum_attempts,
                source_manifest_id, source_row_hash, decision_sequence,
                configuration_fingerprint, algorithm_name, algorithm_version, rebuild_generation,
                observation_sequence, observation_kind, observation_stable_key,
                created_utc_ticks, updated_utc_ticks)
            SELECT 'graph-job-repair-' || lower(hex(randomblob(16))),
                   'repair:' || $repair || ':' || m.row_kind || ':' || m.stable_primary_key,
                   $run, m.row_kind || ':' || m.stable_primary_key,
                   $stage, $pending, $stale, $valid,
                   CASE m.row_kind
                       WHEN 'Source' THEN 600
                       WHEN 'LegacyDecision' THEN 600
                       WHEN 'File' THEN 500
                       WHEN 'Collection' THEN 500
                       WHEN 'Relationship' THEN 400
                       WHEN 'CollectionMembership' THEN 400
                       ELSE 0
                   END,
                   0, $maximum, m.manifest_id, m.canonical_row_hash, $decisionSequence,
                   $settings, 'deterministic-graph-projection', '1.0.0',
                   COALESCE((SELECT MAX(previous.rebuild_generation)
                             FROM graph_jobs previous
                             WHERE previous.component_key = m.row_kind || ':' || m.stable_primary_key), 0) + 1,
                   i.observation_sequence, m.row_kind, m.stable_primary_key, $now, $now
            FROM graph_manifest_rows m
            JOIN graph_observation_inbox i
              ON i.manifest_id = m.manifest_id
             AND i.row_kind = m.row_kind
             AND i.stable_primary_key = m.stable_primary_key
            WHERE m.manifest_id = $manifest AND (
            """,
            predicate,
            ") ORDER BY m.row_kind, m.stable_primary_key;");
        var insertParameters = new List<(string Name, object? Value)>
        {
            ("$repair", repairId), ("$run", runId),
            ("$stage", State(GraphProjectionStage.ObservationCaptured)),
            ("$pending", State(GraphJobExecutionState.Pending)),
            ("$stale", State(GraphFreshnessState.Stale)),
            ("$valid", State(GraphIntegrityState.Valid)),
            ("$maximum", GraphLimits.MaximumRetryCount),
            ("$decisionSequence", decisionSnapshot.Sequence),
            ("$settings", prior.SettingsFingerprint),
            ("$now", nowUtc.UtcTicks), ("$manifest", sourceManifestId),
        };
        insertParameters.AddRange(predicateParameters);
        var inserted = ExecuteNonQuery(connection, transaction, insertSql, insertParameters.ToArray());
        if (inserted != jobCount)
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "The staged repair job set does not match its validated manifest scope.");
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO graph_repair_operations(
                repair_id, scope, target_key, state, started_utc_ticks,
                records_examined, records_repaired, bounded_detail)
            VALUES ($id, $scope, $target, 'Running', $now, $count, 0, $run);
            """,
            ("$id", repairId), ("$scope", request.Kind.ToString()),
            ("$target", request.StableId), ("$now", nowUtc.UtcTicks),
            ("$count", jobCount), ("$run", runId));
        WriteDiagnostic(
            connection,
            transaction,
            runId,
            "repair",
            request.Kind.ToString(),
            "queued",
            "A staged repair run was queued without changing active graph generations.",
            nowUtc);
        PruneProjectionRecoveryHistory(connection, transaction);
        return new StagedRepairSchedule(
            runId,
            jobCount,
            jobCount == 0
                ? "The graph-native decision overlay was queued for staged rebuilding."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Queued {jobCount} durable replacement component(s); current validated generations remain readable until each replacement is published."));
    }

    private static CompletedRepairBasis ReadCompletedRepairBasis(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceManifestId,
        GraphAuthoritySnapshot authoritySnapshot,
        GraphDecisionSnapshot decisionSnapshot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT owner_instance_id, coordinator_epoch, settings_fingerprint,
                   expected_manifest_hash, input_manifest_complete
            FROM graph_runs
            WHERE snapshot_manifest_id = $manifest
              AND snapshot_revision = $revision
              AND legacy_decision_manifest_id = $legacy
              AND privacy_sequence = $privacy
              AND graph_decision_sequence = $decisionSequence
              AND graph_decision_checkpoint_id = $decisionCheckpoint
              AND control_state = $complete
            ORDER BY completed_utc_ticks DESC, run_id DESC
            LIMIT 1;
            """;
        AddParameters(
            command,
            ("$manifest", sourceManifestId), ("$revision", authoritySnapshot.CurrentSourceRevision),
            ("$legacy", authoritySnapshot.LegacyDecisionManifestId), ("$privacy", authoritySnapshot.PrivacySequence),
            ("$decisionSequence", decisionSnapshot.Sequence), ("$decisionCheckpoint", decisionSnapshot.CheckpointId),
            ("$complete", State(GraphRunControlState.Complete)));
        using var reader = command.ExecuteReader();
        if (!reader.Read() || !reader.GetBoolean(4))
        {
            throw new InvalidOperationException(
                "Staged graph repair requires a fully applied current projection. Wait for reconciliation to complete.");
        }

        return new CompletedRepairBasis(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    private static void ValidateAppliedRepairAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompletedRepairBasis prior,
        GraphAuthoritySnapshot authoritySnapshot,
        GraphDecisionSnapshot decisionSnapshot)
    {
        _ = prior;
        var count = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM graph_watermarks
                WHERE source_id = 'deep-index'
                  AND applied_manifest_id = $manifest
                  AND applied_revision = $revision
                  AND applied_decision_sequence = $decisionSequence
                  AND applied_decision_checkpoint_id = $decisionCheckpoint
                  AND applied_privacy_sequence >= $privacy;
                """,
                ("$manifest", authoritySnapshot.CurrentSourceManifestId),
                ("$revision", authoritySnapshot.CurrentSourceRevision),
                ("$decisionSequence", decisionSnapshot.Sequence),
                ("$decisionCheckpoint", decisionSnapshot.CheckpointId),
                ("$privacy", authoritySnapshot.PrivacySequence)),
            CultureInfo.InvariantCulture);
        if (count != 1)
        {
            throw new InvalidOperationException(
                "Staged graph repair cannot start while source, decision, or privacy application is incomplete.");
        }
    }

    private static (string OwnerInstanceId, long FencingEpoch) ReadCurrentCoordinatorIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fallbackOwner,
        long fallbackEpoch)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT owner_instance_id, fencing_epoch FROM graph_coordinator_lease WHERE singleton_id = 1;";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.GetString(0), reader.GetInt64(1))
            : (fallbackOwner, fallbackEpoch);
    }

    private static IReadOnlyList<string> ReadEvidenceDeficientComponents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string>? selectedComponents)
    {
        var selected = selectedComponents is null
            ? null
            : new HashSet<string>(selectedComponents, StringComparer.Ordinal);
        var components = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT e.component_key
            FROM graph_edges e
            JOIN graph_components c
              ON c.component_key = e.component_key AND c.active_generation = e.generation
            LEFT JOIN graph_evidence v
              ON v.component_key = e.component_key AND v.generation = e.generation AND v.edge_id = e.edge_id
            WHERE e.is_manual = 0
            GROUP BY e.component_key, e.generation, e.edge_id
            HAVING COUNT(v.evidence_id) = 0
            ORDER BY e.component_key;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var component = reader.GetString(0);
            if (selected is null || selected.Contains(component))
            {
                components.Add(component);
            }
        }

        return components.Order(StringComparer.Ordinal).ToArray();
    }

    private static string BuildRepairManifestPredicate(
        GraphRepairKind kind,
        IReadOnlyList<string>? selectedComponents,
        out IReadOnlyList<(string Name, object? Value)> parameters)
    {
        if (kind == GraphRepairKind.RebuildDerivedGraph)
        {
            parameters = [];
            return "1 = 1";
        }

        if (kind == GraphRepairKind.ReconcileLegacyDecisions)
        {
            parameters = [("$legacyKind", GraphProjectionObservationKind.LegacyDecision.ToString())];
            return "m.row_kind = $legacyKind";
        }

        if (selectedComponents is null || selectedComponents.Count == 0)
        {
            parameters = [];
            return "0 = 1";
        }

        var values = new List<(string Name, object? Value)>(selectedComponents.Count);
        var names = new string[selectedComponents.Count];
        for (var index = 0; index < selectedComponents.Count; index++)
        {
            names[index] = string.Concat("$component", index.ToString(CultureInfo.InvariantCulture));
            values.Add((names[index], selectedComponents[index]));
        }

        parameters = values;
        return string.Concat("m.row_kind || ':' || m.stable_primary_key IN (", string.Join(", ", names), ")");
    }

    private void EnsureFullRebuildCapacity(SqliteConnection connection, SqliteTransaction transaction)
    {
        var allocated = ReadAllocatedDatabaseBytes(connection, transaction);
        var maximum = ReadMaximumDatabaseBytes(connection, transaction);
        long projected;
        try
        {
            projected = checked(allocated * 2);
        }
        catch (OverflowException)
        {
            projected = long.MaxValue;
        }

        if (allocated < 0 || maximum < GraphLimits.MinimumStorageQuotaBytes || projected > maximum)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Full,
                "The full staged graph rebuild cannot retain the current graph within the configured storage quota.");
        }

        var root = Path.GetPathRoot(_databasePath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            var required = checked(allocated + RebuildFilesystemReserveBytes);
            if (new DriveInfo(root).AvailableFreeSpace < required)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.Full,
                    "The full staged graph rebuild cannot preserve the current graph without the required recovery reserve.");
            }
        }
        catch (IOException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.InputOutput,
                "Available storage could not be verified before the full staged graph rebuild.",
                exception);
        }
    }

    /// <summary>
    /// Terminalizes superseded non-terminal runs after the caller advances the coordinator
    /// fencing epoch. This preserves their durable history while ensuring obsolete input can
    /// never remain Running, PauseRequested, or publishable.
    /// </summary>
    private static void CancelSupersededProjectionRuns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset nowUtc)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE graph_repair_operations
            SET state = 'Cancelled',
                completed_utc_ticks = $now,
                records_repaired = (
                    SELECT COUNT(*) FROM graph_jobs j
                    WHERE j.run_id = graph_repair_operations.bounded_detail
                      AND j.execution_state = $completeJob),
                bounded_detail = 'Staged graph repair was superseded by a newer authoritative source snapshot.'
            WHERE state = 'Running'
              AND bounded_detail IN (
                  SELECT run_id FROM graph_runs
                  WHERE control_state NOT IN ($cancelledRun, $completeRun));
            """,
            ("$now", nowUtc.UtcTicks),
            ("$completeJob", State(GraphJobExecutionState.Complete)),
            ("$cancelledRun", State(GraphRunControlState.Cancelled)),
            ("$completeRun", State(GraphRunControlState.Complete)));
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE graph_job_attempts
            SET completed_utc_ticks = $now,
                outcome = 'Fenced',
                failure_category = 'superseded-source-snapshot'
            WHERE completed_utc_ticks IS NULL
              AND job_id IN (
                  SELECT j.job_id
                  FROM graph_jobs j
                  JOIN graph_runs r ON r.run_id = j.run_id
                  WHERE r.control_state NOT IN ($cancelledRun, $completeRun));
            """,
            ("$now", nowUtc.UtcTicks),
            ("$cancelledRun", State(GraphRunControlState.Cancelled)),
            ("$completeRun", State(GraphRunControlState.Complete)));
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE graph_jobs
            SET execution_state = CASE
                    WHEN execution_state = $completeJob THEN execution_state
                    ELSE $cancelledJob
                END,
                freshness_state = $stale,
                failure_category = CASE
                    WHEN execution_state = $completeJob THEN failure_category
                    ELSE 'superseded-source-snapshot'
                END,
                claim_owner_instance_id = NULL,
                claim_token = NULL,
                claim_fencing_epoch = NULL,
                claim_heartbeat_utc_ticks = NULL,
                claim_expires_utc_ticks = NULL,
                next_eligible_utc_ticks = NULL,
                updated_utc_ticks = $now
            WHERE run_id IN (
                SELECT run_id FROM graph_runs
                WHERE control_state NOT IN ($cancelledRun, $completeRun));
            """,
            ("$completeJob", State(GraphJobExecutionState.Complete)),
            ("$cancelledJob", State(GraphJobExecutionState.Cancelled)),
            ("$stale", State(GraphFreshnessState.Stale)),
            ("$now", nowUtc.UtcTicks),
            ("$cancelledRun", State(GraphRunControlState.Cancelled)),
            ("$completeRun", State(GraphRunControlState.Complete)));
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE graph_runs
            SET control_state = $cancelled,
                freshness_state = $stale,
                cancellation_reason = 'superseded-source-snapshot',
                completed_utc_ticks = $now,
                updated_utc_ticks = $now
            WHERE control_state NOT IN ($cancelled, $complete);
            """,
            ("$cancelled", State(GraphRunControlState.Cancelled)),
            ("$complete", State(GraphRunControlState.Complete)),
            ("$stale", State(GraphFreshnessState.Stale)),
            ("$now", nowUtc.UtcTicks));
    }

    /// <summary>
    /// Enforces the documented current-plus-prior recovery ceiling. Only terminal
    /// operational runs are eligible. The latest two terminal runs, the latest run
    /// that proves the applied watermark, every non-terminal run, active/applied
    /// manifests, and manifests referenced by active components are retained.
    /// Graph-native decisions and the authoritative deep-index store are not touched.
    /// </summary>
    private static int PruneProjectionRecoveryHistory(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var prunedRuns = ExecuteNonQuery(
            connection,
            transaction,
            """
            WITH applied_proof_run AS (
                SELECT r.run_id
                FROM graph_runs r
                JOIN graph_watermarks w
                  ON w.source_id = 'deep-index'
                 AND r.snapshot_manifest_id = w.applied_manifest_id
                 AND r.snapshot_revision = w.applied_revision
                 AND r.graph_decision_sequence = w.applied_decision_sequence
                 AND r.graph_decision_checkpoint_id = w.applied_decision_checkpoint_id
                 AND r.privacy_sequence = w.applied_privacy_sequence
                WHERE r.control_state = $complete
                ORDER BY COALESCE(r.completed_utc_ticks, r.updated_utc_ticks) DESC, r.run_id DESC
                LIMIT 1
            ),
            ranked_terminal_runs AS (
                SELECT r.run_id,
                       ROW_NUMBER() OVER (
                           ORDER BY CASE WHEN r.run_id IN (SELECT run_id FROM applied_proof_run) THEN 0 ELSE 1 END,
                                    COALESCE(r.completed_utc_ticks, r.updated_utc_ticks) DESC,
                                    r.run_id DESC) AS ordinal
                FROM graph_runs r
                WHERE r.control_state IN ($cancelled, $complete)
            ),
            terminal_ceiling AS (
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM graph_runs
                    WHERE control_state NOT IN ($cancelled, $complete))
                    THEN 1 ELSE 2 END AS retained_count
            )
            DELETE FROM graph_runs
            WHERE control_state IN ($cancelled, $complete)
              AND run_id NOT IN (
                  SELECT run_id FROM ranked_terminal_runs
                  WHERE ordinal <= (SELECT retained_count FROM terminal_ceiling));
            """,
            ("$cancelled", State(GraphRunControlState.Cancelled)),
            ("$complete", State(GraphRunControlState.Complete)));

        var prunedManifests = ExecuteNonQuery(
            connection,
            transaction,
            """
            WITH protected_manifests AS (
                SELECT snapshot_manifest_id AS manifest_id FROM graph_runs
                UNION
                SELECT source_manifest_id FROM graph_components
                WHERE source_manifest_id IS NOT NULL
                UNION
                SELECT latest_complete_manifest_id FROM graph_watermarks
                WHERE latest_complete_manifest_id IS NOT NULL
                UNION
                SELECT applied_manifest_id FROM graph_watermarks
                WHERE applied_manifest_id IS NOT NULL
            ),
            ranked_unprotected_manifests AS (
                SELECT m.manifest_id, m.source_id, m.scope,
                       ROW_NUMBER() OVER (
                           PARTITION BY m.source_id, m.scope
                           ORDER BY COALESCE(m.completed_utc_ticks, m.started_utc_ticks) DESC, m.manifest_id DESC) AS ordinal
                FROM graph_manifests m
                WHERE m.is_active = 0
                  AND m.manifest_id NOT IN (SELECT manifest_id FROM protected_manifests)
            ),
            retained_unprotected_manifests AS (
                SELECT candidate.manifest_id
                FROM ranked_unprotected_manifests candidate
                WHERE candidate.ordinal <= MAX(
                    0,
                    2 - (
                        SELECT COUNT(DISTINCT protected.manifest_id)
                        FROM graph_manifests protected
                        WHERE protected.source_id = candidate.source_id
                          AND protected.scope = candidate.scope
                          AND (protected.is_active = 1 OR protected.manifest_id IN (
                              SELECT manifest_id FROM protected_manifests))))
            )
            DELETE FROM graph_manifests
            WHERE is_active = 0
              AND manifest_id NOT IN (
                  SELECT manifest_id FROM protected_manifests)
              AND manifest_id NOT IN (
                  SELECT manifest_id FROM retained_unprotected_manifests);
            """);
        return checked(prunedRuns + prunedManifests);
    }

    private sealed record CompletedRepairBasis(
        string OwnerInstanceId,
        long FencingEpoch,
        string SettingsFingerprint,
        string ExpectedManifestHash);

    private sealed record StagedRepairSchedule(string? RunId, int JobCount, string Message);
}
