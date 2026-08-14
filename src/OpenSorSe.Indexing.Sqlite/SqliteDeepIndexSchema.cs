namespace OpenSorSe.Indexing.Sqlite;

internal static class SqliteDeepIndexSchema
{
    public const string CreateVersionOne = """
        CREATE TABLE IF NOT EXISTS index_meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS index_sources (
            id TEXT PRIMARY KEY,
            root_path TEXT NOT NULL,
            root_path_key TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            indexing_level INTEGER NOT NULL,
            include_subfolders INTEGER NOT NULL,
            enabled INTEGER NOT NULL,
            priority INTEGER NOT NULL,
            exclusions_json TEXT NOT NULL,
            managed_by_watched_folders INTEGER NOT NULL DEFAULT 0,
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS index_runs (
            id TEXT PRIMARY KEY,
            source_id TEXT NOT NULL,
            status INTEGER NOT NULL,
            started_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER,
            discovery_complete INTEGER NOT NULL DEFAULT 0,
            total_discovered INTEGER NOT NULL DEFAULT 0,
            current_stage INTEGER,
            current_file_name TEXT,
            cancellation_reason TEXT,
            FOREIGN KEY(source_id) REFERENCES index_sources(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS index_files (
            id TEXT PRIMARY KEY,
            source_id TEXT NOT NULL,
            full_path TEXT NOT NULL,
            path_key TEXT NOT NULL,
            relative_path TEXT NOT NULL,
            relative_path_key TEXT NOT NULL,
            stable_identity TEXT,
            file_system_id TEXT,
            length INTEGER NOT NULL,
            creation_utc_ticks INTEGER NOT NULL,
            modified_utc_ticks INTEGER NOT NULL,
            attributes INTEGER NOT NULL,
            metadata_fingerprint TEXT NOT NULL,
            content_hash TEXT,
            processor_fingerprint TEXT NOT NULL,
            indexing_level INTEGER NOT NULL,
            fully_indexed INTEGER NOT NULL DEFAULT 0,
            deleted_utc_ticks INTEGER,
            last_seen_run_id TEXT,
            updated_utc_ticks INTEGER NOT NULL,
            UNIQUE(source_id, path_key),
            FOREIGN KEY(source_id) REFERENCES index_sources(id) ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ix_index_files_stable_identity
            ON index_files(source_id, file_system_id, stable_identity)
            WHERE stable_identity IS NOT NULL AND file_system_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_index_files_content_hash ON index_files(content_hash);
        CREATE INDEX IF NOT EXISTS ix_index_files_deleted ON index_files(deleted_utc_ticks);

        CREATE TABLE IF NOT EXISTS index_content (
            content_hash TEXT PRIMARY KEY,
            extracted_text TEXT,
            ocr_text TEXT,
            summary TEXT,
            keywords_json TEXT,
            semantic_json TEXT,
            coverage_level INTEGER NOT NULL DEFAULT -1,
            processor_fingerprint TEXT NOT NULL,
            updated_utc_ticks INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS index_chunks (
            content_hash TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            chunk_text TEXT NOT NULL,
            PRIMARY KEY(content_hash, ordinal),
            FOREIGN KEY(content_hash) REFERENCES index_content(content_hash) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS index_jobs (
            id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL,
            file_id TEXT NOT NULL,
            stage INTEGER NOT NULL,
            status INTEGER NOT NULL,
            attempt INTEGER NOT NULL DEFAULT 0,
            maximum_retries INTEGER NOT NULL,
            next_retry_utc_ticks INTEGER,
            waiting_dependency TEXT,
            failure_category INTEGER NOT NULL DEFAULT 0,
            error_code TEXT,
            priority INTEGER NOT NULL DEFAULT 0,
            queued_utc_ticks INTEGER NOT NULL,
            started_utc_ticks INTEGER,
            completed_utc_ticks INTEGER,
            UNIQUE(run_id, file_id),
            FOREIGN KEY(run_id) REFERENCES index_runs(id) ON DELETE CASCADE,
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_index_jobs_claim
            ON index_jobs(status, next_retry_utc_ticks, priority, queued_utc_ticks);

        CREATE TABLE IF NOT EXISTS index_stage_states (
            file_id TEXT NOT NULL,
            stage INTEGER NOT NULL,
            status INTEGER NOT NULL,
            attempt INTEGER NOT NULL DEFAULT 0,
            processor_fingerprint TEXT NOT NULL,
            started_utc_ticks INTEGER,
            completed_utc_ticks INTEGER,
            next_retry_utc_ticks INTEGER,
            waiting_dependency TEXT,
            failure_category INTEGER NOT NULL DEFAULT 0,
            error_code TEXT,
            duration_milliseconds INTEGER,
            PRIMARY KEY(file_id, stage),
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS index_failures (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            run_id TEXT NOT NULL,
            file_id TEXT NOT NULL,
            stage INTEGER NOT NULL,
            category INTEGER NOT NULL,
            error_code TEXT,
            attempt INTEGER NOT NULL,
            occurred_utc_ticks INTEGER NOT NULL,
            can_retry INTEGER NOT NULL,
            FOREIGN KEY(run_id) REFERENCES index_runs(id) ON DELETE CASCADE,
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_index_failures_occurred ON index_failures(occurred_utc_ticks);

        CREATE TABLE IF NOT EXISTS index_maintenance (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            action_code TEXT NOT NULL,
            reclaimed_bytes INTEGER NOT NULL,
            performed_utc_ticks INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_index_maintenance_performed ON index_maintenance(performed_utc_ticks);
        """;

    public const string CreateVersionTwo = """
        CREATE TABLE IF NOT EXISTS index_privacy_rules (
            source_id TEXT NOT NULL,
            relative_path_key TEXT NOT NULL,
            relative_path TEXT NOT NULL,
            is_excluded INTEGER NOT NULL DEFAULT 0,
            indexing_level_override INTEGER,
            suppress_ocr INTEGER NOT NULL DEFAULT 0,
            suppress_summary INTEGER NOT NULL DEFAULT 0,
            suppress_semantic INTEGER NOT NULL DEFAULT 0,
            repair_stage INTEGER,
            force_reprocess INTEGER NOT NULL DEFAULT 0,
            updated_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY(source_id, relative_path_key),
            FOREIGN KEY(source_id) REFERENCES index_sources(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_index_privacy_rules_excluded
            ON index_privacy_rules(is_excluded, source_id);
        """;

    public const string CreateVersionThree = """
        ALTER TABLE index_privacy_rules
            ADD COLUMN suppress_relationships INTEGER NOT NULL DEFAULT 0;

        CREATE TABLE IF NOT EXISTS index_relationship_features (
            file_id TEXT PRIMARY KEY,
            normalized_stem TEXT NOT NULL,
            folder_key TEXT NOT NULL,
            content_hash TEXT,
            date_bucket INTEGER,
            extracted_text_fingerprint TEXT,
            ocr_text_fingerprint TEXT,
            summary_fingerprint TEXT,
            keyword_keys_json TEXT NOT NULL,
            feature_version TEXT NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_relationship_features_content
            ON index_relationship_features(content_hash) WHERE content_hash IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_relationship_features_folder
            ON index_relationship_features(folder_key) WHERE folder_key <> '';
        CREATE INDEX IF NOT EXISTS ix_relationship_features_date
            ON index_relationship_features(date_bucket) WHERE date_bucket IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_relationship_features_stem
            ON index_relationship_features(normalized_stem);

        CREATE TABLE IF NOT EXISTS index_relationships (
            id TEXT PRIMARY KEY,
            first_file_id TEXT NOT NULL,
            second_file_id TEXT NOT NULL,
            relationship_type INTEGER NOT NULL,
            custom_type TEXT,
            confidence INTEGER NOT NULL,
            algorithm TEXT NOT NULL,
            algorithm_version TEXT NOT NULL,
            created_utc_ticks INTEGER NOT NULL,
            validated_utc_ticks INTEGER NOT NULL,
            decision INTEGER NOT NULL DEFAULT 0,
            is_manual INTEGER NOT NULL DEFAULT 0,
            context_key TEXT,
            CHECK(first_file_id < second_file_id),
            UNIQUE(first_file_id, second_file_id, relationship_type, custom_type),
            FOREIGN KEY(first_file_id) REFERENCES index_files(id) ON DELETE CASCADE,
            FOREIGN KEY(second_file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_relationships_first ON index_relationships(first_file_id, confidence);
        CREATE INDEX IF NOT EXISTS ix_relationships_second ON index_relationships(second_file_id, confidence);
        CREATE INDEX IF NOT EXISTS ix_relationships_context ON index_relationships(context_key);

        CREATE TABLE IF NOT EXISTS index_relationship_evidence (
            relationship_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            evidence_kind INTEGER NOT NULL,
            evidence_key TEXT NOT NULL,
            explanation TEXT NOT NULL,
            PRIMARY KEY(relationship_id, ordinal),
            FOREIGN KEY(relationship_id) REFERENCES index_relationships(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS relationship_pair_overrides (
            first_file_id TEXT NOT NULL,
            second_file_id TEXT NOT NULL,
            decision INTEGER NOT NULL,
            relationship_type INTEGER,
            custom_type TEXT,
            changed_utc_ticks INTEGER NOT NULL,
            CHECK(first_file_id < second_file_id),
            PRIMARY KEY(first_file_id, second_file_id),
            FOREIGN KEY(first_file_id) REFERENCES index_files(id) ON DELETE CASCADE,
            FOREIGN KEY(second_file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS smart_collections (
            id TEXT PRIMARY KEY,
            context_key TEXT UNIQUE,
            title TEXT NOT NULL,
            description TEXT NOT NULL,
            relationship_summary TEXT NOT NULL,
            context_type INTEGER NOT NULL,
            confidence INTEGER NOT NULL,
            creation_source INTEGER NOT NULL,
            is_pinned INTEGER NOT NULL DEFAULT 0,
            is_user_renamed INTEGER NOT NULL DEFAULT 0,
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_smart_collections_display
            ON smart_collections(is_pinned DESC, updated_utc_ticks DESC, title);

        CREATE TABLE IF NOT EXISTS smart_collection_members (
            collection_id TEXT NOT NULL,
            file_id TEXT NOT NULL,
            membership_source INTEGER NOT NULL,
            relationship_id TEXT,
            added_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY(collection_id, file_id),
            FOREIGN KEY(collection_id) REFERENCES smart_collections(id) ON DELETE CASCADE,
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE,
            FOREIGN KEY(relationship_id) REFERENCES index_relationships(id) ON DELETE SET NULL
        );

        CREATE INDEX IF NOT EXISTS ix_smart_collection_members_file
            ON smart_collection_members(file_id, collection_id);

        CREATE TABLE IF NOT EXISTS smart_collection_member_overrides (
            collection_id TEXT NOT NULL,
            file_id TEXT NOT NULL,
            excluded INTEGER NOT NULL DEFAULT 1,
            changed_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY(collection_id, file_id),
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS forgotten_smart_collections (
            context_key TEXT PRIMARY KEY,
            forgotten_utc_ticks INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS relationship_diagnostics (
            id INTEGER PRIMARY KEY CHECK(id = 1),
            last_analysis_utc_ticks INTEGER,
            last_duration_milliseconds INTEGER,
            last_candidate_count INTEGER NOT NULL DEFAULT 0,
            last_relationship_count INTEGER NOT NULL DEFAULT 0,
            last_collection_count INTEGER NOT NULL DEFAULT 0,
            algorithm_version TEXT NOT NULL DEFAULT '',
            repair_operation_count INTEGER NOT NULL DEFAULT 0
        );

        INSERT OR IGNORE INTO relationship_diagnostics(id) VALUES (1);
        """;

    public const string CreateVersionFour = """
        CREATE TABLE IF NOT EXISTS index_media_content (
            content_hash TEXT PRIMARY KEY,
            media_kind INTEGER NOT NULL,
            evidence_json TEXT NOT NULL,
            processing_fingerprint TEXT NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY(content_hash) REFERENCES index_content(content_hash) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_index_media_kind
            ON index_media_content(media_kind, updated_utc_ticks);

        """;

    public const string CreateVersionFourIndexes = """
        CREATE INDEX IF NOT EXISTS ix_relationship_features_media_transcript
            ON index_relationship_features(media_transcript_fingerprint)
            WHERE media_transcript_fingerprint IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_relationship_features_media_ocr
            ON index_relationship_features(media_ocr_fingerprint)
            WHERE media_ocr_fingerprint IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_relationship_features_media_device
            ON index_relationship_features(media_device_key, capture_date_bucket)
            WHERE media_device_key IS NOT NULL;
        """;

    public const string CreateVersionFive = """
        CREATE TABLE IF NOT EXISTS index_relationship_feature_terms (
            file_id TEXT NOT NULL,
            term TEXT NOT NULL,
            PRIMARY KEY(file_id, term),
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS ix_relationship_feature_terms_term
            ON index_relationship_feature_terms(term, file_id);
        """;

    public const string CreateVersionSix = """
        CREATE TABLE IF NOT EXISTS smart_tag_definitions (
            tag_id TEXT PRIMARY KEY,
            tag_type INTEGER NOT NULL,
            canonical_key TEXT NOT NULL,
            display_name TEXT NOT NULL,
            parent_tag_id TEXT,
            taxonomy_version TEXT NOT NULL,
            origin INTEGER NOT NULL,
            is_builtin INTEGER NOT NULL DEFAULT 0,
            is_hidden INTEGER NOT NULL DEFAULT 0,
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            UNIQUE(tag_type, canonical_key),
            FOREIGN KEY(parent_tag_id) REFERENCES smart_tag_definitions(tag_id) ON DELETE SET NULL
        );

        CREATE INDEX IF NOT EXISTS ix_smart_tag_definitions_type
            ON smart_tag_definitions(tag_type, is_hidden, canonical_key);

        CREATE TABLE IF NOT EXISTS file_smart_tag_assignments (
            file_id TEXT NOT NULL,
            tag_id TEXT NOT NULL,
            confidence INTEGER NOT NULL,
            evidence_score REAL,
            origin INTEGER NOT NULL,
            classifier TEXT NOT NULL,
            classifier_version TEXT NOT NULL,
            taxonomy_version TEXT NOT NULL,
            input_fingerprint TEXT NOT NULL,
            evidence_json TEXT NOT NULL,
            assignment_state INTEGER NOT NULL,
            active INTEGER NOT NULL DEFAULT 1,
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY(file_id, tag_id),
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE,
            FOREIGN KEY(tag_id) REFERENCES smart_tag_definitions(tag_id) ON DELETE CASCADE
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS ix_file_smart_tags_tag
            ON file_smart_tag_assignments(tag_id, active, file_id);
        CREATE INDEX IF NOT EXISTS ix_file_smart_tags_file
            ON file_smart_tag_assignments(file_id, active, assignment_state);

        CREATE TABLE IF NOT EXISTS file_smart_tag_decisions (
            file_id TEXT NOT NULL,
            tag_id TEXT NOT NULL,
            decision INTEGER NOT NULL,
            reset_generation INTEGER NOT NULL DEFAULT 0,
            changed_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY(file_id, tag_id),
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE,
            FOREIGN KEY(tag_id) REFERENCES smart_tag_definitions(tag_id) ON DELETE CASCADE
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS ix_file_smart_tag_decisions_state
            ON file_smart_tag_decisions(decision, file_id);

        CREATE TABLE IF NOT EXISTS file_smart_tag_status (
            file_id TEXT PRIMARY KEY,
            classification_state INTEGER NOT NULL,
            classifier TEXT NOT NULL,
            classifier_version TEXT NOT NULL,
            taxonomy_version TEXT NOT NULL,
            input_fingerprint TEXT NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );
        """;
}
