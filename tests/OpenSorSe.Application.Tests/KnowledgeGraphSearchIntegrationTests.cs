using System.Data.Common;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies the optional graph supplement cannot weaken ordinary Search guarantees.</summary>
public sealed class KnowledgeGraphSearchIntegrationTests
{
    /// <summary>Graph expansion materializes an authorized target by exact ID without widening the ordinary candidate scan.</summary>
    [Fact]
    public async Task GraphExpansionMaterializesTargetOutsideInitialPageAndPreservesExactPriority()
    {
        var progressive = new ProgressiveLookup(
            [Document("invoice", "mercedes-invoice.pdf")],
            [Document("warranty", "warranty-card.txt")]);
        var graph = new GraphSource();
        var service = CreateService(progressive, graph);

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf"),
            CancellationToken.None);

        Assert.Equal(
            ["invoice", "warranty"],
            result.Hits.Select(hit => Assert.IsType<string>(hit.FileId)).ToArray());
        Assert.Equal(["warranty"], progressive.RequestedIds);
        var component = Assert.Single(
            result.Hits[1].RankingComponents!,
            item => item.Kind == SearchRankingSignalKind.GraphContext);
        Assert.Equal("Same exact-content document set.", component.Explanation);
        Assert.True(graph.LastRequest!.SeedFileIds.Count <= GraphLimits.MaximumSearchSeeds);
        Assert.True(graph.LastRequest.MaximumExpansions <= GraphLimits.MaximumGraphSearchExpansions);
        Assert.NotNull(result.GraphCoverage);
    }

    /// <summary>The graph switch is independent and prevents provider access when disabled.</summary>
    [Fact]
    public async Task GraphExpansionCanBeDisabledPerRequest()
    {
        var progressive = new ProgressiveLookup(
            [Document("invoice", "mercedes-invoice.pdf")],
            [Document("warranty", "warranty-card.txt")]);
        var graph = new GraphSource();
        var service = CreateService(progressive, graph);

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf") { IncludeGraphContext = false },
            CancellationToken.None);

        Assert.Equal("invoice", Assert.Single(result.Hits).FileId);
        Assert.Equal(0, graph.InvocationCount);
        Assert.Null(result.GraphCoverage);
    }

    /// <summary>Existing v1.9 relationship authority wins when both sources name the same contextual target.</summary>
    [Fact]
    public async Task DirectRelationshipWinsDuplicateGraphExpansion()
    {
        var progressive = new ProgressiveLookup(
            [Document("invoice", "mercedes-invoice.pdf")],
            [Document("warranty", "warranty-card.txt")]);
        var service = CreateService(progressive, new GraphSource(), new RelationshipSource());

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf"),
            CancellationToken.None);

        var contextual = Assert.Single(result.Hits, hit => hit.FileId == "warranty");
        Assert.Contains(contextual.RankingComponents!, item => item.Kind == SearchRankingSignalKind.RelationshipContext);
        Assert.DoesNotContain(contextual.RankingComponents!, item => item.Kind == SearchRankingSignalKind.GraphContext);
    }

    /// <summary>Stale graph output is withheld rather than presented as current evidence.</summary>
    [Fact]
    public async Task StaleGraphExpansionIsNotReturned()
    {
        var progressive = new ProgressiveLookup(
            [Document("invoice", "mercedes-invoice.pdf")],
            [Document("warranty", "warranty-card.txt")]);
        var service = CreateService(progressive, new GraphSource(GraphFreshnessState.Stale));

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf"),
            CancellationToken.None);

        Assert.Equal("invoice", Assert.Single(result.Hits).FileId);
        Assert.Empty(progressive.RequestedIds);
    }

    /// <summary>No-result Search still reports graph projection coverage separately.</summary>
    [Fact]
    public async Task EmptyOrdinaryCorpusStillReportsGraphCoverage()
    {
        var graph = new GraphSource();
        var service = CreateService(new ProgressiveLookup([], []), graph);

        var result = await service.SearchAsync(new SearchRequest("missing"), CancellationToken.None);

        Assert.Equal(SemanticState.Empty, result.State);
        Assert.Empty(result.Hits);
        Assert.NotNull(result.GraphCoverage);
        Assert.True(result.GraphCoverage!.IsAvailable);
        Assert.Empty(graph.LastRequest!.SeedFileIds);
    }

    /// <summary>A busy, locked, or corrupt graph sidecar cannot block ordinary local Search.</summary>
    [Theory]
    [MemberData(nameof(RecoverableGraphFailures))]
    public async Task GraphFailureFallsBackToOrdinarySearch(Exception failure)
    {
        var progressive = new ProgressiveLookup([Document("invoice", "mercedes-invoice.pdf")], []);
        var service = CreateService(progressive, new GraphSource(failure: failure));

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf"),
            CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Equal("invoice", Assert.Single(result.Hits).FileId);
        Assert.NotNull(result.GraphCoverage);
        Assert.False(result.GraphCoverage!.IsAvailable);
    }

    /// <summary>A provider-specific target-resolution failure cannot take down ordinary Search.</summary>
    [Fact]
    public async Task GraphTargetLookupFailureFallsBackToAlreadyLoadedResults()
    {
        var progressive = new ProgressiveLookup(
            [Document("invoice", "mercedes-invoice.pdf")],
            [Document("warranty", "warranty-card.txt")],
            new Exception("synthetic provider-specific lookup failure"));
        var service = CreateService(progressive, new GraphSource());

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf"),
            CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Equal("invoice", Assert.Single(result.Hits).FileId);
        Assert.Equal(["warranty"], progressive.RequestedIds);
    }

    /// <summary>Gets representative recoverable sidecar failures.</summary>
    public static TheoryData<Exception> RecoverableGraphFailures => new()
    {
        new IOException("synthetic graph failure"),
        new InvalidDataException("synthetic graph corruption"),
        new SyntheticDatabaseException(),
        new Exception("synthetic provider-specific failure"),
    };

    private static SemanticSearchService CreateService(
        ProgressiveLookup progressive,
        IGraphSearchSource graph,
        IRelationshipSearchSource? relationships = null) =>
        new(
            new Configuration(),
            new FeatureHashingEmbeddingProvider(),
            new EmptySemanticStore(),
            progressive,
            relationshipSearchSource: relationships,
            graphSearchSource: graph,
            searchDocumentLookup: progressive);

    private static ProgressiveSearchDocument Document(string id, string fileName) => new()
    {
        FileId = id,
        FullPath = Path.Combine(Path.GetTempPath(), "OpenSorSe-synthetic", fileName),
        FileName = fileName,
        RelativePath = fileName,
        FolderName = "OpenSorSe-synthetic",
        IsFullyIndexed = true,
    };

    private sealed class Configuration : IConfigurationService
    {
        public ApplicationSettings Current { get; } = new()
        {
            SemanticSearch = new SemanticSearchSettings
            {
                Enabled = true,
                MaximumDocumentCount = 100,
                MaximumResultCount = 20,
            },
        };

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptySemanticStore : ISemanticIndexStore
    {
        public Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SemanticIndexEntry>>([]);

        public Task ReplaceAsync(IReadOnlyList<SemanticIndexEntry> entries, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ProgressiveLookup(
        IReadOnlyList<ProgressiveSearchDocument> ordinary,
        IReadOnlyList<ProgressiveSearchDocument> lookup,
        Exception? lookupFailure = null) :
        IProgressiveSearchSource,
        IProgressiveSearchDocumentLookup
    {
        public IReadOnlyList<string> RequestedIds { get; private set; } = [];

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(ordinary.Take(maximumCount).ToArray());

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
            IReadOnlyList<string> fileIds,
            CancellationToken cancellationToken = default)
        {
            RequestedIds = fileIds.ToArray();
            if (lookupFailure is not null)
            {
                return Task.FromException<IReadOnlyList<ProgressiveSearchDocument>>(lookupFailure);
            }

            var requested = fileIds.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(
                lookup.Where(item => requested.Contains(item.FileId)).ToArray());
        }

        public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SearchCoverage(ordinary.Count + lookup.Count, ordinary.Count + lookup.Count, 0, 0, 0, ordinary.Count + lookup.Count));

        public Task<IReadOnlyList<string>> GetExcludedPathsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class GraphSource(
        GraphFreshnessState freshness = GraphFreshnessState.Current,
        Exception? failure = null) : IGraphSearchSource
    {
        public int InvocationCount { get; private set; }
        public GraphSearchRequest? LastRequest { get; private set; }

        public Task<GraphSearchResult> ExpandAsync(
            GraphSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            LastRequest = request;
            if (failure is not null)
            {
                return Task.FromException<GraphSearchResult>(failure);
            }

            var expansions = request.SeedFileIds.Contains("invoice", StringComparer.Ordinal)
                ? new[]
                {
                    new GraphSearchExpansion(
                        "invoice",
                        "warranty",
                        "edge-1",
                        GraphEdgeKind.SameDocumentSet,
                        GraphConfidenceLevel.High,
                        "Same exact-content document set.",
                        7,
                        freshness),
                }
                : [];
            return Task.FromResult(new GraphSearchResult(
                expansions,
                new GraphProjectionCoverage(true, true, true, false, 2, 2, 0, 0, "manifest-1", 7, "Current"),
                true,
                "Current"));
        }
    }

    private sealed class RelationshipSource : IRelationshipSearchSource
    {
        public Task<IReadOnlyList<RelationshipSearchExpansion>> ExpandAsync(
            IReadOnlyList<string> seedFileIds,
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RelationshipSearchExpansion>>(
            [
                new RelationshipSearchExpansion(
                    "invoice",
                    "warranty",
                    RelationshipType.SamePurchase,
                    RelationshipConfidence.High,
                    "Same retained invoice number.",
                    "Mercedes Purchase"),
            ]);
    }

    private sealed class SyntheticDatabaseException() : DbException("synthetic graph database busy");
}
