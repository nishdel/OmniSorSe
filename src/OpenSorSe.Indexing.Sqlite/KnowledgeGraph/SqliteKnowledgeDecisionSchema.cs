namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>Defines the isolated, non-rebuildable knowledge-decision schema.</summary>
internal static class SqliteKnowledgeDecisionSchema
{
    internal const int ApplicationId = 1_329_805_899;
    internal const int Version = 1;

    internal static readonly IReadOnlySet<string> RequiredTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "decision_meta",
        "decision_migration_history",
        "graph_settings",
        "graph_native_decisions",
        "graph_manual_entities",
        "graph_entity_aliases",
        "legacy_relationship_decision_mirror",
        "decision_recovery_state",
        "decision_operation_sagas",
        "decision_backup_catalog",
        "decision_diagnostics",
    };

    internal static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RequiredColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["graph_settings"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "singleton_id", "settings_version", "settings_json", "settings_fingerprint",
                "updated_utc_ticks", "decision_sequence",
            },
            ["graph_native_decisions"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "decision_sequence", "decision_id", "idempotency_key", "decision_type",
                "target_kind", "target_key", "payload_json", "created_utc_ticks",
            },
            ["decision_recovery_state"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "current_decision_sequence", "current_privacy_sequence",
                "minimum_restorable_privacy_sequence", "active_store_generation",
                "decision_checkpoint_hash", "updated_utc_ticks",
            },
            ["decision_backup_catalog"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "backup_id", "store_generation", "backup_class", "state", "relative_path",
                "sha256", "privacy_sequence", "is_pinned",
            },
        };

    internal static readonly IReadOnlySet<string> RequiredIndexes = new HashSet<string>(StringComparer.Ordinal)
    {
        "ix_graph_native_decisions_target", "ix_legacy_decision_mirror_manifest",
        "ix_decision_operation_sagas_state", "ix_decision_backup_catalog_state",
    };

    internal const string CreateVersionOne =
        """
        CREATE TABLE IF NOT EXISTS decision_meta (
            key TEXT PRIMARY KEY NOT NULL,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS decision_migration_history (
            schema_version INTEGER PRIMARY KEY NOT NULL,
            migration_id TEXT NOT NULL UNIQUE,
            migration_checksum TEXT NOT NULL,
            started_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NOT NULL,
            application_version TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS graph_settings (
            singleton_id INTEGER PRIMARY KEY NOT NULL CHECK (singleton_id = 1),
            settings_version INTEGER NOT NULL CHECK (settings_version > 0),
            settings_json TEXT NOT NULL CHECK (length(settings_json) <= 65536),
            settings_fingerprint TEXT NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            decision_sequence INTEGER NOT NULL DEFAULT 0 CHECK (decision_sequence >= 0)
        );

        CREATE TABLE IF NOT EXISTS graph_native_decisions (
            decision_sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            decision_id TEXT NOT NULL UNIQUE,
            idempotency_key TEXT NOT NULL UNIQUE,
            decision_type TEXT NOT NULL,
            target_kind TEXT NOT NULL,
            target_key TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (length(payload_json) <= 65536),
            supersedes_decision_id TEXT NULL,
            is_tombstone INTEGER NOT NULL DEFAULT 0 CHECK (is_tombstone IN (0, 1)),
            created_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY (supersedes_decision_id) REFERENCES graph_native_decisions(decision_id)
        );

        CREATE INDEX IF NOT EXISTS ix_graph_native_decisions_target
            ON graph_native_decisions(target_kind, target_key, decision_sequence);

        CREATE TABLE IF NOT EXISTS graph_manual_entities (
            entity_id TEXT PRIMARY KEY NOT NULL,
            entity_type TEXT NOT NULL,
            display_name TEXT NOT NULL CHECK (length(display_name) <= 256),
            created_by_decision_id TEXT NOT NULL,
            last_decision_sequence INTEGER NOT NULL CHECK (last_decision_sequence > 0),
            is_deleted INTEGER NOT NULL DEFAULT 0 CHECK (is_deleted IN (0, 1)),
            updated_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY (created_by_decision_id) REFERENCES graph_native_decisions(decision_id)
        );

        CREATE TABLE IF NOT EXISTS graph_entity_aliases (
            alias_id TEXT PRIMARY KEY NOT NULL,
            entity_id TEXT NOT NULL,
            normalized_alias TEXT NOT NULL CHECK (length(normalized_alias) <= 256),
            display_alias TEXT NOT NULL CHECK (length(display_alias) <= 256),
            created_by_decision_id TEXT NOT NULL,
            last_decision_sequence INTEGER NOT NULL CHECK (last_decision_sequence > 0),
            is_deleted INTEGER NOT NULL DEFAULT 0 CHECK (is_deleted IN (0, 1)),
            updated_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY (entity_id) REFERENCES graph_manual_entities(entity_id) ON DELETE CASCADE,
            FOREIGN KEY (created_by_decision_id) REFERENCES graph_native_decisions(decision_id),
            UNIQUE (entity_id, normalized_alias)
        );

        CREATE TABLE IF NOT EXISTS legacy_relationship_decision_mirror (
            legacy_kind TEXT NOT NULL,
            legacy_key TEXT NOT NULL,
            manifest_id TEXT NOT NULL,
            canonical_row_hash TEXT NOT NULL,
            payload_json TEXT NULL CHECK (payload_json IS NULL OR length(payload_json) <= 65536),
            is_present INTEGER NOT NULL CHECK (is_present IN (0, 1)),
            observed_sequence INTEGER NOT NULL CHECK (observed_sequence >= 0),
            observed_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY (legacy_kind, legacy_key)
        );

        CREATE INDEX IF NOT EXISTS ix_legacy_decision_mirror_manifest
            ON legacy_relationship_decision_mirror(manifest_id, legacy_kind, legacy_key);

        CREATE TABLE IF NOT EXISTS decision_recovery_state (
            singleton_id INTEGER PRIMARY KEY NOT NULL CHECK (singleton_id = 1),
            current_decision_sequence INTEGER NOT NULL DEFAULT 0 CHECK (current_decision_sequence >= 0),
            current_privacy_sequence INTEGER NOT NULL DEFAULT 0 CHECK (current_privacy_sequence >= 0),
            minimum_restorable_privacy_sequence INTEGER NOT NULL DEFAULT 0 CHECK (minimum_restorable_privacy_sequence >= 0),
            active_store_generation INTEGER NOT NULL DEFAULT 1 CHECK (active_store_generation > 0),
            decision_checkpoint_hash TEXT NOT NULL,
            updated_utc_ticks INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS decision_operation_sagas (
            operation_id TEXT PRIMARY KEY NOT NULL,
            idempotency_key TEXT NOT NULL UNIQUE,
            operation_type TEXT NOT NULL,
            target_key TEXT NOT NULL,
            state TEXT NOT NULL,
            decision_sequence INTEGER NULL,
            payload_json TEXT NULL CHECK (payload_json IS NULL OR length(payload_json) <= 65536),
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            failure_category TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_decision_operation_sagas_state
            ON decision_operation_sagas(state, updated_utc_ticks, operation_id);

        CREATE TABLE IF NOT EXISTS decision_backup_catalog (
            backup_id TEXT PRIMARY KEY NOT NULL,
            store_generation INTEGER NOT NULL CHECK (store_generation > 0),
            reason TEXT NOT NULL CHECK (length(reason) <= 128),
            backup_class TEXT NOT NULL,
            state TEXT NOT NULL,
            relative_path TEXT NOT NULL,
            sha256 TEXT NULL,
            byte_length INTEGER NULL CHECK (byte_length IS NULL OR byte_length >= 0),
            schema_version INTEGER NOT NULL,
            minimum_decision_sequence INTEGER NOT NULL CHECK (minimum_decision_sequence >= 0),
            maximum_decision_sequence INTEGER NOT NULL CHECK (maximum_decision_sequence >= minimum_decision_sequence),
            privacy_sequence INTEGER NOT NULL CHECK (privacy_sequence >= 0),
            created_utc_ticks INTEGER NOT NULL,
            verified_utc_ticks INTEGER NULL,
            committed_utc_ticks INTEGER NULL,
            superseded_utc_ticks INTEGER NULL,
            is_pinned INTEGER NOT NULL DEFAULT 0 CHECK (is_pinned IN (0, 1))
        );

        CREATE INDEX IF NOT EXISTS ix_decision_backup_catalog_state
            ON decision_backup_catalog(state, is_pinned DESC, committed_utc_ticks DESC, backup_id);

        CREATE TABLE IF NOT EXISTS decision_diagnostics (
            sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            category TEXT NOT NULL,
            operation TEXT NOT NULL,
            outcome TEXT NOT NULL,
            bounded_detail TEXT NULL CHECK (bounded_detail IS NULL OR length(bounded_detail) <= 1024),
            duration_milliseconds INTEGER NULL CHECK (duration_milliseconds IS NULL OR duration_milliseconds >= 0),
            created_utc_ticks INTEGER NOT NULL
        );

        INSERT OR IGNORE INTO decision_recovery_state(
            singleton_id,
            current_decision_sequence,
            current_privacy_sequence,
            minimum_restorable_privacy_sequence,
            active_store_generation,
            decision_checkpoint_hash,
            updated_utc_ticks)
        VALUES (1, 0, 0, 0, 1, 'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855', 0);
        """;

    internal const string LegacyMirrorExtensionName = "legacy-mirror-ingestion";
    internal const int LegacyMirrorExtensionVersion = 1;
    internal const string CreateLegacyMirrorExtension =
        """
        CREATE TABLE decision_extension_migrations (
            extension_name TEXT PRIMARY KEY NOT NULL,
            schema_version INTEGER NOT NULL CHECK (schema_version > 0),
            migration_id TEXT NOT NULL UNIQUE,
            migration_checksum TEXT NOT NULL,
            started_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NOT NULL,
            application_version TEXT NOT NULL
        );

        CREATE TABLE legacy_mirror_ingest_manifests (
            manifest_id TEXT PRIMARY KEY NOT NULL,
            expected_count INTEGER NOT NULL CHECK (expected_count >= 0 AND expected_count <= 100000),
            staged_count INTEGER NOT NULL DEFAULT 0 CHECK (staged_count >= 0 AND staged_count <= expected_count),
            next_page_sequence INTEGER NOT NULL DEFAULT 0 CHECK (next_page_sequence >= 0),
            state TEXT NOT NULL CHECK (state IN ('Capturing', 'Complete')),
            canonical_aggregate_hash TEXT NULL,
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NULL
        );

        CREATE TABLE legacy_mirror_ingest_pages (
            manifest_id TEXT NOT NULL,
            page_sequence INTEGER NOT NULL CHECK (page_sequence >= 0),
            canonical_page_hash TEXT NOT NULL,
            row_count INTEGER NOT NULL CHECK (row_count > 0 AND row_count <= 256),
            created_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY (manifest_id, page_sequence),
            FOREIGN KEY (manifest_id) REFERENCES legacy_mirror_ingest_manifests(manifest_id) ON DELETE CASCADE
        );

        CREATE TABLE legacy_mirror_ingest_rows (
            manifest_id TEXT NOT NULL,
            page_sequence INTEGER NOT NULL,
            stable_key TEXT NOT NULL,
            legacy_kind TEXT NOT NULL,
            legacy_key TEXT NOT NULL,
            canonical_row_hash TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (length(payload_json) <= 65536),
            is_present INTEGER NOT NULL CHECK (is_present IN (0, 1)),
            observed_sequence INTEGER NOT NULL CHECK (observed_sequence >= 0),
            observed_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY (manifest_id, legacy_kind, legacy_key),
            UNIQUE (manifest_id, stable_key),
            FOREIGN KEY (manifest_id, page_sequence)
                REFERENCES legacy_mirror_ingest_pages(manifest_id, page_sequence) ON DELETE CASCADE
        );

        CREATE INDEX ix_legacy_mirror_ingest_rows_page
            ON legacy_mirror_ingest_rows(manifest_id, page_sequence, stable_key);
        """;
}
