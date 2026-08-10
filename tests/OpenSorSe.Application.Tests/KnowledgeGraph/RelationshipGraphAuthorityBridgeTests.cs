using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Relationships;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Verifies the bounded adapter that preserves the v1.9 relationship decision authority.</summary>
public sealed class RelationshipGraphAuthorityBridgeTests
{
    /// <summary>Verifies an exact legacy pair resolves to its stable relationship ID before unlink.</summary>
    [Fact]
    public async Task UnlinkRelationship_ExactPair_ForwardsStableRelationshipId()
    {
        var relationship = Relationship("legacy-rel-1", "file-a", "file-b");
        var service = new RelationshipServiceStub([Related(relationship)]);
        var bridge = new RelationshipGraphAuthorityBridge(service);

        var result = await bridge.UnlinkRelationshipAsync("file-b", "file-a", preventRegeneration: true);

        Assert.True(result.Succeeded);
        Assert.Equal(("legacy-rel-1", true), service.UnlinkRequest);
        Assert.Contains("authoritative v1.9 relationship", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies ambiguous legacy rows fail closed without mutating either authority.</summary>
    [Fact]
    public async Task UnlinkRelationship_AmbiguousPair_FailsClosed()
    {
        var service = new RelationshipServiceStub(
        [
            Related(Relationship("legacy-rel-1", "file-a", "file-b")),
            Related(Relationship("legacy-rel-2", "file-b", "file-a")),
        ]);
        var bridge = new RelationshipGraphAuthorityBridge(service);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            bridge.UnlinkRelationshipAsync("file-a", "file-b", preventRegeneration: true));

        Assert.Equal("legacy-relationship-ambiguous", error.ReasonCode);
        Assert.Null(service.UnlinkRequest);
    }

    /// <summary>Verifies a stale graph pair cannot create a substitute graph-side correction.</summary>
    [Fact]
    public async Task UnlinkRelationship_MissingPair_FailsClosed()
    {
        var service = new RelationshipServiceStub([]);
        var bridge = new RelationshipGraphAuthorityBridge(service);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            bridge.UnlinkRelationshipAsync("file-a", "file-b", preventRegeneration: true));

        Assert.Equal("legacy-relationship-not-resolvable", error.ReasonCode);
        Assert.Null(service.UnlinkRequest);
    }

    /// <summary>Verifies collection membership removal uses the existing persistent split override.</summary>
    [Fact]
    public async Task SplitCollectionMember_ForwardsExistingStableIds()
    {
        var service = new RelationshipServiceStub([]);
        var bridge = new RelationshipGraphAuthorityBridge(service);

        var result = await bridge.SplitCollectionMemberAsync("collection-1", "file-a");

        Assert.True(result.Succeeded);
        Assert.Equal(("collection-1", "file-a"), service.SplitRequest);
        Assert.Contains("authoritative v1.9 Smart Collection membership", result.Message, StringComparison.Ordinal);
    }

    private static RelatedFile Related(FileRelationship relationship) => new()
    {
        FileId = relationship.SecondFileId,
        FileName = "synthetic.txt",
        FullPath = "/synthetic/synthetic.txt",
        SourceName = "Synthetic",
        Relationship = relationship,
    };

    private static FileRelationship Relationship(string id, string first, string second) => new()
    {
        Id = id,
        FirstFileId = first,
        SecondFileId = second,
        Type = RelationshipType.SameProject,
        Confidence = RelationshipConfidence.High,
        Evidence = [new RelationshipEvidence(RelationshipEvidenceKind.Filename, "synthetic", "Synthetic evidence")],
        Algorithm = "synthetic",
        AlgorithmVersion = "1.0.0",
        CreatedAtUtc = TestGraphData.Now,
        LastValidatedAtUtc = TestGraphData.Now,
    };

    private sealed class RelationshipServiceStub(IReadOnlyList<RelatedFile> related) : IRelationshipService
    {
        internal (string RelationshipId, bool NeverRelate)? UnlinkRequest { get; private set; }
        internal (string CollectionId, string FileId)? SplitRequest { get; private set; }

        public Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
            string fileId,
            RelationshipType? type = null,
            RelationshipConfidence? minimumConfidence = null,
            RelatedFileSort sort = RelatedFileSort.Confidence,
            int maximumCount = 200,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RelatedFile>>(related.Take(maximumCount).ToArray());
        }

        public Task<RelationshipOperationResult> UnlinkAsync(
            string relationshipId,
            bool neverRelate = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnlinkRequest = (relationshipId, neverRelate);
            return Success("Existing relationship removed.");
        }

        public Task<RelationshipOperationResult> SplitCollectionMemberAsync(
            string collectionId,
            string fileId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SplitRequest = (collectionId, fileId);
            return Success("Existing membership removed.");
        }

        public Task<RelationshipAnalysisResult> AnalyzeFileAsync(string fileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RelationshipFileDocument>> GetFilesAsync(int maximumCount = 1_000, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FileRelationship?> GetRelationshipAsync(string relationshipId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SmartCollection>> GetCollectionsAsync(int maximumCount = 500, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<SmartCollectionDetails?> GetCollectionAsync(string collectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> LinkFilesAsync(string firstFileId, string secondFileId, RelationshipType type, string? customType = null, bool alwaysRelate = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> SetDecisionAsync(string relationshipId, RelationshipDecision decision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> RenameCollectionAsync(string collectionId, string title, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> SetCollectionPinnedAsync(string collectionId, bool pinned, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> MergeCollectionsAsync(string targetCollectionId, string sourceCollectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> ForgetCollectionAsync(string collectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> ForgetFileAsync(string fileId, bool excludeFutureAnalysis, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> ForgetSourceAsync(string sourceId, bool excludeFutureAnalysis, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> RebuildFileAsync(string fileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipOperationResult> RepairAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RelationshipSearchExpansion>> ExpandSearchAsync(IReadOnlyList<string> seedFileIds, int maximumCount, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RelationshipDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static Task<RelationshipOperationResult> Success(string message) =>
            Task.FromResult(new RelationshipOperationResult(true, 1, 0, message));
    }
}
