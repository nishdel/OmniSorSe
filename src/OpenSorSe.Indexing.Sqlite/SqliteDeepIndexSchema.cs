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
}
