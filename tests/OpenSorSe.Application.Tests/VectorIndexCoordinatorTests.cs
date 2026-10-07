using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Checks bounded chunk provenance and optional learned-index orchestration.</summary>
public sealed class VectorIndexCoordinatorTests
{
    private static readonly SemanticModelIdentity Model = new("ollama", "test-model", "digest", 2);

    /// <summary>Chunk identities are stable and overlapping offsets resolve exact original evidence.</summary>
    [Fact]
    public void ChunkerPreservesFieldsOffsetsSurrogatesAndOverlap()
    {
        var text = string.Concat(Enumerable.Repeat("text 😀 中文 ", 500));
        var document = new VectorIndexDocument("file", "stamp", [new("native.text", text), new("ai.summary", "AI-derived summary")]);
        var chunks = VectorTextChunker.Create(document);
        Assert.Equal(chunks, VectorTextChunker.Create(document));
        Assert.Contains(chunks, chunk => chunk.Field == "ai.summary");
        foreach (var chunk in chunks)
        {
            var field = document.Fields.Single(item => item.Name == chunk.Field);
            Assert.Equal(field.Text.Substring(chunk.Start, chunk.Length), chunk.Text);
            Assert.InRange(chunk.Length, 1, VectorTextChunker.MaximumChunkCharacters);
            Assert.False(char.IsLowSurrogate(chunk.Text[0]));
            Assert.False(char.IsHighSurrogate(chunk.Text[^1]));
        }
        var native = chunks.Where(chunk => chunk.Field == "native.text").ToArray();
        Assert.True(native[1].Start < native[0].Start + native[0].Length);
        Assert.True(native[1].Start > native[0].Start);
    }

    /// <summary>Encoded Unicode payloads remain bounded by embedding at most eight chunks together.</summary>
    [Fact]
    public async Task CoordinatorBatchesChunksAndCommitsOnlyCompleteModelSpace()
    {
        var store = new StubStore(LongDocument());
        var provider = new StubProvider();
        await using var coordinator = new VectorIndexCoordinator(new StubConfiguration(), store, provider);
        await coordinator.RefreshAsync();
        Assert.Equal(new[] { 8, 8 }, provider.BatchSizes);
        Assert.Equal(1, store.Commits);
        Assert.Equal(16, store.SavedChunks);
        Assert.Equal(VectorIndexState.Idle, coordinator.CurrentStatus.State);
        Assert.Contains("1 files indexed", coordinator.CurrentStatus.Message);
    }

    /// <summary>A model replacement between batches cannot publish a mixed or partial vector set.</summary>
    [Fact]
    public async Task ModelChangeDuringBatchDoesNotCommit()
    {
        var store = new StubStore(LongDocument());
        var provider = new StubProvider { ChangeSecondBatch = true };
        await using var coordinator = new VectorIndexCoordinator(new StubConfiguration(), store, provider);
        await coordinator.RefreshAsync();
        Assert.Equal(0, store.Commits);
        Assert.Equal(VectorIndexState.Unavailable, coordinator.CurrentStatus.State);
    }

    /// <summary>Missing optional model affects only its separate derived coordinator.</summary>
    [Fact]
    public async Task MissingModelPublishesUnavailableWithoutStoreWrites()
    {
        var store = new StubStore(LongDocument());
        var provider = new StubProvider { Available = false };
        await using var coordinator = new VectorIndexCoordinator(new StubConfiguration(), store, provider);
        await coordinator.RefreshAsync();
        Assert.Equal(VectorIndexState.Unavailable, coordinator.CurrentStatus.State);
        Assert.Equal(0, store.Commits);
        Assert.Empty(provider.BatchSizes);
    }

    /// <summary>Cleanup drains pending inference and keeps automatic repopulation paused.</summary>
    [Fact]
    public async Task ReclaimCancelsAndDrainsInferenceBeforeClearing()
    {
        var store = new StubStore(LongDocument());
        var provider = new StubProvider { WaitForCancellation = true };
        await using var coordinator = new VectorIndexCoordinator(new StubConfiguration(), store, provider);
        var refresh = coordinator.RefreshAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.ReclaimAsync();
        await refresh;
        Assert.Equal(0, store.Commits);
        Assert.Equal(1, store.Clears);
        Assert.Equal(VectorIndexState.Paused, coordinator.CurrentStatus.State);
        await coordinator.RefreshAsync();
        Assert.Single(provider.BatchSizes);
    }

    /// <summary>Immediately resuming a paused batch does not turn coordinator-owned cancellation into a user error.</summary>
    [Fact]
    public async Task ImmediateResumeStillHandlesPauseCancellationLocally()
    {
        var provider = new StubProvider { WaitForCancellation = true };
        await using var coordinator = new VectorIndexCoordinator(new StubConfiguration(), new StubStore(LongDocument()), provider);
        var refresh = coordinator.RefreshAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Pause();
        coordinator.Resume();
        await refresh;
        Assert.Equal(VectorIndexState.Idle, coordinator.CurrentStatus.State);
    }

    /// <summary>Caller cancellation leaves no partial commit and remains observable to the caller.</summary>
    [Fact]
    public async Task CallerCancellationDoesNotCommit()
    {
        var store = new StubStore(LongDocument());
        var provider = new StubProvider { WaitForCancellation = true };
        await using var coordinator = new VectorIndexCoordinator(new StubConfiguration(), store, provider);
        using var cancellation = new CancellationTokenSource();
        var refresh = coordinator.RefreshAsync(cancellationToken: cancellation.Token);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(0, store.Commits);
    }

    /// <summary>A malformed document yields to later files instead of permanently blocking the queue.</summary>
    [Fact]
    public async Task DocumentFailureIsRecordedAndLaterDocumentStillCompletes()
    {
        var store = new StubStore(LongDocument()) { AdditionalDocument = new("later", "stamp", [new("native.text", "valid later text")]) };
        var provider = new StubProvider { FailFirstBatch = true };
        await using var coordinator = new VectorIndexCoordinator(new StubConfiguration(), store, provider);
        await coordinator.RefreshAsync();
        Assert.Equal(1, store.Failures);
        Assert.Equal(1, store.Commits);
        Assert.Equal(1, store.SavedChunks);
        Assert.Equal(VectorIndexState.Failed, coordinator.CurrentStatus.State);
        Assert.Contains("1 files waiting for retry", coordinator.CurrentStatus.Message);
    }

    private static VectorIndexDocument LongDocument() => new("file", "stamp", [new("native.text", new string('界', 40_000))]);

    private sealed class StubConfiguration : IConfigurationService
    {
        public ApplicationSettings Current { get; } = new() { SemanticSearch = new() { Enabled = true, EmbeddingsEnabled = true, EmbeddingModel = Model.Model } };
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubProvider : IModelEmbeddingProvider
    {
        public bool Available { get; init; } = true;
        public bool ChangeSecondBatch { get; init; }
        public bool WaitForCancellation { get; init; }
        public bool FailFirstBatch { get; init; }
        public List<int> BatchSizes { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SemanticModelIdentity?> GetModelAsync(CancellationToken cancellationToken = default) => Task.FromResult<SemanticModelIdentity?>(Available ? Model : null);
        public async Task<ModelEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, SemanticModelIdentity? expectedModel = null, CancellationToken cancellationToken = default)
        {
            BatchSizes.Add(inputs.Count);
            Started.TrySetResult();
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (FailFirstBatch && BatchSizes.Count == 1) return new(null, [], "Invalid document batch") { IsDocumentFailure = true };
            return new(ChangeSecondBatch && BatchSizes.Count == 2 ? Model with { Revision = "changed" } : Model,
                inputs.Select(_ => (IReadOnlyList<float>)new float[] { 1, 0 }).ToArray());
        }
    }

    private sealed class StubStore(VectorIndexDocument document) : IVectorSearchStore
    {
        public int Commits { get; private set; }
        public int Clears { get; private set; }
        public int SavedChunks { get; private set; }
        public int Failures { get; private set; }
        public VectorIndexDocument? AdditionalDocument { get; init; }
        public Task<IReadOnlyList<VectorIndexDocument>> GetVectorIndexCandidatesAsync(SemanticModelIdentity model, int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VectorIndexDocument>>(AdditionalDocument is null ? [document] : [document, AdditionalDocument]);
        public Task<bool> ReplaceVectorsAsync(VectorIndexDocument snapshot, SemanticModelIdentity model, IReadOnlyList<VectorChunkEmbedding> chunks, long maximumBytes, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Commits++; SavedChunks = chunks.Count; return Task.FromResult(true); }
        public Task<VectorStorageStatus> GetVectorStatusAsync(SemanticModelIdentity model, CancellationToken cancellationToken = default) => Task.FromResult(new VectorStorageStatus(Commits, (AdditionalDocument is null ? 1 : 2) - Commits, Commits * 100) { FailedFiles = Failures });
        public Task RecordVectorFailureAsync(VectorIndexDocument snapshot, SemanticModelIdentity model, CancellationToken cancellationToken = default) { Failures++; return Task.CompletedTask; }
        public Task ClearVectorsAsync(CancellationToken cancellationToken = default) { Clears++; return Task.CompletedTask; }
        public Task PruneVectorsAsync(SemanticModelIdentity model, long maximumBytes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<VectorSearchResult> SearchVectorsAsync(SemanticModelIdentity model, IReadOnlyList<float> query, DiscoverySearchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new VectorSearchResult([], 0, 0, false));
        public Task<VectorSearchResult> SearchRelatedVectorsAsync(SemanticModelIdentity model, string fileId, DiscoverySearchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new VectorSearchResult([], 0, 0, false));
    }
}
