using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Validates authority-fenced reads, privacy/repair mutations, and durable manual decisions.</summary>
public sealed class GraphApplicationServiceTests
{
    /// <summary>Verifies already-stale source projection cannot be read even when authority is otherwise allowed.</summary>
    [Fact]
    public async Task Query_SourceAdvancedBeforeRead_FailsClosed()
    {
        var source = new FakeGraphProjectionSource
        {
            Authority = new GraphAuthoritySnapshot(true, true, 1, "legacy-1", "allowed")
            {
                CurrentSourceManifestId = "manifest-2",
                CurrentSourceRevision = 2,
            },
        };
        var service = new GraphQueryService(new FakeGraphStore(), source, new FakeGraphDecisionStore());

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodesPageAsync(new GraphNodeQuery()));

        Assert.Equal("source-watermark-pending", error.ReasonCode);
    }

    /// <summary>Verifies a source change during a provider read cannot leak the earlier graph page.</summary>
    [Fact]
    public async Task Query_SourceChangesDuringRead_DiscardsPage()
    {
        var source = new FakeGraphProjectionSource();
        var store = ReadyStore();
        store.BeforeNodePageReturn = () =>
        {
            source.Authority = source.Authority with
            {
                CurrentSourceManifestId = "manifest-2",
                CurrentSourceRevision = 2,
            };
            store.Coverage = store.Coverage with { AppliedManifestId = "manifest-2", AppliedRevision = 2 };
        };
        var service = new GraphQueryService(store, source, new FakeGraphDecisionStore());

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodesPageAsync(new GraphNodeQuery()));

        Assert.Equal("authority-changed-during-read", error.ReasonCode);
    }

    /// <summary>Verifies graph-native reads respect disabled projection coverage.</summary>
    [Fact]
    public async Task Query_DisabledGraph_FailsClosed()
    {
        var store = new FakeGraphStore { Coverage = TestGraphData.Coverage with { IsEnabled = false } };
        var service = new GraphQueryService(store, new FakeGraphProjectionSource(), new FakeGraphDecisionStore());

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodesPageAsync(new GraphNodeQuery()));

        Assert.Equal("graph-disabled", error.ReasonCode);
    }

    /// <summary>Verifies authoritative control disables reads before a stale enabled mirror can be consulted.</summary>
    [Fact]
    public async Task Query_AuthoritativeControlDisabled_FailsClosed()
    {
        var decisions = new FakeGraphDecisionStore
        {
            ControlSettings = new GraphControlSettings(),
        };
        var store = ReadyStore();
        store.Nodes.Add(Node("file-1", GraphNodeKind.File, GraphOrigin.Mechanical));
        var service = new GraphQueryService(store, new FakeGraphProjectionSource(), decisions);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodesPageAsync(new GraphNodeQuery()));

        Assert.Equal("graph-disabled", error.ReasonCode);
    }

    /// <summary>Verifies a control revision change during a provider read discards the page.</summary>
    [Fact]
    public async Task Query_ControlRevisionChangesDuringRead_DiscardsPage()
    {
        var decisions = new FakeGraphDecisionStore();
        var store = ReadyStore();
        store.BeforeNodePageReturn = () => decisions.ControlSettings = decisions.ControlSettings with
        {
            ResourceMode = OpenSorSe.Core.Configuration.IndexingResourceMode.Eco,
            MaximumConcurrency = 1,
            Revision = decisions.ControlSettings.Revision + 1,
        };
        var service = new GraphQueryService(store, new FakeGraphProjectionSource(), decisions);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.GetNodesPageAsync(new GraphNodeQuery()));

        Assert.Equal("authority-changed-during-read", error.ReasonCode);
    }

    /// <summary>Verifies fact and timeline pages expose retained mechanical facts under the same read fence.</summary>
    [Fact]
    public async Task Query_FactsAndTimeline_ReturnBoundedEvidenceBackedRecords()
    {
        var store = ReadyStore();
        store.Facts.Add(new GraphFact("fact-size", "file-1", GraphFactKind.FileSize, "42", ["evidence-1"], "1.0.0"));
        store.Timeline.Add(new GraphTimelineEntry(
            "fact-modified",
            "file-1",
            GraphFactKind.ModifiedTimestamp,
            TestGraphData.Now,
            ["evidence-1"],
            "1.0.0"));
        var service = new GraphQueryService(store, new FakeGraphProjectionSource(), new FakeGraphDecisionStore());

        var facts = await service.GetFactsPageAsync(new GraphFactQuery("file-1"));
        var timeline = await service.GetTimelinePageAsync(new GraphTimelineQuery("file-1"));

        Assert.Equal("42", Assert.Single(facts.Items).CanonicalValue);
        Assert.Equal(TestGraphData.Now, Assert.Single(timeline.Items).OccurredAtUtc);
    }

    /// <summary>Verifies ordinary Search can fall back when graph source projection is behind.</summary>
    [Fact]
    public async Task Search_SourceWatermarkBehind_ReturnsUnavailableFallback()
    {
        var source = new FakeGraphProjectionSource
        {
            Authority = new GraphAuthoritySnapshot(true, true, 1, "legacy-1", "allowed")
            {
                CurrentSourceManifestId = "manifest-2",
                CurrentSourceRevision = 2,
            },
        };
        var service = new GraphSearchSource(new FakeGraphStore(), source, new FakeGraphDecisionStore());

        var result = await service.ExpandAsync(new GraphSearchRequest(["file-1"]));

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Expansions);
    }

    /// <summary>Verifies Search expansion follows authoritative control rather than a lagging derived flag.</summary>
    [Fact]
    public async Task Search_AuthoritativeControlDisabled_ReturnsUnavailableFallback()
    {
        var decisions = new FakeGraphDecisionStore { ControlSettings = new GraphControlSettings() };
        var result = await new GraphSearchSource(ReadyStore(), new FakeGraphProjectionSource(), decisions)
            .ExpandAsync(new GraphSearchRequest(["file-1"]));

        Assert.False(result.IsAvailable);
        Assert.Contains("graph-disabled", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies Search drops expansions when source authority changes during the provider read.</summary>
    [Fact]
    public async Task Search_AuthorityChangesDuringRead_DiscardsExpansions()
    {
        var source = new FakeGraphProjectionSource();
        var store = ReadyStore();
        store.SearchExpansions.Add(new GraphSearchExpansion(
            "file-1",
            "file-2",
            "edge-1",
            GraphEdgeKind.RelatedFile,
            GraphConfidenceLevel.High,
            "Shared exact metadata.",
            1,
            GraphFreshnessState.Current));
        store.BeforeSearchReturn = () =>
        {
            source.Authority = source.Authority with { PrivacySequence = 2 };
            store.Coverage = store.Coverage with { AppliedPrivacySequence = 2 };
        };
        var service = new GraphSearchSource(store, source, new FakeGraphDecisionStore());

        var result = await service.ExpandAsync(new GraphSearchRequest(["file-1"]));

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Expansions);
    }

    /// <summary>Verifies privacy inspection also revalidates source and privacy authority after reading.</summary>
    [Fact]
    public async Task PrivacyInspect_AuthorityChangesDuringRead_DiscardsInspection()
    {
        var source = new FakeGraphProjectionSource();
        var store = ReadyStore();
        store.BeforePrivacyInspectionReturn = () =>
        {
            source.Authority = source.Authority with { PrivacySequence = 2 };
            store.Coverage = store.Coverage with { AppliedPrivacySequence = 2 };
        };
        var service = new GraphPrivacyService(
            store,
            new FakeGraphDecisionStore(),
            source,
            new FakeGraphReconciliationSignal(),
            new FixedGraphTimeProvider(TestGraphData.Now));

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.InspectAsync(new GraphPrivacyScope(GraphPrivacyScopeKind.File, "file-1")));

        Assert.Equal("authority-changed-during-read", error.ReasonCode);
    }

    /// <summary>Verifies privacy inspection is disabled while graph-only forgetting remains available.</summary>
    [Fact]
    public async Task PrivacyInspect_AuthoritativeControlDisabled_FailsClosed()
    {
        var decisions = new FakeGraphDecisionStore { ControlSettings = new GraphControlSettings() };
        var service = new GraphPrivacyService(
            ReadyStore(),
            decisions,
            new FakeGraphProjectionSource(),
            new FakeGraphReconciliationSignal(),
            new FixedGraphTimeProvider(TestGraphData.Now));

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.InspectAsync(new GraphPrivacyScope(GraphPrivacyScopeKind.File, "file-1")));

        Assert.Equal("graph-disabled", error.ReasonCode);
    }

    /// <summary>Verifies both destructive clear workflows require explicit re-enable after completion.</summary>
    [Theory]
    [InlineData(GraphPrivacyAction.ClearAllDerivedData, null)]
    [InlineData(GraphPrivacyAction.ClearAllDecisions, "CLEAR GRAPH DECISIONS")]
    public async Task PrivacyClear_DisablesGraphUntilExplicitReenable(GraphPrivacyAction action, string? confirmation)
    {
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        var service = new GraphPrivacyService(
            store,
            decisions,
            new FakeGraphProjectionSource(),
            new FakeGraphReconciliationSignal(),
            new FixedGraphTimeProvider(TestGraphData.Now));

        await service.ApplyAsync(new GraphPrivacyChange(
            new GraphPrivacyScope(GraphPrivacyScopeKind.All, string.Empty),
            action,
            ConfirmSourceFilesUnaffected: true,
            ConfirmationText: confirmation));

        Assert.False(decisions.ControlSettings.IsEnabled);
        Assert.False(store.Enabled);
    }

    /// <summary>Verifies forgetting is authoritative, graph-only, and schedules reconciliation.</summary>
    [Fact]
    public async Task PrivacyForget_AppendsDecisionAppliesDerivedChangeAndSignals()
    {
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        store.Nodes.Add(Node("file-1", GraphNodeKind.File, GraphOrigin.Mechanical));
        var signal = new FakeGraphReconciliationSignal();
        var service = new GraphPrivacyService(
            store,
            decisions,
            new FakeGraphProjectionSource(),
            signal,
            new FixedGraphTimeProvider(TestGraphData.Now));

        var result = await service.ApplyAsync(new GraphPrivacyChange(
            new GraphPrivacyScope(GraphPrivacyScopeKind.File, "file-1"),
            GraphPrivacyAction.ForgetDerivedData,
            ConfirmSourceFilesUnaffected: true));

        Assert.True(result.Succeeded);
        Assert.Equal(GraphDecisionKind.Forget, Assert.Single(decisions.Entries).Command.Kind);
        Assert.Empty(store.Nodes);
        Assert.Equal(1, signal.Count);
    }

    /// <summary>Verifies selective repair observes a source change and never reports a stale mutation as final.</summary>
    [Fact]
    public async Task Repair_AuthorityChangesDuringMutation_FailsClosedAndDoesNotSignal()
    {
        var source = new FakeGraphProjectionSource();
        var store = new FakeGraphStore();
        store.BeforeRepairReturn = () => source.Authority = source.Authority with
        {
            CurrentSourceManifestId = "manifest-2",
            CurrentSourceRevision = 2,
        };
        var signal = new FakeGraphReconciliationSignal();
        var service = new GraphRepairService(
            store,
            source,
            new FakeGraphDecisionStore(),
            signal,
            new FixedGraphTimeProvider(TestGraphData.Now));

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() => service.ExecuteAsync(
            new GraphRepairRequest(GraphRepairKind.ReprojectFile, "file-1", ConfirmSourceFilesUnaffected: true)));

        Assert.Equal("authority-changed-during-repair", error.ReasonCode);
        Assert.Equal(0, signal.Count);
    }

    /// <summary>Verifies a provider-classified derived failure cannot make a durable decision appear rejected.</summary>
    [Fact]
    public async Task Decision_DerivedProviderFailure_ReportsAuthoritativePendingOutcome()
    {
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore
        {
            InvalidateDecisionFailure = new GraphPersistenceException("database-busy", "Synthetic provider failure."),
        };
        var signal = new FakeGraphReconciliationSignal();
        var service = DecisionService(decisions, store, signal);

        var result = await service.CreateManualEntityAsync("manual:alpha", "Alpha");

        Assert.True(result.Succeeded);
        Assert.Contains("authoritative", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(decisions.Entries);
        Assert.Equal(1, signal.Count);
    }

    /// <summary>Verifies a decision checkpoint change between semantic validation and append is rejected.</summary>
    [Fact]
    public async Task Decision_CheckpointChangesBeforeAppend_FailsClosed()
    {
        var decisions = new FakeGraphDecisionStore();
        decisions.SnapshotReadHook = readCount =>
        {
            if (readCount == 3)
            {
                decisions.AppendAsync(new GraphDecisionCommand
                {
                    Kind = GraphDecisionKind.RejectSuggestion,
                    SubjectId = "suggestion:concurrent",
                    ExpectedSequence = 0,
                }, TestGraphData.Now).GetAwaiter().GetResult();
            }
        };
        var service = DecisionService(decisions, new FakeGraphStore(), new FakeGraphReconciliationSignal());

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.CreateManualEntityAsync("manual:alpha", "Alpha"));

        Assert.Equal("decision-checkpoint-changed", error.ReasonCode);
        Assert.Single(decisions.Entries);
    }

    /// <summary>Verifies disabling during semantic validation prevents an authoritative append.</summary>
    [Fact]
    public async Task Decision_DisabledDuringSemanticRead_DoesNotAppend()
    {
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        store.BeforeNodeDetailReturn = () => decisions.ControlSettings = decisions.ControlSettings with
        {
            IsEnabled = false,
            Revision = decisions.ControlSettings.Revision + 1,
        };
        var service = DecisionService(decisions, store, new FakeGraphReconciliationSignal());

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.CreateManualEntityAsync("manual:alpha", "Alpha"));

        Assert.Equal("graph-disabled", error.ReasonCode);
        Assert.Empty(decisions.Entries);
    }

    /// <summary>Verifies a source exclusion during semantic validation prevents a stale decision append.</summary>
    [Fact]
    public async Task Decision_SourceExcludedDuringSemanticRead_DoesNotAppend()
    {
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        var source = new FakeGraphProjectionSource();
        store.Coverage = ReadyStore().Coverage;
        store.BeforeNodeDetailReturn = () => source.Authority = source.Authority with
        {
            IsAllowed = false,
            ReasonCode = "scope-excluded",
        };
        var service = new GraphDecisionService(
            decisions,
            store,
            new ConservativeGraphIdentityResolver(),
            new FakeGraphReconciliationSignal(),
            new FixedGraphTimeProvider(TestGraphData.Now),
            source);

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.CreateManualEntityAsync("manual:alpha", "Alpha"));

        Assert.Equal("scope-excluded", error.ReasonCode);
        Assert.Empty(decisions.Entries);
    }

    /// <summary>Verifies mechanical identities cannot be renamed through manual-entity commands.</summary>
    [Fact]
    public async Task Decision_RenameMechanicalNode_IsRejected()
    {
        var store = new FakeGraphStore();
        store.Nodes.Add(Node("file-1", GraphNodeKind.File, GraphOrigin.Mechanical));
        var service = DecisionService(new FakeGraphDecisionStore(), store, new FakeGraphReconciliationSignal());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameManualEntityAsync("file-1", "Unsafe rename"));
    }

    /// <summary>Verifies hidden or missing endpoints cannot receive manual edges.</summary>
    [Fact]
    public async Task Decision_LinkHiddenEndpoint_IsRejected()
    {
        var store = new FakeGraphStore();
        store.Nodes.Add(Node("manual:alpha", GraphNodeKind.ManualEntity, GraphOrigin.Manual));
        store.Nodes.Add(Node("file-hidden", GraphNodeKind.File, GraphOrigin.Mechanical) with { IsVisible = false });
        var service = DecisionService(new FakeGraphDecisionStore(), store, new FakeGraphReconciliationSignal());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkAsync("manual:alpha", "file-hidden", "manual-link"));
    }

    /// <summary>Verifies durable suppression captures endpoints, relationship kind, and ownership scope.</summary>
    [Fact]
    public async Task Decision_NeverMerge_CapturesStableRelationshipIdentity()
    {
        var sourceNode = Node("file-1", GraphNodeKind.File, GraphOrigin.Mechanical) with { OwningSourceId = "source-1" };
        var targetNode = Node("file-2", GraphNodeKind.File, GraphOrigin.Mechanical) with { OwningSourceId = "source-1" };
        var edge = new GraphEdge
        {
            Id = "edge-1",
            Kind = GraphEdgeKind.RelatedFile,
            SourceNodeId = sourceNode.Identity.NodeId,
            TargetNodeId = targetNode.Identity.NodeId,
            Confidence = GraphConfidenceLevel.High,
            Origin = GraphOrigin.Mechanical,
            Algorithm = "synthetic",
            AlgorithmVersion = "1.0.0",
            InputFingerprint = "fingerprint-1",
            CreatedAtUtc = TestGraphData.Now,
            LastValidatedAtUtc = TestGraphData.Now,
        };
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        store.Nodes.AddRange([sourceNode, targetNode]);
        store.Neighbors.Add(new GraphNeighbor(targetNode, edge, []));
        var service = DecisionService(decisions, store, new FakeGraphReconciliationSignal());

        await service.UnlinkAsync(edge.Id, preventRegeneration: true);

        var command = Assert.Single(decisions.Entries).Command;
        Assert.Equal(GraphDecisionKind.NeverMerge, command.Kind);
        Assert.Equal(sourceNode.Identity.NodeId, command.RelationshipSourceNodeId);
        Assert.Equal(targetNode.Identity.NodeId, command.RelationshipTargetNodeId);
        Assert.Equal(GraphEdgeKind.RelatedFile, command.RelationshipKind);
        Assert.Equal("source-1", command.RelationshipScope);
    }

    /// <summary>Verifies projected v1.9 relationships commit through legacy authority without a second graph decision.</summary>
    [Fact]
    public async Task Decision_UnlinkLegacyRelationship_WritesThroughBeforeGraphReconciliation()
    {
        var first = Node("node-file-1", GraphNodeKind.File, GraphOrigin.Mechanical) with
        {
            Identity = new GraphIdentity("node-file-1", GraphNodeKind.File, "file", "file-1", "existing-id-v1", "file-1"),
            OwningSourceId = "source-1",
        };
        var second = Node("node-file-2", GraphNodeKind.File, GraphOrigin.Mechanical) with
        {
            Identity = new GraphIdentity("node-file-2", GraphNodeKind.File, "file", "file-2", "existing-id-v1", "file-2"),
            OwningSourceId = "source-1",
        };
        var edge = Edge("edge-legacy", first.Identity.NodeId, second.Identity.NodeId, GraphEdgeKind.RelatedFile, GraphOrigin.LegacyRelationship);
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        store.Nodes.AddRange([first, second]);
        store.Neighbors.Add(new GraphNeighbor(second, edge, []));
        var signal = new FakeGraphReconciliationSignal();
        var bridge = new FakeGraphLegacyAuthorityBridge();
        var service = DecisionService(decisions, store, signal, bridge);

        var result = await service.UnlinkAsync(edge.Id, preventRegeneration: true);

        Assert.True(result.Succeeded);
        Assert.Equal(("file-1", "file-2", true), Assert.Single(bridge.RelationshipUnlinks));
        Assert.Empty(bridge.CollectionSplits);
        Assert.Empty(decisions.Entries);
        Assert.Equal(1, signal.Count);
        Assert.Contains("reconciliation was scheduled", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies projected Smart Collection membership changes retain v1.9 as the only authority.</summary>
    [Fact]
    public async Task Decision_UnlinkLegacyCollectionMembership_WritesThroughToSplitOverride()
    {
        var file = Node("node-file-1", GraphNodeKind.File, GraphOrigin.Mechanical) with
        {
            Identity = new GraphIdentity("node-file-1", GraphNodeKind.File, "file", "file-1", "existing-id-v1", "file-1"),
            OwningSourceId = "source-1",
        };
        var collection = Node("node-collection-1", GraphNodeKind.Collection, GraphOrigin.LegacyCollection) with
        {
            Identity = new GraphIdentity("node-collection-1", GraphNodeKind.Collection, "collection", "collection-1", "existing-id-v1", "collection-1"),
        };
        var edge = Edge("edge-membership", file.Identity.NodeId, collection.Identity.NodeId, GraphEdgeKind.MemberOf, GraphOrigin.LegacyCollection);
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        store.Nodes.AddRange([file, collection]);
        store.Neighbors.Add(new GraphNeighbor(collection, edge, []));
        var signal = new FakeGraphReconciliationSignal();
        var bridge = new FakeGraphLegacyAuthorityBridge();
        var service = DecisionService(decisions, store, signal, bridge);

        var result = await service.UnlinkAsync(edge.Id, preventRegeneration: true);

        Assert.True(result.Succeeded);
        Assert.Equal(("collection-1", "file-1"), Assert.Single(bridge.CollectionSplits));
        Assert.Empty(bridge.RelationshipUnlinks);
        Assert.Empty(decisions.Entries);
        Assert.Equal(1, signal.Count);
    }

    /// <summary>Verifies an unavailable legacy bridge fails closed instead of appending a graph-native command.</summary>
    [Fact]
    public async Task Decision_UnlinkLegacyRelationshipWithoutBridge_FailsClosed()
    {
        var first = Node("file-1", GraphNodeKind.File, GraphOrigin.Mechanical);
        var second = Node("file-2", GraphNodeKind.File, GraphOrigin.Mechanical);
        var edge = Edge("edge-legacy", first.Identity.NodeId, second.Identity.NodeId, GraphEdgeKind.RelatedFile, GraphOrigin.LegacyRelationship);
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        store.Nodes.AddRange([first, second]);
        store.Neighbors.Add(new GraphNeighbor(second, edge, []));
        var service = DecisionService(decisions, store, new FakeGraphReconciliationSignal());

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            service.UnlinkAsync(edge.Id, preventRegeneration: true));

        Assert.Equal("legacy-authority-bridge-unconfigured", error.ReasonCode);
        Assert.Empty(decisions.Entries);
    }

    /// <summary>Verifies a committed legacy correction remains successful if graph refresh signaling fails.</summary>
    [Fact]
    public async Task Decision_LegacyCommitWithRefreshFailure_RemainsAuthoritativeAndPending()
    {
        var first = Node("file-1", GraphNodeKind.File, GraphOrigin.Mechanical);
        var second = Node("file-2", GraphNodeKind.File, GraphOrigin.Mechanical);
        var edge = Edge("edge-legacy", first.Identity.NodeId, second.Identity.NodeId, GraphEdgeKind.RelatedFile, GraphOrigin.LegacyRelationship);
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        store.Nodes.AddRange([first, second]);
        store.Neighbors.Add(new GraphNeighbor(second, edge, []));
        var signal = new FakeGraphReconciliationSignal { ExceptionToThrow = new IOException("synthetic") };
        var bridge = new FakeGraphLegacyAuthorityBridge();
        var service = DecisionService(decisions, store, signal, bridge);

        var result = await service.UnlinkAsync(edge.Id, preventRegeneration: true);

        Assert.True(result.Succeeded);
        Assert.Single(bridge.RelationshipUnlinks);
        Assert.Empty(decisions.Entries);
        Assert.Equal(1, signal.Count);
        Assert.Contains("reconciliation remains pending", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies the generic Apply API cannot bypass legacy ownership with a crafted command.</summary>
    [Fact]
    public async Task Decision_DirectApplyForLegacyEdge_IsRejectedWithoutAppending()
    {
        var first = Node("file-1", GraphNodeKind.File, GraphOrigin.Mechanical);
        var second = Node("file-2", GraphNodeKind.File, GraphOrigin.Mechanical);
        var edge = Edge("edge-legacy", first.Identity.NodeId, second.Identity.NodeId, GraphEdgeKind.RelatedFile, GraphOrigin.LegacyRelationship);
        var decisions = new FakeGraphDecisionStore();
        var store = new FakeGraphStore();
        store.Nodes.AddRange([first, second]);
        store.Neighbors.Add(new GraphNeighbor(second, edge, []));
        var service = DecisionService(decisions, store, new FakeGraphReconciliationSignal(), new FakeGraphLegacyAuthorityBridge());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(new GraphDecisionCommand
        {
            Kind = GraphDecisionKind.NeverMerge,
            SubjectId = edge.Id,
            RelationshipSourceNodeId = edge.SourceNodeId,
            RelationshipTargetNodeId = edge.TargetNodeId,
            RelationshipKind = edge.Kind,
            RelationshipScope = "source-1",
            ExpectedSequence = 0,
        }));

        Assert.Empty(decisions.Entries);
    }

    /// <summary>Verifies diagnostics expose bounded versions, stage timing, history, storage, and maintenance only.</summary>
    [Fact]
    public async Task Diagnostics_ValidRedactedAggregates_AreReturned()
    {
        var store = new FakeGraphStore
        {
            DiagnosticsOverride = new GraphDiagnosticsSnapshot
            {
                SchemaVersion = 4,
                ProviderCode = "sqlite-graph",
                AlgorithmVersions = ["stable-graph-1.0.0"],
                StageDurations = [new GraphStageDurationAggregate("component-validated", 2, TimeSpan.FromMilliseconds(8), TimeSpan.FromMilliseconds(5))],
                OperationalHistory = new GraphOperationalHistorySummary
                {
                    BoundEventCount = 1,
                    RecoveryCount = 2,
                    LastEventAtUtc = TestGraphData.Now,
                },
                StorageBreakdown = GraphStorageBreakdown.Empty,
                Maintenance = new GraphMaintenanceStatus(false, false, TestGraphData.Now, 3, "retention-complete"),
                Coverage = ReadyStore().Coverage,
            },
        };

        var result = await new GraphDiagnosticsService(store).GetSnapshotAsync();

        Assert.Equal(4, result.SchemaVersion);
        Assert.Equal(2, Assert.Single(result.StageDurations).InvocationCount);
        Assert.Equal(2, result.OperationalHistory.RecoveryCount);
    }

    /// <summary>Verifies malformed diagnostic counts never cross the Application boundary.</summary>
    [Fact]
    public async Task Diagnostics_NegativeOperationalHistory_FailsClosed()
    {
        var store = new FakeGraphStore
        {
            DiagnosticsOverride = new GraphDiagnosticsSnapshot
            {
                OperationalHistory = new GraphOperationalHistorySummary { RepairCount = -1 },
                Coverage = ReadyStore().Coverage,
            },
        };

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            new GraphDiagnosticsService(store).GetSnapshotAsync());

        Assert.Equal("graph-diagnostics-invalid", error.ReasonCode);
    }

    /// <summary>Verifies stale or confirmed suggestions cannot be rejected again.</summary>
    [Fact]
    public async Task Decision_ConfirmedSuggestion_IsRejectedAsStale()
    {
        var store = new FakeGraphStore();
        store.Mentions.Add(new GraphMention
        {
            Id = "suggestion:alpha",
            Kind = GraphSuggestionKind.Project,
            SourceStableKey = "file-1",
            Scope = "source-1",
            Label = "Alpha",
            NormalizedKey = "ALPHA",
            ExtractorVersion = "test-v1",
            EvidenceIds = ["evidence-1"],
            IsConfirmed = true,
        });
        var service = DecisionService(new FakeGraphDecisionStore(), store, new FakeGraphReconciliationSignal());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RejectSuggestionAsync("suggestion:alpha"));
    }

    /// <summary>Verifies create/rename/alias/link survive a derived rebuild, then unlink and forget suppress them.</summary>
    [Fact]
    public async Task Decisions_ReplaySurvivesDerivedRebuild_ThenUnlinkAndForgetSuppress()
    {
        var source = new FakeGraphProjectionSource();
        var store = new FakeGraphStore();
        var decisions = new FakeGraphDecisionStore();
        var signal = new FakeGraphReconciliationSignal();
        var service = DecisionService(decisions, store, signal);
        await using var coordinator = Coordinator(source, store, decisions);
        await coordinator.InitializeAsync();

        await service.CreateManualEntityAsync("manual:alpha", "Alpha");
        await coordinator.ReconcileAsync();
        await service.CreateManualEntityAsync("manual:beta", "Beta");
        await coordinator.ReconcileAsync();
        await service.RenameManualEntityAsync("manual:alpha", "Alpha renamed");
        await coordinator.ReconcileAsync();
        await service.AddAliasAsync("manual:alpha", "Project Alpha");
        await coordinator.ReconcileAsync();
        await service.RemoveAliasAsync("manual:alpha", "Project Alpha");
        await coordinator.ReconcileAsync();
        Assert.Empty(store.Aliases);
        await service.AddAliasAsync("manual:alpha", "Project Alpha");
        await coordinator.ReconcileAsync();
        await service.LinkAsync("manual:alpha", "manual:beta", "Explicit user link");
        await coordinator.ReconcileAsync();

        Assert.Equal("Alpha renamed", Assert.Single(store.Nodes, item => item.Identity.NodeId == "manual:alpha").DisplayLabel);
        Assert.Contains(store.Aliases, item => item.NodeId == "manual:alpha" && item.Label == "Project Alpha");
        var edgeId = Assert.Single(store.Neighbors).Edge.Id;

        store.ResetDerivedGraph();
        await coordinator.ReconcileAsync();

        Assert.Equal(2, store.Nodes.Count);
        Assert.Single(store.Aliases);
        Assert.Single(store.Neighbors);

        await service.UnlinkAsync(edgeId, preventRegeneration: true);
        await coordinator.ReconcileAsync();
        Assert.Empty(store.Neighbors);

        var privacy = new GraphPrivacyService(store, decisions, source, signal, new FixedGraphTimeProvider(TestGraphData.Now));
        await privacy.ApplyAsync(new GraphPrivacyChange(
            new GraphPrivacyScope(GraphPrivacyScopeKind.Node, "manual:alpha"),
            GraphPrivacyAction.ForgetDerivedData,
            ConfirmSourceFilesUnaffected: true));
        Assert.DoesNotContain(store.Nodes, item => item.Identity.NodeId == "manual:alpha");
        Assert.Contains(store.Nodes, item => item.Identity.NodeId == "manual:beta");
    }

    /// <summary>Verifies an identical source and decision checkpoint performs no duplicate decision projection.</summary>
    [Fact]
    public async Task DecisionReplay_IdenticalCheckpoint_IsNoOp()
    {
        var decisions = new FakeGraphDecisionStore();
        await decisions.AppendAsync(new GraphDecisionCommand
        {
            Kind = GraphDecisionKind.CreateManualEntity,
            SubjectId = "manual:alpha",
            Label = "Alpha",
            NodeKind = GraphNodeKind.ManualEntity,
            ExpectedSequence = 0,
        }, TestGraphData.Now);
        var store = new FakeGraphStore();
        await using var coordinator = Coordinator(new FakeGraphProjectionSource(), store, decisions);
        await coordinator.InitializeAsync();

        await coordinator.ReconcileAsync();
        var firstCount = store.DecisionProjections.Count;
        await coordinator.ReconcileAsync();

        Assert.Equal(1, firstCount);
        Assert.Equal(firstCount, store.DecisionProjections.Count);
    }

    /// <summary>Verifies interrupted decision-ledger paging resumes from the durable checkpoint.</summary>
    [Fact]
    public async Task DecisionReplay_InterruptedPage_ResumesWithoutDuplicates()
    {
        var decisions = new FakeGraphDecisionStore();
        for (var index = 0; index < 125; index++)
        {
            await decisions.AppendAsync(new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.RejectSuggestion,
                SubjectId = string.Concat("suggestion:", index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)),
                ExpectedSequence = index,
            }, TestGraphData.Now);
        }

        var store = new FakeGraphStore { FailNextDecisionProjectionPageAfterApply = true };
        await using var coordinator = Coordinator(new FakeGraphProjectionSource(), store, decisions);
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<GraphPersistenceException>(() => coordinator.ReconcileAsync());
        Assert.Equal(100, store.DecisionProjections.Count);

        var result = await coordinator.ReconcileAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(125, store.DecisionProjections.Count);
        Assert.Equal(125, store.DecisionProjections.Select(item => item.Decision.DecisionId).Distinct(StringComparer.Ordinal).Count());
    }

    private static GraphDecisionService DecisionService(
        FakeGraphDecisionStore decisions,
        FakeGraphStore store,
        FakeGraphReconciliationSignal signal,
        IGraphLegacyAuthorityBridge? legacyAuthorityBridge = null)
    {
        store.Coverage = store.Coverage with
        {
            IsEnabled = true,
            IsAvailable = true,
            IsStale = false,
            AppliedManifestId = "manifest-1",
            AppliedRevision = 1,
            AppliedDecisionSequence = decisions.Entries.Count,
            AppliedDecisionCheckpointId = string.Concat("checkpoint-0-", decisions.Entries.Count),
            AppliedPrivacySequence = 1,
        };
        return new GraphDecisionService(
            decisions,
            store,
            new ConservativeGraphIdentityResolver(),
            signal,
            new FixedGraphTimeProvider(TestGraphData.Now),
            new FakeGraphProjectionSource(),
            legacyAuthorityBridge: legacyAuthorityBridge);
    }

    private static FakeGraphStore ReadyStore() => new()
    {
        Coverage = TestGraphData.Coverage with
        {
            IngestedManifestId = "manifest-1",
            IngestedRevision = 1,
            AppliedManifestId = "manifest-1",
            AppliedRevision = 1,
        },
    };

    private static GraphProjectionCoordinator Coordinator(
        FakeGraphProjectionSource source,
        FakeGraphStore store,
        FakeGraphDecisionStore decisions) => new(
            source,
            store,
            decisions,
            new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()),
            new DeterministicGraphDecisionProjectionBuilder(new ConservativeGraphIdentityResolver()),
            new FakeGraphResourcePolicy(),
            new FixedGraphTimeProvider(TestGraphData.Now),
            "service-test-owner");

    private static GraphNode Node(string id, GraphNodeKind kind, GraphOrigin origin) => new()
    {
        Identity = new GraphIdentity(id, kind, kind.Value, id, "test-v1", id),
        DisplayLabel = id,
        Origin = origin,
        SourceManifestId = "manifest-1",
        ObservationHash = "observation-hash",
        Algorithm = "synthetic",
        AlgorithmVersion = "1.0.0",
        CreatedAtUtc = TestGraphData.Now,
        LastValidatedAtUtc = TestGraphData.Now,
    };

    private static GraphEdge Edge(
        string id,
        string sourceNodeId,
        string targetNodeId,
        GraphEdgeKind kind,
        GraphOrigin origin) => new()
        {
            Id = id,
            Kind = kind,
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            Confidence = GraphConfidenceLevel.High,
            Origin = origin,
            Algorithm = "synthetic",
            AlgorithmVersion = "1.0.0",
            InputFingerprint = "fingerprint-1",
            CreatedAtUtc = TestGraphData.Now,
            LastValidatedAtUtc = TestGraphData.Now,
        };
}
