using System.Security.Cryptography;
using System.Text;
using OpenSorSe.Application.Indexing;

namespace OpenSorSe.Application.Semantic;

/// <summary>Bounds optional interactive retrieval independently of background model inference.</summary>
public static class SemanticQueryLimits
{
    /// <summary>Gets the maximum total duration of one optional semantic lookup.</summary>
    public const int MaximumDurationSeconds = 5;
}

/// <summary>Identifies one compatible learned vector space, including the installed model digest.</summary>
public sealed record SemanticModelIdentity(string Provider, string Model, string Revision, int Dimensions)
{
    /// <summary>Gets a stable key for the provider, model tag, installed digest and dimensionality.</summary>
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{Provider}\n{Model}\n{Revision}\n{Dimensions}"))).ToLowerInvariant();
}

/// <summary>Returns learned vectors or an explicit optional-provider unavailability reason.</summary>
public sealed record ModelEmbeddingResult(SemanticModelIdentity? Model,
    IReadOnlyList<IReadOnlyList<float>> Vectors, string? UnavailableReason = null)
{
    /// <summary>Gets whether the selected provider returned a usable model result.</summary>
    public bool IsAvailable => Model is not null && UnavailableReason is null;

    /// <summary>Gets whether this document batch failed validation and should yield to other documents.</summary>
    public bool IsDocumentFailure { get; init; }
}

/// <summary>Embeds bounded text using a separately configured, local learned model.</summary>
public interface IModelEmbeddingProvider
{
    /// <summary>Resolves the installed model identity without downloading a model.</summary>
    Task<SemanticModelIdentity?> GetModelAsync(CancellationToken cancellationToken = default);
    /// <summary>Embeds bounded inputs and refuses changes to an expected model space.</summary>
    Task<ModelEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs,
        SemanticModelIdentity? expectedModel = null, CancellationToken cancellationToken = default);
}

/// <summary>Retained catalog evidence; offsets are relative to this exact field value.</summary>
public sealed record VectorSourceField(string Name, string Text);

/// <summary>Snapshot fenced against catalog, evidence, path and privacy changes at commit.</summary>
public sealed record VectorIndexDocument(string FileId, string SourceFingerprint,
    IReadOnlyList<VectorSourceField> Fields);

/// <summary>A stable bounded chunk, with exact provenance in retained indexed evidence.</summary>
public sealed record VectorTextChunk(string ChunkId, int Ordinal, string Field, int Start, int Length, string Text);

/// <summary>Associates a learned vector with one traceable retained-evidence chunk.</summary>
public sealed record VectorChunkEmbedding(VectorTextChunk Chunk, IReadOnlyList<float> Vector);
/// <summary>Publishes a scored file candidate with its strongest matching evidence span.</summary>
public sealed record VectorSearchMatch(string FileId, string ChunkId, double Similarity,
    string Field, int Start, int Length, string Text)
{
    /// <summary>Gets the exact catalog generation used to score this evidence.</summary>
    public string? SourceFingerprint { get; init; }
}
/// <summary>Publishes bounded matches and complete eligible/indexed file counts.</summary>
public sealed record VectorSearchResult(IReadOnlyList<VectorSearchMatch> Matches,
    long EligibleFileCount, long IndexedFileCount, bool IsTruncated)
{
    /// <summary>Gets the source generation used to create a Related Files query centroid.</summary>
    public string? RelatedSourceFingerprint { get; init; }
}
/// <summary>Describes compatible indexed coverage and logical derived storage usage.</summary>
public sealed record VectorStorageStatus(long IndexedFiles, long PendingFiles, long StoredBytes)
{
    /// <summary>Gets files waiting for bounded retry or an explicit rebuild after three unsuccessful attempts.</summary>
    public long FailedFiles { get; init; }
}

/// <summary>Derived learned-vector data in the authoritative catalog provider, never source-file authority.</summary>
public interface IVectorSearchStore
{
    /// <summary>Reads bounded retained catalog snapshots that need fresh embeddings.</summary>
    Task<IReadOnlyList<VectorIndexDocument>> GetVectorIndexCandidatesAsync(SemanticModelIdentity model,
        int maximumCount, CancellationToken cancellationToken = default);
    /// <summary>Atomically publishes a complete chunk set only if its catalog snapshot and quota remain valid.</summary>
    Task<bool> ReplaceVectorsAsync(VectorIndexDocument document, SemanticModelIdentity model,
        IReadOnlyList<VectorChunkEmbedding> chunks, long maximumBytes, CancellationToken cancellationToken = default);
    /// <summary>Ranks all current eligible vectors before applying the result bound.</summary>
    Task<VectorSearchResult> SearchVectorsAsync(SemanticModelIdentity model, IReadOnlyList<float> query,
        DiscoverySearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads file similarity while honoring relationship exclusions, without creating relationships.</summary>
    Task<VectorSearchResult> SearchRelatedVectorsAsync(SemanticModelIdentity model, string fileId,
        DiscoverySearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>Rechecks exact scored generations, filters and decisions after asynchronous document hydration.</summary>
    Task<IReadOnlyList<VectorSearchMatch>> ValidateVectorMatchesAsync(SemanticModelIdentity model,
        VectorSearchResult result, DiscoverySearchRequest request, string? relatedFileId = null,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VectorSearchMatch>>([]);
    /// <summary>Reads current learned coverage for the exact model space.</summary>
    Task<VectorStorageStatus> GetVectorStatusAsync(SemanticModelIdentity model, CancellationToken cancellationToken = default);
    /// <summary>Deletes disposable vectors and chunk text while preserving catalog and user authority.</summary>
    Task ClearVectorsAsync(CancellationToken cancellationToken = default);
    /// <summary>Removes stale or incompatible complete documents and enforces the logical vector budget.</summary>
    Task PruneVectorsAsync(SemanticModelIdentity model, long maximumBytes, CancellationToken cancellationToken = default);

    /// <summary>Records a bounded retry for one current document snapshot without blocking later candidates.</summary>
    Task RecordVectorFailureAsync(VectorIndexDocument document, SemanticModelIdentity model,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Identifies the observable state of independent learned-vector work.</summary>
public enum VectorIndexState
{
    /// <summary>Learned embeddings are disabled by settings.</summary>
    Disabled,
    /// <summary>The coordinator is ready for its next bounded pass.</summary>
    Idle,
    /// <summary>Retained evidence is being embedded.</summary>
    Indexing,
    /// <summary>The user paused derived work.</summary>
    Paused,
    /// <summary>The optional model or provider is unavailable.</summary>
    Unavailable,
    /// <summary>The configured disposable-data budget is full.</summary>
    StorageLimit,
    /// <summary>The last pass could not complete.</summary>
    Failed,
}
/// <summary>Publishes learned-vector progress and an actionable user-facing status.</summary>
public sealed record VectorIndexStatus(VectorIndexState State, long IndexedFiles, long PendingFiles,
    long StoredBytes, string Message);
