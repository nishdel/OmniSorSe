using System.Text.Json;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Validates deterministic bounded mechanical projection and truthful evidence.</summary>
public sealed class GraphProjectionBuilderTests
{
    private readonly DeterministicGraphProjectionBuilder _builder = new(new ConservativeGraphIdentityResolver());

    /// <summary>Verifies a file projects only mechanical identities, evidence-backed edges, and bounded facts.</summary>
    [Fact]
    public void Build_File_ProjectsStableMechanicalComponent()
    {
        var file = TestGraphData.File();

        var component = _builder.Build(file, Snapshot(file), TestGraphData.Now);

        Assert.Equal(string.Concat(file.Kind.ToString(), ":", file.StableKey), component.ComponentKey);
        Assert.Equal(3, component.Nodes.Count);
        Assert.Contains(component.Nodes, item => item.Identity.Kind == GraphNodeKind.File);
        Assert.Contains(component.Nodes, item => item.Identity.Kind == GraphNodeKind.Folder);
        Assert.Contains(component.Nodes, item => item.Identity.Kind == GraphNodeKind.DocumentSet);
        Assert.Contains(component.Edges, item => item.Kind == GraphEdgeKind.OwnedBySource);
        Assert.Contains(component.Edges, item => item.Kind == GraphEdgeKind.LocatedInFolder);
        Assert.Contains(component.Edges, item => item.Kind == GraphEdgeKind.SameDocumentSet);
        Assert.Equal(3, component.Facts.Count);
        Assert.All(component.Edges, edge => Assert.NotEmpty(edge.EvidenceIds));
        Assert.Single(component.RequiredNodeIds);
    }

    /// <summary>Verifies source-scoped actions retain the real provider-neutral source ID.</summary>
    [Fact]
    public void Build_SourceOwnedNodes_RetainAuthoritativeSourceId()
    {
        var file = TestGraphData.File();

        var fileComponent = _builder.Build(file, Snapshot(file), TestGraphData.Now);
        var sourceComponent = _builder.Build(TestGraphData.Source(), Snapshot(TestGraphData.Source()), TestGraphData.Now);

        Assert.Equal(file.SourceId, Assert.Single(fileComponent.Nodes, item => item.Identity.Kind == GraphNodeKind.File).OwningSourceId);
        Assert.Equal(file.SourceId, Assert.Single(fileComponent.Nodes, item => item.Identity.Kind == GraphNodeKind.Folder).OwningSourceId);
        Assert.Null(Assert.Single(fileComponent.Nodes, item => item.Identity.Kind == GraphNodeKind.DocumentSet).OwningSourceId);
        Assert.Equal(
            TestGraphData.Source().SourceId,
            Assert.Single(sourceComponent.Nodes, item => item.Identity.Kind == GraphNodeKind.Source).OwningSourceId);
    }

    /// <summary>Verifies privacy suppression prevents content-derived document-set projection.</summary>
    [Fact]
    public void Build_RelationshipSuppressedFile_DoesNotProjectDocumentSet()
    {
        var file = TestGraphData.File() with { RelationshipAnalysisSuppressed = true };

        var component = _builder.Build(file, Snapshot(file), TestGraphData.Now);

        Assert.DoesNotContain(component.Nodes, item => item.Identity.Kind == GraphNodeKind.DocumentSet);
        Assert.DoesNotContain(component.Edges, item => item.Kind == GraphEdgeKind.SameDocumentSet);
    }

    /// <summary>Verifies current exclusion becomes a deletion projection and retains no candidate facts.</summary>
    [Fact]
    public void Build_ExcludedObservation_ProducesDeletionComponent()
    {
        var file = TestGraphData.File() with { IsExcluded = true };

        var component = _builder.Build(file, Snapshot(file), TestGraphData.Now);

        Assert.True(component.IsDeletion);
        Assert.Empty(component.Nodes);
        Assert.Empty(component.Edges);
        Assert.Empty(component.Facts);
    }

    /// <summary>Verifies an automatic legacy relationship retains exactly its authoritative evidence.</summary>
    [Fact]
    public void Build_AutomaticRelationship_RetainsEvidenceAndOrigin()
    {
        var relationship = Relationship();

        var component = _builder.Build(relationship, Snapshot(relationship), TestGraphData.Now);

        var edge = Assert.Single(component.Edges);
        Assert.Equal(GraphEdgeKind.RelatedFile, edge.Kind);
        Assert.Equal(GraphOrigin.LegacyRelationship, edge.Origin);
        Assert.False(edge.IsManual);
        var evidence = Assert.Single(component.Evidence);
        Assert.Equal("Same invoice number", evidence.Explanation);
        Assert.Equal([evidence.Id], edge.EvidenceIds);
    }

    /// <summary>Verifies an automatic relationship without retained evidence fails closed.</summary>
    [Fact]
    public void Build_AutomaticRelationshipWithoutEvidence_FailsClosed()
    {
        var relationship = Relationship() with { Evidence = [] };

        Assert.Throws<InvalidDataException>(() => _builder.Build(relationship, Snapshot(relationship), TestGraphData.Now));
    }

    /// <summary>Verifies manual legacy relationships are explicit and never fabricate evidence.</summary>
    [Fact]
    public void Build_ManualRelationship_DoesNotFabricateEvidence()
    {
        var relationship = Relationship() with { IsManual = true, Evidence = [] };

        var component = _builder.Build(relationship, Snapshot(relationship), TestGraphData.Now);

        var edge = Assert.Single(component.Edges);
        Assert.True(edge.IsManual);
        Assert.Equal(GraphConfidenceLevel.Confirmed, edge.Confidence);
        Assert.Empty(edge.EvidenceIds);
        Assert.Empty(component.Evidence);
    }

    /// <summary>Verifies a rejected authoritative relationship becomes a deletion projection.</summary>
    [Fact]
    public void Build_RejectedRelationship_ProducesDeletion()
    {
        var relationship = Relationship() with { IsRejected = true };

        Assert.True(_builder.Build(relationship, Snapshot(relationship), TestGraphData.Now).IsDeletion);
    }

    /// <summary>Verifies Smart Collection identity is reused without creating speculative Context entities.</summary>
    [Fact]
    public void Build_Collection_ProjectsCollectionOnly()
    {
        var collection = new GraphCollectionObservation
        {
            StableKey = "collection:tax",
            CanonicalRowHash = "collection-hash",
            Revision = 1,
            ObservedAtUtc = TestGraphData.Now,
            CollectionId = "tax",
            Title = "Tax Return 2026",
        };

        var component = _builder.Build(collection, Snapshot(collection), TestGraphData.Now);

        var node = Assert.Single(component.Nodes);
        Assert.Equal(GraphNodeKind.Collection, node.Identity.Kind);
        Assert.DoesNotContain(component.Nodes, item => item.Identity.Kind.Value == "context");
    }

    /// <summary>Verifies membership maps to one evidence-backed File-to-Collection edge.</summary>
    [Fact]
    public void Build_Membership_ProjectsMemberOfEdge()
    {
        var membership = new GraphCollectionMembershipObservation
        {
            StableKey = "membership:tax:file-1",
            CanonicalRowHash = "membership-hash",
            Revision = 1,
            ObservedAtUtc = TestGraphData.Now,
            CollectionId = "tax",
            FileId = "file-1",
        };

        var component = _builder.Build(membership, Snapshot(membership), TestGraphData.Now);

        Assert.Equal(GraphEdgeKind.MemberOf, Assert.Single(component.Edges).Kind);
        Assert.Single(component.Evidence);
        Assert.Equal(2, component.RequiredNodeIds.Count);
    }

    /// <summary>Verifies v1.9 decisions are retained only in the non-authoritative mirror namespace.</summary>
    [Fact]
    public void Build_LegacyDecision_ProjectsMirrorOnly()
    {
        var decision = new GraphLegacyDecisionObservation
        {
            StableKey = "legacy:never:file-1:file-2",
            CanonicalRowHash = "legacy-hash",
            Revision = 4,
            ObservedAtUtc = TestGraphData.Now,
            DecisionNamespace = "relationship-pair",
            LegacyDecisionKey = "file-1:file-2",
            ActionCode = "never-relate",
        };

        var component = _builder.Build(decision, Snapshot(decision), TestGraphData.Now);

        Assert.Empty(component.Nodes);
        Assert.Empty(component.Edges);
        var mirror = Assert.Single(component.LegacyDecisions);
        Assert.Equal("legacy-1", mirror.LegacyDecisionManifestId);
        Assert.False(mirror.IsRetired);
    }

    /// <summary>Verifies a malformed evidence explanation is rejected rather than exposed.</summary>
    [Theory]
    [InlineData("bad\0text")]
    public void Build_MalformedEvidence_FailsClosed(string explanation)
    {
        var relationship = Relationship() with
        {
            Evidence =
            [
                new GraphProjectionEvidence("evidence-1", GraphEvidenceKind.LegacyRelationship, "invoice", "invoice-match", explanation, "evidence-hash"),
            ],
        };

        Assert.Throws<InvalidDataException>(() => _builder.Build(relationship, Snapshot(relationship), TestGraphData.Now));
    }

    /// <summary>Verifies an unpaired surrogate in retained evidence is rejected.</summary>
    [Fact]
    public void Build_UnpairedSurrogateEvidence_FailsClosed()
    {
        var relationship = Relationship() with
        {
            Evidence =
            [
                new GraphProjectionEvidence("evidence-1", GraphEvidenceKind.LegacyRelationship, "invoice", "invoice-match", "bad\ud800text", "evidence-hash"),
            ],
        };

        Assert.Throws<InvalidDataException>(() => _builder.Build(relationship, Snapshot(relationship), TestGraphData.Now));
    }

    /// <summary>Verifies negative file sizes fail at the record boundary.</summary>
    [Fact]
    public void Build_NegativeFileSize_FailsClosed()
    {
        var file = TestGraphData.File() with { Length = -1 };

        Assert.Throws<InvalidDataException>(() => _builder.Build(file, Snapshot(file), TestGraphData.Now));
    }

    /// <summary>Verifies identical logical inputs yield canonically identical output.</summary>
    [Fact]
    public void Build_IdenticalInputs_IsDeterministic()
    {
        var relationship = Relationship();
        var snapshot = Snapshot(relationship);

        var first = _builder.Build(relationship, snapshot, TestGraphData.Now);
        var second = _builder.Build(relationship, snapshot, TestGraphData.Now);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    /// <summary>Verifies a manifest whose terminal kind counts do not sum to its total is rejected.</summary>
    [Fact]
    public void Build_InvalidManifestCounts_FailsClosed()
    {
        var source = TestGraphData.Source();
        var invalid = Snapshot(source) with { TotalObservationCount = 2 };

        Assert.Throws<InvalidDataException>(() => _builder.Build(source, invalid, TestGraphData.Now));
    }

    /// <summary>Verifies cooperative cancellation is checked before projection work.</summary>
    [Fact]
    public void Build_Cancelled_StopsBeforeProjection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _builder.Build(TestGraphData.File(), Snapshot(TestGraphData.File()), TestGraphData.Now, cancellation.Token));
    }

    private static GraphRelationshipObservation Relationship() => new()
    {
        StableKey = "relationship:invoice",
        CanonicalRowHash = "relationship-hash",
        Revision = 1,
        ObservedAtUtc = TestGraphData.Now,
        RelationshipId = "relationship-1",
        FirstFileId = "file-1",
        SecondFileId = "file-2",
        RelationshipType = "same-purchase",
        Confidence = GraphConfidenceLevel.High,
        Algorithm = "deterministic-evidence",
        AlgorithmVersion = "1.0.0",
        Evidence =
        [
            new GraphProjectionEvidence(
                "relationship-evidence-1",
                GraphEvidenceKind.LegacyRelationship,
                "invoice-1234",
                "same-invoice-number",
                "Same invoice number",
                "evidence-hash"),
        ],
    };

    private static GraphProjectionSnapshot Snapshot(GraphProjectionObservation observation) => new(
        "manifest-1",
        1,
        "legacy-1",
        1,
        TestGraphData.Now,
        "manifest-hash",
        1,
        [new GraphObservationKindCount(observation.Kind, 1)]);
}
