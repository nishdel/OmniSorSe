using OpenSorSe.Scanner.Models;
using OpenSorSe.Application.Models;

namespace OpenSorSe.Application.Content;

/// <summary>Identifies where a local metadata value or tag originated.</summary>
public enum ContentProvenance
{
    /// <summary>Read from ordinary filesystem properties.</summary>
    Filesystem,
    /// <summary>Read from embedded document or media metadata.</summary>
    EmbeddedMetadata,
    /// <summary>Extracted deterministically from native document text.</summary>
    NativeText,
    /// <summary>Extracted by a local OCR engine.</summary>
    Ocr,
    /// <summary>Derived by a deterministic application rule.</summary>
    DeterministicRule,
    /// <summary>Explicitly entered or accepted by the user.</summary>
    UserAccepted,
    /// <summary>Produced by an optional, unverified AI suggestion.</summary>
    AiSuggestion,
    /// <summary>Derived from local similarity indexing.</summary>
    SemanticInference,
}

/// <summary>Identifies the terminal or active state of one OCR attempt.</summary>
public enum OcrStatus
{
    /// <summary>No attempt has been queued.</summary>
    Pending,
    /// <summary>A local engine is processing the input.</summary>
    Processing,
    /// <summary>Useful bounded text was extracted.</summary>
    Completed,
    /// <summary>OCR was deliberately not needed or not enabled.</summary>
    Skipped,
    /// <summary>The attempt failed without stopping the scan.</summary>
    Failed,
    /// <summary>Some text was extracted but a bound or engine warning applied.</summary>
    PartiallyCompleted,
    /// <summary>No compatible local engine or rasterizer is available.</summary>
    Unavailable,
    /// <summary>Text was extracted but excluded from indexing by a configured bound.</summary>
    TextNotIndexedDueToBounds,
}

/// <summary>Classifies a controlled OCR failure without exposing raw process details.</summary>
public enum OcrFailureCategory
{
    /// <summary>No failure occurred.</summary>
    None,
    /// <summary>OCR is disabled by settings.</summary>
    Disabled,
    /// <summary>A compatible local engine is unavailable.</summary>
    EngineUnavailable,
    /// <summary>The input type is unsupported by the available capability.</summary>
    UnsupportedInput,
    /// <summary>The input exceeds the configured byte limit.</summary>
    FileTooLarge,
    /// <summary>The document exceeds the configured page bound.</summary>
    PageLimitExceeded,
    /// <summary>The bounded operation timed out.</summary>
    Timeout,
    /// <summary>The caller cancelled the operation.</summary>
    Cancelled,
    /// <summary>The local engine reported a controlled failure.</summary>
    EngineFailure,
    /// <summary>The local engine returned no useful text.</summary>
    EmptyText,
    /// <summary>The input was malformed or unreadable.</summary>
    MalformedInput,
}

/// <summary>Describes the locally detected OCR capability.</summary>
public sealed record OcrCapability(
    bool IsAvailable,
    string EngineIdentifier,
    string? EngineVersion,
    IReadOnlyList<string> SupportedExtensions,
    bool SupportsPdf,
    string Message)
{
    /// <summary>Gets the bounded language identifiers reported by the local engine.</summary>
    public IReadOnlyList<string> AvailableLanguages { get; init; } = [];

    /// <summary>Gets the detected PDF rasterizer identifier when PDF OCR is supported.</summary>
    public string? RasterizerIdentifier { get; init; }

    /// <summary>Gets the detected PDF rasterizer version when available.</summary>
    public string? RasterizerVersion { get; init; }
}

/// <summary>Contains native text and its deterministic quality decision for one PDF page.</summary>
public sealed record PdfPageText(
    int PageNumber,
    string? NativeText,
    bool HasReliableNativeText)
{
    /// <summary>Gets bounded embedded text before normalization, when the extractor exposes it.</summary>
    public string? RawNativeText { get; init; }
}

/// <summary>Identifies the source retained for one page in a document OCR result.</summary>
public enum OcrPageTextSource
{
    /// <summary>No usable text was found.</summary>
    None,
    /// <summary>Reliable PDF-native text was retained.</summary>
    NativeText,
    /// <summary>Text came from local OCR of a rendered page.</summary>
    Ocr,
    /// <summary>OCR supplemented native text that was present but insufficient.</summary>
    NativeAndOcrFallback,
    /// <summary>The page was deliberately skipped.</summary>
    Skipped,
    /// <summary>The page could not be processed safely.</summary>
    Failed,
}

/// <summary>Contains the bounded provenance and outcome for one document page.</summary>
public sealed record OcrPageResult(
    int PageNumber,
    OcrPageTextSource TextSource,
    OcrStatus Status,
    string? Text,
    double? Confidence,
    string Message)
{
    /// <summary>Gets bounded engine output before normalization.</summary>
    public string? RawText { get; init; }

    /// <summary>Gets the normalized page text retained downstream.</summary>
    public string? NormalizedText { get; init; }

    /// <summary>Gets the configured render DPI for an OCR-rendered PDF page.</summary>
    public int? RenderDpi { get; init; }

    /// <summary>Gets the rendered page width when available.</summary>
    public int? RenderedWidth { get; init; }

    /// <summary>Gets the rendered page height when available.</summary>
    public int? RenderedHeight { get; init; }

    /// <summary>Gets truthful preprocessing steps applied before recognition.</summary>
    public IReadOnlyList<string> PreprocessingSteps { get; init; } = [];

    /// <summary>Gets the bounded page processing duration when measured.</summary>
    public TimeSpan? ProcessingDuration { get; init; }
}

/// <summary>Contains the bounded context for one local OCR request.</summary>
public sealed record OcrRequest(
    string FullPath,
    string Language,
    long MaximumFileBytes,
    int MaximumPages,
    TimeSpan Timeout,
    bool HasReliableNativeText)
{
    /// <summary>Gets page-level native PDF text used to avoid unnecessary rasterization.</summary>
    public IReadOnlyList<PdfPageText> PdfPages { get; init; } = [];

    /// <summary>Gets the configured PDF rasterization resolution.</summary>
    public int RasterizationDpi { get; init; } = 240;

    /// <summary>Gets the maximum rendered width or height in pixels.</summary>
    public int MaximumRasterDimension { get; init; } = 4096;

    /// <summary>Gets the maximum temporary-storage budget for one document.</summary>
    public long MaximumTemporaryStorageBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Gets the maximum combined OCR text retained for the request.</summary>
    public int MaximumTextCharacters { get; init; } = ContentText.MaximumTextCharacters;

    /// <summary>Gets whether all PDF pages should be OCRed instead of preferring reliable native text.</summary>
    public bool ForceReprocessAllPages { get; init; }

    /// <summary>Gets an existing extraction diagnostic session that OCR should enrich.</summary>
    public string? DiagnosticSessionId { get; init; }
}

/// <summary>Contains one controlled OCR outcome.</summary>
public sealed record OcrResult(
    OcrStatus Status,
    string? ExtractedText,
    string? Language,
    double? Confidence,
    int? PageCount,
    IReadOnlyList<string> Warnings,
    OcrFailureCategory FailureCategory,
    TimeSpan ProcessingDuration,
    string EngineIdentifier,
    string? EngineVersion,
    string Message)
{
    /// <summary>Gets whether bounded OCR text is available for local indexing.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(ExtractedText);

    /// <summary>Gets page-level provenance for PDF OCR outcomes.</summary>
    public IReadOnlyList<OcrPageResult> Pages { get; init; } = [];

    /// <summary>Gets the rasterizer used for PDF pages, when applicable.</summary>
    public string? RasterizerIdentifier { get; init; }

    /// <summary>Gets bounded OCR engine output before normalization.</summary>
    public string? RawExtractedText { get; init; }

    /// <summary>Gets the distinct normalized OCR value.</summary>
    public string? NormalizedText { get; init; }

    /// <summary>Gets the exact bounded value supplied to downstream indexing.</summary>
    public string? DownstreamText { get; init; }

    /// <summary>Gets whether page, text, output, or storage bounds truncated the attempt.</summary>
    public bool WasTruncated { get; init; }
}

/// <summary>Represents one normalized metadata value and its provenance.</summary>
public sealed record ExtractedMetadataField(
    string Name,
    string Value,
    ContentProvenance Provenance,
    double Confidence = 1);

/// <summary>Contains defensive metadata and native-text extraction for one known file.</summary>
public sealed record MetadataExtractionResult(
    IReadOnlyList<ExtractedMetadataField> Fields,
    string? NativeText,
    bool HasReliableNativeText,
    int? PageCount,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Gets bounded PDF-native text and quality state per page.</summary>
    public IReadOnlyList<PdfPageText> PdfPages { get; init; } = [];

    /// <summary>Gets bounded embedded text before pipeline normalization, when exposed by an extractor.</summary>
    public string? RawNativeText { get; init; }

    /// <summary>Gets the concrete extractor strategy names that contributed to the result.</summary>
    public IReadOnlyList<string> ExtractionStrategies { get; init; } = [];

    /// <summary>Gets whether any native text, page, field, or warning retention bound was reached.</summary>
    public bool WasTruncated { get; init; }
}

/// <summary>Contains one bounded, reusable local content record.</summary>
public sealed record ContentRecord(
    string FullPath,
    long SourceLength,
    DateTimeOffset SourceLastWriteTimeUtc,
    DateTimeOffset IndexedAtUtc,
    IReadOnlyList<ExtractedMetadataField> Metadata,
    string? NativeText,
    string? OcrText,
    OcrStatus OcrStatus,
    string? OcrEngineIdentifier,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Gets provenance-aware confirmed, suggested, and rejected local tags.</summary>
    public IReadOnlyList<TagAssociation> Tags { get; init; } = [];

    /// <summary>Gets a stable source fingerprint used for cache invalidation.</summary>
    public string SourceFingerprint => $"{SourceLength}:{SourceLastWriteTimeUtc.UtcTicks}";

    /// <summary>Gets the complete OCR/settings fingerprint used to validate cached extraction.</summary>
    public string? ExtractionFingerprint { get; init; }

    /// <summary>Gets bounded page-level OCR/native provenance for PDF content.</summary>
    public IReadOnlyList<OcrPageResult> OcrPages { get; init; } = [];

    /// <summary>Gets the session-only extraction diagnostic identity; it is never persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DiagnosticSessionId { get; init; }
}

/// <summary>Describes the local PDF rendering capability.</summary>
public sealed record PdfRasterizerCapability(
    bool IsAvailable,
    string Identifier,
    string? Version,
    string Message);

/// <summary>Contains one temporary, application-owned rendered PDF page.</summary>
public sealed record RenderedPdfPage(
    int PageNumber,
    string ImagePath,
    long EncodedBytes)
{
    /// <summary>Gets the rendered pixel width when reported by the rasterizer.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the rendered pixel height when reported by the rasterizer.</summary>
    public int? Height { get; init; }
}

/// <summary>Renders bounded individual PDF pages without modifying the source document.</summary>
public interface IPdfPageRasterizer
{
    /// <summary>Detects whether the in-process renderer can be loaded.</summary>
    Task<PdfRasterizerCapability> DetectCapabilityAsync(CancellationToken cancellationToken);

    /// <summary>Returns the page count for one known PDF.</summary>
    Task<int> GetPageCountAsync(string fullPath, CancellationToken cancellationToken);

    /// <summary>Renders one one-based page to a caller-owned temporary workspace.</summary>
    Task<RenderedPdfPage> RenderAsync(
        string fullPath,
        int pageNumber,
        int dpi,
        int maximumDimension,
        string workspacePath,
        CancellationToken cancellationToken);

    /// <summary>Creates an isolated temporary workspace owned by the rasterizer.</summary>
    string CreateWorkspace();

    /// <summary>Deletes one verified application-owned temporary workspace.</summary>
    void DeleteWorkspace(string workspacePath);
}

/// <summary>Summarizes one isolated content-indexing pass.</summary>
public sealed record ContentIndexingSummary(
    int ExaminedCount,
    int IndexedCount,
    int CacheHitCount,
    int FailedCount,
    int OcrCompletedCount,
    int OcrSkippedCount);

/// <summary>Constrains one profile-driven content pass without changing global settings.</summary>
public sealed record ContentIndexingOptions(
    bool MetadataEnabled,
    bool TextEnabled,
    bool OcrEnabled,
    bool OcrOnlyWhenTextUnavailable,
    string OcrLanguage,
    int MaximumPagesPerDocument,
    long MaximumFileSizeBytes);

/// <summary>Abstracts one concrete local OCR engine.</summary>
public interface IOcrEngine
{
    /// <summary>Detects capability without opening user content.</summary>
    Task<OcrCapability> DetectCapabilityAsync(CancellationToken cancellationToken);

    /// <summary>Extracts bounded text from one supported known file.</summary>
    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken);

    /// <summary>Invalidates any successful capability snapshot before an explicit recheck.</summary>
    void ResetCapability()
    {
    }
}

/// <summary>Applies settings, cache, bounds, and normalization around a local OCR engine.</summary>
public interface IOcrService
{
    /// <summary>Gets the current local capability without sending network requests.</summary>
    Task<OcrCapability> GetCapabilityAsync(CancellationToken cancellationToken);

    /// <summary>Forces a fresh local engine and language capability check.</summary>
    Task<OcrCapability> RefreshCapabilityAsync(CancellationToken cancellationToken);

    /// <summary>Runs or skips one bounded OCR request.</summary>
    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken);
}

/// <summary>Extracts bounded metadata from a supported file type.</summary>
public interface IMetadataExtractor
{
    /// <summary>Gets whether this extractor supports the supplied normalized extension.</summary>
    bool Supports(string normalizedExtension);

    /// <summary>Extracts metadata without executing embedded content or fetching remote resources.</summary>
    Task<MetadataExtractionResult> ExtractAsync(
        FileEntry file,
        long maximumInputBytes,
        int maximumPages,
        CancellationToken cancellationToken);
}

/// <summary>Coordinates defensive extractors and normalized provenance.</summary>
public interface IMetadataExtractionPipeline
{
    /// <summary>Extracts bounded metadata and native text for one known file.</summary>
    Task<MetadataExtractionResult> ExtractAsync(
        FileEntry file,
        long maximumInputBytes,
        int maximumPages,
        CancellationToken cancellationToken);
}

/// <summary>Persists bounded local content independently from catalog snapshots.</summary>
public interface IContentStore
{
    /// <summary>Loads one exact-path record when present.</summary>
    Task<ContentRecord?> GetAsync(string fullPath, CancellationToken cancellationToken);

    /// <summary>Lists bounded records in deterministic path order.</summary>
    Task<IReadOnlyList<ContentRecord>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Adds or replaces one normalized record.</summary>
    Task UpsertAsync(ContentRecord record, CancellationToken cancellationToken);

    /// <summary>Removes stale records that are not in the supplied known-path set.</summary>
    Task RemoveMissingAsync(IReadOnlyCollection<string> knownPaths, CancellationToken cancellationToken);

    /// <summary>Removes exact application-owned records for an explicit privacy request.</summary>
    Task RemoveAsync(IReadOnlyCollection<string> fullPaths, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The configured content store does not support targeted privacy deletion.");

    /// <summary>Clears the application-owned cache without changing source files.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>Integrates metadata and OCR with the read-only scan pipeline.</summary>
public interface IContentIndexingService
{
    /// <summary>Indexes known scanned files with per-file failure isolation.</summary>
    Task<ContentIndexingSummary> IndexAsync(
        IReadOnlyCollection<FileEntry> files,
        CancellationToken cancellationToken);

    /// <summary>Indexes with an effective workflow that can only narrow global capabilities.</summary>
    Task<ContentIndexingSummary> IndexAsync(
        IReadOnlyCollection<FileEntry> files,
        ContentIndexingOptions? options,
        CancellationToken cancellationToken) =>
        IndexAsync(files, cancellationToken);
}
