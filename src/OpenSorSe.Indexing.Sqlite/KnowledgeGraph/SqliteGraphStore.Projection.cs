using System.Globalization;
using System.Text.Json;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

public sealed partial class SqliteGraphStore
{
    private static readonly JsonSerializerOptions ProjectionJsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task QueueProjectionPageAsync(
        GraphProjectionRun run,
        GraphProjectionPage page,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(page);
        ValidateProjectionPage(run, page);
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                EnsureRunEpoch(connection, transaction, run, nowUtc, requireRunning: true);
                var existingPage = ReadManifestPage(connection, transaction, page.ManifestId, page.PageSequence);
                if (existingPage is not null)
                {
                    if (existingPage.Value.Count != page.ObservationCount ||
                        !string.Equals(existingPage.Value.Hash, page.CanonicalPageHash, StringComparison.Ordinal) ||
                        existingPage.Value.IsLast != page.IsLastPage)
                    {
                        throw SqliteKnowledgeInfrastructure.Corrupt("A projection page was replayed with different terminal metadata.");
                    }

                    transaction.Commit();
                    return 0;
                }

                var nextPage = Convert.ToInt64(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COALESCE(MAX(page_sequence) + 1, 0) FROM graph_manifest_pages WHERE manifest_id = $manifest;",
                        ("$manifest", page.ManifestId)),
                    CultureInfo.InvariantCulture);
                if (page.PageSequence != nextPage)
                {
                    throw new InvalidOperationException("Projection pages must be durably queued in sequence.");
                }

                var serialized = page.Observations.ToDictionary(
                    StableOrderKey,
                    GraphCanonicalSerializer.SerializeObservation,
                    StringComparer.Ordinal);
                var incomingBytes = serialized.Values.Sum(value => (long)Encoding.UTF8.GetByteCount(value));
                var inboxRows = Convert.ToInt64(
                    ExecuteScalar(connection, transaction, "SELECT COUNT(*) FROM graph_observation_inbox;"),
                    CultureInfo.InvariantCulture);
                var jobRows = Convert.ToInt64(
                    ExecuteScalar(connection, transaction, "SELECT COUNT(*) FROM graph_jobs;"),
                    CultureInfo.InvariantCulture);
                var inboxBytes = Convert.ToInt64(
                    ExecuteScalar(connection, transaction, "SELECT COALESCE(SUM(length(CAST(payload_json AS BLOB))), 0) FROM graph_observation_inbox;"),
                    CultureInfo.InvariantCulture);
                var maximumBytes = ReadMaximumDatabaseBytes(connection, transaction);
                if (ReadAllocatedDatabaseBytes(connection, transaction) + incomingBytes > maximumBytes * 3 / 4)
                {
                    RemoveInactiveGenerations(connection, transaction);
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM graph_diagnostics WHERE sequence NOT IN (SELECT sequence FROM graph_diagnostics ORDER BY sequence DESC LIMIT 10000);");
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM graph_job_attempts WHERE completed_utc_ticks IS NOT NULL AND attempt_id NOT IN (SELECT attempt_id FROM graph_job_attempts ORDER BY started_utc_ticks DESC, attempt_id DESC LIMIT 50000);");
                }

                if (inboxRows + page.ObservationCount > MaximumInboxRows ||
                    jobRows + page.ObservationCount > MaximumJobRows ||
                    inboxBytes + incomingBytes > MaximumInboxPayloadBytes ||
                    ReadAllocatedDatabaseBytes(connection, transaction) + incomingBytes > maximumBytes)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "UPDATE graph_runs SET current_stage = $stage, current_work_label = 'graph-storage-quota', updated_utc_ticks = $now WHERE run_id = $run AND coordinator_epoch = $epoch;",
                        ("$stage", State(GraphJobExecutionState.WaitingForResources)), ("$now", nowUtc.UtcTicks),
                        ("$run", run.RunId), ("$epoch", run.FencingEpoch));
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "INSERT INTO graph_meta(key, value) VALUES ('quota_blocked', '1') ON CONFLICT(key) DO UPDATE SET value = '1';");
                    transaction.Commit();
                    throw new SqliteKnowledgeStoreException(
                        SqliteKnowledgeFailureKind.Full,
                        "Graph projection paused before exceeding its configured storage or queue ceiling.");
                }

                var first = page.Observations.Count == 0 ? null : StableOrderKey(page.Observations[0]);
                var last = page.Observations.Count == 0 ? null : StableOrderKey(page.Observations[^1]);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_manifest_pages(
                        manifest_id, page_sequence, observation_count, canonical_page_hash,
                        first_stable_key, last_stable_key, is_last_page)
                    VALUES ($manifest, $sequence, $count, $hash, $first, $last, $isLast);
                    """,
                    ("$manifest", page.ManifestId), ("$sequence", page.PageSequence),
                    ("$count", page.ObservationCount), ("$hash", page.CanonicalPageHash),
                    ("$first", first), ("$last", last), ("$isLast", page.IsLastPage ? 1 : 0));

                foreach (var observation in page.Observations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var kind = observation.Kind.ToString();
                    var payload = serialized[StableOrderKey(observation)];
                    var existingHash = ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT canonical_row_hash FROM graph_manifest_rows WHERE manifest_id = $manifest AND row_kind = $kind AND stable_primary_key = $key;",
                        ("$manifest", page.ManifestId), ("$kind", kind), ("$key", observation.StableKey)) as string;
                    if (existingHash is not null && !string.Equals(existingHash, observation.CanonicalRowHash, StringComparison.Ordinal))
                    {
                        throw SqliteKnowledgeInfrastructure.Corrupt("A completed source manifest contains conflicting rows for one stable key.");
                    }

                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT OR IGNORE INTO graph_manifest_rows(
                            manifest_id, row_kind, stable_primary_key, canonical_row_hash, payload_json)
                        VALUES ($manifest, $kind, $key, $hash, $payload);
                        """,
                        ("$manifest", page.ManifestId), ("$kind", kind), ("$key", observation.StableKey),
                        ("$hash", observation.CanonicalRowHash), ("$payload", payload));
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT OR IGNORE INTO graph_observation_inbox(
                            manifest_id, source_id, row_kind, stable_primary_key, canonical_row_hash,
                            payload_json, state, enqueued_utc_ticks)
                        VALUES ($manifest, 'deep-index', $kind, $key, $hash, $payload, 'Pending', $now);
                        """,
                        ("$manifest", page.ManifestId), ("$kind", kind), ("$key", observation.StableKey),
                        ("$hash", observation.CanonicalRowHash), ("$payload", payload), ("$now", nowUtc.UtcTicks));
                    var observationSequence = Convert.ToInt64(
                        ExecuteScalar(
                            connection,
                            transaction,
                            "SELECT observation_sequence FROM graph_observation_inbox WHERE manifest_id = $manifest AND row_kind = $kind AND stable_primary_key = $key;",
                            ("$manifest", page.ManifestId), ("$kind", kind), ("$key", observation.StableKey)),
                        CultureInfo.InvariantCulture);
                    var logicalKey = GraphCanonicalSerializer.Hash($"{page.ManifestId}|{kind}|{observation.StableKey}|{observation.CanonicalRowHash}");
                    var jobId = $"graph-job-{logicalKey[..32].ToLowerInvariant()}";
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO graph_jobs(
                            job_id, logical_key, run_id, component_key, stage, execution_state,
                            freshness_state, integrity_state, priority, current_attempt, maximum_attempts,
                            source_manifest_id, source_row_hash, decision_sequence,
                            configuration_fingerprint, algorithm_name, algorithm_version, rebuild_generation,
                            observation_sequence, observation_kind, observation_stable_key,
                            created_utc_ticks, updated_utc_ticks)
                        VALUES ($job, $logical, $run, $component, $stage, $pending, $stale, $valid,
                            $priority, 0, $maximum, $manifest, $rowHash, 0, 'v2-stable',
                                'deterministic-graph-projection', '1.0.0', 1,
                                $observation, $kind, $key, $now, $now)
                        ON CONFLICT(logical_key) DO NOTHING;
                        """,
                        ("$job", jobId), ("$logical", logicalKey), ("$run", run.RunId),
                        ("$component", $"{kind}:{observation.StableKey}"),
                        ("$stage", State(GraphProjectionStage.ObservationCaptured)),
                        ("$priority", ProjectionPriority(observation.Kind)),
                        ("$pending", State(GraphJobExecutionState.Pending)), ("$stale", State(GraphFreshnessState.Stale)),
                        ("$valid", State(GraphIntegrityState.Valid)), ("$maximum", GraphLimits.MaximumRetryCount),
                        ("$manifest", page.ManifestId), ("$rowHash", observation.CanonicalRowHash),
                        ("$observation", observationSequence), ("$kind", kind), ("$key", observation.StableKey),
                        ("$now", nowUtc.UtcTicks));
                }

                var maximumObservationSequence = Convert.ToInt64(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COALESCE(MAX(observation_sequence), 0) FROM graph_observation_inbox WHERE manifest_id = $manifest;",
                        ("$manifest", page.ManifestId)),
                    CultureInfo.InvariantCulture);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_watermarks(
                        source_id, ingestion_manifest_id, ingestion_page_number,
                        ingestion_stable_key, ingested_observation_sequence, updated_utc_ticks)
                    VALUES ('deep-index', $manifest, $page, $key, $sequence, $now)
                    ON CONFLICT(source_id) DO UPDATE SET
                        ingestion_manifest_id = excluded.ingestion_manifest_id,
                        ingestion_page_number = excluded.ingestion_page_number,
                        ingestion_stable_key = excluded.ingestion_stable_key,
                        ingested_observation_sequence = MAX(graph_watermarks.ingested_observation_sequence, excluded.ingested_observation_sequence),
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$manifest", page.ManifestId), ("$page", page.PageSequence), ("$key", last),
                    ("$sequence", maximumObservationSequence), ("$now", nowUtc.UtcTicks));
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task CompleteInputManifestAsync(
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
                var actualCount = Convert.ToInt64(
                    ExecuteScalar(connection, transaction, "SELECT COUNT(*) FROM graph_manifest_rows WHERE manifest_id = $manifest;", ("$manifest", run.Snapshot.ManifestId)),
                    CultureInfo.InvariantCulture);
                var lastPageCount = Convert.ToInt32(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM graph_manifest_pages WHERE manifest_id = $manifest AND is_last_page = 1;",
                        ("$manifest", run.Snapshot.ManifestId)),
                    CultureInfo.InvariantCulture);
                if (actualCount != run.Snapshot.TotalObservationCount || lastPageCount != 1)
                {
                    throw new InvalidOperationException("The source manifest cannot complete until its terminal count and final page are present.");
                }

                var actualHash = GraphCanonicalSerializer.CalculateOrderedManifestHash(
                    ReadManifestRows(connection, transaction, run.Snapshot.ManifestId, cancellationToken));
                if (!string.Equals(actualHash, run.Snapshot.CanonicalManifestHash, StringComparison.Ordinal))
                {
                    throw SqliteKnowledgeInfrastructure.Corrupt("The source manifest terminal hash does not match its retained rows.");
                }

                ExecuteNonQuery(connection, transaction, "UPDATE graph_manifests SET is_active = 0 WHERE source_id = 'deep-index' AND scope = 'complete-schema3';");
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE graph_manifests SET state = 'Complete', completed_utc_ticks = $now, is_active = 1 WHERE manifest_id = $manifest AND state = 'Capturing';",
                    ("$now", nowUtc.UtcTicks), ("$manifest", run.Snapshot.ManifestId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE graph_runs SET input_manifest_complete = 1, updated_utc_ticks = $now WHERE run_id = $run AND coordinator_epoch = $epoch;",
                    ("$now", nowUtc.UtcTicks), ("$run", run.RunId), ("$epoch", run.FencingEpoch));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_watermarks(
                        source_id, latest_complete_manifest_id, latest_complete_revision,
                        ingested_privacy_sequence, updated_utc_ticks)
                    VALUES ('deep-index', $manifest, $revision, $privacy, $now)
                    ON CONFLICT(source_id) DO UPDATE SET
                        latest_complete_manifest_id = excluded.latest_complete_manifest_id,
                        latest_complete_revision = excluded.latest_complete_revision,
                        ingested_privacy_sequence = excluded.ingested_privacy_sequence,
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$manifest", run.Snapshot.ManifestId), ("$revision", run.Snapshot.Revision),
                    ("$privacy", run.Snapshot.PrivacySequence), ("$now", nowUtc.UtcTicks));
                // Prune after the completed-manifest watermark advances. Pruning at
                // run creation must retain both the active candidate and the last
                // completed recovery point; after completion the superseded point
                // is no longer protected and the configured ceiling can be enforced.
                PruneProjectionRecoveryHistory(connection, transaction);
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ApplyDecisionProjectionPageAsync(
        GraphProjectionRun run,
        GraphDecisionSnapshot decisionSnapshot,
        IReadOnlyList<GraphDecisionProjection> projections,
        bool isLastPage,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(decisionSnapshot);
        ArgumentNullException.ThrowIfNull(projections);
        ValidateDecisionSnapshot(run, decisionSnapshot);
        if (projections.Count > GraphLimits.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(projections), "A decision projection page exceeds its bounded limit.");
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                EnsureRunEpoch(connection, transaction, run, nowUtc, requireRunning: false);

                var stagedMaximum = Convert.ToInt64(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COALESCE(MAX(decision_sequence), 0) FROM graph_decision_projection_staging WHERE checkpoint_id = $checkpoint;",
                        ("$checkpoint", decisionSnapshot.CheckpointId)),
                    CultureInfo.InvariantCulture);
                var stagedRows = Convert.ToInt64(
                    ExecuteScalar(connection, transaction, "SELECT COUNT(*) FROM graph_decision_projection_staging;"),
                    CultureInfo.InvariantCulture);
                var stagedBytes = Convert.ToInt64(
                    ExecuteScalar(connection, transaction, "SELECT COALESCE(SUM(length(CAST(payload_json AS BLOB))), 0) FROM graph_decision_projection_staging;"),
                    CultureInfo.InvariantCulture);
                var maximumBytes = ReadMaximumDatabaseBytes(connection, transaction);
                foreach (var projection in projections.OrderBy(item => item.Decision.Sequence))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateDecisionProjection(projection, decisionSnapshot);
                    var existingHash = ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT canonical_hash FROM graph_decision_projection_staging WHERE checkpoint_id = $checkpoint AND decision_sequence = $sequence;",
                        ("$checkpoint", decisionSnapshot.CheckpointId), ("$sequence", projection.Decision.Sequence)) as string;
                    if (existingHash is not null)
                    {
                        if (!string.Equals(existingHash, projection.Decision.CanonicalHash, StringComparison.Ordinal))
                        {
                            throw SqliteKnowledgeInfrastructure.Corrupt("A graph-native decision projection was replayed with different content.");
                        }

                        continue;
                    }

                    if (projection.Decision.Sequence != stagedMaximum + 1)
                    {
                        throw new InvalidDataException("Graph-native decision projections must be staged without sequence gaps.");
                    }

                    var payload = GraphCanonicalSerializer.SerializeDecisionProjection(projection);
                    if (payload.Length > 65_536)
                    {
                        throw new InvalidDataException("A graph-native decision projection exceeds its durable payload limit.");
                    }

                    var payloadBytes = Encoding.UTF8.GetByteCount(payload);
                    if (stagedRows + 1 > 100_000 || stagedBytes + payloadBytes > 256L * 1024L * 1024L ||
                        ReadAllocatedDatabaseBytes(connection, transaction) + payloadBytes > maximumBytes)
                    {
                        ExecuteNonQuery(
                            connection,
                            transaction,
                            "UPDATE graph_runs SET current_stage = $stage, current_work_label = 'graph-decision-quota', updated_utc_ticks = $now WHERE run_id = $run AND coordinator_epoch = $epoch;",
                            ("$stage", State(GraphJobExecutionState.WaitingForResources)), ("$now", nowUtc.UtcTicks),
                            ("$run", run.RunId), ("$epoch", run.FencingEpoch));
                        ExecuteNonQuery(
                            connection,
                            transaction,
                            "INSERT INTO graph_meta(key, value) VALUES ('quota_blocked', '1') ON CONFLICT(key) DO UPDATE SET value = '1';");
                        transaction.Commit();
                        throw new SqliteKnowledgeStoreException(
                            SqliteKnowledgeFailureKind.Full,
                            "Graph decision projection paused before exceeding its configured storage ceiling.");
                    }

                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "INSERT INTO graph_decision_projection_staging(checkpoint_id, decision_sequence, decision_id, canonical_hash, payload_json, staged_utc_ticks) VALUES ($checkpoint, $sequence, $decision, $hash, $payload, $now);",
                        ("$checkpoint", decisionSnapshot.CheckpointId), ("$sequence", projection.Decision.Sequence),
                        ("$decision", projection.Decision.DecisionId), ("$hash", projection.Decision.CanonicalHash),
                        ("$payload", payload), ("$now", nowUtc.UtcTicks));
                    stagedMaximum = projection.Decision.Sequence;
                    stagedRows++;
                    stagedBytes += payloadBytes;
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_watermarks(
                        source_id, ingested_decision_sequence, ingested_decision_checkpoint_id,
                        ingested_decision_canonical_hash,
                        updated_utc_ticks)
                    VALUES ('deep-index', $sequence, $checkpoint, $canonical, $now)
                    ON CONFLICT(source_id) DO UPDATE SET
                        ingested_decision_sequence = excluded.ingested_decision_sequence,
                        ingested_decision_checkpoint_id = excluded.ingested_decision_checkpoint_id,
                        ingested_decision_canonical_hash = excluded.ingested_decision_canonical_hash,
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$sequence", stagedMaximum), ("$checkpoint", decisionSnapshot.CheckpointId),
                    ("$canonical", decisionSnapshot.CanonicalHash),
                    ("$now", nowUtc.UtcTicks));

                if (isLastPage)
                {
                    if (stagedMaximum != decisionSnapshot.Sequence)
                    {
                        throw new InvalidDataException("The staged decision ledger ended before its validated checkpoint.");
                    }

                    var stagedCount = Convert.ToInt64(
                        ExecuteScalar(
                            connection,
                            transaction,
                            "SELECT COUNT(*) FROM graph_decision_projection_staging WHERE checkpoint_id = $checkpoint;",
                            ("$checkpoint", decisionSnapshot.CheckpointId)),
                        CultureInfo.InvariantCulture);
                    if (stagedCount != decisionSnapshot.Sequence || stagedCount > 100_000)
                    {
                        throw new InvalidDataException("The staged decision ledger is incomplete or exceeds its safety ceiling.");
                    }

                    // Publication is deliberately deferred to CompleteProjectionAsync so source
                    // generations and their decision overlay become visible at one atomic boundary.
                    WriteDiagnostic(
                        connection,
                        transaction,
                        run.RunId,
                        "decision",
                        "stage-checkpoint",
                        "complete",
                        decisionSnapshot.CheckpointId,
                        nowUtc);
                }

                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CommitClaimAsync(
        GraphProjectionClaim claim,
        GraphComponentProjection projection,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(projection);
        ValidateProjection(projection);
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (!ClaimIsCurrent(connection, transaction, claim, nowUtc))
                {
                    transaction.Rollback();
                    return false;
                }

                if (!ClaimStageIsCurrent(
                        connection,
                        transaction,
                        claim,
                        GraphProjectionStage.ComponentValidated,
                        projection.InputFingerprint))
                {
                    transaction.Rollback();
                    return false;
                }

                var previousStageTicks = Convert.ToInt64(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT updated_utc_ticks FROM graph_jobs WHERE job_id = $job;",
                        ("$job", claim.WorkItem.WorkId))
                    ?? throw SqliteKnowledgeInfrastructure.Corrupt("The claimed graph job is missing."),
                    CultureInfo.InvariantCulture);

                if (!string.Equals(projection.SourceManifestId, claim.WorkItem.Observation is { } ?
                        ReadJobManifest(connection, transaction, claim.WorkItem.WorkId) : string.Empty, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Projection output does not belong to the claimed source manifest.");
                }
                var durableComponentKey = ReadJobComponent(connection, transaction, claim.WorkItem.WorkId);
                if (!string.Equals(projection.ComponentKey, durableComponentKey, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Projection output does not match the durable component identity.");
                }

                var retainedEdges = projection.Edges
                    .Where(edge => !RelationshipIsSuppressed(connection, transaction, edge))
                    .ToArray();
                var retainedEvidenceIds = retainedEdges
                    .SelectMany(edge => edge.EvidenceIds)
                    .ToHashSet(StringComparer.Ordinal);
                var retainedEvidence = projection.Evidence
                    .Where(evidence => retainedEvidenceIds.Contains(evidence.Id))
                    .ToArray();
                var effectiveProjection = projection with
                {
                    Edges = retainedEdges,
                    Evidence = retainedEvidence,
                };

                ValidateRequiredNodes(connection, transaction, effectiveProjection);
                ValidateEdgeDegrees(connection, transaction, effectiveProjection);

                var generation = Convert.ToInt64(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COALESCE(MAX(generation), 0) + 1 FROM graph_generations WHERE component_key = $component;",
                        ("$component", projection.ComponentKey)),
                    CultureInfo.InvariantCulture);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_generations(
                        component_key, generation, state, source_manifest_id, decision_sequence,
                        created_utc_ticks, node_count, edge_count, evidence_count, alias_count,
                        mention_count, fact_count, canonical_hash)
                    VALUES ($component, $generation, 'Staging', $manifest, 0, $now,
                            $nodes, $edges, $evidence, $aliases, $mentions, $facts, $hash);
                    """,
                    ("$component", projection.ComponentKey), ("$generation", generation),
                    ("$manifest", projection.SourceManifestId), ("$now", nowUtc.UtcTicks),
                    ("$nodes", effectiveProjection.Nodes.Count), ("$edges", effectiveProjection.Edges.Count),
                    ("$evidence", effectiveProjection.Evidence.Count), ("$aliases", effectiveProjection.Aliases.Count),
                    ("$mentions", projection.Mentions.Count), ("$facts", projection.Facts.Count),
                    ("$hash", projection.InputFingerprint));

                foreach (var node in projection.Nodes.OrderBy(item => item.Identity.NodeId, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO graph_nodes(
                            component_key, generation, node_id, node_type, source_entity_id, source_id,
                            identity_scope, canonical_key, normalization_version, canonical_inputs,
                            display_label, normalized_label, origin, source_manifest_id, observation_hash,
                            algorithm_name, algorithm_version, created_utc_ticks, last_validated_utc_ticks,
                            freshness_state, integrity_state, is_visible)
                        VALUES ($component, $generation, $node, $type, $entity, $source, $scope,
                                $canonical, $normalization, $inputs, $label, $normalizedLabel, $origin,
                                $manifest, $observation, $algorithm, $version, $created, $validated,
                                $freshness, $integrity, $visible);
                        """,
                        ("$component", projection.ComponentKey), ("$generation", generation),
                        ("$node", node.Identity.NodeId), ("$type", node.Identity.Kind.Value),
                        ("$entity", node.Identity.CanonicalKey),
                        ("$source", node.OwningSourceId),
                        ("$scope", node.Identity.Scope), ("$canonical", node.Identity.CanonicalKey),
                        ("$normalization", node.Identity.NormalizationVersion), ("$inputs", node.Identity.CanonicalInputs),
                        ("$label", node.DisplayLabel),
                        ("$normalizedLabel", NormalizeLabel(node.DisplayLabel)), ("$origin", State(node.Origin)),
                        ("$manifest", node.SourceManifestId), ("$observation", node.ObservationHash),
                        ("$algorithm", node.Algorithm), ("$version", node.AlgorithmVersion),
                        ("$created", node.CreatedAtUtc.UtcTicks), ("$validated", node.LastValidatedAtUtc.UtcTicks),
                        ("$freshness", State(node.Freshness)), ("$integrity", State(node.Integrity)),
                        ("$visible", node.IsVisible ? 1 : 0));
                }

                foreach (var edge in effectiveProjection.Edges.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO graph_edges(
                            component_key, generation, edge_id, source_node_id, target_node_id, edge_type,
                            confidence, origin, algorithm_name, algorithm_version, input_fingerprint,
                            created_utc_ticks, last_validated_utc_ticks, freshness_state, integrity_state, is_manual)
                        VALUES ($component, $generation, $edge, $source, $target, $type, $confidence,
                                $origin, $algorithm, $version, $fingerprint, $created, $validated,
                                $freshness, $integrity, $manual);
                        """,
                        ("$component", projection.ComponentKey), ("$generation", generation), ("$edge", edge.Id),
                        ("$source", edge.SourceNodeId), ("$target", edge.TargetNodeId), ("$type", edge.Kind.Value),
                        ("$confidence", State(edge.Confidence)), ("$origin", State(edge.Origin)),
                        ("$algorithm", edge.Algorithm), ("$version", edge.AlgorithmVersion),
                        ("$fingerprint", edge.InputFingerprint), ("$created", edge.CreatedAtUtc.UtcTicks),
                        ("$validated", edge.LastValidatedAtUtc.UtcTicks), ("$freshness", State(edge.Freshness)),
                        ("$integrity", State(edge.Integrity)), ("$manual", edge.IsManual ? 1 : 0));
                }

                var edgeByEvidence = effectiveProjection.Edges
                    .SelectMany(edge => edge.EvidenceIds.Select(id => (EvidenceId: id, EdgeId: edge.Id)))
                    .GroupBy(item => item.EvidenceId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().EdgeId, StringComparer.Ordinal);
                foreach (var evidence in effectiveProjection.Evidence.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    if (!edgeByEvidence.TryGetValue(evidence.Id, out var edgeId))
                    {
                        continue;
                    }

                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO graph_evidence(
                            component_key, generation, evidence_id, edge_id, evidence_kind,
                            source_evidence_key, explanation_template_code, explanation,
                            source_manifest_id, observation_hash)
                        VALUES ($component, $generation, $evidence, $edge, $kind, $sourceKey,
                                $template, $explanation, $manifest, $observation);
                        """,
                        ("$component", projection.ComponentKey), ("$generation", generation),
                        ("$evidence", evidence.Id), ("$edge", edgeId), ("$kind", evidence.Kind.Value),
                        ("$sourceKey", evidence.SourceEvidenceKey),
                        ("$template", evidence.ExplanationTemplateCode),
                        ("$explanation", evidence.Explanation),
                        ("$manifest", evidence.SourceManifestId), ("$observation", evidence.ObservationHash));
                }

                foreach (var alias in projection.Aliases.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO graph_aliases(
                            component_key, generation, alias_id, node_id, normalized_alias,
                            display_alias, origin, decision_id, created_utc_ticks)
                        VALUES ($component, $generation, $alias, $node, $normalized, $display, $origin, $decision, $created);
                        """,
                        ("$component", projection.ComponentKey), ("$generation", generation), ("$alias", alias.Id),
                        ("$node", alias.NodeId), ("$normalized", alias.NormalizedLabel),
                        ("$display", alias.Label), ("$origin", State(alias.Origin)),
                        ("$decision", alias.DecisionId), ("$created", alias.CreatedAtUtc.UtcTicks));
                }

                foreach (var mention in projection.Mentions.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO graph_mentions(
                            component_key, generation, mention_id, suggestion_kind, source_stable_key,
                            identity_scope, bounded_label, normalized_key, extractor_version,
                            evidence_ids_json, is_confirmed)
                        VALUES ($component, $generation, $mention, $kind, $source, $scope, $label,
                                $normalized, $version, $evidence, $confirmed);
                        """,
                        ("$component", projection.ComponentKey), ("$generation", generation), ("$mention", mention.Id),
                        ("$kind", State(mention.Kind)), ("$source", mention.SourceStableKey),
                        ("$scope", mention.Scope), ("$label", mention.Label),
                        ("$normalized", mention.NormalizedKey),
                        ("$version", mention.ExtractorVersion),
                        ("$evidence", JsonSerializer.Serialize(mention.EvidenceIds, ProjectionJsonOptions)),
                        ("$confirmed", mention.IsConfirmed ? 1 : 0));
                }

                foreach (var fact in projection.Facts.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO graph_facts(
                            component_key, generation, fact_id, node_id, fact_kind,
                            canonical_value, evidence_ids_json, algorithm_version)
                        VALUES ($component, $generation, $fact, $node, $kind, $value, $evidence, $version);
                        """,
                        ("$component", projection.ComponentKey), ("$generation", generation), ("$fact", fact.Id),
                        ("$node", fact.SubjectNodeId), ("$kind", fact.Kind.Value),
                        ("$value", fact.CanonicalValue),
                        ("$evidence", JsonSerializer.Serialize(fact.EvidenceIds, ProjectionJsonOptions)),
                        ("$version", fact.AlgorithmVersion));
                }

                ValidateStagedGeneration(connection, transaction, projection.ComponentKey, generation);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE graph_generations SET state = 'Published', validated_utc_ticks = $now, published_utc_ticks = $now WHERE component_key = $component AND generation = $generation;",
                    ("$now", nowUtc.UtcTicks), ("$component", projection.ComponentKey), ("$generation", generation));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_components(
                        component_key, active_generation, source_manifest_id, decision_sequence,
                        configuration_fingerprint, algorithm_name, algorithm_version,
                        freshness_state, integrity_state, updated_utc_ticks)
                    VALUES ($component, $generation, $manifest, 0, $fingerprint,
                            'deterministic-graph-projection', '1.0.0', $current, $valid, $now)
                    ON CONFLICT(component_key) DO UPDATE SET
                        active_generation = excluded.active_generation,
                        source_manifest_id = excluded.source_manifest_id,
                        configuration_fingerprint = excluded.configuration_fingerprint,
                        algorithm_name = excluded.algorithm_name,
                        algorithm_version = excluded.algorithm_version,
                        freshness_state = excluded.freshness_state,
                        integrity_state = excluded.integrity_state,
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$component", projection.ComponentKey), ("$generation", generation),
                    ("$manifest", projection.SourceManifestId), ("$fingerprint", projection.InputFingerprint),
                    ("$current", State(GraphFreshnessState.Current)), ("$valid", State(GraphIntegrityState.Valid)),
                    ("$now", nowUtc.UtcTicks));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_jobs
                    SET stage = $stage, stage_input_fingerprint = $fingerprint,
                        execution_state = $complete, freshness_state = $current, integrity_state = $valid,
                        claim_owner_instance_id = NULL, claim_token = NULL, claim_fencing_epoch = NULL,
                        claim_heartbeat_utc_ticks = NULL, claim_expires_utc_ticks = NULL,
                        updated_utc_ticks = $now
                    WHERE job_id = $job;
                    """,
                    ("$stage", State(GraphProjectionStage.ComponentPublished)), ("$fingerprint", projection.InputFingerprint),
                    ("$complete", State(GraphJobExecutionState.Complete)), ("$current", State(GraphFreshnessState.Current)),
                    ("$valid", State(GraphIntegrityState.Valid)), ("$now", nowUtc.UtcTicks), ("$job", claim.WorkItem.WorkId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_job_attempts
                    SET completed_utc_ticks = $now, outcome = $complete
                    WHERE job_id = $job AND attempt_number = $attempt
                      AND owner_instance_id = $owner AND claim_token = $token AND fencing_epoch = $epoch
                      AND completed_utc_ticks IS NULL;
                    """,
                    ("$now", nowUtc.UtcTicks), ("$complete", State(GraphJobExecutionState.Complete)),
                    ("$job", claim.WorkItem.WorkId), ("$attempt", claim.WorkItem.Attempt),
                    ("$owner", claim.OwnerInstanceId), ("$token", claim.ClaimToken), ("$epoch", claim.FencingEpoch));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_observation_inbox
                    SET state = 'Applied', applied_utc_ticks = $now
                    WHERE observation_sequence = (SELECT observation_sequence FROM graph_jobs WHERE job_id = $job);
                    """,
                    ("$now", nowUtc.UtcTicks), ("$job", claim.WorkItem.WorkId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE graph_watermarks
                    SET applied_observation_sequence = MAX(
                            applied_observation_sequence,
                            (SELECT observation_sequence FROM graph_jobs WHERE job_id = $job)),
                        updated_utc_ticks = $now
                    WHERE source_id = 'deep-index';
                    """,
                    ("$job", claim.WorkItem.WorkId), ("$now", nowUtc.UtcTicks));
                RecordProjectionStageDuration(
                    connection,
                    transaction,
                    claim.WorkItem.RunId,
                    GraphProjectionStage.ComponentPublished,
                    previousStageTicks,
                    nowUtc);
                transaction.Commit();
                return true;
            },
            cancellationToken);
    }

    private static void ValidateProjectionPage(GraphProjectionRun run, GraphProjectionPage page)
    {
        if (!string.Equals(page.ManifestId, run.Snapshot.ManifestId, StringComparison.Ordinal) ||
            page.SnapshotRevision != run.Snapshot.Revision || page.PageSequence < 0 ||
            page.ObservationCount != page.Observations.Count || page.Observations.Count > GraphLimits.MaximumProjectionPageSize)
        {
            throw new ArgumentException("The projection page does not belong to the claimed completed snapshot.", nameof(page));
        }

        var ordered = page.Observations.Select(StableOrderKey).ToArray();
        if (!ordered.SequenceEqual(ordered.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            ordered.Distinct(StringComparer.Ordinal).Count() != ordered.Length)
        {
            throw new ArgumentException("Projection observations must have unique stable-primary-key order.", nameof(page));
        }

        foreach (var observation in page.Observations)
        {
            ValidateObservation(observation);
        }

        var hash = GraphCanonicalSerializer.CalculatePageHash(page.Observations);
        if (!string.Equals(hash, page.CanonicalPageHash, StringComparison.Ordinal))
        {
            throw new ArgumentException("The projection page hash is invalid.", nameof(page));
        }
    }

    private static void ValidateObservation(GraphProjectionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ValidateStableId(observation.StableKey, nameof(observation));
        if (observation.Revision < 0 || string.IsNullOrWhiteSpace(observation.CanonicalRowHash) ||
            observation.CanonicalRowHash.Length > 128 || ContainsInvalidProjectionText(observation.CanonicalRowHash))
        {
            throw new InvalidDataException("A graph projection observation has invalid revision or hash metadata.");
        }

        switch (observation)
        {
            case GraphSourceObservation source:
                ValidateStableId(source.SourceId, nameof(observation));
                ValidateBoundedProjectionText(source.DisplayName, GraphLimits.MaximumLabelCharacters, "source label");
                ValidateBoundedProjectionText(source.PathSemanticsVersion, 64, "path-semantics version");
                if (!Enum.IsDefined(source.PathComparison))
                {
                    throw new InvalidDataException("A graph source observation has invalid path comparison metadata.");
                }

                break;
            case GraphFileObservation file:
                ValidateStableId(file.FileId, nameof(observation));
                ValidateStableId(file.SourceId, nameof(observation));
                ValidateBoundedProjectionText(file.FileName, GraphLimits.MaximumLabelCharacters, "file name");
                ValidateBoundedProjectionText(file.PathSemanticsVersion, 64, "path-semantics version");
                if (file.Length < 0 || !Enum.IsDefined(file.PathComparison) ||
                    file.FileName.IndexOfAny(['/', '\\']) >= 0 ||
                    file.RelativePath.Length > 4_096 || file.FolderRelativePath.Length > 4_096 ||
                    ContainsInvalidProjectionText(file.RelativePath) || ContainsInvalidProjectionText(file.FolderRelativePath) ||
                    IsUnsafeRelativePath(file.RelativePath) || IsUnsafeRelativePath(file.FolderRelativePath))
                {
                    throw new InvalidDataException("A graph file observation contains unsafe or unbounded metadata.");
                }

                if (file.ContentHash is not null)
                {
                    ValidateStableId(file.ContentHash, nameof(observation));
                    ValidateBoundedProjectionText(
                        file.ContentHashAlgorithmVersion ?? string.Empty,
                        64,
                        "content-hash algorithm version");
                }
                else if (file.ContentHashAlgorithmVersion is not null)
                {
                    throw new InvalidDataException("A graph file observation has a hash algorithm without a content hash.");
                }

                break;
            case GraphRelationshipObservation relationship:
                ValidateStableId(relationship.RelationshipId, nameof(observation));
                ValidateStableId(relationship.FirstFileId, nameof(observation));
                ValidateStableId(relationship.SecondFileId, nameof(observation));
                ValidateBoundedProjectionText(relationship.RelationshipType, 64, "relationship type");
                ValidateBoundedProjectionText(relationship.Algorithm, 128, "relationship algorithm");
                ValidateBoundedProjectionText(relationship.AlgorithmVersion, 64, "relationship algorithm version");
                if (string.Equals(relationship.FirstFileId, relationship.SecondFileId, StringComparison.Ordinal) ||
                    !Enum.IsDefined(relationship.Confidence) || relationship.Evidence is null ||
                    relationship.Evidence.Count > GraphLimits.MaximumEvidencePerEdge)
                {
                    throw new InvalidDataException("A graph relationship observation has invalid endpoints, confidence, or evidence count.");
                }

                foreach (var evidence in relationship.Evidence)
                {
                    if (evidence is null || !evidence.Kind.IsStable)
                    {
                        throw new InvalidDataException("A graph relationship observation has unsupported evidence.");
                    }

                    ValidateStableId(evidence.StableKey, nameof(observation));
                    ValidateBoundedProjectionText(evidence.EvidenceKey, 256, "relationship evidence key");
                    ValidateBoundedProjectionText(evidence.ExplanationTemplateCode, 128, "relationship evidence template");
                    ValidateBoundedProjectionText(evidence.Explanation, GraphLimits.MaximumEvidenceTextCharacters, "relationship evidence explanation");
                    if (string.IsNullOrWhiteSpace(evidence.CanonicalObservationHash) ||
                        evidence.CanonicalObservationHash.Length > 128 ||
                        ContainsInvalidProjectionText(evidence.CanonicalObservationHash))
                    {
                        throw new InvalidDataException("A graph relationship evidence hash is invalid.");
                    }
                }

                break;
            case GraphCollectionObservation collection:
                ValidateStableId(collection.CollectionId, nameof(observation));
                ValidateBoundedProjectionText(collection.Title, GraphLimits.MaximumLabelCharacters, "collection title");
                break;
            case GraphCollectionMembershipObservation membership:
                ValidateStableId(membership.CollectionId, nameof(observation));
                ValidateStableId(membership.FileId, nameof(observation));
                break;
            case GraphLegacyDecisionObservation decision:
                ValidateBoundedProjectionText(decision.DecisionNamespace, 128, "legacy decision namespace");
                ValidateStableId(decision.LegacyDecisionKey, nameof(observation));
                ValidateBoundedProjectionText(decision.ActionCode, 128, "legacy decision action");
                break;
            case GraphDeletionObservation deletion:
                ValidateStableId(deletion.DeletedStableKey, nameof(observation));
                if (!Enum.IsDefined(deletion.DeletedKind) || deletion.DeletedKind == GraphProjectionObservationKind.Deletion)
                {
                    throw new InvalidDataException("A graph deletion observation has an invalid former kind.");
                }

                break;
            default:
                throw new InvalidDataException("A graph projection observation type is unsupported.");
        }
    }

    private static bool IsUnsafeRelativePath(string value)
    {
        if (value.IndexOf('\0') >= 0 || value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith('\\') || Path.IsPathRooted(value) ||
            value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
        {
            return true;
        }

        return value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static void ValidateProjection(GraphComponentProjection projection)
    {
        ValidateStableId(projection.ComponentKey, nameof(projection));
        ValidateStableId(projection.InputFingerprint, nameof(projection));
        ValidateStableId(projection.SourceManifestId, nameof(projection));
        if (projection.ObservationRevision < 0 || projection.Nodes is null || projection.Edges is null ||
            projection.Evidence is null || projection.RequiredNodeIds is null || projection.Aliases is null ||
            projection.Mentions is null || projection.Facts is null || projection.LegacyDecisions is null)
        {
            throw new InvalidDataException("A graph component has invalid metadata or a missing replacement collection.");
        }

        if (projection.Nodes.Count > GraphLimits.MaximumComponentNodes ||
            projection.Edges.Count > GraphLimits.MaximumComponentEdges ||
            projection.Evidence.Count > GraphLimits.MaximumComponentEdges * GraphLimits.MaximumEvidencePerEdge ||
            projection.Aliases.Count > GraphLimits.MaximumComponentNodes * GraphLimits.MaximumAliasesPerNode ||
            projection.Mentions.Count > GraphLimits.MaximumComponentEdges ||
            projection.Facts.Count > GraphLimits.MaximumComponentEdges ||
            projection.LegacyDecisions.Count > GraphLimits.MaximumComponentEdges)
        {
            throw new ArgumentOutOfRangeException(nameof(projection), "A graph component exceeds a stable safety ceiling.");
        }

        RequireUnique(projection.Nodes.Select(item => item.Identity.NodeId), "node");
        RequireUnique(projection.Edges.Select(item => item.Id), "edge");
        RequireUnique(projection.Evidence.Select(item => item.Id), "evidence");
        RequireUnique(projection.Aliases.Select(item => item.Id), "alias");
        RequireUnique(projection.Mentions.Select(item => item.Id), "mention");
        RequireUnique(projection.Facts.Select(item => item.Id), "fact");
        RequireUnique(projection.RequiredNodeIds, "required-node");
        if (projection.RequiredNodeIds.Count > GraphLimits.MaximumComponentNodes)
        {
            throw new ArgumentOutOfRangeException(nameof(projection), "A graph component references too many external nodes.");
        }

        var localNodeIds = projection.Nodes.Select(item => item.Identity.NodeId).ToHashSet(StringComparer.Ordinal);
        var requiredNodeIds = projection.RequiredNodeIds.ToHashSet(StringComparer.Ordinal);
        var evidenceIds = projection.Evidence.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var node in projection.Nodes)
        {
            ValidateNodeProjection(node, projection.SourceManifestId);
        }

        foreach (var evidence in projection.Evidence)
        {
            ValidateEvidenceProjection(evidence, projection.SourceManifestId);
        }

        foreach (var edge in projection.Edges)
        {
            ValidateEdgeProjection(edge);
            if (string.Equals(edge.SourceNodeId, edge.TargetNodeId, StringComparison.Ordinal) ||
                edge.EvidenceIds.Count > GraphLimits.MaximumEvidencePerEdge ||
                edge.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != edge.EvidenceIds.Count ||
                edge.EvidenceIds.Any(id => !evidenceIds.Contains(id)) ||
                (!localNodeIds.Contains(edge.SourceNodeId) && !requiredNodeIds.Contains(edge.SourceNodeId)) ||
                (!localNodeIds.Contains(edge.TargetNodeId) && !requiredNodeIds.Contains(edge.TargetNodeId)))
            {
                throw new InvalidDataException("A graph edge is cyclic or references invalid bounded evidence.");
            }
        }

        foreach (var aliases in projection.Aliases.GroupBy(item => item.NodeId, StringComparer.Ordinal))
        {
            if (aliases.Count() > GraphLimits.MaximumAliasesPerNode)
            {
                throw new InvalidDataException("A graph node exceeds the bounded alias count.");
            }
        }

        foreach (var alias in projection.Aliases)
        {
            ValidateStableId(alias.Id, nameof(projection));
            ValidateStableId(alias.NodeId, nameof(projection));
            if (!localNodeIds.Contains(alias.NodeId) || !Enum.IsDefined(alias.Origin))
            {
                throw new InvalidDataException("A graph alias references an invalid local node or origin.");
            }

            ValidateBoundedProjectionText(alias.Label, GraphLimits.MaximumLabelCharacters, "alias label");
            ValidateBoundedProjectionText(alias.NormalizedLabel, GraphLimits.MaximumLabelCharacters, "normalized alias");
            if (!string.Equals(alias.NormalizedLabel, NormalizeLabel(alias.Label), StringComparison.Ordinal))
            {
                throw new InvalidDataException("A graph alias does not match its deterministic normalization.");
            }

            if (alias.DecisionId is not null)
            {
                ValidateStableId(alias.DecisionId, nameof(projection));
            }
        }

        foreach (var mention in projection.Mentions)
        {
            ValidateStableId(mention.Id, nameof(projection));
            ValidateStableId(mention.SourceStableKey, nameof(projection));
            if (!Enum.IsDefined(mention.Kind) || mention.EvidenceIds is null ||
                mention.EvidenceIds.Count > GraphLimits.MaximumEvidencePerEdge ||
                mention.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != mention.EvidenceIds.Count ||
                mention.EvidenceIds.Any(id => !evidenceIds.Contains(id)))
            {
                throw new InvalidDataException("A graph mention has invalid evidence or category metadata.");
            }

            ValidateBoundedProjectionText(mention.Scope, GraphLimits.MaximumStableIdCharacters, "mention scope");
            ValidateBoundedProjectionText(mention.Label, GraphLimits.MaximumLabelCharacters, "mention label");
            ValidateBoundedProjectionText(mention.NormalizedKey, GraphLimits.MaximumLabelCharacters, "mention normalized key");
            ValidateBoundedProjectionText(mention.ExtractorVersion, 64, "mention extractor version");
        }

        foreach (var fact in projection.Facts)
        {
            ValidateStableId(fact.Id, nameof(projection));
            ValidateStableId(fact.SubjectNodeId, nameof(projection));
            if (!localNodeIds.Contains(fact.SubjectNodeId) || !fact.Kind.IsStable || fact.EvidenceIds is null ||
                fact.EvidenceIds.Count > GraphLimits.MaximumEvidencePerEdge ||
                fact.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != fact.EvidenceIds.Count ||
                fact.EvidenceIds.Any(id => !evidenceIds.Contains(id)))
            {
                throw new InvalidDataException("A graph fact has invalid subject, kind, or evidence references.");
            }

            ValidateBoundedProjectionText(fact.CanonicalValue, 256, "fact value");
            ValidateBoundedProjectionText(fact.AlgorithmVersion, 64, "fact algorithm version");
        }
    }

    private static void ValidateNodeProjection(GraphNode node, string sourceManifestId)
    {
        if (node is null || node.Identity is null)
        {
            throw new InvalidDataException("A graph projection contains a null node or identity.");
        }

        ValidateStableId(node.Identity.NodeId, nameof(node));
        ValidateStableId(node.SourceManifestId, nameof(node));
        ValidateStableId(node.ObservationHash, nameof(node));
        if (!node.Identity.Kind.IsStable || !Enum.IsDefined(node.Origin) || !Enum.IsDefined(node.Freshness) ||
            !Enum.IsDefined(node.Integrity) ||
            !string.Equals(node.SourceManifestId, sourceManifestId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A graph node has unsupported state or does not belong to its manifest.");
        }

        if (node.OwningSourceId is not null)
        {
            ValidateStableId(node.OwningSourceId, nameof(node));
        }

        ValidateBoundedProjectionText(node.Identity.Scope, GraphLimits.MaximumStableIdCharacters, "identity scope");
        ValidateBoundedProjectionText(node.Identity.CanonicalKey, GraphLimits.MaximumCanonicalIdentityCharacters, "identity canonical key");
        ValidateBoundedProjectionText(node.Identity.NormalizationVersion, 64, "identity normalization version");
        ValidateBoundedProjectionText(node.Identity.CanonicalInputs, GraphLimits.MaximumCanonicalIdentityCharacters, "identity inputs");
        ValidateBoundedProjectionText(node.DisplayLabel, GraphLimits.MaximumLabelCharacters, "node label");
        if (node.DisplayLabel.Trim().Normalize().ToUpperInvariant().Length > GraphLimits.MaximumLabelCharacters)
        {
            throw new InvalidDataException("A graph node label expands beyond the normalized storage ceiling.");
        }

        ValidateBoundedProjectionText(node.Algorithm, 128, "node algorithm");
        ValidateBoundedProjectionText(node.AlgorithmVersion, 64, "node algorithm version");
    }

    private static void ValidateEdgeProjection(GraphEdge edge)
    {
        if (edge is null || edge.EvidenceIds is null)
        {
            throw new InvalidDataException("A graph projection contains a null edge or evidence list.");
        }

        ValidateStableId(edge.Id, nameof(edge));
        ValidateStableId(edge.SourceNodeId, nameof(edge));
        ValidateStableId(edge.TargetNodeId, nameof(edge));
        ValidateStableId(edge.InputFingerprint, nameof(edge));
        if (!edge.Kind.IsStable || !Enum.IsDefined(edge.Confidence) || !Enum.IsDefined(edge.Origin) ||
            !Enum.IsDefined(edge.Freshness) || !Enum.IsDefined(edge.Integrity) ||
            !edge.IsManual && edge.EvidenceIds.Count == 0)
        {
            throw new InvalidDataException("A graph edge has unsupported state or lacks required evidence.");
        }

        ValidateBoundedProjectionText(edge.Algorithm, 128, "edge algorithm");
        ValidateBoundedProjectionText(edge.AlgorithmVersion, 64, "edge algorithm version");
    }

    private static void ValidateEvidenceProjection(GraphEvidenceReference evidence, string sourceManifestId)
    {
        if (evidence is null)
        {
            throw new InvalidDataException("A graph projection contains null evidence.");
        }

        ValidateStableId(evidence.Id, nameof(evidence));
        ValidateStableId(evidence.SourceManifestId, nameof(evidence));
        ValidateStableId(evidence.ObservationHash, nameof(evidence));
        if (!evidence.Kind.IsStable || !string.Equals(evidence.SourceManifestId, sourceManifestId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Graph evidence has unsupported kind or manifest authority.");
        }

        ValidateBoundedProjectionText(evidence.SourceEvidenceKey, 256, "evidence source key");
        ValidateBoundedProjectionText(evidence.ExplanationTemplateCode, 128, "evidence template");
        ValidateBoundedProjectionText(evidence.Explanation, GraphLimits.MaximumEvidenceTextCharacters, "evidence explanation");
    }

    private static void ValidateBoundedProjectionText(string value, int maximum, string category)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || ContainsInvalidProjectionText(value))
        {
            throw new InvalidDataException($"A graph projection contains invalid or oversized {category}.");
        }
    }

    private static bool ContainsInvalidProjectionText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\0' || char.IsControl(character) ||
                char.IsSurrogate(character) &&
                (index + 1 >= value.Length || !char.IsSurrogatePair(character, value[++index])))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateRequiredNodes(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphComponentProjection projection)
    {
        foreach (var requiredNodeId in projection.RequiredNodeIds)
        {
            var available = Convert.ToInt32(
                ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM graph_nodes n JOIN graph_components c ON c.component_key = n.component_key AND c.active_generation = n.generation WHERE n.node_id = $node AND n.is_visible = 1 AND c.freshness_state = $current AND c.integrity_state = $valid;",
                    ("$node", requiredNodeId), ("$current", State(GraphFreshnessState.Current)),
                    ("$valid", State(GraphIntegrityState.Valid))),
                CultureInfo.InvariantCulture);
            if (available == 0)
            {
                throw new InvalidDataException("A graph component requires an endpoint that has not been published.");
            }
        }
    }

    private static void ValidateEdgeDegrees(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphComponentProjection projection)
    {
        foreach (var group in projection.Edges
                     .SelectMany(edge => new[] { edge.SourceNodeId, edge.TargetNodeId })
                     .GroupBy(nodeId => nodeId, StringComparer.Ordinal))
        {
            var retained = Convert.ToInt32(
                ExecuteScalar(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(DISTINCT e.edge_id)
                    FROM graph_edges e
                    JOIN graph_components c
                      ON c.component_key = e.component_key AND c.active_generation = e.generation
                    WHERE (e.source_node_id = $node OR e.target_node_id = $node)
                      AND e.component_key <> $component
                      AND c.freshness_state = $current AND c.integrity_state = $valid;
                    """,
                    ("$node", group.Key), ("$component", projection.ComponentKey),
                    ("$current", State(GraphFreshnessState.Current)), ("$valid", State(GraphIntegrityState.Valid))),
                CultureInfo.InvariantCulture);
            if (retained + group.Count() > GraphLimits.MaximumEdgesPerNode)
            {
                throw new InvalidDataException("A graph projection would exceed the stable incident-edge ceiling.");
            }
        }
    }

    private static bool RelationshipIsSuppressed(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphEdge edge)
    {
        static string? SourceFor(
            SqliteConnection retainedConnection,
            SqliteTransaction retainedTransaction,
            string nodeId) => ExecuteScalar(
                retainedConnection,
                retainedTransaction,
                "SELECT n.source_id FROM graph_nodes n JOIN graph_components c ON c.component_key = n.component_key AND c.active_generation = n.generation WHERE n.node_id = $node AND c.freshness_state = $current AND c.integrity_state = $valid ORDER BY n.last_validated_utc_ticks DESC, n.component_key LIMIT 1;",
                ("$node", nodeId), ("$current", State(GraphFreshnessState.Current)),
                ("$valid", State(GraphIntegrityState.Valid))) as string;

        var source = SourceFor(connection, transaction, edge.SourceNodeId);
        var target = SourceFor(connection, transaction, edge.TargetNodeId);
        var scope = !string.IsNullOrWhiteSpace(source) && string.Equals(source, target, StringComparison.Ordinal)
            ? string.Concat("source:", source)
            : "cross-source";
        var key = RelationshipSuppressionKey(edge.SourceNodeId, edge.TargetNodeId, edge.Kind.Value, scope);
        return Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM graph_decision_suppressions WHERE suppression_kind = 'relationship' AND stable_id = $key;",
                ("$key", key)),
            CultureInfo.InvariantCulture) > 0;
    }

    private static void ValidateDecisionSnapshot(GraphProjectionRun run, GraphDecisionSnapshot snapshot)
    {
        if (!snapshot.IsValid || snapshot.Sequence < 0 || string.IsNullOrWhiteSpace(snapshot.CheckpointId) ||
            snapshot.CheckpointId.Length > GraphLimits.MaximumStableIdCharacters ||
            snapshot.Sequence != run.Snapshot.GraphDecisionSequence ||
            !string.Equals(snapshot.CheckpointId, run.Snapshot.GraphDecisionCheckpointId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The graph-native decision checkpoint does not belong to this projection run.");
        }
    }

    private static void ValidateDecisionProjection(GraphDecisionProjection projection, GraphDecisionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.Decision.Sequence is <= 0 || projection.Decision.Sequence > snapshot.Sequence ||
            string.IsNullOrWhiteSpace(projection.Decision.DecisionId) ||
            string.IsNullOrWhiteSpace(projection.Decision.CanonicalHash) ||
            !string.Equals(projection.SubjectId, projection.Decision.Command.SubjectId, StringComparison.Ordinal) ||
            !string.Equals(projection.TargetId, projection.Decision.Command.TargetId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A graph-native decision projection is inconsistent with its ledger entry.");
        }

        ValidateStableId(projection.Decision.DecisionId, nameof(projection));
        ValidateStableId(projection.Decision.CanonicalHash, nameof(projection));
        ValidateStableId(projection.SubjectId, nameof(projection));
        if (projection.TargetId is not null)
        {
            ValidateStableId(projection.TargetId, nameof(projection));
        }

        if (projection.ReplacementLabel is not null)
        {
            ValidateBoundedProjectionText(
                projection.ReplacementLabel,
                GraphLimits.MaximumLabelCharacters,
                "decision replacement label");
        }

        if (projection.Node is not null)
        {
            ValidateNodeProjection(projection.Node, string.Concat("decision:", snapshot.CheckpointId));
        }

        if (projection.Edge is not null)
        {
            ValidateEdgeProjection(projection.Edge);
        }

        if (projection.Alias is not null)
        {
            ValidateStableId(projection.Alias.Id, nameof(projection));
            ValidateStableId(projection.Alias.NodeId, nameof(projection));
            ValidateBoundedProjectionText(projection.Alias.Label, GraphLimits.MaximumLabelCharacters, "decision alias label");
            ValidateBoundedProjectionText(projection.Alias.NormalizedLabel, GraphLimits.MaximumLabelCharacters, "decision normalized alias");
            if (!Enum.IsDefined(projection.Alias.Origin) ||
                !string.Equals(projection.Alias.NormalizedLabel, NormalizeLabel(projection.Alias.Label), StringComparison.Ordinal))
            {
                throw new InvalidDataException("A graph-native alias has invalid origin or normalization.");
            }

            if (projection.Alias.DecisionId is not null)
            {
                ValidateStableId(projection.Alias.DecisionId, nameof(projection));
            }
        }
    }

    private static void PublishDecisionOverlay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphDecisionSnapshot snapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        const string componentKey = "graph-native-decision-overlay";
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, GraphAlias>(StringComparer.Ordinal);
        var edges = new Dictionary<string, GraphEdge>(StringComparer.Ordinal);
        var suppressions = new Dictionary<(string Kind, string Id), long>();
        var exclusions = new Dictionary<(GraphPrivacyScopeKind Kind, string Id), long>();
        var mergedInto = new Dictionary<string, string>(StringComparer.Ordinal);
        long after = 0;
        while (after < snapshot.Sequence)
        {
            var page = new List<GraphDecisionProjection>(GraphLimits.MaximumPageSize);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "SELECT payload_json FROM graph_decision_projection_staging WHERE checkpoint_id = $checkpoint AND decision_sequence > $after ORDER BY decision_sequence LIMIT $maximum;";
                AddParameters(command, ("$checkpoint", snapshot.CheckpointId), ("$after", after),
                    ("$maximum", GraphLimits.MaximumPageSize));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    page.Add(GraphCanonicalSerializer.DeserializeDecisionProjection(reader.GetString(0)));
                }
            }

            if (page.Count == 0)
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("The staged decision ledger contains a sequence gap.");
            }

            foreach (var projection in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateDecisionProjection(projection, snapshot);
                var command = projection.Decision.Command;
                switch (command.Kind)
                {
                    case GraphDecisionKind.CreateManualEntity when projection.Node is not null:
                        nodes[projection.Node.Identity.NodeId] = projection.Node;
                        break;
                    case GraphDecisionKind.RenameManualEntity when projection.ReplacementLabel is not null:
                        if (nodes.TryGetValue(projection.SubjectId, out var renamed))
                        {
                            nodes[projection.SubjectId] = renamed with
                            {
                                DisplayLabel = projection.ReplacementLabel,
                                LastValidatedAtUtc = nowUtc,
                            };
                        }

                        break;
                    case GraphDecisionKind.AddAlias when projection.Alias is not null:
                        aliases[projection.Alias.Id] = projection.Alias;
                        break;
                    case GraphDecisionKind.RemoveAlias when command.Label is not null:
                        foreach (var key in aliases
                                     .Where(item => string.Equals(item.Value.NodeId, projection.SubjectId, StringComparison.Ordinal) &&
                                                    string.Equals(item.Value.NormalizedLabel, NormalizeLabel(command.Label), StringComparison.Ordinal))
                                     .Select(item => item.Key)
                                     .ToArray())
                        {
                            aliases.Remove(key);
                        }

                        break;
                    case GraphDecisionKind.LinkNodes when projection.Edge is not null:
                        edges[projection.Edge.Id] = projection.Edge;
                        break;
                    case GraphDecisionKind.MergeEntities when projection.TargetId is not null:
                        // Subject is the retained target and TargetId is the source being merged.
                        if (!nodes.ContainsKey(projection.SubjectId) || !nodes.ContainsKey(projection.TargetId))
                        {
                            throw new InvalidDataException("A manual merge references an entity that is not present in the replayed ledger.");
                        }

                        if (string.Equals(
                                ResolveMergedEntity(mergedInto, projection.SubjectId),
                                ResolveMergedEntity(mergedInto, projection.TargetId),
                                StringComparison.Ordinal))
                        {
                            break;
                        }

                        mergedInto[projection.TargetId] = ResolveMergedEntity(mergedInto, projection.SubjectId);
                        _ = ResolveMergedEntity(mergedInto, projection.TargetId);
                        break;
                    case GraphDecisionKind.SplitEntities when projection.TargetId is not null:
                        if (mergedInto.TryGetValue(projection.TargetId, out var mergedTarget) &&
                            string.Equals(ResolveMergedEntity(mergedInto, mergedTarget), ResolveMergedEntity(mergedInto, projection.SubjectId), StringComparison.Ordinal))
                        {
                            mergedInto.Remove(projection.TargetId);
                        }

                        break;
                    case GraphDecisionKind.UnlinkNodes:
                        edges.Remove(projection.SubjectId);
                        suppressions[("edge", projection.SubjectId)] = projection.Decision.Sequence;
                        break;
                    case GraphDecisionKind.NeverMerge:
                        edges.Remove(projection.SubjectId);
                        suppressions[("edge", projection.SubjectId)] = projection.Decision.Sequence;
                        if (command.RelationshipSourceNodeId is not null &&
                            command.RelationshipTargetNodeId is not null &&
                            command.RelationshipKind is { } relationshipKind &&
                            command.RelationshipScope is not null)
                        {
                            suppressions[("relationship", RelationshipSuppressionKey(
                                command.RelationshipSourceNodeId,
                                command.RelationshipTargetNodeId,
                                relationshipKind.Value,
                                command.RelationshipScope))] = projection.Decision.Sequence;
                        }

                        break;
                    case GraphDecisionKind.RejectSuggestion:
                        suppressions[("mention", projection.SubjectId)] = projection.Decision.Sequence;
                        break;
                    case GraphDecisionKind.Forget:
                        nodes.Remove(projection.SubjectId);
                        foreach (var key in aliases.Where(item => item.Value.NodeId == projection.SubjectId).Select(item => item.Key).ToArray())
                        {
                            aliases.Remove(key);
                        }

                        foreach (var key in edges.Where(item => item.Value.SourceNodeId == projection.SubjectId || item.Value.TargetNodeId == projection.SubjectId).Select(item => item.Key).ToArray())
                        {
                            edges.Remove(key);
                        }

                        var scopeCode = command.Reason is null
                            ? "node"
                            : command.Reason.EndsWith("-source", StringComparison.Ordinal) ? "source"
                            : command.Reason.EndsWith("-file", StringComparison.Ordinal) || command.Reason.EndsWith("-collection", StringComparison.Ordinal) ? "canonical"
                            : command.Reason.EndsWith("-all", StringComparison.Ordinal) ? "all"
                            : "node";
                        suppressions[(scopeCode, projection.SubjectId)] = projection.Decision.Sequence;
                        break;
                    case GraphDecisionKind.Exclude:
                        {
                            var scope = DecisionPrivacyScope(command);
                            var stableId = NormalizePrivacyScopeId(
                                connection,
                                transaction,
                                new GraphPrivacyScope(scope, projection.SubjectId));
                            exclusions[(scope, stableId)] = projection.Decision.Sequence;
                            break;
                        }
                    case GraphDecisionKind.Include:
                        {
                            var scope = DecisionPrivacyScope(command);
                            var stableId = NormalizePrivacyScopeId(
                                connection,
                                transaction,
                                new GraphPrivacyScope(scope, projection.SubjectId));
                            exclusions.Remove((scope, stableId));
                            break;
                        }
                }

                after = projection.Decision.Sequence;
                if (nodes.Count > GraphLimits.MaximumComponentNodes ||
                    edges.Count > GraphLimits.MaximumComponentEdges ||
                    aliases.Count > GraphLimits.MaximumComponentNodes * GraphLimits.MaximumAliasesPerNode)
                {
                    throw new InvalidDataException("The graph-native decision overlay exceeds its stable safety ceiling.");
                }
            }
        }

        foreach (var merge in mergedInto.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var resolved = ResolveMergedEntity(mergedInto, merge.Key);
            if (!string.Equals(merge.Key, resolved, StringComparison.Ordinal))
            {
                suppressions[("node", merge.Key)] = snapshot.Sequence;
            }
        }

        var mergedAliases = aliases.Values
            .Select(alias => alias with { NodeId = ResolveMergedEntity(mergedInto, alias.NodeId) })
            .GroupBy(alias => (alias.NodeId, alias.NormalizedLabel))
            .Select(group => group.OrderBy(alias => alias.Id, StringComparer.Ordinal).First())
            .ToDictionary(alias => alias.Id, StringComparer.Ordinal);
        aliases.Clear();
        foreach (var alias in mergedAliases)
        {
            aliases.Add(alias.Key, alias.Value);
        }

        var mergedEdges = new Dictionary<string, GraphEdge>(StringComparer.Ordinal);
        foreach (var edge in edges.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var source = ResolveMergedEntity(mergedInto, edge.SourceNodeId);
            var target = ResolveMergedEntity(mergedInto, edge.TargetNodeId);
            if (!string.Equals(source, target, StringComparison.Ordinal))
            {
                mergedEdges[edge.Id] = edge with { SourceNodeId = source, TargetNodeId = target };
            }
        }

        edges.Clear();
        foreach (var edge in mergedEdges)
        {
            edges.Add(edge.Key, edge.Value);
        }

        ExecuteNonQuery(connection, transaction, "DELETE FROM graph_decision_suppressions;");
        foreach (var item in suppressions.OrderBy(item => item.Key.Kind, StringComparer.Ordinal).ThenBy(item => item.Key.Id, StringComparer.Ordinal))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO graph_decision_suppressions(suppression_kind, stable_id, checkpoint_id, decision_sequence) VALUES ($kind, $id, $checkpoint, $sequence);",
                ("$kind", item.Key.Kind), ("$id", item.Key.Id), ("$checkpoint", snapshot.CheckpointId), ("$sequence", item.Value));
        }

        ExecuteNonQuery(connection, transaction, "DELETE FROM graph_privacy_exclusions;");
        foreach (var item in exclusions
                     .OrderBy(item => item.Key.Kind)
                     .ThenBy(item => item.Key.Id, StringComparer.Ordinal))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO graph_privacy_exclusions(scope_kind, stable_id, authority_sequence, observed_utc_ticks) VALUES ($kind, $id, $sequence, $now);",
                ("$kind", State(item.Key.Kind)), ("$id", item.Key.Id),
                ("$sequence", item.Value), ("$now", nowUtc.UtcTicks));
        }

        MarkDecisionOverlayMissingReferences(connection, transaction, nodes, edges);
        var generation = Convert.ToInt64(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COALESCE(MAX(generation), 0) + 1 FROM graph_generations WHERE component_key = $component;",
                ("$component", componentKey)),
            CultureInfo.InvariantCulture);
        var sourceManifest = string.Concat("decision:", snapshot.CheckpointId);
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO graph_generations(component_key, generation, state, source_manifest_id, decision_sequence, created_utc_ticks, node_count, edge_count, evidence_count, alias_count, mention_count, fact_count, canonical_hash) VALUES ($component, $generation, 'Staging', $manifest, $sequence, $now, $nodes, $edges, 0, $aliases, 0, 0, $hash);",
            ("$component", componentKey), ("$generation", generation), ("$manifest", sourceManifest),
            ("$sequence", snapshot.Sequence), ("$now", nowUtc.UtcTicks), ("$nodes", nodes.Count),
            ("$edges", edges.Count), ("$aliases", aliases.Count), ("$hash", snapshot.CanonicalHash));

        foreach (var node in nodes.Values.OrderBy(item => item.Identity.NodeId, StringComparer.Ordinal))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO graph_nodes(
                    component_key, generation, node_id, node_type, source_entity_id, source_id,
                    identity_scope, canonical_key, normalization_version, canonical_inputs,
                    display_label, normalized_label, origin, source_manifest_id, observation_hash,
                    algorithm_name, algorithm_version, created_utc_ticks, last_validated_utc_ticks,
                    freshness_state, integrity_state, is_visible)
                VALUES ($component, $generation, $node, $type, $entity, $source, $scope, $canonical,
                        $normalization, $inputs, $label, $normalized, $origin, $manifest, $observation,
                        $algorithm, $version, $created, $validated, $freshness, $integrity, $visible);
                """,
                ("$component", componentKey), ("$generation", generation), ("$node", node.Identity.NodeId),
                ("$type", node.Identity.Kind.Value), ("$entity", node.Identity.CanonicalKey),
                ("$source", node.OwningSourceId),
                ("$scope", node.Identity.Scope), ("$canonical", node.Identity.CanonicalKey),
                ("$normalization", node.Identity.NormalizationVersion), ("$inputs", node.Identity.CanonicalInputs),
                ("$label", node.DisplayLabel),
                ("$normalized", NormalizeLabel(node.DisplayLabel)), ("$origin", State(node.Origin)),
                ("$manifest", sourceManifest), ("$observation", node.ObservationHash),
                ("$algorithm", node.Algorithm), ("$version", node.AlgorithmVersion),
                ("$created", node.CreatedAtUtc.UtcTicks), ("$validated", nowUtc.UtcTicks),
                ("$freshness", State(GraphFreshnessState.Current)), ("$integrity", State(GraphIntegrityState.Valid)),
                ("$visible", node.IsVisible ? 1 : 0));
        }

        foreach (var edge in edges.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO graph_edges(component_key, generation, edge_id, source_node_id, target_node_id, edge_type, confidence, origin, algorithm_name, algorithm_version, input_fingerprint, created_utc_ticks, last_validated_utc_ticks, freshness_state, integrity_state, is_manual) VALUES ($component, $generation, $edge, $source, $target, $type, $confidence, $origin, $algorithm, $version, $fingerprint, $created, $validated, $freshness, $integrity, 1);",
                ("$component", componentKey), ("$generation", generation), ("$edge", edge.Id),
                ("$source", edge.SourceNodeId), ("$target", edge.TargetNodeId), ("$type", edge.Kind.Value),
                ("$confidence", State(edge.Confidence)), ("$origin", State(edge.Origin)),
                ("$algorithm", edge.Algorithm), ("$version", edge.AlgorithmVersion),
                ("$fingerprint", edge.InputFingerprint), ("$created", edge.CreatedAtUtc.UtcTicks),
                ("$validated", nowUtc.UtcTicks), ("$freshness", State(GraphFreshnessState.Current)),
                ("$integrity", State(GraphIntegrityState.Valid)));
        }

        foreach (var alias in aliases.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO graph_aliases(component_key, generation, alias_id, node_id, normalized_alias, display_alias, origin, decision_id, created_utc_ticks) VALUES ($component, $generation, $alias, $node, $normalized, $display, $origin, $decision, $created);",
                ("$component", componentKey), ("$generation", generation), ("$alias", alias.Id),
                ("$node", alias.NodeId), ("$normalized", alias.NormalizedLabel),
                ("$display", alias.Label), ("$origin", State(alias.Origin)),
                ("$decision", alias.DecisionId), ("$created", alias.CreatedAtUtc.UtcTicks));
        }

        ExecuteNonQuery(connection, transaction, "UPDATE graph_generations SET state = 'Published', validated_utc_ticks = $now, published_utc_ticks = $now WHERE component_key = $component AND generation = $generation;", ("$now", nowUtc.UtcTicks), ("$component", componentKey), ("$generation", generation));
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO graph_components(component_key, active_generation, source_manifest_id, decision_sequence,
                configuration_fingerprint, algorithm_name, algorithm_version, freshness_state, integrity_state, updated_utc_ticks)
            VALUES ($component, $generation, $manifest, $sequence, $hash, 'graph-native-decision-overlay', '1.0.0', $current, $valid, $now)
            ON CONFLICT(component_key) DO UPDATE SET active_generation = excluded.active_generation,
                source_manifest_id = excluded.source_manifest_id, decision_sequence = excluded.decision_sequence,
                configuration_fingerprint = excluded.configuration_fingerprint,
                freshness_state = excluded.freshness_state, integrity_state = excluded.integrity_state,
                updated_utc_ticks = excluded.updated_utc_ticks;
            """,
            ("$component", componentKey), ("$generation", generation), ("$manifest", sourceManifest),
            ("$sequence", snapshot.Sequence), ("$hash", snapshot.CanonicalHash),
            ("$current", State(GraphFreshnessState.Current)), ("$valid", State(GraphIntegrityState.Valid)),
            ("$now", nowUtc.UtcTicks));
    }

    private static void MarkDecisionOverlayMissingReferences(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyDictionary<string, GraphNode> nodes,
        IDictionary<string, GraphEdge> edges)
    {
        var required = edges.Values.SelectMany(item => new[] { item.SourceNodeId, item.TargetNodeId })
            .Distinct(StringComparer.Ordinal)
            .Where(id => !nodes.ContainsKey(id));
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in required)
        {
            var found = Convert.ToInt32(
                ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM graph_nodes n JOIN graph_components c ON c.component_key = n.component_key AND c.active_generation = n.generation WHERE n.node_id = $id AND n.is_visible = 1 AND c.freshness_state = $current AND c.integrity_state = $valid;",
                    ("$id", id), ("$current", State(GraphFreshnessState.Current)),
                    ("$valid", State(GraphIntegrityState.Valid))),
                CultureInfo.InvariantCulture);
            if (found == 0)
            {
                missing.Add(id);
            }
        }

        foreach (var edgeId in edges
                     .Where(item => missing.Contains(item.Value.SourceNodeId) || missing.Contains(item.Value.TargetNodeId))
                     .Select(item => item.Key)
                     .ToArray())
        {
            edges[edgeId] = edges[edgeId] with
            {
                Freshness = GraphFreshnessState.Stale,
                Integrity = GraphIntegrityState.RepairRequired,
            };
        }
    }

    private static string ResolveMergedEntity(IReadOnlyDictionary<string, string> mergedInto, string nodeId)
    {
        var current = nodeId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (mergedInto.TryGetValue(current, out var target))
        {
            if (!visited.Add(current))
            {
                throw new InvalidDataException("Graph-native merge decisions contain a cycle.");
            }

            current = target;
        }

        return current;
    }

    private static GraphPrivacyScopeKind DecisionPrivacyScope(GraphDecisionCommand command) =>
        command.Reason switch
        {
            { } reason when reason.EndsWith("-source", StringComparison.Ordinal) => GraphPrivacyScopeKind.Source,
            { } reason when reason.EndsWith("-file", StringComparison.Ordinal) => GraphPrivacyScopeKind.File,
            { } reason when reason.EndsWith("-collection", StringComparison.Ordinal) => GraphPrivacyScopeKind.Collection,
            { } reason when reason.EndsWith("-all", StringComparison.Ordinal) => GraphPrivacyScopeKind.All,
            _ => GraphPrivacyScopeKind.Node,
        };

    private static void RequireUnique(IEnumerable<string> values, string category)
    {
        var all = values.ToArray();
        if (all.Distinct(StringComparer.Ordinal).Count() != all.Length)
        {
            throw new InvalidDataException($"A graph component contains duplicate {category} identifiers.");
        }
    }

    private static string StableOrderKey(GraphProjectionObservation observation) =>
        $"{(int)observation.Kind:D2}|{observation.StableKey}";

    private static int ProjectionPriority(GraphProjectionObservationKind kind) => kind switch
    {
        GraphProjectionObservationKind.Source => 600,
        GraphProjectionObservationKind.LegacyDecision => 600,
        GraphProjectionObservationKind.File => 500,
        GraphProjectionObservationKind.Collection => 500,
        GraphProjectionObservationKind.Relationship => 400,
        GraphProjectionObservationKind.CollectionMembership => 400,
        GraphProjectionObservationKind.Deletion => 0,
        _ => 0,
    };

    private static (int Count, string Hash, bool IsLast)? ReadManifestPage(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string manifestId,
        long sequence)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT observation_count, canonical_page_hash, is_last_page FROM graph_manifest_pages WHERE manifest_id = $manifest AND page_sequence = $sequence;";
        AddParameters(command, ("$manifest", manifestId), ("$sequence", sequence));
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2)) : null;
    }

    private static IEnumerable<(string Kind, string StableKey, string RowHash)> ReadManifestRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string manifestId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT row_kind, stable_primary_key, canonical_row_hash FROM graph_manifest_rows WHERE manifest_id = $manifest ORDER BY row_kind, stable_primary_key;";
        command.Parameters.AddWithValue("$manifest", manifestId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }
    }

    private static string ReadJobManifest(SqliteConnection connection, SqliteTransaction transaction, string jobId) =>
        ExecuteScalar(connection, transaction, "SELECT source_manifest_id FROM graph_jobs WHERE job_id = $job;", ("$job", jobId)) as string
        ?? throw SqliteKnowledgeInfrastructure.Corrupt("A graph job has no source manifest.");

    private static string ReadJobComponent(SqliteConnection connection, SqliteTransaction transaction, string jobId) =>
        ExecuteScalar(connection, transaction, "SELECT component_key FROM graph_jobs WHERE job_id = $job;", ("$job", jobId)) as string
        ?? throw SqliteKnowledgeInfrastructure.Corrupt("A graph job has no durable component identity.");

    private static void EnsureRunEpoch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphProjectionRun run,
        DateTimeOffset nowUtc,
        bool requireRunning)
    {
        var state = requireRunning ? State(GraphRunControlState.Running) : null;
        var count = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM graph_runs r
                JOIN graph_coordinator_lease l ON l.singleton_id = 1
                WHERE r.run_id = $run AND r.coordinator_epoch = $epoch
                  AND r.owner_instance_id = l.owner_instance_id
                  AND l.fencing_epoch = $epoch AND l.expires_utc_ticks >= $now
                  AND ($state IS NULL OR r.control_state = $state);
                """,
                ("$run", run.RunId), ("$epoch", run.FencingEpoch), ("$now", nowUtc.UtcTicks), ("$state", state)),
            CultureInfo.InvariantCulture);
        if (count != 1)
        {
            throw new SqliteKnowledgeStoreException(SqliteKnowledgeFailureKind.Busy, "The graph projection run has a stale fencing epoch or is not running.");
        }
    }

    private static void ValidateStagedGeneration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string componentKey,
        long generation)
    {
        var invalidEdges = Convert.ToInt64(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM graph_edges WHERE component_key = $component AND generation = $generation AND source_node_id = target_node_id;",
                ("$component", componentKey), ("$generation", generation)),
            CultureInfo.InvariantCulture);
        var orphanEvidence = Convert.ToInt64(
            ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*) FROM graph_evidence e
                LEFT JOIN graph_edges g ON g.component_key = e.component_key AND g.generation = e.generation AND g.edge_id = e.edge_id
                WHERE e.component_key = $component AND e.generation = $generation AND g.edge_id IS NULL;
                """,
                ("$component", componentKey), ("$generation", generation)),
            CultureInfo.InvariantCulture);
        if (invalidEdges != 0 || orphanEvidence != 0)
        {
            throw new InvalidDataException("The staged graph generation failed integrity validation.");
        }
    }

    private static string NormalizeLabel(string value) => Bound(value.Trim().Normalize().ToUpperInvariant(), GraphLimits.MaximumLabelCharacters);
}
