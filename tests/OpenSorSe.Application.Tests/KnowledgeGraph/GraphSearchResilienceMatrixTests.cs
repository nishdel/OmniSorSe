using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>
/// Exercises graph Search availability, expansion, cancellation, traversal bounds, and
/// diagnostic-redaction gates without using source files or external dependencies.
/// </summary>
public sealed class GraphSearchResilienceMatrixTests
{
    /// <summary>Bootstrap, stale, repair-required, disabled, and corrupt coverage all fail optional expansion closed.</summary>
    [Theory]
    [MemberData(nameof(UnavailableCoverageStates))]
    public async Task Search_UnavailableGraphStates_ReturnNoContext(GraphProjectionCoverage coverage)
    {
        var store = ReadyStore();
        store.Coverage = coverage;
        store.SearchExpansions.Add(Expansion("file-1", "file-2", "edge-1", GraphConfidenceLevel.High));
        var service = SearchSource(store);

        var result = await service.ExpandAsync(new GraphSearchRequest(["file-1"]));

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Expansions);
        Assert.False(result.Coverage.IsAvailable);
        Assert.Contains("unavailable", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Authoritative opt-out disables the graph supplement even when a stale provider mirror says enabled.</summary>
    [Fact]
    public async Task Search_AuthoritativeDisable_ReturnsNoContext()
    {
        var decisions = new FakeGraphDecisionStore { ControlSettings = new GraphControlSettings() };
        var store = ReadyStore();
        store.SearchExpansions.Add(Expansion("file-1", "file-2", "edge-1", GraphConfidenceLevel.High));
        var service = new GraphSearchSource(store, new FakeGraphProjectionSource(), decisions);

        var result = await service.ExpandAsync(new GraphSearchRequest(["file-1"]));

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Expansions);
        Assert.Contains("graph-disabled", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Duplicate logical targets collapse deterministically to their strongest actual retained explanation.</summary>
    [Fact]
    public async Task Search_DuplicateLogicalExpansions_SelectActualStrongestEvidence()
    {
        var store = ReadyStore();
        store.SearchExpansions.AddRange(
        [
            Expansion("file-1", "file-2", "edge-low", GraphConfidenceLevel.Low, "Shared folder only."),
            Expansion("file-1", "file-2", "edge-high-z", GraphConfidenceLevel.High, "Same exact-content document set."),
            Expansion("file-1", "file-2", "edge-high-a", GraphConfidenceLevel.High, "Same retained invoice number."),
        ]);
        var service = SearchSource(store);

        var first = await service.ExpandAsync(new GraphSearchRequest(["file-1"]));
        var second = await service.ExpandAsync(new GraphSearchRequest(["file-1"]));

        var selected = Assert.Single(first.Expansions);
        Assert.Equal("edge-high-a", selected.EdgeId);
        Assert.Equal(GraphConfidenceLevel.High, selected.Confidence);
        Assert.Equal("Same retained invoice number.", selected.Explanation);
        Assert.Equal(first.Expansions, second.Expansions);
    }

    /// <summary>High-degree provider output remains bounded and ordered by deterministic public rules.</summary>
    [Fact]
    public async Task Search_HighDegreeExpansion_IsBoundedAndStable()
    {
        var store = ReadyStore();
        store.SearchExpansions.AddRange(Enumerable.Range(0, GraphLimits.MaximumGraphSearchExpansions)
            .Reverse()
            .Select(index => Expansion(
                "file-1",
                string.Concat("related-", index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)),
                string.Concat("edge-", index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)),
                index % 3 == 0 ? GraphConfidenceLevel.High : GraphConfidenceLevel.Medium)));
        var service = SearchSource(store);

        var first = await service.ExpandAsync(new GraphSearchRequest(
            ["file-1"],
            GraphLimits.MaximumGraphSearchExpansions));
        var second = await service.ExpandAsync(new GraphSearchRequest(
            ["file-1"],
            GraphLimits.MaximumGraphSearchExpansions));

        Assert.Equal(GraphLimits.MaximumGraphSearchExpansions, first.Expansions.Count);
        Assert.Equal(first.Expansions, second.Expansions);
        Assert.Equal(
            first.Expansions
                .OrderByDescending(item => item.Confidence)
                .ThenBy(item => item.RelatedFileId, StringComparer.Ordinal),
            first.Expansions);
    }

    /// <summary>Search seed count, uniqueness, identifier, and expansion ceilings reject graph-explosion input.</summary>
    [Fact]
    public async Task Search_RequestBounds_RejectPathologicalSeedsAndExpansionCounts()
    {
        var service = SearchSource(ReadyStore());
        var tooManySeeds = Enumerable.Range(0, GraphLimits.MaximumSearchSeeds + 1)
            .Select(index => string.Concat("file-", index))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExpandAsync(new GraphSearchRequest(tooManySeeds)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExpandAsync(new GraphSearchRequest(["file-1", "file-1"])));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExpandAsync(new GraphSearchRequest(["file-1\0hostile"])));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ExpandAsync(new GraphSearchRequest(["file-1"], GraphLimits.MaximumGraphSearchExpansions + 1)));
    }

    /// <summary>An already-cancelled Search never returns graph context or converts cancellation to fallback.</summary>
    [Fact]
    public async Task Search_AlreadyCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = SearchSource(ReadyStore());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExpandAsync(new GraphSearchRequest(["file-1"]), cancellation.Token));
    }

    /// <summary>Rapid overlapping identical expansions remain deterministic and independently bounded.</summary>
    [Fact]
    public async Task Search_RapidOverlappingQueries_ReturnStableIndependentResults()
    {
        var store = ReadyStore();
        store.SearchExpansions.AddRange(
        [
            Expansion("file-1", "file-3", "edge-3", GraphConfidenceLevel.Medium),
            Expansion("file-1", "file-2", "edge-2", GraphConfidenceLevel.High),
        ]);
        var service = SearchSource(store);

        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => service.ExpandAsync(new GraphSearchRequest(["file-1"]))));

        Assert.All(results, result =>
        {
            Assert.True(result.IsAvailable);
            Assert.Equal(["file-2", "file-3"], result.Expansions.Select(item => item.RelatedFileId));
        });
    }

    /// <summary>Unavailable privacy authority withholds every concurrent graph supplement without leaking expansions.</summary>
    [Fact]
    public async Task Search_PrivacyAuthorityUnavailableDuringConcurrentQueries_FailsRestrictive()
    {
        var source = new FakeGraphProjectionSource
        {
            Authority = new GraphAuthoritySnapshot(false, false, 2, "legacy-1", "privacy-authority-unavailable")
            {
                CurrentSourceManifestId = "manifest-1",
                CurrentSourceRevision = 1,
            },
        };
        var store = ReadyStore();
        store.SearchExpansions.Add(Expansion("file-1", "private-file", "edge-private", GraphConfidenceLevel.High));
        var service = new GraphSearchSource(store, source, new FakeGraphDecisionStore());

        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => service.ExpandAsync(new GraphSearchRequest(["file-1"]))));

        Assert.All(results, result =>
        {
            Assert.False(result.IsAvailable);
            Assert.Empty(result.Expansions);
            Assert.False(result.Coverage.IsAvailable);
        });
    }

    /// <summary>Ordinary filename Search stays useful under concurrent restrictive graph fallbacks.</summary>
    [Fact]
    public async Task Search_PrivacyAuthorityUnavailable_OrdinaryConcurrentSearchStillReturnsExactFile()
    {
        var source = new FakeGraphProjectionSource
        {
            Authority = new GraphAuthoritySnapshot(false, false, 2, "legacy-1", "privacy-authority-unavailable")
            {
                CurrentSourceManifestId = "manifest-1",
                CurrentSourceRevision = 1,
            },
        };
        var graph = new GraphSearchSource(ReadyStore(), source, new FakeGraphDecisionStore());
        var documents = new SyntheticProgressiveSource(
        [
            new ProgressiveSearchDocument
            {
                FileId = "invoice",
                FullPath = Path.Combine(Path.GetTempPath(), "OmniSorSe-synthetic", "mercedes-invoice.pdf"),
                FileName = "mercedes-invoice.pdf",
                RelativePath = "mercedes-invoice.pdf",
                FolderName = "OmniSorSe-synthetic",
                IsFullyIndexed = true,
            },
        ]);
        var service = new SemanticSearchService(
            new SyntheticConfiguration(),
            new FeatureHashingEmbeddingProvider(),
            new EmptySemanticStore(),
            documents,
            graphSearchSource: graph,
            searchDocumentLookup: documents);

        var results = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => service.SearchAsync(new SearchRequest("mercedes-invoice.pdf"), CancellationToken.None)));

        Assert.All(results, result =>
        {
            var hit = Assert.Single(result.Hits);
            Assert.Equal("invoice", hit.FileId);
            Assert.NotNull(result.GraphCoverage);
            Assert.False(result.GraphCoverage!.IsAvailable);
        });
    }

    /// <summary>Impossible aggregate coverage counts fail closed before they can drive progress or Search messaging.</summary>
    [Fact]
    public async Task Query_ImpossibleCoverageCounts_FailClosed()
    {
        var store = ReadyStore();
        store.Coverage = store.Coverage with
        {
            ProjectedObservationCount = long.MaxValue,
            TotalObservationCount = 1,
            FailedCount = long.MaxValue,
            WaitingCount = long.MaxValue,
        };
        var service = new GraphQueryService(store, new FakeGraphProjectionSource(), new FakeGraphDecisionStore());

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() => service.GetCoverageAsync());

        Assert.Equal("graph-store-invalid", error.ReasonCode);
    }

    /// <summary>Default diagnostics reject a synthetic absolute path in a provider-controlled category.</summary>
    [Fact]
    public async Task Diagnostics_AbsolutePathCategory_IsRejected()
    {
        var store = ReadyStore();
        store.DiagnosticsOverride = new GraphDiagnosticsSnapshot
        {
            ProviderCode = "C:\\SyntheticPrivate\\document.txt",
            Coverage = store.Coverage,
            StorageBreakdown = GraphStorageBreakdown.Empty,
        };

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            new GraphDiagnosticsService(store).GetSnapshotAsync());

        Assert.Equal("graph-diagnostics-invalid", error.ReasonCode);
    }

    /// <summary>Default diagnostics reject synthetic secret-bearing failure text instead of exporting it.</summary>
    [Fact]
    public async Task Diagnostics_SecretBearingFailureCategory_IsRejected()
    {
        var store = ReadyStore();
        store.DiagnosticsOverride = new GraphDiagnosticsSnapshot
        {
            LastFailureCategory = "token=synthetic-secret-value",
            Coverage = store.Coverage,
            StorageBreakdown = GraphStorageBreakdown.Empty,
        };

        var error = await Assert.ThrowsAsync<GraphAccessUnavailableException>(() =>
            new GraphDiagnosticsService(store).GetSnapshotAsync());

        Assert.Equal("graph-diagnostics-invalid", error.ReasonCode);
    }

    /// <summary>Provides synthetic graph states that must never be presented as authoritative expansion coverage.</summary>
    public static TheoryData<GraphProjectionCoverage> UnavailableCoverageStates => new()
    {
        ReadyCoverage() with
        {
            IsAvailable = false,
            IsComplete = false,
            Message = "bootstrap",
        },
        ReadyCoverage() with
        {
            IsStale = true,
            Message = "stale",
        },
        ReadyCoverage() with
        {
            IsAvailable = false,
            IsStale = true,
            FailedCount = 1,
            Message = "repair-required",
        },
        ReadyCoverage() with
        {
            IsEnabled = false,
            IsAvailable = false,
            Message = "disabled",
        },
        ReadyCoverage() with
        {
            ProjectedObservationCount = -1,
            Message = "corrupt",
        },
    };

    private static GraphSearchSource SearchSource(FakeGraphStore store) =>
        new(store, new FakeGraphProjectionSource(), new FakeGraphDecisionStore());

    private static GraphSearchExpansion Expansion(
        string seed,
        string related,
        string edge,
        GraphConfidenceLevel confidence,
        string explanation = "Exact retained graph evidence.") => new(
            seed,
            related,
            edge,
            GraphEdgeKind.RelatedFile,
            confidence,
            explanation,
            1,
            GraphFreshnessState.Current);

    private static FakeGraphStore ReadyStore() => new() { Coverage = ReadyCoverage() };

    private static GraphProjectionCoverage ReadyCoverage() => TestGraphData.Coverage with
    {
        IsEnabled = true,
        IsAvailable = true,
        IsComplete = true,
        IsStale = false,
        ProjectedObservationCount = 2,
        TotalObservationCount = 2,
        IngestedManifestId = "manifest-1",
        IngestedRevision = 1,
        AppliedManifestId = "manifest-1",
        AppliedRevision = 1,
        Message = "current",
    };

    private sealed class SyntheticConfiguration : IConfigurationService
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

    private sealed class SyntheticProgressiveSource(IReadOnlyList<ProgressiveSearchDocument> documents) :
        IProgressiveSearchSource,
        IProgressiveSearchDocumentLookup
    {
        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(documents.Take(maximumCount).ToArray());
        }

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
            IReadOnlyList<string> fileIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = fileIds.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(
                documents.Where(item => requested.Contains(item.FileId)).ToArray());
        }

        public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SearchCoverage(
                documents.Count,
                documents.Count,
                0,
                0,
                0,
                documents.Count));
        }

        public Task<IReadOnlyList<string>> GetExcludedPathsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }
}
