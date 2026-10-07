using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Indexing.Sqlite;

public sealed partial class SqliteDeepIndexStore
{
    // Derived data stays in the catalog transaction boundary. Invalidation triggers also
    // cover privacy operations and source reconciliation that do not run the coordinator.
    private const string VectorSchema = """
        CREATE TABLE IF NOT EXISTS index_vector_documents (
            file_id TEXT PRIMARY KEY, model_key TEXT NOT NULL, source_stamp TEXT NOT NULL,
            byte_count INTEGER NOT NULL, updated_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS index_vector_chunks (
            chunk_id TEXT PRIMARY KEY, file_id TEXT NOT NULL, ordinal INTEGER NOT NULL,
            source_field TEXT NOT NULL, source_start INTEGER NOT NULL, source_length INTEGER NOT NULL,
            chunk_text TEXT NOT NULL, vector BLOB NOT NULL,
            FOREIGN KEY(file_id) REFERENCES index_vector_documents(file_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_vector_chunks_file ON index_vector_chunks(file_id, ordinal);
        CREATE TABLE IF NOT EXISTS index_vector_failures (
            file_id TEXT PRIMARY KEY, model_key TEXT NOT NULL, source_stamp TEXT NOT NULL,
            attempts INTEGER NOT NULL, next_retry_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY(file_id) REFERENCES index_files(id) ON DELETE CASCADE
        );
        CREATE TRIGGER IF NOT EXISTS vector_file_updated AFTER UPDATE ON index_files
        WHEN OLD.full_path IS NOT NEW.full_path OR OLD.metadata_fingerprint IS NOT NEW.metadata_fingerprint
          OR OLD.processor_fingerprint IS NOT NEW.processor_fingerprint OR OLD.content_hash IS NOT NEW.content_hash
          OR OLD.indexing_level IS NOT NEW.indexing_level OR OLD.deleted_utc_ticks IS NOT NEW.deleted_utc_ticks BEGIN
            DELETE FROM index_vector_documents WHERE file_id = NEW.id;
        END;
        CREATE TRIGGER IF NOT EXISTS vector_content_updated AFTER UPDATE ON index_content BEGIN
            DELETE FROM index_vector_documents WHERE file_id IN (SELECT id FROM index_files WHERE content_hash = NEW.content_hash);
        END;
        CREATE TRIGGER IF NOT EXISTS vector_content_deleted BEFORE DELETE ON index_content BEGIN
            DELETE FROM index_vector_documents WHERE file_id IN (SELECT id FROM index_files WHERE content_hash = OLD.content_hash);
        END;
        CREATE TRIGGER IF NOT EXISTS vector_media_updated AFTER UPDATE ON index_media_content BEGIN
            DELETE FROM index_vector_documents WHERE file_id IN (SELECT id FROM index_files WHERE content_hash = NEW.content_hash);
        END;
        CREATE TRIGGER IF NOT EXISTS vector_media_deleted BEFORE DELETE ON index_media_content BEGIN
            DELETE FROM index_vector_documents WHERE file_id IN (SELECT id FROM index_files WHERE content_hash = OLD.content_hash);
        END;
        CREATE TRIGGER IF NOT EXISTS vector_privacy_inserted AFTER INSERT ON index_privacy_rules BEGIN
            DELETE FROM index_vector_documents WHERE file_id IN (SELECT id FROM index_files WHERE source_id = NEW.source_id AND relative_path_key = NEW.relative_path_key);
        END;
        CREATE TRIGGER IF NOT EXISTS vector_privacy_updated AFTER UPDATE ON index_privacy_rules BEGIN
            DELETE FROM index_vector_documents WHERE file_id IN (SELECT id FROM index_files WHERE source_id = NEW.source_id AND relative_path_key = NEW.relative_path_key);
        END;
        CREATE TRIGGER IF NOT EXISTS vector_source_updated AFTER UPDATE ON index_sources
        WHEN OLD.enabled IS NOT NEW.enabled OR OLD.indexing_level IS NOT NEW.indexing_level OR OLD.root_path IS NOT NEW.root_path BEGIN
            DELETE FROM index_vector_documents WHERE file_id IN (SELECT id FROM index_files WHERE source_id = NEW.id);
        END;
        """;

    private const string VectorJoins = """
        FROM index_files f JOIN index_sources s ON s.id = f.source_id
        LEFT JOIN index_content c ON c.content_hash = f.content_hash
        LEFT JOIN index_media_content m ON m.content_hash = f.content_hash
        LEFT JOIN index_privacy_rules p ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
        """;
    private const string VectorEligible = """
        f.deleted_utc_ticks IS NULL AND s.enabled = 1 AND f.indexing_level > 0
        AND COALESCE(p.indexing_level_override,s.indexing_level) > 0
        AND COALESCE(p.is_excluded, 0) = 0 AND COALESCE(p.suppress_semantic, 0) = 0
        """;
    private const string VectorStamp = """
        (s.root_path || '|' || f.full_path || '|' || f.metadata_fingerprint || '|' || f.processor_fingerprint || '|' ||
        COALESCE(f.content_hash,'') || '|' ||
        COALESCE(c.updated_utc_ticks,0) || '|' || COALESCE(m.updated_utc_ticks,0) || '|' ||
        COALESCE(p.updated_utc_ticks,0) || '|' || f.indexing_level)
        """;

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorIndexDocument>> GetVectorIndexCandidatesAsync(SemanticModelIdentity model,
        int maximumCount, CancellationToken cancellationToken = default)
    {
        ValidateVectorModel(model);
        if (maximumCount is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        return RunExclusiveAsync<IReadOnlyList<VectorIndexDocument>>(() =>
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT f.id, {VectorStamp}, f.relative_path, c.extracted_text,
                    CASE WHEN COALESCE(p.suppress_ocr,0) = 0 THEN c.ocr_text END,
                    CASE WHEN COALESCE(p.suppress_summary,0) = 0 THEN c.summary END,
                    c.content_intelligence_json, m.evidence_json, COALESCE(p.suppress_ocr,0), COALESCE(p.suppress_summary,0)
                {VectorJoins}
                LEFT JOIN index_vector_documents v ON v.file_id = f.id
                LEFT JOIN index_vector_failures vf ON vf.file_id = f.id AND vf.model_key = $model AND vf.source_stamp = {VectorStamp}
                WHERE {VectorEligible} AND (v.file_id IS NULL OR v.model_key <> $model OR v.source_stamp <> {VectorStamp})
                  AND (vf.file_id IS NULL OR (vf.attempts < 3 AND vf.next_retry_utc_ticks <= $now))
                ORDER BY s.priority DESC, f.id LIMIT $maximum;
                """;
            AddParameters(command, ("$model", model.Key), ("$maximum", maximumCount), ("$now", DateTimeOffset.UtcNow.UtcTicks));
            using var reader = command.ExecuteReader();
            var result = new List<VectorIndexDocument>();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fields = new List<VectorSourceField> { new("metadata.path", reader.GetString(2)) };
                AddVectorField(fields, "native.text", reader, 3);
                AddVectorField(fields, "ocr.text", reader, 4);
                var summaryField = "derived.summary";
                if (!reader.IsDBNull(6))
                {
                    try
                    {
                        using var intelligence = JsonDocument.Parse(reader.GetString(6));
                        if (intelligence.RootElement.TryGetProperty("Origin", out var origin) &&
                            (origin.ValueKind == JsonValueKind.Number && origin.GetInt32() == 1 ||
                             origin.ValueKind == JsonValueKind.String && origin.GetString() == "AiDerived")) summaryField = "ai.summary";
                    }
                    catch (JsonException) { }
                }
                AddVectorField(fields, summaryField, reader, 5);
                if (!reader.IsDBNull(7))
                {
                    try
                    {
                        var media = JsonSerializer.Deserialize<IndexedMediaEvidence>(reader.GetString(7));
                        if (media is not null)
                        {
                            AddVectorField(fields, "media.transcript", media.Transcript);
                            if (!reader.GetBoolean(8)) AddVectorField(fields, "media.ocr", media.OcrText);
                            if (!reader.GetBoolean(9)) AddVectorField(fields, "ai.visual-description", media.VisualDescription);
                        }
                    }
                    catch (JsonException) { }
                }
                result.Add(new(reader.GetString(0), reader.GetString(1), fields));
            }
            return result;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ReplaceVectorsAsync(VectorIndexDocument document, SemanticModelIdentity model,
        IReadOnlyList<VectorChunkEmbedding> chunks, long maximumBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateVectorModel(model);
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count is < 1 or > VectorTextChunker.MaximumChunks || maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(chunks));
        var expected = VectorTextChunker.Create(document);
        if (expected.Count != chunks.Count || chunks.Select(item => item.Chunk.ChunkId).Distinct(StringComparer.Ordinal).Count() != chunks.Count)
            throw new ArgumentException("The complete deterministic chunk set is required.", nameof(chunks));
        for (var index = 0; index < chunks.Count; index++)
        {
            if (chunks[index].Chunk != expected[index]) throw new ArgumentException("The chunk provenance does not match the snapshot.", nameof(chunks));
            ValidateVector(chunks[index].Vector, model.Dimensions);
        }
        var bytes = chunks.Sum(item => item.Vector.Count * 4L + item.Chunk.Text.Length * 2L + 256);
        return RunExclusiveAsync(() =>
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = $"SELECT {VectorStamp} {VectorJoins} WHERE {VectorEligible} AND f.id = $file;";
            check.Parameters.AddWithValue("$file", document.FileId);
            if (!string.Equals(check.ExecuteScalar() as string, document.SourceFingerprint, StringComparison.Ordinal)) return false;
            var stored = VectorScalarInt64(connection, "SELECT COALESCE(SUM(byte_count),0) FROM index_vector_documents;", transaction);
            using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "SELECT byte_count FROM index_vector_documents WHERE file_id = $file;";
            existing.Parameters.AddWithValue("$file", document.FileId);
            var oldBytes = Convert.ToInt64(existing.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
            if (stored - oldBytes + bytes > maximumBytes) return false;
            ExecuteNonQuery(connection, transaction, "DELETE FROM index_vector_documents WHERE file_id = $file;", ("$file", document.FileId));
            ExecuteNonQuery(connection, transaction, """
                INSERT INTO index_vector_documents(file_id,model_key,source_stamp,byte_count,updated_utc_ticks)
                VALUES($file,$model,$stamp,$bytes,$now);
                """, ("$file", document.FileId), ("$model", model.Key), ("$stamp", document.SourceFingerprint),
                ("$bytes", bytes), ("$now", DateTimeOffset.UtcNow.UtcTicks));
            foreach (var item in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var data = new byte[item.Vector.Count * sizeof(float)];
                for (var index = 0; index < item.Vector.Count; index++)
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(index * sizeof(float)), item.Vector[index]);
                ExecuteNonQuery(connection, transaction, """
                    INSERT INTO index_vector_chunks(chunk_id,file_id,ordinal,source_field,source_start,source_length,chunk_text,vector)
                    VALUES($chunk,$file,$ordinal,$field,$start,$length,$text,$vector);
                    """, ("$chunk", item.Chunk.ChunkId), ("$file", document.FileId), ("$ordinal", item.Chunk.Ordinal),
                    ("$field", item.Chunk.Field), ("$start", item.Chunk.Start), ("$length", item.Chunk.Length),
                    ("$text", item.Chunk.Text), ("$vector", data));
            }
            ExecuteNonQuery(connection, transaction, "DELETE FROM index_vector_failures WHERE file_id = $file;", ("$file", document.FileId));
            transaction.Commit();
            return true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VectorSearchResult> SearchVectorsAsync(SemanticModelIdentity model, IReadOnlyList<float> query,
        DiscoverySearchRequest request, CancellationToken cancellationToken = default)
    {
        ValidateVectorModel(model);
        ValidateVector(query, model.Dimensions);
        ValidateVectorRequest(request);
        return RunExclusiveAsync(() =>
        {
            using var connection = OpenConnection();
            return SearchVectorsCore(connection, model, query, request, null, cancellationToken);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VectorSearchResult> SearchRelatedVectorsAsync(SemanticModelIdentity model, string fileId,
        DiscoverySearchRequest request, CancellationToken cancellationToken = default)
    {
        ValidateVectorModel(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ValidateVectorRequest(request);
        return RunExclusiveAsync(() =>
        {
            using var connection = OpenConnection();
            using var seed = connection.CreateCommand();
            seed.CommandText = $"""
                SELECT k.vector, v.source_stamp {VectorJoins}
                JOIN index_vector_documents v ON v.file_id = f.id AND v.model_key = $model AND v.source_stamp = {VectorStamp}
                JOIN index_vector_chunks k ON k.file_id = f.id
                WHERE {VectorEligible} AND COALESCE(p.suppress_relationships,0) = 0 AND f.id = $file;
                """;
            AddParameters(seed, ("$model", model.Key), ("$file", fileId));
            var centroid = new float[model.Dimensions];
            var count = 0;
            string? sourceFingerprint = null;
            using (var reader = seed.ExecuteReader())
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var vector = ReadVector((byte[])reader.GetValue(0), model.Dimensions);
                    if (vector is null) continue;
                    sourceFingerprint = reader.GetString(1);
                    for (var index = 0; index < vector.Length; index++) centroid[index] += vector[index];
                    count++;
                }
            }
            if (count == 0 || centroid.All(value => value == 0)) return new VectorSearchResult([], 0, 0, false);
            return SearchVectorsCore(connection, model, centroid, request, fileId, cancellationToken)
                with
            { RelatedSourceFingerprint = sourceFingerprint };
        }, cancellationToken);
    }

    private VectorSearchResult SearchVectorsCore(SqliteConnection connection, SemanticModelIdentity model,
        IReadOnlyList<float> query, DiscoverySearchRequest request, string? relatedFileId, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        var clauses = BuildDiscoveryFilterClauses(command, request.Filters);
        var relationshipClause = VectorRelationshipClause(command, relatedFileId);
        command.CommandText = $"""
            SELECT f.id, k.chunk_id, k.source_field, k.source_start, k.source_length, k.chunk_text, k.vector, v.source_stamp
            {VectorJoins}
            LEFT JOIN index_vector_documents v ON v.file_id = f.id AND v.model_key = $model AND v.source_stamp = {VectorStamp}
            LEFT JOIN index_vector_chunks k ON k.file_id = v.file_id
            WHERE {VectorEligible} {clauses} {relationshipClause}
            ORDER BY f.id, k.ordinal;
            """;
        command.Parameters.AddWithValue("$model", model.Key);
        using var reader = command.ExecuteReader();
        // Stream all eligible vectors: bounds apply after policy and filters, never to an arbitrary prefix.
        var top = new PriorityQueue<VectorSearchMatch, (double Similarity, string FileId)>();
        string? file = null;
        VectorSearchMatch? best = null;
        long eligible = 0, indexed = 0, matched = 0;
        var hasVector = false;
        void FinishFile()
        {
            if (file is null) return;
            eligible++;
            if (hasVector) indexed++;
            if (best is null || best.Similarity <= 0) return;
            matched++;
            top.Enqueue(best, (best.Similarity, best.FileId));
            if (top.Count > request.MaximumCandidateCount) top.Dequeue();
        }
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextFile = reader.GetString(0);
            if (file != nextFile) { FinishFile(); file = nextFile; best = null; hasVector = false; }
            if (reader.IsDBNull(1)) continue;
            var vector = ReadVector((byte[])reader.GetValue(6), model.Dimensions);
            if (vector is null) continue;
            hasVector = true;
            var score = VectorCosine(query, vector);
            if (best is null || score > best.Similarity)
                best = new(nextFile, reader.GetString(1), score, reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5))
                { SourceFingerprint = reader.GetString(7) };
        }
        FinishFile();
        var matches = top.UnorderedItems.Select(item => item.Element).OrderByDescending(item => item.Similarity)
            .ThenBy(item => item.FileId, StringComparer.Ordinal).ToArray();
        return new(matches, eligible, indexed, matched > matches.Length);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorSearchMatch>> ValidateVectorMatchesAsync(SemanticModelIdentity model,
        VectorSearchResult result, DiscoverySearchRequest request, string? relatedFileId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateVectorModel(model);
        ValidateVectorRequest(request);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Matches.Count > 1000) throw new ArgumentOutOfRangeException(nameof(result));
        if (result.Matches.Count == 0 || relatedFileId is not null && result.RelatedSourceFingerprint is null)
            return Task.FromResult<IReadOnlyList<VectorSearchMatch>>([]);
        return RunExclusiveAsync<IReadOnlyList<VectorSearchMatch>>(() =>
        {
            using var connection = OpenConnection();
            // One read transaction fences the source and every target against the same catalog state.
            using var transaction = connection.BeginTransaction();
            if (relatedFileId is not null)
            {
                using var source = connection.CreateCommand();
                source.Transaction = transaction;
                source.CommandText = $"""
                    SELECT v.source_stamp {VectorJoins}
                    JOIN index_vector_documents v ON v.file_id=f.id AND v.model_key=$model AND v.source_stamp={VectorStamp}
                    WHERE {VectorEligible} AND COALESCE(p.suppress_relationships,0)=0 AND f.id=$seed;
                    """;
                AddParameters(source, ("$model", model.Key), ("$seed", relatedFileId));
                if (!string.Equals(source.ExecuteScalar() as string, result.RelatedSourceFingerprint, StringComparison.Ordinal)) return [];
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var filters = BuildDiscoveryFilterClauses(command, request.Filters);
            var relationships = VectorRelationshipClause(command, relatedFileId);
            command.CommandText = $"""
                SELECT f.id, k.chunk_id, v.source_stamp {VectorJoins}
                JOIN index_vector_documents v ON v.file_id=f.id AND v.model_key=$model AND v.source_stamp={VectorStamp}
                JOIN index_vector_chunks k ON k.file_id=f.id
                JOIN json_each($matches) requested ON json_extract(requested.value,'$.file')=f.id
                    AND json_extract(requested.value,'$.chunk')=k.chunk_id AND json_extract(requested.value,'$.stamp')=v.source_stamp
                WHERE {VectorEligible} {filters} {relationships};
                """;
            AddParameters(command, ("$model", model.Key), ("$matches", JsonSerializer.Serialize(result.Matches.Select(match =>
                new { file = match.FileId, chunk = match.ChunkId, stamp = match.SourceFingerprint }))));
            var current = new HashSet<(string File, string Chunk, string Stamp)>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    current.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
                }
            }
            transaction.Commit();
            return result.Matches.Where(match => match.SourceFingerprint is { } stamp && current.Contains((match.FileId, match.ChunkId, stamp))).ToArray();
        }, cancellationToken);
    }

    private static string VectorRelationshipClause(SqliteCommand command, string? relatedFileId)
    {
        if (relatedFileId is null) return string.Empty;
        AddParameters(command, ("$seed", relatedFileId), ("$rejected", (int)RelationshipDecision.Rejected), ("$never", (int)RelationshipDecision.NeverRelate));
        return """
            AND f.id <> $seed AND COALESCE(p.suppress_relationships,0) = 0
            AND NOT EXISTS (SELECT 1 FROM relationship_pair_overrides o
                WHERE ((o.first_file_id = $seed AND o.second_file_id = f.id) OR (o.second_file_id = $seed AND o.first_file_id = f.id))
                AND o.decision IN ($rejected,$never))
            AND NOT EXISTS (SELECT 1 FROM index_relationships r
                WHERE ((r.first_file_id = $seed AND r.second_file_id = f.id) OR (r.second_file_id = $seed AND r.first_file_id = f.id))
                AND r.decision IN ($rejected,$never))
            """;
    }

    /// <inheritdoc />
    public Task<VectorStorageStatus> GetVectorStatusAsync(SemanticModelIdentity model, CancellationToken cancellationToken = default)
    {
        ValidateVectorModel(model);
        return RunExclusiveAsync(() =>
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT COUNT(*), COALESCE(SUM(CASE WHEN v.file_id IS NOT NULL THEN 1 ELSE 0 END),0),
                    COALESCE(SUM(CASE WHEN vf.file_id IS NOT NULL AND v.file_id IS NULL THEN 1 ELSE 0 END),0)
                {VectorJoins} LEFT JOIN index_vector_documents v ON v.file_id = f.id AND v.model_key = $model AND v.source_stamp = {VectorStamp}
                LEFT JOIN index_vector_failures vf ON vf.file_id = f.id AND vf.model_key = $model AND vf.source_stamp = {VectorStamp}
                WHERE {VectorEligible};
                """;
            command.Parameters.AddWithValue("$model", model.Key);
            long eligible, indexed, failed;
            using (var reader = command.ExecuteReader()) { reader.Read(); eligible = reader.GetInt64(0); indexed = reader.GetInt64(1); failed = reader.GetInt64(2); }
            return new VectorStorageStatus(indexed, eligible - indexed,
                VectorScalarInt64(connection, "SELECT COALESCE(SUM(byte_count),0) FROM index_vector_documents;"))
            { FailedFiles = failed };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearVectorsAsync(CancellationToken cancellationToken = default) => RunExclusiveAsync(() =>
    {
        using var connection = OpenConnection();
        return ExecuteNonQuery(connection, null, "DELETE FROM index_vector_documents; DELETE FROM index_vector_failures;");
    }, cancellationToken);

    /// <inheritdoc />
    public Task PruneVectorsAsync(SemanticModelIdentity model, long maximumBytes, CancellationToken cancellationToken = default)
    {
        ValidateVectorModel(model);
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        return RunExclusiveAsync(() =>
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, $"""
                DELETE FROM index_vector_documents WHERE model_key <> $model OR file_id NOT IN
                    (SELECT f.id {VectorJoins} WHERE {VectorEligible} AND source_stamp = {VectorStamp});
                DELETE FROM index_vector_failures WHERE model_key <> $model OR file_id NOT IN
                    (SELECT f.id {VectorJoins} WHERE {VectorEligible} AND source_stamp = {VectorStamp});
                """, ("$model", model.Key));
            // Keep the newest complete documents within the budget; never retain partial vectors.
            ExecuteNonQuery(connection, transaction, """
                DELETE FROM index_vector_documents WHERE file_id IN (
                    SELECT file_id FROM (SELECT file_id, SUM(byte_count) OVER (ORDER BY updated_utc_ticks DESC,file_id) AS running_bytes
                        FROM index_vector_documents) WHERE running_bytes > $maximum);
                """, ("$maximum", maximumBytes));
            transaction.Commit();
            return 0;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordVectorFailureAsync(VectorIndexDocument document, SemanticModelIdentity model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateVectorModel(model);
        return RunExclusiveAsync(() =>
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = $"SELECT {VectorStamp} {VectorJoins} WHERE {VectorEligible} AND f.id = $file;";
            check.Parameters.AddWithValue("$file", document.FileId);
            if (!string.Equals(check.ExecuteScalar() as string, document.SourceFingerprint, StringComparison.Ordinal)) return 0;
            ExecuteNonQuery(connection, transaction, """
                INSERT INTO index_vector_failures(file_id,model_key,source_stamp,attempts,next_retry_utc_ticks)
                VALUES($file,$model,$stamp,1,$retry)
                ON CONFLICT(file_id) DO UPDATE SET model_key=excluded.model_key,source_stamp=excluded.source_stamp,
                    attempts=CASE WHEN model_key=$model AND source_stamp=$stamp THEN MIN(3,attempts+1) ELSE 1 END,
                    next_retry_utc_ticks=$retry;
                """, ("$file", document.FileId), ("$model", model.Key), ("$stamp", document.SourceFingerprint),
                ("$retry", DateTimeOffset.UtcNow.AddMinutes(2).UtcTicks));
            transaction.Commit();
            return 0;
        }, cancellationToken);
    }

    private static void AddVectorField(List<VectorSourceField> fields, string name, SqliteDataReader reader, int ordinal)
    { if (!reader.IsDBNull(ordinal)) AddVectorField(fields, name, reader.GetString(ordinal)); }
    private static void AddVectorField(List<VectorSourceField> fields, string name, string? text)
    { if (!string.IsNullOrWhiteSpace(text)) fields.Add(new(name, text)); }
    private static long VectorScalarInt64(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }
    private static void ValidateVectorModel(SemanticModelIdentity model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Provider) || string.IsNullOrWhiteSpace(model.Model) || string.IsNullOrWhiteSpace(model.Revision) ||
            model.Dimensions is < 1 or > 8192) throw new ArgumentException("The vector model identity is invalid.", nameof(model));
    }
    private static void ValidateVector(IReadOnlyList<float> vector, int dimensions)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Count != dimensions || vector.Any(value => !float.IsFinite(value)) || vector.All(value => value == 0))
            throw new ArgumentException("A finite, nonzero vector in the selected model space is required.", nameof(vector));
    }
    private static void ValidateVectorRequest(DiscoverySearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumCandidateCount is < 1 or > 1000 || request.Filters.Count > SearchLimits.MaximumFilters || request.TopicText.Length > SearchLimits.MaximumQueryCharacters)
            throw new ArgumentOutOfRangeException(nameof(request));
    }
    private static float[]? ReadVector(byte[] bytes, int dimensions)
    {
        if (bytes.Length != dimensions * sizeof(float)) return null;
        var vector = new float[dimensions];
        for (var index = 0; index < dimensions; index++)
        {
            vector[index] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * sizeof(float)));
            if (!float.IsFinite(vector[index])) return null;
        }
        return vector.Any(value => value != 0) ? vector : null;
    }
    private static double VectorCosine(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        double dot = 0, normFirst = 0, normSecond = 0;
        for (var index = 0; index < first.Count; index++)
        { dot += (double)first[index] * second[index]; normFirst += (double)first[index] * first[index]; normSecond += (double)second[index] * second[index]; }
        return normFirst > 0 && normSecond > 0 ? Math.Clamp(dot / Math.Sqrt(normFirst * normSecond), -1, 1) : 0;
    }
}
