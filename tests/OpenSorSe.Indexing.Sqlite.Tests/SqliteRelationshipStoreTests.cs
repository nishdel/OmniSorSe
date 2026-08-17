using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
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
        var reconciliation = new ReconciliationSignal();
        var service = new RelationshipService(
            new Configuration(),
            store,
            new DeterministicRelationshipEngine(new FixedTimeProvider(Epoch)),
            derivedProjectionInvalidator: reconciliation.SignalAsync);

        var first = await service.AnalyzeFileAsync(files[0].FileId);
        var second = await service.AnalyzeFileAsync(files[1].FileId);

        Assert.Equal(0, first.CandidateCount);
        Assert.True(second.CandidateCount > 0);
        Assert.True(second.RelationshipCount > 0);
        Assert.NotEmpty(await service.GetRelatedFilesAsync(files[0].FileId));
        Assert.Equal(2, reconciliation.Count);
    }

    /// <summary>Verifies relationship-version refresh is bounded, resumable, cancellable, and leaves extracted content untouched.</summary>
    [Fact]
    public async Task RelationshipService_ReanalyzesOnlyStaleRelationshipFeaturesInRestartableBatches()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var files = await store.GetRelationshipFilesAsync(10);
        foreach (var file in files)
        {
            await store.UpsertRelationshipFeaturesAsync(
                new RelationshipFeatureSet(file.FileId, "stale", "source:records", null, null, null, null, null, [], "old"),
                Epoch);
        }

        var extractedBefore = fixture.Scalar("SELECT GROUP_CONCAT(extracted_text, '|') FROM index_content ORDER BY content_hash;");
        var service = new RelationshipService(
            new Configuration(),
            store,
            new DeterministicRelationshipEngine(new FixedTimeProvider(Epoch)));

        var firstBatch = await service.ReanalyzeStaleAsync(1);

        Assert.Equal(1, firstBatch.SelectedCount);
        Assert.Equal(1, firstBatch.CompletedCount);
        Assert.True(firstBatch.HasMore);
        Assert.Single(await store.GetStaleRelationshipFileIdsAsync("3.0.0", 10));
        Assert.Equal(extractedBefore, fixture.Scalar("SELECT GROUP_CONCAT(extracted_text, '|') FROM index_content ORDER BY content_hash;"));

        var resumed = await service.ReanalyzeStaleAsync(1);

        Assert.Equal(1, resumed.CompletedCount);
        Assert.False(resumed.HasMore);
        Assert.Empty(await store.GetStaleRelationshipFileIdsAsync("3.0.0", 10));

        await store.UpsertRelationshipFeaturesAsync(
            new RelationshipFeatureSet(files[0].FileId, "stale", "source:records", null, null, null, null, null, [], "old"),
            Epoch.AddHours(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReanalyzeStaleAsync(1, cancellation.Token));
        Assert.Single(await store.GetStaleRelationshipFileIdsAsync("3.0.0", 10));
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

    /// <summary>Verifies positive pair authority survives lost heuristic evidence until the user returns to automatic mode.</summary>
    [Fact]
    public async Task AlwaysRelateAuthority_SurvivesReanalysisAndClearsReversibly()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        await store.SaveRelationshipAnalysisAsync(batch, 100);
        var relationship = Assert.Single(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));

        await store.SetRelationshipDecisionAsync(
            relationship.Relationship.Id,
            RelationshipDecision.AlwaysRelate,
            Epoch.AddHours(1));
        await store.SaveRelationshipAnalysisAsync(
            batch with { Proposals = [], CompletedAtUtc = Epoch.AddHours(2) },
            100);

        var retained = Assert.Single(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.Equal(RelationshipDecision.AlwaysRelate, retained.Relationship.Decision);

        await store.ClearRelationshipDecisionAsync(first.FileId, second.FileId, Epoch.AddHours(3));
        await store.SaveRelationshipAnalysisAsync(
            batch with { Proposals = [], CompletedAtUtc = Epoch.AddHours(4) },
            100);

        Assert.Empty(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.Empty(await store.GetRelationshipCorrectionsAsync(first.FileId, 10));
    }

    /// <summary>Verifies typed edges aggregate into one target and pair authority is visible and reversible.</summary>
    [Fact]
    public async Task RelatedFiles_AggregatesTypedPairAndClearsPairAuthority()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        var secondEdge = batch.Proposals[0].Relationship with
        {
            Id = "rel:test-topic",
            Type = RelationshipType.SameTopic,
            Evidence = [new RelationshipEvidence(RelationshipEvidenceKind.Keyword, "lexical:derived:mercedes", "Shared keyword: mercedes")],
        };
        await store.SaveRelationshipAnalysisAsync(
            batch with { Proposals = [batch.Proposals[0], new RelationshipProposal(secondEdge, null)] },
            100);

        var related = Assert.Single(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.Equal(2, related.ContributingRelationships.Count);
        Assert.Equal(2, related.ContributingRelationships.Select(item => item.Type).Distinct().Count());

        await store.SetRelationshipDecisionAsync(related.Relationship.Id, RelationshipDecision.NeverRelate, Epoch.AddHours(1));
        Assert.Empty(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
        var correction = Assert.Single(await store.GetRelationshipCorrectionsAsync(first.FileId, 10));
        Assert.Equal(RelationshipDecision.NeverRelate, correction.Decision);
        Assert.False(correction.HasVisibleRelationship);

        var cleared = await store.ClearRelationshipDecisionAsync(first.FileId, second.FileId, Epoch.AddHours(2));
        Assert.True(cleared.Applied);
        Assert.Empty(await store.GetRelationshipCorrectionsAsync(first.FileId, 10));
        await store.SaveRelationshipAnalysisAsync(batch with { CompletedAtUtc = Epoch.AddHours(3) }, 100);
        Assert.Single(await store.GetRelatedFilesAsync(first.FileId, null, null, RelatedFileSort.Confidence, 10));
    }

    /// <summary>Verifies relationship hydration projects the existing effective Smart Tag authority.</summary>
    [Fact]
    public async Task RelationshipHydration_UsesEffectiveSmartTags()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, _) = await GetPairAsync(store);
        await store.AddUserTagAsync(first.FileId, "Project Phoenix", Epoch);

        var hydrated = Assert.IsType<RelationshipFileDocument>(await store.GetRelationshipFileAsync(first.FileId));

        var tag = Assert.Single(hydrated.TagEvidence);
        Assert.Equal(OpenSorSe.Application.SmartTags.SmartTagOrigin.User, tag.Origin);
        Assert.Equal(OpenSorSe.Application.SmartTags.SmartTagAssignmentState.Accepted, tag.State);
        Assert.Contains(tag.CanonicalKey, hydrated.Tags);
    }

    /// <summary>Verifies relationship-only algorithm staleness is selected deterministically without touching other stages.</summary>
    [Fact]
    public async Task StaleRelationshipSelection_IsVersionTargetedAndBounded()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var files = await store.GetRelationshipFilesAsync(10);
        var (first, second) = await GetPairAsync(store);
        var currentBatch = CreateBatch(first, second);
        await store.SaveRelationshipAnalysisAsync(
            currentBatch with
            {
                Features = currentBatch.Features with { FeatureVersion = "current" },
                AlgorithmVersion = "current",
            },
            100);
        await store.UpsertRelationshipFeaturesAsync(
            new RelationshipFeatureSet(files[0].FileId, "alpha", "source:records", null, null, null, null, null, [], "old"),
            Epoch);
        await store.UpsertRelationshipFeaturesAsync(
            new RelationshipFeatureSet(files[1].FileId, "beta", "source:records", null, null, null, null, null, [], "current"),
            Epoch);

        var stale = await store.GetStaleRelationshipFileIdsAsync("current", 10);
        var diagnostics = await store.GetRelationshipDiagnosticsAsync();

        Assert.Equal([files[0].FileId], stale);
        Assert.Equal(1, diagnostics.StaleRelationshipFileCount);
        Assert.True(diagnostics.RepairNeeded);
        Assert.Equal(0, diagnostics.InvalidRecordCount);

        var orphanPair = new[] { "missing", files[0].FileId }.Order(StringComparer.Ordinal).ToArray();
        fixture.Execute(
            $"PRAGMA foreign_keys=OFF; INSERT INTO relationship_pair_overrides(first_file_id, second_file_id, decision, relationship_type, changed_utc_ticks) VALUES('{orphanPair[0]}', '{orphanPair[1]}', {(int)RelationshipDecision.NeverRelate}, {(int)RelationshipType.SameTopic}, 0);");
        var invalid = await store.GetRelationshipDiagnosticsAsync();
        Assert.Equal(1, invalid.InvalidRecordCount);
        Assert.True(invalid.RepairNeeded);
    }

    /// <summary>Verifies authored automatic-collection rename, pin, exclusion, and tombstone state round-trips.</summary>
    [Fact]
    public async Task SmartCollectionAuthority_RoundTripsWithoutGeneratedEdges()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);
        var collection = Assert.Single(await store.GetCollectionsAsync(10));
        await store.RenameCollectionAsync(collection.Id, "My Purchase", Epoch.AddHours(1));
        await store.SetCollectionPinnedAsync(collection.Id, true, Epoch.AddHours(1));
        await store.SplitCollectionMemberAsync(collection.Id, second.FileId, Epoch.AddHours(1));
        var authority = await store.ExportSmartCollectionUserAuthorityAsync(100);

        await store.RestoreSmartCollectionUserAuthorityAsync(
            new SmartCollectionAuthorityBundle([], []),
            replace: true,
            changedAtUtc: Epoch.AddHours(2));
        await store.RestoreSmartCollectionUserAuthorityAsync(authority, replace: true, changedAtUtc: Epoch.AddHours(3));

        var restored = Assert.IsType<SmartCollectionDetails>(await store.GetCollectionAsync(collection.Id, 10));
        Assert.Equal("My Purchase", restored.Collection.Title);
        Assert.True(restored.Collection.IsPinned);
        Assert.True(restored.Collection.IsUserRenamed);
        Assert.DoesNotContain(restored.Members, item => item.FileId == second.FileId);

        await store.ForgetCollectionAsync(collection.Id, Epoch.AddHours(4));
        var tombstones = await store.ExportSmartCollectionUserAuthorityAsync(100);
        Assert.Contains("purchase:invoice-1234", tombstones.ForgottenContextKeys);
    }

    /// <summary>Verifies an automatic collection exclusion survives restore before generated membership is rebuilt.</summary>
    [Fact]
    public async Task SmartCollectionAuthority_RestoresAutomaticPlaceholderAndExclusionBeforeReanalysis()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        await store.SaveRelationshipAnalysisAsync(batch, 100);
        var collection = Assert.Single(await store.GetCollectionsAsync(10));
        await store.SplitCollectionMemberAsync(collection.Id, second.FileId, Epoch.AddHours(1));
        var authority = await store.ExportSmartCollectionUserAuthorityAsync(100);
        fixture.Execute($"DELETE FROM smart_collections WHERE id = '{collection.Id}';");

        var restored = await store.RestoreSmartCollectionUserAuthorityAsync(
            authority,
            replace: false,
            changedAtUtc: Epoch.AddHours(2));

        Assert.True(restored.AppliedCount >= 2);
        Assert.Empty(await store.GetCollectionsAsync(10));
        Assert.Equal("1", fixture.Scalar($"SELECT COUNT(*) FROM smart_collection_member_overrides WHERE collection_id = '{collection.Id}' AND file_id = '{second.FileId}';"));

        await store.SaveRelationshipAnalysisAsync(batch with { CompletedAtUtc = Epoch.AddHours(3) }, 100);
        var regenerated = Assert.IsType<SmartCollectionDetails>(await store.GetCollectionAsync(collection.Id, 10));
        Assert.Contains(regenerated.Members, item => item.FileId == first.FileId);
        Assert.DoesNotContain(regenerated.Members, item => item.FileId == second.FileId);
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

    /// <summary>Verifies clearing content intelligence invalidates automatic relationships derived from it.</summary>
    [Fact]
    public async Task ClearContentIntelligence_InvalidatesAutomaticRelationshipProjection()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        var batch = CreateBatch(first, second);
        await store.SaveRelationshipAnalysisAsync(batch, 100);

        var result = await store.ClearFileDataAsync(
            first.FileId,
            IndexedDataKind.ContentIntelligence,
            Epoch.AddHours(1));

        Assert.True(result.Applied);
        Assert.Null(await store.GetRelationshipAsync(batch.Proposals[0].Relationship.Id));
        Assert.Empty(await store.GetRelatedFilesAsync(second.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM index_relationship_features;"));
    }

    /// <summary>Verifies a privacy clear never removes an explicit user-created relationship.</summary>
    [Fact]
    public async Task ClearContentIntelligence_PreservesManualRelationship()
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

        await store.ClearFileDataAsync(
            first.FileId,
            IndexedDataKind.ContentIntelligence,
            Epoch.AddHours(1));

        var retained = Assert.Single(await store.GetRelatedFilesAsync(
            first.FileId,
            null,
            null,
            RelatedFileSort.Confidence,
            10));
        Assert.True(retained.Relationship.IsManual);
        Assert.Equal(RelationshipDecision.AlwaysRelate, retained.Relationship.Decision);
    }

    /// <summary>Verifies forgetting relationship data can suppress future analysis without deleting the indexed file.</summary>
    [Fact]
    public async Task ForgetFile_RetainsFileAndSuppressesFutureRelationshipAnalysis()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var (first, second) = await GetPairAsync(store);
        await store.SaveRelationshipAnalysisAsync(CreateBatch(first, second), 100);
        await store.LinkFilesAsync(first.FileId, second.FileId, RelationshipType.Manual, null, true, Epoch);
        var collection = Assert.Single(await store.GetCollectionsAsync(10));
        await store.SplitCollectionMemberAsync(collection.Id, first.FileId, Epoch.AddMinutes(1));

        var result = await store.ForgetFileRelationshipsAsync(first.FileId, true, Epoch.AddHours(1));
        var retained = Assert.IsType<RelationshipFileDocument>(await store.GetRelationshipFileAsync(first.FileId));

        Assert.True(result.Applied);
        Assert.True(retained.RelationshipAnalysisSuppressed);
        Assert.Empty(await store.GetRelatedFilesAsync(second.FileId, null, null, RelatedFileSort.Confidence, 10));
        Assert.Empty(await store.GetRelationshipCorrectionsAsync(second.FileId, 10));
        Assert.Equal("0", fixture.Scalar($"SELECT COUNT(*) FROM relationship_pair_overrides WHERE first_file_id = '{first.FileId}' OR second_file_id = '{first.FileId}';"));
        Assert.Equal("0", fixture.Scalar($"SELECT COUNT(*) FROM smart_collection_members WHERE file_id = '{first.FileId}';"));
        Assert.Equal("0", fixture.Scalar($"SELECT COUNT(*) FROM smart_collection_member_overrides WHERE file_id = '{first.FileId}';"));
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

    /// <summary>Shared bounded concepts retrieve cross-media candidates without relying on filename, folder, or date proximity.</summary>
    [Fact]
    public async Task CandidateSelection_FindsIndexedConceptAcrossOtherwiseUnrelatedFiles()
    {
        using var fixture = new Fixture(fileCount: 3);
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var files = await store.GetRelationshipFilesAsync(10);
        await store.UpsertRelationshipFeaturesAsync(
            new RelationshipFeatureSet(files[0].FileId, "network notes", "documents", null, null, null, null, null, ["raspberry pi"], "2.3.0"),
            Epoch);
        await store.UpsertRelationshipFeaturesAsync(
            new RelationshipFeatureSet(files[1].FileId, "spoken diary", "recordings", null, null, null, null, null, ["raspberry pi"], "2.3.0"),
            Epoch);
        await store.UpsertRelationshipFeaturesAsync(
            new RelationshipFeatureSet(files[2].FileId, "holiday photo", "images", null, null, null, null, null, ["dolomites"], "2.3.0"),
            Epoch);

        var candidates = await store.GetRelationshipCandidatesAsync(
            new RelationshipFeatureSet(files[0].FileId, "network notes", "documents", null, null, null, null, null, ["raspberry pi"], "2.3.0"),
            10);

        var candidate = Assert.Single(candidates);
        Assert.Equal(files[1].FileId, candidate.FileId);
    }

    /// <summary>Verifies skewed common evidence in a conceptual 100k library remains capped and deterministic.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task CandidateSelection_100kSkewedLibraryRemainsBoundedAndDeterministic()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        fixture.Execute(
            """
            WITH RECURSIVE sequence(value) AS (
                VALUES(1)
                UNION ALL SELECT value + 1 FROM sequence WHERE value < 100000
            )
            INSERT INTO index_files(
                id, source_id, full_path, path_key, relative_path, relative_path_key,
                stable_identity, file_system_id, length, creation_utc_ticks, modified_utc_ticks,
                attributes, metadata_fingerprint, content_hash, processor_fingerprint,
                indexing_level, fully_indexed, deleted_utc_ticks, last_seen_run_id, updated_utc_ticks)
            SELECT printf('scale-%06d', value), 'source', printf('/scale/common/file-%06d.txt', value),
                   printf('/scale/common/file-%06d.txt', value), printf('common/file-%06d.txt', value),
                   printf('common/file-%06d.txt', value), printf('stable-%06d', value), 'scale-volume',
                   128, 638924400000000000, 638924400000000000, 0, 'scale', NULL, 'scale',
                   1, 1, NULL, NULL, 638924400000000000
            FROM sequence;

            INSERT INTO index_relationship_features(
                file_id, normalized_stem, folder_key, content_hash, date_bucket,
                extracted_text_fingerprint, ocr_text_fingerprint, summary_fingerprint,
                keyword_keys_json, feature_version, updated_utc_ticks)
            SELECT id, 'generic document', 'source:source:common', NULL, NULL, NULL, NULL, NULL,
                   '["context:derived:common-topic"]', '3.0.0', 638924400000000000
            FROM index_files WHERE id LIKE 'scale-%';

            INSERT INTO index_relationship_feature_terms(file_id, term)
            SELECT id, 'context:derived:common-topic' FROM index_files WHERE id LIKE 'scale-%';
            """);
        var target = new RelationshipFeatureSet(
            "scale-000001",
            "generic document",
            "source:source:common",
            null,
            null,
            null,
            null,
            null,
            ["context:derived:common-topic"],
            "3.0.0");

        var stopwatch = Stopwatch.StartNew();
        var first = await store.GetRelationshipCandidatesAsync(target, 256);
        var second = await store.GetRelationshipCandidatesAsync(target, 256);
        stopwatch.Stop();

        Assert.InRange(first.Count, 1, 256);
        Assert.Equal(first.Select(item => item.FileId), second.Select(item => item.FileId));
        Assert.DoesNotContain(first, item => item.FileId == target.FileId);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Two 100k-library candidate queries took {stopwatch.Elapsed}.");
        Assert.True(new FileInfo(fixture.DatabasePath).Length < 128L * 1024L * 1024L, "The bounded relationship projection exceeded 128 MiB.");
    }

    /// <summary>Oversized candidate terms are rejected before JSON or indexed projections are written.</summary>
    [Fact]
    public async Task RelationshipFeatures_RejectOversizedCandidateTerm()
    {
        using var fixture = new Fixture();
        await using var store = await fixture.CreatePopulatedStoreAsync();
        var file = Assert.Single((await store.GetRelationshipFilesAsync(10)).Take(1));
        var features = new RelationshipFeatureSet(
            file.FileId,
            "notes",
            "documents",
            null,
            null,
            null,
            null,
            null,
            [new string('x', 65)],
            "2.3.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpsertRelationshipFeaturesAsync(features, Epoch));

        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM index_relationship_feature_terms;"));
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
            Root = Path.Combine(Path.GetTempPath(), "OmniSorSe-relationship-tests", Guid.NewGuid().ToString("N"));
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

    private sealed class ReconciliationSignal : IGraphReconciliationSignal
    {
        public int Count { get; private set; }

        public ValueTask SignalAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return ValueTask.CompletedTask;
        }
    }
}
