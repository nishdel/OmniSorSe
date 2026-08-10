using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Relationships;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

public sealed partial class SqliteGraphProjectionSource
{
    private static readonly string[] RequiredDeepIndexTables =
    [
        "index_meta",
        "index_sources",
        "index_files",
        "index_privacy_rules",
        "index_relationships",
        "index_relationship_evidence",
        "relationship_pair_overrides",
        "smart_collections",
        "smart_collection_members",
        "smart_collection_member_overrides",
        "forgotten_smart_collections",
    ];

    private GraphProjectionSnapshot CaptureSnapshot(string stagingPath, CancellationToken cancellationToken)
    {
        try
        {
            using var source = OpenDeepIndexConnection();
            ValidateDeepIndex(source, cancellationToken);
            using var sourceTransaction = source.BeginTransaction(deferred: true);
            using var snapshot = OpenSnapshotConnection(stagingPath, readOnly: false);
            CreateSnapshotSchema(snapshot);
            using var snapshotTransaction = snapshot.BeginTransaction();
            using var writer = new SnapshotWriter(
                snapshot,
                snapshotTransaction,
                _pathSemantics.IsCaseSensitive
                    ? GraphPathComparison.CaseSensitive
                    : GraphPathComparison.CaseInsensitive,
                cancellationToken);
            var accumulator = new SnapshotAccumulator();

            CaptureSources(source, sourceTransaction, writer, accumulator, cancellationToken);
            CaptureFiles(source, sourceTransaction, writer, accumulator, cancellationToken);
            CaptureRelationships(source, sourceTransaction, writer, accumulator, cancellationToken);
            CaptureCollections(source, sourceTransaction, writer, accumulator, cancellationToken);
            CaptureMemberships(source, sourceTransaction, writer, accumulator, cancellationToken);
            CaptureLegacyDecisions(source, sourceTransaction, writer, accumulator, cancellationToken);

            sourceTransaction.Commit();
            var completedAt = _timeProvider.GetUtcNow();
            var manifestRows = ReadManifestRows(snapshot, snapshotTransaction, legacyOnly: false, cancellationToken);
            var legacyRows = ReadManifestRows(snapshot, snapshotTransaction, legacyOnly: true, cancellationToken);
            var manifestHash = manifestRows.Count == 0
                ? EmptyManifestHash
                : GraphCanonicalSerializer.CalculateManifestHash(manifestRows);
            var legacyHash = legacyRows.Count == 0
                ? EmptyManifestHash
                : GraphCanonicalSerializer.CalculateManifestHash(legacyRows);
            var manifestId = string.Concat("kg-manifest:", manifestHash.ToLowerInvariant());
            var legacyManifestId = string.Concat("kg-legacy:", legacyHash.ToLowerInvariant());
            WriteSnapshotMetadata(
                snapshot,
                snapshotTransaction,
                manifestId,
                manifestHash,
                legacyManifestId,
                accumulator,
                completedAt);
            snapshotTransaction.Commit();
            ValidateSnapshotDatabase(snapshot, manifestRows.Count);

            return new GraphProjectionSnapshot(
                manifestId,
                accumulator.Revision,
                legacyManifestId,
                accumulator.PrivacySequence,
                completedAt,
                manifestHash,
                manifestRows.Count,
                accumulator.Counts
                    .OrderBy(item => item.Key)
                    .Select(item => new GraphObservationKindCount(item.Key, item.Value))
                    .ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GraphPersistenceException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw SqliteKnowledgeInfrastructure.Map(exception, "The schema-3 graph source snapshot could not be captured safely.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new GraphPersistenceException(
                "source-snapshot-io",
                "The schema-3 graph source snapshot could not be written safely.",
                exception);
        }
    }

    private GraphProjectionSnapshot CaptureEmptySnapshot(string stagingPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenSnapshotConnection(stagingPath, readOnly: false);
        CreateSnapshotSchema(connection);
        using var transaction = connection.BeginTransaction();
        var completedAt = _timeProvider.GetUtcNow();
        var accumulator = new SnapshotAccumulator();
        var manifestId = string.Concat("kg-manifest:", EmptyManifestHash.ToLowerInvariant());
        var legacyManifestId = string.Concat("kg-legacy:", EmptyManifestHash.ToLowerInvariant());
        WriteSnapshotMetadata(
            connection,
            transaction,
            manifestId,
            EmptyManifestHash,
            legacyManifestId,
            accumulator,
            completedAt);
        transaction.Commit();
        ValidateSnapshotDatabase(connection, 0);
        return new GraphProjectionSnapshot(
            manifestId,
            0,
            legacyManifestId,
            0,
            completedAt,
            EmptyManifestHash,
            0,
            []);
    }

    private static void CaptureSources(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, root_path_key, display_name, indexing_level, include_subfolders,
                   enabled, priority, managed_by_watched_folders, updated_utc_ticks
            FROM index_sources
            ORDER BY id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = RequireStableId(reader.GetString(0), "source ID");
            var revision = NonNegative(reader.GetInt64(8));
            var observation = new GraphSourceObservation
            {
                StableKey = ObservationKey("source", id),
                CanonicalRowHash = HashFields(
                    id,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    revision),
                Revision = revision,
                ObservedAtUtc = SafeUtc(revision),
                IsExcluded = reader.GetInt32(5) == 0,
                SourceId = id,
                DisplayName = SafeSourceLabel(reader.GetString(2)),
                PathSemanticsVersion = "platform-path-v1",
                PathComparison = writer.PathComparison,
            };
            writer.Add(observation, accumulator);
            accumulator.PrivacySequence = Math.Max(accumulator.PrivacySequence, revision);
        }
    }

    private static void CaptureFiles(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT f.id, f.source_id, f.relative_path, f.length,
                   f.creation_utc_ticks, f.modified_utc_ticks, f.metadata_fingerprint,
                   f.content_hash, f.indexing_level, f.fully_indexed,
                   f.deleted_utc_ticks, f.updated_utc_ticks, s.enabled,
                   COALESCE(p.is_excluded, 0), COALESCE(p.suppress_relationships, 0),
                   COALESCE(p.updated_utc_ticks, 0)
            FROM index_files f
            JOIN index_sources s ON s.id = f.source_id
            LEFT JOIN index_privacy_rules p
              ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
            ORDER BY f.id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileId = RequireStableId(reader.GetString(0), "file ID");
            var sourceId = RequireStableId(reader.GetString(1), "file source ID");
            var sourceRelativePath = reader.GetString(2);
            var relativePath = SafeRelativePath(sourceRelativePath);
            var stableKey = ObservationKey("file", fileId);
            var updatedTicks = NonNegative(reader.GetInt64(11));
            var deletedTicks = reader.IsDBNull(10) ? (long?)null : NonNegative(reader.GetInt64(10));
            var revision = Math.Max(updatedTicks, deletedTicks ?? 0);
            var privacyTicks = NonNegative(reader.GetInt64(15));
            var sourceEnabled = reader.GetInt32(12) != 0;
            var excluded = reader.GetInt32(13) != 0;
            var suppressed = reader.GetInt32(14) != 0;
            var rawContentHash = reader.IsDBNull(7) ? null : reader.GetString(7);
            var contentHash = ValidContentHash(rawContentHash) ? rawContentHash!.ToLowerInvariant() : null;
            var canonicalHash = HashFields(
                fileId,
                sourceId,
                relativePath,
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetString(6),
                rawContentHash,
                reader.GetInt32(8),
                reader.GetInt32(9),
                deletedTicks,
                updatedTicks,
                sourceEnabled,
                excluded,
                suppressed,
                privacyTicks);

            GraphProjectionObservation observation;
            if (deletedTicks is not null)
            {
                observation = new GraphDeletionObservation
                {
                    StableKey = stableKey,
                    CanonicalRowHash = canonicalHash,
                    Revision = revision,
                    ObservedAtUtc = SafeUtc(revision),
                    DeletedKind = GraphProjectionObservationKind.File,
                    DeletedStableKey = stableKey,
                };
            }
            else
            {
                observation = new GraphFileObservation
                {
                    StableKey = stableKey,
                    CanonicalRowHash = canonicalHash,
                    Revision = revision,
                    ObservedAtUtc = SafeUtc(revision),
                    IsExcluded = !sourceEnabled || excluded,
                    FileId = fileId,
                    SourceId = sourceId,
                    FileName = SafeRelativeFileName(sourceRelativePath),
                    RelativePath = relativePath,
                    FolderRelativePath = FolderIdentityPath(relativePath),
                    PathSemanticsVersion = "platform-path-v1",
                    PathComparison = writer.PathComparison,
                    Length = Math.Max(0, reader.GetInt64(3)),
                    CreationTimeUtc = OptionalUtc(reader.GetInt64(4)),
                    ModifiedTimeUtc = OptionalUtc(reader.GetInt64(5)),
                    HasBasicMetadata = !string.IsNullOrWhiteSpace(reader.GetString(6)),
                    ContentHash = contentHash,
                    ContentHashAlgorithmVersion = contentHash is null ? null : "sha256-v1",
                    RelationshipAnalysisSuppressed = suppressed || !sourceEnabled || excluded,
                };
            }

            writer.Add(observation, accumulator);
            accumulator.PrivacySequence = Math.Max(accumulator.PrivacySequence, privacyTicks);
        }
    }

    private static void CaptureRelationships(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT r.id, r.first_file_id, r.second_file_id, r.relationship_type,
                   r.custom_type, r.confidence, r.algorithm, r.algorithm_version,
                   r.created_utc_ticks, r.validated_utc_ticks, r.decision, r.is_manual,
                   e.ordinal, e.evidence_kind, e.evidence_key, e.explanation,
                   first.deleted_utc_ticks, second.deleted_utc_ticks,
                   first_source.enabled, second_source.enabled,
                   COALESCE(first_privacy.is_excluded, 0), COALESCE(first_privacy.suppress_relationships, 0),
                   COALESCE(second_privacy.is_excluded, 0), COALESCE(second_privacy.suppress_relationships, 0)
            FROM index_relationships r
            JOIN index_files first ON first.id = r.first_file_id
            JOIN index_files second ON second.id = r.second_file_id
            JOIN index_sources first_source ON first_source.id = first.source_id
            JOIN index_sources second_source ON second_source.id = second.source_id
            LEFT JOIN index_privacy_rules first_privacy
              ON first_privacy.source_id = first.source_id AND first_privacy.relative_path_key = first.relative_path_key
            LEFT JOIN index_privacy_rules second_privacy
              ON second_privacy.source_id = second.source_id AND second_privacy.relative_path_key = second.relative_path_key
            LEFT JOIN index_relationship_evidence e ON e.relationship_id = r.id
            ORDER BY r.id, e.ordinal;
            """;
        using var reader = command.ExecuteReader();
        RelationshipCapture? current = null;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipId = RequireStableId(reader.GetString(0), "relationship ID");
            if (current is null || !string.Equals(current.RelationshipId, relationshipId, StringComparison.Ordinal))
            {
                current?.Write(writer, accumulator);
                current = RelationshipCapture.From(reader);
            }

            current.AddEvidence(reader);
        }

        current?.Write(writer, accumulator);
    }

    private static void CaptureCollections(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT c.id, c.context_key, c.title, c.description, c.relationship_summary,
                   c.context_type, c.confidence, c.creation_source, c.is_pinned,
                   c.is_user_renamed, c.created_utc_ticks, c.updated_utc_ticks,
                   CASE WHEN forgotten.context_key IS NULL THEN 0 ELSE 1 END
            FROM smart_collections c
            LEFT JOIN forgotten_smart_collections forgotten ON forgotten.context_key = c.context_key
            ORDER BY c.id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = RequireStableId(reader.GetString(0), "collection ID");
            var revision = NonNegative(reader.GetInt64(11));
            var creationSource = reader.GetInt32(7);
            var isPinned = reader.GetInt32(8) != 0;
            var renamed = reader.GetInt32(9) != 0;
            var forgotten = reader.GetInt32(12) != 0;
            var observation = new GraphCollectionObservation
            {
                StableKey = ObservationKey("collection", id),
                CanonicalRowHash = HashFields(
                    id,
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    creationSource,
                    isPinned,
                    renamed,
                    reader.GetInt64(10),
                    revision,
                    forgotten),
                Revision = revision,
                ObservedAtUtc = SafeUtc(revision),
                CollectionId = id,
                Title = SafeDisplay(reader.GetString(2), "Smart Collection"),
                IsManual = creationSource != (int)SmartCollectionCreationSource.Automatic || isPinned || renamed,
                IsForgotten = forgotten,
            };
            writer.Add(observation, accumulator);
        }
    }

    private static void CaptureMemberships(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT m.collection_id, m.file_id, m.membership_source, m.relationship_id,
                   m.added_utc_ticks, f.deleted_utc_ticks, s.enabled,
                   COALESCE(p.is_excluded, 0), COALESCE(p.suppress_relationships, 0),
                   COALESCE(member_override.excluded, 0),
                   CASE WHEN forgotten.context_key IS NULL THEN 0 ELSE 1 END
            FROM smart_collection_members m
            JOIN smart_collections c ON c.id = m.collection_id
            JOIN index_files f ON f.id = m.file_id
            JOIN index_sources s ON s.id = f.source_id
            LEFT JOIN index_privacy_rules p
              ON p.source_id = f.source_id AND p.relative_path_key = f.relative_path_key
            LEFT JOIN smart_collection_member_overrides member_override
              ON member_override.collection_id = m.collection_id AND member_override.file_id = m.file_id
            LEFT JOIN forgotten_smart_collections forgotten ON forgotten.context_key = c.context_key
            ORDER BY m.collection_id, m.file_id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collectionId = RequireStableId(reader.GetString(0), "membership collection ID");
            var fileId = RequireStableId(reader.GetString(1), "membership file ID");
            var revision = NonNegative(reader.GetInt64(4));
            var membershipSource = reader.GetInt32(2);
            var excluded = !reader.IsDBNull(5) || reader.GetInt32(6) == 0 ||
                reader.GetInt32(7) != 0 || reader.GetInt32(8) != 0 ||
                reader.GetInt32(9) != 0 || reader.GetInt32(10) != 0;
            var observation = new GraphCollectionMembershipObservation
            {
                StableKey = CompositeObservationKey("membership", collectionId, fileId),
                CanonicalRowHash = HashFields(
                    collectionId,
                    fileId,
                    membershipSource,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    revision,
                    excluded),
                Revision = revision,
                ObservedAtUtc = SafeUtc(revision),
                IsExcluded = excluded,
                CollectionId = collectionId,
                FileId = fileId,
                IsManual = membershipSource != (int)CollectionMembershipSource.Automatic,
            };
            writer.Add(observation, accumulator);
        }
    }

    private static void CaptureLegacyDecisions(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        CaptureRelationshipDecisionRows(source, transaction, writer, accumulator, cancellationToken);
        CapturePairOverrideRows(source, transaction, writer, accumulator, cancellationToken);
        CaptureMemberOverrideRows(source, transaction, writer, accumulator, cancellationToken);
        CaptureForgottenCollectionRows(source, transaction, writer, accumulator, cancellationToken);
        CaptureCollectionStateRows(source, transaction, writer, accumulator, cancellationToken);
    }

    private static void CaptureRelationshipDecisionRows(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, decision, validated_utc_ticks
            FROM index_relationships
            WHERE decision <> 0
            ORDER BY id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            AddLegacyDecision(
                writer,
                accumulator,
                "relationship-decision",
                RequireStableId(reader.GetString(0), "relationship decision ID"),
                EnumCode<RelationshipDecision>(reader.GetInt32(1)),
                NonNegative(reader.GetInt64(2)),
                isRetired: false,
                cancellationToken);
        }
    }

    private static void CapturePairOverrideRows(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT first_file_id, second_file_id, decision, relationship_type,
                   custom_type, changed_utc_ticks
            FROM relationship_pair_overrides
            ORDER BY first_file_id, second_file_id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var first = RequireStableId(reader.GetString(0), "pair override file ID");
            var second = RequireStableId(reader.GetString(1), "pair override file ID");
            AddLegacyDecision(
                writer,
                accumulator,
                "relationship-pair-override",
                CompositeDecisionKey(first, second),
                EnumCode<RelationshipDecision>(reader.GetInt32(2)),
                NonNegative(reader.GetInt64(5)),
                isRetired: false,
                cancellationToken,
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }
    }

    private static void CaptureMemberOverrideRows(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT collection_id, file_id, excluded, changed_utc_ticks
            FROM smart_collection_member_overrides
            ORDER BY collection_id, file_id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var collection = RequireStableId(reader.GetString(0), "member override collection ID");
            var file = RequireStableId(reader.GetString(1), "member override file ID");
            AddLegacyDecision(
                writer,
                accumulator,
                "collection-member-override",
                CompositeDecisionKey(collection, file),
                reader.GetInt32(2) == 0 ? "include" : "exclude",
                NonNegative(reader.GetInt64(3)),
                isRetired: false,
                cancellationToken);
        }
    }

    private static void CaptureForgottenCollectionRows(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT context_key, forgotten_utc_ticks FROM forgotten_smart_collections ORDER BY context_key;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            AddLegacyDecision(
                writer,
                accumulator,
                "forgotten-collection",
                CompositeDecisionKey(reader.GetString(0)),
                "forget",
                NonNegative(reader.GetInt64(1)),
                isRetired: false,
                cancellationToken);
        }
    }

    private static void CaptureCollectionStateRows(
        SqliteConnection source,
        SqliteTransaction transaction,
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, title, is_pinned, is_user_renamed, updated_utc_ticks
            FROM smart_collections
            WHERE is_pinned = 1 OR is_user_renamed = 1
            ORDER BY id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var action = reader.GetInt32(2) != 0 && reader.GetInt32(3) != 0
                ? "renamed-and-pinned"
                : reader.GetInt32(2) != 0 ? "pinned" : "renamed";
            AddLegacyDecision(
                writer,
                accumulator,
                "collection-state",
                RequireStableId(reader.GetString(0), "collection state ID"),
                action,
                NonNegative(reader.GetInt64(4)),
                isRetired: false,
                cancellationToken,
                reader.GetString(1));
        }
    }

    private static void AddLegacyDecision(
        SnapshotWriter writer,
        SnapshotAccumulator accumulator,
        string decisionNamespace,
        string decisionKey,
        string actionCode,
        long revision,
        bool isRetired,
        CancellationToken cancellationToken,
        params object?[] additionalCanonicalFields)
    {
        cancellationToken.ThrowIfCancellationRequested();
        decisionKey = RequireStableId(decisionKey, "legacy decision key");
        actionCode = SafeDisplay(actionCode, "unknown").ToLowerInvariant().Replace(' ', '-');
        var hashFields = new object?[5 + additionalCanonicalFields.Length];
        hashFields[0] = decisionNamespace;
        hashFields[1] = decisionKey;
        hashFields[2] = actionCode;
        hashFields[3] = revision;
        hashFields[4] = isRetired;
        additionalCanonicalFields.CopyTo(hashFields, 5);
        var observation = new GraphLegacyDecisionObservation
        {
            StableKey = CompositeObservationKey("legacy", decisionNamespace, decisionKey),
            CanonicalRowHash = HashFields(hashFields),
            Revision = revision,
            ObservedAtUtc = SafeUtc(revision),
            DecisionNamespace = decisionNamespace,
            LegacyDecisionKey = decisionKey,
            ActionCode = actionCode,
            IsRetired = isRetired,
        };
        writer.Add(observation, accumulator);
    }

    private static void ValidateDeepIndex(SqliteConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = ReadPragmaInt(connection, "user_version");
        if (version > DeepIndexingVersion.SchemaVersion)
        {
            throw new GraphPersistenceException(
                "source-schema-newer",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The deep index schema {version} is newer than the graph adapter supports ({DeepIndexingVersion.SchemaVersion})."));
        }

        if (version != DeepIndexingVersion.SchemaVersion)
        {
            throw new GraphPersistenceException(
                "source-schema-incomplete",
                "Knowledge Graph projection requires the fully migrated schema-3 deep index.");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA quick_check;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(reader.GetString(0), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new GraphPersistenceException("source-corrupt", "The deep index failed SQLite integrity validation.");
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check;";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                throw new GraphPersistenceException("source-corrupt", "The deep index contains an invalid foreign-key reference.");
            }
        }

        var tables = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var missing = RequiredDeepIndexTables.Where(table => !tables.Contains(table)).ToArray();
        if (missing.Length != 0)
        {
            throw new GraphPersistenceException(
                "source-schema-corrupt",
                string.Concat("The deep index is missing graph source tables: ", string.Join(", ", missing), "."));
        }

        using var marker = connection.CreateCommand();
        marker.CommandText = "SELECT value FROM index_meta WHERE key = 'schema_version';";
        if (!string.Equals(
                Convert.ToString(marker.ExecuteScalar(), CultureInfo.InvariantCulture),
                DeepIndexingVersion.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new GraphPersistenceException("source-schema-corrupt", "The deep index schema markers disagree.");
        }
    }

    private static void CreateSnapshotSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            PRAGMA application_id = {SnapshotApplicationId};
            PRAGMA user_version = {SnapshotSchemaVersion};
            CREATE TABLE snapshot_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE snapshot_observations (
                kind_name TEXT NOT NULL,
                stable_key TEXT NOT NULL,
                canonical_row_hash TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                PRIMARY KEY(kind_name, stable_key)
            ) WITHOUT ROWID;
            """;
        command.ExecuteNonQuery();
    }

    private static void WriteSnapshotMetadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string manifestId,
        string manifestHash,
        string legacyManifestId,
        SnapshotAccumulator accumulator,
        DateTimeOffset completedAtUtc)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["manifest_id"] = manifestId,
            ["manifest_hash"] = manifestHash,
            ["legacy_manifest_id"] = legacyManifestId,
            ["revision"] = accumulator.Revision.ToString(CultureInfo.InvariantCulture),
            ["privacy_sequence"] = accumulator.PrivacySequence.ToString(CultureInfo.InvariantCulture),
            ["completed_utc_ticks"] = completedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            ["row_count"] = accumulator.TotalCount.ToString(CultureInfo.InvariantCulture),
        };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO snapshot_meta(key, value) VALUES ($key, $value);";
        var key = command.Parameters.Add("$key", SqliteType.Text);
        var value = command.Parameters.Add("$value", SqliteType.Text);
        foreach (var entry in entries)
        {
            key.Value = entry.Key;
            value.Value = entry.Value;
            command.ExecuteNonQuery();
        }
    }

    private static List<(string Kind, string StableKey, string RowHash)> ReadManifestRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        bool legacyOnly,
        CancellationToken cancellationToken)
    {
        var rows = new List<(string Kind, string StableKey, string RowHash)>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = legacyOnly
            ? "SELECT kind_name, stable_key, canonical_row_hash FROM snapshot_observations WHERE kind_name = $legacy ORDER BY kind_name, stable_key;"
            : "SELECT kind_name, stable_key, canonical_row_hash FROM snapshot_observations ORDER BY kind_name, stable_key;";
        if (legacyOnly)
        {
            command.Parameters.AddWithValue("$legacy", GraphProjectionObservationKind.LegacyDecision.ToString());
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    private static void ValidateSnapshotDatabase(SqliteConnection connection, long expectedCount)
    {
        if (ReadPragmaInt(connection, "application_id") != SnapshotApplicationId ||
            ReadPragmaInt(connection, "user_version") != SnapshotSchemaVersion)
        {
            throw new GraphPersistenceException("source-snapshot-corrupt", "The transient graph source snapshot has invalid schema markers.");
        }

        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new GraphPersistenceException("source-snapshot-corrupt", "The transient graph source snapshot failed integrity validation.");
            }
        }

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM snapshot_observations;";
        if (Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture) != expectedCount)
        {
            throw new GraphPersistenceException("source-snapshot-corrupt", "The transient graph source snapshot row count changed before publication.");
        }
    }

    private static int ReadPragmaInt(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = string.Concat("PRAGMA ", pragma, ";");
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string ObservationKey(string prefix, string stableId)
    {
        var candidate = string.Concat(prefix, ":", stableId);
        return candidate.Length <= GraphLimits.MaximumStableIdCharacters
            ? candidate
            : string.Concat(prefix, ":", HashFields(stableId).ToLowerInvariant());
    }

    private static string CompositeObservationKey(string prefix, params string[] values) =>
        string.Concat(prefix, ":", HashFields(values.Cast<object?>().ToArray()).ToLowerInvariant());

    private static string CompositeDecisionKey(params string[] values) =>
        string.Concat("decision:", HashFields(values.Cast<object?>().ToArray()).ToLowerInvariant());

    private static string RequireStableId(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > GraphLimits.MaximumStableIdCharacters || ContainsInvalidText(value))
        {
            throw new GraphPersistenceException("source-record-corrupt", string.Concat("The deep index contains an invalid ", description, "."));
        }

        return value.Normalize(NormalizationForm.FormC).Trim();
    }

    private static string SafeDisplay(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(Math.Min(value.Length, GraphLimits.MaximumLabelCharacters));
        for (var index = 0; index < value.Length && builder.Length < GraphLimits.MaximumLabelCharacters; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]) &&
                    builder.Length + 2 <= GraphLimits.MaximumLabelCharacters)
                {
                    builder.Append(character).Append(value[++index]);
                }
                else
                {
                    builder.Append('\uFFFD');
                }
            }
            else if (char.IsLowSurrogate(character))
            {
                builder.Append('\uFFFD');
            }
            else if (character == '\0' || char.IsControl(character))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(character);
            }
        }

        var normalized = builder.ToString().Normalize(NormalizationForm.FormC).Trim();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string SafeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim('/');
        var rooted = value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("\\", StringComparison.Ordinal) ||
            (value.Length >= 2 && value[1] == ':');
        if (rooted || ContainsInvalidText(normalized) ||
            normalized.Split('/').Any(segment => segment is "." or ".." or ""))
        {
            return string.Concat("guarded-path/", HashFields(value).ToLowerInvariant());
        }

        normalized = normalized.Normalize(NormalizationForm.FormC);
        if (normalized.Length <= GraphLimits.MaximumStableIdCharacters)
        {
            return normalized;
        }

        return string.Concat("long-path/", HashFields(normalized).ToLowerInvariant());
    }

    private static string SafeRelativeFileName(string value)
    {
        if (ContainsInvalidText(value))
        {
            return "Indexed file";
        }

        var normalized = value.Replace('\\', '/').TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        var candidate = separator < 0 ? normalized : normalized[(separator + 1)..];
        return SafeDisplay(candidate, "Indexed file");
    }

    private static string SafeSourceLabel(string value)
    {
        if (ContainsInvalidText(value))
        {
            return "Indexed source";
        }

        var normalized = value.Replace('\\', '/').TrimEnd('/');
        var looksLikeAbsolutePath = normalized.StartsWith("/", StringComparison.Ordinal) ||
            (normalized.Length >= 2 && normalized[1] == ':');
        if (looksLikeAbsolutePath)
        {
            var separator = normalized.LastIndexOf('/');
            normalized = separator < 0 ? normalized : normalized[(separator + 1)..];
        }

        return SafeDisplay(normalized, "Indexed source");
    }

    private static string FolderIdentityPath(string relativePath)
    {
        var separator = relativePath.LastIndexOf('/');
        var folder = separator <= 0 ? string.Empty : relativePath[..separator];
        return folder.Length <= GraphLimits.MaximumStableIdCharacters
            ? folder
            : string.Concat("long-folder/", HashFields(folder).ToLowerInvariant());
    }

    private static bool ContainsInvalidText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\0' || char.IsControl(character))
            {
                return true;
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return true;
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidContentHash(string? value) =>
        value is { Length: >= 16 and <= 256 } && value.All(char.IsAsciiHexDigit);

    private static string HashFields(params object?[] fields)
    {
        var builder = new StringBuilder();
        foreach (var field in fields)
        {
            var value = field switch
            {
                null => "<null>",
                bool boolean => boolean ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => field.ToString() ?? string.Empty,
            };
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static long NonNegative(long value) => Math.Max(0, value);

    private static DateTimeOffset SafeUtc(long ticks) => OptionalUtc(ticks) ?? DateTimeOffset.UnixEpoch;

    private static DateTimeOffset? OptionalUtc(long ticks)
    {
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return null;
        }

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static string EnumCode<T>(int value) where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value)
            ? ((T)Enum.ToObject(typeof(T), value)).ToString().ToLowerInvariant()
            : string.Concat("unknown-", value.ToString(CultureInfo.InvariantCulture));

    private sealed class SnapshotAccumulator
    {
        internal Dictionary<GraphProjectionObservationKind, long> Counts { get; } = [];
        internal long Revision { get; set; }
        internal long PrivacySequence { get; set; }
        internal long TotalCount { get; set; }
    }

    private sealed class SnapshotWriter : IDisposable
    {
        private readonly SqliteCommand _command;
        private readonly SqliteParameter _kind;
        private readonly SqliteParameter _key;
        private readonly SqliteParameter _hash;
        private readonly SqliteParameter _payload;
        private readonly CancellationToken _cancellationToken;

        internal SnapshotWriter(
            SqliteConnection connection,
            SqliteTransaction transaction,
            GraphPathComparison pathComparison,
            CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _command = connection.CreateCommand();
            _command.Transaction = transaction;
            _command.CommandText =
                "INSERT INTO snapshot_observations(kind_name, stable_key, canonical_row_hash, payload_json) VALUES ($kind, $key, $hash, $payload);";
            _kind = _command.Parameters.Add("$kind", SqliteType.Text);
            _key = _command.Parameters.Add("$key", SqliteType.Text);
            _hash = _command.Parameters.Add("$hash", SqliteType.Text);
            _payload = _command.Parameters.Add("$payload", SqliteType.Text);
            PathComparison = pathComparison;
        }

        internal GraphPathComparison PathComparison { get; }

        internal void Add(GraphProjectionObservation observation, SnapshotAccumulator accumulator)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (accumulator.TotalCount >= MaximumSnapshotRows)
            {
                throw new GraphPersistenceException(
                    "source-snapshot-limit",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The graph source manifest exceeds the hard {MaximumSnapshotRows:N0}-observation safety ceiling."));
            }

            _kind.Value = observation.Kind.ToString();
            _key.Value = observation.StableKey;
            _hash.Value = observation.CanonicalRowHash;
            _payload.Value = GraphCanonicalSerializer.SerializeObservation(observation);
            try
            {
                _command.ExecuteNonQuery();
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new GraphPersistenceException(
                    "source-duplicate-stable-key",
                    "The graph source contains duplicate stable observation keys.",
                    exception);
            }

            accumulator.TotalCount++;
            accumulator.Revision = Math.Max(accumulator.Revision, observation.Revision);
            accumulator.Counts[observation.Kind] = accumulator.Counts.GetValueOrDefault(observation.Kind) + 1;
        }

        public void Dispose() => _command.Dispose();
    }

    private sealed class RelationshipCapture
    {
        private readonly List<GraphProjectionEvidence> _evidence = [];
        private bool _invalidEvidence;

        private RelationshipCapture()
        {
        }

        internal required string RelationshipId { get; init; }
        internal required string FirstFileId { get; init; }
        internal required string SecondFileId { get; init; }
        internal required int RelationshipTypeValue { get; init; }
        internal string? CustomType { get; init; }
        internal required int ConfidenceValue { get; init; }
        internal required string Algorithm { get; init; }
        internal required string AlgorithmVersion { get; init; }
        internal required long CreatedTicks { get; init; }
        internal required long ValidatedTicks { get; init; }
        internal required int DecisionValue { get; init; }
        internal required bool IsManual { get; init; }
        internal required bool IsPrivacyExcluded { get; init; }

        internal static RelationshipCapture From(SqliteDataReader reader) => new()
        {
            RelationshipId = RequireStableId(reader.GetString(0), "relationship ID"),
            FirstFileId = RequireStableId(reader.GetString(1), "relationship endpoint ID"),
            SecondFileId = RequireStableId(reader.GetString(2), "relationship endpoint ID"),
            RelationshipTypeValue = reader.GetInt32(3),
            CustomType = reader.IsDBNull(4) ? null : reader.GetString(4),
            ConfidenceValue = reader.GetInt32(5),
            Algorithm = SafeDisplay(reader.GetString(6), "legacy-relationship"),
            AlgorithmVersion = SafeDisplay(reader.GetString(7), "unknown"),
            CreatedTicks = NonNegative(reader.GetInt64(8)),
            ValidatedTicks = NonNegative(reader.GetInt64(9)),
            DecisionValue = reader.GetInt32(10),
            IsManual = reader.GetInt32(11) != 0,
            IsPrivacyExcluded = !reader.IsDBNull(16) || !reader.IsDBNull(17) ||
                reader.GetInt32(18) == 0 || reader.GetInt32(19) == 0 ||
                reader.GetInt32(20) != 0 || reader.GetInt32(21) != 0 ||
                reader.GetInt32(22) != 0 || reader.GetInt32(23) != 0,
        };

        internal void AddEvidence(SqliteDataReader reader)
        {
            if (reader.IsDBNull(12))
            {
                return;
            }

            if (_evidence.Count >= GraphLimits.MaximumEvidencePerEdge)
            {
                _invalidEvidence = true;
                return;
            }

            var explanation = SafeDisplay(reader.GetString(15), string.Empty);
            if (explanation.Length == 0)
            {
                _invalidEvidence = true;
                return;
            }

            var evidenceKindValue = reader.GetInt32(13);
            var evidenceKeyRaw = reader.GetString(14);
            var stableEvidenceKey = CompositeObservationKey(
                "relationship-evidence",
                RelationshipId,
                reader.GetInt32(12).ToString(CultureInfo.InvariantCulture));
            _evidence.Add(new GraphProjectionEvidence(
                stableEvidenceKey,
                MapEvidenceKind(evidenceKindValue),
                BoundedEvidenceKey(evidenceKeyRaw),
                EvidenceTemplate(evidenceKindValue),
                explanation,
                HashFields(RelationshipId, reader.GetInt32(12), evidenceKindValue, evidenceKeyRaw, explanation)));
        }

        internal void Write(SnapshotWriter writer, SnapshotAccumulator accumulator)
        {
            var revision = Math.Max(CreatedTicks, ValidatedTicks);
            var relationshipType = RelationshipTypeCode(RelationshipTypeValue, CustomType);
            var decisionKnown = Enum.IsDefined(typeof(RelationshipDecision), DecisionValue);
            var rejected = !decisionKnown || DecisionValue is
                (int)RelationshipDecision.Rejected or (int)RelationshipDecision.NeverRelate;
            if (!IsManual && (_invalidEvidence || _evidence.Count == 0))
            {
                rejected = true;
            }

            var confidence = Enum.IsDefined(typeof(RelationshipConfidence), ConfidenceValue)
                ? (GraphConfidenceLevel)ConfidenceValue
                : GraphConfidenceLevel.Low;
            var evidenceHash = HashFields(_evidence
                .OrderBy(item => item.StableKey, StringComparer.Ordinal)
                .SelectMany(item => new object?[]
                {
                    item.StableKey,
                    item.Kind.Value,
                    item.EvidenceKey,
                    item.ExplanationTemplateCode,
                    item.Explanation,
                    item.CanonicalObservationHash,
                })
                .ToArray());
            var observation = new GraphRelationshipObservation
            {
                StableKey = ObservationKey("relationship", RelationshipId),
                CanonicalRowHash = HashFields(
                    RelationshipId,
                    FirstFileId,
                    SecondFileId,
                    RelationshipTypeValue,
                    CustomType,
                    ConfidenceValue,
                    Algorithm,
                    AlgorithmVersion,
                    CreatedTicks,
                    ValidatedTicks,
                    DecisionValue,
                    IsManual,
                    IsPrivacyExcluded,
                    _invalidEvidence,
                    evidenceHash),
                Revision = revision,
                ObservedAtUtc = SafeUtc(revision),
                IsExcluded = IsPrivacyExcluded,
                RelationshipId = RelationshipId,
                FirstFileId = FirstFileId,
                SecondFileId = SecondFileId,
                RelationshipType = relationshipType,
                Confidence = IsManual ? GraphConfidenceLevel.Confirmed : confidence,
                Evidence = _evidence.OrderBy(item => item.StableKey, StringComparer.Ordinal).ToArray(),
                Algorithm = Algorithm,
                AlgorithmVersion = AlgorithmVersion,
                IsManual = IsManual,
                IsRejected = rejected,
            };
            writer.Add(observation, accumulator);
        }

        private static GraphEvidenceKind MapEvidenceKind(int value) => value switch
        {
            (int)RelationshipEvidenceKind.DuplicateContent => GraphEvidenceKind.ExactContentHash,
            (int)RelationshipEvidenceKind.Folder => GraphEvidenceKind.RelativeFolder,
            (int)RelationshipEvidenceKind.Manual => GraphEvidenceKind.Manual,
            _ => GraphEvidenceKind.LegacyRelationship,
        };

        private static string EvidenceTemplate(int value)
        {
            var suffix = Enum.IsDefined(typeof(RelationshipEvidenceKind), value)
                ? ((RelationshipEvidenceKind)value).ToString().ToLowerInvariant()
                : "unknown";
            return string.Concat("legacy-", suffix);
        }

        private static string BoundedEvidenceKey(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= GraphLimits.MaximumStableIdCharacters && !ContainsInvalidText(value)
                ? value.Normalize(NormalizationForm.FormC)
                : string.Concat("evidence:", HashFields(value).ToLowerInvariant());

        private static string RelationshipTypeCode(int value, string? customType)
        {
            if (!Enum.IsDefined(typeof(RelationshipType), value))
            {
                return string.Concat("legacy-unknown-", value.ToString(CultureInfo.InvariantCulture));
            }

            var type = (RelationshipType)value;
            return type == RelationshipType.Custom
                ? string.Concat("Custom: ", SafeDisplay(customType ?? string.Empty, "Unspecified"))
                : type.ToString();
        }
    }
}
