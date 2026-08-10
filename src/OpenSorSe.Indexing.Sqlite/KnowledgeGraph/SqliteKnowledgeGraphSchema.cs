namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>Defines the isolated, rebuildable knowledge-graph schema.</summary>
internal static class SqliteKnowledgeGraphSchema
{
    internal const int ApplicationId = 1_329_806_155;
    internal const int Version = 1;

    internal static readonly IReadOnlySet<string> RequiredTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "graph_meta", "graph_migration_history", "graph_coordinator_lease", "graph_runs",
        "graph_jobs", "graph_job_attempts", "graph_manifests", "graph_manifest_pages", "graph_manifest_rows",
        "graph_observation_inbox", "graph_decision_projection_staging", "graph_decision_suppressions", "graph_watermarks", "graph_components", "graph_generations",
        "graph_nodes", "graph_edges", "graph_evidence", "graph_aliases", "graph_mentions", "graph_facts", "graph_quarantine",
        "graph_integrity_findings", "graph_repair_operations", "graph_privacy_exclusions", "graph_maintenance_history",
        "graph_diagnostics",
    };

    internal static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RequiredColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["graph_runs"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "run_id", "coordinator_epoch", "snapshot_manifest_id", "snapshot_revision",
                "legacy_decision_manifest_id", "privacy_sequence", "graph_decision_sequence",
                "graph_decision_checkpoint_id", "input_manifest_complete", "current_stage",
            },
            ["graph_jobs"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "job_id", "run_id", "stage", "stage_input_fingerprint", "execution_state",
                "claim_owner_instance_id", "claim_token", "claim_fencing_epoch", "claim_expires_utc_ticks",
            },
            ["graph_watermarks"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "source_id", "latest_complete_manifest_id", "latest_complete_revision",
                "applied_manifest_id", "applied_revision", "ingested_observation_sequence",
                "applied_observation_sequence", "ingested_decision_sequence",
                "ingested_decision_checkpoint_id", "ingested_decision_canonical_hash", "applied_decision_sequence",
                "applied_decision_checkpoint_id", "ingested_privacy_sequence", "applied_privacy_sequence",
            },
            ["graph_nodes"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "component_key", "generation", "node_id", "source_id", "canonical_key",
                "source_manifest_id", "freshness_state", "integrity_state", "is_visible",
            },
            ["graph_decision_projection_staging"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "checkpoint_id", "decision_sequence", "decision_id", "canonical_hash", "payload_json",
            },
        };

    internal static readonly IReadOnlySet<string> RequiredIndexes = new HashSet<string>(StringComparer.Ordinal)
    {
        "ix_graph_jobs_eligible", "ix_graph_job_attempts_claim", "ux_graph_manifests_active_source",
        "ix_graph_observation_inbox_state", "ix_graph_decision_projection_staging_checkpoint",
        "ix_graph_nodes_source_entity", "ix_graph_nodes_label", "ix_graph_edges_from", "ix_graph_edges_to",
        "ix_graph_evidence_edge", "ix_graph_aliases_lookup",
    };

    internal const string CreateVersionOne =
        """
        CREATE TABLE IF NOT EXISTS graph_meta (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS graph_migration_history (
            schema_version INTEGER PRIMARY KEY NOT NULL,
            migration_id TEXT NOT NULL UNIQUE,
            migration_checksum TEXT NOT NULL,
            started_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NOT NULL,
            application_version TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS graph_coordinator_lease (
            singleton_id INTEGER PRIMARY KEY NOT NULL CHECK (singleton_id = 1),
            owner_instance_id TEXT NOT NULL,
            fencing_epoch INTEGER NOT NULL CHECK (fencing_epoch > 0),
            lease_token TEXT NOT NULL,
            process_start_identity TEXT NOT NULL,
            acquired_utc_ticks INTEGER NOT NULL,
            heartbeat_utc_ticks INTEGER NOT NULL,
            expires_utc_ticks INTEGER NOT NULL,
            heartbeat_sequence INTEGER NOT NULL CHECK (heartbeat_sequence >= 0)
        );
        CREATE TABLE IF NOT EXISTS graph_runs (
            run_id TEXT PRIMARY KEY NOT NULL,
            control_state TEXT NOT NULL,
            control_sequence INTEGER NOT NULL DEFAULT 0 CHECK (control_sequence >= 0),
            freshness_state TEXT NOT NULL,
            integrity_state TEXT NOT NULL,
            reason TEXT NOT NULL CHECK (length(reason) <= 256),
            settings_fingerprint TEXT NOT NULL,
            coordinator_epoch INTEGER NOT NULL CHECK (coordinator_epoch > 0),
            owner_instance_id TEXT NOT NULL,
            snapshot_manifest_id TEXT NOT NULL,
            snapshot_revision INTEGER NOT NULL CHECK (snapshot_revision >= 0),
            legacy_decision_manifest_id TEXT NOT NULL,
            privacy_sequence INTEGER NOT NULL CHECK (privacy_sequence >= 0),
            graph_decision_sequence INTEGER NOT NULL DEFAULT 0 CHECK (graph_decision_sequence >= 0),
            graph_decision_checkpoint_id TEXT NOT NULL DEFAULT '',
            expected_observation_count INTEGER NOT NULL CHECK (expected_observation_count >= 0),
            expected_manifest_hash TEXT NOT NULL,
            input_manifest_complete INTEGER NOT NULL DEFAULT 0 CHECK (input_manifest_complete IN (0, 1)),
            current_stage TEXT NULL,
            current_work_label TEXT NULL CHECK (current_work_label IS NULL OR length(current_work_label) <= 256),
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NULL,
            cancellation_reason TEXT NULL CHECK (cancellation_reason IS NULL OR length(cancellation_reason) <= 512)
        );
        CREATE INDEX IF NOT EXISTS ix_graph_runs_state ON graph_runs(control_state, updated_utc_ticks, run_id);
        CREATE TABLE IF NOT EXISTS graph_jobs (
            job_id TEXT PRIMARY KEY NOT NULL,
            logical_key TEXT NOT NULL UNIQUE,
            run_id TEXT NOT NULL,
            component_key TEXT NOT NULL,
            stage TEXT NOT NULL,
            stage_input_fingerprint TEXT NULL,
            execution_state TEXT NOT NULL,
            freshness_state TEXT NOT NULL,
            integrity_state TEXT NOT NULL,
            priority INTEGER NOT NULL DEFAULT 0,
            current_attempt INTEGER NOT NULL DEFAULT 0 CHECK (current_attempt >= 0),
            maximum_attempts INTEGER NOT NULL CHECK (maximum_attempts > 0),
            next_eligible_utc_ticks INTEGER NULL,
            waiting_reason TEXT NULL,
            failure_category TEXT NULL,
            source_manifest_id TEXT NULL,
            source_row_hash TEXT NULL,
            decision_sequence INTEGER NOT NULL DEFAULT 0 CHECK (decision_sequence >= 0),
            configuration_fingerprint TEXT NOT NULL,
            algorithm_name TEXT NOT NULL,
            algorithm_version TEXT NOT NULL,
            rebuild_generation INTEGER NOT NULL CHECK (rebuild_generation > 0),
            observation_sequence INTEGER NOT NULL CHECK (observation_sequence > 0),
            observation_kind TEXT NOT NULL,
            observation_stable_key TEXT NOT NULL,
            claim_owner_instance_id TEXT NULL,
            claim_token TEXT NULL,
            claim_fencing_epoch INTEGER NULL,
            claim_heartbeat_utc_ticks INTEGER NULL,
            claim_expires_utc_ticks INTEGER NULL,
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY (run_id) REFERENCES graph_runs(run_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_graph_jobs_eligible ON graph_jobs(execution_state, next_eligible_utc_ticks, priority DESC, created_utc_ticks, job_id);
        CREATE INDEX IF NOT EXISTS ix_graph_jobs_component ON graph_jobs(component_key, rebuild_generation, stage);
        CREATE TABLE IF NOT EXISTS graph_job_attempts (
            attempt_id TEXT PRIMARY KEY NOT NULL,
            job_id TEXT NOT NULL,
            attempt_number INTEGER NOT NULL CHECK (attempt_number > 0),
            owner_instance_id TEXT NOT NULL,
            claim_token TEXT NOT NULL,
            fencing_epoch INTEGER NOT NULL CHECK (fencing_epoch > 0),
            heartbeat_sequence INTEGER NOT NULL DEFAULT 0 CHECK (heartbeat_sequence >= 0),
            started_utc_ticks INTEGER NOT NULL,
            heartbeat_utc_ticks INTEGER NOT NULL,
            expires_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NULL,
            outcome TEXT NULL,
            failure_category TEXT NULL,
            recovery_count INTEGER NOT NULL DEFAULT 0 CHECK (recovery_count >= 0),
            FOREIGN KEY (job_id) REFERENCES graph_jobs(job_id) ON DELETE CASCADE,
            UNIQUE (job_id, attempt_number)
        );
        CREATE INDEX IF NOT EXISTS ix_graph_job_attempts_claim ON graph_job_attempts(owner_instance_id, claim_token, fencing_epoch);
        CREATE TABLE IF NOT EXISTS graph_manifests (
            manifest_id TEXT PRIMARY KEY NOT NULL,
            source_id TEXT NOT NULL,
            scope TEXT NOT NULL,
            state TEXT NOT NULL,
            source_schema_version INTEGER NOT NULL,
            terminal_row_count INTEGER NULL CHECK (terminal_row_count IS NULL OR terminal_row_count >= 0),
            canonical_aggregate_hash TEXT NULL,
            started_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NULL,
            is_active INTEGER NOT NULL DEFAULT 0 CHECK (is_active IN (0, 1))
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_graph_manifests_active_source ON graph_manifests(source_id, scope) WHERE is_active = 1;
        CREATE TABLE IF NOT EXISTS graph_manifest_pages (
            manifest_id TEXT NOT NULL,
            page_sequence INTEGER NOT NULL CHECK (page_sequence >= 0),
            observation_count INTEGER NOT NULL CHECK (observation_count >= 0),
            canonical_page_hash TEXT NOT NULL,
            first_stable_key TEXT NULL,
            last_stable_key TEXT NULL,
            is_last_page INTEGER NOT NULL CHECK (is_last_page IN (0, 1)),
            PRIMARY KEY (manifest_id, page_sequence),
            FOREIGN KEY (manifest_id) REFERENCES graph_manifests(manifest_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS graph_manifest_rows (
            manifest_id TEXT NOT NULL,
            row_kind TEXT NOT NULL,
            stable_primary_key TEXT NOT NULL,
            canonical_row_hash TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (length(payload_json) <= 262144),
            PRIMARY KEY (manifest_id, row_kind, stable_primary_key),
            FOREIGN KEY (manifest_id) REFERENCES graph_manifests(manifest_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS graph_observation_inbox (
            observation_sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            manifest_id TEXT NOT NULL,
            source_id TEXT NOT NULL,
            row_kind TEXT NOT NULL,
            stable_primary_key TEXT NOT NULL,
            canonical_row_hash TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (length(payload_json) <= 262144),
            state TEXT NOT NULL,
            enqueued_utc_ticks INTEGER NOT NULL,
            applied_utc_ticks INTEGER NULL,
            FOREIGN KEY (manifest_id) REFERENCES graph_manifests(manifest_id) ON DELETE CASCADE,
            UNIQUE (manifest_id, row_kind, stable_primary_key)
        );
        CREATE INDEX IF NOT EXISTS ix_graph_observation_inbox_state ON graph_observation_inbox(state, source_id, observation_sequence);
        CREATE TABLE IF NOT EXISTS graph_decision_projection_staging (
            checkpoint_id TEXT NOT NULL,
            decision_sequence INTEGER NOT NULL CHECK (decision_sequence > 0),
            decision_id TEXT NOT NULL,
            canonical_hash TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (length(payload_json) <= 65536),
            staged_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY (checkpoint_id, decision_sequence),
            UNIQUE (checkpoint_id, decision_id)
        );
        CREATE INDEX IF NOT EXISTS ix_graph_decision_projection_staging_checkpoint
            ON graph_decision_projection_staging(checkpoint_id, decision_sequence);
        CREATE TABLE IF NOT EXISTS graph_decision_suppressions (
            suppression_kind TEXT NOT NULL,
            stable_id TEXT NOT NULL CHECK (length(stable_id) <= 256),
            checkpoint_id TEXT NOT NULL,
            decision_sequence INTEGER NOT NULL CHECK (decision_sequence > 0),
            PRIMARY KEY (suppression_kind, stable_id)
        );
        CREATE TABLE IF NOT EXISTS graph_watermarks (
            source_id TEXT PRIMARY KEY NOT NULL,
            latest_complete_manifest_id TEXT NULL,
            latest_complete_revision INTEGER NOT NULL DEFAULT 0 CHECK (latest_complete_revision >= 0),
            applied_manifest_id TEXT NULL,
            applied_revision INTEGER NOT NULL DEFAULT 0 CHECK (applied_revision >= 0),
            ingestion_manifest_id TEXT NULL,
            ingestion_page_number INTEGER NOT NULL DEFAULT 0 CHECK (ingestion_page_number >= 0),
            ingestion_stable_key TEXT NULL,
            ingested_observation_sequence INTEGER NOT NULL DEFAULT 0 CHECK (ingested_observation_sequence >= 0),
            applied_observation_sequence INTEGER NOT NULL DEFAULT 0 CHECK (applied_observation_sequence >= 0),
            ingested_decision_sequence INTEGER NOT NULL DEFAULT 0 CHECK (ingested_decision_sequence >= 0),
            ingested_decision_checkpoint_id TEXT NULL,
            ingested_decision_canonical_hash TEXT NULL,
            applied_decision_sequence INTEGER NOT NULL DEFAULT 0 CHECK (applied_decision_sequence >= 0),
            applied_decision_checkpoint_id TEXT NULL,
            ingested_privacy_sequence INTEGER NOT NULL DEFAULT 0 CHECK (ingested_privacy_sequence >= 0),
            applied_privacy_sequence INTEGER NOT NULL DEFAULT 0 CHECK (applied_privacy_sequence >= 0),
            updated_utc_ticks INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS graph_components (
            component_key TEXT PRIMARY KEY NOT NULL,
            active_generation INTEGER NULL,
            source_manifest_id TEXT NULL,
            decision_sequence INTEGER NOT NULL DEFAULT 0 CHECK (decision_sequence >= 0),
            configuration_fingerprint TEXT NOT NULL,
            algorithm_name TEXT NOT NULL,
            algorithm_version TEXT NOT NULL,
            freshness_state TEXT NOT NULL,
            integrity_state TEXT NOT NULL,
            updated_utc_ticks INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS graph_generations (
            component_key TEXT NOT NULL,
            generation INTEGER NOT NULL CHECK (generation > 0),
            state TEXT NOT NULL,
            source_manifest_id TEXT NULL,
            decision_sequence INTEGER NOT NULL CHECK (decision_sequence >= 0),
            created_utc_ticks INTEGER NOT NULL,
            validated_utc_ticks INTEGER NULL,
            published_utc_ticks INTEGER NULL,
            node_count INTEGER NOT NULL DEFAULT 0 CHECK (node_count >= 0),
            edge_count INTEGER NOT NULL DEFAULT 0 CHECK (edge_count >= 0),
            evidence_count INTEGER NOT NULL DEFAULT 0 CHECK (evidence_count >= 0),
            alias_count INTEGER NOT NULL DEFAULT 0 CHECK (alias_count >= 0),
            mention_count INTEGER NOT NULL DEFAULT 0 CHECK (mention_count >= 0),
            fact_count INTEGER NOT NULL DEFAULT 0 CHECK (fact_count >= 0),
            canonical_hash TEXT NULL,
            PRIMARY KEY (component_key, generation)
        );
        CREATE TABLE IF NOT EXISTS graph_nodes (
            component_key TEXT NOT NULL,
            generation INTEGER NOT NULL,
            node_id TEXT NOT NULL,
            node_type TEXT NOT NULL,
            source_entity_id TEXT NOT NULL,
            source_id TEXT NULL,
            identity_scope TEXT NOT NULL CHECK (length(identity_scope) <= 256),
            canonical_key TEXT NOT NULL CHECK (length(canonical_key) <= 1024),
            normalization_version TEXT NOT NULL CHECK (length(normalization_version) <= 64),
            canonical_inputs TEXT NOT NULL CHECK (length(canonical_inputs) <= 4096),
            display_label TEXT NOT NULL CHECK (length(display_label) <= 256),
            normalized_label TEXT NOT NULL CHECK (length(normalized_label) <= 256),
            origin TEXT NOT NULL,
            source_manifest_id TEXT NOT NULL,
            observation_hash TEXT NOT NULL,
            algorithm_name TEXT NOT NULL,
            algorithm_version TEXT NOT NULL,
            created_utc_ticks INTEGER NOT NULL,
            last_validated_utc_ticks INTEGER NOT NULL,
            freshness_state TEXT NOT NULL,
            integrity_state TEXT NOT NULL,
            is_visible INTEGER NOT NULL DEFAULT 1 CHECK (is_visible IN (0, 1)),
            PRIMARY KEY (component_key, generation, node_id),
            FOREIGN KEY (component_key, generation) REFERENCES graph_generations(component_key, generation) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_graph_nodes_source_entity ON graph_nodes(node_type, source_id, source_entity_id, component_key, generation);
        CREATE INDEX IF NOT EXISTS ix_graph_nodes_label ON graph_nodes(normalized_label, component_key, generation);
        CREATE TABLE IF NOT EXISTS graph_edges (
            component_key TEXT NOT NULL,
            generation INTEGER NOT NULL,
            edge_id TEXT NOT NULL,
            source_node_id TEXT NOT NULL,
            target_node_id TEXT NOT NULL,
            edge_type TEXT NOT NULL,
            confidence TEXT NOT NULL,
            origin TEXT NOT NULL,
            algorithm_name TEXT NOT NULL,
            algorithm_version TEXT NOT NULL,
            input_fingerprint TEXT NOT NULL,
            created_utc_ticks INTEGER NOT NULL,
            last_validated_utc_ticks INTEGER NOT NULL,
            freshness_state TEXT NOT NULL,
            integrity_state TEXT NOT NULL,
            is_manual INTEGER NOT NULL DEFAULT 0 CHECK (is_manual IN (0, 1)),
            PRIMARY KEY (component_key, generation, edge_id),
            FOREIGN KEY (component_key, generation) REFERENCES graph_generations(component_key, generation) ON DELETE CASCADE,
            CHECK (source_node_id <> target_node_id)
        );
        CREATE INDEX IF NOT EXISTS ix_graph_edges_from ON graph_edges(source_node_id, edge_type, component_key, generation, target_node_id);
        CREATE INDEX IF NOT EXISTS ix_graph_edges_to ON graph_edges(target_node_id, edge_type, component_key, generation, source_node_id);
        CREATE TABLE IF NOT EXISTS graph_evidence (
            component_key TEXT NOT NULL,
            generation INTEGER NOT NULL,
            evidence_id TEXT NOT NULL,
            edge_id TEXT NOT NULL,
            evidence_kind TEXT NOT NULL,
            source_evidence_key TEXT NOT NULL CHECK (length(source_evidence_key) <= 256),
            explanation_template_code TEXT NOT NULL CHECK (length(explanation_template_code) <= 128),
            explanation TEXT NOT NULL CHECK (length(explanation) <= 256),
            source_manifest_id TEXT NOT NULL,
            observation_hash TEXT NOT NULL,
            PRIMARY KEY (component_key, generation, evidence_id),
            FOREIGN KEY (component_key, generation, edge_id) REFERENCES graph_edges(component_key, generation, edge_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_graph_evidence_edge ON graph_evidence(component_key, generation, edge_id, evidence_id);
        CREATE TABLE IF NOT EXISTS graph_aliases (
            component_key TEXT NOT NULL,
            generation INTEGER NOT NULL,
            alias_id TEXT NOT NULL,
            node_id TEXT NOT NULL,
            normalized_alias TEXT NOT NULL CHECK (length(normalized_alias) <= 256),
            display_alias TEXT NOT NULL CHECK (length(display_alias) <= 256),
            origin TEXT NOT NULL,
            decision_id TEXT NULL,
            created_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY (component_key, generation, alias_id),
            FOREIGN KEY (component_key, generation)
                REFERENCES graph_generations(component_key, generation) ON DELETE CASCADE,
            UNIQUE (component_key, generation, node_id, normalized_alias)
        );
        CREATE INDEX IF NOT EXISTS ix_graph_aliases_lookup ON graph_aliases(normalized_alias, component_key, generation, node_id);
        CREATE TABLE IF NOT EXISTS graph_mentions (
            component_key TEXT NOT NULL,
            generation INTEGER NOT NULL,
            mention_id TEXT NOT NULL,
            suggestion_kind TEXT NOT NULL,
            source_stable_key TEXT NOT NULL CHECK (length(source_stable_key) <= 256),
            identity_scope TEXT NOT NULL CHECK (length(identity_scope) <= 256),
            bounded_label TEXT NOT NULL CHECK (length(bounded_label) <= 256),
            normalized_key TEXT NOT NULL CHECK (length(normalized_key) <= 256),
            extractor_version TEXT NOT NULL CHECK (length(extractor_version) <= 64),
            evidence_ids_json TEXT NOT NULL CHECK (length(evidence_ids_json) <= 4096),
            is_confirmed INTEGER NOT NULL DEFAULT 0 CHECK (is_confirmed IN (0, 1)),
            PRIMARY KEY (component_key, generation, mention_id),
            FOREIGN KEY (component_key, generation)
                REFERENCES graph_generations(component_key, generation) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_graph_mentions_key ON graph_mentions(normalized_key, identity_scope, component_key, generation, mention_id);
        CREATE INDEX IF NOT EXISTS ix_graph_mentions_source ON graph_mentions(source_stable_key, component_key, generation, mention_id);
        CREATE TABLE IF NOT EXISTS graph_facts (
            component_key TEXT NOT NULL,
            generation INTEGER NOT NULL,
            fact_id TEXT NOT NULL,
            node_id TEXT NOT NULL,
            fact_kind TEXT NOT NULL CHECK (length(fact_kind) <= 128),
            canonical_value TEXT NOT NULL CHECK (length(canonical_value) <= 256),
            evidence_ids_json TEXT NOT NULL CHECK (length(evidence_ids_json) <= 4096),
            algorithm_version TEXT NOT NULL CHECK (length(algorithm_version) <= 64),
            PRIMARY KEY (component_key, generation, fact_id),
            FOREIGN KEY (component_key, generation, node_id)
                REFERENCES graph_nodes(component_key, generation, node_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_graph_facts_node ON graph_facts(node_id, fact_kind, component_key, generation, fact_id);
        CREATE TABLE IF NOT EXISTS graph_quarantine (
            quarantine_id TEXT PRIMARY KEY NOT NULL,
            record_kind TEXT NOT NULL,
            stable_key TEXT NOT NULL,
            failure_category TEXT NOT NULL,
            bounded_detail TEXT NULL CHECK (bounded_detail IS NULL OR length(bounded_detail) <= 1024),
            payload_hash TEXT NULL,
            created_utc_ticks INTEGER NOT NULL,
            resolved_utc_ticks INTEGER NULL
        );
        CREATE TABLE IF NOT EXISTS graph_integrity_findings (
            finding_id TEXT PRIMARY KEY NOT NULL,
            severity TEXT NOT NULL,
            category TEXT NOT NULL,
            component_key TEXT NULL,
            stable_key TEXT NULL,
            bounded_detail TEXT NOT NULL CHECK (length(bounded_detail) <= 1024),
            detected_utc_ticks INTEGER NOT NULL,
            repaired_utc_ticks INTEGER NULL
        );
        CREATE TABLE IF NOT EXISTS graph_repair_operations (
            repair_id TEXT PRIMARY KEY NOT NULL,
            scope TEXT NOT NULL,
            target_key TEXT NULL,
            state TEXT NOT NULL,
            started_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NULL,
            records_examined INTEGER NOT NULL DEFAULT 0 CHECK (records_examined >= 0),
            records_repaired INTEGER NOT NULL DEFAULT 0 CHECK (records_repaired >= 0),
            bounded_detail TEXT NULL CHECK (bounded_detail IS NULL OR length(bounded_detail) <= 1024)
        );
        CREATE TABLE IF NOT EXISTS graph_privacy_exclusions (
            scope_kind TEXT NOT NULL,
            stable_id TEXT NOT NULL CHECK (length(stable_id) <= 256),
            authority_sequence INTEGER NOT NULL DEFAULT 0 CHECK (authority_sequence >= 0),
            observed_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY (scope_kind, stable_id)
        );
        CREATE TABLE IF NOT EXISTS graph_maintenance_history (
            maintenance_id TEXT PRIMARY KEY NOT NULL,
            operation TEXT NOT NULL,
            owner_instance_id TEXT NOT NULL,
            fencing_epoch INTEGER NOT NULL CHECK (fencing_epoch > 0),
            state TEXT NOT NULL,
            started_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NULL,
            records_affected INTEGER NOT NULL DEFAULT 0 CHECK (records_affected >= 0),
            bytes_before INTEGER NULL CHECK (bytes_before IS NULL OR bytes_before >= 0),
            bytes_after INTEGER NULL CHECK (bytes_after IS NULL OR bytes_after >= 0)
        );
        CREATE TABLE IF NOT EXISTS graph_diagnostics (
            sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            run_id TEXT NULL,
            category TEXT NOT NULL,
            operation TEXT NOT NULL,
            outcome TEXT NOT NULL,
            bounded_detail TEXT NULL CHECK (bounded_detail IS NULL OR length(bounded_detail) <= 1024),
            duration_milliseconds INTEGER NULL CHECK (duration_milliseconds IS NULL OR duration_milliseconds >= 0),
            queue_length INTEGER NULL CHECK (queue_length IS NULL OR queue_length >= 0),
            created_utc_ticks INTEGER NOT NULL
        );
        INSERT OR IGNORE INTO graph_meta(key, value) VALUES ('enabled', '0');
        INSERT OR IGNORE INTO graph_meta(key, value) VALUES ('maximum_database_bytes', '536870912');
        INSERT OR IGNORE INTO graph_meta(key, value) VALUES ('quota_blocked', '0');
        """;
}
