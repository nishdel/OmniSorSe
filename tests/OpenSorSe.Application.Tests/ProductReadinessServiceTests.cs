using OpenSorSe.Application.Explorer;
using OpenSorSe.Application.Guidance;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies Home uses bounded durable projections and non-invasive capability discovery.</summary>
public sealed class ProductReadinessServiceTests
{
    /// <summary>Durable counts and shortcuts are projected without hydrating a file graph or evaluating Saved Views.</summary>
    [Fact]
    public async Task GetSnapshotAsync_UsesBoundedCountsAndDoesNotExecuteSavedViews()
    {
        var indexing = new Indexing
        {
            Progress = new IndexingProgressSnapshot
            {
                Status = IndexingRunStatus.Running,
                DiscoveryComplete = true,
                Coverage = new SearchCoverage(25_000, 25_000, 4_000, 0, 0, 0),
            },
            DiscoveryResult = new ProgressiveDiscoveryResult(
                [],
                new SearchCandidateCoverage(25_000, 37, 0, false, true)),
        };
        var savedViews = new SavedViews(Enumerable.Range(1, 5)
            .Select(index => new SavedDiscoveryView(
                $"view:{index}",
                $"View {index}",
                new DiscoveryQueryState("private query", []),
                1,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(index)))
            .ToArray());
        var service = new ProductReadinessService(
            indexing,
            savedViews,
            new Configuration(new ApplicationSettings()),
            new Tools(),
            new Companion());

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(25_000, snapshot.KnownFileCount);
        Assert.True(snapshot.IsBaseSearchReady);
        Assert.Equal(IndexingProgressPhase.DeeperAnalysis, snapshot.Phase);
        Assert.Equal(37, snapshot.PendingReviewCount);
        Assert.Equal(5, snapshot.SavedViewCount);
        Assert.Equal(["view:5", "view:4", "view:3"], snapshot.SavedViewShortcuts.Select(view => view.Id));
        Assert.Equal(1, savedViews.ListCount);
        Assert.NotNull(indexing.LastDiscoveryRequest);
        Assert.Equal(1, indexing.LastDiscoveryRequest!.MaximumCandidateCount);
        Assert.Equal(SearchFilterKind.UnresolvedModerateSmartTag, Assert.Single(indexing.LastDiscoveryRequest.Filters).Kind);
        Assert.Equal(0, indexing.DocumentHydrationCount);
    }

    /// <summary>Home capability inspection reports local configuration only and never executes a tool.</summary>
    [Fact]
    public async Task GetSnapshotAsync_CapabilitiesAreOptionalAndNonExecuting()
    {
        var tools = new Tools();
        var service = new ProductReadinessService(
            new Indexing(),
            new SavedViews([]),
            new Configuration(new ApplicationSettings()),
            tools,
            new Companion());

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(6, snapshot.Capabilities.Count);
        Assert.All(snapshot.Capabilities, capability => Assert.False(string.IsNullOrWhiteSpace(capability.Explanation)));
        Assert.Equal(0, tools.ExecutionCount);
        Assert.Contains(snapshot.Capabilities, capability => capability.Id == "omnibrille" && capability.State == OptionalCapabilityState.NotConfigured);
    }

    private sealed class Indexing : IBackgroundIndexingService
    {
        public event EventHandler<IndexingProgressSnapshot>? ProgressChanged { add { } remove { } }
        public IndexingProgressSnapshot Progress { get; init; } = new();
        public ProgressiveDiscoveryResult DiscoveryResult { get; init; } = new([], SearchCandidateCoverage.Unknown);
        public DiscoverySearchRequest? LastDiscoveryRequest { get; private set; }
        public int DocumentHydrationCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> QueueFolderAsync(string rootPath, IndexingLevel? level = null, bool includeSubfolders = true, IReadOnlyList<string>? exclusions = null, CancellationToken cancellationToken = default) => Task.FromResult("source");
        public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexingSource>>([new("source", "C:\\Docs", "Docs", IndexingLevel.Standard, true, true, 0, [])]);
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelAsync(string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task PrioritizeSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IndexingProgressSnapshot> GetProgressAsync(CancellationToken cancellationToken = default) => Task.FromResult(Progress);
        public Task<IndexStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default) => Task.FromResult(EmptyStorage());
        public Task<IReadOnlyList<IndexingFailure>> GetFailuresAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IndexingFailure>>([]);
        public Task<IndexMaintenanceResult> MaintainAsync(CancellationToken cancellationToken = default) => Task.FromResult(new IndexMaintenanceResult([], EmptyStorage(), true));
        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(int maximumCount, CancellationToken cancellationToken = default)
        {
            DocumentHydrationCount++;
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);
        }
        public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) => Task.FromResult(Progress.Coverage);
        public Task<ProgressiveDiscoveryResult> GetDiscoveryCandidatesAsync(DiscoverySearchRequest request, CancellationToken cancellationToken = default)
        {
            LastDiscoveryRequest = request;
            return Task.FromResult(DiscoveryResult);
        }
        public void Dispose() => GC.SuppressFinalize(this);
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        private static IndexStorageBreakdown EmptyStorage() => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private sealed class SavedViews(IReadOnlyList<SavedDiscoveryView> views) : ISavedDiscoveryViewStore
    {
        public int ListCount { get; private set; }
        public Task<IReadOnlyList<SavedDiscoveryView>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCount++;
            return Task.FromResult(views);
        }
        public Task<SavedDiscoveryView> SaveAsync(SavedDiscoveryView view, CancellationToken cancellationToken = default) => Task.FromResult(view);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class Configuration(ApplicationSettings settings) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = settings;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class Tools : IExternalToolLocator
    {
        public int ExecutionCount => 0;
        public ExternalToolLocation Locate(string commandName, string? configuredPath = null) =>
            new(false, null, "Not configured");
    }

    private sealed class Companion : IExplorerCompanionLocator
    {
        public ExplorerCompanionDiscoveryResult Locate() => new(null, "Not configured");
    }
}
