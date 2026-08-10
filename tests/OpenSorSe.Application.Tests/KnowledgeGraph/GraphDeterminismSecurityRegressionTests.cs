using System.Text.Json;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>
/// Covers hostile graph-boundary inputs and deterministic graph behavior from the v2.0
/// identity, evidence, and traversal regression matrix.
/// </summary>
public sealed class GraphDeterminismSecurityRegressionTests
{
    private readonly DeterministicGraphProjectionBuilder _projectionBuilder =
        new(new ConservativeGraphIdentityResolver());
    private readonly DeterministicGraphDecisionProjectionBuilder _decisionBuilder =
        new(new ConservativeGraphIdentityResolver());

    /// <summary>Persistence classifications cannot smuggle paths, secrets, or control text into diagnostics.</summary>
    [Theory]
    [InlineData("C:/private/index.db")]
    [InlineData("token=secret")]
    [InlineData("provider busy")]
    public void PersistenceFailure_UnsafeReasonCode_IsRejected(string reasonCode)
    {
        Assert.Throws<ArgumentException>(() => new GraphPersistenceException(reasonCode, "Synthetic failure."));
    }

    /// <summary>Malformed and oversized persisted alias labels fail closed before projection.</summary>
    [Theory]
    [MemberData(nameof(InvalidAliasLabels))]
    public void DecisionProjection_InvalidAliasLabel_FailsClosed(string label)
    {
        var entry = Decision(GraphDecisionKind.AddAlias, "manual:alpha", label);

        Assert.Throws<InvalidDataException>(() =>
            _decisionBuilder.Build(entry, DecisionSnapshot(), TestGraphData.Now));
    }

    /// <summary>Alias cycles remain inert labels and never become implicit nodes or edges.</summary>
    [Fact]
    public void DecisionProjection_CyclicAliasLabels_DoNotCreateGraphStructure()
    {
        var first = _decisionBuilder.Build(
            Decision(GraphDecisionKind.AddAlias, "manual:alpha", "manual:beta", sequence: 1),
            DecisionSnapshot(2),
            TestGraphData.Now);
        var second = _decisionBuilder.Build(
            Decision(GraphDecisionKind.AddAlias, "manual:beta", "manual:alpha", sequence: 2),
            DecisionSnapshot(2),
            TestGraphData.Now);

        Assert.NotNull(first.Alias);
        Assert.NotNull(second.Alias);
        Assert.Null(first.Node);
        Assert.Null(first.Edge);
        Assert.Null(second.Node);
        Assert.Null(second.Edge);
        Assert.NotEqual(first.Alias!.Id, second.Alias!.Id);
    }

    /// <summary>Provider alias multiplicity above the hard bound is rejected at the query boundary.</summary>
    [Fact]
    public async Task Query_AliasMultiplicityAboveHardLimit_FailsClosed()
    {
        var store = ReadyStore();
        store.Nodes.Add(Node("manual:alpha", "manual:alpha"));
        store.Aliases.AddRange(Enumerable.Range(0, GraphLimits.MaximumAliasesPerNode + 1)
            .Select(index => new GraphAlias(
                string.Concat("alias-", index),
                "manual:alpha",
                string.Concat("Alias ", index),
                string.Concat("ALIAS ", index),
                GraphOrigin.Manual,
                string.Concat("decision-", index),
                TestGraphData.Now)));
        var service = QueryService(store);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodeDetailAsync("manual:alpha"));

        Assert.Equal("graph-record-invalid", error.ReasonCode);
    }

    /// <summary>Duplicate aliases cannot cross the provider-neutral query boundary.</summary>
    [Fact]
    public async Task Query_DuplicateAliases_FailClosed()
    {
        var store = ReadyStore();
        store.Nodes.Add(Node("manual:alpha", "manual:alpha"));
        store.Aliases.AddRange(
        [
            Alias("alias-1", "manual:alpha", "Alpha project"),
            Alias("alias-2", "manual:alpha", "Alpha project"),
        ]);
        var service = QueryService(store);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodeDetailAsync("manual:alpha"));

        Assert.Equal("graph-record-invalid", error.ReasonCode);
    }

    /// <summary>Adversarially similar filenames and folders never merge stable file identities.</summary>
    [Fact]
    public void Projection_FalseMergeCorpus_RetainsDistinctMechanicalIdentities()
    {
        var files = new[]
        {
            SimilarFile("file-01", "Mercedes Invoice 2026.pdf", "Invoices/Mercedes", '1'),
            SimilarFile("file-02", "Mercedes Invoices 2026.pdf", "Invoice/Mercedes", '2'),
            SimilarFile("file-03", "Mercedes Invoice 2026 (copy).pdf", "Invoices/Mercedes", '3'),
            SimilarFile("file-04", "Mercedes‑Invoice‑2026.pdf", "Invoices/Mercedes", '4'),
            SimilarFile("file-05", "Mercedez Invoice 2026.pdf", "Invoices/Mercedes", '5'),
        };

        var components = files.Select(file => _projectionBuilder.Build(
            file,
            Snapshot(file),
            TestGraphData.Now)).ToArray();

        Assert.Equal(files.Length, components
            .Select(component => Assert.Single(component.Nodes, node => node.Identity.Kind == GraphNodeKind.File).Identity.NodeId)
            .Distinct(StringComparer.Ordinal)
            .Count());
        Assert.Equal(files.Length, components
            .Select(component => Assert.Single(component.Nodes, node => node.Identity.Kind == GraphNodeKind.DocumentSet).Identity.NodeId)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    /// <summary>Conflicting records with one claimed node ID are rejected rather than silently collapsed.</summary>
    [Fact]
    public async Task Query_ConflictingDuplicateNodeIdentity_FailsClosed()
    {
        var store = ReadyStore();
        store.Nodes.Add(Node("kg:file:collision", "file-a"));
        store.Nodes.Add(Node("kg:file:collision", "file-b"));
        var service = QueryService(store);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodesPageAsync(new GraphNodeQuery()));

        Assert.Equal("graph-record-invalid", error.ReasonCode);
    }

    /// <summary>A three-edge cycle is retained without self-loops and is identical across input order.</summary>
    [Fact]
    public void Projection_CyclicRelationshipGraph_IsDeterministic()
    {
        var relationships = new[]
        {
            Relationship("ab", "file-a", "file-b"),
            Relationship("bc", "file-b", "file-c"),
            Relationship("ca", "file-c", "file-a"),
        };

        var forward = relationships
            .Select(item => Assert.Single(_projectionBuilder.Build(item, Snapshot(item), TestGraphData.Now).Edges).Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var reverse = relationships
            .Reverse()
            .Select(item => Assert.Single(_projectionBuilder.Build(item, Snapshot(item), TestGraphData.Now).Edges).Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(3, forward.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(forward, reverse);
        Assert.All(relationships, item =>
        {
            var edge = Assert.Single(_projectionBuilder.Build(item, Snapshot(item), TestGraphData.Now).Edges);
            Assert.NotEqual(edge.SourceNodeId, edge.TargetNodeId);
        });
    }

    /// <summary>Stable and experimental traversal depth ceilings reject graph-explosion requests.</summary>
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public async Task Query_TraversalBeyondSelectedDepthCeiling_IsRejected(bool experimental, int depth)
    {
        var service = QueryService(ReadyStore());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.GetNeighborsPageAsync(new GraphNeighborQuery(
                "file-1",
                Depth: depth,
                ExperimentalTraversal: experimental)));
    }

    /// <summary>Each retained automatic confidence and explanation is copied from actual source evidence.</summary>
    [Theory]
    [InlineData(GraphConfidenceLevel.Low)]
    [InlineData(GraphConfidenceLevel.Medium)]
    [InlineData(GraphConfidenceLevel.High)]
    public void Projection_ConfidenceAndExplanation_AreExactAndDeterministic(GraphConfidenceLevel confidence)
    {
        var relationship = Relationship("invoice", "file-a", "file-b", confidence);
        var snapshot = Snapshot(relationship);

        var first = _projectionBuilder.Build(relationship, snapshot, TestGraphData.Now);
        var second = _projectionBuilder.Build(relationship, snapshot, TestGraphData.Now);
        var edge = Assert.Single(first.Edges);
        var evidence = Assert.Single(first.Evidence);

        Assert.Equal(confidence, edge.Confidence);
        Assert.Equal("Same retained invoice number.", evidence.Explanation);
        Assert.Equal("same-invoice-number", evidence.ExplanationTemplateCode);
        Assert.Equal([evidence.Id], edge.EvidenceIds);
        Assert.Equal("synthetic-rule", edge.Algorithm);
        Assert.Equal("2.1.0", edge.AlgorithmVersion);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    /// <summary>Default source timestamps are rejected instead of becoming apparently valid graph records.</summary>
    [Fact]
    public void Projection_DefaultObservationTimestamp_FailsClosed()
    {
        var file = TestGraphData.File() with { ObservedAtUtc = default };

        Assert.Throws<InvalidDataException>(() =>
            _projectionBuilder.Build(file, Snapshot(file), TestGraphData.Now));
    }

    /// <summary>A hostile null filename is classified as invalid data instead of escaping as an incidental null fault.</summary>
    [Fact]
    public void Projection_NullFileName_FailsClosedAsInvalidData()
    {
        var file = TestGraphData.File() with { FileName = null! };

        Assert.Throws<InvalidDataException>(() =>
            _projectionBuilder.Build(file, Snapshot(file), TestGraphData.Now));
    }

    /// <summary>Default provider node timestamps are rejected by the application query boundary.</summary>
    [Fact]
    public async Task Query_DefaultNodeTimestamp_FailsClosed()
    {
        var store = ReadyStore();
        store.Nodes.Add(Node("file-1", "file-1") with { CreatedAtUtc = default });
        var service = QueryService(store);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodesPageAsync(new GraphNodeQuery()));

        Assert.Equal("graph-record-invalid", error.ReasonCode);
    }

    /// <summary>Enumerates bounded hostile alias values without external or developer data.</summary>
    public static TheoryData<string> InvalidAliasLabels => new()
    {
        "bad\0alias",
        "bad\ud800alias",
        new string('a', GraphLimits.MaximumLabelCharacters + 1),
    };

    private static GraphAlias Alias(string id, string nodeId, string label) => new(
        id,
        nodeId,
        label,
        label.ToUpperInvariant(),
        GraphOrigin.Manual,
        "decision-1",
        TestGraphData.Now);

    private static GraphDecisionEntry Decision(
        GraphDecisionKind kind,
        string subjectId,
        string label,
        long sequence = 1) => new(
            string.Concat("decision-", sequence),
            sequence,
            new GraphDecisionCommand
            {
                Kind = kind,
                SubjectId = subjectId,
                Label = label,
            },
            TestGraphData.Now,
            string.Concat("decision-hash-", sequence));

    private static GraphDecisionSnapshot DecisionSnapshot(long sequence = 1) =>
        new(sequence, string.Concat("checkpoint-", sequence), string.Concat("hash-", sequence), true);

    private static GraphFileObservation SimilarFile(
        string id,
        string fileName,
        string folder,
        char hashDigit) => TestGraphData.File(id) with
        {
            FileName = fileName,
            RelativePath = string.Concat(folder, "/", fileName),
            FolderRelativePath = folder,
            ContentHash = new string(hashDigit, 64),
        };

    private static GraphRelationshipObservation Relationship(
        string id,
        string first,
        string second,
        GraphConfidenceLevel confidence = GraphConfidenceLevel.High) => new()
        {
            StableKey = string.Concat("relationship:", id),
            CanonicalRowHash = string.Concat("relationship-hash-", id),
            Revision = 1,
            ObservedAtUtc = TestGraphData.Now,
            RelationshipId = id,
            FirstFileId = first,
            SecondFileId = second,
            RelationshipType = "same-purchase",
            Confidence = confidence,
            Algorithm = "synthetic-rule",
            AlgorithmVersion = "2.1.0",
            Evidence =
            [
                new GraphProjectionEvidence(
                    string.Concat("evidence:", id),
                    GraphEvidenceKind.LegacyRelationship,
                    string.Concat("invoice:", id),
                    "same-invoice-number",
                    "Same retained invoice number.",
                    string.Concat("evidence-hash-", id)),
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

    private static GraphQueryService QueryService(FakeGraphStore store) =>
        new(store, new FakeGraphProjectionSource(), new FakeGraphDecisionStore());

    private static FakeGraphStore ReadyStore() => new()
    {
        Coverage = TestGraphData.Coverage with
        {
            IsEnabled = true,
            IsAvailable = true,
            IsStale = false,
            IngestedManifestId = "manifest-1",
            IngestedRevision = 1,
            AppliedManifestId = "manifest-1",
            AppliedRevision = 1,
        },
    };

    private static GraphNode Node(string nodeId, string canonicalKey) => new()
    {
        Identity = new GraphIdentity(nodeId, GraphNodeKind.File, "file", canonicalKey, "test-v1", canonicalKey),
        DisplayLabel = canonicalKey,
        Origin = GraphOrigin.Mechanical,
        SourceManifestId = "manifest-1",
        ObservationHash = string.Concat("hash-", canonicalKey),
        Algorithm = "synthetic",
        AlgorithmVersion = "1.0.0",
        CreatedAtUtc = TestGraphData.Now,
        LastValidatedAtUtc = TestGraphData.Now,
    };
}
