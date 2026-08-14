using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Indexing.Sqlite;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Validates the embedded provider's durability, migration, incremental, and recovery behavior.</summary>
public sealed class SqliteDeepIndexStoreTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    /// <summary>Verifies fresh schema initialization and integrity.</summary>
    [Fact]
    public async Task InitializeCreatesVersionedIntegrityCheckedDatabase()
    {
        using var fixture = new IndexFixture();
        await using var store = fixture.CreateStore();

        await store.InitializeAsync();

        Assert.True(File.Exists(fixture.DatabasePath));
        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        Assert.Equal("wal", ReadScalar(fixture.DatabasePath, "PRAGMA journal_mode;"));
        Assert.Equal("ok", ReadScalar(fixture.DatabasePath, "PRAGMA quick_check;"));
        Assert.Equal(
            "1",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'index_media_content';"));
    }

    /// <summary>Verifies the exact production v2.1 schema migrates transactionally and preserves searchable data.</summary>
    [Fact]
    public async Task VersionThreeMigratesToMediaSchemaWithRecoveryBackup()
    {
        using var fixture = new IndexFixture();
        CreateDatabase(
            fixture.DatabasePath,
            SqliteDeepIndexSchema.CreateVersionOne +
            SqliteDeepIndexSchema.CreateVersionTwo +
            SqliteDeepIndexSchema.CreateVersionThree +
            """
            INSERT INTO index_meta(key, value) VALUES ('schema_version', '3');
            INSERT INTO index_sources(
                id, root_path, root_path_key, display_name, indexing_level,
                include_subfolders, enabled, priority, exclusions_json,
                managed_by_watched_folders, created_utc_ticks, updated_utc_ticks)
            VALUES ('source:v21', 'C:/v21', 'c:/v21', 'v2.1 fixture', 1, 1, 1, 0, '[]', 0, 0, 0);
            INSERT INTO index_content(
                content_hash, extracted_text, ocr_text, summary, keywords_json,
                semantic_json, coverage_level, processor_fingerprint, updated_utc_ticks)
            VALUES ('hash:v21', 'preserved Raspberry Pi monitoring text', NULL, NULL, '[]', NULL, 1, 'v2.1', 0);
            INSERT INTO index_files(
                id, source_id, full_path, path_key, relative_path, relative_path_key,
                stable_identity, file_system_id, length, creation_utc_ticks,
                modified_utc_ticks, attributes, metadata_fingerprint, content_hash,
                processor_fingerprint, indexing_level, fully_indexed, deleted_utc_ticks,
                last_seen_run_id, updated_utc_ticks)
            VALUES (
                'file:v21', 'source:v21', 'C:/v21/notes.txt', 'c:/v21/notes.txt',
                'notes.txt', 'notes.txt', NULL, NULL, 42, 0, 0, 0, 'metadata:v21',
                'hash:v21', 'processor:v21', 1, 1, NULL, NULL, 0);
            PRAGMA user_version = 3;
            """);
        await using var migrated = fixture.CreateStore();

        await migrated.InitializeAsync();

        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        var preserved = Assert.Single(await migrated.GetSearchDocumentsAsync(10));
        Assert.Equal("notes.txt", preserved.FileName);
        Assert.Contains("Raspberry Pi", preserved.ExtractedText, StringComparison.Ordinal);
        Assert.Equal(
            "1",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'index_media_content';"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));

        await using var reopened = fixture.CreateStore();
        await reopened.InitializeAsync();
        Assert.Single(await reopened.GetSearchDocumentsAsync(10));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));
    }

    /// <summary>Verifies the exact v2.2 schema reaches schema 6 without altering existing records.</summary>
    [Fact]
    public async Task VersionFourMigratesToContentIntelligenceSchemaAndPreservesSearch()
    {
        using var fixture = new IndexFixture();
        CreateDatabase(
            fixture.DatabasePath,
            SqliteDeepIndexSchema.CreateVersionOne +
            SqliteDeepIndexSchema.CreateVersionTwo +
            SqliteDeepIndexSchema.CreateVersionThree +
            SqliteDeepIndexSchema.CreateVersionFour +
            """
            ALTER TABLE index_relationship_features ADD COLUMN media_transcript_fingerprint TEXT;
            ALTER TABLE index_relationship_features ADD COLUMN media_ocr_fingerprint TEXT;
            ALTER TABLE index_relationship_features ADD COLUMN media_device_key TEXT;
            ALTER TABLE index_relationship_features ADD COLUMN capture_date_bucket INTEGER;
            INSERT INTO index_meta(key, value) VALUES ('schema_version', '4');
            INSERT INTO index_sources(
                id, root_path, root_path_key, display_name, indexing_level,
                include_subfolders, enabled, priority, exclusions_json,
                managed_by_watched_folders, created_utc_ticks, updated_utc_ticks)
            VALUES ('source:v22', 'C:/v22', 'c:/v22', 'v2.2 fixture', 1, 1, 1, 0, '[]', 0, 0, 0);
            INSERT INTO index_content(
                content_hash, extracted_text, ocr_text, summary, keywords_json,
                semantic_json, coverage_level, processor_fingerprint, updated_utc_ticks)
            VALUES ('hash:v22', 'preserved Docker monitoring text', NULL, NULL, '[]', NULL, 1, 'v2.2', 0);
            INSERT INTO index_files(
                id, source_id, full_path, path_key, relative_path, relative_path_key,
                stable_identity, file_system_id, length, creation_utc_ticks,
                modified_utc_ticks, attributes, metadata_fingerprint, content_hash,
                processor_fingerprint, indexing_level, fully_indexed, deleted_utc_ticks,
                last_seen_run_id, updated_utc_ticks)
            VALUES (
                'file:v22', 'source:v22', 'C:/v22/monitoring.txt', 'c:/v22/monitoring.txt',
                'monitoring.txt', 'monitoring.txt', NULL, NULL, 42, 0, 0, 0, 'metadata:v22',
                'hash:v22', 'processor:v22', 1, 1, NULL, NULL, 0);
            PRAGMA user_version = 4;
            """);
        await using var migrated = fixture.CreateStore();

        await migrated.InitializeAsync();

        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        var preserved = Assert.Single(await migrated.GetSearchDocumentsAsync(10));
        Assert.Contains("Docker monitoring", preserved.ExtractedText, StringComparison.Ordinal);
        Assert.Equal(
            "1",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM pragma_table_info('index_content') WHERE name = 'content_intelligence_json';"));
        Assert.Equal(
            "1",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'index_relationship_feature_terms';"));
        Assert.Equal(
            "1",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_relationship_feature_terms_term';"));
        Assert.Equal(
            "3",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('smart_tag_definitions','file_smart_tag_assignments','file_smart_tag_decisions');"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));

        await migrated.InitializeAsync();
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));
    }

    /// <summary>Verifies a genuine schema-5 index gains normalized schema-6 Smart Tag authority transactionally.</summary>
    [Fact]
    public async Task VersionFiveMigratesToSmartTagSchemaWithBackupAndPreservesSearch()
    {
        using var fixture = new IndexFixture();
        CreateDatabase(
            fixture.DatabasePath,
            SqliteDeepIndexSchema.CreateVersionOne +
            SqliteDeepIndexSchema.CreateVersionTwo +
            SqliteDeepIndexSchema.CreateVersionThree +
            SqliteDeepIndexSchema.CreateVersionFour +
            """
            ALTER TABLE index_relationship_features ADD COLUMN media_transcript_fingerprint TEXT;
            ALTER TABLE index_relationship_features ADD COLUMN media_ocr_fingerprint TEXT;
            ALTER TABLE index_relationship_features ADD COLUMN media_device_key TEXT;
            ALTER TABLE index_relationship_features ADD COLUMN capture_date_bucket INTEGER;
            ALTER TABLE index_content ADD COLUMN content_intelligence_json TEXT;
            """ +
            SqliteDeepIndexSchema.CreateVersionFive +
            """
            INSERT INTO index_meta(key, value) VALUES ('schema_version', '5');
            INSERT INTO index_sources(
                id, root_path, root_path_key, display_name, indexing_level,
                include_subfolders, enabled, priority, exclusions_json,
                managed_by_watched_folders, created_utc_ticks, updated_utc_ticks)
            VALUES ('source:v25', 'C:/v25', 'c:/v25', 'v2.5 fixture', 1, 1, 1, 0, '[]', 0, 0, 0);
            INSERT INTO index_content(
                content_hash, extracted_text, ocr_text, summary, keywords_json,
                semantic_json, coverage_level, processor_fingerprint, updated_utc_ticks,
                content_intelligence_json)
            VALUES ('hash:v25', 'preserved searchable invoice text', NULL, NULL, '[]', NULL, 1, 'v2.5', 0, NULL);
            INSERT INTO index_files(
                id, source_id, full_path, path_key, relative_path, relative_path_key,
                stable_identity, file_system_id, length, creation_utc_ticks,
                modified_utc_ticks, attributes, metadata_fingerprint, content_hash,
                processor_fingerprint, indexing_level, fully_indexed, deleted_utc_ticks,
                last_seen_run_id, updated_utc_ticks)
            VALUES ('file:v25', 'source:v25', 'C:/v25/scan.txt', 'c:/v25/scan.txt',
                'scan.txt', 'scan.txt', NULL, NULL, 42, 0, 0, 0, 'metadata:v25',
                'hash:v25', 'processor:v25', 1, 1, NULL, NULL, 0);
            PRAGMA user_version = 5;
            """);
        await using var migrated = fixture.CreateStore();

        await migrated.InitializeAsync();

        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        Assert.Contains("invoice", Assert.Single(await migrated.GetSearchDocumentsAsync(10)).ExtractedText, StringComparison.Ordinal);
        Assert.Equal("27", ReadScalar(fixture.DatabasePath, "SELECT COUNT(*) FROM smart_tag_definitions WHERE is_builtin = 1;"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));

        await migrated.InitializeAsync();
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));
    }

    /// <summary>Schema-6 assignments preserve user authority and exact typed-filter semantics.</summary>
    [Fact]
    public async Task SmartTagsPersistAuthorityAndFilterByCanonicalTypeGroups()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Standard),
            [fixture.Observation("finance-invoice.txt", "finance"), fixture.Observation("legal-invoice.txt", "legal")]);
        var fileIds = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await store.ClaimNextAsync(Epoch.AddDays(10)) is { } claim)
        {
            fileIds[claim.Observation.RelativePath] = claim.FileId;
            var next = claim.Stage switch
            {
                IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
                IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
                IndexingStage.ContentFingerprinted => IndexingStage.TextExtracted,
                IndexingStage.TextExtracted => IndexingStage.SummaryKeywordsGenerated,
                IndexingStage.SummaryKeywordsGenerated => IndexingStage.SemanticRepresentationGenerated,
                IndexingStage.SemanticRepresentationGenerated => IndexingStage.SmartTagsClassified,
                IndexingStage.SmartTagsClassified => IndexingStage.SearchIndexUpdated,
                IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
                IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
                IndexingStage.FileFullyIndexed => (IndexingStage?)null,
                _ => throw new InvalidOperationException("Unexpected stage."),
            };
            var output = claim.Stage switch
            {
                IndexingStage.ContentFingerprinted => new IndexingStageOutput { Status = IndexingStageStatus.Complete, ContentHash = "hash-" + claim.FileId },
                IndexingStage.TextExtracted => new IndexingStageOutput { Status = IndexingStageStatus.Complete, ExtractedText = "invoice evidence" },
                IndexingStage.SmartTagsClassified => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    SmartTagClassification = Classification(
                        claim.Observation.RelativePath.StartsWith("finance", StringComparison.Ordinal)
                            ? "theme.finance"
                            : "theme.legal",
                        "document-type.invoice"),
                },
                _ => new IndexingStageOutput { Status = IndexingStageStatus.Complete },
            };
            await store.SaveStageOutputAsync(claim, output, next, Epoch.AddDays(10), TimeSpan.Zero, null);
        }

        var finance = fileIds["finance-invoice.txt"];
        var legal = fileIds["legal-invoice.txt"];
        await store.AddUserTagAsync(finance, "Review", Epoch.AddDays(11));
        var financeTags = await store.GetFileSmartTagsAsync(finance);
        var userTag = Assert.Single(financeTags, tag => tag.Definition.Type == SmartTagType.UserTag);

        Assert.Equal(
            new[] { finance, legal }.OrderBy(value => value, StringComparer.Ordinal),
            await store.FilterFileIdsBySmartTagsAsync(
                new SmartTagFilter(
                    ThemeTagIds: ["theme.finance", "theme.legal"],
                    DocumentTypeTagIds: ["document-type.invoice"]),
                10));
        Assert.Equal(
            [finance],
            await store.FilterFileIdsBySmartTagsAsync(
                new SmartTagFilter(
                    ThemeTagIds: ["theme.finance"],
                    UserTagIds: [userTag.Definition.TagId]),
                10));

        await store.SetTagDecisionAsync(finance, "theme.finance", SmartTagDecision.Rejected, Epoch.AddDays(12));
        Assert.DoesNotContain(await store.GetFileSmartTagsAsync(finance), tag => tag.Definition.TagId == "theme.finance");
        Assert.Empty(await store.FilterFileIdsBySmartTagsAsync(new SmartTagFilter(ThemeTagIds: ["theme.finance"]), 10));

        await store.ResetTagDecisionsAsync(finance, Epoch.AddDays(13));
        Assert.Contains(await store.GetFileSmartTagsAsync(finance), tag => tag.Definition.TagId == "theme.finance");
        Assert.Equal(userTag.Definition.TagId, Assert.Single(await store.GetFileSmartTagsAsync(finance), tag => tag.Definition.Type == SmartTagType.UserTag).Definition.TagId);
    }

    /// <summary>Only stale classification work is prepared when classifier or taxonomy versions change.</summary>
    [Fact]
    public async Task TaxonomyChangePreparesOnlySmartTagReclassification()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("invoice.txt")]);
        await CompleteStandardRunAsync(store, "hash-smart-tag-reclass");

        var affected = await store.PrepareStaleClassificationsAsync("classifier-next", "taxonomy-next", Epoch.AddDays(20));
        var second = await store.PrepareStaleClassificationsAsync("classifier-next", "taxonomy-next", Epoch.AddDays(20));

        Assert.Equal(1, affected);
        Assert.Equal(0, second);
        Assert.Equal(
            ((int)IndexingStage.SmartTagsClassified).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ReadScalar(fixture.DatabasePath, "SELECT repair_stage FROM index_privacy_rules LIMIT 1;"));
        Assert.Equal("0", ReadScalar(fixture.DatabasePath, "SELECT COUNT(*) FROM file_smart_tag_status;"));
        Assert.Equal("1", ReadScalar(fixture.DatabasePath, "SELECT COUNT(*) FROM index_content WHERE extracted_text IS NOT NULL;"));
    }

    /// <summary>Accepted and rejected authority survives regenerated evidence and remains file-specific.</summary>
    [Fact]
    public async Task ReclassificationPreservesAcceptedAndRejectedUserAuthority()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("authority.txt")]);
        await CompleteStandardRunAsync(
            store,
            "authority-hash",
            Classification("theme.finance", "document-type.invoice"));
        var fileId = Assert.Single(await store.GetSearchDocumentsAsync(10)).FileId;
        await store.SetTagDecisionAsync(fileId, "document-type.invoice", SmartTagDecision.Accepted, Epoch.AddDays(11));
        await store.SetTagDecisionAsync(fileId, "theme.finance", SmartTagDecision.Rejected, Epoch.AddDays(11));

        Assert.Equal(1, await store.PrepareStaleClassificationsAsync(
            "classifier-next",
            "taxonomy-next",
            Epoch.AddDays(12)));
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Standard),
            [fixture.Observation("authority.txt")],
            Epoch.AddDays(13));
        var repair = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(13)));
        Assert.Equal(IndexingStage.SmartTagsClassified, repair.Stage);
        await store.SaveStageOutputAsync(
            repair,
            new IndexingStageOutput
            {
                Status = IndexingStageStatus.Complete,
                SmartTagClassification = Classification("theme.finance", "document-type.invoice"),
            },
            IndexingStage.SearchIndexUpdated,
            Epoch.AddDays(13),
            TimeSpan.FromMilliseconds(1),
            null);

        var tags = await store.GetFileSmartTagsAsync(fileId);
        Assert.Contains(tags, tag => tag.Definition.TagId == "document-type.invoice" &&
            tag.Decision == SmartTagDecision.Accepted && tag.State == SmartTagAssignmentState.Accepted);
        Assert.DoesNotContain(tags, tag => tag.Definition.TagId == "theme.finance");
        await store.ResetTagDecisionsAsync(fileId, Epoch.AddDays(14));
        Assert.Contains(await store.GetFileSmartTagsAsync(fileId), tag =>
            tag.Definition.TagId == "theme.finance" && tag.State == SmartTagAssignmentState.Automatic);
    }

    /// <summary>No-evidence passes retain real fingerprints and are not needlessly queued again at startup.</summary>
    [Fact]
    public async Task NoEvidenceClassificationRetainsFingerprintAndAvoidsRequeue()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("empty.txt")]);

        while (await store.ClaimNextAsync(Epoch.AddDays(10)) is { } claim)
        {
            var next = claim.Stage switch
            {
                IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
                IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
                IndexingStage.ContentFingerprinted => IndexingStage.TextExtracted,
                IndexingStage.TextExtracted => IndexingStage.SummaryKeywordsGenerated,
                IndexingStage.SummaryKeywordsGenerated => IndexingStage.SemanticRepresentationGenerated,
                IndexingStage.SemanticRepresentationGenerated => IndexingStage.SmartTagsClassified,
                IndexingStage.SmartTagsClassified => IndexingStage.SearchIndexUpdated,
                IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
                IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
                IndexingStage.FileFullyIndexed => (IndexingStage?)null,
                _ => throw new InvalidOperationException("Unexpected stage."),
            };
            var output = claim.Stage switch
            {
                IndexingStage.ContentFingerprinted => new IndexingStageOutput { Status = IndexingStageStatus.Complete, ContentHash = "hash-empty" },
                IndexingStage.SmartTagsClassified => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    SmartTagClassification = new SmartTagClassificationResult(
                        SmartTagClassificationState.NoEvidence,
                        [],
                        "No evidence.")
                    {
                        Classifier = "test-classifier",
                        ClassifierVersion = "test-version",
                        TaxonomyVersion = "test-taxonomy",
                        InputFingerprint = "input-empty",
                    },
                },
                _ => new IndexingStageOutput { Status = IndexingStageStatus.Complete },
            };
            await store.SaveStageOutputAsync(claim, output, next, Epoch.AddDays(10), TimeSpan.Zero, null);
        }

        Assert.Equal(0, await store.PrepareStaleClassificationsAsync("test-version", "test-taxonomy", Epoch.AddDays(11)));
        Assert.Equal("test-version", ReadScalar(fixture.DatabasePath, "SELECT classifier_version FROM file_smart_tag_status LIMIT 1;"));
        Assert.Equal("input-empty", ReadScalar(fixture.DatabasePath, "SELECT input_fingerprint FROM file_smart_tag_status LIMIT 1;"));
    }

    /// <summary>Generated clearing and file forgetting preserve or remove authority according to their distinct contracts.</summary>
    [Fact]
    public async Task SmartTagClearAndForgetHaveDistinctAuthoritySemantics()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("invoice.txt")]);
        await CompleteStandardRunAsync(store, "hash-clear", Classification("theme.finance", "document-type.invoice"));
        var fileId = Assert.Single(await store.GetSearchDocumentsAsync(10)).FileId;

        await store.AddUserTagAsync(fileId, "Review", Epoch.AddDays(11));
        await store.SetTagDecisionAsync(fileId, "document-type.invoice", SmartTagDecision.Accepted, Epoch.AddDays(12));
        await store.SetTagDecisionAsync(fileId, "theme.finance", SmartTagDecision.Rejected, Epoch.AddDays(12));
        await store.ClearGeneratedSmartTagsAsync(fileId, Epoch.AddDays(13));

        var retained = await store.GetFileSmartTagsAsync(fileId);
        Assert.Contains(retained, tag => tag.Definition.Type == SmartTagType.UserTag);
        Assert.Contains(retained, tag => tag.Definition.TagId == "document-type.invoice" && tag.Decision == SmartTagDecision.Accepted);
        Assert.DoesNotContain(retained, tag => tag.Definition.TagId == "theme.finance");
        Assert.Equal("2", ReadScalar(fixture.DatabasePath, "SELECT COUNT(*) FROM file_smart_tag_decisions;"));

        await store.ForgetFileAsync(fileId, Epoch.AddDays(14));
        Assert.Equal("0", ReadScalar(fixture.DatabasePath, "SELECT COUNT(*) FROM file_smart_tag_assignments;"));
        Assert.Equal("0", ReadScalar(fixture.DatabasePath, "SELECT COUNT(*) FROM file_smart_tag_decisions;"));
    }

    /// <summary>Legacy authority imports only exact active identities and is recorded once.</summary>
    [Fact]
    public async Task LegacySmartTagImportIsConservativeAndIdempotent()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var observation = fixture.Observation("legacy.txt", stableIdentity: "legacy-stable");
        await QueueAsync(store, fixture.Source(), [observation]);
        await CompleteBasicRunAsync(store, "hash-legacy");
        var fileId = Assert.Single(await store.GetSearchDocumentsAsync(10)).FileId;

        var imported = await store.ImportLegacySmartTagsAsync(
        [
            new LegacySmartTagImport(observation.FullPath, "Personal Project", "personal project", SmartTagDecision.Accepted, true, null),
            new LegacySmartTagImport(observation.FullPath, "Finance", "finance", SmartTagDecision.Rejected, false, "theme.finance"),
            new LegacySmartTagImport(Path.Combine(fixture.Root, "missing.txt"), "Review", "review", SmartTagDecision.Accepted, true, null),
        ], Epoch.AddDays(11));
        var repeated = await store.ImportLegacySmartTagsAsync([], Epoch.AddDays(12));

        Assert.True(imported.Applied);
        Assert.Equal(2, imported.AffectedCount);
        Assert.False(repeated.Applied);
        var tags = await store.GetFileSmartTagsAsync(fileId);
        Assert.Contains(tags, tag => tag.Definition.Type == SmartTagType.UserTag && tag.Definition.DisplayName == "Personal Project");
        Assert.DoesNotContain(tags, tag => tag.Definition.TagId == "theme.finance");
        Assert.Equal("2", ReadScalar(fixture.DatabasePath, "SELECT COUNT(*) FROM file_smart_tag_decisions;"));
        await store.ResetTagDecisionsAsync(fileId, Epoch.AddDays(13));
        Assert.DoesNotContain(await store.GetFileSmartTagsAsync(fileId), tag => tag.Definition.TagId == "theme.finance");
    }

    /// <summary>Verifies structured media evidence round-trips without being flattened into generic metadata.</summary>
    [Fact]
    public async Task MediaEvidenceRoundTripsThroughSearchAndPrivacyInspection()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("recording.m4a")]);
        await SaveCompleteAsync(store, Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10))), IndexingStage.MetadataIndexed);
        await SaveCompleteAsync(store, Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10))), IndexingStage.ContentFingerprinted);
        var fingerprint = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10)));
        var evidence = new IndexedMediaEvidence
        {
            Kind = MediaKind.Audio,
            Metadata = new MediaMetadata
            {
                Kind = MediaKind.Audio,
                Container = "m4a",
                Duration = TimeSpan.FromSeconds(65),
                DeviceModel = "Synthetic Recorder",
            },
            Transcript = "Raspberry Pi monitoring deployment",
            TranscriptSegments = [new MediaTranscriptSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), "Raspberry Pi monitoring deployment")],
            MetadataProvider = "synthetic",
            MetadataProviderVersion = "1",
            TranscriptionProvider = "synthetic-transcriber",
            ProcessingFingerprint = "media-fingerprint",
            Status = MediaExtractionStatus.Completed,
        };
        await store.SaveStageOutputAsync(
            fingerprint,
            new IndexingStageOutput
            {
                Status = IndexingStageStatus.Complete,
                ContentHash = "media-hash",
                MediaEvidence = evidence,
            },
            IndexingStage.SearchIndexUpdated,
            Epoch.AddDays(10),
            TimeSpan.FromMilliseconds(1),
            null);
        await CompleteBasicRunAsync(store, "unused");

        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);
        var privacy = Assert.IsType<IndexPrivacyItem>(await store.InspectFileAsync(document.FileId));

        Assert.Equal("Raspberry Pi monitoring deployment", document.MediaEvidence?.Transcript);
        Assert.DoesNotContain("Raspberry", document.MetadataText, StringComparison.OrdinalIgnoreCase);
        Assert.True(privacy.HasMediaDerivedData);
        Assert.True(privacy.HasMediaTranscript);
        Assert.Equal("Audio", privacy.MediaKind);
    }

    /// <summary>Verifies media-only clearing removes derived evidence while preserving the source record.</summary>
    [Fact]
    public async Task ClearMediaDerivedDataPreservesIndexedFileAndOriginalSourceRegistration()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("photo.jpg")]);
        await SaveCompleteAsync(store, Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10))), IndexingStage.MetadataIndexed);
        await SaveCompleteAsync(store, Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10))), IndexingStage.ContentFingerprinted);
        var fingerprint = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10)));
        await store.SaveStageOutputAsync(
            fingerprint,
            new IndexingStageOutput
            {
                Status = IndexingStageStatus.Complete,
                ContentHash = "photo-hash",
                MediaEvidence = new IndexedMediaEvidence
                {
                    Kind = MediaKind.Image,
                    Metadata = new MediaMetadata { Kind = MediaKind.Image, Width = 100, Height = 50 },
                    OcrText = "private screenshot command",
                    MetadataProvider = "synthetic",
                    MetadataProviderVersion = "1",
                    ProcessingFingerprint = "photo-fingerprint",
                    Status = MediaExtractionStatus.Completed,
                },
            },
            IndexingStage.SearchIndexUpdated,
            Epoch.AddDays(10),
            TimeSpan.Zero,
            null);
        await CompleteBasicRunAsync(store, "unused");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        var cleared = await store.ClearFileDataAsync(document.FileId, IndexedDataKind.MediaDerived, Epoch.AddDays(11));
        var after = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        Assert.True(cleared.Applied);
        Assert.Null(after.MediaEvidence);
        Assert.Single(await store.GetSourcesAsync());
    }

    /// <summary>Verifies structured Content Intelligence round-trips and its explicit privacy category clears only derived index data.</summary>
    [Fact]
    public async Task ContentIntelligenceRoundTripsAndClearsWithoutChangingSourceRegistration()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("concepts.txt")]);
        await CompleteStandardRunAsync(store, "concept-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        Assert.Equal("Raspberry Pi monitoring", Assert.Single(document.ContentIntelligence!.Topics).DisplayName);
        var privacy = Assert.IsType<IndexPrivacyItem>(await store.InspectFileAsync(document.FileId));
        Assert.True(privacy.HasContentIntelligence);

        var result = await store.ClearFileDataAsync(
            document.FileId,
            IndexedDataKind.ContentIntelligence,
            Epoch.AddDays(11));
        var cleared = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        Assert.True(result.Applied);
        Assert.Null(cleared.ContentIntelligence);
        Assert.Single(await store.GetSourcesAsync());
    }

    /// <summary>Verifies malformed retained media JSON is excluded and surfaced as an indexing failure.</summary>
    [Fact]
    public async Task CorruptMediaEvidenceFailsClosedWithoutCorruptResultObject()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("photo.jpg")]);
        await SaveCompleteAsync(store, Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10))), IndexingStage.MetadataIndexed);
        await SaveCompleteAsync(store, Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10))), IndexingStage.ContentFingerprinted);
        var fingerprint = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(10)));
        await store.SaveStageOutputAsync(
            fingerprint,
            new IndexingStageOutput
            {
                Status = IndexingStageStatus.Complete,
                ContentHash = "corrupt-media-hash",
                MediaEvidence = new IndexedMediaEvidence
                {
                    Kind = MediaKind.Image,
                    Metadata = new MediaMetadata { Kind = MediaKind.Image, Width = 100, Height = 50 },
                    MetadataProvider = "synthetic",
                    MetadataProviderVersion = "1",
                    ProcessingFingerprint = "synthetic",
                    Status = MediaExtractionStatus.Completed,
                },
            },
            IndexingStage.SearchIndexUpdated,
            Epoch.AddDays(10),
            TimeSpan.Zero,
            null);
        CreateDatabase(fixture.DatabasePath, "UPDATE index_media_content SET evidence_json = '{not-json';");

        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        Assert.Null(document.MediaEvidence);
        Assert.True(document.HasIndexingFailure);
    }

    /// <summary>Verifies malformed retained Content Intelligence is omitted and made visible as an indexing failure.</summary>
    [Fact]
    public async Task CorruptContentIntelligenceFailsClosedWithoutCorruptResultObject()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("concepts.txt")]);
        await CompleteStandardRunAsync(store, "concept-hash");
        CreateDatabase(fixture.DatabasePath, "UPDATE index_content SET content_intelligence_json = '{not-json';");

        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        Assert.Null(document.ContentIntelligence);
        Assert.True(document.HasIndexingFailure);
        Assert.Equal("concepts.txt", document.FileName);
    }

    /// <summary>Verifies syntactically valid hostile JSON cannot null required derived collections.</summary>
    [Fact]
    public async Task NullContentIntelligenceCollectionsFailClosedWithoutCorruptSearchObject()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("null-concepts.txt")]);
        await CompleteStandardRunAsync(store, "null-concepts-hash");
        CreateDatabase(
            fixture.DatabasePath,
            """
            UPDATE index_content
            SET content_intelligence_json = '{"Topics":null,"Entities":[],"Keywords":[],"Provider":"hostile","ProviderVersion":"1","ProcessingFingerprint":"hostile"}';
            """);

        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        Assert.Null(document.ContentIntelligence);
        Assert.True(document.HasIndexingFailure);
        Assert.Equal("null-concepts.txt", document.FileName);
    }

    /// <summary>Verifies a newer schema fails closed without mutation.</summary>
    [Fact]
    public async Task InitializeRejectsUnsupportedNewerSchemaWithoutChangingIt()
    {
        using var fixture = new IndexFixture();
        CreateDatabase(fixture.DatabasePath, "PRAGMA user_version = 99;");
        await using var store = fixture.CreateStore();

        var exception = await Assert.ThrowsAsync<DeepIndexUnsupportedSchemaException>(
            () => store.InitializeAsync());

        Assert.Equal(99, exception.FoundVersion);
        Assert.Equal(DeepIndexingVersion.SchemaVersion, exception.SupportedVersion);
        Assert.Equal(99, ReadUserVersion(fixture.DatabasePath));
    }

    /// <summary>Verifies corrupt storage produces actionable recovery guidance.</summary>
    [Fact]
    public async Task InitializeReportsCorruptDatabaseWithRecoveryGuidance()
    {
        using var fixture = new IndexFixture();
        File.WriteAllBytes(fixture.DatabasePath, "not a sqlite database"u8.ToArray());
        await using var store = fixture.CreateStore();

        var exception = await Assert.ThrowsAsync<DeepIndexCorruptException>(
            () => store.InitializeAsync());

        Assert.Contains("rebuild", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("not a sqlite database", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath)));
    }

    /// <summary>Verifies explicit corrupt-storage recovery preserves evidence and creates a clean usable schema.</summary>
    [Fact]
    public async Task ExplicitStorageResetPreservesRecoveryCopyAndCreatesFreshSchema()
    {
        using var fixture = new IndexFixture();
        var original = "not a sqlite database"u8.ToArray();
        File.WriteAllBytes(fixture.DatabasePath, original);
        await using var store = fixture.CreateStore();
        _ = await Assert.ThrowsAsync<DeepIndexCorruptException>(() => store.InitializeAsync());

        var recoveryPath = Assert.IsType<string>(await store.ResetStorageAsync(Epoch));

        Assert.True(File.Exists(recoveryPath));
        Assert.Equal(original, File.ReadAllBytes(recoveryPath));
        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        Assert.Equal("ok", ReadScalar(fixture.DatabasePath, "PRAGMA quick_check;"));
        Assert.Empty(await store.GetSourcesAsync());
    }

    /// <summary>Verifies recovery copies and their SQLite sidecars obey the bounded backup policy.</summary>
    [Fact]
    public async Task ExplicitStorageResetPrunesOldRecoveryCopiesAndSidecars()
    {
        using var fixture = new IndexFixture();
        var backupDirectory = Path.Combine(fixture.Root, "backups");
        Directory.CreateDirectory(backupDirectory);
        for (var index = 0; index < 4; index++)
        {
            var path = Path.Combine(backupDirectory, $"deep-index-seed-{index}.db");
            File.WriteAllText(path, "seed");
            File.WriteAllText(path + ".wal", "wal");
            File.WriteAllText(path + ".shm", "shm");
            File.SetLastWriteTimeUtc(path, Epoch.UtcDateTime.AddMinutes(index));
        }

        var oldest = Path.Combine(backupDirectory, "deep-index-seed-0.db");
        File.WriteAllBytes(fixture.DatabasePath, "not a sqlite database"u8.ToArray());
        await using var store = fixture.CreateStore();

        _ = await store.ResetStorageAsync(Epoch.AddHours(1));

        Assert.Equal(3, Directory.EnumerateFiles(backupDirectory, "deep-index-*.db").Count());
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(oldest + ".wal"));
        Assert.False(File.Exists(oldest + ".shm"));
    }

    /// <summary>Verifies interrupted version-zero state migrates with a recovery copy.</summary>
    [Fact]
    public async Task InterruptedVersionZeroMigrationKeepsBackupAndCompletesAtomically()
    {
        using var fixture = new IndexFixture();
        CreateDatabase(
            fixture.DatabasePath,
            "CREATE TABLE legacy_partial(id TEXT PRIMARY KEY); INSERT INTO legacy_partial(id) VALUES ('retained'); PRAGMA user_version = 0;");
        await using var store = fixture.CreateStore();

        await store.InitializeAsync();

        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        Assert.Equal("retained", ReadScalar(fixture.DatabasePath, "SELECT id FROM legacy_partial LIMIT 1;"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));
    }

    /// <summary>Verifies bounded consistent manual backups.</summary>
    [Fact]
    public async Task ManualBackupIsConsistentAndRetainsAtMostThreeCopies()
    {
        using var fixture = new IndexFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();

        var backups = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            backups.Add(await store.CreateBackupAsync());
        }

        Assert.True(File.Exists(backups[^1]));
        Assert.Equal(3, Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "*.db").Count());
        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(backups[^1]));
    }

    /// <summary>Verifies provider-neutral source persistence.</summary>
    [Fact]
    public async Task SourceRoundTripsWithoutProviderDetailsInContract()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Deep) with
        {
            Priority = 42,
            Exclusions = ["*.tmp", "private/*"],
            ManagedByWatchedFolders = true,
        };

        await store.UpsertSourceAsync(source);
        var actual = Assert.Single(await store.GetSourcesAsync());

        Assert.Equal(source.Id, actual.Id);
        Assert.Equal(source.RootPath, actual.RootPath);
        Assert.Equal(source.DisplayName, actual.DisplayName);
        Assert.Equal(source.Level, actual.Level);
        Assert.Equal(source.IncludeSubfolders, actual.IncludeSubfolders);
        Assert.Equal(source.Enabled, actual.Enabled);
        Assert.Equal(source.Priority, actual.Priority);
        Assert.Equal(source.Exclusions, actual.Exclusions);
        Assert.True(actual.ManagedByWatchedFolders);
    }

    /// <summary>Verifies new files enter the durable discovery stage.</summary>
    [Fact]
    public async Task NewFileStartsAtDurableDiscoveryStage()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        var observation = fixture.Observation("new.txt", stableIdentity: "file-1");

        await QueueAsync(store, source, [observation]);
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(1)));

        Assert.Equal(IndexingStage.FileDiscovered, claim.Stage);
        Assert.Equal(observation.FullPath, claim.FullPath);
        Assert.Equal(1, claim.Attempt);
    }

    /// <summary>Verifies unchanged completed files avoid repeated stage work.</summary>
    [Fact]
    public async Task UnchangedFileIndexedTwiceDoesNotRepeatCompletedStages()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        var observation = fixture.Observation("same.txt", stableIdentity: "file-1");
        await QueueAsync(store, source, [observation]);
        await CompleteBasicRunAsync(store, "hash-a");

        await QueueAsync(store, source, [observation], Epoch.AddHours(1));

        Assert.Null(await store.ClaimNextAsync(Epoch.AddHours(2)));
        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddHours(2));
        Assert.Equal(1, progress.Completed);
        Assert.Equal(IndexingRunStatus.Complete, progress.Status);
    }

    /// <summary>Verifies stable rename and move identity reuse.</summary>
    [Theory]
    [InlineData("renamed.txt")]
    [InlineData("subfolder/moved.txt")]
    public async Task StableRenameOrMoveUpdatesPathWithoutRepeatingContent(string relativePath)
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        await QueueAsync(store, source, [fixture.Observation("original.txt", stableIdentity: "stable")]);
        await CompleteBasicRunAsync(store, "hash-a");
        var original = Assert.Single(await store.GetSearchDocumentsAsync(10));
        await store.AddUserTagAsync(original.FileId, "Important", Epoch.AddMinutes(1));

        await QueueAsync(store, source, [fixture.Observation(relativePath, stableIdentity: "stable")], Epoch.AddHours(1));

        Assert.Null(await store.ClaimNextAsync(Epoch.AddHours(2)));
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10));
        Assert.Equal(original.FileId, document.FileId);
        Assert.Equal(Path.GetFileName(relativePath), document.FileName);
        Assert.EndsWith(relativePath.Replace('/', Path.DirectorySeparatorChar), document.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("original.txt", document.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(await store.GetFileSmartTagsAsync(document.FileId), tag =>
            tag.Definition.Type == SmartTagType.UserTag && tag.Definition.DisplayName == "Important");
    }

    /// <summary>Verifies contextual Search resolves only requested visible durable identifiers.</summary>
    [Fact]
    public async Task ExactSearchDocumentLookupIsBoundedDeduplicatedAndPrivacyFiltered()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("context.txt", stableIdentity: "context")]);
        await CompleteBasicRunAsync(store, "context-hash");
        var indexed = Assert.Single(await store.GetSearchDocumentsAsync(10));

        var resolved = await store.GetSearchDocumentsByIdsAsync(
            ["missing", indexed.FileId, indexed.FileId]);

        Assert.Equal(indexed.FileId, Assert.Single(resolved).FileId);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.GetSearchDocumentsByIdsAsync(
                Enumerable.Range(0, RelationshipLimits.MaximumSearchExpansions + 1)
                    .Select(index => $"file-{index}")
                    .ToArray()));
    }

    /// <summary>Verifies metadata-only changes restart only the affected stages.</summary>
    [Fact]
    public async Task MetadataOnlyChangeRestartsAtMetadataAndRetainsContentHash()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        var initial = fixture.Observation("document.txt", stableIdentity: "stable");
        await QueueAsync(store, source, [initial]);
        await CompleteBasicRunAsync(store, "hash-a");
        var changed = initial with
        {
            LastWriteTimeUtc = initial.LastWriteTimeUtc.AddMinutes(1),
            MetadataFingerprint = "metadata-b",
        };

        await QueueAsync(store, source, [changed], Epoch.AddHours(1));
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));

        Assert.Equal(IndexingStage.MetadataIndexed, claim.Stage);
        Assert.Equal("hash-a", claim.ContentHash);
    }

    /// <summary>Verifies processor changes invalidate derived work.</summary>
    [Fact]
    public async Task ProcessorConfigurationChangeInvalidatesCompletedFile()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        var observation = fixture.Observation("document.txt", stableIdentity: "stable");
        await QueueAsync(store, source, [observation], processor: "processor-a");
        await CompleteBasicRunAsync(store, "hash-a");

        await QueueAsync(store, source, [observation], Epoch.AddHours(1), processor: "processor-b");
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));

        Assert.Equal(IndexingStage.MetadataIndexed, claim.Stage);
        Assert.Equal("processor-b", claim.ProcessorFingerprint);
    }

    /// <summary>Verifies duplicate content shares compatible derived data.</summary>
    [Fact]
    public async Task DuplicateContentCanReuseStandardDerivedData()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Standard);
        var first = fixture.Observation("first.txt", stableIdentity: "one");
        await QueueAsync(store, source, [first]);
        await CompleteStandardRunAsync(store, "shared-hash");
        var copy = fixture.Observation("copy.txt", stableIdentity: "two");
        await QueueAsync(store, source, [first, copy], Epoch.AddHours(1));
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));
        Assert.Equal(IndexingStage.FileDiscovered, claim.Stage);
        await SaveCompleteAsync(store, claim, IndexingStage.MetadataIndexed);
        claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));
        await SaveCompleteAsync(store, claim, IndexingStage.ContentFingerprinted);
        claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddHours(2)));

        var reusable = await store.GetReusableContentThroughStageAsync(
            "shared-hash",
            IndexingLevel.Standard,
            "processor");

        Assert.Equal(IndexingStage.SemanticRepresentationGenerated, reusable);
        await store.ReuseContentAsync(
            claim,
            "shared-hash",
            reusable!.Value,
            IndexingStage.SearchIndexUpdated,
            Epoch.AddHours(2));
        Assert.Equal(2, (await store.GetSearchDocumentsAsync(10)).Count);
    }

    /// <summary>Verifies deleted-record retention and explicit cleanup.</summary>
    [Fact]
    public async Task MissingFileIsRetainedThenRemovedByExplicitRetentionPolicy()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        await QueueAsync(store, source, [fixture.Observation("deleted.txt")]);
        await CompleteBasicRunAsync(store, "hash-a");

        await QueueAsync(store, source, [], Epoch.AddDays(1));
        Assert.Equal(0, (await store.GetSearchCoverageAsync()).KnownFileCount);
        var maintenance = await store.MaintainAsync(
            new DeepIndexingSettings { DeletedFileRetentionDays = 0 },
            Epoch.AddDays(2));

        Assert.Contains(maintenance.Actions, action => action.Code == "expired-deleted-files");
        Assert.Empty(await store.GetSearchDocumentsAsync(10));
    }

    /// <summary>Verifies source removal cleans orphaned derived data only.</summary>
    [Fact]
    public async Task RemovingSourceCleansOrphanedDerivedDataWithoutTouchingFolder()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Standard);
        await QueueAsync(store, source, [fixture.Observation("document.txt")]);
        await CompleteStandardRunAsync(store, "hash-a");

        await store.RemoveSourceAsync(source.Id);
        var maintenance = await store.MaintainAsync(new DeepIndexingSettings(), Epoch.AddDays(1));

        Assert.True(Directory.Exists(fixture.Root));
        Assert.Empty(await store.GetSourcesAsync());
        Assert.Empty(await store.GetSearchDocumentsAsync(10));
        Assert.True(maintenance.IsWithinQuota);
    }

    /// <summary>Verifies pause and resume are durable claim controls.</summary>
    [Fact]
    public async Task PausePreventsClaimAndResumeMakesWorkEligible()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("pause.txt")]);

        await store.SetActiveRunsStatusAsync(IndexingRunStatus.Paused, null, Epoch.AddMinutes(1));
        Assert.Null(await store.ClaimNextAsync(Epoch.AddMinutes(2)));
        await store.SetActiveRunsStatusAsync(IndexingRunStatus.Running, null, Epoch.AddMinutes(3));

        Assert.NotNull(await store.ClaimNextAsync(Epoch.AddMinutes(4)));
    }

    /// <summary>Verifies resource waiting never overwrites an explicit durable user pause.</summary>
    [Fact]
    public async Task ResourceWaitingDoesNotOverrideExplicitPause()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("paused.txt")]);
        await store.SetActiveRunsStatusAsync(IndexingRunStatus.Paused, null, Epoch.AddMinutes(1));

        await store.SetActiveRunsStatusAsync(
            IndexingRunStatus.Waiting,
            "waiting for resource policy",
            Epoch.AddMinutes(2));

        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(3));
        Assert.Equal(IndexingRunStatus.Paused, progress.Status);
        Assert.All(
            await store.GetResumableRunsAsync(),
            run => Assert.Equal(IndexingRunStatus.Paused, run.Status));
    }

    /// <summary>Verifies replacing an active refresh cancels both its job and durable stage state.</summary>
    [Fact]
    public async Task NewRefreshLeavesNoSupersededRunningStageState()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source();
        await QueueAsync(store, source, [fixture.Observation("active.txt")]);
        Assert.NotNull(await store.ClaimNextAsync(Epoch.AddMinutes(1)));

        _ = await store.BeginRunAsync(source.Id, Epoch.AddMinutes(2));

        Assert.Equal(
            "0",
            ReadScalar(
                fixture.DatabasePath,
                $"SELECT COUNT(*) FROM index_stage_states WHERE status = {(int)IndexingStageStatus.Running};"));
        var resumable = Assert.Single(await store.GetResumableRunsAsync());
        Assert.Equal(IndexingRunStatus.Running, resumable.Status);
    }

    /// <summary>Verifies cancellation leaves no stale running claim.</summary>
    [Fact]
    public async Task SafeCancellationLeavesNoRunningJobs()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("cancel.txt")]);
        Assert.NotNull(await store.ClaimNextAsync(Epoch.AddMinutes(1)));

        await store.SetActiveRunsStatusAsync(IndexingRunStatus.Cancelled, "test", Epoch.AddMinutes(2));

        Assert.Null(await store.ClaimNextAsync(Epoch.AddMinutes(3)));
        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(3));
        Assert.Equal(IndexingRunStatus.Cancelled, progress.Status);
        Assert.Equal(progress.TotalDiscovered, progress.Processed);
        Assert.Equal(
            "0",
            ReadScalar(
                fixture.DatabasePath,
                $"SELECT COUNT(*) FROM index_stage_states WHERE status = {(int)IndexingStageStatus.Running};"));
    }

    /// <summary>Verifies process restart recovers a running stage.</summary>
    [Fact]
    public async Task RestartRecoveryRequeuesStaleRunningStage()
    {
        using var fixture = new IndexFixture();
        await using (var first = await fixture.CreateInitializedStoreAsync())
        {
            await QueueAsync(first, fixture.Source(), [fixture.Observation("restart.txt")]);
            Assert.NotNull(await first.ClaimNextAsync(Epoch.AddMinutes(1)));
        }

        await using var recovered = fixture.CreateStore();
        await recovered.InitializeAsync();
        Assert.Equal(1, await recovered.RecoverInterruptedWorkAsync(Epoch.AddMinutes(2)));
        var claim = Assert.IsType<IndexingWorkItem>(await recovered.ClaimNextAsync(Epoch.AddMinutes(3)));

        Assert.Equal(IndexingStage.FileDiscovered, claim.Stage);
        Assert.Equal(2, claim.Attempt);
    }

    /// <summary>Verifies retryable work stops at the configured attempt bound.</summary>
    [Fact]
    public async Task RetryableFailureStopsAtConfiguredMaximumAttempts()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(),
            [fixture.Observation("locked.txt")],
            maximumRetries: 1);
        var first = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(1)));
        await store.SaveStageOutputAsync(
            first,
            Retryable("locked"),
            IndexingStage.MetadataIndexed,
            Epoch.AddMinutes(1),
            TimeSpan.Zero,
            Epoch.AddMinutes(2));
        Assert.Equal(1, await store.ResumeEligibleWaitingRunsAsync(Epoch.AddMinutes(3)));
        var second = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(3)));
        await store.SaveStageOutputAsync(
            second,
            Retryable("locked"),
            IndexingStage.MetadataIndexed,
            Epoch.AddMinutes(3),
            TimeSpan.Zero,
            Epoch.AddMinutes(4));

        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(5));
        Assert.Equal(1, progress.Failed);
        Assert.Equal(0, progress.RetryScheduled);
        Assert.Null(await store.ClaimNextAsync(Epoch.AddMinutes(5)));
    }

    /// <summary>Verifies dependency work becomes eligible at its retry time.</summary>
    [Fact]
    public async Task WaitingDependencyBecomesEligibleAtPersistedRetryTime()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("ocr.pdf")]);
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(1)));
        await store.SaveStageOutputAsync(
            claim,
            new IndexingStageOutput
            {
                Status = IndexingStageStatus.WaitingForDependency,
                WaitingDependency = "OCR",
                FailureCategory = IndexingFailureCategory.DependencyUnavailable,
                ErrorCode = "ocr-unavailable",
                IsRetryable = true,
            },
            IndexingStage.MetadataIndexed,
            Epoch.AddMinutes(1),
            TimeSpan.Zero,
            Epoch.AddMinutes(5));

        Assert.Null(await store.ClaimNextAsync(Epoch.AddMinutes(4)));
        var waiting = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(4));
        Assert.Equal(IndexingRunStatus.Waiting, waiting.Status);
        Assert.Equal(0, await store.ResumeEligibleWaitingRunsAsync(Epoch.AddMinutes(4)));
        Assert.Equal(1, await store.ResumeEligibleWaitingRunsAsync(Epoch.AddMinutes(6)));
        var retry = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(6)));
        Assert.Equal(2, retry.Attempt);
    }

    /// <summary>Verifies permanent failures are counted and privacy-safe.</summary>
    [Theory]
    [InlineData(IndexingFailureCategory.PermissionDenied, "access-denied")]
    [InlineData(IndexingFailureCategory.NotFound, "file-not-found")]
    [InlineData(IndexingFailureCategory.Permanent, "unsupported-input")]
    public async Task PermanentFailuresAreCountedAndReviewable(
        IndexingFailureCategory category,
        string code)
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation($"{code}.txt")]);
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddMinutes(1)));
        await store.SaveStageOutputAsync(
            claim,
            new IndexingStageOutput
            {
                Status = IndexingStageStatus.Failed,
                FailureCategory = category,
                ErrorCode = code,
            },
            null,
            Epoch.AddMinutes(1),
            TimeSpan.FromMilliseconds(5),
            null);

        var failure = Assert.Single(await store.GetFailuresAsync(10));
        Assert.Equal(category, failure.Category);
        Assert.Equal(Path.GetFileName(claim.FullPath), failure.FileName);
        Assert.DoesNotContain(fixture.Root, failure.FileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies progress monotonicity and terminal counts.</summary>
    [Fact]
    public async Task ProgressIsMonotonicAndCountsEveryTerminalOutcome()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(),
            [fixture.Observation("one.txt"), fixture.Observation("two.txt", stableIdentity: "two")]);
        var percentages = new List<double>();
        while (await store.ClaimNextAsync(Epoch.AddMinutes(1)) is { } claim)
        {
            await store.SaveStageOutputAsync(
                claim,
                new IndexingStageOutput { Status = IndexingStageStatus.Skipped, StopsFile = true },
                null,
                Epoch.AddMinutes(2),
                TimeSpan.Zero,
                null);
            percentages.Add((await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(2))).OverallPercentage);
        }

        Assert.Equal([50d, 100d], percentages);
        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddMinutes(2));
        Assert.Equal(2, progress.Skipped);
        Assert.Equal(0, progress.Failed);
        Assert.Equal(0, progress.Waiting);
    }

    /// <summary>Verifies estimated time is hidden until enough samples exist.</summary>
    [Fact]
    public async Task EstimateAppearsOnlyAfterMeaningfulSample()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var files = Enumerable.Range(0, 10).Select(index => fixture.Observation($"{index}.txt", $"id-{index}")).ToArray();
        await QueueAsync(store, fixture.Source(), files);
        for (var index = 0; index < 5; index++)
        {
            var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddSeconds(index + 1)));
            await store.SaveStageOutputAsync(
                claim,
                new IndexingStageOutput { Status = IndexingStageStatus.Skipped, StopsFile = true },
                null,
                Epoch.AddSeconds(index + 1),
                TimeSpan.Zero,
                null);
        }

        var early = await store.GetProgressAsync(1024 * 1024, Epoch.AddSeconds(1));
        var sampled = await store.GetProgressAsync(1024 * 1024, Epoch.AddSeconds(10));

        Assert.Null(early.EstimatedRemaining);
        Assert.NotNull(sampled.EstimatedRemaining);
    }

    /// <summary>Verifies partial metadata remains searchable.</summary>
    [Fact]
    public async Task PartialIndexDocumentsRemainAvailableToSearch()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("partial.txt")]);

        var document = Assert.Single(await store.GetSearchDocumentsAsync(10));
        var coverage = await store.GetSearchCoverageAsync();

        Assert.Equal("partial.txt", document.FileName);
        Assert.False(document.IsFullyIndexed);
        Assert.True(coverage.IsIncomplete);
        Assert.Equal(1, coverage.FilenameAndMetadataCount);
    }

    /// <summary>Verifies provider-neutral storage accounting.</summary>
    [Fact]
    public async Task StorageBreakdownReportsBoundedCategoriesAndPhysicalTotal()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("storage.txt")]);
        await CompleteStandardRunAsync(store, "hash-a");

        var storage = await store.GetStorageBreakdownAsync(16 * 1024 * 1024);

        Assert.True(storage.MetadataBytes > 0);
        Assert.True(storage.ExtractedTextBytes > 0);
        Assert.True(storage.SemanticDataBytes > 0);
        Assert.True(storage.DatabaseBytes > 0);
        Assert.Equal(16 * 1024 * 1024, storage.MaximumBytes);
    }

    /// <summary>Verifies near-quota cleanup prunes only explicit rebuildable chunk data before blocking work.</summary>
    [Fact]
    public async Task QuotaCleanupPrunesRebuildableChunksAndReturnsWithinLimit()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("quota.txt")]);
        await CompleteStandardRunAsync(store, "quota-hash");
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE index_chunks SET chunk_text = zeroblob(18 * 1024 * 1024) WHERE content_hash = 'quota-hash';";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        SqliteConnection.ClearAllPools();
        var result = await store.MaintainAsync(
            new DeepIndexingSettings { MaximumIndexSizeMiB = 16 },
            Epoch.AddDays(1));

        Assert.Contains(result.Actions, action => action.Code == "quota-pruned-rebuildable-chunks");
        Assert.True(result.IsWithinQuota);
        Assert.True(result.Storage.DatabaseBytes <= result.Storage.MaximumBytes);
        Assert.Single(await store.GetSearchDocumentsAsync(10));
    }

    /// <summary>Verifies concurrent callers retain a consistent database.</summary>
    [Fact]
    public async Task ConcurrentReadersAndWritersRemainConsistent()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var tasks = Enumerable.Range(0, 24).Select(async index =>
        {
            if (index % 3 == 0)
            {
                await store.UpsertSourceAsync(fixture.Source() with
                {
                    Id = $"source-{index}",
                    RootPath = Path.Combine(fixture.Root, $"source-{index}"),
                    DisplayName = $"Source {index}",
                });
            }
            else if (index % 3 == 1)
            {
                _ = await store.GetSourcesAsync();
            }
            else
            {
                _ = await store.GetSearchCoverageAsync();
            }
        });

        await Task.WhenAll(tasks);

        Assert.Equal(8, (await store.GetSourcesAsync()).Count);
        Assert.Equal("ok", ReadScalar(fixture.DatabasePath, "PRAGMA quick_check;"));
    }

    /// <summary>Verifies case-sensitive and insensitive path policies.</summary>
    [Fact]
    public async Task CaseInsensitivePathsCoalesceWhileCaseSensitivePathsRemainDistinct()
    {
        using var insensitiveFixture = new IndexFixture();
        await using (var insensitive = await insensitiveFixture.CreateInitializedStoreAsync(new WindowsPathSemantics()))
        {
            var source = insensitiveFixture.Source();
            await QueueAsync(
                insensitive,
                source,
                [
                    insensitiveFixture.Observation("Case.txt", stableIdentity: null),
                    insensitiveFixture.Observation("case.txt", stableIdentity: null),
                ]);
            Assert.Single(await insensitive.GetSearchDocumentsAsync(10));
        }

        using var sensitiveFixture = new IndexFixture();
        await using var sensitive = await sensitiveFixture.CreateInitializedStoreAsync(new LinuxPathSemantics());
        await QueueAsync(
            sensitive,
            sensitiveFixture.Source(),
            [
                sensitiveFixture.Observation("Case.txt", stableIdentity: null),
                sensitiveFixture.Observation("case.txt", stableIdentity: null),
            ]);
        Assert.Equal(2, (await sensitive.GetSearchDocumentsAsync(10)).Count);
    }

    /// <summary>Verifies unusual valid filenames persist exactly.</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("résumé 2026 #final.txt")]
    [InlineData("trailing.period.valid-on-linux.")]
    public async Task UnusualValidNamesPersistWithoutNormalizationLoss(string fileName)
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync(new LinuxPathSemantics());
        await QueueAsync(store, fixture.Source(), [fixture.Observation(fileName)]);

        Assert.Equal(fileName, Assert.Single(await store.GetSearchDocumentsAsync(10)).FileName);
    }

    /// <summary>Verifies long paths do not use fixed Windows buffers.</summary>
    [Fact]
    public async Task LongRelativePathPersistsWithoutFixedWindowsBuffer()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync(new LinuxPathSemantics());
        var relative = string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat(new string('a', 40), 8)) + ".txt";
        await QueueAsync(store, fixture.Source(), [fixture.Observation(relative)]);

        Assert.Equal(relative, Assert.Single(await store.GetSearchDocumentsAsync(10)).FullPath[(fixture.Root.Length + 1)..]);
    }

    /// <summary>Verifies rebuild preserves sources while clearing derived values.</summary>
    [Fact]
    public async Task RebuildPreservesSourcesButClearsDerivedDocuments()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(IndexingLevel.Standard), [fixture.Observation("rebuild.txt")]);
        await CompleteStandardRunAsync(store, "hash-a");

        await store.RebuildAsync(Epoch.AddDays(1));

        Assert.Single(await store.GetSourcesAsync());
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10));
        Assert.Null(document.ExtractedText);
        Assert.False(document.IsFullyIndexed);
        Assert.Equal(
            "4",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM pragma_table_info('index_relationship_features') WHERE name IN ('media_transcript_fingerprint', 'media_ocr_fingerprint', 'media_device_key', 'capture_date_bucket');"));
        Assert.Equal(
            "1",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'index_relationship_feature_terms';"));
    }

    /// <summary>Verifies the v1.7 schema migrates transactionally to privacy-aware schema v2.</summary>
    [Fact]
    public async Task VersionOneMigratesToVersionTwoWithRecoveryBackup()
    {
        using var fixture = new IndexFixture();
        await using (var versionTwo = await fixture.CreateInitializedStoreAsync())
        {
            Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        }

        CreateDatabase(
            fixture.DatabasePath,
            "DROP TABLE index_privacy_rules; PRAGMA user_version = 1; UPDATE index_meta SET value = '1' WHERE key = 'schema_version';");
        await using var migrated = fixture.CreateStore();

        await migrated.InitializeAsync();

        Assert.Equal(DeepIndexingVersion.SchemaVersion, ReadUserVersion(fixture.DatabasePath));
        Assert.Equal(
            "1",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'index_privacy_rules';"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));
    }

    /// <summary>Verifies privacy inspection reports categories and counts, never raw derived text or vectors.</summary>
    [Fact]
    public async Task InspectFileReportsStoredCategoriesWithoutRawContent()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Standard),
            [fixture.Observation("private.txt")]);
        await CompleteStandardRunAsync(store, "private-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        var privacy = Assert.IsType<IndexPrivacyItem>(await store.InspectFileAsync(document.FileId));

        Assert.Equal("private.txt", privacy.FileName);
        Assert.True(privacy.ExtractedTextCharacters > 0);
        Assert.True(privacy.HasSummary);
        Assert.True(privacy.KeywordCount > 0);
        Assert.True(privacy.HasSemanticData);
        Assert.True(privacy.HasContentIntelligence);
        Assert.Equal(1, privacy.ContentTopicCount);
        Assert.True(privacy.ChunkCount > 0);
        Assert.DoesNotContain("bounded document text", privacy.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("0.5", privacy.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies per-file metadata-only policy is durable and controls the next claim.</summary>
    [Fact]
    public async Task MetadataOnlyPolicyPersistsAndPreventsDeepStages()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Deep);
        var observation = fixture.Observation("policy.pdf");
        await QueueAsync(store, source, [observation]);
        await CompleteStandardRunAsync(store, "policy-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        var result = await store.SetFilePolicyAsync(
            document.FileId,
            new IndexPrivacyPolicyChange(
                LevelOverride: IndexingLevel.Basic,
                SuppressOcr: true,
                SuppressSummary: true,
                SuppressSemantic: true),
            Epoch.AddDays(11));
        var metadataOnly = Assert.Single(
            await store.GetSearchDocumentsAsync(10),
            item => !item.IsExcluded);
        var privacy = Assert.IsType<IndexPrivacyItem>(
            await store.InspectFileAsync(document.FileId));
        var coverage = await store.GetSearchCoverageAsync();
        await QueueAsync(store, source, [observation], Epoch.AddDays(12));
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(13)));

        Assert.True(result.Applied);
        Assert.Null(metadataOnly.ExtractedText);
        Assert.Null(metadataOnly.OcrText);
        Assert.Null(metadataOnly.Summary);
        Assert.Empty(metadataOnly.Keywords);
        Assert.Null(metadataOnly.SemanticRepresentation);
        Assert.Empty(metadataOnly.SelectedChunks);
        Assert.Equal(0, privacy.ExtractedTextCharacters);
        Assert.Equal(0, coverage.ExtractedTextCount);
        Assert.Equal(0, coverage.OcrCount);
        Assert.Equal(0, coverage.SemanticCount);
        Assert.Equal(IndexingLevel.Basic, claim.Level);
        Assert.True(claim.SuppressOcr);
        Assert.True(claim.SuppressSummary);
        Assert.True(claim.SuppressSemantic);
        Assert.True(claim.ForceReprocess);
    }

    /// <summary>Verifies clearing selected generated data applies a durable anti-regeneration policy.</summary>
    [Fact]
    public async Task ClearSemanticDataRemovesOnlyDerivedIndexDataAndSuppressesRegeneration()
    {
        using var fixture = new IndexFixture();
        var sourceFile = Path.Combine(fixture.Root, "source.txt");
        var original = "original source remains unchanged"u8.ToArray();
        await File.WriteAllBytesAsync(sourceFile, original);
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Standard),
            [fixture.Observation("source.txt")]);
        await CompleteStandardRunAsync(store, "clear-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        var result = await store.ClearFileDataAsync(
            document.FileId,
            IndexedDataKind.SemanticData | IndexedDataKind.Chunks,
            Epoch.AddDays(11));
        var refreshed = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);
        var privacy = Assert.IsType<IndexPrivacyItem>(await store.InspectFileAsync(document.FileId));

        Assert.True(result.Applied);
        Assert.Null(refreshed.SemanticRepresentation);
        Assert.Empty(refreshed.SelectedChunks);
        Assert.True(privacy.SemanticSuppressed);
        Assert.Equal(original, await File.ReadAllBytesAsync(sourceFile));
    }

    /// <summary>Verifies clearing extracted text explicitly downgrades the file to metadata-only indexing.</summary>
    [Fact]
    public async Task ClearExtractedTextDowngradesToBasic()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Standard),
            [fixture.Observation("downgrade.txt")]);
        await CompleteStandardRunAsync(store, "downgrade-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        await store.ClearFileDataAsync(
            document.FileId,
            IndexedDataKind.ExtractedText,
            Epoch.AddDays(11));
        var privacy = Assert.IsType<IndexPrivacyItem>(await store.InspectFileAsync(document.FileId));

        Assert.Equal(IndexingLevel.Basic, privacy.IndexingLevel);
        Assert.Equal(0, privacy.ExtractedTextCharacters);
        Assert.True(privacy.OcrSuppressed);
        Assert.True(privacy.SummarySuppressed);
        Assert.True(privacy.SemanticSuppressed);
    }

    /// <summary>Verifies OCR clearing also removes every dependent generated Search signal.</summary>
    [Fact]
    public async Task ClearOcrDataClearsDependentGeneratedSignalsAndCoverage()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Deep),
            [fixture.Observation("ocr-private.txt")]);
        await CompleteDeepRunAsync(store, "ocr-private-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        await store.ClearFileDataAsync(
            document.FileId,
            IndexedDataKind.OcrText,
            Epoch.AddDays(11));
        var after = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);
        var privacy = Assert.IsType<IndexPrivacyItem>(await store.InspectFileAsync(document.FileId));
        var coverage = await store.GetSearchCoverageAsync();

        Assert.Null(after.OcrText);
        Assert.Null(after.Summary);
        Assert.Empty(after.Keywords);
        Assert.Null(after.SemanticRepresentation);
        Assert.Empty(after.SelectedChunks);
        Assert.True(after.IsFullyIndexed);
        Assert.True(privacy.OcrSuppressed);
        Assert.True(privacy.SummarySuppressed);
        Assert.True(privacy.SemanticSuppressed);
        Assert.Equal(0, coverage.OcrCount);
        Assert.Equal(0, coverage.SemanticCount);
    }

    /// <summary>Verifies forgetting a file retains only a path exclusion that suppresses legacy Search data.</summary>
    [Fact]
    public async Task ForgetFileRetainsSearchExclusionWithoutIndexedFileRecord()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(store, fixture.Source(), [fixture.Observation("forget.txt")]);
        await CompleteBasicRunAsync(store, "forget-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        var result = await store.ForgetFileAsync(document.FileId, Epoch.AddDays(11));
        var documents = await store.GetSearchDocumentsAsync(10);
        var exclusions = await store.GetExcludedSearchPathsAsync(10);
        var coverage = await store.GetSearchCoverageAsync();

        Assert.True(result.Applied);
        Assert.Null(await store.InspectFileAsync(document.FileId));
        Assert.Empty(documents);
        Assert.Contains(
            exclusions,
            path => Path.GetFileName(path).Equals("forget.txt", StringComparison.Ordinal));
        Assert.Equal(0, coverage.KnownFileCount);
        Assert.Equal(1, coverage.ExcludedSourceCount);
    }

    /// <summary>Verifies forgetting a watched source preserves its registration and ownership.</summary>
    [Fact]
    public async Task ForgetSourcePreservesWatchedFolderOwnership()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source() with { ManagedByWatchedFolders = true };
        await QueueAsync(
            store,
            source,
            [fixture.Observation("one.txt", "one"), fixture.Observation("two.txt", "two")]);
        await CompleteBasicRunAsync(store, "shared");

        var result = await store.ForgetSourceAsync(source.Id, Epoch.AddDays(11));
        var retainedSource = Assert.Single(await store.GetSourcesAsync());

        Assert.True(result.Applied);
        Assert.Equal(2, result.AffectedFileCount);
        Assert.True(retainedSource.ManagedByWatchedFolders);
        Assert.Equal(source.RootPath, retainedSource.RootPath);
        Assert.Empty(await store.GetSearchDocumentsAsync(10));
        Assert.Equal(2, (await store.GetExcludedSearchPathsAsync(10)).Count);
    }

    /// <summary>Verifies selective semantic repair invalidates only the semantic and later stages.</summary>
    [Fact]
    public async Task SemanticRepairResumesAtSelectedDurableStage()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Standard);
        var observation = fixture.Observation("repair.txt");
        await QueueAsync(store, source, [observation]);
        await CompleteStandardRunAsync(store, "repair-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        var prepared = await store.PrepareFileRepairAsync(
            document.FileId,
            IndexRepairKind.RegenerateSemanticData,
            Epoch.AddDays(11));
        await QueueAsync(store, source, [observation], Epoch.AddDays(12));
        var claim = Assert.IsType<IndexingWorkItem>(await store.ClaimNextAsync(Epoch.AddDays(13)));

        Assert.True(prepared.Applied);
        Assert.Equal(IndexingStage.SemanticRepresentationGenerated, claim.Stage);
        Assert.Equal("bounded document text", claim.ExtractedText);
        Assert.True(claim.ForceReprocess);
    }

    /// <summary>Verifies each explicit repair resumes at its documented durable boundary.</summary>
    [Theory]
    [InlineData(IndexRepairKind.Rebuild, IndexingStage.FileDiscovered)]
    [InlineData(IndexRepairKind.RefreshMetadata, IndexingStage.MetadataIndexed)]
    [InlineData(IndexRepairKind.RefreshText, IndexingStage.TextExtracted)]
    [InlineData(IndexRepairKind.RefreshOcr, IndexingStage.OcrProcessed)]
    [InlineData(IndexRepairKind.RegenerateSummaryAndKeywords, IndexingStage.SummaryKeywordsGenerated)]
    [InlineData(IndexRepairKind.RegenerateSemanticData, IndexingStage.SemanticRepresentationGenerated)]
    public async Task SelectiveRepairKindsResumeAtRequestedStage(
        IndexRepairKind repair,
        IndexingStage expectedStage)
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        var source = fixture.Source(IndexingLevel.Standard);
        var observation = fixture.Observation("selective-repair.txt");
        await QueueAsync(store, source, [observation]);
        await CompleteStandardRunAsync(store, "selective-repair-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        var prepared = await store.PrepareFileRepairAsync(
            document.FileId,
            repair,
            Epoch.AddDays(11));
        await QueueAsync(store, source, [observation], Epoch.AddDays(12));
        var claim = Assert.IsType<IndexingWorkItem>(
            await store.ClaimNextAsync(Epoch.AddDays(13)));

        Assert.True(prepared.Applied);
        Assert.Equal(expectedStage, claim.Stage);
        Assert.True(claim.ForceReprocess);
    }

    /// <summary>Verifies verification is a no-op for a consistent indexed file.</summary>
    [Fact]
    public async Task VerifyConsistentFileDoesNotQueueRepair()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Standard),
            [fixture.Observation("consistent.txt")]);
        await CompleteStandardRunAsync(store, "consistent-hash");
        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        var result = await store.PrepareFileRepairAsync(
            document.FileId,
            IndexRepairKind.Verify,
            Epoch.AddDays(11));

        Assert.False(result.Applied);
        Assert.Equal(0, result.AffectedFileCount);
    }

    /// <summary>Verifies hostile identifiers remain parameters and cannot alter the SQLite schema.</summary>
    [Fact]
    public async Task PrivacyIdentifiersAreParameterisedAgainstSqlInjection()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        const string hostile = "'; DROP TABLE index_files; --";

        var inspected = await store.InspectFileAsync(hostile);
        var forgotten = await store.ForgetFileAsync(hostile, Epoch);

        Assert.Null(inspected);
        Assert.False(forgotten.Applied);
        Assert.Equal(
            "1",
            ReadScalar(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'index_files';"));
    }

    /// <summary>Verifies corrupt generated ranking fields fall back to valid filename and metadata records.</summary>
    [Fact]
    public async Task CorruptGeneratedSearchDataFallsBackToMetadata()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Standard),
            [fixture.Observation("corrupt.txt")]);
        await CompleteStandardRunAsync(store, "corrupt-hash");
        CreateDatabase(
            fixture.DatabasePath,
            "UPDATE index_content SET keywords_json = '{bad', semantic_json = '[\"not-a-number\"]' WHERE content_hash = 'corrupt-hash';");

        var document = Assert.Single(await store.GetSearchDocumentsAsync(10), item => !item.IsExcluded);

        Assert.Equal("corrupt.txt", document.FileName);
        Assert.Empty(document.Keywords);
        Assert.Null(document.SemanticRepresentation);
        Assert.True(document.HasIndexingFailure);
    }

    /// <summary>Verifies actual source and privacy lookup patterns use declared indexes.</summary>
    [Fact]
    public async Task SearchAndPrivacyQueryPlansUseDeclaredIndexes()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();

        var sourcePlan = ReadQueryPlan(
            fixture.DatabasePath,
            "EXPLAIN QUERY PLAN SELECT * FROM index_files WHERE source_id = 'source' AND relative_path_key = 'x';");
        var privacyPlan = ReadQueryPlan(
            fixture.DatabasePath,
            "EXPLAIN QUERY PLAN SELECT * FROM index_privacy_rules WHERE source_id = 'source' AND relative_path_key = 'x';");

        Assert.Contains("INDEX", sourcePlan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INDEX", privacyPlan, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies BaseFirst scheduling publishes the cheapest stage across files before deeper work.</summary>
    [Fact]
    public async Task ClaimNext_BaseFirst_BreadthFirstAcrossKnownFiles()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Deep),
            [fixture.Observation("first.txt", "first"), fixture.Observation("second.txt", "second")]);

        var first = Assert.IsType<IndexingWorkItem>(
            await store.ClaimNextAsync(Epoch.AddDays(10), InitialScanDepth.BaseFirst));
        Assert.Equal(IndexingStage.FileDiscovered, first.Stage);
        await SaveCompleteAsync(store, first, IndexingStage.MetadataIndexed);
        var second = Assert.IsType<IndexingWorkItem>(
            await store.ClaimNextAsync(Epoch.AddDays(10), InitialScanDepth.BaseFirst));

        Assert.Equal(IndexingStage.FileDiscovered, second.Stage);
        Assert.NotEqual(first.FileId, second.FileId);
    }

    /// <summary>Verifies DeepInitialAnalysis advances one file before starting another file.</summary>
    [Fact]
    public async Task ClaimNext_DeepInitialAnalysis_DepthFirstPerFile()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Deep),
            [fixture.Observation("first.txt", "first"), fixture.Observation("second.txt", "second")]);

        var first = Assert.IsType<IndexingWorkItem>(
            await store.ClaimNextAsync(Epoch.AddDays(10), InitialScanDepth.DeepInitialAnalysis));
        await SaveCompleteAsync(store, first, IndexingStage.MetadataIndexed);
        var second = Assert.IsType<IndexingWorkItem>(
            await store.ClaimNextAsync(Epoch.AddDays(10), InitialScanDepth.DeepInitialAnalysis));

        Assert.Equal(first.FileId, second.FileId);
        Assert.Equal(IndexingStage.MetadataIndexed, second.Stage);
    }

    /// <summary>Verifies names and metadata are searchable before any expensive stage is claimed.</summary>
    [Fact]
    public async Task DiscoveryCommit_PublishesBaseSearchCoverageBeforeDeepAnalysis()
    {
        using var fixture = new IndexFixture();
        await using var store = await fixture.CreateInitializedStoreAsync();
        await QueueAsync(
            store,
            fixture.Source(IndexingLevel.Deep),
            [fixture.Observation("searchable-before-ocr.txt")]);

        var documents = await store.GetSearchDocumentsAsync(10);
        var progress = await store.GetProgressAsync(1024 * 1024, Epoch.AddDays(10));

        Assert.Equal("searchable-before-ocr.txt", Assert.Single(documents).FileName);
        Assert.Equal(1, progress.Coverage.FilenameAndMetadataCount);
        Assert.Equal(0, progress.Coverage.FullyIndexedCount);
        Assert.True(progress.DiscoveryComplete);
        Assert.Equal(IndexingProgressPhase.DeeperAnalysis, progress.Phase);
    }

    private static async Task<string> QueueAsync(
        SqliteDeepIndexStore store,
        IndexingSource source,
        IReadOnlyList<IndexingFileObservation> files,
        DateTimeOffset? at = null,
        string processor = "processor",
        int maximumRetries = 3)
    {
        await store.UpsertSourceAsync(source);
        var runId = await store.BeginRunAsync(source.Id, at ?? Epoch);
        await store.EnqueueDiscoveredFilesAsync(runId, files, processor, maximumRetries);
        await store.CompleteDiscoveryAsync(runId, new HashSet<string>(), at ?? Epoch);
        return runId;
    }

    private static SmartTagClassificationResult Classification(params string[] tagIds) => new(
        SmartTagClassificationState.Classified,
        Array.AsReadOnly(tagIds.Select(tagId => new SmartTagCandidate
        {
            TagId = tagId,
            Type = tagId.StartsWith("theme.", StringComparison.Ordinal)
                ? SmartTagType.Theme
                : SmartTagType.DocumentType,
            Confidence = ContentIntelligenceConfidence.Strong,
            EvidenceScore = 6,
            Origin = SmartTagOrigin.DeterministicClassifier,
            Classifier = "test-classifier",
            ClassifierVersion = "1.0-taxonomy-1.0",
            TaxonomyVersion = "1.0",
            InputFingerprint = "input-test",
            Evidence = [new SmartTagEvidence(ContentEvidenceSourceKind.ExtractedText, "native:test", "Native content matched a test fixture.")],
        }).ToArray()),
        "Classified by deterministic test evidence.")
    {
        Classifier = "test-classifier",
        ClassifierVersion = "1.0-taxonomy-1.0",
        TaxonomyVersion = "1.0",
        InputFingerprint = "input-test",
    };

    private static async Task CompleteBasicRunAsync(SqliteDeepIndexStore store, string hash)
    {
        while (await store.ClaimNextAsync(Epoch.AddDays(10)) is { } claim)
        {
            var next = claim.Stage switch
            {
                IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
                IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
                IndexingStage.ContentFingerprinted => IndexingStage.SearchIndexUpdated,
                IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
                IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
                IndexingStage.FileFullyIndexed => (IndexingStage?)null,
                _ => throw new InvalidOperationException($"Unexpected Basic stage {claim.Stage}."),
            };
            await store.SaveStageOutputAsync(
                claim,
                new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    ContentHash = claim.Stage == IndexingStage.ContentFingerprinted ? hash : null,
                },
                next,
                Epoch.AddDays(10),
                TimeSpan.FromMilliseconds(1),
                null);
        }
    }

    private static async Task CompleteStandardRunAsync(
        SqliteDeepIndexStore store,
        string hash,
        SmartTagClassificationResult? classification = null)
    {
        while (await store.ClaimNextAsync(Epoch.AddDays(10)) is { } claim)
        {
            var next = claim.Stage switch
            {
                IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
                IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
                IndexingStage.ContentFingerprinted => IndexingStage.TextExtracted,
                IndexingStage.TextExtracted => IndexingStage.SummaryKeywordsGenerated,
                IndexingStage.SummaryKeywordsGenerated => IndexingStage.SemanticRepresentationGenerated,
                IndexingStage.SemanticRepresentationGenerated => IndexingStage.SmartTagsClassified,
                IndexingStage.SmartTagsClassified => IndexingStage.SearchIndexUpdated,
                IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
                IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
                IndexingStage.FileFullyIndexed => (IndexingStage?)null,
                _ => throw new InvalidOperationException($"Unexpected Standard stage {claim.Stage}."),
            };
            var output = claim.Stage switch
            {
                IndexingStage.ContentFingerprinted => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    ContentHash = hash,
                },
                IndexingStage.TextExtracted => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    ExtractedText = "bounded document text",
                },
                IndexingStage.SummaryKeywordsGenerated => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    Summary = "bounded summary",
                    Keywords = ["bounded", "document"],
                    ContentIntelligence = new IndexedContentIntelligence
                    {
                        Topics =
                        [
                            new ContentConcept
                            {
                                Kind = ContentConceptKind.Topic,
                                DisplayName = "Raspberry Pi monitoring",
                                NormalizedValue = "raspberry pi monitoring",
                                Confidence = ContentIntelligenceConfidence.Strong,
                                Provider = "test-deterministic",
                                ProviderVersion = "1",
                                Origin = ContentIntelligenceOrigin.Deterministic,
                            },
                        ],
                        Entities = [],
                        Keywords = ["raspberry pi monitoring"],
                        Provider = "test-deterministic",
                        ProviderVersion = "1",
                        ProcessingFingerprint = "content-intelligence-test",
                    },
                    SelectedChunks = ["bounded document text"],
                },
                IndexingStage.SemanticRepresentationGenerated => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    SemanticRepresentation = [0.5f, 0.5f],
                },
                IndexingStage.SmartTagsClassified when classification is not null => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    SmartTagClassification = classification,
                },
                _ => new IndexingStageOutput { Status = IndexingStageStatus.Complete },
            };
            await store.SaveStageOutputAsync(
                claim,
                output,
                next,
                Epoch.AddDays(10),
                TimeSpan.FromMilliseconds(1),
                null);
        }
    }

    private static async Task CompleteDeepRunAsync(SqliteDeepIndexStore store, string hash)
    {
        while (await store.ClaimNextAsync(Epoch.AddDays(10)) is { } claim)
        {
            var next = claim.Stage switch
            {
                IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
                IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
                IndexingStage.ContentFingerprinted => IndexingStage.TextExtracted,
                IndexingStage.TextExtracted => IndexingStage.OcrProcessed,
                IndexingStage.OcrProcessed => IndexingStage.SummaryKeywordsGenerated,
                IndexingStage.SummaryKeywordsGenerated => IndexingStage.SemanticRepresentationGenerated,
                IndexingStage.SemanticRepresentationGenerated => IndexingStage.SmartTagsClassified,
                IndexingStage.SmartTagsClassified => IndexingStage.SearchIndexUpdated,
                IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
                IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
                IndexingStage.FileFullyIndexed => (IndexingStage?)null,
                _ => throw new InvalidOperationException($"Unexpected Deep stage {claim.Stage}."),
            };
            var output = claim.Stage switch
            {
                IndexingStage.ContentFingerprinted => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    ContentHash = hash,
                },
                IndexingStage.TextExtracted => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    ExtractedText = "bounded document text",
                },
                IndexingStage.OcrProcessed => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    OcrText = "private OCR text",
                },
                IndexingStage.SummaryKeywordsGenerated => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    Summary = "bounded summary",
                    Keywords = ["bounded", "document"],
                    SelectedChunks = ["bounded document text"],
                },
                IndexingStage.SemanticRepresentationGenerated => new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Complete,
                    SemanticRepresentation = [0.5f, 0.5f],
                },
                _ => new IndexingStageOutput { Status = IndexingStageStatus.Complete },
            };
            await store.SaveStageOutputAsync(
                claim,
                output,
                next,
                Epoch.AddDays(10),
                TimeSpan.FromMilliseconds(1),
                null);
        }
    }

    private static Task SaveCompleteAsync(
        SqliteDeepIndexStore store,
        IndexingWorkItem claim,
        IndexingStage next) =>
        store.SaveStageOutputAsync(
            claim,
            new IndexingStageOutput { Status = IndexingStageStatus.Complete },
            next,
            Epoch.AddDays(10),
            TimeSpan.Zero,
            null);

    private static IndexingStageOutput Retryable(string code) => new()
    {
        Status = IndexingStageStatus.Failed,
        FailureCategory = IndexingFailureCategory.TransientIo,
        ErrorCode = code,
        IsRetryable = true,
    };

    private static void CreateDatabase(string path, string sql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ReadUserVersion(string path) =>
        Convert.ToInt32(ReadScalar(path, "PRAGMA user_version;"), System.Globalization.CultureInfo.InvariantCulture);

    private static string ReadScalar(string path, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static string ReadQueryPlan(string path, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var details = new List<string>();
        while (reader.Read())
        {
            details.Add(reader.GetString(3));
        }

        return string.Join(Environment.NewLine, details);
    }

    private sealed class IndexFixture : IDisposable
    {
        public IndexFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "OmniSorSe-index-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "deep-index.db");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public SqliteDeepIndexStore CreateStore(IPathSemantics? semantics = null) =>
            new(DatabasePath, semantics ?? PlatformServices.CurrentPathSemantics);

        public async Task<SqliteDeepIndexStore> CreateInitializedStoreAsync(IPathSemantics? semantics = null)
        {
            var store = CreateStore(semantics);
            await store.InitializeAsync();
            return store;
        }

        public IndexingSource Source(IndexingLevel level = IndexingLevel.Basic) =>
            new("source", Root, "Test source", level, true, true, 0, []);

        public IndexingFileObservation Observation(string relativePath, string? stableIdentity = "stable") =>
            new(
                Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                relativePath.Replace('/', Path.DirectorySeparatorChar),
                stableIdentity,
                stableIdentity is null ? null : "volume",
                10,
                Epoch,
                Epoch,
                FileAttributes.Normal,
                "metadata-a");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
