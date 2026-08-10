using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Indexing.Sqlite;

public sealed partial class SqliteDeepIndexStore
{
    /// <inheritdoc />
    public Task<RelationshipOperationResult> LinkFilesAsync(
        string firstFileId,
        string secondFileId,
        RelationshipType type,
        string? customType,
        bool alwaysRelate,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(firstFileId, nameof(firstFileId));
        ValidateRelationshipIdentifier(secondFileId, nameof(secondFileId));
        if (!Enum.IsDefined(type) || string.Equals(firstFileId, secondFileId, StringComparison.Ordinal) ||
            type == RelationshipType.Custom && string.IsNullOrWhiteSpace(customType))
        {
            throw new ArgumentException("A manual relationship requires two different files and a valid type.");
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (ReadRelationshipFile(connection, firstFileId, transaction) is null ||
                    ReadRelationshipFile(connection, secondFileId, transaction) is null)
                {
                    return new RelationshipOperationResult(false, 0, 0, "Both indexed files must still be available.");
                }

                var (first, second) = CanonicalPair(firstFileId, secondFileId);
                var id = "rel:manual:" + StableRelationshipKey($"{first}|{second}|{type}|{customType}");
                var decision = alwaysRelate ? RelationshipDecision.AlwaysRelate : RelationshipDecision.Confirmed;
                var relationship = new FileRelationship
                {
                    Id = id,
                    FirstFileId = first,
                    SecondFileId = second,
                    Type = type,
                    CustomType = type == RelationshipType.Custom ? BoundOrNull(customType, 64) : null,
                    Confidence = RelationshipConfidence.Confirmed,
                    Evidence = [new RelationshipEvidence(RelationshipEvidenceKind.Manual, "user-link", "Linked by you")],
                    Algorithm = "user",
                    AlgorithmVersion = "1",
                    CreatedAtUtc = changedAtUtc,
                    LastValidatedAtUtc = changedAtUtc,
                    Decision = decision,
                    IsManual = true,
                };
                UpsertRelationship(connection, transaction, relationship, null);
                ReplaceEvidence(connection, transaction, relationship);
                UpsertPairOverride(connection, transaction, first, second, decision, type, customType, changedAtUtc);
                transaction.Commit();
                return new RelationshipOperationResult(
                    true,
                    1,
                    0,
                    "The files were linked in the local index. Original files were not changed.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> UnlinkFilesAsync(
        string relationshipId,
        bool neverRelate,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(relationshipId, nameof(relationshipId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var relationship = ReadRelationship(connection, relationshipId, transaction);
                if (relationship is null)
                {
                    return MissingRelationship();
                }

                if (neverRelate)
                {
                    UpsertPairOverride(
                        connection,
                        transaction,
                        relationship.FirstFileId,
                        relationship.SecondFileId,
                        RelationshipDecision.NeverRelate,
                        relationship.Type,
                        relationship.CustomType,
                        changedAtUtc);
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM index_relationships WHERE id = $id;",
                    ("$id", relationshipId));
                CleanupAutomaticCollections(connection, transaction);
                transaction.Commit();
                return new RelationshipOperationResult(
                    true,
                    1,
                    0,
                    neverRelate
                        ? "The relationship was removed and will not be suggested again. Original files were not changed."
                        : "The relationship was removed from the index. Original files were not changed.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> SetRelationshipDecisionAsync(
        string relationshipId,
        RelationshipDecision decision,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(relationshipId, nameof(relationshipId));
        if (!Enum.IsDefined(decision) || decision == RelationshipDecision.None)
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var relationship = ReadRelationship(connection, relationshipId, transaction);
                if (relationship is null)
                {
                    return MissingRelationship();
                }

                UpsertPairOverride(
                    connection,
                    transaction,
                    relationship.FirstFileId,
                    relationship.SecondFileId,
                    decision,
                    relationship.Type,
                    relationship.CustomType,
                    changedAtUtc);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE index_relationships
                    SET decision = $decision,
                        confidence = CASE WHEN $decision IN ($confirmed, $always)
                                          THEN $confirmedConfidence ELSE confidence END,
                        validated_utc_ticks = $now
                    WHERE id = $id;
                    """,
                    ("$decision", (int)decision),
                    ("$confirmed", (int)RelationshipDecision.Confirmed),
                    ("$always", (int)RelationshipDecision.AlwaysRelate),
                    ("$confirmedConfidence", (int)RelationshipConfidence.Confirmed),
                    ("$now", changedAtUtc.UtcTicks),
                    ("$id", relationshipId));
                if (decision is RelationshipDecision.Rejected or RelationshipDecision.NeverRelate)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "DELETE FROM smart_collection_members WHERE relationship_id = $relationship AND membership_source = $automatic;",
                        ("$relationship", relationshipId),
                        ("$automatic", (int)CollectionMembershipSource.Automatic));
                    CleanupAutomaticCollections(connection, transaction);
                }

                transaction.Commit();
                return new RelationshipOperationResult(true, 1, 0, "Your relationship correction was saved.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> RenameCollectionAsync(
        string collectionId,
        string title,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (title.Length > RelationshipLimits.MaximumCollectionTitleCharacters ||
            title.Any(char.IsControl) || SearchTextNormalizer.ContainsMalformedUnicode(title))
        {
            throw new ArgumentException("The collection title is malformed or exceeds the supported bound.", nameof(title));
        }

        var normalizedTitle = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return UpdateCollectionAsync(
            collectionId,
            "UPDATE smart_collections SET title = $value, is_user_renamed = 1, updated_utc_ticks = $now WHERE id = $id;",
            normalizedTitle,
            changedAtUtc,
            "The virtual collection was renamed. Original files were not changed.",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> SetCollectionPinnedAsync(
        string collectionId,
        bool pinned,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default) =>
        UpdateCollectionAsync(
            collectionId,
            "UPDATE smart_collections SET is_pinned = $value, updated_utc_ticks = $now WHERE id = $id;",
            pinned ? 1 : 0,
            changedAtUtc,
            pinned ? "The virtual collection was pinned." : "The virtual collection was unpinned.",
            cancellationToken);

    /// <inheritdoc />
    public Task<RelationshipOperationResult> MergeCollectionsAsync(
        string targetCollectionId,
        string sourceCollectionId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(targetCollectionId, nameof(targetCollectionId));
        ValidateRelationshipIdentifier(sourceCollectionId, nameof(sourceCollectionId));
        if (string.Equals(targetCollectionId, sourceCollectionId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A collection cannot be merged into itself.");
        }

        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (ReadCollection(connection, targetCollectionId, transaction) is null ||
                    ReadCollection(connection, sourceCollectionId, transaction) is null)
                {
                    return MissingCollection();
                }

                var combinedMembers = ScalarCount(
                    connection,
                    """
                    SELECT COUNT(DISTINCT file_id)
                    FROM smart_collection_members
                    WHERE collection_id IN ($target, $source);
                    """,
                    ("$target", targetCollectionId),
                    ("$source", sourceCollectionId));
                if (combinedMembers > RelationshipLimits.MaximumCollectionMembers)
                {
                    return new RelationshipOperationResult(
                        false,
                        0,
                        0,
                        $"The collections were not merged because their {combinedMembers:N0} distinct members exceed the {RelationshipLimits.MaximumCollectionMembers:N0}-member safety limit.");
                }

                var moved = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT OR IGNORE INTO smart_collection_members(
                        collection_id, file_id, membership_source, relationship_id, added_utc_ticks)
                    SELECT $target, file_id, $manual, relationship_id, $now
                    FROM smart_collection_members
                    WHERE collection_id = $source
                    ORDER BY file_id;
                    """,
                    ("$target", targetCollectionId),
                    ("$source", sourceCollectionId),
                    ("$manual", (int)CollectionMembershipSource.Manual),
                    ("$now", changedAtUtc.UtcTicks));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE smart_collections SET creation_source = $merged, updated_utc_ticks = $now WHERE id = $target;",
                    ("$merged", (int)SmartCollectionCreationSource.Merged),
                    ("$now", changedAtUtc.UtcTicks),
                    ("$target", targetCollectionId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM smart_collections WHERE id = $source;",
                    ("$source", sourceCollectionId));
                transaction.Commit();
                return new RelationshipOperationResult(
                    true,
                    0,
                    2,
                    $"The virtual collections were merged with {moved} additional members. Original files were not moved.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> SplitCollectionMemberAsync(
        string collectionId,
        string fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(collectionId, nameof(collectionId));
        ValidateRelationshipIdentifier(fileId, nameof(fileId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var removed = ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM smart_collection_members WHERE collection_id = $collection AND file_id = $file;",
                    ("$collection", collectionId),
                    ("$file", fileId));
                if (removed > 0)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO smart_collection_member_overrides(collection_id, file_id, excluded, changed_utc_ticks)
                        VALUES($collection, $file, 1, $now)
                        ON CONFLICT(collection_id, file_id) DO UPDATE SET
                            excluded = 1, changed_utc_ticks = excluded.changed_utc_ticks;
                        """,
                        ("$collection", collectionId),
                        ("$file", fileId),
                        ("$now", changedAtUtc.UtcTicks));
                }

                CleanupAutomaticCollections(connection, transaction);
                transaction.Commit();
                return new RelationshipOperationResult(
                    removed > 0,
                    0,
                    removed > 0 ? 1 : 0,
                    removed > 0
                        ? "The file was removed from the virtual collection and will not be re-added automatically."
                        : "The file is not a member of that collection.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> ForgetCollectionAsync(
        string collectionId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(collectionId, nameof(collectionId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var context = ExecuteScalar(
                    connection,
                    transaction,
                    "SELECT context_key FROM smart_collections WHERE id = $id;",
                    ("$id", collectionId)) as string;
                if (context is not null)
                {
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO forgotten_smart_collections(context_key, forgotten_utc_ticks)
                        VALUES($context, $now)
                        ON CONFLICT(context_key) DO UPDATE SET forgotten_utc_ticks = excluded.forgotten_utc_ticks;
                        """,
                        ("$context", context),
                        ("$now", changedAtUtc.UtcTicks));
                }

                var removed = ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM smart_collections WHERE id = $id;",
                    ("$id", collectionId));
                transaction.Commit();
                return new RelationshipOperationResult(
                    removed > 0,
                    0,
                    removed,
                    removed > 0
                        ? "The virtual collection was forgotten. Original files were not changed."
                        : "The collection is no longer available.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> ForgetFileRelationshipsAsync(
        string fileId,
        bool excludeFutureAnalysis,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(fileId, nameof(fileId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var identity = ReadFileIdentity(connection, transaction, fileId);
                if (identity is null)
                {
                    return MissingRelationshipFile();
                }

                var relationshipCount = ScalarCount(
                    connection,
                    "SELECT COUNT(*) FROM index_relationships WHERE first_file_id = $file OR second_file_id = $file;",
                    ("$file", fileId));
                var collectionCount = ScalarCount(
                    connection,
                    "SELECT COUNT(*) FROM smart_collection_members WHERE file_id = $file;",
                    ("$file", fileId));
                DeleteFileRelationshipData(connection, transaction, fileId, keepManualRelationships: false);
                if (excludeFutureAnalysis)
                {
                    UpsertPrivacyRule(
                        connection,
                        transaction,
                        identity,
                        new IndexPrivacyPolicyChange(SuppressRelationships: true),
                        changedAtUtc,
                        repairStage: null,
                        forceReprocess: false);
                }

                CleanupAutomaticCollections(connection, transaction);
                transaction.Commit();
                return new RelationshipOperationResult(
                    true,
                    ToOperationCount(relationshipCount),
                    ToOperationCount(collectionCount),
                    excludeFutureAnalysis
                        ? "Relationship data was forgotten and future analysis was disabled. The original file was not changed."
                        : "Relationship data was forgotten. The original file was not changed.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> ForgetSourceRelationshipsAsync(
        string sourceId,
        bool excludeFutureAnalysis,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(sourceId, nameof(sourceId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var files = ReadFileIdentities(connection, transaction, sourceId);
                if (files.Count == 0)
                {
                    return new RelationshipOperationResult(false, 0, 0, "The source has no indexed relationship data.");
                }

                var relationships = ScalarCount(
                    connection,
                    """
                    SELECT COUNT(*) FROM index_relationships r
                    WHERE r.first_file_id IN (SELECT id FROM index_files WHERE source_id = $source)
                       OR r.second_file_id IN (SELECT id FROM index_files WHERE source_id = $source);
                    """,
                    ("$source", sourceId));
                var collections = ScalarCount(
                    connection,
                    """
                    SELECT COUNT(DISTINCT collection_id) FROM smart_collection_members
                    WHERE file_id IN (SELECT id FROM index_files WHERE source_id = $source);
                    """,
                    ("$source", sourceId));
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DeleteFileRelationshipData(connection, transaction, file.FileId, keepManualRelationships: false);
                    if (excludeFutureAnalysis)
                    {
                        UpsertPrivacyRule(
                            connection,
                            transaction,
                            file,
                            new IndexPrivacyPolicyChange(SuppressRelationships: true),
                            changedAtUtc,
                            repairStage: null,
                            forceReprocess: false);
                    }
                }

                CleanupAutomaticCollections(connection, transaction);
                transaction.Commit();
                return new RelationshipOperationResult(
                    true,
                    ToOperationCount(relationships),
                    ToOperationCount(collections),
                    "Relationship data for the indexed source was forgotten. Source files and source ownership were not changed.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> PrepareRelationshipRebuildAsync(
        string fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRelationshipIdentifier(fileId, nameof(fileId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var identity = ReadFileIdentity(connection, transaction, fileId);
                if (identity is null)
                {
                    return MissingRelationshipFile();
                }

                var relationships = ScalarCount(
                    connection,
                    "SELECT COUNT(*) FROM index_relationships WHERE is_manual = 0 AND (first_file_id = $file OR second_file_id = $file);",
                    ("$file", fileId));
                DeleteFileRelationshipData(connection, transaction, fileId, keepManualRelationships: true);
                UpsertPrivacyRule(
                    connection,
                    transaction,
                    identity,
                    new IndexPrivacyPolicyChange(SuppressRelationships: false),
                    changedAtUtc,
                    repairStage: null,
                    forceReprocess: false);
                CleanupAutomaticCollections(connection, transaction);
                transaction.Commit();
                return new RelationshipOperationResult(
                    true,
                    ToOperationCount(relationships),
                    0,
                    "Automatic relationship data was prepared for a targeted rebuild. Manual links and original files were preserved.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RelationshipSearchExpansion>> GetSearchExpansionsAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seedFileIds);
        ValidateRelationshipCount(maximumCount, RelationshipLimits.MaximumSearchExpansions, nameof(maximumCount));
        if (seedFileIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RelationshipSearchExpansion>>([]);
        }

        var seeds = seedFileIds.Distinct(StringComparer.Ordinal).Take(32).ToArray();
        foreach (var seed in seeds)
        {
            ValidateRelationshipIdentifier(seed, nameof(seedFileIds));
        }

        return RunExclusiveAsync<IReadOnlyList<RelationshipSearchExpansion>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                var names = new string[seeds.Length];
                for (var index = 0; index < seeds.Length; index++)
                {
                    names[index] = "$seed" + index.ToString(CultureInfo.InvariantCulture);
                    command.Parameters.AddWithValue(names[index], seeds[index]);
                }

                command.CommandText =
                    $"""
                    SELECT DISTINCT r.id,
                           CASE WHEN r.first_file_id IN ({string.Join(", ", names)}) THEN r.first_file_id ELSE r.second_file_id END,
                           CASE WHEN r.first_file_id IN ({string.Join(", ", names)}) THEN r.second_file_id ELSE r.first_file_id END,
                           r.relationship_type, r.confidence, c.title
                    FROM index_relationships r
                    JOIN index_files related
                      ON related.id = CASE WHEN r.first_file_id IN ({string.Join(", ", names)}) THEN r.second_file_id ELSE r.first_file_id END
                    JOIN index_files seed
                      ON seed.id = CASE WHEN r.first_file_id IN ({string.Join(", ", names)}) THEN r.first_file_id ELSE r.second_file_id END
                    LEFT JOIN index_privacy_rules p
                      ON p.source_id = related.source_id AND p.relative_path_key = related.relative_path_key
                    LEFT JOIN index_privacy_rules seed_privacy
                      ON seed_privacy.source_id = seed.source_id AND seed_privacy.relative_path_key = seed.relative_path_key
                    LEFT JOIN smart_collection_members m ON m.relationship_id = r.id
                    LEFT JOIN smart_collections c ON c.id = m.collection_id
                    WHERE (r.first_file_id IN ({string.Join(", ", names)}) OR r.second_file_id IN ({string.Join(", ", names)}))
                      AND r.confidence >= $minimum
                      AND r.decision NOT IN ($rejected, $never)
                      AND related.deleted_utc_ticks IS NULL
                      AND COALESCE(p.is_excluded, 0) = 0
                      AND COALESCE(p.suppress_relationships, 0) = 0
                      AND COALESCE(seed_privacy.is_excluded, 0) = 0
                      AND COALESCE(seed_privacy.suppress_relationships, 0) = 0
                    ORDER BY r.confidence DESC, r.id, c.title
                    LIMIT $maximum;
                    """;
                AddParameters(
                    command,
                    ("$minimum", (int)RelationshipConfidence.Medium),
                    ("$rejected", (int)RelationshipDecision.Rejected),
                    ("$never", (int)RelationshipDecision.NeverRelate),
                    ("$maximum", maximumCount));
                using var reader = command.ExecuteReader();
                var rows = new List<(string Id, string Seed, string Related, RelationshipType Type, RelationshipConfidence Confidence, string? Collection)>();
                while (reader.Read())
                {
                    var type = (RelationshipType)reader.GetInt32(3);
                    var confidence = (RelationshipConfidence)reader.GetInt32(4);
                    if (Enum.IsDefined(type) && Enum.IsDefined(confidence))
                    {
                        rows.Add((
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetString(2),
                            type,
                            confidence,
                            reader.IsDBNull(5) ? null : reader.GetString(5)));
                    }
                }

                reader.Close();
                return Array.AsReadOnly(rows
                    .Select(row =>
                    {
                        var relationship = ReadRelationship(connection, row.Id);
                        return relationship is null
                            ? null
                            : new RelationshipSearchExpansion(
                                row.Seed,
                                row.Related,
                                row.Type,
                                row.Confidence,
                                relationship.Explanation,
                                row.Collection);
                    })
                    .OfType<RelationshipSearchExpansion>()
                    .DistinctBy(item => (item.SeedFileId, item.RelatedFileId))
                    .Take(maximumCount)
                    .ToArray());
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipDiagnosticsSnapshot> GetRelationshipDiagnosticsAsync(
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT
                        (SELECT COUNT(*) FROM index_relationships),
                        (SELECT COUNT(*) FROM smart_collections),
                        (SELECT COUNT(*) FROM index_relationship_evidence),
                        (SELECT COUNT(*) FROM relationship_pair_overrides),
                        (SELECT COUNT(*) FROM index_relationships WHERE decision IN ($rejected, $never)),
                        (SELECT COUNT(*) FROM index_privacy_rules WHERE suppress_relationships = 1),
                        d.last_analysis_utc_ticks, d.last_duration_milliseconds,
                        d.last_candidate_count, d.last_relationship_count, d.last_collection_count,
                        d.algorithm_version, d.repair_operation_count
                    FROM relationship_diagnostics d
                    WHERE d.id = 1;
                    """;
                AddParameters(
                    command,
                    ("$rejected", (int)RelationshipDecision.Rejected),
                    ("$never", (int)RelationshipDecision.NeverRelate));
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return new RelationshipDiagnosticsSnapshot(0, 0, 0, 0, 0, 0, null, null, 0, 0, 0, string.Empty, 0);
                }

                return new RelationshipDiagnosticsSnapshot(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero),
                    reader.IsDBNull(7) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(7)),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetString(11),
                    reader.GetInt32(12));
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<RelationshipOperationResult> RepairRelationshipsAsync(
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var affected = 0;
                affected += ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    DELETE FROM index_relationships
                    WHERE first_file_id = second_file_id
                       OR relationship_type NOT BETWEEN $minimumType AND $maximumType
                       OR confidence NOT BETWEEN $minimumConfidence AND $maximumConfidence
                       OR decision NOT BETWEEN $minimumDecision AND $maximumDecision
                       OR created_utc_ticks < $minimumTicks OR created_utc_ticks > $maximumTicks
                       OR validated_utc_ticks < $minimumTicks OR validated_utc_ticks > $maximumTicks
                       OR first_file_id NOT IN (SELECT id FROM index_files WHERE deleted_utc_ticks IS NULL)
                       OR second_file_id NOT IN (SELECT id FROM index_files WHERE deleted_utc_ticks IS NULL);
                    """,
                    ("$minimumType", (int)RelationshipType.SameProject),
                    ("$maximumType", (int)RelationshipType.Custom),
                    ("$minimumConfidence", (int)RelationshipConfidence.Low),
                    ("$maximumConfidence", (int)RelationshipConfidence.Confirmed),
                    ("$minimumDecision", (int)RelationshipDecision.None),
                    ("$maximumDecision", (int)RelationshipDecision.NeverRelate),
                    ("$minimumTicks", DateTimeOffset.MinValue.Ticks),
                    ("$maximumTicks", DateTimeOffset.MaxValue.Ticks));
                affected += ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    DELETE FROM index_relationship_evidence
                    WHERE evidence_kind NOT BETWEEN $minimumKind AND $maximumKind
                       OR length(evidence_key) > $maximumEvidence
                       OR length(explanation) > $maximumEvidence;
                    """,
                    ("$minimumKind", (int)RelationshipEvidenceKind.DuplicateContent),
                    ("$maximumKind", (int)RelationshipEvidenceKind.Manual),
                    ("$maximumEvidence", RelationshipLimits.MaximumEvidenceTextCharacters));
                affected += ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    DELETE FROM index_relationships
                    WHERE is_manual = 0
                      AND NOT EXISTS(
                          SELECT 1 FROM index_relationship_evidence e
                          WHERE e.relationship_id = index_relationships.id);
                    """);
                affected += ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    DELETE FROM smart_collections
                    WHERE context_type NOT BETWEEN $minimumType AND $maximumType
                       OR confidence NOT BETWEEN $minimumConfidence AND $maximumConfidence
                       OR creation_source NOT BETWEEN $minimumSource AND $maximumSource
                       OR created_utc_ticks < $minimumTicks OR created_utc_ticks > $maximumTicks
                       OR updated_utc_ticks < $minimumTicks OR updated_utc_ticks > $maximumTicks
                       OR length(title) > $maximumTitle
                       OR length(description) > $maximumDescription
                       OR length(relationship_summary) > $maximumDescription;
                    """,
                    ("$minimumType", (int)RelationshipType.SameProject),
                    ("$maximumType", (int)RelationshipType.Custom),
                    ("$minimumConfidence", (int)RelationshipConfidence.Low),
                    ("$maximumConfidence", (int)RelationshipConfidence.Confirmed),
                    ("$minimumSource", (int)SmartCollectionCreationSource.Automatic),
                    ("$maximumSource", (int)SmartCollectionCreationSource.Merged),
                    ("$minimumTicks", DateTimeOffset.MinValue.Ticks),
                    ("$maximumTicks", DateTimeOffset.MaxValue.Ticks),
                    ("$maximumTitle", RelationshipLimits.MaximumCollectionTitleCharacters),
                    ("$maximumDescription", RelationshipLimits.MaximumCollectionDescriptionCharacters));
                affected += ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    DELETE FROM smart_collection_members
                    WHERE membership_source NOT BETWEEN $minimum AND $maximum
                       OR added_utc_ticks < $minimumTicks OR added_utc_ticks > $maximumTicks;
                    """,
                    ("$minimum", (int)CollectionMembershipSource.Automatic),
                    ("$maximum", (int)CollectionMembershipSource.Manual),
                    ("$minimumTicks", DateTimeOffset.MinValue.Ticks),
                    ("$maximumTicks", DateTimeOffset.MaxValue.Ticks));
                affected += ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    DELETE FROM smart_collection_members
                    WHERE file_id NOT IN (SELECT id FROM index_files WHERE deleted_utc_ticks IS NULL);
                    """);
                CleanupAutomaticCollections(connection, transaction);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE relationship_diagnostics
                    SET repair_operation_count = repair_operation_count + 1,
                        last_analysis_utc_ticks = COALESCE(last_analysis_utc_ticks, $now)
                    WHERE id = 1;
                    """,
                    ("$now", changedAtUtc.UtcTicks));
                transaction.Commit();
                return new RelationshipOperationResult(
                    true,
                    affected,
                    0,
                    affected == 0
                        ? "Relationship storage is consistent; no repair was required."
                        : $"Repaired {affected} stale relationship records without changing original files.");
            },
            cancellationToken);

    private Task<RelationshipOperationResult> UpdateCollectionAsync(
        string collectionId,
        string sql,
        object value,
        DateTimeOffset changedAtUtc,
        string message,
        CancellationToken cancellationToken)
    {
        ValidateRelationshipIdentifier(collectionId, nameof(collectionId));
        return RunExclusiveAsync(
            () =>
            {
                using var connection = OpenConnection();
                var affected = ExecuteNonQuery(
                    connection,
                    null,
                    sql,
                    ("$value", value),
                    ("$now", changedAtUtc.UtcTicks),
                    ("$id", collectionId));
                return new RelationshipOperationResult(affected > 0, 0, affected, affected > 0 ? message : "The collection is no longer available.");
            },
            cancellationToken);
    }

    private static void UpsertPairOverride(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string firstFileId,
        string secondFileId,
        RelationshipDecision decision,
        RelationshipType type,
        string? customType,
        DateTimeOffset changedAtUtc)
    {
        var (first, second) = CanonicalPair(firstFileId, secondFileId);
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO relationship_pair_overrides(
                first_file_id, second_file_id, decision, relationship_type, custom_type, changed_utc_ticks)
            VALUES($first, $second, $decision, $type, $custom, $now)
            ON CONFLICT(first_file_id, second_file_id) DO UPDATE SET
                decision = excluded.decision,
                relationship_type = excluded.relationship_type,
                custom_type = excluded.custom_type,
                changed_utc_ticks = excluded.changed_utc_ticks;
            """,
            ("$first", first),
            ("$second", second),
            ("$decision", (int)decision),
            ("$type", (int)type),
            ("$custom", BoundOrNull(customType, 64)),
            ("$now", changedAtUtc.UtcTicks));
    }

    private static (string First, string Second) CanonicalPair(string first, string second) =>
        string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);

    private static int ToOperationCount(long value) =>
        value >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

    private static void DeleteFileRelationshipData(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fileId,
        bool keepManualRelationships)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM smart_collection_members WHERE file_id = $file AND ($keepManual = 0 OR membership_source = $automatic);",
            ("$file", fileId),
            ("$keepManual", keepManualRelationships ? 1 : 0),
            ("$automatic", (int)CollectionMembershipSource.Automatic));
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM index_relationship_features WHERE file_id = $file;",
            ("$file", fileId));
        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM index_relationships
            WHERE (first_file_id = $file OR second_file_id = $file)
              AND ($keepManual = 0 OR is_manual = 0);
            """,
            ("$file", fileId),
            ("$keepManual", keepManualRelationships ? 1 : 0));
        if (!keepManualRelationships)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "DELETE FROM relationship_pair_overrides WHERE first_file_id = $file OR second_file_id = $file;",
                ("$file", fileId));
        }

        CleanupAutomaticCollections(connection, transaction);
    }

    private static RelationshipOperationResult MissingRelationship() =>
        new(false, 0, 0, "The relationship is no longer available.");

    private static RelationshipOperationResult MissingCollection() =>
        new(false, 0, 0, "One or both collections are no longer available.");

    private static RelationshipOperationResult MissingRelationshipFile() =>
        new(false, 0, 0, "The indexed file is no longer available.");
}
