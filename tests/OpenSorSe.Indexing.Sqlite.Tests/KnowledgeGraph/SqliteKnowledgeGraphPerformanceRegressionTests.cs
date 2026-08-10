using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests.KnowledgeGraph;

/// <summary>
/// Exercises modest synthetic provider workloads behind deliberately generous regression ceilings.
/// These checks are not universal storage-throughput or latency claims.
/// </summary>
public sealed class SqliteKnowledgeGraphPerformanceRegressionTests
{
    /// <summary>Verifies bounded keyset pages visit a modest projected file corpus once and terminate.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task BoundedFilePaging_VisitsSyntheticCorpusWithinRegressionCeiling()
    {
        const int fileCount = 96;
        const int pageSize = 23;
        await using var fixture = new PerformanceGraphFixture();
        await fixture.InitializeAsync();
        await fixture.ProjectAsync(fileCount, "performance-paging-manifest");
        var visited = new HashSet<string>(StringComparer.Ordinal);
        GraphPageCursor? cursor = null;
        var pageCount = 0;
        var stopwatch = Stopwatch.StartNew();

        do
        {
            var page = await fixture.GraphStore.GetNodesAsync(
                new GraphNodeQuery(GraphNodeKind.File, Cursor: cursor, PageSize: pageSize));
            Assert.InRange(page.Items.Count, 1, pageSize);
            Assert.Equal((long)fileCount, page.TotalCount);
            Assert.All(page.Items, item => Assert.True(visited.Add(item.Identity.NodeId)));
            cursor = page.NextCursor;
            pageCount++;
        }
        while (cursor is not null);

        stopwatch.Stop();
        Assert.Equal(fileCount, visited.Count);
        Assert.Equal((int)Math.Ceiling(fileCount / (double)pageSize), pageCount);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Reading {fileCount:N0} projected files in {pageCount:N0} bounded pages took {stopwatch.Elapsed}.");
    }

    /// <summary>Verifies replaying an identical completed manifest creates no new projection claim.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task IdenticalCompletedManifest_ReplaysAsBoundedNoOp()
    {
        const int fileCount = 64;
        const string manifestId = "performance-no-op-manifest";
        await using var fixture = new PerformanceGraphFixture();
        await fixture.InitializeAsync();
        var observations = PerformanceGraphFixture.CreateObservations(fileCount);
        await fixture.ProjectAsync(observations, manifestId);
        var jobsBefore = fixture.CountJobs();
        var stopwatch = Stopwatch.StartNew();

        var replay = await fixture.BeginOnlyAsync(observations, manifestId);
        var unexpectedClaim = await fixture.GraphStore.TryClaimNextAsync(
            replay,
            PerformanceGraphFixture.Owner,
            PerformanceGraphFixture.Epoch,
            TimeSpan.FromMinutes(1));
        var completed = await fixture.GraphStore.CompleteProjectionAsync(
            replay,
            PerformanceGraphFixture.Epoch);

        stopwatch.Stop();
        Assert.Null(unexpectedClaim);
        Assert.True(completed.Succeeded, completed.Message);
        Assert.Equal(jobsBefore, fixture.CountJobs());
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Replaying an unchanged {fileCount:N0}-file completed manifest took {stopwatch.Elapsed}.");
    }

    private sealed class PerformanceGraphFixture : IAsyncDisposable
    {
        internal const string Owner = "graph-performance-owner";
        internal static readonly DateTimeOffset Epoch = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        private readonly DeterministicGraphProjectionBuilder _builder =
            new(new ConservativeGraphIdentityResolver());
        private readonly DeterministicGraphDecisionProjectionBuilder _decisionBuilder =
            new(new ConservativeGraphIdentityResolver());
        private readonly string _root;
        private readonly string _graphDatabasePath;
        private readonly SqliteGraphDecisionStore _decisionStore;

        internal PerformanceGraphFixture()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "opensorse-graph-performance-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _graphDatabasePath = Path.Combine(_root, "knowledge-graph.db");
            GraphStore = new SqliteGraphStore(_graphDatabasePath);
            _decisionStore = new SqliteGraphDecisionStore(
                Path.Combine(_root, "knowledge-decisions.db"));
        }

        internal SqliteGraphStore GraphStore { get; }

        internal async Task InitializeAsync()
        {
            await _decisionStore.InitializeAsync();
            await _decisionStore.SetControlSettingsAsync(
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

        internal Task ProjectAsync(int fileCount, string manifestId) =>
            ProjectAsync(CreateObservations(fileCount), manifestId);

        internal async Task ProjectAsync(
            IReadOnlyList<GraphProjectionObservation> observations,
            string manifestId)
        {
            var run = await BeginOnlyAsync(observations, manifestId);
            GraphProjectionClaim? claim;
            while ((claim = await GraphStore.TryClaimNextAsync(
                       run,
                       Owner,
                       Epoch,
                       TimeSpan.FromMinutes(1))) is not null)
            {
                var projection = _builder.Build(claim.WorkItem.Observation, run.Snapshot, Epoch);
                claim = await AdvanceToValidatedAsync(claim, projection.InputFingerprint);
                Assert.True(await GraphStore.CommitClaimAsync(claim, projection, Epoch));
            }

            var completed = await GraphStore.CompleteProjectionAsync(run, Epoch);
            Assert.True(completed.Succeeded, completed.Message);
        }

        internal async Task<GraphProjectionRun> BeginOnlyAsync(
            IReadOnlyList<GraphProjectionObservation> observations,
            string manifestId)
        {
            var ordered = observations
                .OrderBy(item => (int)item.Kind)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            var decisions = await _decisionStore.GetSnapshotAsync();
            var snapshot = new GraphProjectionSnapshot(
                manifestId,
                Revision: 1,
                LegacyDecisionManifestId: "legacy-performance-manifest",
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
                GraphDecisionSequence = decisions.Sequence,
                GraphDecisionCheckpointId = decisions.CheckpointId,
            };
            var run = await GraphStore.BeginProjectionAsync(snapshot, Owner, Epoch);
            await GraphStore.QueueProjectionPageAsync(
                run,
                new GraphProjectionPage(
                    manifestId,
                    snapshot.Revision,
                    PageSequence: 0,
                    ObservationCount: ordered.Length,
                    CanonicalPageHash: PageHash(ordered),
                    Observations: ordered,
                    NextCursor: null,
                    IsLastPage: true),
                Epoch);
            await GraphStore.CompleteInputManifestAsync(run, Epoch);
            var entries = await _decisionStore.ReadAsync(0, 1_000);
            await GraphStore.ApplyDecisionProjectionPageAsync(
                run,
                decisions,
                entries.Select(item => _decisionBuilder.Build(item, decisions, Epoch)).ToArray(),
                isLastPage: true,
                Epoch);
            return run;
        }

        internal long CountJobs()
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection(
                $"Data Source={_graphDatabasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM graph_jobs;";
            return Convert.ToInt64(
                command.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static IReadOnlyList<GraphProjectionObservation> CreateObservations(int fileCount)
        {
            var observations = new List<GraphProjectionObservation>(fileCount + 1)
            {
                new GraphSourceObservation
                {
                    StableKey = "performance-source",
                    CanonicalRowHash = Hash("performance-source"),
                    Revision = 1,
                    ObservedAtUtc = Epoch,
                    SourceId = "performance-source",
                    DisplayName = "Synthetic performance source",
                    PathSemanticsVersion = "performance-path-v1",
                    PathComparison = GraphPathComparison.CaseSensitive,
                },
            };
            for (var index = 0; index < fileCount; index++)
            {
                var id = $"performance-file-{index:D5}";
                observations.Add(new GraphFileObservation
                {
                    StableKey = id,
                    CanonicalRowHash = Hash(id),
                    Revision = 1,
                    ObservedAtUtc = Epoch,
                    FileId = id,
                    SourceId = "performance-source",
                    FileName = $"document-{index:D5}.txt",
                    RelativePath = $"records/document-{index:D5}.txt",
                    FolderRelativePath = "records",
                    PathSemanticsVersion = "performance-path-v1",
                    PathComparison = GraphPathComparison.CaseSensitive,
                    Length = 1_024 + index,
                    ModifiedTimeUtc = Epoch,
                    HasBasicMetadata = true,
                });
            }

            return observations;
        }

        private async Task<GraphProjectionClaim> AdvanceToValidatedAsync(
            GraphProjectionClaim claim,
            string inputFingerprint)
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
            var current = Array.IndexOf(stages, claim.WorkItem.Stage);
            Assert.InRange(current, 0, stages.Length - 1);
            for (var next = current + 1; next < stages.Length; next++)
            {
                claim = Assert.IsType<GraphProjectionClaim>(await GraphStore.AdvanceClaimStageAsync(
                    claim,
                    new GraphProjectionStageTransition(stages[next - 1], stages[next], inputFingerprint),
                    Epoch));
            }

            return claim;
        }

        private static string PageHash(IEnumerable<GraphProjectionObservation> observations) =>
            HashLines(observations.Select(item => $"{item.Kind}|{item.StableKey}|{item.CanonicalRowHash}"));

        private static string ManifestHash(IEnumerable<GraphProjectionObservation> observations) =>
            HashLines(observations
                .OrderBy(item => item.Kind.ToString(), StringComparer.Ordinal)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .Select(item => $"{item.Kind}|{item.StableKey}|{item.CanonicalRowHash}"));

        private static string HashLines(IEnumerable<string> lines) => Hash(string.Join('\n', lines));

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await GraphStore.DisposeAsync();
            await _decisionStore.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
