using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Resilience;
using OpenSorSe.Application.Relationships;
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

            var relationshipAttention = CreateService(
                root,
                indexing,
                new ProfileState(ProfileOwnershipStatus.Owned),
                new RunState(false),
                new IndexHealth(true),
                new Relationships());

            var relationshipSnapshot = await relationshipAttention.CheckAsync();

            var relationshipIssue = Assert.Single(
                relationshipSnapshot.Issues,
                issue => issue.Code == "relationship-reanalysis");
            Assert.Equal(OperationalHealthState.Attention, relationshipIssue.State);
            Assert.Contains("7 stale file(s)", relationshipIssue.Summary, StringComparison.Ordinal);
            Assert.Contains("2 invalid record(s)", relationshipIssue.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("filename", relationshipIssue.Summary, StringComparison.OrdinalIgnoreCase);
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
        IDeepIndexHealthProbe indexHealth,
        IRelationshipStore? relationships = null) =>
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
            new Workflows(),
            relationships);

    private sealed class Relationships : IRelationshipStore
    {
        public Task<RelationshipFileDocument?> GetRelationshipFileAsync(string fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertRelationshipFeaturesAsync(RelationshipFeatureSet features, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RelationshipFileDocument>> GetRelationshipCandidatesAsync(RelationshipFeatureSet target, int maximumCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveRelationshipAnalysisAsync(RelationshipAnalysisBatch batch, int maximumCollectionMembers, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RelationshipFileDocument>> GetRelationshipFilesAsync(int maximumCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(string fileId, RelationshipType? type, RelationshipConfidence? minimumConfidence, RelatedFileSort sort, int maximumCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileRelationship?> GetRelationshipAsync(string relationshipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SmartCollection>> GetCollectionsAsync(int maximumCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SmartCollectionDetails?> GetCollectionAsync(string collectionId, int maximumMembers, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> LinkFilesAsync(string firstFileId, string secondFileId, RelationshipType type, string? customType, bool alwaysRelate, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> UnlinkFilesAsync(string relationshipId, bool neverRelate, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> SetRelationshipDecisionAsync(string relationshipId, RelationshipDecision decision, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> RenameCollectionAsync(string collectionId, string title, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> SetCollectionPinnedAsync(string collectionId, bool pinned, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> MergeCollectionsAsync(string targetCollectionId, string sourceCollectionId, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> SplitCollectionMemberAsync(string collectionId, string fileId, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> ForgetCollectionAsync(string collectionId, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> ForgetFileRelationshipsAsync(string fileId, bool excludeFutureAnalysis, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> ForgetSourceRelationshipsAsync(string sourceId, bool excludeFutureAnalysis, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipOperationResult> PrepareRelationshipRebuildAsync(string fileId, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RelationshipSearchExpansion>> GetSearchExpansionsAsync(IReadOnlyList<string> seedFileIds, int maximumCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RelationshipDiagnosticsSnapshot> GetRelationshipDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RelationshipDiagnosticsSnapshot(4, 1, 5, 0, 0, 0, DateTimeOffset.UtcNow, TimeSpan.Zero, 0, 0, 0, "3.0.0", 0)
            {
                StaleRelationshipFileCount = 7,
                InvalidRecordCount = 2,
                RepairNeeded = true,
            });
        public Task<RelationshipOperationResult> RepairRelationshipsAsync(DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

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
