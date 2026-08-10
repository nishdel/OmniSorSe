using System.Reflection;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Indexing.Sqlite;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Validates v1.9 relationship persistence, migration, privacy, recovery, and bounded graph behavior.</summary>
public sealed class SqliteRelationshipStoreTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies the application service incrementally relates later files to previously indexed features.</summary>
    [Fact]
    public async Task RelationshipService_AnalyzesIncrementallyWithoutAi()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var files = await store.GetRelationshipFilesAsync(10);
        var service = new RelationshipService(new Configuration(), store, new DeterministicRelationshipEngine(new FixedTimeProvider(Epoch)));

        var first = await service.AnalyzeFileAsync(files[0].FileId);
        var second = await service.AnalyzeFileAsync(files[1].FileId);

        Assert.Equal(0, first.CandidateCount);
        Assert.True(second.CandidateCount > 0);
        Assert.True(second.RelationshipCount > 0);
        Assert.NotEmpty(await service.GetRelatedFilesAsync(files[0].FileId));
    }

    /// <summary>Verifies the application service skips analysis when the user's global relationship setting is off.</summary>
    [Fact]
    public async Task RelationshipService_DisabledSettingSkipsWithoutPublishingOutput()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var file = Assert.Single((await store.GetRelationshipFilesAsync(10)).Take(1));
        var service = new RelationshipService(
            new Configuration(new DeepIndexingSettings { RelationshipAnalysisEnabled = false }),
            store,
            new DeterministicRelationshipEngine());

        var result = await service.AnalyzeFileAsync(file.FileId);

        Assert.True(result.Skipped);
        Assert.Empty(await store.GetRelatedFilesAsync(file.FileId, null, null, RelatedFileSort.Confidence, 10));
    }

    /// <summary>Verifies manual-control file lists avoid loading private or memory-heavy generated content.</summary>
    [Fact]
    public async Task RelationshipFileList_UsesLightweightProjection()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();

        var files = await store.GetRelationshipFilesAsync(10);

        Assert.NotEmpty(files);
        Assert.All(files, file =>
        {
            Assert.Null(file.ExtractedText);
            Assert.Null(file.OcrText);
            Assert.Null(file.Summary);
            Assert.Empty(file.Keywords);
            Assert.Null(file.SemanticRepresentation);
        });
        Assert.NotNull((await store.GetRelationshipFileAsync(files[0].FileId))?.ExtractedText);
    }

    /// <summary>Verifies automatic relationships, evidence, collections, timeline, diagnostics, and Search expansion survive storage.</summary>
    [Fact]
    public async Task SaveAnalysis_PersistsExplainableRelationshipAndCollection()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);

        await store.SaveRelationshipAnalysisAsync(batch, 100);

        var related = Assert.Single(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        var collection = Assert.Single(await store.GetCollectionsAsync(10));
        var details = Assert.IsType<SmartCollectionDetails>(await store.GetCollectionAsync(collection.Id, 100));
        var expansion = Assert.Single(await store.GetSearchExpansionsAsync([first.FileId], 10));
        var diagnostics = await store.GetRelationshipDiagnosticsAsync();
        var privacy = Assert.IsType<IndexPrivacyItem>(await store.InspectFileAsync(first.FileId));
        var storage = await store.GetStorageBreakdownAsync(1024 * 1024);

        Assert.Equal(second.FileId, related.FileId);
        Assert.Equal("Same invoice number", related.Relationship.Explanation);
        Assert.Equal(2, details.Members.Count);
        Assert.Single(details.Relationships);
        Assert.Equal(2, details.Timeline.Count);
        Assert.Equal(second.FileId, expansion.RelatedFileId);
        Assert.Equal("Mercedes Purchase", expansion.CollectionTitle);
        Assert.Equal(1, diagnostics.RelationshipCount);
        Assert.Equal(1, diagnostics.CollectionCount);
        Assert.Equal(2, diagnostics.LastCandidateCount);
        Assert.Equal(1, privacy.RelationshipCount);
        Assert.Equal(1, privacy.CollectionCount);
        Assert.True(storage.RelationshipDataBytes > 0);
    }

    /// <summary>Verifies a persistent never-relate correction prevents automatic recreation.</summary>
    [Fact]
    public async Task NeverRelateOverride_PreventsAutomaticRecreation()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        await store.SaveRelationshipAnalysisAsync(batch, 100);
        var relationship = Assert.IsType<FileRelationship>(await store.GetRelationshipAsync(batch.Proposals[0].Relationship.Id));

        await store.SetRelationshipDecisionAsync(relationship.Id, RelationshipDecision.NeverRelate, Epoch.AddHours(1));
        await store.SaveRelationshipAnalysisAsync(batch with { CompletedAtUtc = Epoch.AddHours(2) }, 100);

        Assert.Empty(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.Empty(await store.GetSearchExpansionsAsync([first.FileId], 10));
        Assert.Equal(1, (await store.GetRelationshipDiagnosticsAsync()).ManualOverrideCount);
    }

    /// <summary>Verifies explicit manual links remain during targeted automatic rebuild preparation.</summary>
    [Fact]
    public async Task TargetedRebuild_PreservesManualRelationship()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.LinkFilesAsync(
            first.FileId,
            second.FileId,
            RelationshipType.Manual,
            null,
            true,
            Epoch);

        var prepared = await store.PrepareRelationshipRebuildAsync(first.FileId, Epoch.AddHours(1));

        Assert.True(prepared.Applied);
        var related = Assert.Single(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.True(related.Relationship.IsManual);
        Assert.Equal(RelationshipDecision.AlwaysRelate, related.Relationship.Decision);
    }

    /// <summary>Verifies a full derived-index rebuild removes automatic graph data while retaining explicit user links.</summary>
    [Fact]
    public async Task FullRebuild_ClearsAutomaticGraphAndPreservesManualRelationship()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.UpsertRelationshipFeaturesAsync(
            new RelationshipFeatureSet(first.FileId, "invoice", "records", null, null, null, null, null, ["invoice"], "1"),
            Epoch);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);
        await store.LinkFilesAsync(first.FileId, second.FileId, RelationshipType.Manual, null, true, Epoch);

        await store.RebuildAsync(Epoch.AddHours(1));

        var retained = Assert.Single(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.True(retained.Relationship.IsManual);
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM index_relationship_features;"));
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM smart_collections;"));
        var diagnostics = await store.GetRelationshipDiagnosticsAsync();
        Assert.Equal(0, diagnostics.LastCandidateCount);
        Assert.Equal(0, diagnostics.LastGeneratedRelationshipCount);
    }

    /// <summary>Verifies forgetting relationship data can suppress future analysis without deleting the indexed file.</summary>
    [Fact]
    public async Task ForgetFile_RetainsFileAndSuppressesFutureRelationshipAnalysis()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);

        var result = await store.ForgetFileRelationshipsAsync(first.FileId, true, Epoch.AddHours(1));
        var retained = Assert.IsType<RelationshipFileDocument>(await store.GetRelationshipFileAsync(first.FileId));

        Assert.True(result.Applied);
        Assert.True(retained.RelationshipAnalysisSuppressed);
        Assert.Empty(await store.GetRelatedFilesAsync(second.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.NotNull(await store.InspectFileAsync(first.FileId));
    }

    /// <summary>Verifies forgetting one watched source preserves source registration and ownership.</summary>
    [Fact]
    public async Task ForgetSource_PreservesWatchedSourceOwnership()
    {
        using var fixture = new Fixture(managedByWatchedFolders: true);
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);

        var result = await store.ForgetSourceRelationshipsAsync("source", true, Epoch.AddHours(1));
        var source = Assert.Single(await store.GetSourcesAsync());

        Assert.True(result.Applied);
        Assert.True(source.ManagedByWatchedFolders);
        Assert.True(Assert.IsType<RelationshipFileDocument>(await store.GetRelationshipFileAsync(first.FileId)).RelationshipAnalysisSuppressed);
        Assert.True(Assert.IsType<RelationshipFileDocument>(await store.GetRelationshipFileAsync(second.FileId)).RelationshipAnalysisSuppressed);
        Assert.Empty(await store.GetRelationshipFilesAsync(10));
    }

    /// <summary>Verifies manual collection splits persist and cannot be undone by the same automatic batch.</summary>
    [Fact]
    public async Task SplitCollectionMember_PersistsAgainstAutomaticRefresh()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        await store.SaveRelationshipAnalysisAsync(batch, 100);
        var collection = Assert.Single(await store.GetCollectionsAsync(10));

        await store.SplitCollectionMemberAsync(collection.Id, second.FileId, Epoch.AddHours(1));
        await store.SaveRelationshipAnalysisAsync(batch with { CompletedAtUtc = Epoch.AddHours(2) }, 100);
        var details = Assert.IsType<SmartCollectionDetails>(await store.GetCollectionAsync(collection.Id, 100));

        Assert.DoesNotContain(details.Members, member => member.FileId == second.FileId);
    }

    /// <summary>Verifies forgotten automatic collections retain a tombstone against immediate regeneration.</summary>
    [Fact]
    public async Task ForgetCollection_PreventsImmediateRegeneration()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        await store.SaveRelationshipAnalysisAsync(batch, 100);
        var collection = Assert.Single(await store.GetCollectionsAsync(10));

        await store.ForgetCollectionAsync(collection.Id, Epoch.AddHours(1));
        await store.SaveRelationshipAnalysisAsync(batch with { CompletedAtUtc = Epoch.AddHours(2) }, 100);

        Assert.Empty(await store.GetCollectionsAsync(10));
        Assert.Single(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
    }

    /// <summary>Verifies corrupt relationship enum data is hidden and removed by targeted repair.</summary>
    [Fact]
    public async Task Repair_RemovesCorruptRelationshipRows()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        await store.SaveRelationshipAnalysisAsync(batch, 100);
        fixture.Execute("UPDATE index_relationships SET confidence = 999;");

        Assert.Null(await store.GetRelationshipAsync(batch.Proposals[0].Relationship.Id));
        var repaired = await store.RepairRelationshipsAsync(Epoch.AddHours(1));

        Assert.True(repaired.Applied);
        Assert.True(repaired.AffectedRelationshipCount > 0);
        Assert.Equal(0, (await store.GetRelationshipDiagnosticsAsync()).RelationshipCount);
    }

    /// <summary>Verifies a privacy-only suppression hides both sides of contextual relationship reads.</summary>
    [Fact]
    public async Task RelationshipSuppression_HidesListsCollectionsAndSearchExpansion()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);

        await store.SetFilePolicyAsync(
            first.FileId,
            new IndexPrivacyPolicyChange(SuppressRelationships: true),
            Epoch.AddHours(1));

        Assert.DoesNotContain(await store.GetRelationshipFilesAsync(10), item => item.FileId == first.FileId);
        Assert.Empty(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.Empty(await store.GetRelatedFilesAsync(second.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.Empty(await store.GetSearchExpansionsAsync([first.FileId], 10));
        var collection = Assert.Single(await store.GetCollectionsAsync(10));
        var details = Assert.IsType<SmartCollectionDetails>(await store.GetCollectionAsync(collection.Id, 10));
        Assert.Equal(second.FileId, Assert.Single(details.Members).FileId);
        Assert.Empty(details.Relationships);
    }

    /// <summary>Verifies malformed collection timestamps are hidden and removed by derived-data repair.</summary>
    [Fact]
    public async Task Repair_RemovesCorruptCollectionRows()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);
        fixture.Execute("UPDATE smart_collections SET updated_utc_ticks = 9223372036854775807;");

        Assert.Empty(await store.GetCollectionsAsync(10));
        var repaired = await store.RepairRelationshipsAsync(Epoch.AddHours(1));

        Assert.True(repaired.AffectedRelationshipCount > 0);
        Assert.Equal(0, (await store.GetRelationshipDiagnosticsAsync()).CollectionCount);
    }

    /// <summary>Verifies corrupt collection-member timestamps are hidden and removed without breaking inspection.</summary>
    [Fact]
    public async Task Repair_RemovesCorruptCollectionMemberTimestamps()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);
        var collection = Assert.Single(await store.GetCollectionsAsync(10));
        fixture.Execute("UPDATE smart_collection_members SET added_utc_ticks = 9223372036854775807 WHERE file_id = '" + first.FileId + "';");

        var beforeRepair = Assert.IsType<SmartCollectionDetails>(await store.GetCollectionAsync(collection.Id, 10));
        Assert.DoesNotContain(beforeRepair.Members, member => member.FileId == first.FileId);
        var repaired = await store.RepairRelationshipsAsync(Epoch.AddHours(1));

        Assert.True(repaired.AffectedRelationshipCount > 0);
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM smart_collection_members WHERE added_utc_ticks > 3155378975999999999;"));
    }

    /// <summary>Verifies the provider boundary rejects malformed collection titles even without the application service.</summary>
    [Fact]
    public async Task RenameCollection_RejectsMalformedTitleAtProviderBoundary()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);
        var collection = Assert.Single(await store.GetCollectionsAsync(10));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RenameCollectionAsync(collection.Id, "invalid\0title", Epoch.AddHours(1)));

        Assert.Equal("Mercedes Purchase", Assert.Single(await store.GetCollectionsAsync(10)).Title);
    }

    /// <summary>Verifies oversized or malformed generated evidence cannot enter relationship storage.</summary>
    [Fact]
    public async Task SaveAnalysis_RejectsMalformedEvidenceAtProviderBoundary()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        var malformed = batch.Proposals[0].Relationship with
        {
            Evidence =
            [
                new RelationshipEvidence(
                    RelationshipEvidenceKind.Summary,
                    "summary",
                    new string('x', RelationshipLimits.MaximumEvidenceTextCharacters + 1)),
            ],
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveRelationshipAnalysisAsync(
                batch with { Proposals = [batch.Proposals[0] with { Relationship = malformed }] },
                100));

        Assert.Equal(0, (await store.GetRelationshipDiagnosticsAsync()).RelationshipCount);
    }

    /// <summary>Verifies migration from the exact v1.8 schema is transactional and retains existing indexed data.</summary>
    [Fact]
    public async Task Initialize_MigratesV18SchemaAndRetainsExistingRows()
    {
        using var fixture = new Fixture();
        fixture.CreateVersionTwoDatabase();
        fixture.Execute(
            """
            INSERT INTO index_sources(
                id, root_path, root_path_key, display_name, indexing_level, include_subfolders,
                enabled, priority, exclusions_json, managed_by_watched_folders, created_utc_ticks, updated_utc_ticks)
            VALUES('retained', '/synthetic', '/synthetic', 'Retained', 0, 1, 1, 0, '[]', 0, 1, 1);
            """);
        await using var store = fixture.CreateStore();

        await store.InitializeAsync();

        Assert.Equal(DeepIndexingVersion.SchemaVersion, fixture.ReadUserVersion());
        Assert.Equal("retained", Assert.Single(await store.GetSourcesAsync()).Id);
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM index_relationships;"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "backups"), "deep-index-*.db"));
    }

    /// <summary>Verifies feature-based candidate selection is bounded and excludes the target itself.</summary>
    [Fact]
    public async Task CandidateSelection_IsBoundedAndExcludesTarget()
    {
        using var fixture = new Fixture(fileCount: 12);
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var files = await store.GetRelationshipFilesAsync(20);
        foreach (var file in files)
        {
            await store.UpsertRelationshipFeaturesAsync(
                new RelationshipFeatureSet(file.FileId, "invoice 1234", "records", null, Epoch.Date.Ticks, null, null, null, ["invoice"], "1"),
                Epoch);
        }

        var target = new RelationshipFeatureSet(files[0].FileId, "invoice 1234", "records", null, Epoch.Date.Ticks, null, null, null, ["invoice"], "1");
        var candidates = await store.GetRelationshipCandidatesAsync(target, 3);

        Assert.Equal(3, candidates.Count);
        Assert.DoesNotContain(candidates, item => item.FileId == target.FileId);
        Assert.Equal(candidates.OrderBy(item => item.FileId, StringComparer.Ordinal).Select(item => item.FileId), candidates.Select(item => item.FileId));
    }

    private static async Task<(RelationshipFileDocument First, RelationshipFileDocument Second)> GetPairAsync(SqliteDeepIndexStore store)
    {
        var files = await store.GetRelationshipFilesAsync(20);
        return (files[0], files[1]);
    }

    private static RelationshipAnalysisBatch CreateBatch(RelationshipFileDocument first, RelationshipFileDocument second)
    {
        var (firstId, secondId) = string.CompareOrdinal(first.FileId, second.FileId) < 0
            ? (first.FileId, second.FileId)
            : (second.FileId, first.FileId);
        var relationship = new FileRelationship
        {
            Id = "rel:test",
            FirstFileId = firstId,
            SecondFileId = secondId,
            Type = RelationshipType.SamePurchase,
            Confidence = RelationshipConfidence.High,
            Evidence = [new RelationshipEvidence(RelationshipEvidenceKind.Filename, "invoice-1234", "Same invoice number")],
            Algorithm = "test",
            AlgorithmVersion = "1",
            CreatedAtUtc = Epoch,
            LastValidatedAtUtc = Epoch,
        };
        var suggestion = new SmartCollectionSuggestion(
            "purchase:invoice-1234",
            "Mercedes Purchase",
            "Synthetic purchase documents.",
            relationship.Explanation,
            RelationshipType.SamePurchase,
            RelationshipConfidence.High,
            firstId,
            secondId,
            relationship.Id);
        return new RelationshipAnalysisBatch(
            first.FileId,
            new RelationshipFeatureSet(first.FileId, "invoice 1234", "records", null, Epoch.Date.Ticks, null, null, null, ["invoice"], "1"),
            2,
            [new RelationshipProposal(relationship, suggestion)],
            "test",
            "1",
            Epoch,
            TimeSpan.FromMilliseconds(2));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _managedByWatchedFolders;
        private readonly int _fileCount;

        public Fixture(bool managedByWatchedFolders = false, int fileCount = 2)
        {
            _managedByWatchedFolders = managedByWatchedFolders;
            _fileCount = fileCount;
            Root = Path.Combine(Path.GetTempPath(), "OpenSorSe-relationship-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "deep-index.db");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public SqliteDeepIndexStore CreateStore() => new(DatabasePath, PlatformServices.CurrentPathSemantics);

        public async Task<SqliteDeepIndexStore> CreatePopulatedStoreAsync()
        {
            var store = CreateStore();
            await store.InitializeAsync();
            var source = new IndexingSource(
                "source",
                Root,
                "Synthetic source",
                IndexingLevel.Standard,
                true,
                true,
                0,
                [])
            {
                ManagedByWatchedFolders = _managedByWatchedFolders,
            };
            await store.UpsertSourceAsync(source);
            var run = await store.BeginRunAsync(source.Id, Epoch);
            var observations = Enumerable.Range(0, _fileCount)
                .Select(index => new IndexingFileObservation(
                    Path.Combine(Root, $"invoice-{1234 + index}.pdf"),
                    $"records{Path.DirectorySeparatorChar}invoice-{1234 + index}.pdf",
                    "identity-" + index,
                    "synthetic-volume",
                    100 + index,
                    Epoch,
                    Epoch.AddMinutes(index),
                    FileAttributes.Normal,
                    "metadata-" + index))
                .ToArray();
            await store.EnqueueDiscoveredFilesAsync(run, observations, "processor", 3);
            await store.CompleteDiscoveryAsync(run, new HashSet<string>(), Epoch);
            while (await store.ClaimNextAsync(Epoch.AddDays(1)) is { } claim)
            {
                var next = claim.Stage switch
                {
                    IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
                    IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
                    IndexingStage.ContentFingerprinted => IndexingStage.TextExtracted,
                    IndexingStage.TextExtracted => IndexingStage.SummaryKeywordsGenerated,
                    IndexingStage.SummaryKeywordsGenerated => IndexingStage.SemanticRepresentationGenerated,
                    IndexingStage.SemanticRepresentationGenerated => IndexingStage.SearchIndexUpdated,
                    IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
                    IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
                    IndexingStage.FileFullyIndexed => (IndexingStage?)null,
                    _ => throw new InvalidOperationException("Unexpected stage."),
                };
                var output = claim.Stage switch
                {
                    IndexingStage.ContentFingerprinted => new IndexingStageOutput { Status = IndexingStageStatus.Complete, ContentHash = "hash-" + claim.FileId },
                    IndexingStage.TextExtracted => new IndexingStageOutput { Status = IndexingStageStatus.Complete, ExtractedText = "Mercedes invoice synthetic text " + claim.FileId },
                    IndexingStage.SummaryKeywordsGenerated => new IndexingStageOutput { Status = IndexingStageStatus.Complete, Summary = "Mercedes invoice", Keywords = ["mercedes", "invoice"] },
                    IndexingStage.SemanticRepresentationGenerated => new IndexingStageOutput { Status = IndexingStageStatus.Complete, SemanticRepresentation = [1f, 0f] },
                    _ => new IndexingStageOutput { Status = IndexingStageStatus.Complete },
                };
                await store.SaveStageOutputAsync(claim, output, next, Epoch.AddDays(1), TimeSpan.Zero, null);
            }

            return store;
        }

        public void CreateVersionTwoDatabase()
        {
            var schemaType = typeof(SqliteDeepIndexStore).Assembly.GetType("OpenSorSe.Indexing.Sqlite.SqliteDeepIndexSchema", throwOnError: true)!;
            var versionOne = (string)schemaType.GetField("CreateVersionOne", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
            var versionTwo = (string)schemaType.GetField("CreateVersionTwo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
            Execute(versionOne + versionTwo + " PRAGMA user_version = 2;");
        }

        public void Execute(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public string Scalar(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
        }

        public int ReadUserVersion() => int.Parse(Scalar("PRAGMA user_version;"), System.Globalization.CultureInfo.InvariantCulture);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class Configuration : IConfigurationService
    {
        public Configuration(DeepIndexingSettings? deepIndexing = null)
        {
            Current = new ApplicationSettings { DeepIndexing = deepIndexing ?? new DeepIndexingSettings() };
        }

        public ApplicationSettings Current { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
