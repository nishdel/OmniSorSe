using System.ComponentModel;
using OpenSorSe.Application.Explorer;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Validates bounded Knowledge Graph presentation, control, recovery, and privacy behavior.</summary>
public sealed class KnowledgeGraphViewModelTests
{
    /// <summary>The Related Files surface exposes a bounded optional-companion launch with honest status.</summary>
    [Fact]
    public async Task OpenInOmniBrille_UsesLaunchServiceAndPresentsItsOutcome()
    {
        var companion = new FakeCompanionLaunchService(new ExplorerCompanionLaunchResult(
            ExplorerCompanionLaunchStatus.Connected,
            "OmniBrille connected to the authorized local index.",
            "launch-1",
            "session-1"));
        using var viewModel = Create(
            new FakeCoordinator(Status(enabled: true)),
            new FakeQueryService(),
            companion: companion);

        Assert.True(viewModel.OpenInOmniBrilleCommand.CanExecute(null));
        await viewModel.OpenInOmniBrilleCommand.ExecuteAsync(null);

        Assert.Equal(1, companion.LaunchCount);
        Assert.False(viewModel.IsCompanionLaunching);
        Assert.Contains("connected", viewModel.CompanionStatusText, StringComparison.OrdinalIgnoreCase);
        var source = ReadViewSource();
        Assert.Contains("Open in OmniBrille", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies a missing provider leaves an honest no-op surface and existing shell navigation intact.</summary>
    [Fact]
    public async Task UnavailableProvider_IsSafeAndGraphDiagnosticsRemainAdvanced()
    {
        using var graph = new KnowledgeGraphViewModel();

        await graph.RefreshAsync();

        Assert.False(graph.IsEnabled);
        Assert.True(graph.ShowEnablement);
        Assert.False(graph.RequestEnableCommand.CanExecute(null));
        Assert.Contains("unavailable", graph.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(graph.Nodes);

        using var shell = new MainViewModel();
        await shell.NavigateAsync(NavigationDestination.KnowledgeGraph);
        Assert.Equal(NavigationDestination.Dashboard, shell.SelectedDestination);
        Assert.Equal("Home", shell.CurrentPageTitle);
        Assert.False(shell.IsKnowledgeGraphSelected);
        Assert.DoesNotContain(shell.NavigationItems, item =>
            item.Destination == NavigationDestination.KnowledgeGraph);
    }

    /// <summary>Verifies first enablement requires explicit consent before the coordinator is invoked.</summary>
    [Fact]
    public async Task Enablement_RequiresExplicitConfirmationAndReportsIndependentState()
    {
        var coordinator = new FakeCoordinator(Status(enabled: false));
        var query = new FakeQueryService();
        using var viewModel = Create(coordinator, query);

        viewModel.RequestEnableCommand.Execute(null);

        Assert.True(viewModel.IsEnableConfirmationPending);
        Assert.Equal(0, coordinator.EnableCount);
        Assert.Equal(KnowledgeGraphFocusTarget.EnableControl, viewModel.LastFocusRequest?.Target);

        await viewModel.ConfirmEnableCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.EnableCount);
        Assert.Equal(1, coordinator.ReconcileCount);
        Assert.True(coordinator.LastConsentConfirmed);
        Assert.True(viewModel.IsEnabled);
        Assert.True(viewModel.ShowGraphWorkspace);
        Assert.False(viewModel.IsEnableConfirmationPending);
        Assert.Contains("original", ReadViewSource(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Disabled but retained sidecars keep their privacy and clear controls available without reenabling processing.</summary>
    [Fact]
    public async Task DisabledRetainedGraph_KeepsPrivacyManagementVisible()
    {
        var status = Status(enabled: false) with
        {
            StorageBreakdown = new GraphStorageBreakdown
            {
                DerivedStoreBytes = 1024,
                DecisionLedgerBytes = 512,
                TotalBytes = 1536,
                MaximumBytes = 16L * 1024L * 1024L,
                RequiredReserveBytes = 1024,
                IsInventoryVerified = true,
            },
        };
        using var viewModel = Create(
            new FakeCoordinator(status),
            new FakeQueryService(),
            privacy: new FakePrivacyService());

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.ShowEnablement);
        Assert.True(viewModel.ShowGraphWorkspace);
        Assert.True(viewModel.RequestClearDerivedCommand.CanExecute(null));
        Assert.Contains("decision ledger", viewModel.StorageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies refresh-view stays read-only while explicit reconciliation projects indexed data.</summary>
    [Fact]
    public async Task Reconcile_IsExplicitCancellableProjectionActionSeparateFromViewRefresh()
    {
        var coordinator = new FakeCoordinator(Status(enabled: true));
        using var viewModel = Create(coordinator, new FakeQueryService());

        await viewModel.RefreshAsync();

        Assert.Equal(0, coordinator.ReconcileCount);
        Assert.True(viewModel.ReconcileCommand.CanExecute(null));

        await viewModel.ReconcileCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.ReconcileCount);
        Assert.Contains("Reconciled", viewModel.Status.Message, StringComparison.Ordinal);
        Assert.Contains("Update graph now", ReadViewSource(), StringComparison.Ordinal);
    }

    /// <summary>Verifies resume and retry transitions continue the durable queue through reconciliation.</summary>
    [Fact]
    public async Task ResumeAndRetry_ContinueDurableProjectionWork()
    {
        var pausedCoordinator = new FakeCoordinator(Status(enabled: true, run: GraphRunControlState.Paused));
        using (var pausedViewModel = Create(pausedCoordinator, new FakeQueryService()))
        {
            await pausedViewModel.RefreshAsync();
            Assert.True(pausedViewModel.ResumeCommand.CanExecute(null));

            await pausedViewModel.ResumeCommand.ExecuteAsync(null);

            Assert.Equal(1, pausedCoordinator.ResumeCount);
            Assert.Equal(1, pausedCoordinator.ReconcileCount);
        }

        var retryCoordinator = new FakeCoordinator(Status(enabled: true, run: GraphRunControlState.Cancelled) with
        {
            RetryableFailureCount = 1,
        });
        using var retryViewModel = Create(retryCoordinator, new FakeQueryService());
        await retryViewModel.RefreshAsync();
        Assert.True(retryViewModel.RetryCommand.CanExecute(null));

        await retryViewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(1, retryCoordinator.RetryCount);
        Assert.Equal(1, retryCoordinator.ReconcileCount);
    }

    /// <summary>Verifies all four durable axes, progress, coverage, storage, and estimates remain independent.</summary>
    [Fact]
    public async Task Refresh_PresentsFourAxesProgressCoverageAndStorage()
    {
        var status = Status(
            enabled: true,
            run: GraphRunControlState.PauseRequested,
            job: GraphJobExecutionState.WaitingForDependency,
            freshness: GraphFreshnessState.Stale,
            integrity: GraphIntegrityState.RepairRequired,
            processed: 25,
            total: 100,
            storage: 4 * 1024 * 1024,
            maximumStorage: 16 * 1024 * 1024) with
        {
            CurrentStage = "edge-publication",
            CurrentWorkLabel = "bounded component",
            EstimatedRemaining = TimeSpan.FromMinutes(3),
            WaitingCount = 2,
        };
        var coordinator = new FakeCoordinator(status);
        using var viewModel = Create(coordinator, new FakeQueryService());

        await viewModel.RefreshAsync();

        Assert.Contains("Pause requested", viewModel.RunStateText, StringComparison.Ordinal);
        Assert.Contains("dependency", viewModel.JobStateText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stale", viewModel.FreshnessText, StringComparison.Ordinal);
        Assert.Contains("Repair required", viewModel.IntegrityText, StringComparison.Ordinal);
        Assert.Equal(0.25, viewModel.ProgressValue, 3);
        Assert.Contains("25", viewModel.ProgressText, StringComparison.Ordinal);
        Assert.Contains("Estimated", viewModel.ProgressText, StringComparison.Ordinal);
        Assert.Contains("25", viewModel.CoverageText, StringComparison.Ordinal);
        Assert.Contains("4.0 MiB", viewModel.StorageText, StringComparison.Ordinal);
        Assert.Contains("16.0 MiB", viewModel.StorageText, StringComparison.Ordinal);
        Assert.Contains("edge-publication", viewModel.CurrentStageText, StringComparison.Ordinal);
        Assert.True(viewModel.HasWaitReason);
    }

    /// <summary>Resource controls and graph diagnostics are reviewable without exposing source content.</summary>
    [Fact]
    public async Task ResourceControlsAndDiagnostics_ArePersistedBoundedAndContentFree()
    {
        var coordinator = new FakeCoordinator(Status(enabled: true));
        var diagnostics = new FakeDiagnosticsService();
        using var viewModel = Create(coordinator, new FakeQueryService(), diagnostics: diagnostics);

        await viewModel.RefreshAsync();
        viewModel.SelectedResourceMode = OpenSorSe.Core.Configuration.IndexingResourceMode.Fast;
        viewModel.ProcessOnlyWhileIdle = true;
        viewModel.PauseBelowBatteryPercentage = 35;
        await viewModel.SaveResourceSettingsCommand.ExecuteAsync(null);

        Assert.Contains("4 bounded worker", viewModel.ResourceSettingsText, StringComparison.Ordinal);
        Assert.Contains("idle host", viewModel.ResourceSettingsText, StringComparison.Ordinal);
        Assert.Contains("run-safe-1", viewModel.DiagnosticsText, StringComparison.Ordinal);
        Assert.Contains("queue 3", viewModel.DiagnosticsText, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", viewModel.DiagnosticsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document text", viewModel.DiagnosticsText, StringComparison.OrdinalIgnoreCase);

        viewModel.RequestMaintenanceCommand.Execute(null);
        Assert.True(viewModel.IsMaintenanceConfirmationPending);
        Assert.Equal(0, coordinator.MaintenanceCount);
        await viewModel.ConfirmMaintenanceCommand.ExecuteAsync(null);
        Assert.Equal(1, coordinator.MaintenanceCount);
        Assert.Contains("removed 0", viewModel.RepairText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies repeated identical status samples do not flood the live region.</summary>
    [Fact]
    public void StatusEvents_CoalesceIdenticalAnnouncements()
    {
        var status = Status(enabled: true, run: GraphRunControlState.Running, processed: 10, total: 100);
        var coordinator = new FakeCoordinator(status);
        using var viewModel = Create(coordinator, new FakeQueryService());
        var changes = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(KnowledgeGraphViewModel.AnnouncementText))
            {
                changes++;
            }
        };

        coordinator.Publish(status);
        coordinator.Publish(status);

        Assert.Equal(1, changes);
        Assert.Contains("Running", viewModel.AnnouncementText, StringComparison.Ordinal);
    }

    /// <summary>Verifies UI pages are capped at 50 even if a hostile provider returns more.</summary>
    [Fact]
    public async Task NodePaging_UsesOpaqueCursorsAndDefensivelyCapsRows()
    {
        var firstCursor = new GraphPageCursor("next-50");
        var query = new FakeQueryService
        {
            NodesHandler = (request, _) => Task.FromResult(
                request.Cursor is null
                    ? new GraphPage<GraphNode>(
                        Enumerable.Range(0, 75).Select(index => Node($"node-{index:D3}", $"Item {index:D3}")).ToArray(),
                        firstCursor,
                        125)
                    : new GraphPage<GraphNode>(
                        Enumerable.Range(50, 25).Select(index => Node($"node-{index:D3}", $"Item {index:D3}")).ToArray(),
                        null,
                        75)),
            DetailsHandler = (id, _) => Task.FromResult<GraphNodeDetails?>(Details(Node(id, id))),
        };
        using var viewModel = Create(new FakeCoordinator(Status(enabled: true)), query);

        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => !viewModel.IsBusy);

        Assert.Equal(KnowledgeGraphViewModel.PageSize, viewModel.Nodes.Count);
        Assert.Equal(GraphLimits.DefaultPageSize, Assert.Single(query.NodeQueries).PageSize);
        Assert.Equal("Showing 1–50 of 125 items", viewModel.PageText);
        Assert.DoesNotContain("â", viewModel.PageText, StringComparison.Ordinal);
        Assert.True(viewModel.NextPageCommand.CanExecute(null));

        await viewModel.NextPageCommand.ExecuteAsync(null);
        await DrainAsync(viewModel, () => !viewModel.IsBusy);

        Assert.Equal(firstCursor, query.NodeQueries.Last().Cursor);
        Assert.Equal(25, viewModel.Nodes.Count);
        Assert.Equal("node-050", viewModel.Nodes[0].Id);
        Assert.Equal("Showing 51–75 of 75 items", viewModel.PageText);
        Assert.Equal(KnowledgeGraphFocusTarget.NodeList, viewModel.LastFocusRequest?.Target);
        Assert.True(viewModel.PreviousPageCommand.CanExecute(null));
    }

    /// <summary>Verifies superseded detail responses cannot replace the current selected item.</summary>
    [Fact]
    public async Task Selection_DiscardsStaleDetailResponses()
    {
        var firstCompletion = new TaskCompletionSource<GraphNodeDetails?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<GraphNodeDetails?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Node("node-1", "First");
        var second = Node("node-2", "Second");
        var query = new FakeQueryService
        {
            NodesHandler = (_, _) => Task.FromResult(new GraphPage<GraphNode>([first, second], null, 2)),
            DetailsHandler = (id, _) => id == "node-1" ? firstCompletion.Task : secondCompletion.Task,
        };
        using var viewModel = Create(new FakeCoordinator(Status(enabled: true)), query);

        var refresh = viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => viewModel.SelectedNode?.Id == "node-1");
        viewModel.SelectedNode = viewModel.Nodes.Single(item => item.Id == "node-2");
        secondCompletion.SetResult(Details(second, "second-alias"));
        await DrainAsync(viewModel, () => viewModel.NodeDetailText.Contains("second-alias", StringComparison.Ordinal));
        firstCompletion.SetResult(Details(first, "stale-first-alias"));
        await refresh;
        await DrainAsync(viewModel, () => !viewModel.IsBusy);

        Assert.Equal("node-2", viewModel.SelectedNode?.Id);
        Assert.Contains("second-alias", viewModel.NodeDetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("stale-first-alias", viewModel.NodeDetailText, StringComparison.Ordinal);
    }

    /// <summary>Verifies cancellation interrupts a pending query and leaves no corrupt partial page.</summary>
    [Fact]
    public async Task CancelCurrent_CancelsPendingPageWithoutPublishingPartialRows()
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new FakeQueryService
        {
            NodesHandler = async (_, token) =>
            {
                requested.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new GraphPage<GraphNode>([Node("never", "Never")], null, 1);
            },
        };
        using var viewModel = Create(new FakeCoordinator(Status(enabled: true)), query);

        var refresh = viewModel.RefreshAsync();
        await requested.Task;
        Assert.False(refresh.IsCompleted);
        viewModel.CancelCurrentCommand.Execute(null);
        await refresh;

        Assert.Empty(viewModel.Nodes);
        Assert.False(viewModel.IsBusy);
        Assert.DoesNotContain("Never", viewModel.Status.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies synchronous shell teardown safely cancels an in-flight mutation continuation.</summary>
    [Fact]
    public async Task Dispose_CancelsPendingMutationWithoutRacingGateRelease()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new FakeCoordinator(Status(enabled: true))
        {
            ReconcileHandler = async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new GraphOperationResult(true, "Unexpected completion.", 0);
            },
        };
        var viewModel = Create(coordinator, new FakeQueryService());
        await viewModel.RefreshAsync();

        var operation = viewModel.ReconcileCommand.ExecuteAsync(null);
        await started.Task;

        viewModel.Dispose();
        await operation;

        Assert.False(viewModel.IsBusy);
        Assert.Contains("cancelled safely", viewModel.Status.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies direct neighbors and evidence are one-hop, bounded, and use actual retained evidence.</summary>
    [Fact]
    public async Task Details_LoadBoundedOneHopNeighborsAndActualEvidence()
    {
        var source = Node("node-1", "Invoice");
        var evidence = Enumerable.Range(0, GraphLimits.MaximumEvidencePerEdge + 3)
            .Select(index => Evidence($"e-{index}", $"Actual evidence {index}"))
            .ToArray();
        var neighbor = new GraphNeighbor(
            Node("node-2", "Receipt"),
            Edge("edge-1", "node-1", "node-2", ["e-0"]),
            [evidence[0]]);
        var query = new FakeQueryService
        {
            NodesHandler = (_, _) => Task.FromResult(new GraphPage<GraphNode>([source], null, 1)),
            DetailsHandler = (_, _) => Task.FromResult<GraphNodeDetails?>(Details(source)),
            NeighborsHandler = (request, _) =>
            {
                Assert.Equal(GraphLimits.StableTraversalDepth, request.Depth);
                Assert.False(request.ExperimentalTraversal);
                Assert.Equal(GraphLimits.DefaultPageSize, request.PageSize);
                return Task.FromResult(new GraphPage<GraphNeighbor>([neighbor], null, 1));
            },
            EvidenceHandler = (_, _) => Task.FromResult<IReadOnlyList<GraphEvidenceReference>>(evidence),
        };
        using var viewModel = Create(new FakeCoordinator(Status(enabled: true)), query);

        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => viewModel.Neighbors.Count == 1 && !viewModel.IsBusy);
        viewModel.SelectedNeighbor = viewModel.Neighbors[0];
        await DrainAsync(viewModel, () => !viewModel.IsBusy);

        Assert.Equal("Actual evidence 0", viewModel.Neighbors[0].EvidenceSummary);
        Assert.Equal(GraphLimits.MaximumEvidencePerEdge, viewModel.Evidence.Count);
        Assert.All(viewModel.Evidence, row => Assert.Contains("Actual evidence", row.Explanation, StringComparison.Ordinal));
    }

    /// <summary>Verifies manual merge/link controls route only compatible reviewed choices to Application services.</summary>
    [Fact]
    public async Task ManualControls_RejectMechanicalMergeAndRouteCompatibleActions()
    {
        var file = Node("file-1", "File", GraphNodeKind.File);
        var first = Node("manual-1", "First", GraphNodeKind.ManualEntity, GraphOrigin.Manual);
        var second = Node("manual-2", "Second", GraphNodeKind.ManualEntity, GraphOrigin.Manual);
        var query = QueryWithNodes(file, first, second);
        var decisions = new FakeDecisionService();
        using var viewModel = Create(
            new FakeCoordinator(Status(enabled: true)),
            query,
            decisions: decisions);
        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => !viewModel.IsBusy);

        viewModel.SelectedNode = viewModel.Nodes.Single(item => item.Id == "file-1");
        viewModel.MergeTarget = viewModel.Nodes.Single(item => item.Id == "manual-1");
        Assert.False(viewModel.MergeSelectedCommand.CanExecute(null));

        viewModel.SelectedNode = viewModel.Nodes.Single(item => item.Id == "manual-1");
        viewModel.MergeTarget = viewModel.Nodes.Single(item => item.Id == "manual-2");
        Assert.True(viewModel.MergeSelectedCommand.CanExecute(null));
        await viewModel.MergeSelectedCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDecisionConfirmationPending);
        Assert.Empty(decisions.Calls);
        Assert.Contains("original files remain unchanged", viewModel.DecisionConfirmationText, StringComparison.OrdinalIgnoreCase);
        await viewModel.ConfirmDecisionActionCommand.ExecuteAsync(null);
        Assert.Contains("merge:manual-2:manual-1", decisions.Calls);

        await DrainAsync(viewModel, () => !viewModel.IsBusy);
        viewModel.SelectedNode = viewModel.Nodes.Single(item => item.Id == "manual-1");
        viewModel.LinkTarget = viewModel.Nodes.Single(item => item.Id == "manual-2");
        viewModel.ManualRelationshipLabel = "Reviewed synthetic link";
        await viewModel.LinkSelectedCommand.ExecuteAsync(null);
        Assert.Contains("link:manual-1:manual-2:Reviewed synthetic link", decisions.Calls);
    }

    /// <summary>Aliases use explicit decisions and the inspector shows only real bounded fact/timeline queries.</summary>
    [Fact]
    public async Task AliasFactAndTimelineInspection_UsesReviewedStoredData()
    {
        var manual = Node("manual-1", "Project", GraphNodeKind.ManualEntity, GraphOrigin.Manual);
        var query = QueryWithNodes(manual);
        query.DetailsHandler = (_, _) => Task.FromResult<GraphNodeDetails?>(Details(manual, "Existing alias"));
        query.FactsHandler = (_, _) => Task.FromResult(new GraphPage<GraphFact>(
            [new GraphFact("fact-size", manual.Identity.NodeId, GraphFactKind.FileSize, "4096", ["evidence-1"], "facts-v1")],
            null,
            1));
        query.TimelineHandler = (_, _) => Task.FromResult(new GraphPage<GraphTimelineEntry>(
            [new GraphTimelineEntry(
                "fact-created",
                manual.Identity.NodeId,
                GraphFactKind.CreatedTimestamp,
                DateTimeOffset.UnixEpoch,
                ["evidence-1"],
                "facts-v1")],
            null,
            1));
        var decisions = new FakeDecisionService();
        using var viewModel = Create(new FakeCoordinator(Status(enabled: true)), query, decisions: decisions);

        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => viewModel.Facts.Count == 1 && viewModel.Timeline.Count == 1 && !viewModel.IsBusy);

        Assert.Equal("4.0 KiB", viewModel.Facts[0].ValueText);
        Assert.Contains("1970", viewModel.Timeline[0].WhenText, StringComparison.Ordinal);
        Assert.Equal("Existing alias", Assert.Single(viewModel.Aliases));
        viewModel.AliasLabel = "New alias";
        await viewModel.AddAliasCommand.ExecuteAsync(null);
        Assert.Contains("alias:manual-1:New alias", decisions.Calls);

        await DrainAsync(viewModel, () => !viewModel.IsBusy);
        viewModel.SelectedAlias = "Existing alias";
        await viewModel.RemoveAliasCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDecisionConfirmationPending);
        Assert.DoesNotContain("remove-alias:manual-1:Existing alias", decisions.Calls);
        await viewModel.ConfirmDecisionActionCommand.ExecuteAsync(null);
        Assert.Contains("remove-alias:manual-1:Existing alias", decisions.Calls);
    }

    /// <summary>Split, unlink, and suggestion rejection never mutate decisions before reviewed confirmation.</summary>
    [Fact]
    public async Task DestructiveDecisionControls_RequireConfirmationAndSupportCancellation()
    {
        var manual = Node("manual-1", "Manual group", GraphNodeKind.ManualEntity, GraphOrigin.Manual);
        var member = Node("manual-2", "Member", GraphNodeKind.ManualEntity, GraphOrigin.Manual);
        var suggestion = Node("suggestion-1", "Suggested project", GraphNodeKind.ManualEntity, GraphOrigin.ExperimentalSuggestion);
        var neighbor = new GraphNeighbor(
            member,
            Edge("edge-auto", manual.Identity.NodeId, member.Identity.NodeId, ["evidence-1"]),
            [Evidence("evidence-1", "Actual retained evidence")]);
        var query = QueryWithNodes(manual, member, suggestion);
        query.NeighborsHandler = (_, _) => Task.FromResult(new GraphPage<GraphNeighbor>([neighbor], null, 1));
        var decisions = new FakeDecisionService();
        using var viewModel = Create(new FakeCoordinator(Status(enabled: true)), query, decisions: decisions);
        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => viewModel.Neighbors.Count == 1 && !viewModel.IsBusy);

        viewModel.SelectedNeighbor = viewModel.Neighbors[0];
        await viewModel.SplitSelectedCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDecisionConfirmationPending);
        Assert.DoesNotContain("split:manual-1:manual-2", decisions.Calls);
        viewModel.CancelDecisionActionCommand.Execute(null);
        Assert.DoesNotContain("split:manual-1:manual-2", decisions.Calls);

        await viewModel.UnlinkSelectedCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDecisionConfirmationPending);
        await viewModel.ConfirmDecisionActionCommand.ExecuteAsync(null);
        Assert.Contains("unlink:edge-auto:True", decisions.Calls);

        await DrainAsync(viewModel, () => !viewModel.IsBusy);
        viewModel.SelectedNode = viewModel.Nodes.Single(item => item.Id == "suggestion-1");
        await DrainAsync(viewModel, () => !viewModel.IsBusy);
        await viewModel.RejectSelectedSuggestionCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDecisionConfirmationPending);
        Assert.DoesNotContain("reject:suggestion-1", decisions.Calls);
        await viewModel.ConfirmDecisionActionCommand.ExecuteAsync(null);
        Assert.Contains("reject:suggestion-1", decisions.Calls);
    }

    /// <summary>Verifies legacy authority is disclosed and immutable structural edges cannot be unlinked.</summary>
    [Fact]
    public async Task UnlinkControl_DisclosesLegacyWriteThroughAndRejectsStructuralEdges()
    {
        var file = Node("file-1", "Invoice", GraphNodeKind.File);
        var related = Node("file-2", "Receipt", GraphNodeKind.File);
        var source = Node("source-1", "Synthetic source", GraphNodeKind.Source);
        var legacyEdge = Edge(
            "edge-legacy",
            file.Identity.NodeId,
            related.Identity.NodeId,
            ["evidence-1"],
            GraphOrigin.LegacyRelationship);
        var structuralEdge = Edge(
            "edge-owner",
            file.Identity.NodeId,
            source.Identity.NodeId,
            ["evidence-2"],
            GraphOrigin.Mechanical,
            GraphEdgeKind.OwnedBySource);
        var query = QueryWithNodes(file, related, source);
        query.NeighborsHandler = (_, _) => Task.FromResult(new GraphPage<GraphNeighbor>(
        [
            new GraphNeighbor(related, legacyEdge, [Evidence("evidence-1", "Existing relationship evidence")]),
            new GraphNeighbor(source, structuralEdge, [Evidence("evidence-2", "Existing source ownership")]),
        ],
        null,
        2));
        var decisions = new FakeDecisionService();
        using var viewModel = Create(new FakeCoordinator(Status(enabled: true)), query, decisions: decisions);
        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => viewModel.Neighbors.Count == 2 && !viewModel.IsBusy);

        var legacy = viewModel.Neighbors.Single(item => item.EdgeId == legacyEdge.Id);
        viewModel.SelectedNeighbor = legacy;
        Assert.True(legacy.IsLegacyOwned);
        Assert.True(viewModel.UnlinkSelectedCommand.CanExecute(null));
        Assert.Contains("existing v1.9 relationship index", legacy.AccessibleText, StringComparison.Ordinal);
        await viewModel.UnlinkSelectedCommand.ExecuteAsync(null);
        Assert.Contains("v1.9 relationship authority", viewModel.DecisionConfirmationText, StringComparison.Ordinal);

        viewModel.CancelDecisionActionCommand.Execute(null);
        viewModel.SelectedNeighbor = viewModel.Neighbors.Single(item => item.EdgeId == structuralEdge.Id);
        Assert.False(viewModel.SelectedNeighbor.CanUnlink);
        Assert.False(viewModel.UnlinkSelectedCommand.CanExecute(null));
    }

    /// <summary>Verifies scoped forget, exclusion, and clear-derived/clear-decisions remain distinct confirmed actions.</summary>
    [Fact]
    public async Task PrivacyActions_AreConfirmedDistinctAndNeverClaimSourceMutation()
    {
        var privacy = new FakePrivacyService();
        using var viewModel = Create(
            new FakeCoordinator(Status(enabled: true)),
            QueryWithNodes(Node("file-1", "Invoice", GraphNodeKind.File)),
            privacy: privacy);
        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => !viewModel.IsBusy);

        viewModel.RequestForgetSelectedCommand.Execute(null);
        Assert.True(viewModel.IsPrivacyConfirmationPending);
        Assert.Empty(privacy.Changes);
        Assert.Contains("graph-native choices", viewModel.PrivacyConfirmationText, StringComparison.OrdinalIgnoreCase);
        await viewModel.ConfirmPrivacyActionCommand.ExecuteAsync(null);
        var forgotten = Assert.Single(privacy.Changes);
        Assert.Equal(GraphPrivacyScopeKind.File, forgotten.Scope.Kind);
        Assert.Equal(GraphPrivacyAction.ForgetDerivedData, forgotten.Action);
        Assert.True(forgotten.ConfirmSourceFilesUnaffected);

        viewModel.RequestClearDerivedCommand.Execute(null);
        Assert.True(viewModel.IsClearConfirmationPending);
        await viewModel.ConfirmClearCommand.ExecuteAsync(null);
        Assert.Contains(privacy.Changes, change => change.Action == GraphPrivacyAction.ClearAllDerivedData);

        viewModel.RequestClearDecisionsCommand.Execute(null);
        Assert.Contains("irreversibly", viewModel.ClearConfirmationText, StringComparison.OrdinalIgnoreCase);
        await viewModel.ConfirmClearCommand.ExecuteAsync(null);
        Assert.Contains(privacy.Changes, change => change.Action == GraphPrivacyAction.ClearAllDecisions);
        Assert.All(privacy.Changes, change => Assert.True(change.ConfirmSourceFilesUnaffected));
    }

    /// <summary>A successful privacy barrier clears previously rendered data before any authority-pending refresh.</summary>
    [Fact]
    public async Task PrivacyAction_ClearsVisibleGraphDataWhenReconciliationIsPending()
    {
        var readCount = 0;
        var query = new FakeQueryService
        {
            NodesHandler = (_, _) => ++readCount == 1
                ? Task.FromResult(new GraphPage<GraphNode>([Node("file-1", "Private invoice", GraphNodeKind.File)], null, 1))
                : Task.FromException<GraphPage<GraphNode>>(new InvalidOperationException("authority pending")),
            DetailsHandler = (id, _) => Task.FromResult<GraphNodeDetails?>(Details(Node(id, "Private invoice", GraphNodeKind.File), "private-alias")),
        };
        using var viewModel = Create(
            new FakeCoordinator(Status(enabled: true)),
            query,
            privacy: new FakePrivacyService());
        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => !viewModel.IsBusy);
        Assert.Single(viewModel.Nodes);
        Assert.NotNull(viewModel.SelectedNode);

        viewModel.RequestForgetSelectedCommand.Execute(null);
        await viewModel.ConfirmPrivacyActionCommand.ExecuteAsync(null);
        await DrainAsync(viewModel, () => !viewModel.IsBusy);

        Assert.Empty(viewModel.Nodes);
        Assert.Null(viewModel.SelectedNode);
        Assert.Empty(viewModel.Neighbors);
        Assert.Empty(viewModel.Evidence);
        Assert.DoesNotContain("Private invoice", viewModel.NodeDetailText, StringComparison.Ordinal);
    }

    /// <summary>An external authority transition hides every cached graph projection before reconciliation completes.</summary>
    [Fact]
    public async Task ExternalStaleAuthorityTransition_HidesCachedGraphDataImmediately()
    {
        var coordinator = new FakeCoordinator(Status(enabled: true, processed: 1, total: 1));
        using var viewModel = Create(
            coordinator,
            QueryWithNodes(Node("file-private", "Private report", GraphNodeKind.File)));
        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => viewModel.Nodes.Count == 1 && !viewModel.IsBusy);

        coordinator.Publish(Status(
            enabled: true,
            freshness: GraphFreshnessState.Stale,
            processed: 0,
            total: 1));

        Assert.Empty(viewModel.Nodes);
        Assert.Null(viewModel.SelectedNode);
        Assert.Empty(viewModel.Facts);
        Assert.Empty(viewModel.Timeline);
        Assert.DoesNotContain("Private report", viewModel.NodeDetailText, StringComparison.Ordinal);
    }

    /// <summary>Verifies selective repair is used before a separately confirmed full derived rebuild.</summary>
    [Fact]
    public async Task RepairActions_AreSelectiveConfirmedAndRelocateFocus()
    {
        var repair = new FakeRepairService();
        using var viewModel = Create(
            new FakeCoordinator(Status(enabled: true)),
            QueryWithNodes(Node("file-1", "Invoice", GraphNodeKind.File)),
            repair: repair);
        await viewModel.RefreshAsync();
        await DrainAsync(viewModel, () => !viewModel.IsBusy);

        await viewModel.VerifySelectedCommand.ExecuteAsync(null);
        Assert.Equal(GraphRepairKind.Verify, Assert.Single(repair.Requests).Kind);

        viewModel.RequestRepairSelectedCommand.Execute(null);
        Assert.True(viewModel.IsRepairConfirmationPending);
        Assert.Equal(KnowledgeGraphFocusTarget.RepairHeading, viewModel.LastFocusRequest?.Target);
        await viewModel.ConfirmRepairActionCommand.ExecuteAsync(null);
        Assert.Contains(repair.Requests, request => request.Kind == GraphRepairKind.RepairEvidence && request.StableId == "file-1");

        viewModel.RequestFullRebuildCommand.Execute(null);
        Assert.Contains("last-resort", viewModel.RepairConfirmationText, StringComparison.OrdinalIgnoreCase);
        await viewModel.ConfirmRepairActionCommand.ExecuteAsync(null);
        Assert.Contains(repair.Requests, request => request.Kind == GraphRepairKind.RebuildDerivedGraph && request.StableId is null);
        Assert.Equal(KnowledgeGraphFocusTarget.RepairHeading, viewModel.LastFocusRequest?.Target);
    }

    private static KnowledgeGraphViewModel Create(
        FakeCoordinator coordinator,
        FakeQueryService query,
        FakePrivacyService? privacy = null,
        FakeRepairService? repair = null,
        FakeDecisionService? decisions = null,
        IGraphDiagnosticsService? diagnostics = null,
        IExplorerCompanionLaunchService? companion = null) => new(
            coordinator,
            query,
            privacy,
            repair,
            decisions,
            null,
            diagnostics,
            companion);

    private static FakeQueryService QueryWithNodes(params GraphNode[] nodes) => new()
    {
        NodesHandler = (_, _) => Task.FromResult(new GraphPage<GraphNode>(nodes, null, nodes.Length)),
        DetailsHandler = (id, _) => Task.FromResult<GraphNodeDetails?>(
            Details(nodes.Single(node => node.Identity.NodeId == id))),
    };

    private static GraphCoordinatorStatus Status(
        bool enabled,
        GraphRunControlState run = GraphRunControlState.Complete,
        GraphJobExecutionState? job = null,
        GraphFreshnessState freshness = GraphFreshnessState.Current,
        GraphIntegrityState integrity = GraphIntegrityState.Valid,
        long processed = 0,
        long total = 0,
        long storage = 0,
        long maximumStorage = 0) => new()
        {
            IsEnabled = enabled,
            RunControl = run,
            ActiveJobState = job,
            Freshness = freshness,
            Integrity = integrity,
            ProcessedObservationCount = processed,
            TotalObservationCount = total,
            RemainingObservationCount = Math.Max(0, total - processed),
            CompletedCount = processed,
            PendingCount = Math.Max(0, total - processed),
            StorageSizeBytes = storage,
            MaximumStorageSizeBytes = maximumStorage,
            Coverage = new GraphProjectionCoverage(
                enabled,
                enabled,
                enabled && processed >= total,
                freshness == GraphFreshnessState.Stale,
                processed,
                total,
                0,
                job is GraphJobExecutionState.WaitingForDependency or GraphJobExecutionState.WaitingForResources ? 1 : 0,
                enabled ? "manifest" : null,
                1,
                enabled ? "Synthetic graph coverage." : "Graph disabled."),
            Message = enabled ? "Synthetic graph state." : "Graph disabled.",
        };

    private static GraphNode Node(
        string id,
        string label,
        GraphNodeKind? kind = null,
        GraphOrigin origin = GraphOrigin.Mechanical) => new()
        {
            Identity = new GraphIdentity(
                id,
                kind ?? GraphNodeKind.File,
                "source-1",
                id,
                "1",
                $"synthetic:{id}"),
            DisplayLabel = label,
            OwningSourceId = kind is null || kind == GraphNodeKind.File || kind == GraphNodeKind.Folder
                ? "source-1"
                : kind == GraphNodeKind.Source ? id : null,
            Origin = origin,
            SourceManifestId = "manifest",
            ObservationHash = $"hash-{id}",
            Algorithm = origin == GraphOrigin.Manual ? "manual" : "synthetic-mechanical",
            AlgorithmVersion = "1",
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            LastValidatedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
        };

    private static GraphNodeDetails Details(GraphNode node, params string[] aliases) =>
        new(node, aliases, 1, 1);

    private static GraphEvidenceReference Evidence(string id, string explanation) => new()
    {
        Id = id,
        Kind = GraphEvidenceKind.StableIdentity,
        SourceEvidenceKey = id,
        ExplanationTemplateCode = "synthetic.actual",
        Explanation = explanation,
        SourceManifestId = "manifest",
        ObservationHash = $"hash-{id}",
    };

    private static GraphEdge Edge(
        string id,
        string source,
        string target,
        IReadOnlyList<string> evidenceIds,
        GraphOrigin origin = GraphOrigin.Mechanical,
        GraphEdgeKind? kind = null) => new()
        {
            Id = id,
            Kind = kind ?? GraphEdgeKind.RelatedFile,
            SourceNodeId = source,
            TargetNodeId = target,
            Confidence = GraphConfidenceLevel.High,
            Origin = origin,
            EvidenceIds = evidenceIds,
            Algorithm = "synthetic",
            AlgorithmVersion = "1",
            InputFingerprint = "fingerprint",
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            LastValidatedAtUtc = DateTimeOffset.UnixEpoch,
        };

    private static async Task DrainAsync(
        KnowledgeGraphViewModel viewModel,
        Func<bool> condition)
    {
        if (condition())
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, _) =>
        {
            if (condition())
            {
                completion.TrySetResult();
            }
        };

        viewModel.PropertyChanged += handler;
        try
        {
            if (condition())
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }

        Assert.True(condition(), "The controlled asynchronous condition did not complete.");
    }

    private static string ReadViewSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solution = Path.Combine(directory.FullName, "OpenSorSe.sln");
            if (File.Exists(solution))
            {
                return File.ReadAllText(Path.Combine(
                    directory.FullName,
                    "src",
                    "OpenSorSe.Desktop",
                    "Views",
                    "KnowledgeGraphView.axaml"));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeCoordinator : IGraphProjectionCoordinator
    {
        public FakeCoordinator(GraphCoordinatorStatus status) => CurrentStatus = status;

        public event EventHandler<GraphCoordinatorStatus>? StatusChanged;

        public GraphCoordinatorStatus CurrentStatus { get; private set; }
        public int EnableCount { get; private set; }
        public int ReconcileCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int RetryCount { get; private set; }
        public int MaintenanceCount { get; private set; }
        public bool LastConsentConfirmed { get; private set; }
        public Func<CancellationToken, Task<GraphOperationResult>>? ReconcileHandler { get; init; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<GraphCoordinatorStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentStatus);

        public Task<GraphControlSettings> GetControlSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphControlSettings
            {
                IsEnabled = CurrentStatus.IsEnabled,
                ConsentConfirmed = CurrentStatus.IsEnabled,
                Revision = 1,
            });

        public Task<GraphControlSettings> UpdateResourceSettingsAsync(
            GraphResourceControlUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphControlSettings
            {
                IsEnabled = CurrentStatus.IsEnabled,
                ConsentConfirmed = CurrentStatus.IsEnabled,
                ResourceMode = update.ResourceMode,
                MaximumConcurrency = update.ResourceMode switch
                {
                    OpenSorSe.Core.Configuration.IndexingResourceMode.Eco => 1,
                    OpenSorSe.Core.Configuration.IndexingResourceMode.Balanced => 2,
                    OpenSorSe.Core.Configuration.IndexingResourceMode.Fast => 4,
                    _ => 2,
                },
                ProcessOnlyWhileIdle = update.ProcessOnlyWhileIdle,
                ProcessOnlyWhileConnectedToPower = update.ProcessOnlyWhileConnectedToPower,
                PauseBelowBatteryPercentage = update.PauseBelowBatteryPercentage,
                ProcessingWindowStartHour = update.ProcessingWindowStartHour,
                ProcessingWindowEndHour = update.ProcessingWindowEndHour,
                Revision = update.ExpectedRevision + 1,
            });

        public Task<GraphMaintenanceResult> MaintainAsync(
            GraphMaintenanceRequest request,
            CancellationToken cancellationToken = default)
        {
            MaintenanceCount++;
            return Task.FromResult(new GraphMaintenanceResult(0, 0, 0, false, DateTimeOffset.UnixEpoch, "Synthetic maintenance."));
        }

        public Task<GraphOperationResult> EnableAsync(bool consentConfirmed, CancellationToken cancellationToken = default)
        {
            EnableCount++;
            LastConsentConfirmed = consentConfirmed;
            CurrentStatus = Status(enabled: consentConfirmed, run: GraphRunControlState.Running);
            Publish(CurrentStatus);
            return Task.FromResult(new GraphOperationResult(consentConfirmed, "Knowledge Graph enabled.", 1));
        }

        public Task<GraphOperationResult> DisableAsync(CancellationToken cancellationToken = default)
        {
            CurrentStatus = Status(enabled: false);
            Publish(CurrentStatus);
            return Task.FromResult(new GraphOperationResult(true, "Knowledge Graph disabled and retained.", 1));
        }

        public async Task<GraphOperationResult> ReconcileAsync(CancellationToken cancellationToken = default)
        {
            ReconcileCount++;
            if (ReconcileHandler is not null)
            {
                return await ReconcileHandler(cancellationToken);
            }

            CurrentStatus = CurrentStatus with { RunControl = GraphRunControlState.Complete };
            Publish(CurrentStatus);
            return await Success("Reconciled.");
        }

        public Task<GraphOperationResult> PauseAsync(CancellationToken cancellationToken = default) => Success("Paused.");
        public Task<GraphOperationResult> ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCount++;
            CurrentStatus = CurrentStatus with { RunControl = GraphRunControlState.Running };
            Publish(CurrentStatus);
            return Success("Resumed.");
        }

        public Task<GraphOperationResult> CancelAsync(string reasonCode, CancellationToken cancellationToken = default) => Success("Cancellation requested.");
        public Task<GraphOperationResult> RetryAsync(string? workId = null, CancellationToken cancellationToken = default)
        {
            RetryCount++;
            CurrentStatus = CurrentStatus with { RunControl = GraphRunControlState.Running };
            Publish(CurrentStatus);
            return Success("Retried.");
        }
        public Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(GraphCoordinatorStatus status)
        {
            CurrentStatus = status;
            StatusChanged?.Invoke(this, status);
        }

        private static Task<GraphOperationResult> Success(string message) =>
            Task.FromResult(new GraphOperationResult(true, message, 1));
    }

    private sealed class FakeQueryService : IGraphQueryService
    {
        public Func<GraphNodeQuery, CancellationToken, Task<GraphPage<GraphNode>>> NodesHandler { get; set; } =
            (_, _) => Task.FromResult(new GraphPage<GraphNode>([], null, 0));
        public Func<string, CancellationToken, Task<GraphNodeDetails?>> DetailsHandler { get; set; } =
            (_, _) => Task.FromResult<GraphNodeDetails?>(null);
        public Func<GraphNeighborQuery, CancellationToken, Task<GraphPage<GraphNeighbor>>> NeighborsHandler { get; set; } =
            (_, _) => Task.FromResult(new GraphPage<GraphNeighbor>([], null, 0));
        public Func<GraphFactQuery, CancellationToken, Task<GraphPage<GraphFact>>> FactsHandler { get; set; } =
            (_, _) => Task.FromResult(new GraphPage<GraphFact>([], null, 0));
        public Func<GraphTimelineQuery, CancellationToken, Task<GraphPage<GraphTimelineEntry>>> TimelineHandler { get; set; } =
            (_, _) => Task.FromResult(new GraphPage<GraphTimelineEntry>([], null, 0));
        public Func<string, CancellationToken, Task<IReadOnlyList<GraphEvidenceReference>>> EvidenceHandler { get; set; } =
            (_, _) => Task.FromResult<IReadOnlyList<GraphEvidenceReference>>([]);
        public List<GraphNodeQuery> NodeQueries { get; } = [];

        public Task<GraphPage<GraphNode>> GetNodesPageAsync(GraphNodeQuery query, CancellationToken cancellationToken = default)
        {
            NodeQueries.Add(query);
            return NodesHandler(query, cancellationToken);
        }

        public Task<GraphNodeDetails?> GetNodeDetailAsync(string nodeId, CancellationToken cancellationToken = default) =>
            DetailsHandler(nodeId, cancellationToken);

        public Task<GraphPage<GraphFact>> GetFactsPageAsync(GraphFactQuery query, CancellationToken cancellationToken = default) =>
            FactsHandler(query, cancellationToken);

        public Task<GraphPage<GraphTimelineEntry>> GetTimelinePageAsync(GraphTimelineQuery query, CancellationToken cancellationToken = default) =>
            TimelineHandler(query, cancellationToken);

        public Task<GraphPage<GraphNeighbor>> GetNeighborsPageAsync(GraphNeighborQuery query, CancellationToken cancellationToken = default) =>
            NeighborsHandler(query, cancellationToken);

        public Task<IReadOnlyList<GraphEvidenceReference>> GetEvidenceAsync(string edgeId, CancellationToken cancellationToken = default) =>
            EvidenceHandler(edgeId, cancellationToken);

        public Task<GraphProjectionCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status(enabled: true).Coverage);
    }

    private sealed class FakeCompanionLaunchService(ExplorerCompanionLaunchResult result)
        : IExplorerCompanionLaunchService
    {
        public int LaunchCount { get; private set; }

        public Task<ExplorerCompanionLaunchResult> LaunchAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakePrivacyService : IGraphPrivacyService
    {
        public List<GraphPrivacyChange> Changes { get; } = [];

        public Task<GraphPrivacyInspection> InspectAsync(GraphPrivacyScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphPrivacyInspection(
                scope,
                1,
                2,
                3,
                4,
                5,
                false,
                DateTimeOffset.UnixEpoch,
                "Synthetic inspection."));

        public Task<GraphOperationResult> ApplyAsync(GraphPrivacyChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.FromResult(new GraphOperationResult(true, "Privacy action completed; original files were unchanged.", 1));
        }
    }

    private sealed class FakeRepairService : IGraphRepairService
    {
        public List<GraphRepairRequest> Requests { get; } = [];

        public Task<GraphOperationResult> ExecuteAsync(GraphRepairRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new GraphOperationResult(true, "Selective repair completed; original files were unchanged.", 1));
        }
    }

    private sealed class FakeDiagnosticsService : IGraphDiagnosticsService
    {
        public Task<GraphDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GraphDiagnosticsSnapshot
            {
                RunId = "run-safe-1",
                ProjectionRevision = 7,
                NodeCount = 11,
                EdgeCount = 9,
                EvidenceCount = 8,
                DecisionCount = 2,
                RepairRequiredCount = 1,
                RecoveredClaimCount = 4,
                QueueLength = 3,
                LastFailureCategory = "database-busy",
                Coverage = Status(enabled: true).Coverage,
            });
    }

    private sealed class FakeDecisionService : IGraphDecisionService
    {
        public List<string> Calls { get; } = [];

        public Task<GraphOperationResult> ApplyAsync(GraphDecisionCommand command, CancellationToken cancellationToken = default) => Success($"apply:{command.Kind}");
        public Task<GraphOperationResult> CreateManualEntityAsync(string entityId, string label, CancellationToken cancellationToken = default) => Success($"create:{entityId}:{label}");
        public Task<GraphOperationResult> RenameManualEntityAsync(string entityId, string label, CancellationToken cancellationToken = default) => Success($"rename:{entityId}:{label}");
        public Task<GraphOperationResult> AddAliasAsync(string entityId, string alias, CancellationToken cancellationToken = default) => Success($"alias:{entityId}:{alias}");
        public Task<GraphOperationResult> RemoveAliasAsync(string entityId, string alias, CancellationToken cancellationToken = default) => Success($"remove-alias:{entityId}:{alias}");
        public Task<GraphOperationResult> LinkAsync(string sourceNodeId, string targetNodeId, string reason, CancellationToken cancellationToken = default) => Success($"link:{sourceNodeId}:{targetNodeId}:{reason}");
        public Task<GraphOperationResult> UnlinkAsync(string edgeId, bool preventRegeneration, CancellationToken cancellationToken = default) => Success($"unlink:{edgeId}:{preventRegeneration}");
        public Task<GraphOperationResult> MergeAsync(string targetEntityId, string sourceEntityId, CancellationToken cancellationToken = default) => Success($"merge:{targetEntityId}:{sourceEntityId}");
        public Task<GraphOperationResult> SplitAsync(string entityId, string memberId, CancellationToken cancellationToken = default) => Success($"split:{entityId}:{memberId}");
        public Task<GraphOperationResult> RejectSuggestionAsync(string suggestionId, CancellationToken cancellationToken = default) => Success($"reject:{suggestionId}");

        private Task<GraphOperationResult> Success(string call)
        {
            Calls.Add(call);
            return Task.FromResult(new GraphOperationResult(true, "Manual graph decision completed.", 1));
        }
    }
}
