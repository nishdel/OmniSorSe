using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Resilience;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Lifecycle;
using OpenSorSe.Core.Platform;
using OpenSorSe.Executor;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies bounded health aggregation without document hydration or repair.</summary>
public sealed class OperationalHealthServiceTests
{
    /// <summary>Healthy state stays cheap while ownership/index/storage faults produce stable issue codes.</summary>
    [Fact]
    public async Task CheckAsync_UsesMetadataOnlyAndSurfacesRecoveryConditions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"OmniSorSe-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var indexing = new Indexing(root, failed: 0);
            var healthy = CreateService(
                root,
                indexing,
                new ProfileState(ProfileOwnershipStatus.Owned),
                new RunState(false),
                new IndexHealth(true));

            var healthySnapshot = await healthy.CheckAsync();

            Assert.Equal(OperationalHealthState.Healthy, healthySnapshot.State);
            Assert.Empty(healthySnapshot.Issues);
            Assert.Equal(0, indexing.DocumentHydrationCount);

            var unhealthyIndexing = new Indexing(Path.Combine(root, "missing-source"), failed: 2);
            var unhealthy = CreateService(
                root,
                unhealthyIndexing,
                ProfileOwnershipLease.NotRequired,
                new RunState(true),
                new IndexHealth(false));

            var snapshot = await unhealthy.CheckAsync();

            Assert.Equal(OperationalHealthState.RecoveryRequired, snapshot.State);
            Assert.Equal(1, snapshot.UnreachableSourceCount);
            Assert.Equal(2, snapshot.FailedJobCount);
            Assert.Contains(snapshot.Issues, issue => issue.Code == "profile-ownership");
            Assert.Contains(snapshot.Issues, issue => issue.Code == "sqlite-integrity");
            Assert.Contains(snapshot.Issues, issue => issue.Code == "previous-shutdown");
            Assert.Contains(snapshot.Issues, issue => issue.Code == "source-reachability");
            Assert.Contains(snapshot.Issues, issue => issue.Code == "failed-index-jobs");
            Assert.Equal(0, unhealthyIndexing.DocumentHydrationCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static OperationalHealthService CreateService(
        string root,
        IBackgroundIndexingService indexing,
        IProfileOwnershipState ownership,
        IApplicationRunState runState,
        IDeepIndexHealthProbe indexHealth) =>
        new(
            new Configuration(),
            indexing,
            indexHealth,
            new PathProvider(root),
            ownership,
            RecoverySafetyState.Unmanaged,
            runState,
            new SavedViews(),
            new WatchedFolders(),
            new Workflows());

    private sealed class Configuration : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new();
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class PathProvider : IApplicationPathProvider
    {
        public PathProvider(string root)
        {
            Directory.CreateDirectory(root);
            this.Paths = new ApplicationPathSet(root, root, root, root, root, root);
        }

        public ApplicationPathSet Paths { get; }
        public string SettingsFilePath => Path.Combine(Paths.ConfigurationDirectory, "settings.json");
        public void EnsureOwnedDirectories() { }
    }

    private sealed record ProfileState(ProfileOwnershipStatus Status) : IProfileOwnershipState
    {
        public string Message => "test";
        public string ProfileFingerprint => "test";
    }

    private sealed record RunState(bool PreviousShutdownWasAbnormal) : IApplicationRunState
    {
        public bool HadPreviousRun => PreviousShutdownWasAbnormal;
        public Task MarkCleanAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record IndexHealth(bool Healthy) : IDeepIndexHealthProbe
    {
        public Task<DeepIndexHealthSnapshot> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeepIndexHealthSnapshot(Healthy, 6, Healthy, Healthy ? "healthy" : "recovery required"));
    }

    private sealed class SavedViews : ISavedDiscoveryViewStore
    {
        public Task<IReadOnlyList<SavedDiscoveryView>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SavedDiscoveryView>>([]);
        public Task<SavedDiscoveryView> SaveAsync(SavedDiscoveryView view, CancellationToken cancellationToken = default) => Task.FromResult(view);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class WatchedFolders : IWatchedFolderConfigurationStore
    {
        public Task<IReadOnlyList<WatchedFolderConfiguration>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WatchedFolderConfiguration>>([]);
        public Task SaveAsync(IReadOnlyList<WatchedFolderConfiguration> configurations, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Workflows : IWorkflowLibraryStore
    {
        public Task<WorkflowLibraryLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowLibraryLoadResult([], [], null, null, false));
        public Task SaveAsync(IReadOnlyList<WorkflowProfile> profiles, IReadOnlyList<SortingRecipe> recipes, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Indexing(string sourceRoot, long failed) : IBackgroundIndexingService
    {
        public event EventHandler<IndexingProgressSnapshot>? ProgressChanged { add { } remove { } }
        public int DocumentHydrationCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> QueueFolderAsync(string rootPath, IndexingLevel? level = null, bool includeSubfolders = true, IReadOnlyList<string>? exclusions = null, CancellationToken cancellationToken = default) => Task.FromResult("source");
        public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexingSource>>([new("source", sourceRoot, "Source", IndexingLevel.Standard, true, true, 0, [])]);
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelAsync(string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task PrioritizeSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IndexingProgressSnapshot> GetProgressAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexingProgressSnapshot { Failed = failed });
        public Task<IndexStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexStorageBreakdown(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        public Task<IReadOnlyList<IndexingFailure>> GetFailuresAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexingFailure>>([]);
        public Task<IndexMaintenanceResult> MaintainAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexMaintenanceResult([], new IndexStorageBreakdown(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), true));
        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(int maximumCount, CancellationToken cancellationToken = default)
        {
            DocumentHydrationCount++;
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);
        }
        public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SearchCoverage(0, 0, 0, 0, 0, 0));
        public void Dispose() => GC.SuppressFinalize(this);
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
