using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Semantic;

/// <summary>Defines defensive bounds shared by local Search interpretation and ranking.</summary>
public static class SearchLimits
{
    /// <summary>Gets the maximum accepted query length.</summary>
    public const int MaximumQueryCharacters = 512;

    /// <summary>Gets the maximum topic-token count.</summary>
    public const int MaximumQueryTokens = 32;

    /// <summary>Gets the maximum interpreted-filter count.</summary>
    public const int MaximumFilters = 16;

    /// <summary>Gets the maximum filename candidates examined by typo matching.</summary>
    public const int MaximumFuzzyCandidates = 4096;

    /// <summary>Gets the maximum result-snippet length.</summary>
    public const int MaximumSnippetCharacters = 240;

    /// <summary>Gets the maximum results returned by one local ranking pass.</summary>
    public const int MaximumRankedResults = 1000;
}

/// <summary>Identifies a deterministic Search filter.</summary>
public enum SearchFilterKind
{
    /// <summary>Filters by a plain-language file category.</summary>
    FileType,
    /// <summary>Filters by filename extension.</summary>
    Extension,
    /// <summary>Filters by inclusive creation date.</summary>
    CreatedOnOrAfter,
    /// <summary>Filters by exclusive creation date.</summary>
    CreatedBefore,
    /// <summary>Filters by inclusive modification date.</summary>
    ModifiedOnOrAfter,
    /// <summary>Filters by exclusive modification date.</summary>
    ModifiedBefore,
    /// <summary>Filters by inclusive minimum byte length.</summary>
    MinimumSizeBytes,
    /// <summary>Filters by inclusive maximum byte length.</summary>
    MaximumSizeBytes,
    /// <summary>Filters by configured indexing source.</summary>
    Source,
    /// <summary>Filters by folder name or source-relative folder path.</summary>
    Folder,
    /// <summary>Filters by an accepted or generated tag.</summary>
    Tag,
    /// <summary>Filters by indexing level.</summary>
    IndexingLevel,
    /// <summary>Filters by full or partial indexing state.</summary>
    IndexingCompletion,
    /// <summary>Filters by retained OCR availability.</summary>
    OcrAvailability,
    /// <summary>Filters by retained related-concept data availability.</summary>
    SemanticAvailability,
    /// <summary>Filters by retained indexing failure state.</summary>
    FailureState,
    /// <summary>Filters by an exact canonical Theme Smart Tag identifier.</summary>
    SmartTagTheme,
    /// <summary>Filters by an exact canonical Document Type Smart Tag identifier.</summary>
    SmartTagDocumentType,
    /// <summary>Filters by an exact canonical User Tag identifier.</summary>
    SmartTagUser,
}

/// <summary>Describes one visible, removable interpreted Search filter.</summary>
/// <param name="Id">Stable identifier used by ViewModels when removing a filter.</param>
/// <param name="Kind">Provider-neutral filter kind.</param>
/// <param name="Value">Invariant bounded value interpreted by the ranker.</param>
/// <param name="DisplayName">Plain-language description shown to users.</param>
public sealed record SearchFilter(string Id, SearchFilterKind Kind, string Value, string DisplayName);

/// <summary>Contains a bounded Search request and any user-edited active filters.</summary>
/// <param name="QueryText">Original local query text.</param>
/// <param name="ActiveFilters">Explicit filters to apply when interpretation is disabled.</param>
/// <param name="InterpretFilters">Whether deterministic local interpretation should discover filters.</param>
/// <param name="TopicTextOverride">Topic terms retained after a user edits interpreted filters.</param>
/// <param name="IncludeRelationshipContext">Whether explainable direct relationships may expand otherwise ranked results.</param>
public sealed record SearchRequest(
    string QueryText,
    IReadOnlyList<SearchFilter>? ActiveFilters = null,
    bool InterpretFilters = true,
    string? TopicTextOverride = null,
    bool IncludeRelationshipContext = true)
{
    /// <summary>
    /// Gets whether this explicit request may ask the optional configured local-AI provider to
    /// rerank a bounded set of already-known local results. Deterministic Search remains authoritative.
    /// </summary>
    public bool UseAiAssistance { get; init; }

    /// <summary>
    /// Gets whether the optional v2.0 Knowledge Graph may add bounded one-hop
    /// context. This is independent from the existing v1.9 relationship context.
    /// </summary>
    public bool IncludeGraphContext { get; init; } = true;
}

/// <summary>Separates ordinary topic terms from visible deterministic filters.</summary>
public sealed record SearchInterpretation(
    string OriginalText,
    string TopicText,
    IReadOnlyList<string> TopicTokens,
    IReadOnlyList<SearchFilter> Filters);

/// <summary>Identifies an actual ranking signal that may be explained to a user.</summary>
public enum SearchRankingSignalKind
{
    /// <summary>The complete filename matched.</summary>
    ExactFilename,
    /// <summary>The query exactly matched the filename without its extension.</summary>
    ExactFilenameStem,
    /// <summary>The normalized filename begins with the complete topic phrase.</summary>
    FilenamePrefix,
    /// <summary>The normalized filename contains the complete topic phrase.</summary>
    FilenameSubstring,
    /// <summary>The complete topic phrase matched a literal field.</summary>
    ExactPhrase,
    /// <summary>Filename tokens matched.</summary>
    FilenameToken,
    /// <summary>A bounded filename typo match contributed.</summary>
    FuzzyFilename,
    /// <summary>A folder name matched.</summary>
    FolderName,
    /// <summary>A source-relative or absolute path matched.</summary>
    Path,
    /// <summary>The extension matched.</summary>
    Extension,
    /// <summary>The file category matched.</summary>
    FileType,
    /// <summary>An accepted tag matched.</summary>
    Tag,
    /// <summary>Basic or embedded metadata matched.</summary>
    Metadata,
    /// <summary>Extracted document text matched.</summary>
    ExtractedText,
    /// <summary>OCR-derived text matched.</summary>
    OcrText,
    /// <summary>A generated summary matched.</summary>
    Summary,
    /// <summary>A generated keyword matched.</summary>
    Keyword,
    /// <summary>A selected bounded text chunk matched.</summary>
    Chunk,
    /// <summary>Related-concept similarity contributed.</summary>
    SemanticSimilarity,
    /// <summary>An evidence-backed file relationship or Smart Collection contributed.</summary>
    RelationshipContext,
    /// <summary>A visible user-selected filter matched.</summary>
    Filter,
    /// <summary>Recent modification time resolved an otherwise comparable result.</summary>
    Recency,
    /// <summary>Configured source priority resolved an otherwise comparable result.</summary>
    SourcePriority,
    /// <summary>Indexing completeness resolved an otherwise comparable result.</summary>
    IndexingCompleteness,
    /// <summary>An evidence-backed bounded Knowledge Graph edge contributed.</summary>
    GraphContext,
    /// <summary>Optional local AI changed order only among equally strong deterministic tiers.</summary>
    AiAssistedOrder,
    /// <summary>Deterministic embedded image, audio, or video metadata matched.</summary>
    MediaMetadata,
    /// <summary>A locally produced bounded audio or video transcript matched.</summary>
    MediaTranscript,
    /// <summary>Local OCR over an image or representative video frame matched.</summary>
    MediaOcr,
    /// <summary>An optional, visibly derived visual description matched.</summary>
    MediaVisualDescription,
    /// <summary>A bounded topic derived from retained indexed evidence matched.</summary>
    ContentTopic,
    /// <summary>A bounded textual entity derived from retained indexed evidence matched.</summary>
    ContentEntity,
    /// <summary>A source-grounded bounded content-intelligence summary matched.</summary>
    ContentIntelligenceSummary,
    /// <summary>An eligible Theme Smart Tag matched.</summary>
    SmartTagTheme,
    /// <summary>An eligible Document Type Smart Tag matched.</summary>
    SmartTagDocumentType,
    /// <summary>An explicit User Tag matched.</summary>
    SmartTagUser,
}

/// <summary>Describes one actual, explainable component used by ranking.</summary>
public sealed record SearchRankingComponent(
    SearchRankingSignalKind Kind,
    string Field,
    double Contribution,
    string Explanation,
    string? MatchedText = null);

/// <summary>Identifies the indexed field used for a bounded result snippet.</summary>
public enum SearchSnippetSource
{
    /// <summary>No safe snippet was available.</summary>
    None,
    /// <summary>The filename supplied the snippet.</summary>
    Filename,
    /// <summary>The source-relative path supplied the snippet.</summary>
    Path,
    /// <summary>Metadata supplied the snippet.</summary>
    Metadata,
    /// <summary>Extracted document text supplied the snippet.</summary>
    ExtractedText,
    /// <summary>OCR text supplied the snippet.</summary>
    OcrText,
    /// <summary>A summary supplied the snippet.</summary>
    Summary,
    /// <summary>A tag or keyword supplied the snippet.</summary>
    TagOrKeyword,
    /// <summary>A selected bounded chunk supplied the snippet.</summary>
    Chunk,
    /// <summary>Structured media metadata supplied the snippet.</summary>
    MediaMetadata,
    /// <summary>A bounded local transcript supplied the snippet.</summary>
    MediaTranscript,
    /// <summary>Image or representative-frame OCR supplied the snippet.</summary>
    MediaOcr,
    /// <summary>An optional derived visual description supplied the snippet.</summary>
    MediaVisualDescription,
    /// <summary>A bounded derived topic supplied the snippet.</summary>
    ContentTopic,
    /// <summary>A bounded textual entity supplied the snippet.</summary>
    ContentEntity,
    /// <summary>A source-grounded content-intelligence summary supplied the snippet.</summary>
    ContentIntelligenceSummary,
}

/// <summary>Identifies one accessible highlighted range within a bounded snippet.</summary>
public sealed record SearchHighlight(int Start, int Length);

/// <summary>Contains a privacy-bounded result snippet created only from retained index data.</summary>
public sealed record SearchSnippet(
    SearchSnippetSource Source,
    string SourceLabel,
    string Text,
    IReadOnlyList<SearchHighlight> Highlights)
{
    private SearchHighlight? PrimaryHighlight =>
        Highlights.FirstOrDefault(item =>
            item.Start >= 0 &&
            item.Length > 0 &&
            item.Start <= Text.Length &&
            item.Length <= Text.Length - item.Start);

    /// <summary>Gets the text preceding the first highlighted match.</summary>
    public string Prefix => PrimaryHighlight is { } highlight
        ? Text[..highlight.Start]
        : Text;

    /// <summary>Gets the first highlighted match.</summary>
    public string MatchedText => PrimaryHighlight is { } highlight
        ? Text.Substring(highlight.Start, highlight.Length)
        : string.Empty;

    /// <summary>Gets the text following the first highlighted match.</summary>
    public string Suffix => PrimaryHighlight is { } highlight
        ? Text[(highlight.Start + highlight.Length)..]
        : string.Empty;

    /// <summary>Gets a screen-reader description containing provenance and bounded snippet text.</summary>
    public string AccessibleText => $"{SourceLabel}: {Text}";
}

/// <summary>Contains provider-neutral indexed fields consumed by the ranker.</summary>
public sealed record SearchCandidateDocument
{
    /// <summary>Gets the durable file identifier when supplied by progressive indexing.</summary>
    public string? FileId { get; init; }

    /// <summary>Gets the local full path used only for opening and bounded path matching.</summary>
    public required string FullPath { get; init; }

    /// <summary>Gets the filename.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the source-relative path.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Gets the containing folder name.</summary>
    public string FolderName { get; init; } = string.Empty;

    /// <summary>Gets the normalized extension including its leading dot.</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>Gets the plain-language file category.</summary>
    public string FileType { get; init; } = string.Empty;

    /// <summary>Gets the durable source identifier.</summary>
    public string? SourceId { get; init; }

    /// <summary>Gets the display-safe source name.</summary>
    public string? SourceName { get; init; }

    /// <summary>Gets the source priority.</summary>
    public int SourcePriority { get; init; }

    /// <summary>Gets the observed file size.</summary>
    public long Length { get; init; }

    /// <summary>Gets the observed creation time.</summary>
    public DateTimeOffset? CreationTimeUtc { get; init; }

    /// <summary>Gets the observed modification time.</summary>
    public DateTimeOffset? ModifiedTimeUtc { get; init; }

    /// <summary>Gets the selected indexing level.</summary>
    public IndexingLevel? IndexingLevel { get; init; }

    /// <summary>Gets accepted user or system tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets typed effective Smart Tags used by exact canonical filters and explanations.</summary>
    public IReadOnlyList<FileSmartTag> SmartTags { get; init; } = [];

    /// <summary>Gets bounded searchable metadata.</summary>
    public string MetadataText { get; init; } = string.Empty;

    /// <summary>Gets bounded extracted document text.</summary>
    public string? ExtractedText { get; init; }

    /// <summary>Gets bounded OCR text.</summary>
    public string? OcrText { get; init; }

    /// <summary>Gets structured provider-neutral media evidence.</summary>
    public IndexedMediaEvidence? MediaEvidence { get; init; }

    /// <summary>Gets bounded topics, textual entities, and grounded summary evidence.</summary>
    public IndexedContentIntelligence? ContentIntelligence { get; init; }

    /// <summary>Gets a bounded generated summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets bounded generated keywords.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Gets bounded selected chunks.</summary>
    public IReadOnlyList<string> Chunks { get; init; } = [];

    /// <summary>Gets one bounded related-concept representation.</summary>
    public IReadOnlyList<float>? SemanticRepresentation { get; init; }

    /// <summary>Gets whether all applicable stages completed.</summary>
    public bool IsFullyIndexed { get; init; }

    /// <summary>Gets whether a retained stage failure is associated with the file.</summary>
    public bool HasIndexingFailure { get; init; }

    /// <summary>Gets optional evidence-backed context supplied by relationship expansion.</summary>
    public SearchRelationshipContext? RelationshipContext { get; init; }

    /// <summary>Gets optional evidence-backed context supplied by bounded graph expansion.</summary>
    public SearchGraphContext? GraphContext { get; init; }
}

/// <summary>Provides one actual graph edge contribution to Search ranking.</summary>
public sealed record SearchGraphContext(
    string SeedFileId,
    string EdgeId,
    GraphEdgeKind EdgeKind,
    GraphConfidenceLevel Confidence,
    string Explanation,
    long ProjectionRevision,
    GraphFreshnessState Freshness)
{
    /// <summary>Maps the retained edge evidence to one explicit ranking component.</summary>
    public SearchRankingComponent ToRankingComponent() => new(
        SearchRankingSignalKind.GraphContext,
        "Knowledge Graph context",
        Confidence switch
        {
            GraphConfidenceLevel.Confirmed => 4,
            GraphConfidenceLevel.High => 3,
            GraphConfidenceLevel.Medium => 2,
            _ => 1,
        },
        Explanation);
}

/// <summary>Contains one independently testable ranked candidate.</summary>
public sealed record RankedSearchCandidate(
    SearchCandidateDocument Document,
    double Score,
    IReadOnlyList<SearchRankingComponent> Components,
    SearchSnippet? Snippet);

/// <summary>Contains one advanced Search execution result.</summary>
public sealed record SearchExecutionResult(
    SemanticState State,
    string Message,
    IReadOnlyList<SemanticSearchHit> Hits,
    SearchInterpretation Interpretation,
    SearchCoverage Coverage)
{
    /// <summary>Gets the outcome of the optional bounded AI-assistance layer.</summary>
    public AiSearchAssistanceResult AiAssistance { get; init; } = AiSearchAssistanceResult.NotRequested;

    /// <summary>
    /// Gets graph-projection coverage independently from deep-index Search coverage.
    /// A null value means no graph provider was configured.
    /// </summary>
    public GraphProjectionCoverage? GraphCoverage { get; init; }
}

/// <summary>Identifies the user-visible outcome of optional local-AI Search assistance.</summary>
public enum AiSearchAssistanceState
{
    /// <summary>The Search request did not ask for AI assistance.</summary>
    NotRequested,
    /// <summary>The explicit setting or capability gate is disabled.</summary>
    Disabled,
    /// <summary>The configured provider or selected model was unavailable.</summary>
    Unavailable,
    /// <summary>The provider returned invalid or ungrounded data and local order was preserved.</summary>
    InvalidResponse,
    /// <summary>The bounded request completed but did not change deterministic order.</summary>
    NoChange,
    /// <summary>The bounded request safely changed order within deterministic relevance tiers.</summary>
    Applied,
}

/// <summary>Describes optional AI assistance without exposing prompts or private candidate text.</summary>
public sealed record AiSearchAssistanceResult(
    AiSearchAssistanceState State,
    string Message,
    int CandidateCount,
    bool WasApplied)
{
    /// <summary>Gets the default result used by ordinary deterministic Search.</summary>
    public static AiSearchAssistanceResult NotRequested { get; } = new(
        AiSearchAssistanceState.NotRequested,
        "AI assistance was not requested. Deterministic local Search was used.",
        0,
        false);
}

/// <summary>Contains safely reranked candidates and the assistance outcome.</summary>
public sealed record AiSearchRerankResult(
    IReadOnlyList<RankedSearchCandidate> Candidates,
    AiSearchAssistanceResult Assistance);

/// <summary>Optionally reranks a bounded candidate set without discovering or inventing files.</summary>
public interface IAiSearchAssistant
{
    /// <summary>
    /// May reorder supplied candidates within deterministic relevance tiers. Implementations must
    /// return only the supplied candidates and preserve their deterministic scores.
    /// </summary>
    Task<AiSearchRerankResult> RerankAsync(
        SearchInterpretation interpretation,
        IReadOnlyList<RankedSearchCandidate> candidates,
        AiSettings settings,
        CancellationToken cancellationToken);
}

/// <summary>Interprets conservative local filters without calling AI or a storage provider.</summary>
public interface ISearchQueryInterpreter
{
    /// <summary>Validates and interprets one bounded query using the configured locale and clock.</summary>
    SearchInterpretation Interpret(SearchRequest request);
}

/// <summary>Ranks provider-neutral candidates with explicit deterministic components.</summary>
public interface ISearchRanker
{
    /// <summary>Applies visible filters and returns stable ranked candidates.</summary>
    IReadOnlyList<RankedSearchCandidate> Rank(
        SearchInterpretation interpretation,
        IReadOnlyList<SearchCandidateDocument> candidates,
        int maximumResults,
        CancellationToken cancellationToken);
}

/// <summary>Creates short snippets from already retained bounded index fields.</summary>
public interface ISearchSnippetFactory
{
    /// <summary>Creates a safe snippet based on actual ranking components.</summary>
    SearchSnippet? Create(
        SearchCandidateDocument candidate,
        SearchInterpretation interpretation,
        IReadOnlyList<SearchRankingComponent> components);
}
