using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests.KnowledgeGraph;

/// <summary>Runs the real deterministic builders against the real SQLite graph provider.</summary>
public sealed class SqliteGraphStoreEndToEndTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies dependency-safe claims and provider/builder component identities agree.</summary>
    [Fact]
    public async Task ComponentIdentityAndClaimsRemainDependencySafe()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var observations = SyntheticObservations();

        var projection = await fixture.ProjectAsync(observations, manifestId: "manifest-1");

        Assert.Equal(
            [
                GraphProjectionObservationKind.Source,
                GraphProjectionObservationKind.File,
                GraphProjectionObservationKind.File,
                GraphProjectionObservationKind.Relationship,
            ],
            projection.ClaimedKinds);
        Assert.All(projection.ComponentKeys, key => Assert.Contains(':', key));
        Assert.Contains("Source:source-1", projection.ComponentKeys);
        Assert.Contains("File:file-1", projection.ComponentKeys);
        Assert.Equal(
            projection.ComponentKeys.Order(StringComparer.Ordinal),
            fixture.ReadStrings("SELECT component_key FROM graph_components WHERE component_key <> 'graph-native-decision-overlay' ORDER BY component_key;"));

        var expansions = await fixture.GraphStore.GetSearchExpansionsAsync(["file-1"], 10);
        var expansion = Assert.Single(expansions);
        Assert.Equal("file-2", expansion.RelatedFileId);
        Assert.Equal("Same synthetic invoice set.", expansion.Explanation);
    }

    /// <summary>Verifies file exclusions apply immediately, survive replay, and remain reversible.</summary>
    [Fact]
    public async Task FileExclusionIsImmediateDurableReplayableAndReversible()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var sourceFile = fixture.CreateOriginalSourceMarker();
        var originalHash = HashFile(sourceFile);
        var observations = SyntheticObservations();
        await fixture.ProjectAsync(observations, manifestId: "manifest-privacy");
        var fileTwo = Assert.Single(
            (await fixture.GraphStore.GetNodesAsync(new GraphNodeQuery(GraphNodeKind.File, PageSize: 10))).Items,
            item => item.Identity.CanonicalKey == "file-2");

        var before = await fixture.DecisionStore.GetSnapshotAsync();
        var excludedDecision = await fixture.DecisionStore.AppendAsync(
            new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.Exclude,
                SubjectId = fileTwo.Identity.NodeId,
                Reason = "privacy-scope-file",
                ExpectedSequence = before.Sequence,
                ExpectedControlSettingsRevision = 1,
            },
            Epoch.AddMinutes(1));
        var excludedSnapshot = await fixture.DecisionStore.GetSnapshotAsync();
        Assert.Equal(excludedDecision.Sequence, excludedSnapshot.Sequence);
        await fixture.GraphStore.ApplyPrivacyAsync(
            new GraphPrivacyChange(
                new GraphPrivacyScope(GraphPrivacyScopeKind.File, fileTwo.Identity.NodeId),
                GraphPrivacyAction.ExcludeFromProjection,
                ConfirmSourceFilesUnaffected: true),
            excludedSnapshot,
            Epoch.AddMinutes(1));

        Assert.Null(await fixture.GraphStore.GetNodeAsync(fileTwo.Identity.NodeId));
        Assert.Empty(await fixture.GraphStore.GetSearchExpansionsAsync(["file-1"], 10));
        var inspection = await fixture.GraphStore.InspectPrivacyAsync(
            new GraphPrivacyScope(GraphPrivacyScopeKind.File, fileTwo.Identity.NodeId));
        Assert.True(inspection.IsExcluded);
        Assert.Equal("file-2", fixture.ReadScalar<string>(
            "SELECT stable_id FROM graph_privacy_exclusions WHERE scope_kind = 'File';"));

        await fixture.RestartGraphStoreAsync();
        Assert.Null(await fixture.GraphStore.GetNodeAsync(fileTwo.Identity.NodeId));

        await fixture.ProjectAsync(observations, manifestId: "manifest-privacy");
        Assert.Null(await fixture.GraphStore.GetNodeAsync(fileTwo.Identity.NodeId));

        var include = await fixture.DecisionStore.AppendAsync(
            new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.Include,
                SubjectId = fileTwo.Identity.NodeId,
                Reason = "privacy-scope-file",
                ExpectedSequence = excludedSnapshot.Sequence,
                ExpectedControlSettingsRevision = 1,
            },
            Epoch.AddMinutes(2));
        var includedSnapshot = await fixture.DecisionStore.GetSnapshotAsync();
        Assert.Equal(include.Sequence, includedSnapshot.Sequence);
        await fixture.GraphStore.ApplyPrivacyAsync(
            new GraphPrivacyChange(
                new GraphPrivacyScope(GraphPrivacyScopeKind.File, fileTwo.Identity.NodeId),
                GraphPrivacyAction.IncludeInProjection,
                ConfirmSourceFilesUnaffected: true),
            includedSnapshot,
            Epoch.AddMinutes(2));
        await fixture.ProjectAsync(observations, manifestId: "manifest-privacy");

        Assert.Empty(fixture.ReadStrings("SELECT scope_kind || ':' || stable_id FROM graph_privacy_exclusions ORDER BY scope_kind, stable_id;"));
        Assert.Equal("Current:Valid", fixture.ReadScalar<string>(
            "SELECT freshness_state || ':' || integrity_state FROM graph_components WHERE component_key = 'File:file-2';"));
        var finalCoverage = await fixture.GraphStore.GetCoverageAsync();
        Assert.True(finalCoverage.IsComplete, finalCoverage.Message);
        Assert.NotNull(await fixture.GraphStore.GetNodeAsync(fileTwo.Identity.NodeId));
        Assert.Equal(originalHash, HashFile(sourceFile));
    }

    /// <summary>Verifies Clear Graph and Decisions removes all graph-owned derived state without touching source data.</summary>
    [Fact]
    public async Task ClearGraphAndDecisionsRemovesDerivedStateAndDecisionBearingBackups()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var sourceMarker = fixture.CreateOriginalSourceMarker();
        var sourceHash = HashFile(sourceMarker);
        await fixture.ProjectAsync(SyntheticObservations(), manifestId: "manifest-clear-all");
        await fixture.DecisionStore.AppendAsync(
            new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.LinkNodes,
                SubjectId = "node-clear-source",
                TargetId = "node-clear-target",
                ExpectedSequence = 0,
                ExpectedControlSettingsRevision = 1,
            },
            Epoch.AddMinutes(30));

        var cleared = await fixture.DecisionStore.ClearAsync("CLEAR GRAPH DECISIONS", Epoch.AddMinutes(31));
        Assert.True(cleared.Succeeded);
        var emptyDecisions = await fixture.DecisionStore.GetSnapshotAsync();
        await fixture.GraphStore.ApplyPrivacyAsync(
            new GraphPrivacyChange(
                new GraphPrivacyScope(GraphPrivacyScopeKind.All, "graph:all"),
                GraphPrivacyAction.ClearAllDecisions,
                ConfirmSourceFilesUnaffected: true,
                ConfirmationText: "CLEAR GRAPH DECISIONS"),
            emptyDecisions,
            Epoch.AddMinutes(31));

        Assert.Equal(0, fixture.ReadScalar<int>("SELECT COUNT(*) FROM graph_components;"));
        Assert.Equal(0, fixture.ReadScalar<int>("SELECT COUNT(*) FROM graph_runs;"));
        Assert.Equal(0, fixture.ReadScalar<int>("SELECT COUNT(*) FROM graph_manifests;"));
        Assert.Equal(0, fixture.ReadScalar<int>("SELECT COUNT(*) FROM graph_jobs;"));
        Assert.Equal(0, fixture.ReadScalar<int>("SELECT COUNT(*) FROM graph_observation_inbox;"));
        Assert.Empty(await fixture.DecisionStore.ReadAsync(0, 10));
        Assert.False((await fixture.GraphStore.GetStatusAsync()).IsEnabled);
        Assert.All(fixture.DecisionBackupDatabases(), backup =>
            Assert.Equal(0, GraphFixture.ReadDatabaseScalar<int>(backup, "SELECT COUNT(*) FROM graph_native_decisions;")));
        Assert.Equal(sourceHash, HashFile(sourceMarker));
    }

    /// <summary>Verifies pre-job quota admission is reported as a truthful resource wait.</summary>
    [Fact]
    public async Task QuotaBlockedIngestionReportsWaitingForResourcesWithoutSyntheticJob()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        fixture.Execute(
            "INSERT INTO graph_meta(key, value) VALUES ('maximum_database_bytes', '16777216') ON CONFLICT(key) DO UPDATE SET value = excluded.value; CREATE TABLE quota_status_filler(payload BLOB NOT NULL); INSERT INTO quota_status_filler(payload) VALUES (zeroblob(16777216));");

        var exception = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() =>
            fixture.BeginOnlyAsync(SyntheticObservations(), "manifest-quota-wait"));
        var status = await fixture.GraphStore.GetStatusAsync();

        Assert.Equal(SqliteKnowledgeFailureKind.Full, exception.Kind);
        Assert.Equal(GraphJobExecutionState.WaitingForResources, status.ActiveJobState);
        Assert.Equal(GraphJobExecutionState.WaitingForResources.ToString(), status.CurrentStage);
        Assert.Equal(1, status.WaitingCount);
        Assert.Equal(0, status.RunningCount);
        Assert.Equal(0, fixture.ReadScalar<int>("SELECT COUNT(*) FROM graph_jobs;"));
        Assert.Contains("storage quota", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies privacy and integrity gates suppress every direct relationship read path.</summary>
    [Fact]
    public async Task PrivacyAndIntegrityGatesWithholdDirectEdgeAndEvidenceReads()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        await fixture.ProjectAsync(SyntheticObservations(), manifestId: "manifest-gates");
        var edgeId = fixture.ReadScalar<string>("SELECT edge_id FROM graph_edges WHERE edge_type = 'related-file' LIMIT 1;");
        Assert.NotNull(await fixture.GraphStore.GetEdgeAsync(edgeId));
        Assert.NotEmpty(await fixture.GraphStore.GetEvidenceAsync(edgeId, 10));

        fixture.Execute(
            "UPDATE graph_components SET integrity_state = 'RepairRequired' WHERE component_key = 'Relationship:relationship-1';");

        Assert.Null(await fixture.GraphStore.GetEdgeAsync(edgeId));
        Assert.Empty(await fixture.GraphStore.GetEvidenceAsync(edgeId, 10));
        Assert.Empty(await fixture.GraphStore.GetSearchExpansionsAsync(["file-1"], 10));
    }

    /// <summary>Verifies durable progress labels remain bounded and exclude private paths.</summary>
    [Fact]
    public async Task ProgressLabelsAreDurableBoundedAndPathSafe()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var run = await fixture.BeginOnlyAsync(SyntheticObservations(), "manifest-progress");

        await fixture.GraphStore.SetRunStageAsync(
            run,
            new GraphRunStageUpdate("project-files", "File batch 1 of 2"),
            Epoch);
        var status = await fixture.GraphStore.GetStatusAsync();

        Assert.Equal("project-files", status.CurrentStage);
        Assert.Equal("File batch 1 of 2", status.CurrentWorkLabel);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.GraphStore.SetRunStageAsync(
            run,
            new GraphRunStageUpdate("project-files", @"C:\private\invoice.pdf"),
            Epoch));
    }

    /// <summary>Verifies projection stages are monotonic, fenced, restart-safe, and publication-bound.</summary>
    [Fact]
    public async Task ClaimStagesAreFencedDurableAndResumeWithoutFalseAdvancement()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var run = await fixture.BeginOnlyAsync([SyntheticObservations()[0]], "manifest-stages", Epoch);
        var claim = Assert.IsType<GraphProjectionClaim>(
            await fixture.GraphStore.TryClaimNextAsync(run, "provider-test-owner", Epoch, TimeSpan.FromMinutes(1)));
        var projection = fixture.Build(claim, run);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.GraphStore.AdvanceClaimStageAsync(
            claim,
            new GraphProjectionStageTransition(
                GraphProjectionStage.ObservationCaptured,
                GraphProjectionStage.IdentityResolved,
                projection.InputFingerprint),
            Epoch));
        Assert.Equal(
            GraphProjectionStage.ObservationCaptured.ToString(),
            fixture.ReadScalar<string>("SELECT stage FROM graph_jobs LIMIT 1;"));

        var fenced = claim with { ClaimToken = "claim-obsolete-token" };
        Assert.Null(await fixture.GraphStore.AdvanceClaimStageAsync(
            fenced,
            new GraphProjectionStageTransition(
                GraphProjectionStage.ObservationCaptured,
                GraphProjectionStage.CandidatesExtracted,
                projection.InputFingerprint),
            Epoch));
        Assert.Equal(
            GraphProjectionStage.ObservationCaptured.ToString(),
            fixture.ReadScalar<string>("SELECT stage FROM graph_jobs LIMIT 1;"));

        claim = Assert.IsType<GraphProjectionClaim>(await fixture.GraphStore.AdvanceClaimStageAsync(
            claim,
            new GraphProjectionStageTransition(
                GraphProjectionStage.ObservationCaptured,
                GraphProjectionStage.CandidatesExtracted,
                projection.InputFingerprint),
            Epoch));
        Assert.Equal(GraphProjectionStage.CandidatesExtracted, claim.WorkItem.Stage);
        Assert.Equal(projection.InputFingerprint, claim.WorkItem.StageInputFingerprint);

        await fixture.RestartGraphStoreAsync();
        var resumed = Assert.IsType<GraphProjectionClaim>(
            await fixture.GraphStore.TryClaimNextAsync(run, "provider-test-owner", Epoch.AddMinutes(3), TimeSpan.FromMinutes(1)));
        Assert.Equal(GraphProjectionStage.CandidatesExtracted, resumed.WorkItem.Stage);
        Assert.Equal(projection.InputFingerprint, resumed.WorkItem.StageInputFingerprint);
        Assert.False(await fixture.GraphStore.CommitClaimAsync(resumed, projection, Epoch.AddMinutes(3)));

        resumed = await fixture.AdvanceToValidatedAsync(resumed, projection.InputFingerprint, Epoch.AddMinutes(3));
        Assert.True(await fixture.GraphStore.CommitClaimAsync(resumed, projection, Epoch.AddMinutes(3)));
        Assert.Equal(
            GraphProjectionStage.ComponentPublished.ToString(),
            fixture.ReadScalar<string>("SELECT stage FROM graph_jobs LIMIT 1;"));

        var completed = await fixture.GraphStore.CompleteProjectionAsync(run, Epoch.AddMinutes(3));
        Assert.True(completed.Succeeded, completed.Message);
        Assert.Equal(
            GraphProjectionStage.StaleRowsCleaned.ToString(),
            fixture.ReadScalar<string>("SELECT stage FROM graph_jobs LIMIT 1;"));
    }

    /// <summary>Verifies hostile projection fields are rejected instead of silently truncated.</summary>
    [Fact]
    public async Task OversizedOrMalformedProjectionIsRejectedWithoutPartialPublication()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var run = await fixture.BeginOnlyAsync([SyntheticObservations()[0]], "manifest-validation", Epoch);
        var claim = Assert.IsType<GraphProjectionClaim>(
            await fixture.GraphStore.TryClaimNextAsync(run, "provider-test-owner", Epoch, TimeSpan.FromMinutes(1)));
        var projection = fixture.Build(claim, run);
        claim = await fixture.AdvanceToValidatedAsync(claim, projection.InputFingerprint, Epoch);

        var oversized = projection with
        {
            Nodes =
            [
                projection.Nodes[0] with { DisplayLabel = new string('x', GraphLimits.MaximumLabelCharacters + 1) },
            ],
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.GraphStore.CommitClaimAsync(claim, oversized, Epoch));

        var malformed = projection with
        {
            Nodes =
            [
                projection.Nodes[0] with { DisplayLabel = "invalid\ud800label" },
            ],
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.GraphStore.CommitClaimAsync(claim, malformed, Epoch));

        Assert.Equal(0L, fixture.ReadScalar<long>("SELECT COUNT(*) FROM graph_generations;"));
        Assert.True(await fixture.GraphStore.CommitClaimAsync(claim, projection, Epoch));
    }

    /// <summary>Verifies authoritative consent changes fence reads before the derived mirror updates.</summary>
    [Fact]
    public async Task DecisionControlDisableImmediatelyFencesGraphReads()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-control-fence");
        var file = Assert.Single(
            (await fixture.GraphStore.GetNodesAsync(new GraphNodeQuery(GraphNodeKind.File, PageSize: 10))).Items,
            item => item.Identity.CanonicalKey == "file-1");

        var current = await fixture.DecisionStore.GetControlSettingsAsync();
        var disabled = await fixture.DecisionStore.SetControlSettingsAsync(
            current with { IsEnabled = false },
            current.Revision,
            Epoch.AddMinutes(20));

        Assert.Null(await fixture.GraphStore.GetNodeAsync(file.Identity.NodeId));
        Assert.False((await fixture.GraphStore.GetStatusAsync()).IsEnabled);

        await fixture.DecisionStore.SetControlSettingsAsync(
            disabled with { IsEnabled = true },
            disabled.Revision,
            Epoch.AddMinutes(21));
        Assert.NotNull(await fixture.GraphStore.GetNodeAsync(file.Identity.NodeId));
    }

    /// <summary>Verifies cancellation and restart never hide or overwrite the last validated component generation.</summary>
    [Fact]
    public async Task SelectiveRepairRetainsValidatedGenerationUntilAtomicReplacement()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var sourceMarker = fixture.CreateOriginalSourceMarker();
        var sourceHash = HashFile(sourceMarker);
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-staged-selective");
        var decisionBefore = await fixture.DecisionStore.GetSnapshotAsync();
        var originalGeneration = fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'File:file-1';");

        var cancelledRepair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.ReprojectComponent, "File:file-1", true),
            Epoch.AddMinutes(20));
        Assert.True(cancelledRepair.Succeeded, cancelledRepair.Message);
        var cancelledRun = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(21));
        var cancelled = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                cancelledRun.RunId,
                GraphRunControlState.Cancelled,
                "test-cancel-before-publication",
                Epoch.AddMinutes(21)));
        Assert.True(cancelled.Succeeded, cancelled.Message);

        var cancelledCoverage = await fixture.GraphStore.GetCoverageAsync();
        Assert.False(cancelledCoverage.IsComplete);
        Assert.False(cancelledCoverage.IsStale);
        Assert.NotEmpty((await fixture.GraphStore.GetNodesAsync(new GraphNodeQuery(PageSize: 10))).Items);
        Assert.Equal(originalGeneration, fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'File:file-1';"));
        Assert.Equal("Cancelled", fixture.ReadScalar<string>(
            "SELECT state FROM graph_repair_operations ORDER BY started_utc_ticks DESC LIMIT 1;"));

        var replacementRepair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.ReprojectComponent, "File:file-1", true),
            Epoch.AddMinutes(22));
        Assert.True(replacementRepair.Succeeded, replacementRepair.Message);
        await fixture.RestartGraphStoreAsync(Epoch.AddMinutes(23));
        Assert.Equal(originalGeneration, fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'File:file-1';"));
        Assert.NotEmpty((await fixture.GraphStore.GetNodesAsync(new GraphNodeQuery(PageSize: 10))).Items);

        var replacementRun = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(24));
        var claim = Assert.IsType<GraphProjectionClaim>(await fixture.GraphStore.TryClaimNextAsync(
            replacementRun,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(24),
            TimeSpan.FromMinutes(1)));
        Assert.Equal(GraphProjectionObservationKind.File, claim.WorkItem.Observation.Kind);
        var projection = fixture.Build(claim, replacementRun);
        claim = await fixture.AdvanceToValidatedAsync(claim, projection.InputFingerprint, Epoch.AddMinutes(24));
        Assert.Equal(originalGeneration, fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'File:file-1';"));
        Assert.True(await fixture.GraphStore.CommitClaimAsync(claim, projection, Epoch.AddMinutes(24)));
        Assert.Equal(originalGeneration + 1, fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'File:file-1';"));

        var completed = await fixture.GraphStore.CompleteProjectionAsync(replacementRun, Epoch.AddMinutes(24).AddSeconds(10));
        Assert.True(completed.Succeeded, completed.Message);
        Assert.True((await fixture.GraphStore.GetCoverageAsync()).IsComplete);
        Assert.Equal("Complete", fixture.ReadScalar<string>(
            "SELECT state FROM graph_repair_operations ORDER BY started_utc_ticks DESC LIMIT 1;"));
        Assert.Equal(decisionBefore, await fixture.DecisionStore.GetSnapshotAsync());
        Assert.Equal(sourceHash, HashFile(sourceMarker));
    }

    /// <summary>Verifies a full rebuild resumes from durable jobs and leaves untouched components on their prior generations.</summary>
    [Fact]
    public async Task FullRebuildResumesAfterRestartWithoutDeleteFirstGap()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var sourceMarker = fixture.CreateOriginalSourceMarker();
        var sourceHash = HashFile(sourceMarker);
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-staged-full");
        var decisionBefore = await fixture.DecisionStore.GetSnapshotAsync();
        var originalGenerations = fixture.ReadPairs(
            "SELECT component_key, CAST(active_generation AS TEXT) FROM graph_components WHERE component_key <> 'graph-native-decision-overlay' ORDER BY component_key;")
            .ToDictionary(item => item.Key, item => long.Parse(item.Value, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);

        var repair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(30));
        Assert.True(repair.Succeeded, repair.Message);
        var run = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(31));
        var firstClaim = Assert.IsType<GraphProjectionClaim>(await fixture.GraphStore.TryClaimNextAsync(
            run,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(31),
            TimeSpan.FromMinutes(1)));
        Assert.Equal(GraphProjectionObservationKind.Source, firstClaim.WorkItem.Observation.Kind);
        var firstProjection = fixture.Build(firstClaim, run);
        firstClaim = await fixture.AdvanceToValidatedAsync(firstClaim, firstProjection.InputFingerprint, Epoch.AddMinutes(31));
        Assert.True(await fixture.GraphStore.CommitClaimAsync(firstClaim, firstProjection, Epoch.AddMinutes(31)));
        Assert.Equal(originalGenerations["Source:source-1"] + 1, fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'Source:source-1';"));
        Assert.Equal(originalGenerations["File:file-1"], fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'File:file-1';"));
        var partialCoverage = await fixture.GraphStore.GetCoverageAsync();
        Assert.False(partialCoverage.IsComplete);
        Assert.False(partialCoverage.IsStale);
        Assert.NotEmpty((await fixture.GraphStore.GetNodesAsync(new GraphNodeQuery(PageSize: 10))).Items);

        var cancelled = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                run.RunId,
                GraphRunControlState.Cancelled,
                "test-cancel-after-partial-rebuild",
                Epoch.AddMinutes(31).AddSeconds(10)));
        Assert.True(cancelled.Succeeded, cancelled.Message);
        Assert.Equal("Cancelled", fixture.ReadScalar<string>(
            "SELECT state FROM graph_repair_operations ORDER BY started_utc_ticks DESC LIMIT 1;"));
        var cancelledCoverage = await fixture.GraphStore.GetCoverageAsync();
        Assert.False(cancelledCoverage.IsComplete);
        Assert.False(cancelledCoverage.IsStale);
        Assert.NotEmpty((await fixture.GraphStore.GetNodesAsync(new GraphNodeQuery(PageSize: 10))).Items);
        var postCancellationGenerations = fixture.ReadPairs(
            "SELECT component_key, CAST(active_generation AS TEXT) FROM graph_components WHERE component_key <> 'graph-native-decision-overlay' ORDER BY component_key;")
            .ToDictionary(item => item.Key, item => long.Parse(item.Value, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);

        var resumedRepair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(31).AddSeconds(20));
        Assert.True(resumedRepair.Succeeded, resumedRepair.Message);

        await fixture.RestartGraphStoreAsync(Epoch.AddMinutes(32));
        run = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(33));
        await fixture.ProcessRemainingAsync(run, Epoch.AddMinutes(33));
        var completed = await fixture.GraphStore.CompleteProjectionAsync(run, Epoch.AddMinutes(33).AddSeconds(10));
        Assert.True(completed.Succeeded, completed.Message);

        foreach (var original in postCancellationGenerations)
        {
            Assert.Equal(original.Value + 1, fixture.ReadScalar<long>(
                $"SELECT active_generation FROM graph_components WHERE component_key = '{original.Key}';"));
        }

        Assert.Equal(0, fixture.ReadScalar<int>(
            "SELECT COUNT(*) FROM graph_jobs WHERE execution_state = 'Running';"));
        Assert.Equal("Complete", fixture.ReadScalar<string>(
            "SELECT state FROM graph_repair_operations ORDER BY started_utc_ticks DESC LIMIT 1;"));
        Assert.Equal(decisionBefore, await fixture.DecisionStore.GetSnapshotAsync());
        Assert.Equal(sourceHash, HashFile(sourceMarker));
    }

    /// <summary>Verifies repeated authoritative manifests terminalize obsolete work and retain only current recovery history.</summary>
    [Fact]
    public async Task RepeatedManifestsTerminalizeSupersededRunsWithinRecoveryCeiling()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        var sourceMarker = fixture.CreateOriginalSourceMarker();
        var sourceHash = HashFile(sourceMarker);
        var decisionBefore = await fixture.DecisionStore.GetSnapshotAsync();
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-supersession-applied");
        var repair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(40));
        Assert.True(repair.Succeeded, repair.Message);

        for (var generation = 1; generation <= 6; generation++)
        {
            await fixture.BeginOnlyAsync(
                SyntheticObservations(),
                $"manifest-supersession-{generation}",
                Epoch.AddMinutes(40 + generation));
        }

        Assert.Equal(1, fixture.ReadScalar<int>(
            "SELECT COUNT(*) FROM graph_runs WHERE control_state NOT IN ('Cancelled', 'Complete');"));
        Assert.Equal(0, fixture.ReadScalar<int>(
            "SELECT COUNT(*) FROM graph_runs WHERE control_state = 'PauseRequested';"));
        Assert.InRange(fixture.ReadScalar<int>("SELECT COUNT(*) FROM graph_runs;"), 1, 2);
        Assert.InRange(fixture.ReadScalar<int>("SELECT COUNT(*) FROM graph_manifests;"), 1, 2);
        Assert.Equal(0, fixture.ReadScalar<int>(
            "SELECT COUNT(*) FROM graph_jobs WHERE execution_state = 'Running';"));
        Assert.Equal(0, fixture.ReadScalar<int>(
            "SELECT COUNT(*) FROM graph_jobs j JOIN graph_runs r ON r.run_id = j.run_id WHERE r.control_state = 'Cancelled' AND j.execution_state NOT IN ('Cancelled', 'Complete');"));
        Assert.Equal("Cancelled", fixture.ReadScalar<string>(
            "SELECT state FROM graph_repair_operations ORDER BY started_utc_ticks DESC LIMIT 1;"));
        Assert.Equal(decisionBefore, await fixture.DecisionStore.GetSnapshotAsync());
        Assert.Equal(sourceHash, HashFile(sourceMarker));
    }

    /// <summary>Verifies a decision-overlay-only staged repair completes without inventing source jobs.</summary>
    [Fact]
    public async Task DecisionOverlayOnlyRepairCompletesWithZeroSourceJobsAndPreservesDecisions()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        await fixture.DecisionStore.AppendAsync(
            new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.LinkNodes,
                SubjectId = "node-overlay-source",
                TargetId = "node-overlay-target",
                Reason = "overlay-repair-test",
                ExpectedSequence = 0,
                ExpectedControlSettingsRevision = 1,
            },
            Epoch.AddMinutes(50));
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-overlay-only");
        var decisionsBefore = await fixture.DecisionStore.GetSnapshotAsync();
        var originalGeneration = fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'graph-native-decision-overlay';");

        var repair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(
                GraphRepairKind.ReprojectComponent,
                "graph-native-decision-overlay",
                ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(51));
        Assert.True(repair.Succeeded, repair.Message);
        Assert.Equal(0, repair.AffectedCount);
        var run = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(52));
        Assert.Null(await fixture.GraphStore.TryClaimNextAsync(
            run,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(52),
            TimeSpan.FromMinutes(1)));

        var completed = await fixture.GraphStore.CompleteProjectionAsync(run, Epoch.AddMinutes(52).AddSeconds(10));
        Assert.True(completed.Succeeded, completed.Message);
        Assert.Equal(originalGeneration + 1, fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'graph-native-decision-overlay';"));
        Assert.Equal(decisionsBefore, await fixture.DecisionStore.GetSnapshotAsync());
        Assert.Single(await fixture.DecisionStore.ReadAsync(0, 10));
    }

    /// <summary>Verifies restart honors durable cancellation even when an obsolete claim has not reached its wall-clock expiry.</summary>
    [Fact]
    public async Task RestartFencesNonExpiredClaimAndAcknowledgesRepairCancellation()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-restart-cancel");
        var originalGeneration = fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'Source:source-1';");
        var repair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(60));
        Assert.True(repair.Succeeded, repair.Message);
        var run = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(60));
        Assert.NotNull(await fixture.GraphStore.TryClaimNextAsync(
            run,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(60),
            TimeSpan.FromSeconds(30)));
        var requested = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                run.RunId,
                GraphRunControlState.CancelRequested,
                "test-restart-cancellation",
                Epoch.AddMinutes(60).AddSeconds(5)));
        Assert.True(requested.Succeeded, requested.Message);
        fixture.ExtendRunningClaim(Epoch.AddMinutes(70));

        await fixture.RestartGraphStoreAsync(
            Epoch.AddMinutes(61),
            "provider-test-owner-after-cancel-restart");

        Assert.Equal("Cancelled", fixture.ReadScalar<string>(
            "SELECT control_state FROM graph_runs ORDER BY created_utc_ticks DESC LIMIT 1;"));
        Assert.Equal(0, fixture.ReadScalar<int>(
            "SELECT COUNT(*) FROM graph_jobs WHERE execution_state = 'Running' OR claim_token IS NOT NULL;"));
        Assert.Equal("RecoveredCancelled", fixture.ReadScalar<string>(
            "SELECT outcome FROM graph_job_attempts ORDER BY started_utc_ticks DESC LIMIT 1;"));
        Assert.Equal("Cancelled", fixture.ReadScalar<string>(
            "SELECT state FROM graph_repair_operations ORDER BY started_utc_ticks DESC LIMIT 1;"));
        var coverage = await fixture.GraphStore.GetCoverageAsync();
        Assert.False(coverage.IsComplete);
        Assert.False(coverage.IsStale);
        Assert.NotEmpty((await fixture.GraphStore.GetNodesAsync(new GraphNodeQuery(PageSize: 10))).Items);
        Assert.Equal(originalGeneration, fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'Source:source-1';"));
    }

    /// <summary>Verifies durable cancellation cannot be overwritten by a late pause or resume request.</summary>
    [Fact]
    public async Task CancellationWinsLatePauseAndResumeRequests()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-cancel-precedence");
        var repair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(65));
        Assert.True(repair.Succeeded, repair.Message);
        var run = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(65));
        Assert.NotNull(await fixture.GraphStore.TryClaimNextAsync(
            run,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(65),
            TimeSpan.FromMinutes(1)));

        var cancellation = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                run.RunId,
                GraphRunControlState.CancelRequested,
                "test-cancellation-precedence",
                Epoch.AddMinutes(65).AddSeconds(1)));
        Assert.True(cancellation.Succeeded, cancellation.Message);
        Assert.Equal("CancelRequested", fixture.ReadScalar<string>(
            "SELECT control_state FROM graph_runs ORDER BY created_utc_ticks DESC, run_id DESC LIMIT 1;"));

        var pause = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                run.RunId,
                GraphRunControlState.PauseRequested,
                "late-pause",
                Epoch.AddMinutes(65).AddSeconds(2)));
        var resume = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                run.RunId,
                GraphRunControlState.Running,
                "late-resume",
                Epoch.AddMinutes(65).AddSeconds(3)));
        var repeatedCancellation = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                run.RunId,
                GraphRunControlState.CancelRequested,
                "repeat-cancellation",
                Epoch.AddMinutes(65).AddSeconds(4)));

        Assert.False(pause.Succeeded);
        Assert.False(resume.Succeeded);
        Assert.Contains("cancellation", pause.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cancellation", resume.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(repeatedCancellation.Succeeded, repeatedCancellation.Message);
        Assert.Equal("CancelRequested", fixture.ReadScalar<string>(
            "SELECT control_state FROM graph_runs ORDER BY created_utc_ticks DESC, run_id DESC LIMIT 1;"));
        Assert.Equal("Running", fixture.ReadScalar<string>(
            "SELECT execution_state FROM graph_jobs WHERE claim_token IS NOT NULL LIMIT 1;"));
    }

    /// <summary>Verifies a healthy coordinator does not keep an independently expired job claim alive.</summary>
    [Fact]
    public async Task HealthyCoordinatorHeartbeatDoesNotKeepExpiredJobClaimAlive()
    {
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-independent-heartbeat");
        var repair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(66));
        Assert.True(repair.Succeeded, repair.Message);
        var run = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(66));
        var originalClaim = Assert.IsType<GraphProjectionClaim>(await fixture.GraphStore.TryClaimNextAsync(
            run,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(66),
            TimeSpan.FromSeconds(10)));

        var healthyHeartbeat = await fixture.GraphStore.RecoverAsync(
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(66).AddSeconds(5));
        Assert.Equal(0, healthyHeartbeat.RecoveredClaimCount);
        Assert.Equal("Running", fixture.ReadScalar<string>(
            "SELECT execution_state FROM graph_jobs WHERE claim_token IS NOT NULL LIMIT 1;"));

        var expiredRecovery = await fixture.GraphStore.RecoverAsync(
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(66).AddSeconds(21));
        Assert.Equal(1, expiredRecovery.RecoveredClaimCount);
        Assert.Equal("RetryableFailure", fixture.ReadScalar<string>(
            "SELECT execution_state FROM graph_jobs WHERE job_id = '" + originalClaim.WorkItem.WorkId + "';"));
        Assert.Equal("RecoveredExpired", fixture.ReadScalar<string>(
            "SELECT outcome FROM graph_job_attempts ORDER BY started_utc_ticks DESC LIMIT 1;"));

        var replacementClaim = Assert.IsType<GraphProjectionClaim>(await fixture.GraphStore.TryClaimNextAsync(
            run,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(66).AddSeconds(22),
            TimeSpan.FromSeconds(10)));
        Assert.Equal(originalClaim.FencingEpoch, replacementClaim.FencingEpoch);
        Assert.NotEqual(originalClaim.ClaimToken, replacementClaim.ClaimToken);
        Assert.Equal(originalClaim.WorkItem.Attempt + 1, replacementClaim.WorkItem.Attempt);
    }

    /// <summary>Verifies wall-clock jumps cannot roll back an epoch or authorize an old claim token.</summary>
    [Fact]
    public async Task CoordinatorEpochRemainsMonotonicAcrossForwardAndBackwardClockJumps()
    {
        const string secondOwner = "provider-test-owner-clock-forward";
        const string thirdOwner = "provider-test-owner-after-clock-rollback";
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-clock-aba");
        var repair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(67));
        Assert.True(repair.Succeeded, repair.Message);
        var firstRun = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(67));
        var obsoleteClaim = Assert.IsType<GraphProjectionClaim>(await fixture.GraphStore.TryClaimNextAsync(
            firstRun,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(67),
            TimeSpan.FromSeconds(10)));

        var forwardRecovery = await fixture.GraphStore.RecoverAsync(
            secondOwner,
            Epoch.AddMinutes(67).AddSeconds(40));
        Assert.Equal(1, forwardRecovery.RecoveredClaimCount);
        var secondEpoch = fixture.ReadScalar<long>(
            "SELECT fencing_epoch FROM graph_coordinator_lease WHERE singleton_id = 1;");
        Assert.True(secondEpoch > obsoleteClaim.FencingEpoch);

        await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(
            () => fixture.GraphStore.RecoverAsync(
                GraphFixture.OwnerInstanceId,
                Epoch.AddMinutes(67).AddSeconds(5)));
        Assert.Equal(secondEpoch, fixture.ReadScalar<long>(
            "SELECT fencing_epoch FROM graph_coordinator_lease WHERE singleton_id = 1;"));

        await fixture.GraphStore.RecoverAsync(
            thirdOwner,
            Epoch.AddMinutes(67).AddSeconds(80));
        var thirdEpoch = fixture.ReadScalar<long>(
            "SELECT fencing_epoch FROM graph_coordinator_lease WHERE singleton_id = 1;");
        Assert.True(thirdEpoch > secondEpoch);
        var obsoleteProjection = fixture.Build(obsoleteClaim, firstRun);
        Assert.False(await fixture.GraphStore.CommitClaimAsync(
            obsoleteClaim,
            obsoleteProjection,
            Epoch.AddMinutes(67).AddSeconds(81)));
    }

    /// <summary>Verifies restart fences an old claim, acknowledges pause, and resumes the same durable repair work.</summary>
    [Fact]
    public async Task RestartFencesNonExpiredClaimAcknowledgesPauseAndResumesRepair()
    {
        const string restartedOwner = "provider-test-owner-after-pause-restart";
        using var fixture = new GraphFixture();
        await fixture.InitializeAsync();
        await fixture.ProjectAsync(SyntheticObservations(), "manifest-restart-pause");
        var originalGeneration = fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'Source:source-1';");
        var repair = await fixture.ScheduleRepairAsync(
            new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, ConfirmSourceFilesUnaffected: true),
            Epoch.AddMinutes(70));
        Assert.True(repair.Succeeded, repair.Message);
        var run = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(70));
        Assert.NotNull(await fixture.GraphStore.TryClaimNextAsync(
            run,
            GraphFixture.OwnerInstanceId,
            Epoch.AddMinutes(70),
            TimeSpan.FromSeconds(30)));
        var requested = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                run.RunId,
                GraphRunControlState.PauseRequested,
                "test-restart-pause",
                Epoch.AddMinutes(70).AddSeconds(5)));
        Assert.True(requested.Succeeded, requested.Message);
        fixture.ExtendRunningClaim(Epoch.AddMinutes(80));

        await fixture.RestartGraphStoreAsync(Epoch.AddMinutes(71), restartedOwner);

        Assert.Equal("Paused", fixture.ReadScalar<string>(
            "SELECT control_state FROM graph_runs ORDER BY created_utc_ticks DESC LIMIT 1;"));
        Assert.Equal(0, fixture.ReadScalar<int>(
            "SELECT COUNT(*) FROM graph_jobs WHERE execution_state = 'Running' OR claim_token IS NOT NULL;"));
        Assert.Equal("RecoveredFenced", fixture.ReadScalar<string>(
            "SELECT outcome FROM graph_job_attempts ORDER BY started_utc_ticks DESC LIMIT 1;"));
        Assert.Equal("Running", fixture.ReadScalar<string>(
            "SELECT state FROM graph_repair_operations ORDER BY started_utc_ticks DESC LIMIT 1;"));
        var pausedCoverage = await fixture.GraphStore.GetCoverageAsync();
        Assert.False(pausedCoverage.IsComplete);
        Assert.False(pausedCoverage.IsStale);
        Assert.NotEmpty((await fixture.GraphStore.GetNodesAsync(new GraphNodeQuery(PageSize: 10))).Items);
        Assert.Equal(originalGeneration, fixture.ReadScalar<long>(
            "SELECT active_generation FROM graph_components WHERE component_key = 'Source:source-1';"));

        var resumed = await fixture.GraphStore.SetRunControlAsync(
            new GraphRunControlRequest(
                run.RunId,
                GraphRunControlState.Running,
                "test-resume-after-restart",
                Epoch.AddMinutes(71).AddSeconds(1)));
        Assert.True(resumed.Succeeded, resumed.Message);
        run = await fixture.OpenCurrentRunAsync(Epoch.AddMinutes(71).AddSeconds(2), restartedOwner);
        Assert.Equal(4, await fixture.ProcessRemainingAsync(
            run,
            Epoch.AddMinutes(71).AddSeconds(2),
            restartedOwner));
        var completed = await fixture.GraphStore.CompleteProjectionAsync(
            run,
            Epoch.AddMinutes(71).AddSeconds(10));
        Assert.True(completed.Succeeded, completed.Message);
        Assert.True((await fixture.GraphStore.GetCoverageAsync()).IsComplete);
        Assert.Equal("Complete", fixture.ReadScalar<string>(
            "SELECT state FROM graph_repair_operations ORDER BY started_utc_ticks DESC LIMIT 1;"));
    }

    private static IReadOnlyList<GraphProjectionObservation> SyntheticObservations() =>
    [
        new GraphSourceObservation
        {
            StableKey = "source-1",
            CanonicalRowHash = Hash("source-1"),
            Revision = 1,
            ObservedAtUtc = Epoch,
            SourceId = "source-1",
            DisplayName = "Synthetic source",
            PathSemanticsVersion = "test-v1",
            PathComparison = GraphPathComparison.CaseSensitive,
        },
        File("file-1", "invoice.pdf", 100),
        File("file-2", "receipt.pdf", 200),
        new GraphRelationshipObservation
        {
            StableKey = "relationship-1",
            CanonicalRowHash = Hash("relationship-1"),
            Revision = 1,
            ObservedAtUtc = Epoch,
            RelationshipId = "relationship-1",
            FirstFileId = "file-1",
            SecondFileId = "file-2",
            RelationshipType = "same-project",
            Confidence = GraphConfidenceLevel.High,
            Algorithm = "synthetic-rule",
            AlgorithmVersion = "1.0.0",
            Evidence =
            [
                new GraphProjectionEvidence(
                    "evidence-1",
                    GraphEvidenceKind.LegacyRelationship,
                    "invoice-set",
                    "same-synthetic-set",
                    "Same synthetic invoice set.",
                    Hash("evidence-1")),
            ],
        },
    ];

    private static GraphFileObservation File(string id, string name, long length) => new()
    {
        StableKey = id,
        CanonicalRowHash = Hash(id),
        Revision = 1,
        ObservedAtUtc = Epoch,
        FileId = id,
        SourceId = "source-1",
        FileName = name,
        RelativePath = $"records/{name}",
        FolderRelativePath = "records",
        PathSemanticsVersion = "test-v1",
        PathComparison = GraphPathComparison.CaseSensitive,
        Length = length,
        ModifiedTimeUtc = Epoch,
        HasBasicMetadata = true,
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string HashFile(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class GraphFixture : IDisposable
    {
        private const string Owner = "provider-test-owner";
        private readonly DeterministicGraphProjectionBuilder _builder =
            new(new ConservativeGraphIdentityResolver());
        private readonly DeterministicGraphDecisionProjectionBuilder _decisionBuilder =
            new(new ConservativeGraphIdentityResolver());
        private long _operationSequence;
        private DateTimeOffset _lastOperationAt = Epoch;

        internal GraphFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "opensorse-graph-provider-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            GraphDatabasePath = Path.Combine(Root, "knowledge-graph.db");
            DecisionDatabasePath = Path.Combine(Root, "knowledge-decisions.db");
            GraphStore = new SqliteGraphStore(GraphDatabasePath);
            DecisionStore = new SqliteGraphDecisionStore(DecisionDatabasePath);
        }

        internal string Root { get; }
        internal string GraphDatabasePath { get; }
        internal string DecisionDatabasePath { get; }
        internal SqliteGraphStore GraphStore { get; private set; }
        internal SqliteGraphDecisionStore DecisionStore { get; }
        internal static string OwnerInstanceId => Owner;
        internal GraphProjectionSnapshot LastSnapshot { get; private set; } = null!;

        internal GraphComponentProjection Build(GraphProjectionClaim claim, GraphProjectionRun run) =>
            _builder.Build(claim.WorkItem.Observation, run.Snapshot, Epoch);

        internal async Task InitializeAsync()
        {
            await DecisionStore.InitializeAsync();
            await DecisionStore.SetControlSettingsAsync(
                new GraphControlSettings
                {
                    IsEnabled = true,
                    ConsentConfirmed = true,
                },
                expectedRevision: 0,
                Epoch);
            await GraphStore.InitializeAsync();
            await GraphStore.SetEnabledAsync(true, consentConfirmed: true, Epoch);
            await GraphStore.RecoverAsync(Owner, Epoch);
        }

        internal async Task RestartGraphStoreAsync(
            DateTimeOffset? nowUtc = null,
            string ownerInstanceId = Owner)
        {
            await GraphStore.DisposeAsync();
            GraphStore = new SqliteGraphStore(GraphDatabasePath);
            await GraphStore.InitializeAsync();
            await GraphStore.RecoverAsync(ownerInstanceId, nowUtc ?? Epoch.AddMinutes(3));
        }

        internal async Task<GraphOperationResult> ScheduleRepairAsync(
            GraphRepairRequest request,
            DateTimeOffset nowUtc)
        {
            var decisions = await DecisionStore.GetSnapshotAsync();
            var authority = new GraphAuthoritySnapshot(
                true,
                true,
                LastSnapshot.PrivacySequence,
                LastSnapshot.LegacyDecisionManifestId,
                "allowed")
            {
                CurrentSourceManifestId = LastSnapshot.ManifestId,
                CurrentSourceRevision = LastSnapshot.Revision,
            };
            return await GraphStore.RepairAsync(request, decisions, authority, nowUtc);
        }

        internal Task<GraphProjectionRun> OpenCurrentRunAsync(
            DateTimeOffset nowUtc,
            string ownerInstanceId = Owner) =>
            GraphStore.BeginProjectionAsync(LastSnapshot, ownerInstanceId, nowUtc);

        internal async Task<int> ProcessRemainingAsync(
            GraphProjectionRun run,
            DateTimeOffset nowUtc,
            string ownerInstanceId = Owner)
        {
            var processed = 0;
            GraphProjectionClaim? claim;
            while ((claim = await GraphStore.TryClaimNextAsync(run, ownerInstanceId, nowUtc, TimeSpan.FromMinutes(1))) is not null)
            {
                var projection = Build(claim, run);
                claim = await AdvanceToValidatedAsync(claim, projection.InputFingerprint, nowUtc);
                Assert.True(await GraphStore.CommitClaimAsync(claim, projection, nowUtc));
                processed++;
            }

            return processed;
        }

        internal void ExtendRunningClaim(DateTimeOffset expiresAtUtc) => Execute(
            $"UPDATE graph_jobs SET claim_expires_utc_ticks = {expiresAtUtc.UtcTicks} WHERE execution_state = 'Running'; " +
            $"UPDATE graph_job_attempts SET expires_utc_ticks = {expiresAtUtc.UtcTicks} WHERE completed_utc_ticks IS NULL;");

        internal string CreateOriginalSourceMarker()
        {
            var path = Path.Combine(Root, "original-source-marker.txt");
            System.IO.File.WriteAllText(path, "original source content remains unchanged");
            return path;
        }

        internal async Task<ProjectionResult> ProjectAsync(
            IReadOnlyList<GraphProjectionObservation> observations,
            string manifestId)
        {
            var run = await BeginOnlyAsync(observations, manifestId);
            var operationAt = _lastOperationAt;
            var claimedKinds = new List<GraphProjectionObservationKind>();
            var componentKeys = new List<string>();
            GraphProjectionClaim? claim;
            while ((claim = await GraphStore.TryClaimNextAsync(run, Owner, operationAt, TimeSpan.FromMinutes(1))) is not null)
            {
                claimedKinds.Add(claim.WorkItem.Observation.Kind);
                var projection = _builder.Build(claim.WorkItem.Observation, run.Snapshot, operationAt);
                componentKeys.Add(projection.ComponentKey);
                claim = await AdvanceToValidatedAsync(claim, projection.InputFingerprint, operationAt);
                Assert.True(await GraphStore.CommitClaimAsync(claim, projection, operationAt));
            }

            var completed = await GraphStore.CompleteProjectionAsync(run, operationAt);
            Assert.True(completed.Succeeded, completed.Message);
            return new ProjectionResult(claimedKinds, componentKeys);
        }

        internal async Task<GraphProjectionClaim> AdvanceToValidatedAsync(
            GraphProjectionClaim claim,
            string inputFingerprint,
            DateTimeOffset nowUtc)
        {
            var stages = new[]
            {
                GraphProjectionStage.ObservationCaptured,
                GraphProjectionStage.CandidatesExtracted,
                GraphProjectionStage.CandidatesNormalized,
                GraphProjectionStage.IdentityResolved,
                GraphProjectionStage.EdgesPrepared,
                GraphProjectionStage.ComponentValidated,
            };
            var index = Array.IndexOf(stages, claim.WorkItem.Stage);
            Assert.InRange(index, 0, stages.Length - 1);
            for (var next = index + 1; next < stages.Length; next++)
            {
                claim = Assert.IsType<GraphProjectionClaim>(await GraphStore.AdvanceClaimStageAsync(
                    claim,
                    new GraphProjectionStageTransition(stages[next - 1], stages[next], inputFingerprint),
                    nowUtc));
            }

            return claim;
        }

        internal async Task<GraphProjectionRun> BeginOnlyAsync(
            IReadOnlyList<GraphProjectionObservation> observations,
            string manifestId,
            DateTimeOffset? operationAt = null)
        {
            _lastOperationAt = operationAt ?? Epoch.AddMinutes(10).AddSeconds(Interlocked.Increment(ref _operationSequence));
            var ordered = observations
                .OrderBy(item => (int)item.Kind)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            var decisionSnapshot = await DecisionStore.GetSnapshotAsync();
            var snapshot = new GraphProjectionSnapshot(
                manifestId,
                Revision: 1,
                LegacyDecisionManifestId: "legacy-manifest-1",
                PrivacySequence: 0,
                CompletedAtUtc: Epoch,
                CanonicalManifestHash: ManifestHash(ordered),
                TotalObservationCount: ordered.Length,
                ObservationCounts: ordered
                    .GroupBy(item => item.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => new GraphObservationKindCount(group.Key, group.LongCount()))
                    .ToArray())
            {
                GraphDecisionSequence = decisionSnapshot.Sequence,
                GraphDecisionCheckpointId = decisionSnapshot.CheckpointId,
            };
            LastSnapshot = snapshot;
            var run = await GraphStore.BeginProjectionAsync(snapshot, Owner, _lastOperationAt);
            var page = new GraphProjectionPage(
                manifestId,
                snapshot.Revision,
                PageSequence: 0,
                ObservationCount: ordered.Length,
                CanonicalPageHash: PageHash(ordered),
                Observations: ordered,
                NextCursor: null,
                IsLastPage: true);
            await GraphStore.QueueProjectionPageAsync(run, page, _lastOperationAt);
            await GraphStore.CompleteInputManifestAsync(run, _lastOperationAt);

            var decisions = await DecisionStore.ReadAsync(0, 1_000);
            var projections = decisions
                .Select(item => _decisionBuilder.Build(item, decisionSnapshot, Epoch))
                .ToArray();
            await GraphStore.ApplyDecisionProjectionPageAsync(
                run,
                decisionSnapshot,
                projections,
                isLastPage: true,
                _lastOperationAt);
            return run;
        }

        internal void Execute(string sql)
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection($"Data Source={GraphDatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        internal T ReadScalar<T>(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={GraphDatabasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        internal IReadOnlyList<string> ReadStrings(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={GraphDatabasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var values = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        internal IReadOnlyList<KeyValuePair<string, string>> ReadPairs(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={GraphDatabasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var values = new List<KeyValuePair<string, string>>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                values.Add(new KeyValuePair<string, string>(reader.GetString(0), reader.GetString(1)));
            }

            return values;
        }

        internal IReadOnlyList<string> DecisionBackupDatabases()
        {
            var directory = Path.Combine(Root, "backups", "knowledge-decisions");
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "decision-backup-*.db", SearchOption.TopDirectoryOnly)
                : [];
        }

        internal static T ReadDatabaseScalar<T>(string databasePath, string sql)
        {
            using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string PageHash(IEnumerable<GraphProjectionObservation> observations) =>
            HashLines(observations.Select(item => $"{item.Kind}|{item.StableKey}|{item.CanonicalRowHash}"));

        private static string ManifestHash(IEnumerable<GraphProjectionObservation> observations) =>
            HashLines(observations
                .OrderBy(item => item.Kind.ToString(), StringComparer.Ordinal)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .Select(item => $"{item.Kind}|{item.StableKey}|{item.CanonicalRowHash}"));

        private static string HashLines(IEnumerable<string> lines) => Hash(string.Join('\n', lines));

        public void Dispose()
        {
            GraphStore.DisposeAsync().AsTask().GetAwaiter().GetResult();
            DecisionStore.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed record ProjectionResult(
        IReadOnlyList<GraphProjectionObservationKind> ClaimedKinds,
        IReadOnlyList<string> ComponentKeys);
}
