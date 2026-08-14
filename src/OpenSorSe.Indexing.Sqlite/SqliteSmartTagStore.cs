using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Indexing.Sqlite;

public sealed partial class SqliteDeepIndexStore
{
    private const int MaximumSmartTagFilterResults = 100_000;
    private const int MaximumSmartTagBatchFiles = 100_000;
    private const int MaximumSmartTagEvidenceJsonCharacters = 4096;
    private const string LegacySmartTagImportMetaKey = "smart_tags_legacy_import_complete";

    /// <inheritdoc />
    public Task<string?> ResolveActiveFileIdAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var pathKey = PathKey(fullPath);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return ExecuteScalar(
                    connection,
                    "SELECT id FROM index_files WHERE path_key = $path AND deleted_utc_ticks IS NULL LIMIT 1;",
                    ("$path", pathKey)) as string;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PrepareStaleClassificationsAsync(
        string classifierVersion,
        string taxonomyVersion,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classifierVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxonomyVersion);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    SELECT f.id, f.source_id, f.relative_path_key, f.relative_path
                    FROM index_files f
                    LEFT JOIN file_smart_tag_status s ON s.file_id = f.id
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
                    WHERE f.deleted_utc_ticks IS NULL
                      AND f.indexing_level <> $basic
                      AND (s.file_id IS NULL OR s.classifier_version <> $classifier OR s.taxonomy_version <> $taxonomy)
                      AND NOT (COALESCE(p.repair_stage, -1) = $stage AND COALESCE(p.force_reprocess, 0) = 1)
                    ORDER BY f.source_id, f.relative_path_key, f.id;
                    """;
                command.Parameters.AddWithValue("$basic", (int)IndexingLevel.Basic);
                command.Parameters.AddWithValue("$classifier", Bound(classifierVersion, 64));
                command.Parameters.AddWithValue("$taxonomy", Bound(taxonomyVersion, 32));
                command.Parameters.AddWithValue("$stage", (int)IndexingStage.SmartTagsClassified);
                var identities = new List<FileIdentity>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        identities.Add(new FileIdentity(
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetString(2),
                            reader.GetString(3)));
                    }
                }

                foreach (var identity in identities)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        UPDATE file_smart_tag_assignments
                        SET active = CASE WHEN EXISTS (
                                SELECT 1 FROM file_smart_tag_decisions d
                                WHERE d.file_id = file_smart_tag_assignments.file_id
                                  AND d.tag_id = file_smart_tag_assignments.tag_id
                                  AND d.decision = $accepted
                            ) THEN 1 ELSE 0 END,
                            updated_utc_ticks = $now
                        WHERE file_id = $file AND origin <> $user;
                        DELETE FROM file_smart_tag_status WHERE file_id = $file;
                        DELETE FROM index_stage_states WHERE file_id = $file AND stage = $stage;
                        """,
                        ("$accepted", (int)SmartTagDecision.Accepted),
                        ("$now", changedAtUtc.UtcTicks),
                        ("$file", identity.FileId),
                        ("$user", (int)SmartTagOrigin.User),
                        ("$stage", (int)IndexingStage.SmartTagsClassified));
                    UpsertPrivacyRule(
                        connection,
                        transaction,
                        identity,
                        new IndexPrivacyPolicyChange(),
                        changedAtUtc,
                        IndexingStage.SmartTagsClassified,
                        forceReprocess: true);
                }

                transaction.Commit();
                return identities.Count;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SmartTagDefinition>> GetSmartTagDefinitionsAsync(
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync<IReadOnlyList<SmartTagDefinition>>(
            () =>
            {
                using var connection = OpenConnection();
                return ReadSmartTagDefinitions(connection);
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileSmartTag>> GetFileSmartTagsAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        ValidateFileId(fileId);
        var result = await GetFileSmartTagsAsync([fileId], cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(fileId, out var tags) ? tags : [];
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, IReadOnlyList<FileSmartTag>>> GetFileSmartTagsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        var bounded = fileIds.Distinct(StringComparer.Ordinal).ToArray();
        if (bounded.Length > MaximumSmartTagBatchFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIds));
        }

        foreach (var fileId in bounded)
        {
            ValidateFileId(fileId);
        }

        return RunExclusiveAsync<IReadOnlyDictionary<string, IReadOnlyList<FileSmartTag>>>(
            () =>
            {
                using var connection = OpenConnection();
                return ReadEffectiveSmartTags(connection, bounded);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SmartTagOperationResult> AddUserTagAsync(
        string fileId,
        string displayName,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateFileId(fileId);
        var display = SmartTagUserInput.NormalizeDisplayName(displayName);
        var normalized = SmartTagUserInput.NormalizeCanonicalKey(display);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                EnsureActiveFile(connection, transaction, fileId);
                var tagId = UserTagId(normalized);
                var existing = Convert.ToInt32(ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM file_smart_tag_assignments WHERE file_id = $file AND tag_id = $tag AND active = 1;",
                    ("$file", fileId),
                    ("$tag", tagId)), CultureInfo.InvariantCulture);
                if (existing == 1)
                {
                    transaction.Commit();
                    return new SmartTagOperationResult(false, 0, "That user tag is already associated with this file.");
                }

                var count = Convert.ToInt32(ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM file_smart_tag_assignments a JOIN smart_tag_definitions d ON d.tag_id = a.tag_id WHERE a.file_id = $file AND a.active = 1 AND d.tag_type = $type;",
                    ("$file", fileId),
                    ("$type", (int)SmartTagType.UserTag)), CultureInfo.InvariantCulture);
                if (count >= SmartTagLimits.MaximumUserTagsPerFile)
                {
                    throw new InvalidOperationException($"A file can have at most {SmartTagLimits.MaximumUserTagsPerFile} user tags.");
                }

                UpsertUserDefinition(connection, transaction, tagId, normalized, display, changedAtUtc);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO file_smart_tag_assignments(
                        file_id, tag_id, confidence, evidence_score, origin, classifier,
                        classifier_version, taxonomy_version, input_fingerprint, evidence_json,
                        assignment_state, active, created_utc_ticks, updated_utc_ticks)
                    VALUES($file, $tag, $confidence, NULL, $origin, 'User', '1', 'user', 'user', '[]', $state, 1, $now, $now)
                    ON CONFLICT(file_id, tag_id) DO UPDATE SET
                        confidence = excluded.confidence,
                        origin = excluded.origin,
                        classifier = excluded.classifier,
                        classifier_version = excluded.classifier_version,
                        taxonomy_version = excluded.taxonomy_version,
                        input_fingerprint = excluded.input_fingerprint,
                        evidence_json = excluded.evidence_json,
                        assignment_state = excluded.assignment_state,
                        active = 1,
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$file", fileId),
                    ("$tag", tagId),
                    ("$confidence", (int)ContentIntelligenceConfidence.Strong),
                    ("$origin", (int)SmartTagOrigin.User),
                    ("$state", (int)SmartTagAssignmentState.Accepted),
                    ("$now", changedAtUtc.UtcTicks));
                transaction.Commit();
                return new SmartTagOperationResult(true, 1, $"The local user tag “{display}” was added.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SmartTagOperationResult> RemoveTagAsync(
        string fileId,
        string tagId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateFileId(fileId);
        ValidateTagId(tagId);
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var originValue = ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT origin FROM file_smart_tag_assignments WHERE file_id = $file AND tag_id = $tag;",
                    ("$file", fileId),
                    ("$tag", tagId));
                if (originValue is null or DBNull)
                {
                    transaction.Commit();
                    return new SmartTagOperationResult(false, 0, "The Smart Tag assignment no longer exists.");
                }

                var origin = (SmartTagOrigin)Convert.ToInt32(originValue, CultureInfo.InvariantCulture);
                if (origin == SmartTagOrigin.User)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM file_smart_tag_assignments WHERE file_id = $file AND tag_id = $tag; DELETE FROM file_smart_tag_decisions WHERE file_id = $file AND tag_id = $tag;",
                        ("$file", fileId),
                        ("$tag", tagId));
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM smart_tag_definitions WHERE tag_id = $tag AND is_builtin = 0 AND NOT EXISTS (SELECT 1 FROM file_smart_tag_assignments WHERE tag_id = $tag);",
                        ("$tag", tagId));
                }
                else
                {
                    UpsertDecision(connection, transaction, fileId, tagId, SmartTagDecision.Rejected, changedAtUtc);
                }

                transaction.Commit();
                return new SmartTagOperationResult(true, 1, origin == SmartTagOrigin.User
                    ? "The user tag was removed."
                    : "The generated Smart Tag was rejected and will stay hidden until decisions are reset.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SmartTagOperationResult> SetTagDecisionAsync(
        string fileId,
        string tagId,
        SmartTagDecision decision,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateFileId(fileId);
        ValidateTagId(tagId);
        if (decision is not (SmartTagDecision.Accepted or SmartTagDecision.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var count = Convert.ToInt32(ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM file_smart_tag_assignments WHERE file_id = $file AND tag_id = $tag AND active = 1;",
                    ("$file", fileId),
                    ("$tag", tagId)), CultureInfo.InvariantCulture);
                if (count != 1)
                {
                    transaction.Commit();
                    return new SmartTagOperationResult(false, 0, "The generated Smart Tag is no longer available for review.");
                }

                UpsertDecision(connection, transaction, fileId, tagId, decision, changedAtUtc);
                transaction.Commit();
                return new SmartTagOperationResult(true, 1, decision == SmartTagDecision.Accepted
                    ? "The Smart Tag was accepted and is now user-authorized evidence."
                    : "The Smart Tag was rejected and will not be suggested again until decisions are reset.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SmartTagOperationResult> ResetTagDecisionsAsync(
        string? fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (fileId is not null)
        {
            ValidateFileId(fileId);
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var count = ExecuteNonQuery(
                    connection,
                    transaction,
                    fileId is null
                        ? "DELETE FROM file_smart_tag_decisions;"
                        : "DELETE FROM file_smart_tag_decisions WHERE file_id = $file;",
                    ("$file", fileId));
                if (fileId is not null)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "UPDATE index_files SET updated_utc_ticks = $now WHERE id = $file;",
                        ("$now", changedAtUtc.UtcTicks),
                        ("$file", fileId));
                }

                transaction.Commit();
                return new SmartTagOperationResult(count > 0, count, count > 0
                    ? "Smart Tag decisions were reset. Current generated evidence may be reviewed again."
                    : "No Smart Tag decisions required resetting.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SmartTagOperationResult> ClearGeneratedSmartTagsAsync(
        string? fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (fileId is not null)
        {
            ValidateFileId(fileId);
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var predicate = fileId is null ? string.Empty : " AND file_id = $file";
                var count = ExecuteNonQuery(
                    connection,
                    transaction,
                    $"""
                    DELETE FROM file_smart_tag_assignments
                    WHERE origin <> $user{predicate}
                      AND NOT EXISTS (
                          SELECT 1 FROM file_smart_tag_decisions d
                          WHERE d.file_id = file_smart_tag_assignments.file_id
                            AND d.tag_id = file_smart_tag_assignments.tag_id
                            AND d.decision = $accepted
                      );
                    """,
                    ("$user", (int)SmartTagOrigin.User),
                    ("$accepted", (int)SmartTagDecision.Accepted),
                    ("$file", fileId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    fileId is null
                        ? "DELETE FROM file_smart_tag_status;"
                        : "DELETE FROM file_smart_tag_status WHERE file_id = $file;",
                    ("$file", fileId));
                transaction.Commit();
                return new SmartTagOperationResult(count > 0, count, count > 0
                    ? "Generated Smart Tags were cleared. User tags, accepted authority, and rejections were preserved."
                    : "No generated Smart Tags required clearing.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> FilterFileIdsBySmartTagsAsync(
        SmartTagFilter filter,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (maximumCount is < 1 or > MaximumSmartTagFilterResults)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var groups = new[]
        {
            NormalizeFilter(filter.ThemeTagIds, SmartTagType.Theme),
            NormalizeFilter(filter.DocumentTypeTagIds, SmartTagType.DocumentType),
            NormalizeFilter(filter.UserTagIds, SmartTagType.UserTag),
        }.Where(item => item.Ids.Length > 0).ToArray();

        return RunExclusiveAsync<IReadOnlyList<string>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                var clauses = new List<string>();
                for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var group = groups[groupIndex];
                    var names = new List<string>();
                    for (var idIndex = 0; idIndex < group.Ids.Length; idIndex++)
                    {
                        var name = $"$g{groupIndex}_{idIndex}";
                        names.Add(name);
                        command.Parameters.AddWithValue(name, group.Ids[idIndex]);
                    }

                    clauses.Add($"""
                        EXISTS (
                            SELECT 1
                            FROM file_smart_tag_assignments a
                            JOIN smart_tag_definitions d ON d.tag_id = a.tag_id
                            LEFT JOIN file_smart_tag_decisions x ON x.file_id = a.file_id AND x.tag_id = a.tag_id
                            WHERE a.file_id = f.id AND a.active = 1
                              AND d.tag_type = {(int)group.Type}
                              AND a.tag_id IN ({string.Join(",", names)})
                              AND COALESCE(x.decision, 0) <> {(int)SmartTagDecision.Rejected}
                              AND ({(filter.IncludeSuggestions ? 1 : 0)} = 1
                                   OR x.decision = {(int)SmartTagDecision.Accepted}
                                   OR a.origin = {(int)SmartTagOrigin.User}
                                   OR a.assignment_state = {(int)SmartTagAssignmentState.Automatic})
                        )
                        """);
                }

                command.CommandText = $"""
                    SELECT f.id
                    FROM index_files f
                    WHERE f.deleted_utc_ticks IS NULL
                      {(clauses.Count == 0 ? string.Empty : "AND " + string.Join(" AND ", clauses))}
                    ORDER BY f.id
                    LIMIT $maximum;
                    """;
                command.Parameters.AddWithValue("$maximum", maximumCount);
                using var reader = command.ExecuteReader();
                var result = new List<string>();
                while (reader.Read())
                {
                    result.Add(reader.GetString(0));
                }

                return Array.AsReadOnly(result.ToArray());
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsLegacySmartTagImportCompleteAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                return string.Equals(
                    ExecuteScalar(connection, "SELECT value FROM index_meta WHERE key = $key;", ("$key", LegacySmartTagImportMetaKey)) as string,
                    "1",
                    StringComparison.Ordinal);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<SmartTagOperationResult> ImportLegacySmartTagsAsync(
        IReadOnlyList<LegacySmartTagImport> imports,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imports);
        if (imports.Count > 20_000)
        {
            throw new ArgumentOutOfRangeException(nameof(imports));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (string.Equals(
                    ExecuteScalar(connection, transaction, "SELECT value FROM index_meta WHERE key = $key;", ("$key", LegacySmartTagImportMetaKey)) as string,
                    "1",
                    StringComparison.Ordinal))
                {
                    transaction.Commit();
                    return new SmartTagOperationResult(false, 0, "Legacy Smart Tag authority was already imported.");
                }

                var imported = 0;
                foreach (var item in imports)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(item.FullPath) || !Path.IsPathRooted(item.FullPath))
                    {
                        continue;
                    }

                    var fileIds = ReadActiveFileIdsByPath(connection, transaction, PathKey(item.FullPath));
                    if (fileIds.Count != 1)
                    {
                        continue;
                    }

                    var fileId = fileIds[0];
                    string tagId;
                    if (item.IsUserOwned || string.IsNullOrWhiteSpace(item.BuiltInTagId))
                    {
                        var display = SmartTagUserInput.NormalizeDisplayName(item.DisplayName);
                        var normalized = SmartTagUserInput.NormalizeCanonicalKey(
                            string.IsNullOrWhiteSpace(item.NormalizedValue) ? display : item.NormalizedValue);
                        tagId = UserTagId(normalized);
                        UpsertUserDefinition(connection, transaction, tagId, normalized, display, changedAtUtc);
                        InsertLegacyAcceptedAssignment(connection, transaction, fileId, tagId, SmartTagOrigin.User, changedAtUtc);
                    }
                    else
                    {
                        tagId = item.BuiltInTagId;
                        if (!SmartTagDefinitionExists(connection, transaction, tagId))
                        {
                            continue;
                        }

                        if (item.Decision == SmartTagDecision.Accepted)
                        {
                            InsertLegacyAcceptedAssignment(connection, transaction, fileId, tagId, SmartTagOrigin.DeterministicClassifier, changedAtUtc);
                        }
                    }

                    if (item.Decision is SmartTagDecision.Accepted or SmartTagDecision.Rejected)
                    {
                        UpsertDecision(connection, transaction, fileId, tagId, item.Decision, changedAtUtc);
                    }

                    imported++;
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    "INSERT INTO index_meta(key, value) VALUES($key, '1') ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                    ("$key", LegacySmartTagImportMetaKey));
                transaction.Commit();
                return new SmartTagOperationResult(true, imported,
                    $"Imported {imported.ToString(CultureInfo.InvariantCulture)} safely resolved legacy Smart Tag association(s). Ambiguous path identities were preserved only in their legacy stores.");
            },
            cancellationToken);
    }

    private static void SeedBuiltInSmartTagTaxonomy(SqliteConnection connection, SmartTagTaxonomy taxonomy)
    {
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.UtcTicks;
        foreach (var definition in taxonomy.Definitions.OrderBy(item => item.ParentTagId is null ? 0 : 1))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO smart_tag_definitions(
                    tag_id, tag_type, canonical_key, display_name, parent_tag_id,
                    taxonomy_version, origin, is_builtin, is_hidden, created_utc_ticks, updated_utc_ticks)
                VALUES($id, $type, $key, $display, $parent, $version, $origin, 1, $hidden, $now, $now)
                ON CONFLICT(tag_id) DO UPDATE SET
                    tag_type = excluded.tag_type,
                    canonical_key = excluded.canonical_key,
                    display_name = excluded.display_name,
                    parent_tag_id = excluded.parent_tag_id,
                    taxonomy_version = excluded.taxonomy_version,
                    origin = excluded.origin,
                    is_builtin = 1,
                    updated_utc_ticks = excluded.updated_utc_ticks;
                """,
                ("$id", definition.TagId),
                ("$type", (int)definition.Type),
                ("$key", definition.CanonicalKey),
                ("$display", definition.DisplayName),
                ("$parent", definition.ParentTagId),
                ("$version", definition.TaxonomyVersion),
                ("$origin", (int)definition.Origin),
                ("$hidden", definition.IsHidden ? 1 : 0),
                ("$now", now));
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO index_meta(key, value) VALUES('smart_tag_taxonomy_version', $version) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            ("$version", taxonomy.Version));
        transaction.Commit();
    }

    private static void PersistGeneratedSmartTags(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fileId,
        SmartTagClassificationResult result,
        DateTimeOffset changedAtUtc)
    {
        if (result.Candidates.Count > SmartTagLimits.MaximumThemesPerFile + SmartTagLimits.MaximumDocumentTypesPerFile ||
            result.Candidates.Any(item => item.Evidence.Count > SmartTagLimits.MaximumEvidencePerAssignment))
        {
            throw new InvalidDataException("Generated Smart Tag output exceeds its durable bounds.");
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO file_smart_tag_status(
                file_id, classification_state, classifier, classifier_version,
                taxonomy_version, input_fingerprint, updated_utc_ticks)
            VALUES($file, $state, $classifier, $classifierVersion, $taxonomyVersion, $fingerprint, $now)
            ON CONFLICT(file_id) DO UPDATE SET
                classification_state = excluded.classification_state,
                classifier = excluded.classifier,
                classifier_version = excluded.classifier_version,
                taxonomy_version = excluded.taxonomy_version,
                input_fingerprint = excluded.input_fingerprint,
                updated_utc_ticks = excluded.updated_utc_ticks;
            """,
            ("$file", fileId),
            ("$state", (int)result.State),
            ("$classifier", result.Classifier),
            ("$classifierVersion", result.ClassifierVersion),
            ("$taxonomyVersion", result.TaxonomyVersion),
            ("$fingerprint", result.InputFingerprint),
            ("$now", changedAtUtc.UtcTicks));
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE file_smart_tag_assignments AS a
            SET active = CASE WHEN EXISTS (
                    SELECT 1 FROM file_smart_tag_decisions d
                    WHERE d.file_id = a.file_id AND d.tag_id = a.tag_id AND d.decision = $accepted
                ) THEN 1 ELSE 0 END,
                updated_utc_ticks = $now
            WHERE a.file_id = $file AND a.origin <> $user;
            """,
            ("$accepted", (int)SmartTagDecision.Accepted),
            ("$now", changedAtUtc.UtcTicks),
            ("$file", fileId),
            ("$user", (int)SmartTagOrigin.User));

        foreach (var candidate in result.Candidates)
        {
            if (!SmartTagDefinitionExists(connection, transaction, candidate.TagId))
            {
                throw new InvalidDataException("The classifier returned an unknown taxonomy identity.");
            }

            var evidenceJson = JsonSerializer.Serialize(candidate.Evidence);
            if (evidenceJson.Length > MaximumSmartTagEvidenceJsonCharacters)
            {
                throw new InvalidDataException("Generated Smart Tag evidence exceeds its durable bound.");
            }

            var state = candidate.Origin == SmartTagOrigin.DeterministicClassifier &&
                candidate.Confidence == ContentIntelligenceConfidence.Strong
                    ? SmartTagAssignmentState.Automatic
                    : SmartTagAssignmentState.Suggested;
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO file_smart_tag_assignments(
                    file_id, tag_id, confidence, evidence_score, origin, classifier,
                    classifier_version, taxonomy_version, input_fingerprint, evidence_json,
                    assignment_state, active, created_utc_ticks, updated_utc_ticks)
                VALUES($file, $tag, $confidence, $score, $origin, $classifier,
                    $classifierVersion, $taxonomyVersion, $fingerprint, $evidence,
                    $state, 1, $now, $now)
                ON CONFLICT(file_id, tag_id) DO UPDATE SET
                    confidence = excluded.confidence,
                    evidence_score = excluded.evidence_score,
                    origin = excluded.origin,
                    classifier = excluded.classifier,
                    classifier_version = excluded.classifier_version,
                    taxonomy_version = excluded.taxonomy_version,
                    input_fingerprint = excluded.input_fingerprint,
                    evidence_json = excluded.evidence_json,
                    assignment_state = excluded.assignment_state,
                    active = 1,
                    updated_utc_ticks = excluded.updated_utc_ticks;
                """,
                ("$file", fileId),
                ("$tag", candidate.TagId),
                ("$confidence", (int)candidate.Confidence),
                ("$score", candidate.EvidenceScore),
                ("$origin", (int)candidate.Origin),
                ("$classifier", Bound(candidate.Classifier, 128)),
                ("$classifierVersion", Bound(candidate.ClassifierVersion, 64)),
                ("$taxonomyVersion", Bound(candidate.TaxonomyVersion, 32)),
                ("$fingerprint", Bound(candidate.InputFingerprint, 256)),
                ("$evidence", evidenceJson),
                ("$state", (int)state),
                ("$now", changedAtUtc.UtcTicks));
        }
    }

    private static IReadOnlyList<SmartTagDefinition> ReadSmartTagDefinitions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tag_id, tag_type, canonical_key, display_name, parent_tag_id, taxonomy_version, origin, is_builtin, is_hidden FROM smart_tag_definitions ORDER BY tag_type, canonical_key;";
        using var reader = command.ExecuteReader();
        var result = new List<SmartTagDefinition>();
        while (reader.Read())
        {
            result.Add(new SmartTagDefinition
            {
                TagId = reader.GetString(0),
                Type = (SmartTagType)reader.GetInt32(1),
                CanonicalKey = reader.GetString(2),
                DisplayName = reader.GetString(3),
                ParentTagId = reader.IsDBNull(4) ? null : reader.GetString(4),
                TaxonomyVersion = reader.GetString(5),
                Origin = (SmartTagOrigin)reader.GetInt32(6),
                IsBuiltIn = reader.GetBoolean(7),
                IsHidden = reader.GetBoolean(8),
            });
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private static ProgressiveSearchDocument ApplySearchProjection(
        ProgressiveSearchDocument document,
        IReadOnlyDictionary<string, string> hashes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> chunks,
        IReadOnlyDictionary<string, IReadOnlyList<FileSmartTag>> smartTags)
    {
        var projectedTags = smartTags.TryGetValue(document.FileId, out var values) ? values : [];
        return document with
        {
            SelectedChunks = hashes.TryGetValue(document.FileId, out var hash) && chunks.TryGetValue(hash, out var selected)
                ? selected
                : document.SelectedChunks,
            SmartTags = projectedTags,
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<FileSmartTag>> ReadEffectiveSmartTags(
        SqliteConnection connection,
        IReadOnlyList<string> fileIds)
    {
        if (fileIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<FileSmartTag>>(StringComparer.Ordinal);
        }

        using var command = connection.CreateCommand();
        var parameters = new List<string>();
        for (var index = 0; index < fileIds.Count; index++)
        {
            var name = $"$file{index}";
            parameters.Add(name);
            command.Parameters.AddWithValue(name, fileIds[index]);
        }

        command.CommandText = $"""
            SELECT a.file_id, d.tag_id, d.tag_type, d.canonical_key, d.display_name,
                   d.parent_tag_id, d.taxonomy_version, d.origin, d.is_builtin, d.is_hidden,
                   a.confidence, a.origin, a.assignment_state, COALESCE(x.decision, 0),
                   a.evidence_json, a.updated_utc_ticks
            FROM file_smart_tag_assignments a
            JOIN smart_tag_definitions d ON d.tag_id = a.tag_id
            LEFT JOIN file_smart_tag_decisions x ON x.file_id = a.file_id AND x.tag_id = a.tag_id
            WHERE a.file_id IN ({string.Join(",", parameters)}) AND a.active = 1
            ORDER BY a.file_id, d.tag_type, d.display_name COLLATE NOCASE, d.tag_id;
            """;
        using var reader = command.ExecuteReader();
        var result = fileIds.ToDictionary(
            id => id,
            _ => new List<FileSmartTag>(),
            StringComparer.Ordinal);
        while (reader.Read())
        {
            var decision = (SmartTagDecision)reader.GetInt32(13);
            if (decision == SmartTagDecision.Rejected)
            {
                continue;
            }

            var origin = (SmartTagOrigin)reader.GetInt32(11);
            var storedState = (SmartTagAssignmentState)reader.GetInt32(12);
            var state = decision == SmartTagDecision.Accepted || origin == SmartTagOrigin.User
                ? SmartTagAssignmentState.Accepted
                : storedState;
            var evidence = TryDeserializeSmartTagEvidence(reader.GetString(14));
            result[reader.GetString(0)].Add(new FileSmartTag
            {
                FileId = reader.GetString(0),
                Definition = new SmartTagDefinition
                {
                    TagId = reader.GetString(1),
                    Type = (SmartTagType)reader.GetInt32(2),
                    CanonicalKey = reader.GetString(3),
                    DisplayName = reader.GetString(4),
                    ParentTagId = reader.IsDBNull(5) ? null : reader.GetString(5),
                    TaxonomyVersion = reader.GetString(6),
                    Origin = (SmartTagOrigin)reader.GetInt32(7),
                    IsBuiltIn = reader.GetBoolean(8),
                    IsHidden = reader.GetBoolean(9),
                },
                Confidence = (ContentIntelligenceConfidence)reader.GetInt32(10),
                Origin = origin,
                State = state,
                Decision = decision,
                Evidence = evidence,
                UpdatedAtUtc = new DateTimeOffset(reader.GetInt64(15), TimeSpan.Zero),
            });
        }

        return result.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<FileSmartTag>)Array.AsReadOnly(item.Value.ToArray()),
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<SmartTagEvidence> TryDeserializeSmartTagEvidence(string json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<IReadOnlyList<SmartTagEvidence>>(json) ?? [];
            return values.Count <= SmartTagLimits.MaximumEvidencePerAssignment && values.All(item =>
                item.EvidenceKey.Length <= 128 && item.Explanation.Length <= 256)
                    ? values
                    : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void UpsertDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fileId,
        string tagId,
        SmartTagDecision decision,
        DateTimeOffset changedAtUtc) => ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO file_smart_tag_decisions(file_id, tag_id, decision, reset_generation, changed_utc_ticks)
            VALUES($file, $tag, $decision, 0, $now)
            ON CONFLICT(file_id, tag_id) DO UPDATE SET
                decision = excluded.decision,
                changed_utc_ticks = excluded.changed_utc_ticks;
            """,
            ("$file", fileId),
            ("$tag", tagId),
            ("$decision", (int)decision),
            ("$now", changedAtUtc.UtcTicks));

    private static void UpsertUserDefinition(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tagId,
        string normalized,
        string display,
        DateTimeOffset changedAtUtc) => ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO smart_tag_definitions(
                tag_id, tag_type, canonical_key, display_name, parent_tag_id,
                taxonomy_version, origin, is_builtin, is_hidden, created_utc_ticks, updated_utc_ticks)
            VALUES($id, $type, $key, $display, NULL, 'user', $origin, 0, 0, $now, $now)
            ON CONFLICT(tag_type, canonical_key) DO UPDATE SET updated_utc_ticks = excluded.updated_utc_ticks;
            """,
            ("$id", tagId),
            ("$type", (int)SmartTagType.UserTag),
            ("$key", normalized),
            ("$display", display),
            ("$origin", (int)SmartTagOrigin.User),
            ("$now", changedAtUtc.UtcTicks));

    private static void InsertLegacyAcceptedAssignment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fileId,
        string tagId,
        SmartTagOrigin origin,
        DateTimeOffset changedAtUtc) => ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO file_smart_tag_assignments(
                file_id, tag_id, confidence, evidence_score, origin, classifier,
                classifier_version, taxonomy_version, input_fingerprint, evidence_json,
                assignment_state, active, created_utc_ticks, updated_utc_ticks)
            VALUES($file, $tag, $confidence, NULL, $origin, 'Legacy migration', '1', 'legacy', 'legacy', '[]', $state, 1, $now, $now)
            ON CONFLICT(file_id, tag_id) DO NOTHING;
            """,
            ("$file", fileId),
            ("$tag", tagId),
            ("$confidence", (int)ContentIntelligenceConfidence.Strong),
            ("$origin", (int)origin),
            ("$state", (int)SmartTagAssignmentState.Accepted),
            ("$now", changedAtUtc.UtcTicks));

    private static IReadOnlyList<string> ReadActiveFileIdsByPath(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string pathKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM index_files WHERE path_key = $path AND deleted_utc_ticks IS NULL ORDER BY id LIMIT 2;";
        command.Parameters.AddWithValue("$path", pathKey);
        using var reader = command.ExecuteReader();
        var values = new List<string>(2);
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static bool SmartTagDefinitionExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tagId) => Convert.ToInt32(ExecuteScalar(
            connection,
            transaction,
            "SELECT COUNT(*) FROM smart_tag_definitions WHERE tag_id = $tag;",
            ("$tag", tagId)), CultureInfo.InvariantCulture) == 1;

    private static void EnsureActiveFile(SqliteConnection connection, SqliteTransaction transaction, string fileId)
    {
        var count = Convert.ToInt32(ExecuteScalar(
            connection,
            transaction,
            "SELECT COUNT(*) FROM index_files WHERE id = $file AND deleted_utc_ticks IS NULL;",
            ("$file", fileId)), CultureInfo.InvariantCulture);
        if (count != 1)
        {
            throw new InvalidOperationException("The indexed file is no longer active.");
        }
    }

    private static (SmartTagType Type, string[] Ids) NormalizeFilter(IReadOnlyList<string>? values, SmartTagType type)
    {
        var ids = (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(17)
            .ToArray();
        if (ids.Length > 16 || ids.Any(value => value.Length > 96))
        {
            throw new ArgumentOutOfRangeException(nameof(values), "At most 16 bounded canonical Smart Tag IDs may be filtered per type.");
        }

        return (type, ids);
    }

    private static string UserTagId(string normalized)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"user.{hash[..32]}";
    }

    private static void ValidateFileId(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId) || fileId.Length > 256 || fileId.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("A bounded durable file identity is required.", nameof(fileId));
        }
    }

    private static void ValidateTagId(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId) || tagId.Length > 96 || tagId.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("A bounded canonical Smart Tag identity is required.", nameof(tagId));
        }
    }
}
