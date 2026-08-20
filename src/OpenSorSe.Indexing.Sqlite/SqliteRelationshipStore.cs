using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Indexing.Sqlite;

public sealed partial class SqliteDeepIndexStore
{
    /// <inheritdoc />
    public Task<RelationshipFileDocument?> GetRelationshipFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(fileId, nameof(fileId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return ReadRelationshipFile(connection, fileId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RelationshipFileDocument>> GetRelationshipFilesAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipCount(maximumCount, 10_000, nameof(maximumCount));
        return RunExclusiveAsync<IReadOnlyList<RelationshipFileDocument>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT f.id, f.source_id, s.display_name, f.full_path, f.relative_path,
                           f.content_hash, f.creation_utc_ticks, f.modified_utc_ticks,
                           f.fully_indexed
                    FROM index_files f
                    JOIN index_sources s ON s.id = f.source_id
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
                    WHERE f.deleted_utc_ticks IS NULL
                      AND COALESCE(p.is_excluded, 0) = 0
                      AND COALESCE(p.suppress_relationships, 0) = 0
                    ORDER BY f.relative_path_key, f.id
                    LIMIT $maximum;
                    """;
                command.Parameters.AddWithValue("$maximum", maximumCount);
                using var reader = command.ExecuteReader();
                var files = new List<RelationshipFileDocument>();
                while (reader.Read())
                {
                    var fullPath = reader.GetString(3);
                    var relativePath = reader.GetString(4);
                    files.Add(new RelationshipFileDocument
                    {
                        FileId = reader.GetString(0),
                        SourceId = reader.GetString(1),
                        SourceName = reader.GetString(2),
                        FullPath = fullPath,
                        RelativePath = relativePath,
                        FileName = Path.GetFileName(fullPath),
                        FolderName = Path.GetDirectoryName(relativePath) ?? string.Empty,
                        Extension = Path.GetExtension(fullPath).ToLowerInvariant(),
                        ContentHash = reader.IsDBNull(5) ? null : reader.GetString(5),
                        CreationTimeUtc = ReadOptionalTimestamp(reader, 6),
                        ModifiedTimeUtc = ReadOptionalTimestamp(reader, 7),
                        IsFullyIndexed = reader.GetBoolean(8),
                    });
                }

                return Array.AsReadOnly(files.ToArray());
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpsertRelationshipFeaturesAsync(
        RelationshipFeatureSet features,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(features);
        ValidateRelationshipIdentifier(features.FileId, nameof(features));
        if (features.KeywordKeys.Count > 64 ||
            features.KeywordKeys.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 64) ||
            string.IsNullOrWhiteSpace(features.FeatureVersion) ||
            features.FeatureVersion.Length > 64)
        {
            throw new InvalidDataException("Relationship features exceed supported bounds.");
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
                    INSERT INTO index_relationship_features(
                        file_id, normalized_stem, folder_key, content_hash, date_bucket,
                        extracted_text_fingerprint, ocr_text_fingerprint, summary_fingerprint,
                        keyword_keys_json, feature_version, updated_utc_ticks,
                        media_transcript_fingerprint, media_ocr_fingerprint, media_device_key, capture_date_bucket)
                    VALUES($file, $stem, $folder, $hash, $date, $text, $ocr, $summary, $keywords, $version, $now,
                           $mediaTranscript, $mediaOcr, $device, $captureDate)
                    ON CONFLICT(file_id) DO UPDATE SET
                        normalized_stem = excluded.normalized_stem,
                        folder_key = excluded.folder_key,
                        content_hash = excluded.content_hash,
                        date_bucket = excluded.date_bucket,
                        extracted_text_fingerprint = excluded.extracted_text_fingerprint,
                        ocr_text_fingerprint = excluded.ocr_text_fingerprint,
                        summary_fingerprint = excluded.summary_fingerprint,
                        keyword_keys_json = excluded.keyword_keys_json,
                        feature_version = excluded.feature_version,
                        media_transcript_fingerprint = excluded.media_transcript_fingerprint,
                        media_ocr_fingerprint = excluded.media_ocr_fingerprint,
                        media_device_key = excluded.media_device_key,
                        capture_date_bucket = excluded.capture_date_bucket,
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$file", features.FileId),
                    ("$stem", Bound(features.NormalizedStem, 256)),
                    ("$folder", Bound(features.FolderKey, 512)),
                    ("$hash", BoundOrNull(features.ContentHash, 128)),
                    ("$date", features.DateBucket),
                    ("$text", BoundOrNull(features.ExtractedTextFingerprint, 128)),
                    ("$ocr", BoundOrNull(features.OcrTextFingerprint, 128)),
                    ("$summary", BoundOrNull(features.SummaryFingerprint, 128)),
                    ("$keywords", JsonSerializer.Serialize(features.KeywordKeys.Take(64))),
                    ("$version", features.FeatureVersion),
                    ("$mediaTranscript", BoundOrNull(features.MediaTranscriptFingerprint, 128)),
                    ("$mediaOcr", BoundOrNull(features.MediaOcrFingerprint, 128)),
                    ("$device", BoundOrNull(features.MediaDeviceKey, 256)),
                    ("$captureDate", features.CaptureDateBucket),
                    ("$now", changedAtUtc.UtcTicks));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM index_relationship_feature_terms WHERE file_id = $file;",
                    ("$file", features.FileId));
                foreach (var term in features.KeywordKeys.Distinct(StringComparer.Ordinal).Take(64))
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "INSERT INTO index_relationship_feature_terms(file_id, term) VALUES($file, $term);",
                        ("$file", features.FileId),
                        ("$term", term));
                }

                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RelationshipFileDocument>> GetRelationshipCandidatesAsync(
        RelationshipFeatureSet target,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateRelationshipCount(maximumCount, RelationshipLimits.MaximumCandidates, nameof(maximumCount));
        return RunExclusiveAsync<IReadOnlyList<RelationshipFileDocument>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    WITH requested_terms(term) AS (
                        SELECT CAST(value AS TEXT) FROM json_each($keywords)
                    ),
                    term_frequency AS (
                        SELECT t.term, COUNT(*) AS frequency
                        FROM index_relationship_feature_terms t
                        JOIN requested_terms requested ON requested.term = t.term
                        GROUP BY t.term
                    ),
                    exact_candidates(file_id, bucket, overlap_count, rarity_score) AS (
                        SELECT rf.file_id, 0,
                               CASE
                                   WHEN $hash IS NOT NULL AND rf.content_hash = $hash THEN 8
                                   WHEN ($text IS NOT NULL AND rf.extracted_text_fingerprint = $text)
                                     OR ($ocr IS NOT NULL AND rf.ocr_text_fingerprint = $ocr)
                                     OR ($mediaTranscript IS NOT NULL AND rf.media_transcript_fingerprint = $mediaTranscript)
                                     OR ($mediaOcr IS NOT NULL AND rf.media_ocr_fingerprint = $mediaOcr) THEN 5
                                   ELSE 2
                               END,
                               0.0
                        FROM index_relationship_features rf
                        WHERE rf.file_id <> $file AND (
                               ($hash IS NOT NULL AND rf.content_hash = $hash)
                            OR ($text IS NOT NULL AND rf.extracted_text_fingerprint = $text)
                            OR ($ocr IS NOT NULL AND rf.ocr_text_fingerprint = $ocr)
                            OR ($summary IS NOT NULL AND rf.summary_fingerprint = $summary)
                            OR ($mediaTranscript IS NOT NULL AND rf.media_transcript_fingerprint = $mediaTranscript)
                            OR ($mediaOcr IS NOT NULL AND rf.media_ocr_fingerprint = $mediaOcr)
                        )
                        ORDER BY 3 DESC, rf.file_id
                        LIMIT 128
                    ),
                    term_candidates(file_id, bucket, overlap_count, rarity_score) AS (
                        SELECT t.file_id, 1, COUNT(*), SUM(1.0 / frequency.frequency)
                        FROM index_relationship_feature_terms t
                        JOIN requested_terms requested ON requested.term = t.term
                        JOIN term_frequency frequency ON frequency.term = t.term
                        WHERE t.file_id <> $file
                        GROUP BY t.file_id
                        ORDER BY COUNT(*) DESC, SUM(1.0 / frequency.frequency) DESC, t.file_id
                        LIMIT 192
                    ),
                    lexical_candidates(file_id, bucket, overlap_count, rarity_score) AS (
                        SELECT rf.file_id, 2, 0, 0.0
                        FROM index_relationship_features rf
                        WHERE rf.file_id <> $file AND $stem <> '' AND rf.normalized_stem = $stem
                        ORDER BY rf.file_id
                        LIMIT 96
                    ),
                    context_candidates(file_id, bucket, overlap_count, rarity_score) AS (
                        SELECT rf.file_id, 3, 0, 0.0
                        FROM index_relationship_features rf
                        WHERE rf.file_id <> $file AND (
                               ($folder <> '' AND rf.folder_key = $folder)
                            OR ($device IS NOT NULL AND rf.media_device_key = $device
                                AND $captureDate IS NOT NULL
                                AND rf.capture_date_bucket BETWEEN $captureDate - $day AND $captureDate + $day)
                            OR ($date IS NOT NULL AND rf.date_bucket BETWEEN $date - $day AND $date + $day)
                        )
                        ORDER BY rf.file_id
                        LIMIT 96
                    ),
                    all_candidates AS (
                        SELECT * FROM exact_candidates
                        UNION ALL SELECT * FROM term_candidates
                        UNION ALL SELECT * FROM lexical_candidates
                        UNION ALL SELECT * FROM context_candidates
                    ),
                    ranked_candidates AS (
                        SELECT file_id, MIN(bucket) AS bucket,
                               MAX(overlap_count) AS overlap_count,
                               MAX(rarity_score) AS rarity_score
                        FROM all_candidates
                        GROUP BY file_id
                    )
                    SELECT ranked.file_id
                    FROM ranked_candidates ranked
                    JOIN index_files f ON f.id = ranked.file_id
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
                    WHERE f.deleted_utc_ticks IS NULL
                      AND COALESCE(p.is_excluded, 0) = 0
                      AND COALESCE(p.suppress_relationships, 0) = 0
                    ORDER BY ranked.bucket, ranked.overlap_count DESC,
                             ranked.rarity_score DESC, ranked.file_id
                    LIMIT $maximum;
                    """;
                AddParameters(
                    command,
                    ("$file", target.FileId),
                    ("$hash", target.ContentHash),
                    ("$text", target.ExtractedTextFingerprint),
                    ("$ocr", target.OcrTextFingerprint),
                    ("$summary", target.SummaryFingerprint),
                    ("$mediaTranscript", target.MediaTranscriptFingerprint),
                    ("$mediaOcr", target.MediaOcrFingerprint),
                    ("$device", target.MediaDeviceKey),
                    ("$captureDate", target.CaptureDateBucket),
                    ("$folder", target.FolderKey),
                    ("$stem", target.NormalizedStem),
                    ("$keywords", JsonSerializer.Serialize(target.KeywordKeys.Take(64))),
                    ("$date", target.DateBucket),
                    ("$day", TimeSpan.TicksPerDay),
                    ("$maximum", maximumCount));
                using var reader = command.ExecuteReader();
                var ids = new List<string>();
                while (reader.Read())
                {
                    ids.Add(reader.GetString(0));
                }

                reader.Close();
                return ReadRelationshipCandidateFiles(connection, ids, target.FeatureVersion);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveRelationshipAnalysisAsync(
        RelationshipAnalysisBatch batch,
        int maximumCollectionMembers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ValidateRelationshipIdentifier(batch.FileId, nameof(batch));
        ValidateRelationshipCount(maximumCollectionMembers, RelationshipLimits.MaximumCollectionMembers, nameof(maximumCollectionMembers));
        if (batch.CandidateCount is < 0 or > RelationshipLimits.MaximumCandidates ||
            batch.Proposals.Count > RelationshipLimits.MaximumRelationshipsPerFile ||
            batch.Duration < TimeSpan.Zero ||
            !IsValidBoundedText(batch.Algorithm, 64) ||
            !IsValidBoundedText(batch.AlgorithmVersion, 64))
        {
            throw new InvalidDataException("Relationship output exceeds the configured graph bound.");
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var retainedIds = batch.Proposals.Select(item => item.Relationship.Id).ToHashSet(StringComparer.Ordinal);
                DeleteStaleAutomaticRelationships(connection, transaction, batch.FileId, retainedIds);
                var collectionCount = 0;
                foreach (var proposal in batch.Proposals)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateRelationship(proposal.Relationship);
                    if (proposal.Collection is not null)
                    {
                        ValidateCollectionSuggestion(proposal.Collection, proposal.Relationship);
                    }

                    var relationship = ApplyPairOverride(connection, transaction, proposal.Relationship);
                    if (relationship is null)
                    {
                        continue;
                    }

                    UpsertRelationship(connection, transaction, relationship, proposal.Collection?.ContextKey);
                    ReplaceEvidence(connection, transaction, relationship);
                    if (proposal.Collection is not null &&
                        UpsertAutomaticCollection(connection, transaction, proposal.Collection, maximumCollectionMembers, batch.CompletedAtUtc))
                    {
                        collectionCount++;
                    }
                }

                CleanupAutomaticCollections(connection, transaction);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE relationship_diagnostics
                    SET last_analysis_utc_ticks = $now,
                        last_duration_milliseconds = $duration,
                        last_candidate_count = $candidates,
                        last_relationship_count = $relationships,
                        last_collection_count = $collections,
                        algorithm_version = $version
                    WHERE id = 1;
                    """,
                    ("$now", batch.CompletedAtUtc.UtcTicks),
                    ("$duration", Math.Max(0L, (long)batch.Duration.TotalMilliseconds)),
                    ("$candidates", batch.CandidateCount),
                    ("$relationships", batch.Proposals.Count),
                    ("$collections", collectionCount),
                    ("$version", Bound(batch.AlgorithmVersion, 64)));
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetStaleRelationshipFileIdsAsync(
        string algorithmVersion,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidBoundedText(algorithmVersion, 64))
        {
            throw new ArgumentException("The relationship algorithm version is invalid.", nameof(algorithmVersion));
        }

        ValidateRelationshipCount(maximumCount, RelationshipLimits.MaximumCandidates + 1, nameof(maximumCount));
        return RunExclusiveAsync<IReadOnlyList<string>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT f.id
                    FROM index_files f
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
                    LEFT JOIN index_relationship_features rf ON rf.file_id = f.id
                    WHERE f.deleted_utc_ticks IS NULL
                      AND f.fully_indexed = 1
                      AND COALESCE(p.is_excluded, 0) = 0
                      AND COALESCE(p.suppress_relationships, 0) = 0
                      AND (rf.file_id IS NULL OR rf.feature_version <> $version)
                    ORDER BY COALESCE(rf.updated_utc_ticks, 0), f.id
                    LIMIT $maximum;
                    """;
                AddParameters(command, ("$version", algorithmVersion), ("$maximum", maximumCount));
                using var reader = command.ExecuteReader();
                var ids = new List<string>();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ids.Add(reader.GetString(0));
                }

                return ids.AsReadOnly();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
        string fileId,
        RelationshipType? type,
        RelationshipConfidence? minimumConfidence,
        RelatedFileSort sort,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(fileId, nameof(fileId));
        ValidateRelationshipCount(maximumCount, 1_000, nameof(maximumCount));
        var orderBy = sort switch
        {
            RelatedFileSort.Relationship => "r.relationship_type, r.confidence DESC, f.relative_path_key, f.id",
            RelatedFileSort.FileName => "f.relative_path_key, f.id",
            RelatedFileSort.LastValidated => "r.validated_utc_ticks DESC, f.relative_path_key, f.id",
            _ => "r.confidence DESC, r.relationship_type, f.relative_path_key, f.id",
        };
        return RunExclusiveAsync<IReadOnlyList<RelatedFile>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    SELECT r.id, f.id, f.full_path, s.display_name
                    FROM index_relationships r
                    JOIN index_files seed ON seed.id = $file
                    JOIN index_files f ON f.id = CASE WHEN r.first_file_id = $file THEN r.second_file_id ELSE r.first_file_id END
                    JOIN index_sources s ON s.id = f.source_id
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
                    LEFT JOIN index_privacy_rules seed_privacy
                      ON seed_privacy.source_id = seed.source_id AND seed_privacy.relative_path_key = seed.relative_path_key
                    WHERE (r.first_file_id = $file OR r.second_file_id = $file)
                      AND r.decision NOT IN ($rejected, $never)
                      AND seed.deleted_utc_ticks IS NULL
                      AND COALESCE(seed_privacy.is_excluded, 0) = 0
                      AND COALESCE(seed_privacy.suppress_relationships, 0) = 0
                      AND f.deleted_utc_ticks IS NULL
                      AND COALESCE(p.is_excluded, 0) = 0
                      AND COALESCE(p.suppress_relationships, 0) = 0
                      AND ($type IS NULL OR r.relationship_type = $type)
                      AND ($confidence IS NULL OR r.confidence >= $confidence)
                    ORDER BY {orderBy}
                    LIMIT $maximum;
                    """;
                var fetchMaximum = Math.Min(1_000, Math.Max(maximumCount, maximumCount * 4));
                AddParameters(
                    command,
                    ("$file", fileId),
                    ("$rejected", (int)RelationshipDecision.Rejected),
                    ("$never", (int)RelationshipDecision.NeverRelate),
                    ("$type", type.HasValue ? (int)type.Value : null),
                    ("$confidence", minimumConfidence.HasValue ? (int)minimumConfidence.Value : null),
                    ("$maximum", fetchMaximum));
                using var reader = command.ExecuteReader();
                var rows = new List<(string RelationshipId, string FileId, string Path, string Source)>();
                while (reader.Read())
                {
                    rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
                }

                reader.Close();
                var typedRows = rows.Select(row => new RelatedFile
                {
                    FileId = row.FileId,
                    FileName = Path.GetFileName(row.Path),
                    FullPath = row.Path,
                    SourceName = row.Source,
                    Relationship = ReadRelationship(connection, row.RelationshipId)!,
                }).Where(item => item.Relationship is not null).ToArray();
                return RelationshipPairAggregator.ToRelatedFiles(
                    RelationshipPairAggregator.Aggregate(typedRows, maximumCount, sort));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RelatedFileContext>> GetRelatedFileContextsAsync(
        string fileId,
        RelationshipType? type,
        RelationshipConfidence? minimumConfidence,
        RelatedFileSort sort,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        var rows = await GetRelatedFilesAsync(
                fileId,
                type,
                minimumConfidence,
                sort,
                maximumCount,
                cancellationToken)
            .ConfigureAwait(false);
        return RelationshipPairAggregator.Aggregate(rows, maximumCount, sort);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RelationshipPairCorrection>> GetRelationshipCorrectionsAsync(
        string fileId,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(fileId, nameof(fileId));
        ValidateRelationshipCount(maximumCount, 1_000, nameof(maximumCount));
        return RunExclusiveAsync<IReadOnlyList<RelationshipPairCorrection>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT o.first_file_id, o.second_file_id,
                           f.id, f.full_path, s.display_name,
                           o.decision, o.relationship_type, o.custom_type,
                           o.changed_utc_ticks,
                           CASE WHEN EXISTS (
                               SELECT 1 FROM index_relationships r
                               WHERE r.first_file_id = o.first_file_id
                                 AND r.second_file_id = o.second_file_id
                                 AND r.decision NOT IN ($rejected, $never)
                           ) THEN 1 ELSE 0 END
                    FROM relationship_pair_overrides o
                    JOIN index_files f
                      ON f.id = CASE WHEN o.first_file_id = $file THEN o.second_file_id ELSE o.first_file_id END
                    JOIN index_sources s ON s.id = f.source_id
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
                    WHERE (o.first_file_id = $file OR o.second_file_id = $file)
                      AND f.deleted_utc_ticks IS NULL
                      AND COALESCE(p.is_excluded, 0) = 0
                      AND COALESCE(p.suppress_relationships, 0) = 0
                    ORDER BY o.changed_utc_ticks DESC, o.first_file_id, o.second_file_id
                    LIMIT $maximum;
                    """;
                AddParameters(
                    command,
                    ("$file", fileId),
                    ("$rejected", (int)RelationshipDecision.Rejected),
                    ("$never", (int)RelationshipDecision.NeverRelate),
                    ("$maximum", maximumCount));
                using var reader = command.ExecuteReader();
                var corrections = new List<RelationshipPairCorrection>();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ticks = reader.GetInt64(8);
                    if (ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks ||
                        !Enum.IsDefined((RelationshipDecision)reader.GetInt32(5)) ||
                        !Enum.IsDefined((RelationshipType)reader.GetInt32(6)))
                    {
                        continue;
                    }

                    corrections.Add(new RelationshipPairCorrection(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        Path.GetFileName(reader.GetString(3)),
                        reader.GetString(3),
                        reader.GetString(4),
                        (RelationshipDecision)reader.GetInt32(5),
                        (RelationshipType)reader.GetInt32(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7),
                        new DateTimeOffset(ticks, TimeSpan.Zero),
                        reader.GetInt32(9) == 1));
                }

                return corrections.AsReadOnly();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<FileRelationship?> GetRelationshipAsync(
        string relationshipId,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(relationshipId, nameof(relationshipId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return ReadRelationship(connection, relationshipId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SmartCollection>> GetCollectionsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipCount(maximumCount, 2_000, nameof(maximumCount));
        return RunExclusiveAsync<IReadOnlyList<SmartCollection>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = CollectionSelect +
                    " WHERE EXISTS (" +
                    "SELECT 1 FROM smart_collection_members visible " +
                    "JOIN index_files vf ON vf.id = visible.file_id " +
                    "LEFT JOIN index_privacy_rules vp ON vp.source_id = vf.source_id AND vp.relative_path_key = vf.relative_path_key " +
                    "WHERE visible.collection_id = c.id AND vf.deleted_utc_ticks IS NULL " +
                    "AND COALESCE(vp.is_excluded, 0) = 0 AND COALESCE(vp.suppress_relationships, 0) = 0) " +
                    "ORDER BY c.is_pinned DESC, c.updated_utc_ticks DESC, c.title, c.id LIMIT $maximum;";
                command.Parameters.AddWithValue("$maximum", maximumCount);
                using var reader = command.ExecuteReader();
                var collections = new List<SmartCollection>();
                while (reader.Read())
                {
                    if (ReadCollection(reader) is { } collection)
                    {
                        collections.Add(collection);
                    }
                }

                return Array.AsReadOnly(collections.ToArray());
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SmartCollectionDetails?> GetCollectionAsync(
        string collectionId,
        int maximumMembers,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(collectionId, nameof(collectionId));
        ValidateRelationshipCount(maximumMembers, RelationshipLimits.MaximumCollectionMembers, nameof(maximumMembers));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                var collection = ReadCollection(connection, collectionId);
                if (collection is null)
                {
                    return null;
                }

                var members = ReadCollectionMembers(connection, collectionId, maximumMembers);
                var memberIds = members.Select(item => item.FileId).ToHashSet(StringComparer.Ordinal);
                var relationships = ReadCollectionRelationships(connection, collectionId, maximumMembers)
                    .Where(item => memberIds.Contains(item.FirstFileId) && memberIds.Contains(item.SecondFileId))
                    .ToArray();
                var files = members.ToDictionary(item => item.FileId, StringComparer.Ordinal);
                var timeline = ReadCollectionTimeline(connection, collectionId, maximumMembers)
                    .Where(item => files.ContainsKey(item.FileId))
                    .ToArray();
                return new SmartCollectionDetails(
                    collection,
                    members,
                    Array.AsReadOnly(relationships),
                    Array.AsReadOnly(timeline));
            },
            cancellationToken);
    }

    private const string CollectionSelect =
        """
        SELECT c.id, c.title, c.description, c.relationship_summary, c.context_type,
               c.confidence, c.creation_source, c.is_pinned, c.is_user_renamed,
               c.updated_utc_ticks,
               (SELECT COUNT(*) FROM smart_collection_members m
                JOIN index_files f ON f.id = m.file_id
                LEFT JOIN index_privacy_rules p
                  ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
                WHERE m.collection_id = c.id AND f.deleted_utc_ticks IS NULL
                  AND COALESCE(p.is_excluded, 0) = 0
                  AND COALESCE(p.suppress_relationships, 0) = 0)
        FROM smart_collections c
        """;

    private static RelationshipFileDocument? ReadRelationshipFile(
        SqliteConnection connection,
        string fileId,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RelationshipFileSelect +
            " WHERE f.id = $file AND f.deleted_utc_ticks IS NULL AND COALESCE(p.is_excluded, 0) = 0;";
        command.Parameters.AddWithValue("$file", fileId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var document = ReadRelationshipFileRow(reader);
        reader.Close();
        return AddRelationshipTagEvidence(connection, [document], transaction)[0];
    }

    private static IReadOnlyList<RelationshipFileDocument> ReadRelationshipFiles(
        SqliteConnection connection,
        IReadOnlyList<string> fileIds,
        SqliteTransaction? transaction = null)
    {
        if (fileIds.Count == 0)
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var parameters = fileIds.Select((_, index) => "$file" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
        command.CommandText = RelationshipFileSelect +
            $" WHERE f.id IN ({string.Join(',', parameters)}) AND f.deleted_utc_ticks IS NULL " +
            "AND COALESCE(p.is_excluded, 0) = 0;";
        for (var index = 0; index < fileIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], fileIds[index]);
        }

        using var reader = command.ExecuteReader();
        var documents = new List<RelationshipFileDocument>(fileIds.Count);
        while (reader.Read())
        {
            documents.Add(ReadRelationshipFileRow(reader));
        }

        reader.Close();
        var hydrated = AddRelationshipTagEvidence(connection, documents, transaction)
            .ToDictionary(item => item.FileId, StringComparer.Ordinal);
        return fileIds.Where(hydrated.ContainsKey).Select(id => hydrated[id]).ToArray();
    }

    private static IReadOnlyList<RelationshipFileDocument> ReadRelationshipCandidateFiles(
        SqliteConnection connection,
        IReadOnlyList<string> fileIds,
        string expectedFeatureVersion)
    {
        if (fileIds.Count == 0)
        {
            return [];
        }

        using var command = connection.CreateCommand();
        var parameters = fileIds.Select((_, index) => "$candidate" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
        command.CommandText = RelationshipCandidateFileSelect +
            $" WHERE f.id IN ({string.Join(',', parameters)}) AND f.deleted_utc_ticks IS NULL " +
            "AND COALESCE(p.is_excluded, 0) = 0;";
        for (var index = 0; index < fileIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], fileIds[index]);
        }

        using var reader = command.ExecuteReader();
        var compact = new List<RelationshipFileDocument>(fileIds.Count);
        while (reader.Read())
        {
            var document = ReadRelationshipFileRow(reader);
            compact.Add(document with
            {
                PrecomputedRelationshipFeatures = new RelationshipFeatureSet(
                    document.FileId,
                    reader.GetString(22),
                    reader.GetString(23),
                    reader.IsDBNull(24) ? null : reader.GetString(24),
                    reader.IsDBNull(25) ? null : reader.GetInt64(25),
                    reader.IsDBNull(26) ? null : reader.GetString(26),
                    reader.IsDBNull(27) ? null : reader.GetString(27),
                    reader.IsDBNull(28) ? null : reader.GetString(28),
                    TryDeserializeStrings(reader.GetString(29)),
                    reader.GetString(30))
                {
                    MediaTranscriptFingerprint = reader.IsDBNull(31) ? null : reader.GetString(31),
                    MediaOcrFingerprint = reader.IsDBNull(32) ? null : reader.GetString(32),
                    MediaDeviceKey = reader.IsDBNull(33) ? null : reader.GetString(33),
                    CaptureDateBucket = reader.IsDBNull(34) ? null : reader.GetInt64(34),
                },
            });
        }

        reader.Close();
        var staleIds = compact
            .Where(item => !string.Equals(
                item.PrecomputedRelationshipFeatures!.FeatureVersion,
                expectedFeatureVersion,
                StringComparison.Ordinal))
            .Select(item => item.FileId)
            .ToArray();
        var stale = ReadRelationshipFiles(connection, staleIds)
            .ToDictionary(item => item.FileId, StringComparer.Ordinal);
        var current = AddRelationshipTagEvidence(
                connection,
                compact.Where(item => !stale.ContainsKey(item.FileId)).ToArray(),
                transaction: null)
            .ToDictionary(item => item.FileId, StringComparer.Ordinal);
        foreach (var item in stale)
        {
            current[item.Key] = item.Value;
        }

        return fileIds.Where(current.ContainsKey).Select(id => current[id]).ToArray();
    }

    private static RelationshipFileDocument ReadRelationshipFileRow(SqliteDataReader reader)
    {
        var fullPath = reader.GetString(3);
        var relativePath = reader.GetString(4);
        var basic = (IndexingLevel)reader.GetInt32(16) == IndexingLevel.Basic;
        var keywords = basic || reader.GetBoolean(18) || reader.IsDBNull(12)
            ? []
            : TryDeserializeStrings(reader.GetString(12));
        var semanticValid = true;
        var semantic = basic || reader.GetBoolean(19) || reader.IsDBNull(13)
            ? null
            : TryDeserializeFloats(reader.GetString(13), out semanticValid);
        var media = reader.IsDBNull(20) ? null : TryDeserializeMediaEvidence(reader.GetString(20));
        var contentIntelligence = basic || reader.GetBoolean(18) || reader.IsDBNull(21)
            ? null
            : TryDeserializeContentIntelligence(reader.GetString(21));
        return new RelationshipFileDocument
        {
            FileId = reader.GetString(0),
            SourceId = reader.GetString(1),
            SourceName = reader.GetString(2),
            FullPath = fullPath,
            RelativePath = relativePath,
            FileName = Path.GetFileName(fullPath),
            FolderName = Path.GetDirectoryName(relativePath) ?? string.Empty,
            Extension = Path.GetExtension(fullPath).ToLowerInvariant(),
            ContentHash = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreationTimeUtc = ReadOptionalTimestamp(reader, 6),
            ModifiedTimeUtc = ReadOptionalTimestamp(reader, 7),
            MetadataText = string.Join(' ', reader.GetString(8), MediaEvidenceText.CreateMetadataText(media)),
            ExtractedText = basic || reader.IsDBNull(9) ? null : reader.GetString(9),
            OcrText = basic || reader.GetBoolean(17) || reader.IsDBNull(10) ? null : reader.GetString(10),
            MediaEvidence = media,
            ContentIntelligence = contentIntelligence,
            Summary = basic || reader.GetBoolean(18) || reader.IsDBNull(11) ? null : reader.GetString(11),
            Keywords = keywords,
            SemanticRepresentation = semanticValid ? semantic : null,
            IsFullyIndexed = reader.GetBoolean(14),
            RelationshipAnalysisSuppressed = reader.GetBoolean(15),
        };
    }

    private static IReadOnlyList<RelationshipFileDocument> AddRelationshipTagEvidence(
        SqliteConnection connection,
        IReadOnlyList<RelationshipFileDocument> documents,
        SqliteTransaction? transaction)
    {
        var effectiveByFile = ReadEffectiveSmartTags(connection, documents.Select(item => item.FileId).ToArray(), transaction);
        return documents.Select(document =>
        {
            var effectiveTags = effectiveByFile.GetValueOrDefault(document.FileId) ?? [];
            return document with
            {
                Tags = effectiveTags
                    .Select(item => item.Definition.CanonicalKey)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                TagEvidence = effectiveTags
                    .Select(item => new RelationshipTagEvidence(
                        item.Definition.CanonicalKey,
                        item.Definition.DisplayName,
                        item.Definition.Type,
                        item.Confidence,
                        item.Origin,
                        item.State,
                        item.Decision))
                    .ToArray(),
            };
        }).ToArray();
    }

    private const string RelationshipFileSelect =
        """
        SELECT f.id, f.source_id, s.display_name, f.full_path, f.relative_path,
               f.content_hash, f.creation_utc_ticks, f.modified_utc_ticks,
               f.metadata_fingerprint, c.extracted_text, c.ocr_text, c.summary,
               c.keywords_json, c.semantic_json, f.fully_indexed,
               COALESCE(p.suppress_relationships, 0), f.indexing_level,
               COALESCE(p.suppress_ocr, 0), COALESCE(p.suppress_summary, 0),
               COALESCE(p.suppress_semantic, 0), m.evidence_json,
               c.content_intelligence_json
        FROM index_files f
        JOIN index_sources s ON s.id = f.source_id
        LEFT JOIN index_content c ON c.content_hash = f.content_hash
        LEFT JOIN index_media_content m ON m.content_hash = f.content_hash
        LEFT JOIN index_privacy_rules p
          ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
        """;

    private const string RelationshipCandidateFileSelect =
        """
        SELECT f.id, f.source_id, s.display_name, f.full_path, f.relative_path,
               f.content_hash, f.creation_utc_ticks, f.modified_utc_ticks,
               f.metadata_fingerprint, NULL, NULL, NULL,
               c.keywords_json, c.semantic_json, f.fully_indexed,
               COALESCE(p.suppress_relationships, 0), f.indexing_level,
               COALESCE(p.suppress_ocr, 0), COALESCE(p.suppress_summary, 0),
               COALESCE(p.suppress_semantic, 0), NULL,
               c.content_intelligence_json,
               rf.normalized_stem, rf.folder_key, rf.content_hash, rf.date_bucket,
               rf.extracted_text_fingerprint, rf.ocr_text_fingerprint, rf.summary_fingerprint,
               rf.keyword_keys_json, rf.feature_version, rf.media_transcript_fingerprint,
               rf.media_ocr_fingerprint, rf.media_device_key, rf.capture_date_bucket
        FROM index_files f
        JOIN index_sources s ON s.id = f.source_id
        JOIN index_relationship_features rf ON rf.file_id = f.id
        LEFT JOIN index_content c ON c.content_hash = f.content_hash
        LEFT JOIN index_privacy_rules p
          ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
        """;

    private static DateTimeOffset? ReadOptionalTimestamp(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var ticks = reader.GetInt64(ordinal);
        return ticks >= DateTimeOffset.MinValue.Ticks && ticks <= DateTimeOffset.MaxValue.Ticks
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;
    }

    private static FileRelationship? ReadRelationship(
        SqliteConnection connection,
        string relationshipId,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, first_file_id, second_file_id, relationship_type, custom_type,
                   confidence, algorithm, algorithm_version, created_utc_ticks,
                   validated_utc_ticks, decision, is_manual
            FROM index_relationships
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", relationshipId);
        using var reader = command.ExecuteReader();
        if (!reader.Read() ||
            !Enum.IsDefined((RelationshipType)reader.GetInt32(3)) ||
            !Enum.IsDefined((RelationshipConfidence)reader.GetInt32(5)) ||
            !Enum.IsDefined((RelationshipDecision)reader.GetInt32(10)))
        {
            return null;
        }

        var row = new
        {
            Id = reader.GetString(0),
            First = reader.GetString(1),
            Second = reader.GetString(2),
            Type = (RelationshipType)reader.GetInt32(3),
            Custom = reader.IsDBNull(4) ? null : reader.GetString(4),
            Confidence = (RelationshipConfidence)reader.GetInt32(5),
            Algorithm = reader.GetString(6),
            Version = reader.GetString(7),
            Created = reader.GetInt64(8),
            Validated = reader.GetInt64(9),
            Decision = (RelationshipDecision)reader.GetInt32(10),
            Manual = reader.GetBoolean(11),
        };
        reader.Close();
        var createdAt = row.Created >= DateTimeOffset.MinValue.Ticks && row.Created <= DateTimeOffset.MaxValue.Ticks
            ? new DateTimeOffset(row.Created, TimeSpan.Zero)
            : (DateTimeOffset?)null;
        var validatedAt = row.Validated >= DateTimeOffset.MinValue.Ticks && row.Validated <= DateTimeOffset.MaxValue.Ticks
            ? new DateTimeOffset(row.Validated, TimeSpan.Zero)
            : (DateTimeOffset?)null;
        if (createdAt is null || validatedAt is null ||
            string.CompareOrdinal(row.First, row.Second) >= 0 ||
            row.Id.Length > 256 || row.Algorithm.Length > 64 || row.Version.Length > 64 ||
            row.Custom?.Length > 64)
        {
            return null;
        }

        var evidence = ReadEvidence(connection, row.Id, transaction);
        if (!row.Manual && evidence.Count == 0)
        {
            return null;
        }

        return new FileRelationship
        {
            Id = row.Id,
            FirstFileId = row.First,
            SecondFileId = row.Second,
            Type = row.Type,
            CustomType = row.Custom,
            Confidence = row.Confidence,
            Evidence = evidence,
            Algorithm = row.Algorithm,
            AlgorithmVersion = row.Version,
            CreatedAtUtc = createdAt.Value,
            LastValidatedAtUtc = validatedAt.Value,
            Decision = row.Decision,
            IsManual = row.Manual,
        };
    }

    private static IReadOnlyList<RelationshipEvidence> ReadEvidence(
        SqliteConnection connection,
        string relationshipId,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT evidence_kind, evidence_key, explanation
            FROM index_relationship_evidence
            WHERE relationship_id = $relationship
            ORDER BY ordinal
            LIMIT $maximum;
            """;
        AddParameters(
            command,
            ("$relationship", relationshipId),
            ("$maximum", RelationshipLimits.MaximumEvidencePerRelationship));
        using var reader = command.ExecuteReader();
        var evidence = new List<RelationshipEvidence>();
        while (reader.Read())
        {
            var kind = (RelationshipEvidenceKind)reader.GetInt32(0);
            var key = reader.GetString(1);
            var explanation = reader.GetString(2);
            if (!Enum.IsDefined(kind) ||
                !IsValidBoundedText(key, RelationshipLimits.MaximumEvidenceTextCharacters) ||
                !IsValidBoundedText(explanation, RelationshipLimits.MaximumEvidenceTextCharacters))
            {
                continue;
            }

            evidence.Add(new RelationshipEvidence(
                kind,
                key,
                explanation));
        }

        return Array.AsReadOnly(evidence.ToArray());
    }

    private static void DeleteStaleAutomaticRelationships(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fileId,
        IReadOnlySet<string> retainedIds)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM index_relationships WHERE is_manual = 0 AND decision NOT IN ($confirmed, $always) " +
            "AND (first_file_id = $file OR second_file_id = $file)";
        AddParameters(
            command,
            ("$confirmed", (int)RelationshipDecision.Confirmed),
            ("$always", (int)RelationshipDecision.AlwaysRelate),
            ("$file", fileId));
        if (retainedIds.Count > 0)
        {
            var names = new List<string>();
            var index = 0;
            foreach (var id in retainedIds.Order(StringComparer.Ordinal))
            {
                var name = "$retained" + index.ToString(CultureInfo.InvariantCulture);
                names.Add(name);
                command.Parameters.AddWithValue(name, id);
                index++;
            }

            command.CommandText += $" AND id NOT IN ({string.Join(", ", names)})";
        }

        command.CommandText += ";";
        command.ExecuteNonQuery();
    }

    private static FileRelationship? ApplyPairOverride(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FileRelationship relationship)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT decision
            FROM relationship_pair_overrides
            WHERE first_file_id = $first AND second_file_id = $second;
            """;
        AddParameters(command, ("$first", relationship.FirstFileId), ("$second", relationship.SecondFileId));
        var value = command.ExecuteScalar();
        if (value is null || value is DBNull)
        {
            return relationship;
        }

        var decision = (RelationshipDecision)Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return decision switch
        {
            RelationshipDecision.Rejected or RelationshipDecision.NeverRelate => null,
            RelationshipDecision.Confirmed or RelationshipDecision.AlwaysRelate => relationship with
            {
                Decision = decision,
                Confidence = RelationshipConfidence.Confirmed,
            },
            _ => relationship,
        };
    }

    private static void UpsertRelationship(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FileRelationship relationship,
        string? contextKey)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO index_relationships(
                id, first_file_id, second_file_id, relationship_type, custom_type,
                confidence, algorithm, algorithm_version, created_utc_ticks,
                validated_utc_ticks, decision, is_manual, context_key)
            VALUES($id, $first, $second, $type, $custom, $confidence, $algorithm,
                   $version, $created, $validated, $decision, $manual, $context)
            ON CONFLICT(id) DO UPDATE SET
                relationship_type = excluded.relationship_type,
                custom_type = excluded.custom_type,
                confidence = CASE WHEN index_relationships.decision IN ($confirmed, $always)
                                  THEN $confirmedConfidence ELSE excluded.confidence END,
                algorithm = excluded.algorithm,
                algorithm_version = excluded.algorithm_version,
                validated_utc_ticks = excluded.validated_utc_ticks,
                context_key = excluded.context_key;
            """,
            ("$id", relationship.Id),
            ("$first", relationship.FirstFileId),
            ("$second", relationship.SecondFileId),
            ("$type", (int)relationship.Type),
            ("$custom", relationship.CustomType),
            ("$confidence", (int)relationship.Confidence),
            ("$algorithm", Bound(relationship.Algorithm, 64)),
            ("$version", Bound(relationship.AlgorithmVersion, 64)),
            ("$created", relationship.CreatedAtUtc.UtcTicks),
            ("$validated", relationship.LastValidatedAtUtc.UtcTicks),
            ("$decision", (int)relationship.Decision),
            ("$manual", relationship.IsManual ? 1 : 0),
            ("$context", BoundOrNull(contextKey, 256)),
            ("$confirmed", (int)RelationshipDecision.Confirmed),
            ("$always", (int)RelationshipDecision.AlwaysRelate),
            ("$confirmedConfidence", (int)RelationshipConfidence.Confirmed));
    }

    private static void ReplaceEvidence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FileRelationship relationship)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM index_relationship_evidence WHERE relationship_id = $relationship;",
            ("$relationship", relationship.Id));
        var ordinal = 0;
        foreach (var evidence in relationship.Evidence.Take(RelationshipLimits.MaximumEvidencePerRelationship))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO index_relationship_evidence(
                    relationship_id, ordinal, evidence_kind, evidence_key, explanation)
                VALUES($relationship, $ordinal, $kind, $key, $explanation);
                """,
                ("$relationship", relationship.Id),
                ("$ordinal", ordinal++),
                ("$kind", (int)evidence.Kind),
                ("$key", Bound(evidence.EvidenceKey, RelationshipLimits.MaximumEvidenceTextCharacters)),
                ("$explanation", Bound(evidence.Explanation, RelationshipLimits.MaximumEvidenceTextCharacters)));
        }
    }

    private static bool UpsertAutomaticCollection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SmartCollectionSuggestion suggestion,
        int maximumMembers,
        DateTimeOffset changedAtUtc)
    {
        var forgotten = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM forgotten_smart_collections WHERE context_key = $context;",
                ("$context", suggestion.ContextKey)) ?? 0,
            CultureInfo.InvariantCulture) > 0;
        if (forgotten)
        {
            return false;
        }

        var collectionId = "collection:" + StableRelationshipKey(suggestion.ContextKey);
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO smart_collections(
                id, context_key, title, description, relationship_summary, context_type,
                confidence, creation_source, created_utc_ticks, updated_utc_ticks)
            VALUES($id, $context, $title, $description, $summary, $type, $confidence,
                   $source, $now, $now)
            ON CONFLICT(context_key) DO UPDATE SET
                title = CASE WHEN smart_collections.is_user_renamed = 1 THEN smart_collections.title ELSE excluded.title END,
                description = excluded.description,
                relationship_summary = excluded.relationship_summary,
                context_type = excluded.context_type,
                confidence = MAX(smart_collections.confidence, excluded.confidence),
                updated_utc_ticks = excluded.updated_utc_ticks;
            """,
            ("$id", collectionId),
            ("$context", Bound(suggestion.ContextKey, 256)),
            ("$title", Bound(suggestion.Title, RelationshipLimits.MaximumCollectionTitleCharacters)),
            ("$description", Bound(suggestion.Description, RelationshipLimits.MaximumCollectionDescriptionCharacters)),
            ("$summary", Bound(suggestion.RelationshipSummary, RelationshipLimits.MaximumCollectionDescriptionCharacters)),
            ("$type", (int)suggestion.ContextType),
            ("$confidence", (int)suggestion.Confidence),
            ("$source", (int)SmartCollectionCreationSource.Automatic),
            ("$now", changedAtUtc.UtcTicks));
        var storedId = Convert.ToString(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT id FROM smart_collections WHERE context_key = $context;",
                ("$context", suggestion.ContextKey)),
            CultureInfo.InvariantCulture) ?? collectionId;
        AddAutomaticMember(connection, transaction, storedId, suggestion.FirstFileId, suggestion.RelationshipId, maximumMembers, changedAtUtc);
        AddAutomaticMember(connection, transaction, storedId, suggestion.SecondFileId, suggestion.RelationshipId, maximumMembers, changedAtUtc);
        return true;
    }

    private static void AddAutomaticMember(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string collectionId,
        string fileId,
        string relationshipId,
        int maximumMembers,
        DateTimeOffset changedAtUtc)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT OR IGNORE INTO smart_collection_members(
                collection_id, file_id, membership_source, relationship_id, added_utc_ticks)
            SELECT $collection, $file, $source, $relationship, $now
            WHERE NOT EXISTS(
                      SELECT 1 FROM smart_collection_member_overrides
                      WHERE collection_id = $collection AND file_id = $file AND excluded = 1)
              AND (SELECT COUNT(*) FROM smart_collection_members WHERE collection_id = $collection) < $maximum;
            """,
            ("$collection", collectionId),
            ("$file", fileId),
            ("$source", (int)CollectionMembershipSource.Automatic),
            ("$relationship", relationshipId),
            ("$now", changedAtUtc.UtcTicks),
            ("$maximum", maximumMembers));
    }

    private static void CleanupAutomaticCollections(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM smart_collection_members
            WHERE membership_source = $automatic
              AND relationship_id IS NULL;
            DELETE FROM smart_collections
            WHERE creation_source = $automatic
              AND is_pinned = 0
              AND is_user_renamed = 0
              AND NOT EXISTS(SELECT 1 FROM smart_collection_member_overrides o WHERE o.collection_id = smart_collections.id)
              AND NOT EXISTS(SELECT 1 FROM smart_collection_members m WHERE m.collection_id = smart_collections.id);
            """,
            ("$automatic", (int)SmartCollectionCreationSource.Automatic));
    }

    private static SmartCollection? ReadCollection(
        SqliteConnection connection,
        string collectionId,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CollectionSelect + " WHERE c.id = $id;";
        command.Parameters.AddWithValue("$id", collectionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCollection(reader) : null;
    }

    private static SmartCollection? ReadCollection(SqliteDataReader reader)
    {
        var contextType = (RelationshipType)reader.GetInt32(4);
        var confidence = (RelationshipConfidence)reader.GetInt32(5);
        var creationSource = (SmartCollectionCreationSource)reader.GetInt32(6);
        var updatedTicks = reader.GetInt64(9);
        var id = reader.GetString(0);
        var title = reader.GetString(1);
        var description = reader.GetString(2);
        var summary = reader.GetString(3);
        if (!Enum.IsDefined(contextType) || !Enum.IsDefined(confidence) || !Enum.IsDefined(creationSource) ||
            updatedTicks < DateTimeOffset.MinValue.Ticks || updatedTicks > DateTimeOffset.MaxValue.Ticks ||
            id.Length > 256 || title.Length > RelationshipLimits.MaximumCollectionTitleCharacters ||
            description.Length > RelationshipLimits.MaximumCollectionDescriptionCharacters ||
            summary.Length > RelationshipLimits.MaximumCollectionDescriptionCharacters)
        {
            return null;
        }

        return new SmartCollection
        {
            Id = id,
            Title = title,
            Description = description,
            RelationshipSummary = summary,
            ContextType = contextType,
            Confidence = confidence,
            CreationSource = creationSource,
            IsPinned = reader.GetBoolean(7),
            IsUserRenamed = reader.GetBoolean(8),
            LastUpdatedAtUtc = new DateTimeOffset(updatedTicks, TimeSpan.Zero),
            MemberCount = reader.GetInt32(10),
        };
    }

    private static IReadOnlyList<SmartCollectionMember> ReadCollectionMembers(
        SqliteConnection connection,
        string collectionId,
        int maximumMembers)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT m.file_id, f.full_path, s.display_name, m.membership_source, m.added_utc_ticks
            FROM smart_collection_members m
            JOIN index_files f ON f.id = m.file_id
            JOIN index_sources s ON s.id = f.source_id
            LEFT JOIN index_privacy_rules p
              ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
            WHERE m.collection_id = $collection AND f.deleted_utc_ticks IS NULL
              AND COALESCE(p.is_excluded, 0) = 0
              AND COALESCE(p.suppress_relationships, 0) = 0
              AND m.membership_source BETWEEN $automatic AND $manual
              AND m.added_utc_ticks BETWEEN $minimumTicks AND $maximumTicks
            ORDER BY f.relative_path_key, f.id
            LIMIT $maximum;
            """;
        AddParameters(
            command,
            ("$collection", collectionId),
            ("$automatic", (int)CollectionMembershipSource.Automatic),
            ("$manual", (int)CollectionMembershipSource.Manual),
            ("$minimumTicks", DateTimeOffset.MinValue.Ticks),
            ("$maximumTicks", DateTimeOffset.MaxValue.Ticks),
            ("$maximum", maximumMembers));
        using var reader = command.ExecuteReader();
        var members = new List<SmartCollectionMember>();
        while (reader.Read())
        {
            members.Add(new SmartCollectionMember(
                collectionId,
                reader.GetString(0),
                Path.GetFileName(reader.GetString(1)),
                reader.GetString(1),
                reader.GetString(2),
                (CollectionMembershipSource)reader.GetInt32(3),
                new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero)));
        }

        return Array.AsReadOnly(members.ToArray());
    }

    private static IReadOnlyList<FileRelationship> ReadCollectionRelationships(
        SqliteConnection connection,
        string collectionId,
        int maximumCount)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT r.id
            FROM index_relationships r
            JOIN smart_collection_members first ON first.file_id = r.first_file_id AND first.collection_id = $collection
            JOIN smart_collection_members second ON second.file_id = r.second_file_id AND second.collection_id = $collection
            WHERE r.decision NOT IN ($rejected, $never)
            ORDER BY r.confidence DESC, r.id
            LIMIT $maximum;
            """;
        AddParameters(
            command,
            ("$collection", collectionId),
            ("$rejected", (int)RelationshipDecision.Rejected),
            ("$never", (int)RelationshipDecision.NeverRelate),
            ("$maximum", maximumCount));
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        reader.Close();
        return Array.AsReadOnly(ids.Select(id => ReadRelationship(connection, id)).OfType<FileRelationship>().ToArray());
    }

    private static IReadOnlyList<CollectionTimelineEvent> ReadCollectionTimeline(
        SqliteConnection connection,
        string collectionId,
        int maximumCount)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT f.id, f.full_path, f.modified_utc_ticks
            FROM smart_collection_members m
            JOIN index_files f ON f.id = m.file_id
            LEFT JOIN index_privacy_rules p
              ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
            WHERE m.collection_id = $collection AND f.deleted_utc_ticks IS NULL
              AND COALESCE(p.is_excluded, 0) = 0
              AND COALESCE(p.suppress_relationships, 0) = 0
              AND f.modified_utc_ticks BETWEEN $minimum AND $maximumTicks
            ORDER BY f.modified_utc_ticks, f.relative_path_key, f.id
            LIMIT $maximum;
            """;
        AddParameters(
            command,
            ("$collection", collectionId),
            ("$minimum", DateTimeOffset.MinValue.Ticks),
            ("$maximumTicks", DateTimeOffset.MaxValue.Ticks),
            ("$maximum", maximumCount));
        using var reader = command.ExecuteReader();
        var events = new List<CollectionTimelineEvent>();
        while (reader.Read())
        {
            var path = reader.GetString(1);
            events.Add(new CollectionTimelineEvent(
                reader.GetString(0),
                Path.GetFileName(path),
                new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
                "File modified",
                "Indexed modified time"));
        }

        return Array.AsReadOnly(events.ToArray());
    }

    private static void ValidateRelationship(FileRelationship relationship)
    {
        ValidateRelationshipIdentifier(relationship.Id, nameof(relationship));
        ValidateRelationshipIdentifier(relationship.FirstFileId, nameof(relationship));
        ValidateRelationshipIdentifier(relationship.SecondFileId, nameof(relationship));
        if (string.CompareOrdinal(relationship.FirstFileId, relationship.SecondFileId) >= 0 ||
            !Enum.IsDefined(relationship.Type) || !Enum.IsDefined(relationship.Confidence) ||
            !Enum.IsDefined(relationship.Decision) ||
            relationship.Evidence.Count > RelationshipLimits.MaximumEvidencePerRelationship ||
            !relationship.IsManual && relationship.Evidence.Count == 0 ||
            !IsValidBoundedText(relationship.Algorithm, 64) ||
            !IsValidBoundedText(relationship.AlgorithmVersion, 64) ||
            relationship.CustomType is not null && !IsValidBoundedText(relationship.CustomType, 64) ||
            relationship.Type == RelationshipType.Custom && string.IsNullOrWhiteSpace(relationship.CustomType) ||
            relationship.Evidence.Any(item =>
                !Enum.IsDefined(item.Kind) ||
                !IsValidBoundedText(item.EvidenceKey, RelationshipLimits.MaximumEvidenceTextCharacters) ||
                !IsValidBoundedText(item.Explanation, RelationshipLimits.MaximumEvidenceTextCharacters)))
        {
            throw new InvalidDataException("Relationship output is malformed or exceeds supported bounds.");
        }
    }

    private static void ValidateCollectionSuggestion(
        SmartCollectionSuggestion suggestion,
        FileRelationship relationship)
    {
        if (!IsValidBoundedText(suggestion.ContextKey, 256) ||
            !IsValidBoundedText(suggestion.Title, RelationshipLimits.MaximumCollectionTitleCharacters) ||
            !IsValidBoundedText(suggestion.Description, RelationshipLimits.MaximumCollectionDescriptionCharacters) ||
            !IsValidBoundedText(suggestion.RelationshipSummary, RelationshipLimits.MaximumCollectionDescriptionCharacters) ||
            !Enum.IsDefined(suggestion.ContextType) ||
            !Enum.IsDefined(suggestion.Confidence) ||
            !string.Equals(suggestion.RelationshipId, relationship.Id, StringComparison.Ordinal) ||
            !string.Equals(suggestion.FirstFileId, relationship.FirstFileId, StringComparison.Ordinal) ||
            !string.Equals(suggestion.SecondFileId, relationship.SecondFileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Smart Collection output is malformed or exceeds supported bounds.");
        }
    }

    private static bool IsValidBoundedText(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl) &&
        !SearchTextNormalizer.ContainsMalformedUnicode(value);

    private static void ValidateRelationshipIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
        {
            throw new ArgumentException("The relationship identifier is malformed or exceeds the supported bound.", parameterName);
        }
    }

    private static void ValidateRelationshipCount(int value, int maximum, string parameterName)
    {
        if (value is < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static string StableRelationshipKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
}
