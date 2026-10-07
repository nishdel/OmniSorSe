using OpenSorSe.Core.Configuration;
using OpenSorSe.Application.Indexing;

namespace OpenSorSe.Application.Semantic;

/// <summary>Refreshes disposable learned vectors independently of the deterministic indexing queue.</summary>
public sealed class VectorIndexCoordinator : IAsyncDisposable
{
    private readonly IConfigurationService _configuration;
    private readonly IVectorSearchStore _store;
    private readonly IModelEmbeddingProvider _provider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateLock = new();
    private CancellationTokenSource? _active;
    private Task? _worker;
    private volatile bool _paused;
    private bool _disposed;

    /// <summary>Initializes independent derived work against the existing catalog and selected local provider.</summary>
    public VectorIndexCoordinator(IConfigurationService configuration, IVectorSearchStore store, IModelEmbeddingProvider provider)
    {
        _configuration = configuration;
        _store = store;
        _provider = provider;
    }

    /// <summary>Gets the latest learned-vector progress state.</summary>
    public VectorIndexStatus CurrentStatus { get; private set; } = new(VectorIndexState.Disabled, 0, 0, 0, "Learned semantic search is disabled.");
    /// <summary>Publishes progress independently of deterministic indexing events.</summary>
    public event EventHandler<VectorIndexStatus>? StatusChanged;

    /// <summary>Starts one process-lifetime background refresh loop.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateLock) _worker ??= Task.Run(RunAsync);
        return Task.CompletedTask;
    }

    /// <summary>Pauses new batches and cancels current optional inference.</summary>
    public void Pause()
    {
        _paused = true;
        lock (_stateLock) _active?.Cancel();
        Publish(CurrentStatus with { State = VectorIndexState.Paused, Message = "Learned vector indexing is paused." });
    }

    /// <summary>Resumes independent indexing on the next bounded background pass.</summary>
    public void Resume()
    {
        _paused = false;
        Publish(CurrentStatus with { State = VectorIndexState.Idle, Message = "Learned vector indexing will resume within 30 seconds." });
    }

    /// <summary>Cancels and drains inference before reclaiming its disposable data; resume remains explicit.</summary>
    public async Task ReclaimAsync(CancellationToken cancellationToken = default)
    {
        Pause();
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.ClearVectorsAsync(cancellationToken).ConfigureAwait(false);
            Publish(new(VectorIndexState.Paused, 0, 0, 0, "Learned vectors were cleared. Resume indexing to rebuild from retained evidence."));
        }
        finally { _refreshGate.Release(); }
    }

    /// <summary>Processes a bounded batch from retained evidence, optionally clearing derived vectors first.</summary>
    public async Task RefreshAsync(bool rebuild = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _refreshGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            lock (_stateLock) _active = linked;
            if (rebuild) await _store.ClearVectorsAsync(linked.Token).ConfigureAwait(false);
            if (_paused) return;
            var settings = _configuration.Current.SemanticSearch;
            if (!settings.Enabled || !settings.EmbeddingsEnabled)
            {
                Publish(CurrentStatus with { State = VectorIndexState.Disabled, Message = "Learned semantic search is disabled." });
                return;
            }
            var model = await _provider.GetModelAsync(linked.Token).ConfigureAwait(false);
            if (model is null)
            {
                Publish(CurrentStatus with { State = VectorIndexState.Unavailable, Message = "The selected local embedding model is unavailable. Ordinary Search remains available." });
                return;
            }
            var maximumBytes = (long)Math.Min(settings.MaximumVectorStorageMiB,
                _configuration.Current.DeepIndexing.MaximumIndexSizeMiB) * 1024 * 1024;
            if (_store is IDeepIndexStore catalog)
            {
                var storage = await catalog.GetStorageBreakdownAsync(
                    (long)_configuration.Current.DeepIndexing.MaximumIndexSizeMiB * 1024 * 1024, linked.Token).ConfigureAwait(false);
                // Retain a page/transaction margin and count existing catalog bytes. A full
                // catalog must not provoke an endless build/maintenance/prune cycle.
                maximumBytes = Math.Min(maximumBytes, storage.MaximumBytes - Math.Max(0, storage.DatabaseBytes - storage.VectorDataBytes) - 1024 * 1024);
                if (maximumBytes <= 0)
                {
                    var full = await _store.GetVectorStatusAsync(model, linked.Token).ConfigureAwait(false);
                    Publish(new(VectorIndexState.StorageLimit, full.IndexedFiles, full.PendingFiles, full.StoredBytes,
                        "The library storage limit leaves no room for learned vectors. Reclaim space or raise the limit to continue."));
                    return;
                }
            }
            await _store.PruneVectorsAsync(model, maximumBytes, linked.Token).ConfigureAwait(false);
            var initial = await _store.GetVectorStatusAsync(model, linked.Token).ConfigureAwait(false);
            var processed = 0;
            var candidates = await _store.GetVectorIndexCandidatesAsync(model, 32, linked.Token).ConfigureAwait(false);
            foreach (var document in candidates)
            {
                linked.Token.ThrowIfCancellationRequested();
                var current = _configuration.Current.SemanticSearch;
                if (_paused || !current.Enabled || !current.EmbeddingsEnabled || current.EmbeddingModel != settings.EmbeddingModel) break;
                var chunks = VectorTextChunker.Create(document);
                Publish(new(VectorIndexState.Indexing, initial.IndexedFiles + processed, Math.Max(0, initial.PendingFiles - processed), initial.StoredBytes,
                    $"{model.Model}: embedding {processed + 1} of {candidates.Count} in this batch; {initial.PendingFiles} files pending."));
                var embedded = new List<IReadOnlyList<float>>(chunks.Count);
                var documentFailed = false;
                foreach (var batch in chunks.Chunk(8))
                {
                    // Batches bound the encoded JSON request even for non-ASCII text.
                    var output = await _provider.EmbedAsync(batch.Select(chunk => chunk.Text).ToArray(), model, linked.Token).ConfigureAwait(false);
                    var malformed = output.IsAvailable && output.Model?.Key == model.Key &&
                        (output.Vectors.Count != batch.Length || output.Vectors.Any(vector =>
                            vector is null || vector.Count != model.Dimensions || vector.Any(value => !float.IsFinite(value)) || vector.All(value => value == 0)));
                    if (!output.IsAvailable || output.Model?.Key != model.Key || malformed)
                    {
                        if (output.IsDocumentFailure || malformed)
                        {
                            await _store.RecordVectorFailureAsync(document, model, linked.Token).ConfigureAwait(false);
                            documentFailed = true;
                            break;
                        }
                        Publish(CurrentStatus with { State = VectorIndexState.Unavailable, Message = output.UnavailableReason ?? "The embedding model changed or returned incomplete vectors; this batch will be retried." });
                        return;
                    }
                    embedded.AddRange(output.Vectors);
                }
                if (documentFailed) continue;
                current = _configuration.Current.SemanticSearch;
                if (!current.Enabled || !current.EmbeddingsEnabled || current.EmbeddingModel != settings.EmbeddingModel || _paused) break;
                var values = chunks.Select((chunk, index) => new VectorChunkEmbedding(chunk, embedded[index])).ToArray();
                if (!await _store.ReplaceVectorsAsync(document, model, values, maximumBytes, linked.Token).ConfigureAwait(false))
                {
                    var status = await _store.GetVectorStatusAsync(model, linked.Token).ConfigureAwait(false);
                    // A changed catalog snapshot is retried on the next pass; a full budget stays explicit.
                    if (status.StoredBytes + values.Sum(value => value.Vector.Count * 4L + value.Chunk.Text.Length * 2L + 256) > maximumBytes)
                    {
                        Publish(new(VectorIndexState.StorageLimit, status.IndexedFiles, status.PendingFiles, status.StoredBytes,
                            "The learned vector storage limit was reached. Existing vectors and ordinary Search remain available."));
                        return;
                    }
                }
                else processed++;
            }
            var final = await _store.GetVectorStatusAsync(model, linked.Token).ConfigureAwait(false);
            Publish(new(_paused ? VectorIndexState.Paused : final.FailedFiles > 0 ? VectorIndexState.Failed : VectorIndexState.Idle, final.IndexedFiles, final.PendingFiles, final.StoredBytes,
                $"{model.Model}: {final.IndexedFiles} files indexed, {final.PendingFiles} pending; {final.StoredBytes / (1024d * 1024):F1} MiB. " +
                (_paused ? "Paused." : final.FailedFiles > 0 ? $"{final.FailedFiles} files waiting for retry; after three attempts use Rebuild to retry." :
                    final.PendingFiles == 0 ? "Up to date." : "Indexing continues in the background.")));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            Publish(CurrentStatus with { State = VectorIndexState.Failed, Message = "Learned vector indexing could not complete. Ordinary Search remains available." });
        }
        finally
        {
            lock (_stateLock) _active = null;
            _refreshGate.Release();
        }
    }

    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try { await RefreshAsync(cancellationToken: _lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (Exception) { Publish(CurrentStatus with { State = VectorIndexState.Failed, Message = "Learned vector indexing could not complete. Ordinary Search remains available." }); }
            try { await Task.Delay(TimeSpan.FromSeconds(CurrentStatus.PendingFiles > 0 && CurrentStatus.State == VectorIndexState.Idle ? 2 : 30), _lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Publish(VectorIndexStatus status)
    {
        CurrentStatus = status;
        try { StatusChanged?.Invoke(this, status); }
        catch (Exception) { /* Presentation subscribers cannot fail derived work. */ }
    }

    /// <summary>Cancels and drains derived work before the owning catalog is disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_worker is not null) await _worker.ConfigureAwait(false);
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        _refreshGate.Release();
        _lifetime.Dispose();
        _refreshGate.Dispose();
    }
}
