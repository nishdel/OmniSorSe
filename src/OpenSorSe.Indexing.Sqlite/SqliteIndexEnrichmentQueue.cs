using OpenSorSe.Application.Indexing;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Indexing.Sqlite;

public sealed partial class SqliteDeepIndexStore
{
    /// <inheritdoc />
    public Task<int> QueueRetainedEnrichmentAsync(string sourceId, DateTimeOffset queuedAtUtc,
        int maximumRetries, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (maximumRetries is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRetries));
        }

        return RunExclusiveAsync(() =>
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var prior = connection.CreateCommand();
            prior.Transaction = transaction;
            prior.CommandText = """
                SELECT id, discovery_complete FROM index_runs WHERE source_id = $source
                ORDER BY started_utc_ticks DESC, id DESC LIMIT 1;
                """;
            prior.Parameters.AddWithValue("$source", sourceId);
            using var reader = prior.ExecuteReader();
            if (!reader.Read())
            {
                return 0;
            }

            var previousRun = reader.GetString(0);
            if (!reader.GetBoolean(1))
            {
                throw new InvalidOperationException("Wait for folder discovery to finish before changing AI enrichment.");
            }

            reader.Close();
            var runId = Guid.NewGuid().ToString("N");
            ExecuteNonQuery(connection, transaction, """
                INSERT INTO index_runs(id, source_id, status, started_utc_ticks, updated_utc_ticks, discovery_complete)
                SELECT $run, id, $running, $now, $now, 1 FROM index_sources WHERE id = $source AND enabled = 1;
                """, ("$run", runId), ("$source", sourceId), ("$running", (int)IndexingRunStatus.Running), ("$now", queuedAtUtc.UtcTicks));

            // Retained native/OCR evidence goes straight to enrichment. Incomplete deterministic work
            // keeps its earlier stage. Privacy exclusions and user-owned tag/relationship tables are untouched.
            var count = ExecuteNonQuery(connection, transaction, """
                INSERT INTO index_jobs(id, run_id, file_id, stage, status, maximum_retries, queued_utc_ticks)
                SELECT lower(hex(randomblob(16))), $run, f.id,
                       CASE WHEN old.stage < $summary AND old.status NOT IN ($complete, $skipped)
                            THEN old.stage
                            WHEN f.indexing_level = $basic OR COALESCE(p.suppress_summary, 0) = 1 THEN old.stage
                            ELSE $summary END,
                       $queued, $retries, $now
                FROM index_files f
                JOIN index_sources s ON s.id = f.source_id
                LEFT JOIN index_content c ON c.content_hash = f.content_hash
                LEFT JOIN index_jobs old ON old.run_id = $previous AND old.file_id = f.id
                LEFT JOIN index_privacy_rules p ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
                WHERE f.source_id = $source AND s.enabled = 1 AND f.deleted_utc_ticks IS NULL
                  AND COALESCE(p.is_excluded, 0) = 0
                  AND ((s.ai_enrichment_enabled = 1 AND f.indexing_level <> $basic AND COALESCE(p.suppress_summary, 0) = 0
                        AND length(COALESCE(c.extracted_text, '') || COALESCE(c.ocr_text, '')) > 0)
                       OR old.status NOT IN ($complete, $skipped));
                """, ("$run", runId), ("$previous", previousRun), ("$source", sourceId),
                ("$summary", (int)IndexingStage.SummaryKeywordsGenerated), ("$queued", (int)IndexingStageStatus.Queued),
                ("$complete", (int)IndexingStageStatus.Complete), ("$skipped", (int)IndexingStageStatus.Skipped),
                ("$basic", (int)IndexingLevel.Basic), ("$retries", maximumRetries), ("$now", queuedAtUtc.UtcTicks));

            ExecuteNonQuery(connection, transaction, """
                UPDATE index_jobs SET status = $cancelled, completed_utc_ticks = $now
                WHERE run_id <> $run AND status NOT IN ($complete, $skipped, $failed, $cancelled)
                  AND run_id IN (SELECT id FROM index_runs WHERE source_id = $source);
                UPDATE index_runs SET status = $cancelledRun, completed_utc_ticks = $now, updated_utc_ticks = $now
                WHERE source_id = $source AND id <> $run AND status IN ($pending, $running, $paused, $waiting, $cancelling);
                DELETE FROM index_stage_states WHERE file_id IN (SELECT file_id FROM index_jobs WHERE run_id = $run)
                  AND stage >= $summary;
                UPDATE index_files SET fully_indexed = 0 WHERE id IN (SELECT file_id FROM index_jobs WHERE run_id = $run);
                INSERT INTO index_stage_states(file_id, stage, status, attempt, processor_fingerprint)
                SELECT j.file_id, j.stage, $queued, 0, f.processor_fingerprint
                FROM index_jobs j JOIN index_files f ON f.id = j.file_id WHERE j.run_id = $run
                ON CONFLICT(file_id, stage) DO UPDATE SET status = $queued, attempt = 0,
                    started_utc_ticks = NULL, completed_utc_ticks = NULL, next_retry_utc_ticks = NULL,
                    waiting_dependency = NULL, failure_category = 0, error_code = NULL;
                UPDATE index_runs SET total_discovered = $count WHERE id = $run;
                """, ("$run", runId), ("$source", sourceId), ("$now", queuedAtUtc.UtcTicks), ("$count", count),
                ("$cancelled", (int)IndexingStageStatus.Cancelled), ("$complete", (int)IndexingStageStatus.Complete),
                ("$skipped", (int)IndexingStageStatus.Skipped), ("$failed", (int)IndexingStageStatus.Failed),
                ("$cancelledRun", (int)IndexingRunStatus.Cancelled), ("$pending", (int)IndexingRunStatus.Pending),
                ("$running", (int)IndexingRunStatus.Running), ("$paused", (int)IndexingRunStatus.Paused),
                ("$waiting", (int)IndexingRunStatus.Waiting), ("$cancelling", (int)IndexingRunStatus.Cancelling),
                ("$summary", (int)IndexingStage.SummaryKeywordsGenerated), ("$queued", (int)IndexingStageStatus.Queued));
            UpdateRunCompletion(connection, transaction, runId, queuedAtUtc);
            transaction.Commit();
            return count;
        }, cancellationToken);
    }
}
