using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Platform;
using OpenSorSe.Indexing.Sqlite;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests.KnowledgeGraph;

/// <summary>
/// Verifies that Application graph facades cannot bypass the explicit-consent boundary owned by
/// the composite SQLite lifecycle.
/// </summary>
public sealed class SqliteGraphConsentBoundaryIntegrationTests
{
    private static readonly string EmptySha256 = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));

    /// <summary>
    /// Verifies that startup inspection and every ordinary Application facade fail closed without
    /// creating either sidecar, a bootstrap marker, or a transient graph snapshot.
    /// </summary>
    [Theory]
    [InlineData(DirectGraphCall.Query)]
    [InlineData(DirectGraphCall.Search)]
    [InlineData(DirectGraphCall.Privacy)]
    [InlineData(DirectGraphCall.Repair)]
    [InlineData(DirectGraphCall.Decision)]
    [InlineData(DirectGraphCall.Diagnostics)]
    public async Task DirectApplicationCallBeforeConsentDoesNotCreateGraphArtifacts(DirectGraphCall call)
    {
        using var fixture = new ConsentFixture();
        await fixture.SeedV19DeepIndexAsync();
        await using var lifecycle = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);
        await using var graphStore = new SqliteGraphStore(fixture.GraphDatabasePath);
        await using var decisionStore = new SqliteGraphDecisionStore(fixture.DecisionDatabasePath);
        await using var source = new EmptyProjectionSource();
        var signal = new NoOpReconciliationSignal();

        Assert.Equal(GraphStorageProvisioningState.Unprovisioned, await lifecycle.GetProvisioningStateAsync());
        fixture.AssertNoGraphArtifacts();

        Exception? failure = null;
        switch (call)
        {
            case DirectGraphCall.Query:
                failure = await Record.ExceptionAsync(
                    () => new GraphQueryService(graphStore, source, decisionStore)
                        .GetNodesPageAsync(new GraphNodeQuery()));
                break;
            case DirectGraphCall.Search:
                {
                    var result = await new GraphSearchSource(graphStore, source, decisionStore)
                        .ExpandAsync(new GraphSearchRequest(["file:synthetic"]));
                    Assert.False(result.IsAvailable);
                    Assert.Empty(result.Expansions);
                    Assert.Contains("unavailable", result.Message, StringComparison.OrdinalIgnoreCase);
                    break;
                }
            case DirectGraphCall.Privacy:
                failure = await Record.ExceptionAsync(
                    () => new GraphPrivacyService(graphStore, decisionStore, source, signal)
                        .InspectAsync(new GraphPrivacyScope(GraphPrivacyScopeKind.All, string.Empty)));
                break;
            case DirectGraphCall.Repair:
                failure = await Record.ExceptionAsync(
                    () => new GraphRepairService(graphStore, source, decisionStore, signal)
                        .ExecuteAsync(new GraphRepairRequest(GraphRepairKind.Verify)));
                break;
            case DirectGraphCall.Decision:
                failure = await Record.ExceptionAsync(
                    () => new GraphDecisionService(
                            decisionStore,
                            graphStore,
                            new ConservativeGraphIdentityResolver(),
                            signal,
                            projectionSource: source)
                        .CreateManualEntityAsync("manual:synthetic", "Synthetic entity"));
                break;
            case DirectGraphCall.Diagnostics:
                failure = await Record.ExceptionAsync(
                    () => new GraphDiagnosticsService(graphStore, lifecycle).GetSnapshotAsync());
                break;
            default:
                throw new InvalidOperationException("The direct graph-call test case is not supported.");
        }

        if (call != DirectGraphCall.Search)
        {
            Assert.NotNull(failure);
        }

        fixture.AssertNoGraphArtifacts();
        fixture.AssertV19DeepIndexUnchanged();
        Assert.Equal(GraphStorageProvisioningState.Unprovisioned, await lifecycle.GetProvisioningStateAsync());
    }

    /// <summary>
    /// Verifies that coordinator startup remains non-mutating, while the first explicit enablement
    /// provisions both sidecars and permits ordinary Application reads after initialization.
    /// </summary>
    [Fact]
    public async Task ExplicitEnableProvisionsBothSidecarsBeforeApplicationReadsBecomeAvailable()
    {
        using var fixture = new ConsentFixture();
        await fixture.SeedV19DeepIndexAsync();
        await using var lifecycle = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);
        await using var graphStore = new SqliteGraphStore(fixture.GraphDatabasePath);
        await using var decisionStore = new SqliteGraphDecisionStore(fixture.DecisionDatabasePath);
        await using var source = new EmptyProjectionSource();
        var identity = new ConservativeGraphIdentityResolver();
        await using var coordinator = new GraphProjectionCoordinator(
            source,
            graphStore,
            decisionStore,
            new DeterministicGraphProjectionBuilder(identity),
            new DeterministicGraphDecisionProjectionBuilder(identity),
            new AlwaysEligibleResourcePolicy(),
            ownerInstanceId: "consent-integration-worker",
            storageLifecycle: lifecycle);

        await coordinator.InitializeAsync();

        var initialStatus = await coordinator.GetStatusAsync();
        Assert.False(initialStatus.IsProvisioned);
        Assert.False(initialStatus.IsEnabled);
        fixture.AssertNoGraphArtifacts();

        var enabled = await coordinator.EnableAsync(consentConfirmed: true);

        Assert.True(enabled.Succeeded);
        Assert.True(File.Exists(fixture.GraphDatabasePath));
        Assert.True(File.Exists(fixture.DecisionDatabasePath));
        Assert.False(File.Exists(fixture.BootstrapMarkerPath));
        Assert.False(File.Exists(fixture.StagedGraphDatabasePath));
        Assert.False(File.Exists(fixture.StagedDecisionDatabasePath));
        Assert.Equal(GraphStorageProvisioningState.Provisioned, await lifecycle.GetProvisioningStateAsync());

        var reconciled = await coordinator.ReconcileAsync();
        Assert.True(reconciled.Succeeded);

        var nodes = await new GraphQueryService(graphStore, source, decisionStore)
            .GetNodesPageAsync(new GraphNodeQuery());
        var search = await new GraphSearchSource(graphStore, source, decisionStore)
            .ExpandAsync(new GraphSearchRequest(["file:synthetic"]));

        Assert.Empty(nodes.Items);
        Assert.True(search.IsAvailable);
        Assert.Empty(search.Expansions);
        fixture.AssertV19DeepIndexUnchanged();
    }

    /// <summary>Enumerates direct graph Application boundaries exercised before consent.</summary>
    public enum DirectGraphCall
    {
        /// <summary>Provider-neutral graph query.</summary>
        Query,
        /// <summary>Optional Search expansion.</summary>
        Search,
        /// <summary>Privacy inspection.</summary>
        Privacy,
        /// <summary>Selective verification and repair.</summary>
        Repair,
        /// <summary>Graph-native manual decision.</summary>
        Decision,
        /// <summary>Privacy-safe diagnostics.</summary>
        Diagnostics,
    }

    private sealed class ConsentFixture : IDisposable
    {
        private string? _deepIndexHash;

        internal ConsentFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "opensorse-graph-consent-tests",
                Guid.NewGuid().ToString("N"));
            IndexDirectory = Path.Combine(Root, "index");
            GraphDatabasePath = Path.Combine(IndexDirectory, "knowledge-graph.db");
            DecisionDatabasePath = Path.Combine(IndexDirectory, "knowledge-decisions.db");
            DeepIndexDatabasePath = Path.Combine(IndexDirectory, "deep-index.db");
            BootstrapMarkerPath = Path.Combine(IndexDirectory, ".knowledge-data.bootstrap.json");
            StagedGraphDatabasePath = Path.Combine(IndexDirectory, ".knowledge-graph.bootstrap.db");
            StagedDecisionDatabasePath = Path.Combine(IndexDirectory, ".knowledge-decisions.bootstrap.db");
        }

        internal string Root { get; }
        internal string IndexDirectory { get; }
        internal string GraphDatabasePath { get; }
        internal string DecisionDatabasePath { get; }
        internal string DeepIndexDatabasePath { get; }
        internal string BootstrapMarkerPath { get; }
        internal string StagedGraphDatabasePath { get; }
        internal string StagedDecisionDatabasePath { get; }

        internal void AssertNoGraphArtifacts()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            var artifacts = Directory
                .EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Where(IsConsentControlledArtifact)
                .Select(path => Path.GetRelativePath(Root, path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.Empty(artifacts);
        }

        internal async Task SeedV19DeepIndexAsync()
        {
            using (var store = new SqliteDeepIndexStore(DeepIndexDatabasePath, PlatformServices.CurrentPathSemantics))
            {
                await store.InitializeAsync();
            }

            SqliteConnection.ClearAllPools();
            _deepIndexHash = HashFile(DeepIndexDatabasePath);
        }

        internal void AssertV19DeepIndexUnchanged()
        {
            Assert.NotNull(_deepIndexHash);
            Assert.True(File.Exists(DeepIndexDatabasePath));
            Assert.Equal(_deepIndexHash, HashFile(DeepIndexDatabasePath));
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static bool IsConsentControlledArtifact(string path)
        {
            var name = Path.GetFileName(path);
            return name.StartsWith("knowledge-graph.db", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("knowledge-decisions.db", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(".knowledge-data.bootstrap.json", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(".knowledge-graph.bootstrap.db", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(".knowledge-decisions.bootstrap.db", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("knowledge-graph-snapshot", StringComparison.OrdinalIgnoreCase);
        }

        private static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

    }

    private sealed class EmptyProjectionSource : IGraphProjectionSource
    {
        private const string ManifestId = "synthetic-manifest-1";
        private const string LegacyDecisionManifestId = "synthetic-legacy-1";

        public Task<GraphProjectionSnapshot> OpenCompletedSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GraphProjectionSnapshot(
                ManifestId,
                1,
                LegacyDecisionManifestId,
                1,
                DateTimeOffset.UnixEpoch.AddDays(1),
                EmptySha256,
                0,
                []));
        }

        public Task<GraphProjectionPage> ReadPageAsync(
            GraphProjectionSnapshot snapshot,
            GraphProjectionCursor? cursor,
            int maximumCount,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ManifestId, snapshot.ManifestId);
            Assert.Null(cursor);
            Assert.InRange(maximumCount, 1, GraphLimits.MaximumProjectionPageSize);
            return Task.FromResult(new GraphProjectionPage(
                ManifestId,
                1,
                0,
                0,
                EmptySha256,
                [],
                null,
                true));
        }

        public Task<GraphAuthoritySnapshot> ValidateAuthorityAsync(
            GraphAuthorityRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GraphAuthoritySnapshot(
                true,
                true,
                1,
                LegacyDecisionManifestId,
                "allowed")
            {
                CurrentSourceManifestId = ManifestId,
                CurrentSourceRevision = 1,
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpReconciliationSignal : IGraphReconciliationSignal
    {
        public ValueTask SignalAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlwaysEligibleResourcePolicy : IGraphResourceAdmissionPolicy
    {
        public Task<GraphResourceEligibility> GetEligibilityAsync(
            GraphControlSettings settings,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GraphResourceEligibility(true, null));
        }
    }
}
