using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Indexing.Sqlite;

/// <summary>
/// Implements the provider-independent durable indexing store with an application-owned SQLite database.
/// </summary>
public sealed partial class SqliteDeepIndexStore : IDeepIndexStore, IIndexPrivacyStore, IRelationshipStore, IDisposable
{
    private const int MaximumSearchDocuments = 100_000;
    private const int MaximumFailureRecords = 10_000;
    private readonly string _databasePath;
    private readonly IPathSemantics _pathSemantics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Initializes a local SQLite provider for one application-owned database path.</summary>
    public SqliteDeepIndexStore(string databasePath, IPathSemantics pathSemantics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
    }

    /// <summary>Gets the fully qualified application-owned database path.</summary>
    public string DatabasePath => _databasePath;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)
                    ?? throw new InvalidOperationException("The index database must have a parent directory."));
                try
                {
                    using var connection = OpenConnection(configureJournalMode: true);
                    EnsureIntegrity(connection);
                    var version = Convert.ToInt32(ExecuteScalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
                    if (version > DeepIndexingVersion.SchemaVersion)
                    {
                        throw new DeepIndexUnsupportedSchemaException(version, DeepIndexingVersion.SchemaVersion);
                    }

                    if (version < DeepIndexingVersion.SchemaVersion)
                    {
                        if (HasUserTables(connection))
                        {
                            CreateBackupCore(connection, "pre-migration");
                        }

                        using var transaction = connection.BeginTransaction();
                        if (version < 1)
                        {
                            ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionOne);
                        }

                        if (version < 2)
                        {
                            ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionTwo);
                        }

                        if (version < 3)
                        {
                            ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionThree);
                        }

                        if (version < 4)
                        {
                            ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionFour);
                            EnsureColumn(connection, transaction, "index_relationship_features", "media_transcript_fingerprint", "TEXT");
                            EnsureColumn(connection, transaction, "index_relationship_features", "media_ocr_fingerprint", "TEXT");
                            EnsureColumn(connection, transaction, "index_relationship_features", "media_device_key", "TEXT");
                            EnsureColumn(connection, transaction, "index_relationship_features", "capture_date_bucket", "INTEGER");
                            ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionFourIndexes);
                        }

                        if (version < 5)
                        {
                            EnsureColumn(connection, transaction, "index_content", "content_intelligence_json", "TEXT");
                            ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionFive);
                        }

                        ExecuteNonQuery(
                            connection,
                            transaction,
                            "INSERT INTO index_meta(key, value) VALUES ('schema_version', $version) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                            ("$version", DeepIndexingVersion.SchemaVersion.ToString(CultureInfo.InvariantCulture)));
                        ExecuteNonQuery(connection, transaction, $"PRAGMA user_version = {DeepIndexingVersion.SchemaVersion};");
                        transaction.Commit();
                    }

                    EnsureIntegrity(connection);
                    return 0;
                }
                catch (DeepIndexUnsupportedSchemaException)
                {
                    throw;
                }
                catch (DeepIndexCorruptException)
                {
                    throw;
                }
                catch (SqliteException exception)
                {
                    throw new DeepIndexCorruptException(
                        "The background index could not be opened safely. Review available backups, then rebuild the derived index from Search settings.",
                        exception);
                }
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> RecoverInterruptedWorkAsync(DateTimeOffset recoveredAtUtc, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var recovered = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_jobs
                    SET status = $queued,
                        started_utc_ticks = NULL,
                        next_retry_utc_ticks = NULL,
                        waiting_dependency = NULL,
                        failure_category = $none,
                        error_code = NULL
                    WHERE status = $running;
                    """,
                    ("$queued", (int)IndexingStageStatus.Queued),
                    ("$running", (int)IndexingStageStatus.Running),
                    ("$none", (int)IndexingFailureCategory.None));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_stage_states
                    SET status = $queued,
                        started_utc_ticks = NULL,
                        next_retry_utc_ticks = NULL,
                        waiting_dependency = NULL,
                        failure_category = $none,
                        error_code = NULL
                    WHERE status = $running;
                    """,
                    ("$queued", (int)IndexingStageStatus.Queued),
                    ("$running", (int)IndexingStageStatus.Running),
                    ("$none", (int)IndexingFailureCategory.None));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_runs
                    SET status = $running,
                        updated_utc_ticks = $updated,
                        current_stage = NULL,
                        current_file_name = NULL
                    WHERE status IN ($wasRunning, $cancelling);
                    """,
                    ("$running", (int)IndexingRunStatus.Running),
                    ("$updated", recoveredAtUtc.UtcTicks),
                    ("$wasRunning", (int)IndexingRunStatus.Running),
                    ("$cancelling", (int)IndexingRunStatus.Cancelling));
                transaction.Commit();
                return recovered;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ResumableIndexingRun>> GetResumableRunsAsync(
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync<IReadOnlyList<ResumableIndexingRun>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT r.id, r.status, r.discovery_complete,
                           s.id, s.root_path, s.display_name, s.indexing_level,
                           s.include_subfolders, s.enabled, s.priority, s.exclusions_json,
                           s.managed_by_watched_folders
                    FROM index_runs r
                    JOIN index_sources s ON s.id = r.source_id
                    WHERE r.id = (
                        SELECT latest.id
                        FROM index_runs latest
                        WHERE latest.source_id = r.source_id
                        ORDER BY latest.started_utc_ticks DESC, latest.id DESC
                        LIMIT 1
                    )
                      AND r.status <> $complete
                    ORDER BY r.started_utc_ticks, r.id;
                    """;
                AddParameters(command, ("$complete", (int)IndexingRunStatus.Complete));
                using var reader = command.ExecuteReader();
                var runs = new List<ResumableIndexingRun>();
                while (reader.Read())
                {
                    runs.Add(new ResumableIndexingRun(
                        reader.GetString(0),
                        new IndexingSource(
                            reader.GetString(3),
                            reader.GetString(4),
                            reader.GetString(5),
                            (IndexingLevel)reader.GetInt32(6),
                            reader.GetBoolean(7),
                            reader.GetBoolean(8),
                            reader.GetInt32(9),
                            DeserializeStrings(reader.GetString(10)),
                            reader.GetBoolean(11)),
                        (IndexingRunStatus)reader.GetInt32(1),
                        reader.GetBoolean(2)));
                }

                return runs;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task UpsertSourceAsync(IndexingSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                var now = DateTimeOffset.UtcNow.UtcTicks;
                ExecuteNonQuery(
                    connection,
                    null,
                    """
                    INSERT INTO index_sources(
                        id, root_path, root_path_key, display_name, indexing_level,
                        include_subfolders, enabled, priority, exclusions_json,
                        managed_by_watched_folders, created_utc_ticks, updated_utc_ticks)
                    VALUES(
                        $id, $root, $rootKey, $display, $level,
                        $include, $enabled, $priority, $exclusions, $watched, $now, $now)
                    ON CONFLICT(id) DO UPDATE SET
                        root_path = excluded.root_path,
                        root_path_key = excluded.root_path_key,
                        display_name = excluded.display_name,
                        indexing_level = excluded.indexing_level,
                        include_subfolders = excluded.include_subfolders,
                        enabled = excluded.enabled,
                        priority = excluded.priority,
                        exclusions_json = excluded.exclusions_json,
                        managed_by_watched_folders = excluded.managed_by_watched_folders,
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$id", source.Id),
                    ("$root", _pathSemantics.NormalizeAbsolutePath(source.RootPath)),
                    ("$rootKey", PathKey(source.RootPath)),
                    ("$display", Bound(source.DisplayName, 256)),
                    ("$level", (int)source.Level),
                    ("$include", source.IncludeSubfolders ? 1 : 0),
                    ("$enabled", source.Enabled ? 1 : 0),
                    ("$priority", source.Priority),
                    ("$exclusions", JsonSerializer.Serialize(source.Exclusions.Take(128))),
                    ("$watched", source.ManagedByWatchedFolders ? 1 : 0),
                    ("$now", now));
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync<IReadOnlyList<IndexingSource>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT id, root_path, display_name, indexing_level, include_subfolders,
                           enabled, priority, exclusions_json, managed_by_watched_folders
                    FROM index_sources
                    ORDER BY priority DESC, display_name COLLATE NOCASE, id;
                    """;
                using var reader = command.ExecuteReader();
                var sources = new List<IndexingSource>();
                while (reader.Read())
                {
                    sources.Add(new IndexingSource(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        (IndexingLevel)reader.GetInt32(3),
                        reader.GetBoolean(4),
                        reader.GetBoolean(5),
                        reader.GetInt32(6),
                        DeserializeStrings(reader.GetString(7)),
                        reader.GetBoolean(8)));
                }

                return sources;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SetSourcePriorityAsync(string sourceId, int priority, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                ExecuteNonQuery(
                    connection,
                    null,
                    "UPDATE index_sources SET priority = $priority, updated_utc_ticks = $now WHERE id = $id;",
                    ("$priority", priority),
                    ("$now", DateTimeOffset.UtcNow.UtcTicks),
                    ("$id", sourceId));
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(connection, transaction, "DELETE FROM index_sources WHERE id = $id;", ("$id", sourceId));
                DeleteOrphanedContent(connection, transaction);
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> BeginRunAsync(string sourceId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var runId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_jobs
                    SET status = $cancelled,
                        completed_utc_ticks = $started,
                        failure_category = $category,
                        error_code = 'superseded-by-refresh'
                    WHERE run_id IN (
                        SELECT id FROM index_runs
                        WHERE source_id = $source
                          AND status IN ($pending, $runningRun, $pausedRun, $waitingRun, $cancellingRun)
                    )
                      AND status NOT IN ($complete, $skipped, $failed, $cancelled);

                    UPDATE index_runs
                    SET status = $cancelledRun,
                        completed_utc_ticks = $started,
                        updated_utc_ticks = $started,
                        cancellation_reason = 'Superseded by a newer source refresh.',
                        current_stage = NULL,
                        current_file_name = NULL
                    WHERE source_id = $source
                      AND status IN ($pending, $runningRun, $pausedRun, $waitingRun, $cancellingRun);
                    """,
                    ("$cancelled", (int)IndexingStageStatus.Cancelled),
                    ("$started", startedAtUtc.UtcTicks),
                    ("$category", (int)IndexingFailureCategory.Cancelled),
                    ("$source", sourceId),
                    ("$pending", (int)IndexingRunStatus.Pending),
                    ("$complete", (int)IndexingStageStatus.Complete),
                    ("$skipped", (int)IndexingStageStatus.Skipped),
                    ("$failed", (int)IndexingStageStatus.Failed),
                    ("$cancelledRun", (int)IndexingRunStatus.Cancelled),
                    ("$runningRun", (int)IndexingRunStatus.Running),
                    ("$pausedRun", (int)IndexingRunStatus.Paused),
                    ("$waitingRun", (int)IndexingRunStatus.Waiting),
                    ("$cancellingRun", (int)IndexingRunStatus.Cancelling));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_stage_states
                    SET status = $cancelled,
                        completed_utc_ticks = $started,
                        failure_category = $category,
                        error_code = 'superseded-by-refresh'
                    WHERE status NOT IN ($complete, $skipped, $failed, $cancelled)
                      AND EXISTS (
                          SELECT 1
                          FROM index_jobs j
                          JOIN index_runs r ON r.id = j.run_id
                          WHERE j.file_id = index_stage_states.file_id
                            AND j.stage = index_stage_states.stage
                            AND r.source_id = $source
                            AND r.status = $cancelledRun
                            AND r.completed_utc_ticks = $started
                      );
                    """,
                    ("$cancelled", (int)IndexingStageStatus.Cancelled),
                    ("$started", startedAtUtc.UtcTicks),
                    ("$category", (int)IndexingFailureCategory.Cancelled),
                    ("$complete", (int)IndexingStageStatus.Complete),
                    ("$skipped", (int)IndexingStageStatus.Skipped),
                    ("$failed", (int)IndexingStageStatus.Failed),
                    ("$source", sourceId),
                    ("$cancelledRun", (int)IndexingRunStatus.Cancelled));
                var changed = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO index_runs(
                        id, source_id, status, started_utc_ticks, updated_utc_ticks, discovery_complete)
                    SELECT $id, id, $status, $started, $started, 0
                    FROM index_sources
                    WHERE id = $source AND enabled = 1;
                    """,
                    ("$id", runId),
                    ("$source", sourceId),
                    ("$status", (int)IndexingRunStatus.Running),
                    ("$started", startedAtUtc.UtcTicks));
                if (changed != 1)
                {
                    transaction.Rollback();
                    throw new InvalidOperationException("The indexing source is missing or disabled.");
                }

                transaction.Commit();
                return runId;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task EnqueueDiscoveredFilesAsync(
        string runId,
        IReadOnlyList<IndexingFileObservation> files,
        string processorFingerprint,
        int maximumRetryCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorFingerprint);
        if (maximumRetryCount is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRetryCount));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var source = ReadRunSource(connection, transaction, runId);
                foreach (var observation in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    QueueObservation(connection, transaction, runId, source, observation, processorFingerprint, maximumRetryCount);
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_runs
                    SET total_discovered = (SELECT COUNT(*) FROM index_jobs WHERE run_id = $run),
                        updated_utc_ticks = $now
                    WHERE id = $run;
                    """,
                    ("$run", runId),
                    ("$now", DateTimeOffset.UtcNow.UtcTicks));
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task CompleteDiscoveryAsync(
        string runId,
        IReadOnlySet<string> observedRelativePaths,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(observedRelativePaths);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_files
                    SET deleted_utc_ticks = $now,
                        fully_indexed = 0,
                        updated_utc_ticks = $now
                    WHERE source_id = (SELECT source_id FROM index_runs WHERE id = $run)
                      AND deleted_utc_ticks IS NULL
                      AND (last_seen_run_id IS NULL OR last_seen_run_id <> $run);
                    """,
                    ("$now", completedAtUtc.UtcTicks),
                    ("$run", runId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_runs
                    SET discovery_complete = 1, updated_utc_ticks = $now
                    WHERE id = $run;
                    """,
                    ("$now", completedAtUtc.UtcTicks),
                    ("$run", runId));
                UpdateRunCompletion(connection, transaction, runId, completedAtUtc);
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexingWorkItem?> ClaimNextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    SELECT j.id, j.run_id, f.id, f.source_id, f.full_path, f.relative_path,
                           f.length, f.creation_utc_ticks, f.modified_utc_ticks, f.attributes,
                           f.stable_identity, f.file_system_id, f.metadata_fingerprint,
                           f.indexing_level, j.stage, j.attempt, f.processor_fingerprint,
                           f.content_hash, c.extracted_text, c.ocr_text, m.evidence_json,
                           COALESCE(p.suppress_ocr, 0),
                           COALESCE(p.suppress_summary, 0),
                           COALESCE(p.suppress_semantic, 0),
                           COALESCE(p.force_reprocess, 0),
                           c.content_intelligence_json
                    FROM index_jobs j
                    JOIN index_runs r ON r.id = j.run_id
                    JOIN index_sources s ON s.id = r.source_id
                    JOIN index_files f ON f.id = j.file_id
                    LEFT JOIN index_content c ON c.content_hash = f.content_hash
                    LEFT JOIN index_media_content m ON m.content_hash = f.content_hash
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id
                     AND p.relative_path_key = f.relative_path_key
                    WHERE r.status = $running
                      AND f.deleted_utc_ticks IS NULL
                      AND (
                          j.status IN ($queued, $retry)
                          OR (j.status = $waiting AND j.next_retry_utc_ticks IS NOT NULL AND j.next_retry_utc_ticks <= $now)
                      )
                      AND (j.next_retry_utc_ticks IS NULL OR j.next_retry_utc_ticks <= $now)
                    ORDER BY s.priority DESC, j.priority DESC, j.queued_utc_ticks, j.id
                    LIMIT 1;
                    """;
                AddParameters(
                    command,
                    ("$running", (int)IndexingRunStatus.Running),
                    ("$queued", (int)IndexingStageStatus.Queued),
                    ("$retry", (int)IndexingStageStatus.RetryScheduled),
                    ("$waiting", (int)IndexingStageStatus.WaitingForDependency),
                    ("$now", nowUtc.UtcTicks));
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    transaction.Commit();
                    return null;
                }

                var work = new IndexingWorkItem
                {
                    JobId = reader.GetString(0),
                    RunId = reader.GetString(1),
                    FileId = reader.GetString(2),
                    SourceId = reader.GetString(3),
                    FullPath = reader.GetString(4),
                    RelativePath = reader.GetString(5),
                    Observation = new IndexingFileObservation(
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11),
                        reader.GetInt64(6),
                        new DateTimeOffset(reader.GetInt64(7), TimeSpan.Zero),
                        new DateTimeOffset(reader.GetInt64(8), TimeSpan.Zero),
                        (FileAttributes)reader.GetInt64(9),
                        reader.GetString(12)),
                    Level = (IndexingLevel)reader.GetInt32(13),
                    Stage = (IndexingStage)reader.GetInt32(14),
                    Attempt = reader.GetInt32(15) + 1,
                    ProcessorFingerprint = reader.GetString(16),
                    ContentHash = reader.IsDBNull(17) ? null : reader.GetString(17),
                    ExtractedText = reader.IsDBNull(18) ? null : reader.GetString(18),
                    OcrText = reader.IsDBNull(19) ? null : reader.GetString(19),
                    MediaEvidence = reader.IsDBNull(20) ? null : TryDeserializeMediaEvidence(reader.GetString(20)),
                    SuppressOcr = reader.GetBoolean(21),
                    SuppressSummary = reader.GetBoolean(22),
                    SuppressSemantic = reader.GetBoolean(23),
                    ForceReprocess = reader.GetBoolean(24),
                    ContentIntelligence = reader.IsDBNull(25)
                        ? null
                        : TryDeserializeContentIntelligence(reader.GetString(25)),
                };
                reader.Close();
                var changed = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_jobs
                    SET status = $running,
                        attempt = attempt + 1,
                        started_utc_ticks = $now,
                        completed_utc_ticks = NULL,
                        next_retry_utc_ticks = NULL,
                        waiting_dependency = NULL
                    WHERE id = $id AND status <> $running;
                    """,
                    ("$running", (int)IndexingStageStatus.Running),
                    ("$now", nowUtc.UtcTicks),
                    ("$id", work.JobId));
                if (changed != 1)
                {
                    transaction.Rollback();
                    return null;
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO index_stage_states(
                        file_id, stage, status, attempt, processor_fingerprint, started_utc_ticks)
                    VALUES($file, $stage, $status, $attempt, $processor, $now)
                    ON CONFLICT(file_id, stage) DO UPDATE SET
                        status = excluded.status,
                        attempt = excluded.attempt,
                        processor_fingerprint = excluded.processor_fingerprint,
                        started_utc_ticks = excluded.started_utc_ticks,
                        completed_utc_ticks = NULL,
                        next_retry_utc_ticks = NULL,
                        waiting_dependency = NULL,
                        failure_category = $none,
                        error_code = NULL;
                    """,
                    ("$file", work.FileId),
                    ("$stage", (int)work.Stage),
                    ("$status", (int)IndexingStageStatus.Running),
                    ("$attempt", work.Attempt),
                    ("$processor", work.ProcessorFingerprint),
                    ("$now", nowUtc.UtcTicks),
                    ("$none", (int)IndexingFailureCategory.None));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_runs
                    SET current_stage = $stage,
                        current_file_name = $fileName,
                        updated_utc_ticks = $now
                    WHERE id = $run;
                    """,
                    ("$stage", (int)work.Stage),
                    ("$fileName", Bound(Path.GetFileName(work.FullPath), 512)),
                    ("$now", nowUtc.UtcTicks),
                    ("$run", work.RunId));
                transaction.Commit();
                return work;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> ResumeEligibleWaitingRunsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return ExecuteNonQuery(
                    connection,
                    null,
                    """
                    UPDATE index_runs
                    SET status = $running,
                        cancellation_reason = NULL,
                        updated_utc_ticks = $now
                    WHERE status = $waitingRun
                      AND EXISTS (
                          SELECT 1
                          FROM index_jobs j
                          WHERE j.run_id = index_runs.id
                            AND (
                                j.status = $queued
                                OR (j.status IN ($retry, $waitingJob)
                                    AND j.next_retry_utc_ticks IS NOT NULL
                                    AND j.next_retry_utc_ticks <= $now)
                            )
                      );
                    """,
                    ("$running", (int)IndexingRunStatus.Running),
                    ("$now", nowUtc.UtcTicks),
                    ("$waitingRun", (int)IndexingRunStatus.Waiting),
                    ("$queued", (int)IndexingStageStatus.Queued),
                    ("$retry", (int)IndexingStageStatus.RetryScheduled),
                    ("$waitingJob", (int)IndexingStageStatus.WaitingForDependency));
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SaveStageOutputAsync(
        IndexingWorkItem workItem,
        IndexingStageOutput output,
        IndexingStage? nextStage,
        DateTimeOffset completedAtUtc,
        TimeSpan duration,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(output);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                EnsureClaimIsCurrent(connection, transaction, workItem);
                SaveDerivedOutput(connection, transaction, workItem, output, completedAtUtc);

                var stageStatus = output.Status;
                var jobStatus = stageStatus;
                if (stageStatus == IndexingStageStatus.Failed &&
                    output.IsRetryable &&
                    workItem.Attempt <= ReadMaximumRetries(connection, transaction, workItem.JobId))
                {
                    stageStatus = IndexingStageStatus.RetryScheduled;
                    jobStatus = IndexingStageStatus.RetryScheduled;
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_stage_states
                    SET status = $status,
                        attempt = $attempt,
                        completed_utc_ticks = $completed,
                        next_retry_utc_ticks = $retry,
                        waiting_dependency = $dependency,
                        failure_category = $category,
                        error_code = $error,
                        duration_milliseconds = $duration
                    WHERE file_id = $file AND stage = $stage;
                    """,
                    ("$status", (int)stageStatus),
                    ("$attempt", workItem.Attempt),
                    ("$completed", completedAtUtc.UtcTicks),
                    ("$retry", retryAtUtc?.UtcTicks),
                    ("$dependency", BoundOrNull(output.WaitingDependency, 128)),
                    ("$category", (int)output.FailureCategory),
                    ("$error", BoundOrNull(output.ErrorCode, 256)),
                    ("$duration", Math.Max(0, (long)duration.TotalMilliseconds)),
                    ("$file", workItem.FileId),
                    ("$stage", (int)workItem.Stage));

                if (stageStatus is IndexingStageStatus.Failed or
                    IndexingStageStatus.RetryScheduled or
                    IndexingStageStatus.WaitingForDependency)
                {
                    InsertFailure(connection, transaction, workItem, output, completedAtUtc, stageStatus != IndexingStageStatus.Failed);
                }

                if (stageStatus is IndexingStageStatus.Complete or IndexingStageStatus.Skipped)
                {
                    if (nextStage.HasValue)
                    {
                        QueueNextStage(connection, transaction, workItem, nextStage.Value, completedAtUtc);
                    }
                    else
                    {
                        ExecuteNonQuery(
                            connection,
                            transaction,
                            """
                            UPDATE index_jobs
                            SET status = $terminalStatus, completed_utc_ticks = $now, error_code = NULL,
                                failure_category = $none, waiting_dependency = NULL, next_retry_utc_ticks = NULL
                            WHERE id = $id;
                            """,
                            ("$terminalStatus", (int)stageStatus),
                            ("$now", completedAtUtc.UtcTicks),
                            ("$none", (int)IndexingFailureCategory.None),
                            ("$id", workItem.JobId));
                        if (stageStatus == IndexingStageStatus.Complete)
                        {
                            ExecuteNonQuery(
                                connection,
                                transaction,
                                """
                                UPDATE index_files
                                SET fully_indexed = 1, updated_utc_ticks = $now
                                WHERE id = $file;
                                """,
                                ("$now", completedAtUtc.UtcTicks),
                                ("$file", workItem.FileId));
                            ExecuteNonQuery(
                                connection,
                                transaction,
                                """
                                UPDATE index_privacy_rules
                                SET force_reprocess = 0, updated_utc_ticks = $now
                                WHERE source_id = $source
                                  AND relative_path_key = (
                                      SELECT relative_path_key FROM index_files WHERE id = $file
                                  );
                                """,
                                ("$now", completedAtUtc.UtcTicks),
                                ("$source", workItem.SourceId),
                                ("$file", workItem.FileId));
                        }
                    }
                }
                else
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE index_jobs
                        SET status = $status,
                            next_retry_utc_ticks = $retry,
                            waiting_dependency = $dependency,
                            failure_category = $category,
                            error_code = $error,
                            completed_utc_ticks = CASE WHEN $terminal = 1 THEN $now ELSE NULL END
                        WHERE id = $id;
                        """,
                        ("$status", (int)jobStatus),
                        ("$retry", retryAtUtc?.UtcTicks),
                        ("$dependency", BoundOrNull(output.WaitingDependency, 128)),
                        ("$category", (int)output.FailureCategory),
                        ("$error", BoundOrNull(output.ErrorCode, 256)),
                        ("$terminal", jobStatus is IndexingStageStatus.Failed or IndexingStageStatus.Cancelled ? 1 : 0),
                        ("$now", completedAtUtc.UtcTicks),
                        ("$id", workItem.JobId));
                }

                UpdateRunCompletion(connection, transaction, workItem.RunId, completedAtUtc);
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexingStage?> GetReusableContentThroughStageAsync(
        string contentHash,
        IndexingLevel level,
        string processorFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        return RunExclusiveAsync<IndexingStage?>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT coverage_level
                    FROM index_content
                    WHERE content_hash = $hash AND processor_fingerprint = $processor;
                    """;
                AddParameters(command, ("$hash", contentHash), ("$processor", processorFingerprint));
                var value = command.ExecuteScalar();
                if (value is null or DBNull)
                {
                    return null;
                }

                var coverage = (IndexingLevel)Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return coverage >= level
                    ? IndexingStage.SemanticRepresentationGenerated
                    : null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ReuseContentAsync(
        IndexingWorkItem workItem,
        string contentHash,
        IndexingStage throughStage,
        IndexingStage nextStage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                EnsureClaimIsCurrent(connection, transaction, workItem);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE index_files SET content_hash = $hash, updated_utc_ticks = $now WHERE id = $file;",
                    ("$hash", contentHash),
                    ("$now", completedAtUtc.UtcTicks),
                    ("$file", workItem.FileId));
                foreach (var stage in Enum.GetValues<IndexingStage>())
                {
                    if (stage <= IndexingStage.ContentFingerprinted || stage > throughStage)
                    {
                        continue;
                    }

                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO index_stage_states(
                            file_id, stage, status, attempt, processor_fingerprint, completed_utc_ticks, duration_milliseconds)
                        VALUES($file, $stage, $complete, 0, $processor, $now, 0)
                        ON CONFLICT(file_id, stage) DO UPDATE SET
                            status = excluded.status,
                            processor_fingerprint = excluded.processor_fingerprint,
                            completed_utc_ticks = excluded.completed_utc_ticks,
                            duration_milliseconds = 0,
                            failure_category = $none,
                            error_code = NULL;
                        """,
                        ("$file", workItem.FileId),
                        ("$stage", (int)stage),
                        ("$complete", (int)IndexingStageStatus.Complete),
                        ("$processor", workItem.ProcessorFingerprint),
                        ("$now", completedAtUtc.UtcTicks),
                        ("$none", (int)IndexingFailureCategory.None));
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_stage_states
                    SET status = $complete, completed_utc_ticks = $now, duration_milliseconds = 0
                    WHERE file_id = $file AND stage = $fingerprint;
                    """,
                    ("$complete", (int)IndexingStageStatus.Complete),
                    ("$now", completedAtUtc.UtcTicks),
                    ("$file", workItem.FileId),
                    ("$fingerprint", (int)IndexingStage.ContentFingerprinted));
                QueueNextStage(connection, transaction, workItem, nextStage, completedAtUtc);
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetActiveRunsStatusAsync(
        IndexingRunStatus status,
        string? reason,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (status is not (IndexingRunStatus.Running or IndexingRunStatus.Paused or
            IndexingRunStatus.Waiting or IndexingRunStatus.Cancelling or IndexingRunStatus.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_runs
                    SET status = $status,
                        cancellation_reason = $reason,
                        updated_utc_ticks = $now,
                        completed_utc_ticks = CASE WHEN $terminal = 1 THEN $now ELSE NULL END,
                        current_stage = CASE WHEN $terminal = 1 THEN NULL ELSE current_stage END,
                        current_file_name = CASE WHEN $terminal = 1 THEN NULL ELSE current_file_name END
                    WHERE status IN ($pending, $running, $paused, $waiting, $cancelling)
                      AND ($status <> $waitingTarget OR status <> $paused);
                    """,
                    ("$status", (int)status),
                    ("$reason", BoundOrNull(reason, 256)),
                    ("$now", changedAtUtc.UtcTicks),
                    ("$terminal", status == IndexingRunStatus.Cancelled ? 1 : 0),
                    ("$pending", (int)IndexingRunStatus.Pending),
                    ("$running", (int)IndexingRunStatus.Running),
                    ("$paused", (int)IndexingRunStatus.Paused),
                    ("$waiting", (int)IndexingRunStatus.Waiting),
                    ("$cancelling", (int)IndexingRunStatus.Cancelling),
                    ("$waitingTarget", (int)IndexingRunStatus.Waiting));
                if (status == IndexingRunStatus.Cancelled)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE index_jobs
                        SET status = $cancelled,
                            completed_utc_ticks = $now,
                            failure_category = $category,
                            error_code = 'cancelled'
                        WHERE status NOT IN ($complete, $failed, $cancelled);
                        """,
                        ("$cancelled", (int)IndexingStageStatus.Cancelled),
                        ("$now", changedAtUtc.UtcTicks),
                        ("$category", (int)IndexingFailureCategory.Cancelled),
                        ("$complete", (int)IndexingStageStatus.Complete),
                        ("$failed", (int)IndexingStageStatus.Failed));
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE index_stage_states
                        SET status = $cancelled,
                            completed_utc_ticks = $now,
                            failure_category = $category,
                            error_code = 'cancelled'
                        WHERE status NOT IN ($complete, $skipped, $failed, $cancelled)
                          AND EXISTS (
                              SELECT 1
                              FROM index_jobs j
                              JOIN index_runs r ON r.id = j.run_id
                              WHERE j.file_id = index_stage_states.file_id
                                AND j.stage = index_stage_states.stage
                                AND r.status = $cancelledRun
                          );
                        """,
                        ("$cancelled", (int)IndexingStageStatus.Cancelled),
                        ("$now", changedAtUtc.UtcTicks),
                        ("$category", (int)IndexingFailureCategory.Cancelled),
                        ("$complete", (int)IndexingStageStatus.Complete),
                        ("$skipped", (int)IndexingStageStatus.Skipped),
                        ("$failed", (int)IndexingStageStatus.Failed),
                        ("$cancelledRun", (int)IndexingRunStatus.Cancelled));
                }

                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkRunFailedAsync(
        string runId,
        string reason,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_runs
                    SET status = $failed,
                        cancellation_reason = $reason,
                        updated_utc_ticks = $now,
                        completed_utc_ticks = $now,
                        current_stage = NULL,
                        current_file_name = NULL
                    WHERE id = $run;
                    """,
                    ("$failed", (int)IndexingRunStatus.Failed),
                    ("$reason", Bound(reason, 256)),
                    ("$now", failedAtUtc.UtcTicks),
                    ("$run", runId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_jobs
                    SET status = $failed,
                        completed_utc_ticks = $now,
                        failure_category = $category,
                        error_code = $reason
                    WHERE run_id = $run
                      AND status NOT IN ($complete, $skipped, $cancelled);
                    """,
                    ("$failed", (int)IndexingStageStatus.Failed),
                    ("$now", failedAtUtc.UtcTicks),
                    ("$category", (int)IndexingFailureCategory.TransientIo),
                    ("$reason", Bound(reason, 256)),
                    ("$run", runId),
                    ("$complete", (int)IndexingStageStatus.Complete),
                    ("$skipped", (int)IndexingStageStatus.Skipped),
                    ("$cancelled", (int)IndexingStageStatus.Cancelled));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_stage_states
                    SET status = $failed,
                        completed_utc_ticks = $now,
                        failure_category = $category,
                        error_code = $reason
                    WHERE status NOT IN ($complete, $skipped, $failed, $cancelled)
                      AND EXISTS (
                          SELECT 1
                          FROM index_jobs j
                          WHERE j.run_id = $run
                            AND j.file_id = index_stage_states.file_id
                            AND j.stage = index_stage_states.stage
                      );
                    """,
                    ("$failed", (int)IndexingStageStatus.Failed),
                    ("$now", failedAtUtc.UtcTicks),
                    ("$category", (int)IndexingFailureCategory.TransientIo),
                    ("$reason", Bound(reason, 256)),
                    ("$run", runId),
                    ("$complete", (int)IndexingStageStatus.Complete),
                    ("$skipped", (int)IndexingStageStatus.Skipped),
                    ("$cancelled", (int)IndexingStageStatus.Cancelled));
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> RetryIncompleteAsync(DateTimeOffset queuedAtUtc, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var changed = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_jobs
                    SET status = $queued,
                        next_retry_utc_ticks = NULL,
                        waiting_dependency = NULL,
                        failure_category = $none,
                        error_code = NULL,
                        completed_utc_ticks = NULL,
                        queued_utc_ticks = $now
                    WHERE status IN ($failed, $waiting, $retry, $cancelled)
                      AND attempt <= maximum_retries;
                    """,
                    ("$queued", (int)IndexingStageStatus.Queued),
                    ("$none", (int)IndexingFailureCategory.None),
                    ("$now", queuedAtUtc.UtcTicks),
                    ("$failed", (int)IndexingStageStatus.Failed),
                    ("$waiting", (int)IndexingStageStatus.WaitingForDependency),
                    ("$retry", (int)IndexingStageStatus.RetryScheduled),
                    ("$cancelled", (int)IndexingStageStatus.Cancelled));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_stage_states
                    SET status = $queued,
                        started_utc_ticks = NULL,
                        completed_utc_ticks = NULL,
                        next_retry_utc_ticks = NULL,
                        waiting_dependency = NULL,
                        failure_category = $none,
                        error_code = NULL
                    WHERE EXISTS (
                        SELECT 1
                        FROM index_jobs j
                        WHERE j.file_id = index_stage_states.file_id
                          AND j.stage = index_stage_states.stage
                          AND j.status = $queued
                    );
                    """,
                    ("$queued", (int)IndexingStageStatus.Queued),
                    ("$none", (int)IndexingFailureCategory.None));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_runs
                    SET status = $running, completed_utc_ticks = NULL, updated_utc_ticks = $now
                    WHERE id IN (
                        SELECT DISTINCT run_id FROM index_jobs
                        WHERE status = $queued
                    );
                    """,
                    ("$running", (int)IndexingRunStatus.Running),
                    ("$queued", (int)IndexingStageStatus.Queued),
                    ("$now", queuedAtUtc.UtcTicks));
                transaction.Commit();
                return changed;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IndexingProgressSnapshot> GetProgressAsync(
        long maximumIndexSizeBytes,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT r.id, r.status, r.current_stage, r.current_file_name,
                           r.total_discovered, r.started_utc_ticks,
                           SUM(CASE WHEN j.status IN ($complete, $skipped, $failed, $cancelled) THEN 1 ELSE 0 END),
                           SUM(CASE WHEN j.status = $complete THEN 1 ELSE 0 END),
                           SUM(CASE WHEN j.status = $skipped THEN 1 ELSE 0 END),
                           SUM(CASE WHEN j.status = $failed THEN 1 ELSE 0 END),
                           SUM(CASE WHEN j.status = $waiting THEN 1 ELSE 0 END),
                           SUM(CASE WHEN j.status = $retry THEN 1 ELSE 0 END)
                    FROM index_runs r
                    LEFT JOIN index_jobs j ON j.run_id = r.id
                    GROUP BY r.id
                    ORDER BY
                        CASE WHEN r.status IN ($runningRun, $pausedRun, $waitingRun, $cancellingRun) THEN 0 ELSE 1 END,
                        r.started_utc_ticks DESC
                    LIMIT 1;
                    """;
                AddParameters(
                    command,
                    ("$complete", (int)IndexingStageStatus.Complete),
                    ("$skipped", (int)IndexingStageStatus.Skipped),
                    ("$failed", (int)IndexingStageStatus.Failed),
                    ("$cancelled", (int)IndexingStageStatus.Cancelled),
                    ("$waiting", (int)IndexingStageStatus.WaitingForDependency),
                    ("$retry", (int)IndexingStageStatus.RetryScheduled),
                    ("$runningRun", (int)IndexingRunStatus.Running),
                    ("$pausedRun", (int)IndexingRunStatus.Paused),
                    ("$waitingRun", (int)IndexingRunStatus.Waiting),
                    ("$cancellingRun", (int)IndexingRunStatus.Cancelling));
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return new IndexingProgressSnapshot
                    {
                        IndexSizeBytes = GetPhysicalSize(),
                        MaximumIndexSizeBytes = maximumIndexSizeBytes,
                        Coverage = ReadCoverage(connection),
                    };
                }

                var total = reader.GetInt64(4);
                var started = new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero);
                var processed = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
                var elapsed = nowUtc - started;
                var speed = elapsed.TotalSeconds >= 1 ? processed / elapsed.TotalSeconds : 0;
                var remaining = Math.Max(0, total - processed);
                TimeSpan? estimate = processed >= 5 && elapsed >= TimeSpan.FromSeconds(2) && speed > 0 && remaining > 0
                    ? TimeSpan.FromSeconds(remaining / speed)
                    : null;
                return new IndexingProgressSnapshot
                {
                    RunId = reader.GetString(0),
                    Status = (IndexingRunStatus)reader.GetInt32(1),
                    CurrentStage = reader.IsDBNull(2) ? null : (IndexingStage)reader.GetInt32(2),
                    CurrentFile = reader.IsDBNull(3) ? null : reader.GetString(3),
                    TotalDiscovered = total,
                    Processed = processed,
                    Completed = reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                    Skipped = reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
                    Failed = reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
                    Waiting = reader.IsDBNull(10) ? 0 : reader.GetInt64(10),
                    RetryScheduled = reader.IsDBNull(11) ? 0 : reader.GetInt64(11),
                    FilesPerSecond = speed,
                    EstimatedRemaining = estimate,
                    IndexSizeBytes = GetPhysicalSize(),
                    MaximumIndexSizeBytes = maximumIndexSizeBytes,
                    Coverage = ReadCoverage(connection),
                };
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<SearchCoverage> GetSearchCoverageAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return ReadCoverage(connection);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IndexStorageBreakdown> GetStorageBreakdownAsync(
        long maximumIndexSizeBytes,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return ReadStorageBreakdown(connection, maximumIndexSizeBytes);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetSearchDocumentsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > MaximumSearchDocuments)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        return RunExclusiveAsync<IReadOnlyList<ProgressiveSearchDocument>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT f.id, f.full_path, f.relative_path, f.length, f.modified_utc_ticks,
                           f.fully_indexed, c.extracted_text, c.ocr_text, c.summary,
                           c.keywords_json, c.semantic_json,
                           s.id, s.display_name, s.priority, f.creation_utc_ticks,
                           f.indexing_level,
                           EXISTS(SELECT 1 FROM index_failures x WHERE x.file_id = f.id),
                           f.content_hash,
                           COALESCE(p.suppress_ocr, 0),
                           COALESCE(p.suppress_summary, 0),
                           COALESCE(p.suppress_semantic, 0),
                           m.evidence_json, c.content_intelligence_json
                    FROM index_files f
                    JOIN index_sources s ON s.id = f.source_id
                    LEFT JOIN index_content c ON c.content_hash = f.content_hash
                    LEFT JOIN index_media_content m ON m.content_hash = f.content_hash
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id
                     AND p.relative_path_key = f.relative_path_key
                    WHERE f.deleted_utc_ticks IS NULL
                      AND COALESCE(p.is_excluded, 0) = 0
                    ORDER BY f.relative_path_key, f.id
                    LIMIT $maximum;
                    """;
                command.Parameters.AddWithValue("$maximum", maximumCount);
                using var reader = command.ExecuteReader();
                var documents = new List<ProgressiveSearchDocument>();
                var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    var fullPath = reader.GetString(1);
                    var extension = Path.GetExtension(fullPath).ToLowerInvariant();
                    var suppressOcr = reader.GetBoolean(18);
                    var suppressSummary = reader.GetBoolean(19);
                    var suppressSemantic = reader.GetBoolean(20);
                    var indexingLevel = (IndexingLevel)reader.GetInt32(15);
                    var keywordsValid = true;
                    var keywords = reader.IsDBNull(9)
                        ? []
                        : TryDeserializeStrings(reader.GetString(9), out keywordsValid);
                    var semanticValid = true;
                    var semantic = suppressSemantic || reader.IsDBNull(10)
                        ? null
                        : TryDeserializeFloats(reader.GetString(10), out semanticValid);
                    var mediaEvidence = reader.IsDBNull(21) ? null : TryDeserializeMediaEvidence(reader.GetString(21));
                    var contentIntelligence = reader.IsDBNull(22)
                        ? null
                        : TryDeserializeContentIntelligence(reader.GetString(22));
                    documents.Add(new ProgressiveSearchDocument
                    {
                        FileId = reader.GetString(0),
                        FullPath = fullPath,
                        FileName = Path.GetFileName(fullPath),
                        RelativePath = reader.GetString(2),
                        FolderName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? string.Empty,
                        Extension = extension,
                        FileType = SearchFileTypeClassifier.Classify(extension),
                        SourceId = reader.GetString(11),
                        SourceName = reader.GetString(12),
                        SourcePriority = reader.GetInt32(13),
                        Length = reader.GetInt64(3),
                        CreationTimeUtc = new DateTimeOffset(reader.GetInt64(14), TimeSpan.Zero),
                        ModifiedTimeUtc = new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero),
                        IndexingLevel = indexingLevel,
                        MetadataText = string.Join(' ',
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{Path.GetExtension(fullPath)} {reader.GetInt64(3)} {new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero):O} {reader.GetString(2)}")),
                        IsFullyIndexed = reader.GetBoolean(5),
                        ExtractedText = indexingLevel == IndexingLevel.Basic || reader.IsDBNull(6)
                            ? null
                            : reader.GetString(6),
                        OcrText = indexingLevel == IndexingLevel.Basic || suppressOcr || reader.IsDBNull(7)
                            ? null
                            : reader.GetString(7),
                        MediaEvidence = mediaEvidence,
                        ContentIntelligence = indexingLevel == IndexingLevel.Basic || suppressSummary
                            ? null
                            : contentIntelligence,
                        Summary = indexingLevel == IndexingLevel.Basic || suppressSummary || reader.IsDBNull(8)
                            ? null
                            : reader.GetString(8),
                        Keywords = indexingLevel == IndexingLevel.Basic || suppressSummary ? [] : keywords,
                        SemanticRepresentation = indexingLevel == IndexingLevel.Basic ? null : semantic,
                        HasIndexingFailure = reader.GetBoolean(16) || !keywordsValid || !semanticValid ||
                            (!reader.IsDBNull(21) && mediaEvidence is null) ||
                            (!reader.IsDBNull(22) && contentIntelligence is null),
                    });
                    if (indexingLevel != IndexingLevel.Basic &&
                        !suppressSemantic &&
                        !reader.IsDBNull(17))
                    {
                        hashes[reader.GetString(0)] = reader.GetString(17);
                    }
                }

                reader.Close();
                var chunks = ReadChunks(connection, hashes.Values.Distinct(StringComparer.Ordinal));
                return documents
                    .Select(document =>
                        hashes.TryGetValue(document.FileId, out var hash) &&
                        chunks.TryGetValue(hash, out var selected)
                            ? document with { SelectedChunks = selected }
                            : document)
                    .ToArray();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetSearchDocumentsByIdsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        var boundedIds = fileIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(RelationshipLimits.MaximumSearchExpansions + 1)
            .ToArray();
        if (boundedIds.Length > RelationshipLimits.MaximumSearchExpansions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileIds),
                $"At most {RelationshipLimits.MaximumSearchExpansions} exact Search document identifiers may be requested.");
        }

        if (boundedIds.Any(id => id.Length > 256 || id.IndexOf('\0') >= 0))
        {
            throw new ArgumentException("Search document identifiers must be bounded valid text.", nameof(fileIds));
        }

        if (boundedIds.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);
        }

        return RunExclusiveAsync<IReadOnlyList<ProgressiveSearchDocument>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                var parameters = new string[boundedIds.Length];
                for (var index = 0; index < boundedIds.Length; index++)
                {
                    parameters[index] = $"$file{index}";
                    command.Parameters.AddWithValue(parameters[index], boundedIds[index]);
                }

                command.CommandText =
                    $$"""
                    SELECT f.id, f.full_path, f.relative_path, f.length, f.modified_utc_ticks,
                           f.fully_indexed, c.extracted_text, c.ocr_text, c.summary,
                           c.keywords_json, c.semantic_json,
                           s.id, s.display_name, s.priority, f.creation_utc_ticks,
                           f.indexing_level,
                           EXISTS(SELECT 1 FROM index_failures x WHERE x.file_id = f.id),
                           f.content_hash,
                           COALESCE(p.suppress_ocr, 0),
                           COALESCE(p.suppress_summary, 0),
                           COALESCE(p.suppress_semantic, 0),
                           m.evidence_json, c.content_intelligence_json
                    FROM index_files f
                    JOIN index_sources s ON s.id = f.source_id
                    LEFT JOIN index_content c ON c.content_hash = f.content_hash
                    LEFT JOIN index_media_content m ON m.content_hash = f.content_hash
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id
                     AND p.relative_path_key = f.relative_path_key
                    WHERE f.deleted_utc_ticks IS NULL
                      AND COALESCE(p.is_excluded, 0) = 0
                      AND f.id IN ({{string.Join(", ", parameters)}})
                    ORDER BY f.id;
                    """;
                using var reader = command.ExecuteReader();
                var documents = new List<ProgressiveSearchDocument>(boundedIds.Length);
                var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    var fullPath = reader.GetString(1);
                    var extension = Path.GetExtension(fullPath).ToLowerInvariant();
                    var suppressOcr = reader.GetBoolean(18);
                    var suppressSummary = reader.GetBoolean(19);
                    var suppressSemantic = reader.GetBoolean(20);
                    var indexingLevel = (IndexingLevel)reader.GetInt32(15);
                    var keywordsValid = true;
                    var keywords = reader.IsDBNull(9)
                        ? []
                        : TryDeserializeStrings(reader.GetString(9), out keywordsValid);
                    var semanticValid = true;
                    var semantic = suppressSemantic || reader.IsDBNull(10)
                        ? null
                        : TryDeserializeFloats(reader.GetString(10), out semanticValid);
                    var mediaEvidence = reader.IsDBNull(21) ? null : TryDeserializeMediaEvidence(reader.GetString(21));
                    var contentIntelligence = reader.IsDBNull(22)
                        ? null
                        : TryDeserializeContentIntelligence(reader.GetString(22));
                    documents.Add(new ProgressiveSearchDocument
                    {
                        FileId = reader.GetString(0),
                        FullPath = fullPath,
                        FileName = Path.GetFileName(fullPath),
                        RelativePath = reader.GetString(2),
                        FolderName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? string.Empty,
                        Extension = extension,
                        FileType = SearchFileTypeClassifier.Classify(extension),
                        SourceId = reader.GetString(11),
                        SourceName = reader.GetString(12),
                        SourcePriority = reader.GetInt32(13),
                        Length = reader.GetInt64(3),
                        CreationTimeUtc = new DateTimeOffset(reader.GetInt64(14), TimeSpan.Zero),
                        ModifiedTimeUtc = new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero),
                        IndexingLevel = indexingLevel,
                        MetadataText = string.Join(' ',
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{Path.GetExtension(fullPath)} {reader.GetInt64(3)} {new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero):O} {reader.GetString(2)}")),
                        IsFullyIndexed = reader.GetBoolean(5),
                        ExtractedText = indexingLevel == IndexingLevel.Basic || reader.IsDBNull(6)
                            ? null
                            : reader.GetString(6),
                        OcrText = indexingLevel == IndexingLevel.Basic || suppressOcr || reader.IsDBNull(7)
                            ? null
                            : reader.GetString(7),
                        MediaEvidence = mediaEvidence,
                        ContentIntelligence = indexingLevel == IndexingLevel.Basic || suppressSummary
                            ? null
                            : contentIntelligence,
                        Summary = indexingLevel == IndexingLevel.Basic || suppressSummary || reader.IsDBNull(8)
                            ? null
                            : reader.GetString(8),
                        Keywords = indexingLevel == IndexingLevel.Basic || suppressSummary ? [] : keywords,
                        SemanticRepresentation = indexingLevel == IndexingLevel.Basic ? null : semantic,
                        HasIndexingFailure = reader.GetBoolean(16) || !keywordsValid || !semanticValid ||
                            (!reader.IsDBNull(21) && mediaEvidence is null) ||
                            (!reader.IsDBNull(22) && contentIntelligence is null),
                    });
                    if (indexingLevel != IndexingLevel.Basic &&
                        !suppressSemantic &&
                        !reader.IsDBNull(17))
                    {
                        hashes[reader.GetString(0)] = reader.GetString(17);
                    }
                }

                reader.Close();
                var chunks = ReadChunks(connection, hashes.Values.Distinct(StringComparer.Ordinal));
                return documents
                    .Select(document =>
                        hashes.TryGetValue(document.FileId, out var hash) &&
                        chunks.TryGetValue(hash, out var selected)
                            ? document with { SelectedChunks = selected }
                            : document)
                    .ToArray();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetExcludedSearchPathsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > MaximumSearchDocuments)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        return RunExclusiveAsync<IReadOnlyList<string>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT s.root_path, p.relative_path
                    FROM index_privacy_rules p
                    JOIN index_sources s ON s.id = p.source_id
                    WHERE p.is_excluded = 1
                    ORDER BY p.source_id, p.relative_path_key
                    LIMIT $maximum;
                    """;
                command.Parameters.AddWithValue("$maximum", maximumCount);
                using var reader = command.ExecuteReader();
                var paths = new List<string>();
                while (reader.Read())
                {
                    var root = reader.GetString(0);
                    var relative = reader.GetString(1);
                    try
                    {
                        var fullPath = _pathSemantics.NormalizeAbsolutePath(
                            Path.Combine(root, relative));
                        if (_pathSemantics.IsWithinRoot(root, fullPath))
                        {
                            paths.Add(fullPath);
                        }
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException or
                        NotSupportedException or
                        PathTooLongException)
                    {
                        // Corrupt exclusion paths remain retained for storage diagnostics but never affect unrelated paths.
                    }
                }

                return Array.AsReadOnly(paths.ToArray());
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexingFailure>> GetFailuresAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > MaximumFailureRecords)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        return RunExclusiveAsync<IReadOnlyList<IndexingFailure>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT x.run_id, f.full_path, x.stage, x.category, x.error_code,
                           x.attempt, x.occurred_utc_ticks, x.can_retry
                    FROM index_failures x
                    JOIN index_files f ON f.id = x.file_id
                    ORDER BY x.occurred_utc_ticks DESC, x.id DESC
                    LIMIT $maximum;
                    """;
                command.Parameters.AddWithValue("$maximum", maximumCount);
                using var reader = command.ExecuteReader();
                var failures = new List<IndexingFailure>();
                while (reader.Read())
                {
                    failures.Add(new IndexingFailure(
                        reader.GetString(0),
                        Path.GetFileName(reader.GetString(1)),
                        (IndexingStage)reader.GetInt32(2),
                        (IndexingFailureCategory)reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.GetInt32(5),
                        new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero),
                        reader.GetBoolean(7)));
                }

                return failures;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexPrivacyItem?> InspectFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return ReadPrivacyItems(connection, "f.id = $value", fileId, 1).SingleOrDefault();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexPrivacyItem>> InspectSourceAsync(
        string sourceId,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (maximumCount is < 1 or > MaximumSearchDocuments)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        return RunExclusiveAsync<IReadOnlyList<IndexPrivacyItem>>(
            () =>
            {
                using var connection = OpenConnection();
                return ReadPrivacyItems(connection, "f.source_id = $value", sourceId, maximumCount);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> ForgetFileAsync(
        string fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var identity = ReadFileIdentity(connection, transaction, fileId);
                if (identity is null)
                {
                    return MissingPrivacyItem();
                }

                UpsertPrivacyRule(
                    connection,
                    transaction,
                    identity,
                    new IndexPrivacyPolicyChange(Excluded: true),
                    changedAtUtc,
                    repairStage: null,
                    forceReprocess: false);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM index_files WHERE id = $file;",
                    ("$file", fileId));
                DeleteOrphanedContent(connection, transaction);
                transaction.Commit();
                return new IndexPrivacyOperationResult(
                    true,
                    identity.SourceId,
                    1,
                    "Indexed data for the file was forgotten and future indexing is excluded. The original file was not changed.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> ForgetSourceAsync(
        string sourceId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var affected = checked((int)ScalarCount(
                    connection,
                    "SELECT COUNT(*) FROM index_files WHERE source_id = $source;",
                    ("$source", sourceId)));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO index_privacy_rules(
                        source_id, relative_path_key, relative_path, is_excluded,
                        indexing_level_override, suppress_ocr, suppress_summary,
                        suppress_semantic, repair_stage, force_reprocess,
                        updated_utc_ticks)
                    SELECT source_id, relative_path_key, relative_path, 1,
                           NULL, 0, 0, 0, NULL, 0, $now
                    FROM index_files
                    WHERE source_id = $source
                    ON CONFLICT(source_id, relative_path_key) DO UPDATE SET
                        relative_path = excluded.relative_path,
                        is_excluded = 1,
                        repair_stage = NULL,
                        force_reprocess = 0,
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$now", changedAtUtc.UtcTicks),
                    ("$source", sourceId));

                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM index_files WHERE source_id = $source;",
                    ("$source", sourceId));
                DeleteOrphanedContent(connection, transaction);
                transaction.Commit();
                return new IndexPrivacyOperationResult(
                    affected > 0,
                    sourceId,
                    affected,
                    affected > 0
                        ? "Indexed files for the source were forgotten and excluded from future indexing. Source files and source ownership were not changed."
                        : "The source has no indexed files to forget.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> SetFilePolicyAsync(
        string fileId,
        IndexPrivacyPolicyChange change,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(change);
        if (change.LevelOverride.HasValue && !Enum.IsDefined(change.LevelOverride.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(change));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var identity = ReadFileIdentity(connection, transaction, fileId);
                if (identity is null)
                {
                    return MissingPrivacyItem();
                }

                UpsertPrivacyRule(
                    connection,
                    transaction,
                    identity,
                    change,
                    changedAtUtc,
                    repairStage: change.LevelOverride.HasValue ? IndexingStage.MetadataIndexed : null,
                    forceReprocess: change.LevelOverride.HasValue);
                if (change.LevelOverride.HasValue)
                {
                    if (change.LevelOverride == IndexingLevel.Basic)
                    {
                        var contentHash = ExecuteScalar(
                            connection,
                            transaction,
                            "SELECT content_hash FROM index_files WHERE id = $file;",
                            ("$file", fileId)) as string;
                        var sharedCount = string.IsNullOrWhiteSpace(contentHash)
                            ? 0
                            : ScalarCount(
                                connection,
                                """
                                SELECT COUNT(*)
                                FROM index_files
                                WHERE content_hash = $hash
                                  AND deleted_utc_ticks IS NULL;
                                """,
                                ("$hash", contentHash));
                        if (sharedCount <= 1)
                        {
                            ClearDataCore(
                                connection,
                                transaction,
                                fileId,
                                contentHash,
                                IndexedDataKind.ExtractedText |
                                IndexedDataKind.OcrText |
                                IndexedDataKind.SummaryAndKeywords |
                                IndexedDataKind.ContentIntelligence |
                                IndexedDataKind.SemanticData |
                                IndexedDataKind.Chunks,
                                changedAtUtc);
                        }
                    }

                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE index_files
                        SET indexing_level = $level, fully_indexed = 0, updated_utc_ticks = $now
                        WHERE id = $file;
                        """,
                        ("$level", (int)change.LevelOverride.Value),
                        ("$now", changedAtUtc.UtcTicks),
                        ("$file", fileId));
                }

                transaction.Commit();
                return new IndexPrivacyOperationResult(
                    true,
                    identity.SourceId,
                    1,
                    "The per-file index policy was updated. The original file was not changed.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> ClearFileDataAsync(
        string fileId,
        IndexedDataKind data,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        if (data == IndexedDataKind.None || (data & ~IndexedDataKind.AllDerived) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var identity = ReadFileIdentity(connection, transaction, fileId);
                if (identity is null)
                {
                    return MissingPrivacyItem();
                }

                var effectiveData = ExpandDependentData(data);
                var contentHash = ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT content_hash FROM index_files WHERE id = $file;",
                    ("$file", fileId)) as string;
                var clearsExtractedText = effectiveData.HasFlag(IndexedDataKind.ExtractedText);
                UpsertPrivacyRule(
                    connection,
                    transaction,
                    identity,
                    new IndexPrivacyPolicyChange(
                        LevelOverride: clearsExtractedText ? IndexingLevel.Basic : null,
                        SuppressOcr:
                            effectiveData.HasFlag(IndexedDataKind.OcrText) || clearsExtractedText
                                ? true
                                : null,
                        SuppressSummary:
                            effectiveData.HasFlag(IndexedDataKind.SummaryAndKeywords) ||
                            clearsExtractedText
                                ? true
                                : null,
                        SuppressSemantic:
                            effectiveData.HasFlag(IndexedDataKind.SemanticData) ||
                            effectiveData.HasFlag(IndexedDataKind.Chunks) ||
                            clearsExtractedText
                                ? true
                                : null),
                    changedAtUtc,
                    repairStage: null,
                    forceReprocess: false);
                if (clearsExtractedText)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "UPDATE index_files SET indexing_level = $level WHERE id = $file;",
                        ("$level", (int)IndexingLevel.Basic),
                        ("$file", fileId));
                }

                var affected = string.IsNullOrWhiteSpace(contentHash)
                    ? 1
                    : checked((int)ScalarCount(
                        connection,
                        "SELECT COUNT(*) FROM index_files WHERE content_hash = $hash AND deleted_utc_ticks IS NULL;",
                        ("$hash", contentHash)));
                ClearDataCore(
                    connection,
                    transaction,
                    fileId,
                    contentHash,
                    effectiveData,
                    changedAtUtc);
                transaction.Commit();
                return new IndexPrivacyOperationResult(
                    true,
                    identity.SourceId,
                    affected,
                    affected > 1
                        ? $"The selected generated data was cleared for {affected} identical-content records that shared it. Original files were not changed."
                        : "The selected generated index data was cleared. The original file was not changed.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> PrepareFileRepairAsync(
        string fileId,
        IndexRepairKind repair,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        if (!Enum.IsDefined(repair))
        {
            throw new ArgumentOutOfRangeException(nameof(repair));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var identity = ReadFileIdentity(connection, transaction, fileId);
                if (identity is null)
                {
                    return MissingPrivacyItem();
                }

                var stage = ResolveRepairStage(connection, transaction, fileId, repair);
                if (!stage.HasValue)
                {
                    return new IndexPrivacyOperationResult(
                        false,
                        identity.SourceId,
                        0,
                        "The indexed record is internally consistent and no repair was queued.");
                }

                PrepareRepairCore(connection, transaction, identity, fileId, stage.Value, changedAtUtc);
                transaction.Commit();
                return new IndexPrivacyOperationResult(
                    true,
                    identity.SourceId,
                    1,
                    $"Selective repair was prepared from {stage.Value}. The original file was not changed.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> PrepareSourceRepairAsync(
        string sourceId,
        IndexRepairKind repair,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (!Enum.IsDefined(repair))
        {
            throw new ArgumentOutOfRangeException(nameof(repair));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var files = ReadFileIdentities(connection, transaction, sourceId);
                var affected = 0;
                foreach (var identity in files)
                {
                    var stage = ResolveRepairStage(connection, transaction, identity.FileId, repair);
                    if (!stage.HasValue)
                    {
                        continue;
                    }

                    PrepareRepairCore(
                        connection,
                        transaction,
                        identity,
                        identity.FileId,
                        stage.Value,
                        changedAtUtc);
                    affected++;
                }

                transaction.Commit();
                return new IndexPrivacyOperationResult(
                    affected > 0,
                    sourceId,
                    affected,
                    affected > 0
                        ? $"Selective source repair was prepared for {affected} indexed file(s). Original files were not changed."
                        : "No indexed records required the selected repair.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IndexMaintenanceResult> MaintainAsync(
        DeepIndexingSettings settings,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                var maximumBytes = settings.MaximumIndexSizeMiB * 1024L * 1024L;
                var actions = new List<IndexMaintenanceAction>();
                var before = GetPhysicalSize();
                using (var transaction = connection.BeginTransaction())
                {
                    var failureCutoff = nowUtc.AddDays(-settings.FailedJobHistoryRetentionDays).UtcTicks;
                    var failureCount = ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM index_failures WHERE occurred_utc_ticks < $cutoff;",
                        ("$cutoff", failureCutoff));
                    if (failureCount > 0)
                    {
                        actions.Add(new IndexMaintenanceAction("expired-failure-history", 0, nowUtc));
                    }

                    var deletedCutoff = nowUtc.AddDays(-settings.DeletedFileRetentionDays).UtcTicks;
                    var deletedCount = ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM index_files WHERE deleted_utc_ticks IS NOT NULL AND deleted_utc_ticks <= $cutoff;",
                        ("$cutoff", deletedCutoff));
                    if (deletedCount > 0)
                    {
                        actions.Add(new IndexMaintenanceAction("expired-deleted-files", 0, nowUtc));
                    }

                    var orphanCount = DeleteOrphanedContent(connection, transaction);
                    if (orphanCount > 0)
                    {
                        actions.Add(new IndexMaintenanceAction("orphaned-derived-content", 0, nowUtc));
                    }

                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM index_maintenance WHERE performed_utc_ticks < $cutoff;",
                        ("$cutoff", nowUtc.AddDays(-30).UtcTicks));
                    transaction.Commit();
                }

                ExecuteNonQuery(connection, null, "PRAGMA wal_checkpoint(TRUNCATE);");
                if (GetPhysicalSize() > maximumBytes)
                {
                    var chunkCount = ExecuteNonQuery(connection, null, "DELETE FROM index_chunks;");
                    if (chunkCount > 0)
                    {
                        actions.Add(new IndexMaintenanceAction("quota-pruned-rebuildable-chunks", 0, nowUtc));
                    }
                }

                ExecuteNonQuery(connection, null, "VACUUM;");
                ExecuteNonQuery(connection, null, "PRAGMA wal_checkpoint(TRUNCATE);");
                var after = GetPhysicalSize();
                var reclaimed = Math.Max(0, before - after);
                if (reclaimed > 0)
                {
                    actions.Add(new IndexMaintenanceAction("database-compaction", reclaimed, nowUtc));
                }

                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var action in actions)
                    {
                        ExecuteNonQuery(
                            connection,
                            transaction,
                            """
                            INSERT INTO index_maintenance(action_code, reclaimed_bytes, performed_utc_ticks)
                            VALUES($code, $bytes, $performed);
                            """,
                            ("$code", action.Code),
                            ("$bytes", action.ReclaimedBytes),
                            ("$performed", action.PerformedAtUtc.UtcTicks));
                    }

                    transaction.Commit();
                }

                var storage = ReadStorageBreakdown(connection, maximumBytes);
                return new IndexMaintenanceResult(actions.AsReadOnly(), storage, storage.DatabaseBytes <= maximumBytes);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task CompactAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                ExecuteNonQuery(connection, null, "PRAGMA wal_checkpoint(TRUNCATE);");
                ExecuteNonQuery(connection, null, "VACUUM;");
                ExecuteNonQuery(connection, null, "PRAGMA wal_checkpoint(TRUNCATE);");
                return 0;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<string> CreateBackupAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return CreateBackupCore(connection, "manual");
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<string?> ResetStorageAsync(
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                var backupDirectory = Path.Combine(
                    Path.GetDirectoryName(_databasePath)
                        ?? throw new InvalidOperationException("The index database has no parent directory."),
                    "backups");
                Directory.CreateDirectory(backupDirectory);
                string? recoveryPath = null;
                if (File.Exists(_databasePath))
                {
                    recoveryPath = Path.Combine(
                        backupDirectory,
                        $"deep-index-recovery-{requestedAtUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
                    File.Move(_databasePath, recoveryPath);
                }

                MoveSidecarToRecovery(_databasePath + "-wal", recoveryPath, ".wal");
                MoveSidecarToRecovery(_databasePath + "-shm", recoveryPath, ".shm");
                using var connection = OpenConnection(configureJournalMode: true);
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionOne);
                ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionTwo);
                ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionThree);
                ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionFour);
                EnsureColumn(connection, transaction, "index_relationship_features", "media_transcript_fingerprint", "TEXT");
                EnsureColumn(connection, transaction, "index_relationship_features", "media_ocr_fingerprint", "TEXT");
                EnsureColumn(connection, transaction, "index_relationship_features", "media_device_key", "TEXT");
                EnsureColumn(connection, transaction, "index_relationship_features", "capture_date_bucket", "INTEGER");
                ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionFourIndexes);
                EnsureColumn(connection, transaction, "index_content", "content_intelligence_json", "TEXT");
                ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionFive);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "INSERT INTO index_meta(key, value) VALUES ('schema_version', $version) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                    ("$version", DeepIndexingVersion.SchemaVersion.ToString(CultureInfo.InvariantCulture)));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    $"PRAGMA user_version = {DeepIndexingVersion.SchemaVersion};");
                transaction.Commit();
                EnsureIntegrity(connection);
                PruneBackups(backupDirectory);
                return recoveryPath;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task RebuildAsync(DateTimeOffset requestedAtUtc, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(connection, transaction, "DELETE FROM index_runs;");
                ExecuteNonQuery(connection, transaction, "DELETE FROM index_chunks;");
                ExecuteNonQuery(connection, transaction, "DELETE FROM index_content;");
                ExecuteNonQuery(connection, transaction, "DELETE FROM index_relationship_features;");
                ExecuteNonQuery(connection, transaction, "DELETE FROM index_relationships WHERE is_manual = 0;");
                CleanupAutomaticCollections(connection, transaction);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE relationship_diagnostics
                    SET last_analysis_utc_ticks = NULL,
                        last_duration_milliseconds = NULL,
                        last_candidate_count = 0,
                        last_relationship_count = 0,
                        last_collection_count = 0;
                    """);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_files
                    SET content_hash = NULL,
                        fully_indexed = 0,
                        deleted_utc_ticks = NULL,
                        last_seen_run_id = NULL,
                        updated_utc_ticks = $now;
                    """,
                    ("$now", requestedAtUtc.UtcTicks));
                transaction.Commit();
                return 0;
            },
            cancellationToken);

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private SqliteConnection OpenConnection(bool configureJournalMode = false)
    {
        var connection = new SqliteConnection(BuildConnectionString());
        try
        {
            connection.Open();
            ExecuteNonQuery(connection, null, "PRAGMA foreign_keys = ON;");
            ExecuteNonQuery(connection, null, "PRAGMA busy_timeout = 5000;");
            ExecuteNonQuery(connection, null, "PRAGMA synchronous = FULL;");
            if (configureJournalMode)
            {
                // WAL is persistent database state. Repeating its mode pragma
                // for every operation can consume a busy deadline and makes
                // file-handle cleanup depend on shared connection-pool state.
                ExecuteNonQuery(connection, null, "PRAGMA journal_mode = WAL;");
                ExecuteNonQuery(connection, null, "PRAGMA wal_autocheckpoint = 1000;");
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private string BuildConnectionString() => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        // Store operations are already serialized by _gate. Unpooled,
        // short-lived connections make disposal and index-only reset release
        // the database deterministically without process-wide pool clearing.
        Pooling = false,
    }.ToString();

    private async Task<T> RunExclusiveAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return operation();
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void QueueObservation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        IndexingSource source,
        IndexingFileObservation observation,
        string processorFingerprint,
        int maximumRetryCount)
    {
        ValidateObservation(source, observation);
        var relativeKey = RelativePathKey(observation.RelativePath);
        var privacy = ReadPrivacyRule(connection, transaction, source.Id, relativeKey);
        if (privacy?.IsExcluded == true)
        {
            return;
        }

        var effectiveLevel = privacy?.LevelOverride ?? source.Level;
        var existing = FindExistingFile(connection, transaction, source.Id, observation);
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var fileId = existing?.Id ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var unchanged = existing is not null &&
            privacy?.RepairStage is null &&
            privacy?.ForceReprocess != true &&
            existing.FullyIndexed &&
            string.Equals(existing.MetadataFingerprint, observation.MetadataFingerprint, StringComparison.Ordinal) &&
            string.Equals(existing.ProcessorFingerprint, processorFingerprint, StringComparison.Ordinal) &&
            existing.Level == effectiveLevel;
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO index_files(
                id, source_id, full_path, path_key, relative_path, relative_path_key,
                stable_identity, file_system_id, length, creation_utc_ticks, modified_utc_ticks,
                attributes, metadata_fingerprint, content_hash, processor_fingerprint,
                indexing_level, fully_indexed, deleted_utc_ticks, last_seen_run_id, updated_utc_ticks)
            VALUES(
                $id, $source, $path, $pathKey, $relative, $relativeKey,
                $identity, $fileSystem, $length, $created, $modified,
                $attributes, $metadata, $contentHash, $processor,
                $level, $fully, NULL, $run, $now)
            ON CONFLICT(id) DO UPDATE SET
                full_path = excluded.full_path,
                path_key = excluded.path_key,
                relative_path = excluded.relative_path,
                relative_path_key = excluded.relative_path_key,
                stable_identity = excluded.stable_identity,
                file_system_id = excluded.file_system_id,
                length = excluded.length,
                creation_utc_ticks = excluded.creation_utc_ticks,
                modified_utc_ticks = excluded.modified_utc_ticks,
                attributes = excluded.attributes,
                metadata_fingerprint = excluded.metadata_fingerprint,
                processor_fingerprint = excluded.processor_fingerprint,
                indexing_level = excluded.indexing_level,
                fully_indexed = $fully,
                deleted_utc_ticks = NULL,
                last_seen_run_id = excluded.last_seen_run_id,
                updated_utc_ticks = excluded.updated_utc_ticks;
            """,
            ("$id", fileId),
            ("$source", source.Id),
            ("$path", observation.FullPath),
            ("$pathKey", PathKey(observation.FullPath)),
            ("$relative", observation.RelativePath),
            ("$relativeKey", relativeKey),
            ("$identity", observation.StableIdentity),
            ("$fileSystem", observation.FileSystemId),
            ("$length", observation.Length),
            ("$created", observation.CreationTimeUtc.UtcTicks),
            ("$modified", observation.LastWriteTimeUtc.UtcTicks),
            ("$attributes", (long)observation.Attributes),
            ("$metadata", observation.MetadataFingerprint),
            ("$contentHash", existing?.ContentHash),
            ("$processor", processorFingerprint),
            ("$level", (int)effectiveLevel),
            ("$fully", unchanged ? 1 : 0),
            ("$run", runId),
            ("$now", now));
        var startingStage = privacy?.RepairStage ??
            (existing is null ? IndexingStage.FileDiscovered : IndexingStage.MetadataIndexed);
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO index_jobs(
                id, run_id, file_id, stage, status, attempt, maximum_retries,
                priority, queued_utc_ticks, completed_utc_ticks)
            VALUES($id, $run, $file, $stage, $status, 0, $maximumRetries, $priority, $now, $completed)
            ON CONFLICT(run_id, file_id) DO UPDATE SET
                maximum_retries = excluded.maximum_retries,
                priority = excluded.priority,
                queued_utc_ticks = MIN(index_jobs.queued_utc_ticks, excluded.queued_utc_ticks);
            """,
            ("$id", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)),
            ("$run", runId),
            ("$file", fileId),
            ("$stage", (int)startingStage),
            ("$status", unchanged ? (int)IndexingStageStatus.Complete : (int)IndexingStageStatus.Queued),
            ("$maximumRetries", maximumRetryCount),
            ("$priority", source.Priority),
            ("$now", now),
            ("$completed", unchanged ? now : null),
            ("$none", (int)IndexingFailureCategory.None));
        if (privacy?.RepairStage is not null)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE index_privacy_rules
                SET repair_stage = NULL, updated_utc_ticks = $now
                WHERE source_id = $source AND relative_path_key = $relative;
                """,
                ("$now", now),
                ("$source", source.Id),
                ("$relative", relativeKey));
        }
    }

    private ExistingFile? FindExistingFile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        IndexingFileObservation observation)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, metadata_fingerprint, processor_fingerprint, indexing_level,
                   fully_indexed, content_hash
            FROM index_files
            WHERE source_id = $source
              AND (
                  (stable_identity IS NOT NULL AND file_system_id IS NOT NULL
                   AND stable_identity = $identity AND file_system_id = $fileSystem)
                  OR path_key = $pathKey
              )
            ORDER BY CASE WHEN stable_identity = $identity AND file_system_id = $fileSystem THEN 0 ELSE 1 END
            LIMIT 1;
            """;
        AddParameters(
            command,
            ("$source", sourceId),
            ("$identity", observation.StableIdentity),
            ("$fileSystem", observation.FileSystemId),
            ("$pathKey", PathKey(observation.FullPath)));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ExistingFile(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                (IndexingLevel)reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5))
            : null;
    }

    private static void SaveDerivedOutput(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IndexingWorkItem work,
        IndexingStageOutput output,
        DateTimeOffset completedAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(output.ContentHash))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE index_files
                SET content_hash = $hash, updated_utc_ticks = $now
                WHERE id = $file;
                """,
                ("$hash", output.ContentHash),
                ("$now", completedAtUtc.UtcTicks),
                ("$file", work.FileId));
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO index_content(content_hash, coverage_level, processor_fingerprint, updated_utc_ticks)
                VALUES($hash, $coverage, $processor, $now)
                ON CONFLICT(content_hash) DO UPDATE SET updated_utc_ticks = excluded.updated_utc_ticks;
                """,
                ("$hash", output.ContentHash),
                ("$coverage", -1),
                ("$processor", work.ProcessorFingerprint),
                ("$now", completedAtUtc.UtcTicks));
        }

        var contentHash = output.ContentHash ?? work.ContentHash;
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return;
        }

        if (output.ExtractedText is not null ||
            output.OcrText is not null ||
            output.Summary is not null ||
            output.Keywords is not null ||
            output.SemanticRepresentation is not null ||
            output.ContentIntelligence is not null)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO index_content(
                    content_hash, extracted_text, ocr_text, summary, keywords_json,
                    semantic_json, content_intelligence_json, coverage_level, processor_fingerprint, updated_utc_ticks)
                VALUES(
                    $hash, $text, $ocr, $summary, $keywords,
                    $semantic, $intelligence, $coverage, $processor, $now)
                ON CONFLICT(content_hash) DO UPDATE SET
                    extracted_text = COALESCE(excluded.extracted_text, index_content.extracted_text),
                    ocr_text = COALESCE(excluded.ocr_text, index_content.ocr_text),
                    summary = COALESCE(excluded.summary, index_content.summary),
                    keywords_json = COALESCE(excluded.keywords_json, index_content.keywords_json),
                    semantic_json = COALESCE(excluded.semantic_json, index_content.semantic_json),
                    content_intelligence_json = COALESCE(excluded.content_intelligence_json, index_content.content_intelligence_json),
                    coverage_level = MAX(index_content.coverage_level, excluded.coverage_level),
                    processor_fingerprint = excluded.processor_fingerprint,
                    updated_utc_ticks = excluded.updated_utc_ticks;
                """,
                ("$hash", contentHash),
                ("$text", output.ExtractedText),
                ("$ocr", output.OcrText),
                ("$summary", output.Summary),
                ("$keywords", output.Keywords is null ? null : JsonSerializer.Serialize(output.Keywords.Take(64))),
                ("$semantic", output.SemanticRepresentation is null ? null : JsonSerializer.Serialize(output.SemanticRepresentation)),
                ("$intelligence", output.ContentIntelligence is null ? null : JsonSerializer.Serialize(output.ContentIntelligence)),
                ("$coverage", work.Stage == IndexingStage.SemanticRepresentationGenerated ? (int)work.Level : -1),
                ("$processor", work.ProcessorFingerprint),
                ("$now", completedAtUtc.UtcTicks));
        }

        if (output.SelectedChunks is not null)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "DELETE FROM index_chunks WHERE content_hash = $hash;",
                ("$hash", contentHash));
            var ordinal = 0;
            foreach (var chunk in output.SelectedChunks)
            {
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO index_chunks(content_hash, ordinal, chunk_text)
                    VALUES($hash, $ordinal, $text);
                    """,
                    ("$hash", contentHash),
                    ("$ordinal", ordinal++),
                    ("$text", chunk));
            }
        }

        if (output.MediaEvidence is not null)
        {
            ValidateMediaEvidence(output.MediaEvidence);
            var mediaJson = JsonSerializer.Serialize(output.MediaEvidence);
            if (mediaJson.Length > 1_048_576)
            {
                throw new InvalidDataException("Structured media evidence exceeds the durable provider bound.");
            }

            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT OR IGNORE INTO index_content(
                    content_hash, coverage_level, processor_fingerprint, updated_utc_ticks)
                VALUES($hash, -1, $processor, $now);
                """,
                ("$hash", contentHash),
                ("$processor", work.ProcessorFingerprint),
                ("$now", completedAtUtc.UtcTicks));

            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO index_media_content(
                    content_hash, media_kind, evidence_json, processing_fingerprint, updated_utc_ticks)
                VALUES($hash, $kind, $evidence, $fingerprint, $now)
                ON CONFLICT(content_hash) DO UPDATE SET
                    media_kind = excluded.media_kind,
                    evidence_json = excluded.evidence_json,
                    processing_fingerprint = excluded.processing_fingerprint,
                    updated_utc_ticks = excluded.updated_utc_ticks;
                """,
                ("$hash", contentHash),
                ("$kind", (int)output.MediaEvidence.Kind),
                ("$evidence", mediaJson),
                ("$fingerprint", output.MediaEvidence.ProcessingFingerprint),
                ("$now", completedAtUtc.UtcTicks));
        }
    }

    private static void QueueNextStage(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IndexingWorkItem work,
        IndexingStage nextStage,
        DateTimeOffset nowUtc)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE index_jobs
            SET stage = $stage,
                status = $queued,
                attempt = 0,
                next_retry_utc_ticks = NULL,
                waiting_dependency = NULL,
                failure_category = $none,
                error_code = NULL,
                queued_utc_ticks = $now,
                started_utc_ticks = NULL,
                completed_utc_ticks = NULL
            WHERE id = $id;
            """,
            ("$stage", (int)nextStage),
            ("$queued", (int)IndexingStageStatus.Queued),
            ("$none", (int)IndexingFailureCategory.None),
            ("$now", nowUtc.UtcTicks),
            ("$id", work.JobId));
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO index_stage_states(file_id, stage, status, attempt, processor_fingerprint)
            VALUES($file, $stage, $queued, 0, $processor)
            ON CONFLICT(file_id, stage) DO UPDATE SET
                status = excluded.status,
                attempt = 0,
                processor_fingerprint = excluded.processor_fingerprint,
                started_utc_ticks = NULL,
                completed_utc_ticks = NULL,
                next_retry_utc_ticks = NULL,
                waiting_dependency = NULL,
                failure_category = $none,
                error_code = NULL,
                duration_milliseconds = NULL;
            """,
            ("$file", work.FileId),
            ("$stage", (int)nextStage),
            ("$queued", (int)IndexingStageStatus.Queued),
            ("$processor", work.ProcessorFingerprint),
            ("$none", (int)IndexingFailureCategory.None));
    }

    private static void EnsureClaimIsCurrent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IndexingWorkItem work)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM index_jobs WHERE id = $id AND stage = $stage AND status = $running;";
        AddParameters(
            command,
            ("$id", work.JobId),
            ("$stage", (int)work.Stage),
            ("$running", (int)IndexingStageStatus.Running));
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException("The indexing stage claim is no longer current.");
        }
    }

    private static int ReadMaximumRetries(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT maximum_retries FROM index_jobs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", jobId);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void InsertFailure(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IndexingWorkItem work,
        IndexingStageOutput output,
        DateTimeOffset occurredAtUtc,
        bool canRetry)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO index_failures(
                run_id, file_id, stage, category, error_code, attempt, occurred_utc_ticks, can_retry)
            VALUES($run, $file, $stage, $category, $error, $attempt, $occurred, $retry);
            """,
            ("$run", work.RunId),
            ("$file", work.FileId),
            ("$stage", (int)work.Stage),
            ("$category", (int)output.FailureCategory),
            ("$error", BoundOrNull(output.ErrorCode, 256)),
            ("$attempt", work.Attempt),
            ("$occurred", occurredAtUtc.UtcTicks),
            ("$retry", canRetry ? 1 : 0));
    }

    private static void UpdateRunCompletion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        DateTimeOffset changedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT discovery_complete,
                   SUM(CASE WHEN j.status NOT IN ($complete, $skipped, $failed, $cancelled) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN j.status = $failed THEN 1 ELSE 0 END),
                   SUM(CASE
                       WHEN j.status IN ($queued, $runningJob) THEN 1
                       WHEN j.status IN ($retry, $waitingJob)
                            AND j.next_retry_utc_ticks IS NOT NULL
                            AND j.next_retry_utc_ticks <= $now THEN 1
                       ELSE 0
                   END)
            FROM index_runs r
            LEFT JOIN index_jobs j ON j.run_id = r.id
            WHERE r.id = $run
            GROUP BY r.id;
            """;
        AddParameters(
            command,
            ("$complete", (int)IndexingStageStatus.Complete),
            ("$skipped", (int)IndexingStageStatus.Skipped),
            ("$failed", (int)IndexingStageStatus.Failed),
            ("$cancelled", (int)IndexingStageStatus.Cancelled),
            ("$queued", (int)IndexingStageStatus.Queued),
            ("$runningJob", (int)IndexingStageStatus.Running),
            ("$retry", (int)IndexingStageStatus.RetryScheduled),
            ("$waitingJob", (int)IndexingStageStatus.WaitingForDependency),
            ("$now", changedAtUtc.UtcTicks),
            ("$run", runId));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return;
        }

        var discoveryComplete = reader.GetBoolean(0);
        var remaining = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
        var failures = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
        var eligible = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
        reader.Close();
        if (!discoveryComplete)
        {
            return;
        }

        if (remaining > 0)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE index_runs
                SET status = $status,
                    updated_utc_ticks = $now
                WHERE id = $run
                  AND status NOT IN ($paused, $cancelling, $cancelled, $failed);
                """,
                ("$status", eligible > 0 ? (int)IndexingRunStatus.Running : (int)IndexingRunStatus.Waiting),
                ("$now", changedAtUtc.UtcTicks),
                ("$run", runId),
                ("$paused", (int)IndexingRunStatus.Paused),
                ("$cancelling", (int)IndexingRunStatus.Cancelling),
                ("$cancelled", (int)IndexingRunStatus.Cancelled),
                ("$failed", (int)IndexingRunStatus.Failed));
            return;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE index_runs
            SET status = $status,
                completed_utc_ticks = $now,
                updated_utc_ticks = $now,
                current_stage = NULL,
                current_file_name = NULL
            WHERE id = $run
              AND status NOT IN ($cancelledRun, $failedRun);
            """,
            ("$status", failures > 0 ? (int)IndexingRunStatus.CompleteWithFailures : (int)IndexingRunStatus.Complete),
            ("$now", changedAtUtc.UtcTicks),
            ("$run", runId),
            ("$cancelledRun", (int)IndexingRunStatus.Cancelled),
            ("$failedRun", (int)IndexingRunStatus.Failed));
    }

    private SearchCoverage ReadCoverage(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*),
                   SUM(CASE WHEN metadata_fingerprint <> '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.indexing_level <> $basic
                                 AND c.extracted_text IS NOT NULL
                                 AND c.extracted_text <> '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.indexing_level <> $basic
                                 AND COALESCE(p.suppress_ocr, 0) = 0
                                 AND c.ocr_text IS NOT NULL
                                 AND c.ocr_text <> '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.indexing_level <> $basic
                                 AND COALESCE(p.suppress_semantic, 0) = 0
                                 AND c.semantic_json IS NOT NULL
                                 AND c.semantic_json <> '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.fully_indexed = 1 THEN 1 ELSE 0 END)
            FROM index_files f
            LEFT JOIN index_content c ON c.content_hash = f.content_hash
            LEFT JOIN index_privacy_rules p
              ON p.source_id = f.source_id
             AND p.relative_path_key = f.relative_path_key
            WHERE f.deleted_utc_ticks IS NULL
              AND COALESCE(p.is_excluded, 0) = 0;
            """;
        command.Parameters.AddWithValue("$basic", (int)IndexingLevel.Basic);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new SearchCoverage(0, 0, 0, 0, 0, 0);
        }

        var coverage = new SearchCoverage(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
            reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
            reader.IsDBNull(5) ? 0 : reader.GetInt64(5));
        reader.Close();
        return coverage with
        {
            ExcludedSourceCount = ScalarCount(
                connection,
                """
                SELECT
                    (SELECT COUNT(*) FROM index_privacy_rules WHERE is_excluded = 1) +
                    (SELECT COUNT(*) FROM index_sources WHERE exclusions_json <> '[]');
                """),
            WaitingForOcrCount = ScalarCount(
                connection,
                "SELECT COUNT(*) FROM index_jobs WHERE status = $status AND waiting_dependency = 'OCR';",
                ("$status", (int)IndexingStageStatus.WaitingForDependency)),
            WaitingForAiCount = ScalarCount(
                connection,
                "SELECT COUNT(*) FROM index_jobs WHERE status = $status AND waiting_dependency = 'local AI';",
                ("$status", (int)IndexingStageStatus.WaitingForDependency)),
            FailedStageCount = ScalarCount(
                connection,
                "SELECT COUNT(*) FROM index_stage_states WHERE status = $status;",
                ("$status", (int)IndexingStageStatus.Failed)),
            IsAvailable = true,
        };
    }

    private IndexStorageBreakdown ReadStorageBreakdown(SqliteConnection connection, long maximumBytes)
    {
        static long ScalarInt64(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        }

        var metadata = ScalarInt64(
            connection,
            "SELECT COALESCE(SUM(LENGTH(full_path) + LENGTH(relative_path) + LENGTH(metadata_fingerprint) + 96), 0) FROM index_files;");
        var extracted = ScalarInt64(connection, "SELECT COALESCE(SUM(LENGTH(extracted_text)), 0) FROM index_content;");
        var ocr = ScalarInt64(connection, "SELECT COALESCE(SUM(LENGTH(ocr_text)), 0) FROM index_content;");
        var media = ScalarInt64(connection, "SELECT COALESCE(SUM(LENGTH(evidence_json)), 0) FROM index_media_content;");
        var contentIntelligence = ScalarInt64(
            connection,
            "SELECT COALESCE(SUM(LENGTH(content_intelligence_json)), 0) FROM index_content;");
        var summaries = ScalarInt64(
            connection,
            "SELECT COALESCE(SUM(LENGTH(summary) + LENGTH(keywords_json)), 0) FROM index_content;");
        var semantic = ScalarInt64(
            connection,
            "SELECT COALESCE(SUM(LENGTH(semantic_json)), 0) FROM index_content;") +
            ScalarInt64(connection, "SELECT COALESCE(SUM(LENGTH(chunk_text)), 0) FROM index_chunks;");
        var relationships = ScalarInt64(
            connection,
            "SELECT COALESCE(SUM(LENGTH(id) + LENGTH(first_file_id) + LENGTH(second_file_id) + LENGTH(algorithm) + LENGTH(algorithm_version) + 64), 0) FROM index_relationships;") +
            ScalarInt64(
                connection,
                "SELECT COALESCE(SUM(LENGTH(evidence_key) + LENGTH(explanation) + 24), 0) FROM index_relationship_evidence;") +
            ScalarInt64(
                connection,
                "SELECT COALESCE(SUM(LENGTH(title) + LENGTH(description) + LENGTH(relationship_summary) + 64), 0) FROM smart_collections;") +
            ScalarInt64(connection, "SELECT COALESCE(COUNT(*) * 64, 0) FROM smart_collection_members;");
        var jobs = ScalarInt64(
            connection,
            "SELECT COALESCE(COUNT(*) * 128, 0) FROM index_jobs;") +
            ScalarInt64(connection, "SELECT COALESCE(COUNT(*) * 128, 0) FROM index_stage_states;") +
            ScalarInt64(connection, "SELECT COALESCE(COUNT(*) * 128, 0) FROM index_failures;");
        var diagnostics = ScalarInt64(connection, "SELECT COALESCE(COUNT(*) * 64, 0) FROM index_maintenance;");
        return new IndexStorageBreakdown(
            metadata,
            extracted,
            ocr,
            summaries,
            semantic,
            relationships,
            jobs,
            diagnostics,
            GetPhysicalSize(),
            maximumBytes)
        {
            MediaDerivedDataBytes = media,
            ContentIntelligenceBytes = contentIntelligence,
        };
    }

    private static IReadOnlyList<IndexPrivacyItem> ReadPrivacyItems(
        SqliteConnection connection,
        string predicate,
        string value,
        int maximumCount)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT f.id, s.id, s.display_name, s.root_path, f.full_path, f.relative_path,
                   f.indexing_level, s.managed_by_watched_folders,
                   LENGTH(f.full_path) + LENGTH(f.relative_path) + LENGTH(f.metadata_fingerprint) + 96,
                   CASE WHEN f.indexing_level = $basic
                        THEN NULL ELSE LENGTH(c.extracted_text) END,
                   CASE WHEN f.indexing_level = $basic OR COALESCE(p.suppress_ocr, 0) = 1
                        THEN NULL ELSE LENGTH(c.ocr_text) END,
                   CASE WHEN f.indexing_level = $basic OR COALESCE(p.suppress_summary, 0) = 1
                        THEN NULL ELSE c.summary END,
                   CASE WHEN f.indexing_level = $basic OR COALESCE(p.suppress_summary, 0) = 1
                        THEN NULL ELSE c.keywords_json END,
                   CASE WHEN f.indexing_level = $basic OR COALESCE(p.suppress_semantic, 0) = 1
                        THEN NULL ELSE c.semantic_json END,
                   CASE WHEN f.indexing_level = $basic OR COALESCE(p.suppress_semantic, 0) = 1
                        THEN 0 ELSE (SELECT COUNT(*) FROM index_chunks k WHERE k.content_hash = f.content_hash) END,
                   (SELECT COUNT(*) FROM index_files shared
                    WHERE shared.content_hash = f.content_hash AND shared.deleted_utc_ticks IS NULL),
                   (SELECT COUNT(*) FROM index_failures x WHERE x.file_id = f.id),
                   (SELECT COUNT(*) FROM index_stage_states st WHERE st.file_id = f.id),
                   f.fully_indexed, f.updated_utc_ticks, f.processor_fingerprint,
                   COALESCE(p.is_excluded, 0), COALESCE(p.suppress_ocr, 0),
                   COALESCE(p.suppress_summary, 0), COALESCE(p.suppress_semantic, 0),
                   COALESCE(p.suppress_relationships, 0),
                   (SELECT COUNT(*) FROM index_relationships r
                    WHERE r.first_file_id = f.id OR r.second_file_id = f.id),
                   (SELECT COUNT(*) FROM smart_collection_members m WHERE m.file_id = f.id)
                   , mc.evidence_json, c.content_intelligence_json
            FROM index_files f
            JOIN index_sources s ON s.id = f.source_id
            LEFT JOIN index_content c ON c.content_hash = f.content_hash
            LEFT JOIN index_media_content mc ON mc.content_hash = f.content_hash
            LEFT JOIN index_privacy_rules p
              ON p.source_id = f.source_id
             AND p.relative_path_key = f.relative_path_key
            WHERE {predicate}
            ORDER BY f.relative_path_key, f.id
            LIMIT $maximum;
            """;
        AddParameters(
            command,
            ("$value", value),
            ("$maximum", maximumCount),
            ("$basic", (int)IndexingLevel.Basic));
        using var reader = command.ExecuteReader();
        var items = new List<IndexPrivacyItem>();
        while (reader.Read())
        {
            var keywords = reader.IsDBNull(12)
                ? []
                : TryDeserializeStrings(reader.GetString(12));
            var media = reader.IsDBNull(28) ? null : TryDeserializeMediaEvidence(reader.GetString(28));
            var contentIntelligence = reader.IsDBNull(29)
                ? null
                : TryDeserializeContentIntelligence(reader.GetString(29));
            items.Add(new IndexPrivacyItem
            {
                FileId = reader.GetString(0),
                SourceId = reader.GetString(1),
                SourceName = reader.GetString(2),
                SourceRootPath = reader.GetString(3),
                FileName = Path.GetFileName(reader.GetString(4)),
                RelativePath = reader.GetString(5),
                IndexingLevel = (IndexingLevel)reader.GetInt32(6),
                ManagedByWatchedFolders = reader.GetBoolean(7),
                MetadataBytes = reader.GetInt64(8),
                ExtractedTextCharacters = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                OcrTextCharacters = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                HasSummary = !reader.IsDBNull(11) && !string.IsNullOrWhiteSpace(reader.GetString(11)),
                KeywordCount = keywords.Count,
                HasSemanticData = !reader.IsDBNull(13) && !string.IsNullOrWhiteSpace(reader.GetString(13)),
                ChunkCount = reader.GetInt32(14),
                SharedContentReferenceCount = reader.GetInt32(15),
                FailureCount = reader.GetInt32(16),
                StageHistoryCount = reader.GetInt32(17),
                HasMediaDerivedData = media is not null,
                MediaKind = media?.Kind.ToString(),
                HasMediaTranscript = !string.IsNullOrWhiteSpace(media?.Transcript),
                HasMediaOcr = !string.IsNullOrWhiteSpace(media?.OcrText),
                HasVisualDescription = !string.IsNullOrWhiteSpace(media?.VisualDescription),
                HasContentIntelligence = contentIntelligence is not null,
                ContentTopicCount = contentIntelligence?.Topics.Count ?? 0,
                ContentEntityCount = contentIntelligence?.Entities.Count ?? 0,
                IsFullyIndexed = reader.GetBoolean(18),
                LastIndexedUtc = new DateTimeOffset(reader.GetInt64(19), TimeSpan.Zero),
                ProcessorVersion = DeepIndexingVersion.ProcessorVersion,
                ProviderName = "Embedded local index",
                IsExcluded = reader.GetBoolean(21),
                OcrSuppressed = reader.GetBoolean(22),
                SummarySuppressed = reader.GetBoolean(23),
                SemanticSuppressed = reader.GetBoolean(24),
                RelationshipAnalysisSuppressed = reader.GetBoolean(25),
                RelationshipCount = reader.GetInt32(26),
                CollectionCount = reader.GetInt32(27),
            });
        }

        return Array.AsReadOnly(items.ToArray());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadChunks(
        SqliteConnection connection,
        IEnumerable<string> contentHashes)
    {
        var hashes = contentHashes.Distinct(StringComparer.Ordinal).ToArray();
        var output = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        const int batchSize = 400;
        for (var offset = 0; offset < hashes.Length; offset += batchSize)
        {
            var batch = hashes.Skip(offset).Take(batchSize).ToArray();
            using var command = connection.CreateCommand();
            var parameterNames = new string[batch.Length];
            for (var index = 0; index < batch.Length; index++)
            {
                parameterNames[index] = $"$hash{index}";
                command.Parameters.AddWithValue(parameterNames[index], batch[index]);
            }

            command.CommandText =
                $"""
                SELECT content_hash, chunk_text
                FROM index_chunks
                WHERE content_hash IN ({string.Join(", ", parameterNames)})
                ORDER BY content_hash, ordinal;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var hash = reader.GetString(0);
                if (!output.TryGetValue(hash, out var chunks))
                {
                    chunks = [];
                    output[hash] = chunks;
                }

                chunks.Add(reader.GetString(1));
            }
        }

        return output.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()),
            StringComparer.Ordinal);
    }

    private static PrivacyRule? ReadPrivacyRule(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        string relativePathKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT is_excluded, indexing_level_override, suppress_ocr, suppress_summary,
                   suppress_semantic, suppress_relationships, repair_stage, force_reprocess
            FROM index_privacy_rules
            WHERE source_id = $source AND relative_path_key = $relative;
            """;
        AddParameters(command, ("$source", sourceId), ("$relative", relativePathKey));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new PrivacyRule(
                reader.GetBoolean(0),
                reader.IsDBNull(1) ? null : (IndexingLevel)reader.GetInt32(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : (IndexingStage)reader.GetInt32(6),
                reader.GetBoolean(7))
            : null;
    }

    private static FileIdentity? ReadFileIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fileId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, source_id, relative_path_key, relative_path
            FROM index_files
            WHERE id = $file;
            """;
        command.Parameters.AddWithValue("$file", fileId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new FileIdentity(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3))
            : null;
    }

    private static IReadOnlyList<FileIdentity> ReadFileIdentities(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, source_id, relative_path_key, relative_path
            FROM index_files
            WHERE source_id = $source
            ORDER BY relative_path_key, id;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        using var reader = command.ExecuteReader();
        var output = new List<FileIdentity>();
        while (reader.Read())
        {
            output.Add(new FileIdentity(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return Array.AsReadOnly(output.ToArray());
    }

    private static void UpsertPrivacyRule(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FileIdentity identity,
        IndexPrivacyPolicyChange change,
        DateTimeOffset changedAtUtc,
        IndexingStage? repairStage,
        bool forceReprocess)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO index_privacy_rules(
                source_id, relative_path_key, relative_path, is_excluded,
                indexing_level_override, suppress_ocr, suppress_summary,
                suppress_semantic, suppress_relationships, repair_stage, force_reprocess, updated_utc_ticks)
            VALUES(
                $source, $relativeKey, $relative, COALESCE($excluded, 0),
                $level, COALESCE($ocr, 0), COALESCE($summary, 0),
                COALESCE($semantic, 0), COALESCE($relationships, 0), $repair, $force, $now)
            ON CONFLICT(source_id, relative_path_key) DO UPDATE SET
                relative_path = excluded.relative_path,
                is_excluded = COALESCE($excluded, index_privacy_rules.is_excluded),
                indexing_level_override = COALESCE($level, index_privacy_rules.indexing_level_override),
                suppress_ocr = COALESCE($ocr, index_privacy_rules.suppress_ocr),
                suppress_summary = COALESCE($summary, index_privacy_rules.suppress_summary),
                suppress_semantic = COALESCE($semantic, index_privacy_rules.suppress_semantic),
                suppress_relationships = COALESCE($relationships, index_privacy_rules.suppress_relationships),
                repair_stage = COALESCE($repair, index_privacy_rules.repair_stage),
                force_reprocess = MAX(index_privacy_rules.force_reprocess, $force),
                updated_utc_ticks = excluded.updated_utc_ticks;
            """,
            ("$source", identity.SourceId),
            ("$relativeKey", identity.RelativePathKey),
            ("$relative", identity.RelativePath),
            ("$excluded", change.Excluded.HasValue ? (change.Excluded.Value ? 1 : 0) : null),
            ("$level", change.LevelOverride.HasValue ? (int)change.LevelOverride.Value : null),
            ("$ocr", change.SuppressOcr.HasValue ? (change.SuppressOcr.Value ? 1 : 0) : null),
            ("$summary", change.SuppressSummary.HasValue ? (change.SuppressSummary.Value ? 1 : 0) : null),
            ("$semantic", change.SuppressSemantic.HasValue ? (change.SuppressSemantic.Value ? 1 : 0) : null),
            ("$relationships", change.SuppressRelationships.HasValue ? (change.SuppressRelationships.Value ? 1 : 0) : null),
            ("$repair", repairStage.HasValue ? (int)repairStage.Value : null),
            ("$force", forceReprocess ? 1 : 0),
            ("$now", changedAtUtc.UtcTicks));
    }

    private static void ClearDataCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fileId,
        string? contentHash,
        IndexedDataKind data,
        DateTimeOffset changedAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(contentHash))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE index_content
                SET extracted_text = CASE WHEN $text = 1 THEN NULL ELSE extracted_text END,
                    ocr_text = CASE WHEN $ocr = 1 THEN NULL ELSE ocr_text END,
                    summary = CASE WHEN $summary = 1 THEN NULL ELSE summary END,
                    keywords_json = CASE WHEN $summary = 1 THEN NULL ELSE keywords_json END,
                    semantic_json = CASE WHEN $semantic = 1 THEN NULL ELSE semantic_json END,
                    content_intelligence_json = CASE WHEN $intelligence = 1 THEN NULL ELSE content_intelligence_json END,
                    coverage_level = CASE
                        WHEN $text = 1 OR $ocr = 1 OR $summary = 1 OR $semantic = 1 OR $intelligence = 1 THEN -1
                        ELSE coverage_level
                    END,
                    updated_utc_ticks = $now
                WHERE content_hash = $hash;
                """,
                ("$text", data.HasFlag(IndexedDataKind.ExtractedText) ? 1 : 0),
                ("$ocr", data.HasFlag(IndexedDataKind.OcrText) ? 1 : 0),
                ("$summary", data.HasFlag(IndexedDataKind.SummaryAndKeywords) ? 1 : 0),
                ("$semantic", data.HasFlag(IndexedDataKind.SemanticData) ? 1 : 0),
                ("$intelligence", data.HasFlag(IndexedDataKind.ContentIntelligence) ? 1 : 0),
                ("$now", changedAtUtc.UtcTicks),
                ("$hash", contentHash));
            if (data.HasFlag(IndexedDataKind.Chunks))
            {
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM index_chunks WHERE content_hash = $hash;",
                    ("$hash", contentHash));
            }

            if (data.HasFlag(IndexedDataKind.MediaDerived))
            {
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM index_media_content WHERE content_hash = $hash;",
                    ("$hash", contentHash));
            }
            else if (data.HasFlag(IndexedDataKind.OcrText))
            {
                ClearMediaOcr(connection, transaction, contentHash, changedAtUtc);
            }
        }

        if (data.HasFlag(IndexedDataKind.ProcessingHistory))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "DELETE FROM index_failures WHERE file_id = $file;",
                ("$file", fileId));
            ExecuteNonQuery(
                connection,
                transaction,
                "DELETE FROM index_stage_states WHERE file_id = $file;",
                ("$file", fileId));
        }

        if (data.HasFlag(IndexedDataKind.Relationships))
        {
            DeleteFileRelationshipData(connection, transaction, fileId, keepManualRelationships: false);
        }
        else if (data.HasFlag(IndexedDataKind.ExtractedText) ||
                 data.HasFlag(IndexedDataKind.OcrText) ||
                 data.HasFlag(IndexedDataKind.SummaryAndKeywords) ||
                 data.HasFlag(IndexedDataKind.SemanticData) ||
                 data.HasFlag(IndexedDataKind.MediaDerived) ||
                 data.HasFlag(IndexedDataKind.ContentIntelligence))
        {
            // Automatic relationships are derived from these fields and cannot
            // outlive forgotten evidence. Explicit manual links and overrides
            // are user authority and are preserved.
            DeleteFileRelationshipData(connection, transaction, fileId, keepManualRelationships: true);
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE index_files SET updated_utc_ticks = $now WHERE id = $file;",
            ("$now", changedAtUtc.UtcTicks),
            ("$file", fileId));
        DeleteOrphanedContentStatic(connection, transaction);
    }

    private static IndexedDataKind ExpandDependentData(IndexedDataKind data)
    {
        if (data.HasFlag(IndexedDataKind.ExtractedText))
        {
            data |= IndexedDataKind.OcrText |
                IndexedDataKind.SummaryAndKeywords |
                IndexedDataKind.ContentIntelligence |
                IndexedDataKind.SemanticData |
                IndexedDataKind.Chunks;
        }

        if (data.HasFlag(IndexedDataKind.OcrText))
        {
            data |= IndexedDataKind.SummaryAndKeywords |
                IndexedDataKind.ContentIntelligence |
                IndexedDataKind.SemanticData |
                IndexedDataKind.Chunks;
        }

        if (data.HasFlag(IndexedDataKind.SummaryAndKeywords))
        {
            data |= IndexedDataKind.ContentIntelligence | IndexedDataKind.SemanticData | IndexedDataKind.Chunks;
        }

        if (data.HasFlag(IndexedDataKind.MediaDerived))
        {
            data |= IndexedDataKind.ContentIntelligence | IndexedDataKind.SemanticData | IndexedDataKind.Chunks;
        }

        if (data.HasFlag(IndexedDataKind.ContentIntelligence))
        {
            data |= IndexedDataKind.SemanticData | IndexedDataKind.Chunks;
        }

        return data;
    }

    private static void ClearMediaOcr(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contentHash,
        DateTimeOffset changedAtUtc)
    {
        var json = ExecuteScalar(
            connection,
            transaction,
            "SELECT evidence_json FROM index_media_content WHERE content_hash = $hash;",
            ("$hash", contentHash)) as string;
        var evidence = string.IsNullOrWhiteSpace(json) ? null : TryDeserializeMediaEvidence(json);
        if (evidence is null || string.IsNullOrWhiteSpace(evidence.OcrText))
        {
            return;
        }

        var updated = evidence with { OcrText = null, OcrFrameCount = 0 };
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE index_media_content SET evidence_json = $evidence, updated_utc_ticks = $now WHERE content_hash = $hash;",
            ("$evidence", JsonSerializer.Serialize(updated)),
            ("$now", changedAtUtc.UtcTicks),
            ("$hash", contentHash));
    }

    private static IndexingStage? ResolveRepairStage(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fileId,
        IndexRepairKind repair)
    {
        if (repair == IndexRepairKind.RetryFailedStage)
        {
            var value = ExecuteScalar(
                connection,
                transaction,
                """
                SELECT MIN(stage)
                FROM index_stage_states
                WHERE file_id = $file
                  AND status IN ($failed, $waiting, $retry, $cancelled);
                """,
                ("$file", fileId),
                ("$failed", (int)IndexingStageStatus.Failed),
                ("$waiting", (int)IndexingStageStatus.WaitingForDependency),
                ("$retry", (int)IndexingStageStatus.RetryScheduled),
                ("$cancelled", (int)IndexingStageStatus.Cancelled));
            return value is null or DBNull
                ? null
                : (IndexingStage)Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        if (repair == IndexRepairKind.Verify)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT f.metadata_fingerprint, f.indexing_level, f.fully_indexed,
                       f.content_hash, c.extracted_text
                FROM index_files f
                LEFT JOIN index_content c ON c.content_hash = f.content_hash
                WHERE f.id = $file;
                """;
            command.Parameters.AddWithValue("$file", fileId);
            using var reader = command.ExecuteReader();
            if (!reader.Read() || string.IsNullOrWhiteSpace(reader.GetString(0)))
            {
                return IndexingStage.MetadataIndexed;
            }

            var level = (IndexingLevel)reader.GetInt32(1);
            var fullyIndexed = reader.GetBoolean(2);
            if (fullyIndexed && level != IndexingLevel.Basic && reader.IsDBNull(3))
            {
                return IndexingStage.ContentFingerprinted;
            }

            if (fullyIndexed &&
                level != IndexingLevel.Basic &&
                (reader.IsDBNull(4) || string.IsNullOrWhiteSpace(reader.GetString(4))))
            {
                return IndexingStage.TextExtracted;
            }

            return null;
        }

        return repair switch
        {
            IndexRepairKind.Rebuild => IndexingStage.FileDiscovered,
            IndexRepairKind.RefreshMetadata => IndexingStage.MetadataIndexed,
            IndexRepairKind.RefreshText => IndexingStage.TextExtracted,
            IndexRepairKind.RefreshOcr => IndexingStage.OcrProcessed,
            IndexRepairKind.RegenerateSummaryAndKeywords => IndexingStage.SummaryKeywordsGenerated,
            IndexRepairKind.RegenerateSemanticData => IndexingStage.SemanticRepresentationGenerated,
            _ => null,
        };
    }

    private static void PrepareRepairCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FileIdentity identity,
        string fileId,
        IndexingStage stage,
        DateTimeOffset changedAtUtc)
    {
        var clear = stage switch
        {
            <= IndexingStage.ContentFingerprinted => IndexedDataKind.AllDerived,
            IndexingStage.TextExtracted =>
                IndexedDataKind.ExtractedText |
                IndexedDataKind.OcrText |
                IndexedDataKind.SummaryAndKeywords |
                IndexedDataKind.ContentIntelligence |
                IndexedDataKind.SemanticData |
                IndexedDataKind.Chunks,
            IndexingStage.OcrProcessed =>
                IndexedDataKind.OcrText |
                IndexedDataKind.SummaryAndKeywords |
                IndexedDataKind.ContentIntelligence |
                IndexedDataKind.SemanticData |
                IndexedDataKind.Chunks,
            IndexingStage.SummaryKeywordsGenerated =>
                IndexedDataKind.SummaryAndKeywords |
                IndexedDataKind.ContentIntelligence |
                IndexedDataKind.SemanticData |
                IndexedDataKind.Chunks,
            IndexingStage.SemanticRepresentationGenerated =>
                IndexedDataKind.SemanticData | IndexedDataKind.Chunks,
            _ => IndexedDataKind.ProcessingHistory,
        };
        var contentHash = ExecuteScalar(
            connection,
            transaction,
            "SELECT content_hash FROM index_files WHERE id = $file;",
            ("$file", fileId)) as string;
        ClearDataCore(connection, transaction, fileId, contentHash, clear, changedAtUtc);
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM index_stage_states WHERE file_id = $file AND stage >= $stage;",
            ("$file", fileId),
            ("$stage", (int)stage));
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM index_failures WHERE file_id = $file AND stage >= $stage;",
            ("$file", fileId),
            ("$stage", (int)stage));
        UpsertPrivacyRule(
            connection,
            transaction,
            identity,
            new IndexPrivacyPolicyChange(
                Excluded: false,
                SuppressOcr: stage <= IndexingStage.OcrProcessed ? false : null,
                SuppressSummary: stage <= IndexingStage.SummaryKeywordsGenerated ? false : null,
                SuppressSemantic: stage <= IndexingStage.SemanticRepresentationGenerated ? false : null),
            changedAtUtc,
            stage,
            forceReprocess: true);
    }

    private static long ScalarCount(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters) =>
        Convert.ToInt64(ExecuteScalar(connection, sql, parameters) ?? 0, CultureInfo.InvariantCulture);

    private static IndexPrivacyOperationResult MissingPrivacyItem() => new(
        false,
        null,
        0,
        "The selected indexed file no longer exists.");

    private int DeleteOrphanedContent(SqliteConnection connection, SqliteTransaction transaction) =>
        DeleteOrphanedContentStatic(connection, transaction);

    private static int DeleteOrphanedContentStatic(
        SqliteConnection connection,
        SqliteTransaction transaction) =>
        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM index_content
            WHERE NOT EXISTS (
                SELECT 1 FROM index_files f WHERE f.content_hash = index_content.content_hash
            );
            """);

    private long GetPhysicalSize()
    {
        static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
        return Length(_databasePath) + Length(_databasePath + "-wal") + Length(_databasePath + "-shm");
    }

    private static void MoveSidecarToRecovery(
        string sidecarPath,
        string? recoveryPath,
        string recoverySuffix)
    {
        if (!File.Exists(sidecarPath))
        {
            return;
        }

        if (recoveryPath is null)
        {
            File.Delete(sidecarPath);
            return;
        }

        File.Move(sidecarPath, recoveryPath + recoverySuffix);
    }

    private string CreateBackupCore(SqliteConnection source, string reason)
    {
        var backupDirectory = Path.Combine(
            Path.GetDirectoryName(_databasePath) ?? throw new InvalidOperationException("The index database has no parent directory."),
            "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"deep-index-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Bound(reason, 32)}-{Guid.NewGuid():N}.db");
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString()))
        {
            destination.Open();
            source.BackupDatabase(destination);
            destination.Close();
            SqliteConnection.ClearPool(destination);
        }

        PruneBackups(backupDirectory);
        return backupPath;
    }

    private static void PruneBackups(string backupDirectory)
    {
        foreach (var stale in new DirectoryInfo(backupDirectory)
                     .EnumerateFiles("deep-index-*.db", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                     .Skip(3))
        {
            File.Delete(stale.FullName + ".wal");
            File.Delete(stale.FullName + ".shm");
            stale.Delete();
        }
    }

    private static bool HasUserTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static void EnsureIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeepIndexCorruptException(
                "The background index failed its integrity check. Source files were not changed. Restore a reviewed backup or rebuild the derived index.");
        }
    }

    private IndexingSource ReadRunSource(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT s.id, s.root_path, s.display_name, s.indexing_level, s.include_subfolders,
                   s.enabled, s.priority, s.exclusions_json, s.managed_by_watched_folders
            FROM index_runs r
            JOIN index_sources s ON s.id = r.source_id
            WHERE r.id = $run;
            """;
        command.Parameters.AddWithValue("$run", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("The indexing run does not exist.");
        }

        return new IndexingSource(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            (IndexingLevel)reader.GetInt32(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetInt32(6),
            DeserializeStrings(reader.GetString(7)),
            reader.GetBoolean(8));
    }

    private void ValidateObservation(IndexingSource source, IndexingFileObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!_pathSemantics.IsWithinRoot(source.RootPath, observation.FullPath) ||
            Path.IsPathRooted(observation.RelativePath) ||
            observation.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment == "..") ||
            observation.Length < 0 ||
            string.IsNullOrWhiteSpace(observation.MetadataFingerprint))
        {
            throw new ArgumentException("The discovered file is outside its source or has invalid metadata.", nameof(observation));
        }
    }

    private void ValidateSource(IndexingSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Id) ||
            string.IsNullOrWhiteSpace(source.DisplayName) ||
            !Path.IsPathFullyQualified(source.RootPath) ||
            !Enum.IsDefined(source.Level) ||
            source.Exclusions.Count > 128 ||
            source.Exclusions.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512))
        {
            throw new ArgumentException("The indexing source is invalid.", nameof(source));
        }
    }

    private string PathKey(string path)
    {
        var normalized = _pathSemantics.NormalizeAbsolutePath(path);
        return _pathSemantics.IsCaseSensitive ? normalized : normalized.ToUpperInvariant();
    }

    private string RelativePathKey(string relativePath)
    {
        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return _pathSemantics.IsCaseSensitive ? normalized : normalized.ToUpperInvariant();
    }

    private static int ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        return command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        return command.ExecuteScalar();
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string declaration)
    {
        var exists = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = $column;",
                ("$column", columnName)),
            CultureInfo.InvariantCulture) > 0;
        if (!exists)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                $"ALTER TABLE {tableName} ADD COLUMN {columnName} {declaration};");
        }
    }

    private static object? ExecuteScalar(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        return command.ExecuteScalar();
    }

    private static void AddParameters(SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static IReadOnlyList<string> DeserializeStrings(string value) =>
        JsonSerializer.Deserialize<string[]>(value) ?? [];

    private static IReadOnlyList<string> TryDeserializeStrings(string value) =>
        TryDeserializeStrings(value, out _);

    private static IReadOnlyList<string> TryDeserializeStrings(string value, out bool isValid)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(value);
            if (values is null ||
                values.Length > 256 ||
                values.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 1024))
            {
                isValid = false;
                return [];
            }

            isValid = true;
            return Array.AsReadOnly(values);
        }
        catch (JsonException)
        {
            isValid = false;
            return [];
        }
    }

    private static IReadOnlyList<float>? TryDeserializeFloats(string value, out bool isValid)
    {
        try
        {
            var values = JsonSerializer.Deserialize<float[]>(value);
            if (values is null ||
                values.Length is 0 or > 4096 ||
                values.Any(item => !float.IsFinite(item)))
            {
                isValid = false;
                return null;
            }

            isValid = true;
            return Array.AsReadOnly(values);
        }
        catch (JsonException)
        {
            isValid = false;
            return null;
        }
    }

    private static IndexedMediaEvidence? TryDeserializeMediaEvidence(string value)
    {
        try
        {
            if (value.Length > 1_048_576)
            {
                return null;
            }

            var evidence = JsonSerializer.Deserialize<IndexedMediaEvidence>(value);
            ValidateMediaEvidence(evidence);
            return evidence;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private static IndexedContentIntelligence? TryDeserializeContentIntelligence(string value)
    {
        try
        {
            if (value.Length > 262_144)
            {
                return null;
            }

            var intelligence = JsonSerializer.Deserialize<IndexedContentIntelligence>(value);
            if (intelligence is null ||
                intelligence.Topics is null ||
                intelligence.Entities is null ||
                intelligence.Keywords is null ||
                intelligence.Topics.Count > 64 ||
                intelligence.Entities.Count > 64 ||
                intelligence.Keywords.Count > 128 ||
                intelligence.Keywords.Any(keyword => !IsBoundedContentText(keyword, 256)) ||
                !IsBoundedContentText(intelligence.Provider, 128) ||
                !IsBoundedContentText(intelligence.ProviderVersion, 128) ||
                !IsBoundedContentText(intelligence.ProcessingFingerprint, 128) ||
                intelligence.Topics.Concat(intelligence.Entities).Any(concept => !IsValidContentConcept(concept)) ||
                intelligence.Summary is { } summary && !IsValidContentSummary(summary))
            {
                return null;
            }

            return intelligence;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsValidContentConcept(ContentConcept? concept) =>
        concept is not null &&
        Enum.IsDefined(concept.Kind) &&
        Enum.IsDefined(concept.Confidence) &&
        Enum.IsDefined(concept.Origin) &&
        IsBoundedContentText(concept.DisplayName, 256) &&
        IsBoundedContentText(concept.NormalizedValue, 256) &&
        IsBoundedContentText(concept.Provider, 128) &&
        IsBoundedContentText(concept.ProviderVersion, 128) &&
        concept.Evidence is not null &&
        concept.Evidence.Count <= 8 &&
        concept.Evidence.All(IsValidContentEvidence);

    private static bool IsValidContentSummary(ContentSummaryEvidence? summary) =>
        summary is not null &&
        IsBoundedContentText(summary.Text, 4_096) &&
        IsBoundedContentText(summary.Provider, 128) &&
        IsBoundedContentText(summary.ProviderVersion, 128) &&
        Enum.IsDefined(summary.Origin) &&
        summary.Evidence is not null &&
        summary.Evidence.Count <= 8 &&
        summary.Evidence.All(IsValidContentEvidence);

    private static bool IsValidContentEvidence(ContentEvidenceReference? evidence) =>
        evidence is not null &&
        Enum.IsDefined(evidence.Source) &&
        IsBoundedContentText(evidence.EvidenceKey, 128) &&
        IsBoundedContentText(evidence.Excerpt, 1_024);

    private static bool IsBoundedContentText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static void ValidateMediaEvidence(IndexedMediaEvidence? evidence)
    {
        if (evidence is null ||
            evidence.Kind is MediaKind.None ||
            evidence.Metadata.Kind != evidence.Kind ||
            string.IsNullOrWhiteSpace(evidence.ProcessingFingerprint) ||
            evidence.ProcessingFingerprint.Length > 128 ||
            evidence.Transcript?.Length > 1_048_576 ||
            evidence.OcrText?.Length > 262_144 ||
            evidence.VisualDescription?.Length > 8_192 ||
            evidence.TranscriptSegments.Count > 512 ||
            evidence.VisualTags.Count > 64 ||
            evidence.SampledFrameCount is < 0 or > 64 ||
            evidence.OcrFrameCount is < 0 or > 32 ||
            evidence.Metadata.Width is < 1 or > 1_000_000 ||
            evidence.Metadata.Height is < 1 or > 1_000_000 ||
            evidence.Metadata.Duration is { } duration &&
                (duration < TimeSpan.Zero || duration > TimeSpan.FromDays(7)) ||
            evidence.Metadata.FrameRate is { } frameRate && (!double.IsFinite(frameRate) || frameRate is <= 0 or > 1_000) ||
            evidence.Metadata.BitRate is < 0 or > 100_000_000_000 ||
            evidence.Metadata.SampleRate is < 1 or > 768_000 ||
            evidence.Metadata.Channels is < 1 or > 64 ||
            evidence.Metadata.Orientation is < 1 or > 8 ||
            evidence.Metadata.Latitude is { } latitude && (!double.IsFinite(latitude) || latitude is < -90 or > 90) ||
            evidence.Metadata.Longitude is { } longitude && (!double.IsFinite(longitude) || longitude is < -180 or > 180) ||
            evidence.Metadata.TextFields.Count > 32 ||
            evidence.Metadata.TextFields.Any(field =>
                !IsBoundedMediaText(field.Key, 128) || !IsBoundedMediaText(field.Value, 512)) ||
            !IsBoundedMediaText(evidence.MetadataProvider, 128) ||
            !IsBoundedMediaText(evidence.MetadataProviderVersion, 128) ||
            !IsOptionalBoundedMediaText(evidence.TranscriptionProvider, 128) ||
            !IsOptionalBoundedMediaText(evidence.DescriptionProvider, 128) ||
            evidence.TranscriptSegments.Any(segment =>
                segment.Start < TimeSpan.Zero ||
                segment.End < segment.Start ||
                segment.End > TimeSpan.FromDays(7) ||
                !IsBoundedMediaText(segment.Text, 2_048)) ||
            evidence.VisualTags.Any(tag => !IsBoundedMediaText(tag, 64)) ||
            evidence.Warnings.Count > 16 ||
            evidence.Warnings.Any(warning => !IsBoundedMediaText(warning, 256)))
        {
            throw new InvalidDataException("Stored media evidence is malformed or outside supported bounds.");
        }
    }

    private static bool IsBoundedMediaText(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t');

    private static bool IsOptionalBoundedMediaText(string? value, int maximumLength) =>
        value is null || IsBoundedMediaText(value, maximumLength);

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? BoundOrNull(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value.Trim(), maximumLength);

    private sealed record ExistingFile(
        string Id,
        string MetadataFingerprint,
        string ProcessorFingerprint,
        IndexingLevel Level,
        bool FullyIndexed,
        string? ContentHash);

    private sealed record FileIdentity(
        string FileId,
        string SourceId,
        string RelativePathKey,
        string RelativePath);

    private sealed record PrivacyRule(
        bool IsExcluded,
        IndexingLevel? LevelOverride,
        bool SuppressOcr,
        bool SuppressSummary,
        bool SuppressSemantic,
        bool SuppressRelationships,
        IndexingStage? RepairStage,
        bool ForceReprocess);
}
