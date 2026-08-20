using OpenSorSe.Application.Content;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies Forget coordinates schema-6 authority and every rebuildable compatibility cache.</summary>
public sealed class IndexPrivacyDeletionCoordinatorTests
{
    /// <summary>Verifies a file Forget clears every legacy path-keyed authority after SQLite deletion.</summary>
    [Fact]
    public async Task ForgetFile_ClearsEveryCompatibilityAuthority()
    {
        var inner = new PrivacyService(applied: true);
        var content = new ContentStore();
        var semantic = new SemanticStore();
        var thumbnails = new ThumbnailProvider();
        var coordinator = new IndexPrivacyDeletionCoordinator(inner, content, semantic, thumbnails);

        var result = await coordinator.ForgetFileAsync("file:stable");

        Assert.True(result.Applied);
        Assert.Equal(1, content.ClearCount);
        Assert.Equal(1, semantic.ClearCount);
        Assert.Equal(1, thumbnails.ClearCount);
    }

    /// <summary>Verifies a retry completes compatibility erasure even when SQLite already reports no remaining row.</summary>
    [Fact]
    public async Task ForgetRetry_WhenAuthoritativeRowAlreadyGone_StillClearsCompatibilityState()
    {
        var content = new ContentStore();
        var semantic = new SemanticStore();
        var thumbnails = new ThumbnailProvider();
        var coordinator = new IndexPrivacyDeletionCoordinator(
            new PrivacyService(applied: false),
            content,
            semantic,
            thumbnails);

        var result = await coordinator.ForgetSourceAsync("source:stable");

        Assert.False(result.Applied);
        Assert.Equal(1, content.ClearCount);
        Assert.Equal(1, semantic.ClearCount);
        Assert.Equal(1, thumbnails.ClearCount);
    }

    private sealed class ContentStore : IContentStore
    {
        public int ClearCount { get; private set; }
        public Task<ContentRecord?> GetAsync(string fullPath, CancellationToken cancellationToken) => Task.FromResult<ContentRecord?>(null);
        public Task<IReadOnlyList<ContentRecord>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContentRecord>>([]);
        public Task UpsertAsync(ContentRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveMissingAsync(IReadOnlyCollection<string> knownPaths, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class SemanticStore : ISemanticIndexStore
    {
        public int ClearCount { get; private set; }
        public Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SemanticIndexEntry>>([]);
        public Task ReplaceAsync(IReadOnlyList<SemanticIndexEntry> entries, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThumbnailProvider : IMediaThumbnailProvider
    {
        public int ClearCount { get; private set; }
        public Task<MediaCapability> DetectCapabilityAsync(MediaKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<string?> GetThumbnailAsync(string fullPath, IndexedMediaEvidence evidence, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class PrivacyService(bool applied) : IIndexPrivacyService
    {
        private readonly IndexPrivacyOperationResult _result = new(applied, "source:stable", applied ? 1 : 0, "bounded");
        public Task<IndexPrivacyItem?> InspectFileAsync(string fileId, CancellationToken cancellationToken = default) => Task.FromResult<IndexPrivacyItem?>(null);
        public Task<IReadOnlyList<IndexPrivacyItem>> InspectSourceAsync(string sourceId, int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IndexPrivacyItem>>([]);
        public Task<IndexPrivacyOperationResult> ForgetFileAsync(string fileId, CancellationToken cancellationToken = default) => Task.FromResult(_result);
        public Task<IndexPrivacyOperationResult> ForgetSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.FromResult(_result);
        public Task<IndexPrivacyOperationResult> SetFilePolicyAsync(string fileId, IndexPrivacyPolicyChange change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IndexPrivacyOperationResult> ClearFileDataAsync(string fileId, IndexedDataKind data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IndexPrivacyOperationResult> RepairFileAsync(string fileId, IndexRepairKind repair, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IndexPrivacyOperationResult> RepairSourceAsync(string sourceId, IndexRepairKind repair, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
