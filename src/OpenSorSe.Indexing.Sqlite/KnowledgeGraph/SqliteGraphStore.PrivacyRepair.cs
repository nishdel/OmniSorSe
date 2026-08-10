using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>Reports one bounded graph maintenance pass.</summary>
public sealed record SqliteGraphMaintenanceResult(
    int RecordsRemoved,
    long BytesBefore,
    long BytesAfter,
    bool QuotaBlocked,
    string Message);

public sealed partial class SqliteGraphStore
{
    /// <inheritdoc />
    public Task<GraphPrivacyInspection> InspectPrivacyAsync(
        GraphPrivacyScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidateScope(scope);
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                var components = ResolveScopeComponents(connection, transaction: null, scope);
                var counts = ReadScopeCounts(connection, components);
                var persistedScopeId = NormalizePrivacyScopeId(connection, transaction: null, scope);
                var excluded = Convert.ToInt32(
                    SqliteKnowledgeInfrastructure.ExecuteScalar(
                        connection,
                        "SELECT COUNT(*) FROM graph_privacy_exclusions WHERE scope_kind = $kind AND stable_id = $id;",
                        ("$kind", State(scope.Kind)), ("$id", persistedScopeId)),
                    CultureInfo.InvariantCulture) > 0;
                var decisions = ReadDecisionCount(scope);
                return new GraphPrivacyInspection(
                    scope,
                    counts.Nodes,
                    counts.Edges,
                    counts.Evidence,
                    counts.Aliases,
                    decisions,
                    excluded,
                    counts.LastProjected,
                    "Inspection includes only graph-owned bounded metadata. Original files were not read.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphOperationResult> ApplyPrivacyAsync(
        GraphPrivacyChange change,
        GraphDecisionSnapshot decisionSnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ValidateDecisionAuthority(decisionSnapshot);
        ValidateScope(change.Scope);
        if (!change.ConfirmSourceFilesUnaffected)
        {
            throw new InvalidOperationException("Graph privacy actions require confirmation that original files are unaffected.");
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                FenceActiveWork(connection, transaction, "privacy-authority-changed", nowUtc);
                var affected = 0;
                if (change.Action is GraphPrivacyAction.ClearAllDerivedData or GraphPrivacyAction.ClearAllDecisions)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "INSERT INTO graph_meta(key, value) VALUES ('enabled', '0') ON CONFLICT(key) DO UPDATE SET value = '0';");
                }

                var persistedScopeId = NormalizePrivacyScopeId(connection, transaction, change.Scope);
                if (change.Action == GraphPrivacyAction.ClearAllDecisions)
                {
                    affected += ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM graph_components WHERE component_key = 'graph-native-decision-overlay';");
                    affected += ExecuteNonQuery(connection, transaction, "DELETE FROM graph_decision_suppressions;");
                    affected += ExecuteNonQuery(connection, transaction, "DELETE FROM graph_decision_projection_staging;");
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "UPDATE graph_watermarks SET ingested_decision_sequence = 0, ingested_decision_checkpoint_id = $checkpoint, ingested_decision_canonical_hash = $hash, applied_decision_sequence = 0, applied_decision_checkpoint_id = NULL, updated_utc_ticks = $now WHERE source_id = 'deep-index';",
                        ("$checkpoint", decisionSnapshot.CheckpointId), ("$hash", decisionSnapshot.CanonicalHash),
                        ("$now", nowUtc.UtcTicks));
                }

                if (change.Action is GraphPrivacyAction.ForgetDerivedData or
                    GraphPrivacyAction.ClearAllDerivedData or
                    GraphPrivacyAction.ClearAllDecisions)
                {
                    var clearAllDerived = change.Action is
                        GraphPrivacyAction.ClearAllDerivedData or GraphPrivacyAction.ClearAllDecisions;
                    var components = clearAllDerived
                        ? ReadAllComponents(connection, transaction)
                        : ResolveScopeComponents(connection, transaction, change.Scope);
                    affected += DeleteComponents(connection, transaction, components);
                    if (clearAllDerived)
                    {
                        affected += ExecuteNonQuery(connection, transaction, "DELETE FROM graph_runs;");
                        affected += ExecuteNonQuery(connection, transaction, "DELETE FROM graph_manifests;");
                        affected += ExecuteNonQuery(
                            connection,
                            transaction,
                            """
                            UPDATE graph_watermarks
                            SET latest_complete_manifest_id = NULL,
                                latest_complete_revision = 0,
                                applied_manifest_id = NULL,
                                applied_revision = 0,
                                ingestion_manifest_id = NULL,
                                ingestion_page_number = 0,
                                ingestion_stable_key = NULL,
                                ingested_observation_sequence = 0,
                                applied_observation_sequence = 0,
                                ingested_privacy_sequence = 0,
                                applied_privacy_sequence = 0,
                                applied_decision_sequence = 0,
                                applied_decision_checkpoint_id = NULL,
                                updated_utc_ticks = $now;
                            """,
                            ("$now", nowUtc.UtcTicks));
                    }
                }

                if (change.Action == GraphPrivacyAction.ExcludeFromProjection)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO graph_privacy_exclusions(scope_kind, stable_id, authority_sequence, observed_utc_ticks)
                        VALUES ($kind, $id, $authority, $now)
                        ON CONFLICT(scope_kind, stable_id) DO UPDATE SET
                            authority_sequence = excluded.authority_sequence,
                            observed_utc_ticks = excluded.observed_utc_ticks;
                        """,
                        ("$kind", State(change.Scope.Kind)), ("$id", persistedScopeId),
                        ("$authority", decisionSnapshot.Sequence), ("$now", nowUtc.UtcTicks));
                    affected++;
                }
                else if (change.Action == GraphPrivacyAction.IncludeInProjection)
                {
                    affected += ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM graph_privacy_exclusions WHERE scope_kind = $kind AND stable_id = $id;",
                        ("$kind", State(change.Scope.Kind)), ("$id", persistedScopeId));
                }

                WriteDiagnostic(connection, transaction, null, "privacy", change.Action.ToString(), "complete", null, nowUtc);
                transaction.Commit();
                return new GraphOperationResult(
                    true,
                    change.Action is GraphPrivacyAction.ClearAllDerivedData or GraphPrivacyAction.ClearAllDecisions
                        ? "Graph-owned data was updated transactionally and Knowledge Graph remains disabled until explicitly re-enabled. Original files were not changed."
                        : "Graph-owned data was updated transactionally. Original files were not changed.",
                    affected);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphOperationResult> RepairAsync(
        GraphRepairRequest request,
        GraphDecisionSnapshot decisionSnapshot,
        GraphAuthoritySnapshot authoritySnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decisionSnapshot);
        ArgumentNullException.ThrowIfNull(authoritySnapshot);
        if (request.Kind != GraphRepairKind.Verify && !request.ConfirmSourceFilesUnaffected)
        {
            throw new InvalidOperationException("Graph repair requires confirmation that original files are unaffected.");
        }

        if (request.StableId is not null)
        {
            ValidateStableId(request.StableId, nameof(request));
        }

        if (request.Kind != GraphRepairKind.Verify)
        {
            ValidateDecisionAuthority(decisionSnapshot);
            if (!authoritySnapshot.IsAvailable || !authoritySnapshot.IsAllowed || authoritySnapshot.PrivacySequence < 0 ||
                string.IsNullOrWhiteSpace(authoritySnapshot.LegacyDecisionManifestId))
            {
                throw new InvalidOperationException("Graph repair authority is unavailable or stale.");
            }
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                SqliteKnowledgeInfrastructure.Validate(
                    connection,
                    SqliteKnowledgeGraphSchema.ApplicationId,
                    SqliteKnowledgeGraphSchema.Version,
                    SqliteKnowledgeGraphSchema.RequiredTables,
                    "graph_meta",
                    "graph_migration_history",
                    SqliteKnowledgeGraphSchema.CreateVersionOne,
                    SqliteKnowledgeGraphSchema.RequiredColumns,
                    SqliteKnowledgeGraphSchema.RequiredIndexes);
                using var transaction = connection.BeginTransaction();
                var repairId = $"graph-repair-{Guid.NewGuid():N}";
                if (request.Kind is GraphRepairKind.RepairEvidence or
                    GraphRepairKind.RebuildDerivedGraph or
                    GraphRepairKind.ReprojectComponent or
                    GraphRepairKind.ReprojectFile or
                    GraphRepairKind.ReprojectSource or
                    GraphRepairKind.ReconcileLegacyDecisions)
                {
                    var schedule = ScheduleStagedRepair(
                        connection,
                        transaction,
                        request,
                        decisionSnapshot,
                        authoritySnapshot,
                        repairId,
                        nowUtc,
                        cancellationToken);
                    if (schedule.RunId is null)
                    {
                        ExecuteNonQuery(
                            connection,
                            transaction,
                            """
                            INSERT INTO graph_repair_operations(
                                repair_id, scope, target_key, state, started_utc_ticks,
                                completed_utc_ticks, records_examined, records_repaired, bounded_detail)
                            VALUES ($id, $scope, $target, 'Complete', $now, $now, 0, 0, $detail);
                            """,
                            ("$id", repairId), ("$scope", request.Kind.ToString()),
                            ("$target", request.StableId), ("$now", nowUtc.UtcTicks),
                            ("$detail", Bound(schedule.Message, 1024)));
                        WriteDiagnostic(
                            connection,
                            transaction,
                            null,
                            "repair",
                            request.Kind.ToString(),
                            "complete",
                            schedule.Message,
                            nowUtc);
                    }

                    transaction.Commit();
                    return new GraphOperationResult(true, schedule.Message, schedule.JobCount);
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    "INSERT INTO graph_repair_operations(repair_id, scope, target_key, state, started_utc_ticks) VALUES ($id, $scope, $target, 'Running', $now);",
                    ("$id", repairId), ("$scope", request.Kind.ToString()), ("$target", request.StableId), ("$now", nowUtc.UtcTicks));
                var examined = 0;
                var repaired = 0;
                string detail;
                switch (request.Kind)
                {
                    case GraphRepairKind.Verify:
                        (examined, repaired) = VerifyActiveGraph(
                            connection,
                            transaction,
                            request.StableId is null ? null : ResolveRepairComponents(connection, transaction, request.StableId),
                            nowUtc);
                        detail = repaired == 0 ? "Active graph invariants are valid." : "Integrity findings were recorded for selective repair.";
                        break;
                    case GraphRepairKind.RemoveOrphans:
                        repaired = RemoveInactiveGenerations(connection, transaction);
                        examined = repaired;
                        detail = "Verified inactive graph generations were removed.";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(request));
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_repair_operations
                    SET state = 'Complete', completed_utc_ticks = $now,
                        records_examined = $examined, records_repaired = $repaired,
                        bounded_detail = $detail
                    WHERE repair_id = $id;
                    """,
                    ("$now", nowUtc.UtcTicks), ("$examined", examined), ("$repaired", repaired),
                    ("$detail", Bound(detail, 1024)), ("$id", repairId));
                WriteDiagnostic(connection, transaction, null, "repair", request.Kind.ToString(), "complete", detail, nowUtc);
                transaction.Commit();
                return new GraphOperationResult(true, detail, repaired);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task InvalidateDecisionAsync(
        GraphDecisionEntry decision,
        GraphDecisionSnapshot decisionSnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ValidateDecisionAuthority(decisionSnapshot);
        if (decision.Sequence is <= 0 || decision.Sequence > decisionSnapshot.Sequence)
        {
            throw new InvalidDataException("The invalidating decision is outside its validated checkpoint.");
        }
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                FenceActiveWork(connection, transaction, "decision-authority-changed", nowUtc);
                var components = ResolveDecisionComponents(connection, transaction, decision.Command);
                var affected = MarkComponentsForReprojection(connection, transaction, components, nowUtc);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE graph_watermarks SET applied_decision_sequence = MIN(applied_decision_sequence, $before), applied_decision_checkpoint_id = NULL, updated_utc_ticks = $now WHERE source_id = 'deep-index';",
                    ("$before", Math.Max(0, decision.Sequence - 1)), ("$now", nowUtc.UtcTicks));
                WriteDiagnostic(connection, transaction, null, "decision", decision.Command.Kind.ToString(), "invalidated", affected.ToString(CultureInfo.InvariantCulture), nowUtc);
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    private static void ValidateDecisionAuthority(GraphDecisionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsValid || snapshot.Sequence < 0 || string.IsNullOrWhiteSpace(snapshot.CheckpointId) ||
            snapshot.CheckpointId.Length > GraphLimits.MaximumStableIdCharacters)
        {
            throw new InvalidOperationException("Graph-native decision authority is invalid.");
        }
    }

    private static void FenceActiveWork(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string reason,
        DateTimeOffset nowUtc)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE graph_coordinator_lease SET fencing_epoch = fencing_epoch + 1, expires_utc_ticks = $now, heartbeat_utc_ticks = $now WHERE singleton_id = 1;",
            ("$now", nowUtc.UtcTicks));
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE graph_runs SET control_state = $pause, freshness_state = $stale, cancellation_reason = $reason, updated_utc_ticks = $now WHERE control_state IN ($pending, $running, $pauseRequested);",
            ("$pause", State(GraphRunControlState.PauseRequested)), ("$stale", State(GraphFreshnessState.Stale)),
            ("$reason", Bound(reason, 512)), ("$now", nowUtc.UtcTicks),
            ("$pending", State(GraphRunControlState.Pending)), ("$running", State(GraphRunControlState.Running)),
            ("$pauseRequested", State(GraphRunControlState.PauseRequested)));
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE graph_jobs
            SET execution_state = $retry, failure_category = $reason,
                claim_owner_instance_id = NULL, claim_token = NULL, claim_fencing_epoch = NULL,
                claim_heartbeat_utc_ticks = NULL, claim_expires_utc_ticks = NULL,
                next_eligible_utc_ticks = $now, updated_utc_ticks = $now
            WHERE execution_state = $running;
            """,
            ("$retry", State(GraphJobExecutionState.RetryableFailure)), ("$reason", Bound(reason, 128)),
            ("$now", nowUtc.UtcTicks), ("$running", State(GraphJobExecutionState.Running)));
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE graph_job_attempts SET completed_utc_ticks = $now, outcome = 'Fenced', failure_category = $reason WHERE completed_utc_ticks IS NULL;",
            ("$now", nowUtc.UtcTicks), ("$reason", Bound(reason, 128)));
    }

    /// <inheritdoc />
    public Task<GraphDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                var status = ReadStatus(connection);
                var nodeCount = CountActive(connection, "graph_nodes");
                var edgeCount = CountActive(connection, "graph_edges");
                var evidenceCount = CountActive(connection, "graph_evidence");
                var repairCount = Convert.ToInt64(
                    SqliteKnowledgeInfrastructure.ExecuteScalar(connection, "SELECT COUNT(*) FROM graph_components WHERE integrity_state = $repair;", ("$repair", State(GraphIntegrityState.RepairRequired))),
                    CultureInfo.InvariantCulture);
                var queue = status.PendingCount + status.RunningCount + status.RetryableFailureCount + status.WaitingCount;
                var lastFailure = SqliteKnowledgeInfrastructure.ExecuteScalar(
                    connection,
                    "SELECT failure_category FROM graph_jobs WHERE failure_category IS NOT NULL ORDER BY updated_utc_ticks DESC, job_id DESC LIMIT 1;") as string;
                return new GraphDiagnosticsSnapshot
                {
                    SchemaVersion = SqliteKnowledgeGraphSchema.Version,
                    ProviderCode = "sqlite",
                    AlgorithmVersions = ReadAlgorithmVersions(connection),
                    StageDurations = ReadStageDurations(connection),
                    OperationalHistory = ReadOperationalHistory(connection),
                    RunId = status.RunId,
                    ProjectionRevision = status.Coverage.ProjectionRevision,
                    NodeCount = nodeCount,
                    EdgeCount = edgeCount,
                    EvidenceCount = evidenceCount,
                    DecisionCount = ReadTotalDecisionCount(),
                    RepairRequiredCount = repairCount,
                    RecoveredClaimCount = Interlocked.Read(ref _recoveredClaims),
                    QueueLength = queue,
                    LastFailureCategory = lastFailure,
                    StorageBreakdown = status.StorageBreakdown,
                    Maintenance = status.Maintenance,
                    Coverage = status.Coverage,
                };
            },
            cancellationToken);

    /// <summary>Runs bounded retention, checkpoint, compaction, and quota maintenance.</summary>
    public Task<SqliteGraphMaintenanceResult> MaintainAsync(
        long maximumDatabaseBytes,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        MaintainCoreAsync(maximumDatabaseBytes, allowCompaction: true, nowUtc, cancellationToken);

    /// <inheritdoc />
    public async Task<GraphMaintenanceResult> MaintainAsync(
        GraphMaintenanceRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await MaintainCoreAsync(
                request.MaximumStorageSizeBytes,
                request.AllowCompaction,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);
        return new GraphMaintenanceResult(
            result.RecordsRemoved,
            result.BytesBefore,
            result.BytesAfter,
            result.QuotaBlocked,
            nowUtc,
            result.Message);
    }

    private Task<SqliteGraphMaintenanceResult> MaintainCoreAsync(
        long maximumDatabaseBytes,
        bool allowCompaction,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (maximumDatabaseBytes is < 16L * 1024L * 1024L or > 16L * 1024L * 1024L * 1024L)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDatabaseBytes));
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                cancellationToken.ThrowIfCancellationRequested();
                var beforeStorage = ReadStorageBreakdown(connection);
                var before = beforeStorage.TotalBytes;
                var availableForDerived = maximumDatabaseBytes - beforeStorage.DecisionLedgerBytes -
                                          beforeStorage.VerifiedBackupBytes - beforeStorage.RequiredReserveBytes;
                var derivedQuota = Math.Clamp(
                    availableForDerived,
                    16L * 1024L * 1024L,
                    16L * 1024L * 1024L * 1024L);
                var maintenanceId = $"graph-maintenance-{Guid.NewGuid():N}";
                var removed = 0;
                using (var transaction = connection.BeginTransaction())
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "INSERT INTO graph_meta(key, value) VALUES ('maximum_database_bytes', $derived) ON CONFLICT(key) DO UPDATE SET value = excluded.value; INSERT INTO graph_meta(key, value) VALUES ('maximum_total_storage_bytes', $total) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                        ("$derived", derivedQuota.ToString(CultureInfo.InvariantCulture)),
                        ("$total", maximumDatabaseBytes.ToString(CultureInfo.InvariantCulture)));
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "INSERT INTO graph_maintenance_history(maintenance_id, operation, owner_instance_id, fencing_epoch, state, started_utc_ticks, bytes_before) VALUES ($id, 'retention-compact', 'maintenance', 1, 'Running', $now, $bytes);",
                        ("$id", maintenanceId), ("$now", nowUtc.UtcTicks), ("$bytes", before));
                    removed = RemoveInactiveGenerations(connection, transaction);
                    removed += ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM graph_diagnostics WHERE sequence NOT IN (SELECT sequence FROM graph_diagnostics ORDER BY sequence DESC LIMIT 10000);");
                    removed += ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM graph_job_attempts WHERE attempt_id NOT IN (SELECT attempt_id FROM graph_job_attempts ORDER BY started_utc_ticks DESC, attempt_id DESC LIMIT 50000) AND completed_utc_ticks IS NOT NULL;");
                    cancellationToken.ThrowIfCancellationRequested();
                    transaction.Commit();
                }

                SqliteKnowledgeInfrastructure.ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
                if (allowCompaction && before > maximumDatabaseBytes * 3 / 4)
                {
                    SqliteKnowledgeInfrastructure.ExecuteNonQuery(connection, "VACUUM;");
                }

                var afterStorage = ReadStorageBreakdown(connection);
                var after = afterStorage.TotalBytes;
                var blocked = after > maximumDatabaseBytes ||
                              afterStorage.RequiredReserveBytes > maximumDatabaseBytes - Math.Min(after, maximumDatabaseBytes);
                using (var completion = connection.BeginTransaction())
                {
                    ExecuteNonQuery(
                        connection,
                        completion,
                        "UPDATE graph_maintenance_history SET state = 'Complete', completed_utc_ticks = $now, records_affected = $removed, bytes_after = $after WHERE maintenance_id = $id AND state = 'Running';",
                        ("$now", nowUtc.UtcTicks), ("$removed", removed), ("$after", after), ("$id", maintenanceId));
                    ExecuteNonQuery(
                        connection,
                        completion,
                        "INSERT INTO graph_meta(key, value) VALUES ('quota_blocked', $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                        ("$value", blocked ? "1" : "0"));
                    ExecuteNonQuery(
                        connection,
                        completion,
                        blocked
                            ? "UPDATE graph_runs SET current_stage = $stage, current_work_label = 'graph-storage-quota', updated_utc_ticks = $now WHERE control_state IN ($pending, $running);"
                            : "UPDATE graph_runs SET current_stage = NULL, current_work_label = NULL, updated_utc_ticks = $now WHERE current_work_label IN ('graph-storage-quota', 'graph-decision-quota');",
                        ("$stage", State(GraphJobExecutionState.WaitingForResources)),
                        ("$now", nowUtc.UtcTicks),
                        ("$pending", State(GraphRunControlState.Pending)),
                        ("$running", State(GraphRunControlState.Running)));
                    if (blocked)
                    {
                        WriteDiagnostic(
                            connection,
                            completion,
                            null,
                            "storage-quota",
                            "maintenance",
                            "blocked",
                            null,
                            nowUtc);
                    }
                    completion.Commit();
                }

                return new SqliteGraphMaintenanceResult(
                    removed,
                    before,
                    after,
                    blocked,
                    blocked ? "Graph storage remains above its configured total quota or recovery reserve after safe cleanup." : "Graph maintenance completed.");
            },
            cancellationToken);
    }

    /// <summary>Gets current physical SQLite main, WAL, and shared-memory bytes.</summary>
    public long PhysicalSize => FileLength(_databasePath) + FileLength(_databasePath + "-wal") + FileLength(_databasePath + "-shm");

    private GraphStorageBreakdown ReadStorageBreakdown(SqliteConnection graphConnection)
    {
        const long requiredReserve = 32L * 1024L * 1024L;
        var derived = PhysicalSize;
        var decisions = FileLength(_decisionDatabasePath) +
                        FileLength(_decisionDatabasePath + "-wal") +
                        FileLength(_decisionDatabasePath + "-shm");
        var verifiedBackups = 0L;
        var inventoryVerified = true;
        var backupDirectory = Path.Combine(
            Path.GetDirectoryName(_databasePath)!,
            "backups",
            "knowledge-decisions");
        var cataloguedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(_decisionDatabasePath))
        {
            try
            {
                using var decision = SqliteKnowledgeInfrastructure.OpenConnection(_decisionDatabasePath, readOnly: true);
                using var command = decision.CreateCommand();
                command.CommandText =
                    "SELECT relative_path, byte_length FROM decision_backup_catalog WHERE state = 'Committed' AND sha256 IS NOT NULL ORDER BY backup_id;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var relative = reader.GetString(0);
                    var safeName = Path.GetFileName(relative);
                    if (!string.Equals(relative, safeName, StringComparison.Ordinal) || reader.IsDBNull(1))
                    {
                        inventoryVerified = false;
                        continue;
                    }

                    var path = Path.Combine(backupDirectory, safeName);
                    var manifest = path + ".manifest.json";
                    cataloguedFiles.Add(safeName);
                    cataloguedFiles.Add(safeName + ".manifest.json");
                    var expectedLength = reader.GetInt64(1);
                    if (!File.Exists(path) || !File.Exists(manifest) || FileLength(path) != expectedLength)
                    {
                        inventoryVerified = false;
                        continue;
                    }

                    verifiedBackups = checked(verifiedBackups + FileLength(path) + FileLength(manifest));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException or SqliteKnowledgeStoreException)
            {
                inventoryVerified = false;
            }
        }

        if (Directory.Exists(backupDirectory))
        {
            try
            {
                inventoryVerified &= Directory.EnumerateFiles(backupDirectory, "decision-backup-*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .All(name => name is not null && cataloguedFiles.Contains(name));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                inventoryVerified = false;
            }
        }

        var maximum = Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                graphConnection,
                "SELECT COALESCE((SELECT value FROM graph_meta WHERE key = 'maximum_total_storage_bytes'), (SELECT value FROM graph_meta WHERE key = 'maximum_database_bytes'), $default);",
                ("$default", DefaultMaximumDatabaseBytes.ToString(CultureInfo.InvariantCulture))) ?? DefaultMaximumDatabaseBytes,
            CultureInfo.InvariantCulture);
        long total;
        try
        {
            total = checked(derived + decisions + verifiedBackups);
        }
        catch (OverflowException)
        {
            total = long.MaxValue;
            inventoryVerified = false;
        }

        return new GraphStorageBreakdown
        {
            DerivedStoreBytes = derived,
            DecisionLedgerBytes = decisions,
            VerifiedBackupBytes = verifiedBackups,
            TotalBytes = total,
            MaximumBytes = maximum,
            RequiredReserveBytes = requiredReserve,
            IsInventoryVerified = inventoryVerified,
        };
    }

    private static GraphMaintenanceStatus ReadMaintenanceStatus(SqliteConnection connection)
    {
        var quotaBlocked = string.Equals(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT value FROM graph_meta WHERE key = 'quota_blocked';") as string,
            "1",
            StringComparison.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT state, completed_utc_ticks, records_affected FROM graph_maintenance_history ORDER BY started_utc_ticks DESC, maintenance_id DESC LIMIT 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return GraphMaintenanceStatus.Idle with { QuotaBlocked = quotaBlocked };
        }

        var state = reader.GetString(0);
        var running = string.Equals(state, "Running", StringComparison.Ordinal);
        var completed = reader.IsDBNull(1)
            ? (DateTimeOffset?)null
            : new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero);
        var removed = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
        return new GraphMaintenanceStatus(
            running,
            quotaBlocked,
            completed,
            removed,
            state switch
            {
                "Running" => "Graph maintenance is running.",
                "Interrupted" => "Interrupted graph maintenance was recovered; run maintenance again when practical.",
                _ => "Graph maintenance completed.",
            });
    }

    private static IReadOnlyList<string> ReadAlgorithmVersions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT algorithm_name || ':' || algorithm_version
            FROM graph_components
            WHERE integrity_state = $valid
            ORDER BY algorithm_name, algorithm_version
            LIMIT 32;
            """;
        command.Parameters.AddWithValue("$valid", State(GraphIntegrityState.Valid));
        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = reader.GetString(0);
            if (value.Length is < 1 or > 64 || value.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':')))
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("A persisted graph algorithm diagnostic code is invalid.");
            }

            values.Add(value);
        }

        return values;
    }

    private static IReadOnlyList<GraphStageDurationAggregate> ReadStageDurations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT operation, COUNT(*), SUM(duration_milliseconds), MAX(duration_milliseconds)
            FROM graph_diagnostics
            WHERE category = 'projection-stage' AND duration_milliseconds > 0
            GROUP BY operation
            ORDER BY operation
            LIMIT 32;
            """;
        var values = new List<GraphStageDurationAggregate>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var stage = reader.GetString(0);
            var count = reader.GetInt64(1);
            var total = reader.GetInt64(2);
            var maximum = reader.GetInt64(3);
            if (stage.Length is < 1 or > 64 || !stage.All(char.IsAsciiLetterOrDigit) ||
                count <= 0 || total <= 0 || maximum <= 0 || maximum > total ||
                total > TimeSpan.MaxValue.TotalMilliseconds)
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("Persisted graph stage-duration diagnostics are invalid.");
            }

            values.Add(new GraphStageDurationAggregate(
                stage,
                count,
                TimeSpan.FromMilliseconds(total),
                TimeSpan.FromMilliseconds(maximum)));
        }

        return values;
    }

    private static GraphOperationalHistorySummary ReadOperationalHistory(SqliteConnection connection)
    {
        var bounds = CountDiagnosticEvents(connection, "bound", null);
        var quotas = CountDiagnosticEvents(connection, "storage-quota", "blocked");
        var cancellations = Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT COUNT(*) FROM graph_runs WHERE control_state = $cancelled;",
                ("$cancelled", State(GraphRunControlState.Cancelled))) ?? 0,
            CultureInfo.InvariantCulture);
        var recoveries = Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT COALESCE(SUM(recovery_count), 0) FROM graph_job_attempts;") ?? 0,
            CultureInfo.InvariantCulture);
        var repairs = Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(connection, "SELECT COUNT(*) FROM graph_repair_operations;") ?? 0,
            CultureInfo.InvariantCulture);
        var lastTicksValue = SqliteKnowledgeInfrastructure.ExecuteScalar(
            connection,
            """
            SELECT MAX(event_ticks)
            FROM (
                SELECT created_utc_ticks AS event_ticks FROM graph_diagnostics
                UNION ALL SELECT started_utc_ticks FROM graph_repair_operations
                UNION ALL SELECT completed_utc_ticks FROM graph_repair_operations WHERE completed_utc_ticks IS NOT NULL
                UNION ALL SELECT started_utc_ticks FROM graph_maintenance_history
                UNION ALL SELECT completed_utc_ticks FROM graph_maintenance_history WHERE completed_utc_ticks IS NOT NULL
                UNION ALL SELECT completed_utc_ticks FROM graph_job_attempts
                    WHERE completed_utc_ticks IS NOT NULL AND (recovery_count > 0 OR outcome = 'Cancelled')
            );
            """);
        DateTimeOffset? lastEvent = null;
        if (lastTicksValue is not null and not DBNull)
        {
            var ticks = Convert.ToInt64(lastTicksValue, CultureInfo.InvariantCulture);
            if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("Persisted graph operational-history time is invalid.");
            }

            lastEvent = new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        return new GraphOperationalHistorySummary
        {
            BoundEventCount = bounds,
            QuotaEventCount = quotas,
            CancellationCount = cancellations,
            RecoveryCount = recoveries,
            RepairCount = repairs,
            LastEventAtUtc = lastEvent,
        };
    }

    private static long CountDiagnosticEvents(SqliteConnection connection, string category, string? outcome) =>
        Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT COUNT(*) FROM graph_diagnostics WHERE category = $category AND ($outcome IS NULL OR outcome = $outcome);",
                ("$category", category), ("$outcome", outcome)) ?? 0,
            CultureInfo.InvariantCulture);

    private static (int Nodes, int Edges, int Evidence, int Aliases, DateTimeOffset? LastProjected) ReadScopeCounts(
        SqliteConnection connection,
        IReadOnlyList<string> components)
    {
        if (components.Count == 0)
        {
            return (0, 0, 0, 0, null);
        }

        var nodes = CountForComponents(connection, "graph_nodes", components);
        var edges = CountForComponents(connection, "graph_edges", components);
        var evidence = CountForComponents(connection, "graph_evidence", components);
        var aliases = CountForComponents(connection, "graph_aliases", components);
        long? maximumTicks = null;
        foreach (var chunk in components.Chunk(500))
        {
            var parameters = ComponentParameters(chunk);
            var clause = string.Join(",", parameters.Select(item => item.Name));
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT MAX(updated_utc_ticks) FROM graph_components WHERE component_key IN ({clause});";
            AddParameters(command, parameters);
            var ticks = command.ExecuteScalar();
            if (ticks is not null and not DBNull)
            {
                maximumTicks = Math.Max(maximumTicks ?? long.MinValue, Convert.ToInt64(ticks, CultureInfo.InvariantCulture));
            }
        }

        return (nodes, edges, evidence, aliases, maximumTicks is null ? null : new DateTimeOffset(maximumTicks.Value, TimeSpan.Zero));
    }

    private static int DeleteComponents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> components)
    {
        var affected = 0;
        foreach (var component in components.Distinct(StringComparer.Ordinal))
        {
            affected += ExecuteNonQuery(connection, transaction, "DELETE FROM graph_components WHERE component_key = $component;", ("$component", component));
            affected += ExecuteNonQuery(connection, transaction, "DELETE FROM graph_generations WHERE component_key = $component;", ("$component", component));
        }

        return affected;
    }

    private static IReadOnlyList<string> ResolveScopeComponents(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        GraphPrivacyScope scope)
    {
        if (scope.Kind == GraphPrivacyScopeKind.All)
        {
            return ReadAllComponents(connection, transaction);
        }

        var nodeKind = scope.Kind switch
        {
            GraphPrivacyScopeKind.File => GraphNodeKind.File.Value,
            GraphPrivacyScopeKind.Source => GraphNodeKind.Source.Value,
            GraphPrivacyScopeKind.Collection => GraphNodeKind.Collection.Value,
            _ => null,
        };
        var components = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                WITH scoped_nodes AS (
                    SELECT DISTINCT n.node_id
                    FROM graph_nodes n
                    JOIN graph_components c
                      ON c.component_key = n.component_key AND c.active_generation = n.generation
                    WHERE ($isNode = 1 AND n.node_id = $id)
                       OR ($isSource = 1 AND (n.node_id = $id OR n.source_id = $id OR (n.node_type = 'source' AND n.canonical_key = $id)))
                       OR ($isSource = 0 AND $kind IS NOT NULL AND n.node_type = $kind AND (n.node_id = $id OR n.canonical_key = $id))
                )
                SELECT DISTINCT n.component_key
                FROM graph_nodes n JOIN scoped_nodes s ON s.node_id = n.node_id
                UNION
                SELECT DISTINCT e.component_key
                FROM graph_edges e
                JOIN scoped_nodes s ON s.node_id = e.source_node_id OR s.node_id = e.target_node_id
                ORDER BY 1
                LIMIT 100001;
                """;
            AddParameters(
                command,
                ("$isNode", scope.Kind == GraphPrivacyScopeKind.Node ? 1 : 0),
                ("$isSource", scope.Kind == GraphPrivacyScopeKind.Source ? 1 : 0),
                ("$id", scope.StableId), ("$kind", nodeKind));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                components.Add(reader.GetString(0));
            }
        }

        if (components.Count > 100_000)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Full,
                "The selected graph privacy scope exceeds the bounded transactional maintenance ceiling.");
        }

        return components.Order(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizePrivacyScopeId(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        GraphPrivacyScope scope)
    {
        if (scope.Kind is GraphPrivacyScopeKind.All or GraphPrivacyScopeKind.Node)
        {
            return scope.StableId;
        }

        var nodeKind = scope.Kind switch
        {
            GraphPrivacyScopeKind.File => GraphNodeKind.File.Value,
            GraphPrivacyScopeKind.Source => GraphNodeKind.Source.Value,
            GraphPrivacyScopeKind.Collection => GraphNodeKind.Collection.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT CASE
                       WHEN $kind = 'source' THEN COALESCE(NULLIF(n.source_id, ''), n.canonical_key)
                       ELSE n.canonical_key
                   END
            FROM graph_nodes n
            JOIN graph_components c
              ON c.component_key = n.component_key AND c.active_generation = n.generation
            WHERE n.node_type = $kind
              AND (n.node_id = $id OR n.canonical_key = $id OR ($kind = 'source' AND n.source_id = $id))
            ORDER BY CASE WHEN n.node_id = $id THEN 0 WHEN n.canonical_key = $id THEN 1 ELSE 2 END,
                     n.last_validated_utc_ticks DESC, n.component_key, n.generation DESC
            LIMIT 1;
            """;
        AddParameters(command, ("$kind", nodeKind), ("$id", scope.StableId));
        return command.ExecuteScalar() as string ?? scope.StableId;
    }

    private static IReadOnlyList<string> ResolveDecisionComponents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphDecisionCommand command)
    {
        var components = new HashSet<string>(StringComparer.Ordinal);
        using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            """
            SELECT DISTINCT component_key FROM graph_nodes
            WHERE node_id IN ($subject, $target) OR canonical_key IN ($subject, $target)
            UNION
            SELECT DISTINCT component_key FROM graph_edges
            WHERE edge_id IN ($subject, $target) OR source_node_id IN ($subject, $target) OR target_node_id IN ($subject, $target);
            """;
        AddParameters(query, ("$subject", command.SubjectId), ("$target", command.TargetId));
        using var reader = query.ExecuteReader();
        while (reader.Read())
        {
            components.Add(reader.GetString(0));
        }

        return components.Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> ReadAllComponents(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var components = new List<string>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT component_key FROM graph_components ORDER BY component_key LIMIT 100001;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            components.Add(reader.GetString(0));
        }

        if (components.Count > 100_000)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Full,
                "The graph maintenance scope exceeds the bounded transactional ceiling.");
        }

        return components;
    }

    private static IReadOnlyList<string> ResolveRepairComponents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string stableId)
    {
        var components = new List<string>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT component_key
            FROM (
                SELECT c.component_key
                FROM graph_components c
                WHERE c.component_key = $id
                UNION
                SELECT n.component_key
                FROM graph_nodes n
                JOIN graph_components c
                  ON c.component_key = n.component_key AND c.active_generation = n.generation
                WHERE n.node_id = $id OR n.canonical_key = $id
                UNION
                SELECT e.component_key
                FROM graph_edges e
                JOIN graph_components c
                  ON c.component_key = e.component_key AND c.active_generation = e.generation
                WHERE e.edge_id = $id OR e.source_node_id = $id OR e.target_node_id = $id)
            ORDER BY component_key
            LIMIT 100001;
            """;
        command.Parameters.AddWithValue("$id", stableId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            components.Add(reader.GetString(0));
        }

        if (components.Count > 100_000)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Full,
                "The selected graph repair scope exceeds the bounded transactional ceiling.");
        }

        return components;
    }

    private static int MarkComponentsForReprojection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> components,
        DateTimeOffset nowUtc)
    {
        var affected = 0;
        foreach (var component in components.Distinct(StringComparer.Ordinal))
        {
            affected += ExecuteNonQuery(
                connection,
                transaction,
                "UPDATE graph_components SET freshness_state = $stale, updated_utc_ticks = $now WHERE component_key = $component;",
                ("$stale", State(GraphFreshnessState.Stale)), ("$now", nowUtc.UtcTicks), ("$component", component));
            ExecuteNonQuery(
                connection,
                transaction,
                "UPDATE graph_jobs SET stage = $stage, stage_input_fingerprint = NULL, execution_state = $pending, freshness_state = $stale, rebuild_generation = rebuild_generation + 1, updated_utc_ticks = $now WHERE component_key = $component AND execution_state = $complete;",
                ("$stage", State(GraphProjectionStage.ObservationCaptured)),
                ("$pending", State(GraphJobExecutionState.Pending)), ("$stale", State(GraphFreshnessState.Stale)),
                ("$now", nowUtc.UtcTicks), ("$component", component), ("$complete", State(GraphJobExecutionState.Complete)));
        }

        return affected;
    }

    private static (int Examined, int Findings) VerifyActiveGraph(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string>? selectedComponents,
        DateTimeOffset nowUtc)
    {
        var components = selectedComponents ?? ReadAllComponents(connection, transaction);
        if (selectedComponents is null)
        {
            ExecuteNonQuery(connection, transaction, "DELETE FROM graph_integrity_findings WHERE repaired_utc_ticks IS NULL;");
        }
        else
        {
            foreach (var component in components)
            {
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM graph_integrity_findings WHERE repaired_utc_ticks IS NULL AND component_key = $component;",
                    ("$component", component));
            }
        }

        var missingGeneration = 0;
        var missingEndpoints = 0;
        foreach (var component in components)
        {
            missingGeneration += Convert.ToInt32(
                ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM graph_components c LEFT JOIN graph_generations g ON g.component_key = c.component_key AND g.generation = c.active_generation WHERE c.component_key = $component AND (c.active_generation IS NULL OR g.generation IS NULL);",
                    ("$component", component)),
                CultureInfo.InvariantCulture);
            missingEndpoints += Convert.ToInt32(
                ExecuteScalar(
                    connection,
                    transaction,
                    """
                    WITH active_nodes AS (
                        SELECT DISTINCT n.node_id FROM graph_nodes n
                        JOIN graph_components c ON c.component_key = n.component_key AND c.active_generation = n.generation)
                    SELECT COUNT(*) FROM graph_edges e
                    JOIN graph_components c ON c.component_key = e.component_key AND c.active_generation = e.generation
                    LEFT JOIN active_nodes s ON s.node_id = e.source_node_id
                    LEFT JOIN active_nodes t ON t.node_id = e.target_node_id
                    WHERE e.component_key = $component AND (s.node_id IS NULL OR t.node_id IS NULL);
                    """,
                    ("$component", component)),
                CultureInfo.InvariantCulture);
        }

        var findings = missingGeneration + missingEndpoints;
        if (missingGeneration > 0)
        {
            RecordFinding(connection, transaction, "error", "missing-active-generation", missingGeneration, nowUtc);
        }

        if (missingEndpoints > 0)
        {
            RecordFinding(connection, transaction, "error", "missing-edge-endpoint", missingEndpoints, nowUtc);
        }

        return (components.Count, findings);
    }

    private static int MarkEvidenceDeficientComponents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string>? selectedComponents,
        DateTimeOffset nowUtc)
    {
        var selected = selectedComponents is null
            ? null
            : new HashSet<string>(selectedComponents, StringComparer.Ordinal);
        var components = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT DISTINCT e.component_key
                FROM graph_edges e
                JOIN graph_components c ON c.component_key = e.component_key AND c.active_generation = e.generation
                LEFT JOIN graph_evidence v ON v.component_key = e.component_key AND v.generation = e.generation AND v.edge_id = e.edge_id
                WHERE e.is_manual = 0
                GROUP BY e.component_key, e.generation, e.edge_id
                HAVING COUNT(v.evidence_id) = 0;
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
        }

        foreach (var component in components)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "UPDATE graph_components SET freshness_state = $stale, integrity_state = $repair, updated_utc_ticks = $now WHERE component_key = $component;",
                ("$stale", State(GraphFreshnessState.Stale)), ("$repair", State(GraphIntegrityState.RepairRequired)),
                ("$now", nowUtc.UtcTicks), ("$component", component));
        }

        return components.Count;
    }

    private static int RemoveInactiveGenerations(SqliteConnection connection, SqliteTransaction transaction) =>
        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM graph_generations
            WHERE NOT EXISTS (
                SELECT 1 FROM graph_components c
                WHERE c.component_key = graph_generations.component_key
                  AND c.active_generation = graph_generations.generation)
              AND generation < (
                SELECT COALESCE(MAX(newer.generation), graph_generations.generation)
                FROM graph_generations newer
                WHERE newer.component_key = graph_generations.component_key);
            """);

    private static void RecordFinding(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string severity,
        string category,
        int count,
        DateTimeOffset nowUtc) =>
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO graph_integrity_findings(finding_id, severity, category, bounded_detail, detected_utc_ticks) VALUES ($id, $severity, $category, $detail, $now);",
            ("$id", $"finding-{Guid.NewGuid():N}"), ("$severity", severity), ("$category", category),
            ("$detail", $"{count.ToString(CultureInfo.InvariantCulture)} bounded records require selective repair."),
            ("$now", nowUtc.UtcTicks));

    private static void WriteDiagnostic(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? runId,
        string category,
        string operation,
        string outcome,
        string? detail,
        DateTimeOffset nowUtc) =>
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO graph_diagnostics(run_id, category, operation, outcome, bounded_detail, created_utc_ticks) VALUES ($run, $category, $operation, $outcome, $detail, $now);",
            ("$run", runId), ("$category", Bound(category, 128)), ("$operation", Bound(operation, 128)),
            ("$outcome", Bound(outcome, 128)), ("$detail", detail is null ? null : Bound(detail, 1024)), ("$now", nowUtc.UtcTicks));

    private static void RecordProjectionStageDuration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        GraphProjectionStage stage,
        long previousStageTicks,
        DateTimeOffset nowUtc)
    {
        if (previousStageTicks < 0 || previousStageTicks > nowUtc.UtcTicks)
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("A graph job stage timestamp is invalid.");
        }

        var elapsedTicks = nowUtc.UtcTicks - previousStageTicks;
        if (elapsedTicks == 0)
        {
            return;
        }

        var durationMilliseconds = checked((elapsedTicks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond);
        var queueLength = Convert.ToInt64(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM graph_jobs WHERE run_id = $run AND execution_state IN ($pending, $running, $retry, $dependency, $resources);",
                ("$run", runId),
                ("$pending", State(GraphJobExecutionState.Pending)),
                ("$running", State(GraphJobExecutionState.Running)),
                ("$retry", State(GraphJobExecutionState.RetryableFailure)),
                ("$dependency", State(GraphJobExecutionState.WaitingForDependency)),
                ("$resources", State(GraphJobExecutionState.WaitingForResources))) ?? 0,
            CultureInfo.InvariantCulture);
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO graph_diagnostics(run_id, category, operation, outcome, duration_milliseconds, queue_length, created_utc_ticks) VALUES ($run, 'projection-stage', $stage, 'complete', $duration, $queue, $now);",
            ("$run", runId), ("$stage", State(stage)), ("$duration", durationMilliseconds),
            ("$queue", queueLength), ("$now", nowUtc.UtcTicks));
    }

    private static long CountActive(SqliteConnection connection, string table) =>
        Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                $"SELECT COUNT(*) FROM {table} r JOIN graph_components c ON c.component_key = r.component_key AND c.active_generation = r.generation;"),
            CultureInfo.InvariantCulture);

    private static int CountForComponents(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> components)
    {
        var total = 0;
        foreach (var chunk in components.Chunk(500))
        {
            var parameters = ComponentParameters(chunk);
            var clause = string.Join(",", parameters.Select(item => item.Name));
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM {table} r JOIN graph_components c ON c.component_key = r.component_key AND c.active_generation = r.generation WHERE r.component_key IN ({clause});";
            AddParameters(command, parameters);
            total = checked(total + Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture));
        }

        return total;
    }

    private static (string Name, object? Value)[] ComponentParameters(IReadOnlyList<string> components) =>
        components.Select((component, index) => ($"$component{index.ToString(CultureInfo.InvariantCulture)}", (object?)component)).ToArray();

    private int ReadDecisionCount(GraphPrivacyScope scope)
    {
        if (!File.Exists(_decisionDatabasePath))
        {
            return 0;
        }

        using var connection = SqliteKnowledgeInfrastructure.OpenConnection(_decisionDatabasePath, readOnly: true);
        return Convert.ToInt32(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT COUNT(*) FROM graph_native_decisions WHERE $all = 1 OR target_key = $id;",
                ("$all", scope.Kind == GraphPrivacyScopeKind.All ? 1 : 0), ("$id", scope.StableId)),
            CultureInfo.InvariantCulture);
    }

    private long ReadTotalDecisionCount()
    {
        if (!File.Exists(_decisionDatabasePath))
        {
            return 0;
        }

        using var connection = SqliteKnowledgeInfrastructure.OpenConnection(_decisionDatabasePath, readOnly: true);
        return Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(connection, "SELECT COUNT(*) FROM graph_native_decisions;"),
            CultureInfo.InvariantCulture);
    }

    private static void ValidateScope(GraphPrivacyScope scope)
    {
        if (scope.Kind == GraphPrivacyScopeKind.All)
        {
            return;
        }

        ValidateStableId(scope.StableId, nameof(scope));
    }

    private static long FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
