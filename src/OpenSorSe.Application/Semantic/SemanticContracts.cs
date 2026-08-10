using OpenSorSe.Application.Models;

namespace OpenSorSe.Application.Semantic;

/// <summary>Identifies local Semantic Search Beta availability and operation states.</summary>
public enum SemanticState
{
    /// <summary>The feature is disabled in Settings.</summary>
    Disabled,
    /// <summary>No local index entries are available.</summary>
    Empty,
    /// <summary>A local index operation is active.</summary>
    Indexing,
    /// <summary>The local index is ready.</summary>
    Ready,
    /// <summary>The operation was cancelled without publishing a partial index.</summary>
    Cancelled,
    /// <summary>The operation failed safely.</summary>
    Failed,
}

/// <summary>Reports bounded semantic-index progress.</summary>
public sealed record SemanticIndexProgress(int ProcessedCount, int TotalCount, string Message);

/// <summary>Stores one local semantic-index document without exposing raw vectors to the UI.</summary>
public sealed record SemanticIndexEntry(
    string FullPath,
    string SourceFingerprint,
    string IndexFingerprint,
    string FileName,
    IReadOnlyList<TagAssociation> Tags,
    IReadOnlyList<string> MetadataTerms,
    IReadOnlyList<string> NativeTextTerms,
    IReadOnlyList<string> OcrTextTerms,
    IReadOnlyList<float> Vector,
    DateTimeOffset IndexedAtUtc);

/// <summary>Contains a local hybrid-ranked semantic result.</summary>
public sealed record SemanticSearchHit(
    string FullPath,
    string FileName,
    double Score,
    string Explanation,
    IReadOnlyList<string> MatchedTags,
    bool MatchedMetadata,
    bool MatchedNativeText,
    bool MatchedOcrText,
    string? FileId = null,
    IReadOnlyList<SearchRankingComponent>? RankingComponents = null,
    SearchSnippet? Snippet = null,
    bool IsFullyIndexed = true,
    OpenSorSe.Core.Configuration.IndexingLevel? IndexingLevel = null,
    string? SourceName = null)
{
    /// <summary>Gets a plain-language result source indicator.</summary>
    public string SourceIndicator => string.IsNullOrWhiteSpace(SourceName)
        ? "Compatible local Search index"
        : SourceName;

    /// <summary>Gets a plain-language per-result indexing coverage indicator.</summary>
    public string CoverageIndicator => IsFullyIndexed
        ? "Fully indexed"
        : "Partially indexed";
}

/// <summary>Contains one controlled semantic operation outcome.</summary>
public sealed record SemanticResult<T>(
    SemanticState State,
    string Message,
    T Value);

/// <summary>Creates deterministic local vector features.</summary>
public interface IEmbeddingProvider
{
    /// <summary>Gets the fixed vector dimension.</summary>
    int Dimensions { get; }

    /// <summary>Creates a normalized local vector without network or model activity.</summary>
    IReadOnlyList<float> Embed(string text);
}

/// <summary>Persists a bounded versioned semantic index.</summary>
public interface ISemanticIndexStore
{
    /// <summary>Lists all valid index entries in deterministic path order.</summary>
    Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Atomically replaces the complete local index.</summary>
    Task ReplaceAsync(IReadOnlyList<SemanticIndexEntry> entries, CancellationToken cancellationToken);

    /// <summary>Clears the application-owned index without changing source files.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>Builds and refreshes the local index from application-owned content records.</summary>
public interface ISemanticIndexer
{
    /// <summary>Builds either an incremental refresh or an explicit full rebuild.</summary>
    Task<SemanticResult<int>> BuildAsync(
        bool rebuild,
        IProgress<SemanticIndexProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Runs bounded local hybrid semantic searches.</summary>
public interface ISemanticSearchService
{
    /// <summary>Searches the local index and returns explanations without raw embeddings.</summary>
    Task<SemanticResult<IReadOnlyList<SemanticSearchHit>>> SearchAsync(
        string query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Searches with visible interpreted filters. Existing implementations retain compatibility
    /// through the default query-only adapter.
    /// </summary>
    async Task<SearchExecutionResult> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await SearchAsync(request.QueryText, cancellationToken).ConfigureAwait(false);
        var interpretation = new SearchInterpretation(
            request.QueryText,
            request.TopicTextOverride ?? request.QueryText,
            [],
            request.ActiveFilters ?? []);
        return new SearchExecutionResult(
            result.State,
            result.Message,
            result.Value,
            interpretation,
            new Indexing.SearchCoverage(0, 0, 0, 0, 0, 0));
    }
}
