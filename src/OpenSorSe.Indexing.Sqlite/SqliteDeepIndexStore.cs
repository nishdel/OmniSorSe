using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Indexing.Sqlite;

/// <summary>
/// Implements the provider-independent durable indexing store with an application-owned SQLite database.
/// </summary>
public sealed class SqliteDeepIndexStore : IDeepIndexStore, IDisposable
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
                    using var connection = OpenConnection();
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
                        ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionOne);
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
                           f.content_hash, c.extracted_text, c.ocr_text
                    FROM index_jobs j
                    JOIN index_runs r ON r.id = j.run_id
                    JOIN index_sources s ON s.id = r.source_id
                    JOIN index_files f ON f.id = j.file_id
                    LEFT JOIN index_content c ON c.content_hash = f.content_hash
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
                           c.keywords_json, c.semantic_json
                    FROM index_files f
                    LEFT JOIN index_content c ON c.content_hash = f.content_hash
                    WHERE f.deleted_utc_ticks IS NULL
                    ORDER BY f.relative_path_key, f.id
                    LIMIT $maximum;
                    """;
                command.Parameters.AddWithValue("$maximum", maximumCount);
                using var reader = command.ExecuteReader();
                var documents = new List<ProgressiveSearchDocument>();
                while (reader.Read())
                {
                    var fullPath = reader.GetString(1);
                    documents.Add(new ProgressiveSearchDocument
                    {
                        FileId = reader.GetString(0),
                        FullPath = fullPath,
                        FileName = Path.GetFileName(fullPath),
                        FolderName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? string.Empty,
                        MetadataText = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{Path.GetExtension(fullPath)} {reader.GetInt64(3)} {new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero):O} {reader.GetString(2)}"),
                        IsFullyIndexed = reader.GetBoolean(5),
                        ExtractedText = reader.IsDBNull(6) ? null : reader.GetString(6),
                        OcrText = reader.IsDBNull(7) ? null : reader.GetString(7),
                        Summary = reader.IsDBNull(8) ? null : reader.GetString(8),
                        Tags = reader.IsDBNull(9) ? [] : DeserializeStrings(reader.GetString(9)),
                        SemanticRepresentation = reader.IsDBNull(10) ? null : DeserializeFloats(reader.GetString(10)),
                    });
                }

                return documents;
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
                SqliteConnection.ClearAllPools();
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
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(connection, transaction, SqliteDeepIndexSchema.CreateVersionOne);
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
            SqliteConnection.ClearPool(new SqliteConnection(BuildConnectionString()));
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(BuildConnectionString());
        try
        {
            connection.Open();
            ExecuteNonQuery(connection, null, "PRAGMA foreign_keys = ON;");
            ExecuteNonQuery(connection, null, "PRAGMA busy_timeout = 5000;");
            ExecuteNonQuery(connection, null, "PRAGMA journal_mode = WAL;");
            ExecuteNonQuery(connection, null, "PRAGMA synchronous = FULL;");
            ExecuteNonQuery(connection, null, "PRAGMA wal_autocheckpoint = 1000;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            SqliteConnection.ClearPool(connection);
            throw;
        }
    }

    private string BuildConnectionString() => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true,
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
        var existing = FindExistingFile(connection, transaction, source.Id, observation);
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var fileId = existing?.Id ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var unchanged = existing is not null &&
            existing.FullyIndexed &&
            string.Equals(existing.MetadataFingerprint, observation.MetadataFingerprint, StringComparison.Ordinal) &&
            string.Equals(existing.ProcessorFingerprint, processorFingerprint, StringComparison.Ordinal) &&
            existing.Level == source.Level;
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
            ("$relativeKey", RelativePathKey(observation.RelativePath)),
            ("$identity", observation.StableIdentity),
            ("$fileSystem", observation.FileSystemId),
            ("$length", observation.Length),
            ("$created", observation.CreationTimeUtc.UtcTicks),
            ("$modified", observation.LastWriteTimeUtc.UtcTicks),
            ("$attributes", (long)observation.Attributes),
            ("$metadata", observation.MetadataFingerprint),
            ("$contentHash", existing?.ContentHash),
            ("$processor", processorFingerprint),
            ("$level", (int)source.Level),
            ("$fully", unchanged ? 1 : 0),
            ("$run", runId),
            ("$now", now));
        var startingStage = existing is null ? IndexingStage.FileDiscovered : IndexingStage.MetadataIndexed;
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
            output.SemanticRepresentation is not null)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO index_content(
                    content_hash, extracted_text, ocr_text, summary, keywords_json,
                    semantic_json, coverage_level, processor_fingerprint, updated_utc_ticks)
                VALUES(
                    $hash, $text, $ocr, $summary, $keywords,
                    $semantic, $coverage, $processor, $now)
                ON CONFLICT(content_hash) DO UPDATE SET
                    extracted_text = COALESCE(excluded.extracted_text, index_content.extracted_text),
                    ocr_text = COALESCE(excluded.ocr_text, index_content.ocr_text),
                    summary = COALESCE(excluded.summary, index_content.summary),
                    keywords_json = COALESCE(excluded.keywords_json, index_content.keywords_json),
                    semantic_json = COALESCE(excluded.semantic_json, index_content.semantic_json),
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
                   SUM(CASE WHEN c.extracted_text IS NOT NULL AND c.extracted_text <> '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN c.ocr_text IS NOT NULL AND c.ocr_text <> '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN c.semantic_json IS NOT NULL AND c.semantic_json <> '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.fully_indexed = 1 THEN 1 ELSE 0 END)
            FROM index_files f
            LEFT JOIN index_content c ON c.content_hash = f.content_hash
            WHERE f.deleted_utc_ticks IS NULL;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new SearchCoverage(0, 0, 0, 0, 0, 0);
        }

        return new SearchCoverage(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
            reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
            reader.IsDBNull(5) ? 0 : reader.GetInt64(5));
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
        var summaries = ScalarInt64(
            connection,
            "SELECT COALESCE(SUM(LENGTH(summary) + LENGTH(keywords_json)), 0) FROM index_content;");
        var semantic = ScalarInt64(
            connection,
            "SELECT COALESCE(SUM(LENGTH(semantic_json)), 0) FROM index_content;") +
            ScalarInt64(connection, "SELECT COALESCE(SUM(LENGTH(chunk_text)), 0) FROM index_chunks;");
        var relationships = ScalarInt64(
            connection,
            "SELECT COALESCE(COUNT(*) * 32, 0) FROM index_files WHERE content_hash IS NOT NULL;");
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
            maximumBytes);
    }

    private int DeleteOrphanedContent(SqliteConnection connection, SqliteTransaction transaction) =>
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

    private static void AddParameters(SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static IReadOnlyList<string> DeserializeStrings(string value) =>
        JsonSerializer.Deserialize<string[]>(value) ?? [];

    private static IReadOnlyList<float> DeserializeFloats(string value) =>
        JsonSerializer.Deserialize<float[]>(value) ?? [];

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
}
