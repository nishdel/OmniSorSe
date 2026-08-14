using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Indexing;

/// <summary>Identifies application-owned indexed artefacts that may be inspected or cleared.</summary>
[Flags]
public enum IndexedDataKind
{
    /// <summary>No derived artefact is selected.</summary>
    None = 0,
    /// <summary>Bounded extracted document text.</summary>
    ExtractedText = 1 << 0,
    /// <summary>Bounded OCR-derived text.</summary>
    OcrText = 1 << 1,
    /// <summary>Generated summary and keywords.</summary>
    SummaryAndKeywords = 1 << 2,
    /// <summary>Related-concept representation.</summary>
    SemanticData = 1 << 3,
    /// <summary>Selected bounded text chunks.</summary>
    Chunks = 1 << 4,
    /// <summary>Evidence-backed file relationships, collection membership, and relationship features.</summary>
    Relationships = 1 << 5,
    /// <summary>Durable stage and failure history.</summary>
    ProcessingHistory = 1 << 6,
    /// <summary>Structured media metadata, OCR, transcripts, and optional descriptions.</summary>
    MediaDerived = 1 << 7,
    /// <summary>Bounded topics, textual entities, summaries, and their provenance.</summary>
    ContentIntelligence = 1 << 8,
    /// <summary>Generated and user-authoritative schema-6 Smart Tag state.</summary>
    SmartTags = 1 << 9,
    /// <summary>All content-derived and operational data while retaining source registration.</summary>
    AllDerived = ExtractedText |
        OcrText |
        SummaryAndKeywords |
        SemanticData |
        Chunks |
        Relationships |
        ProcessingHistory |
        MediaDerived |
        ContentIntelligence |
        SmartTags,
}

/// <summary>Identifies a targeted durable repair boundary.</summary>
public enum IndexRepairKind
{
    /// <summary>Verifies the retained record and refreshes it only when inconsistent.</summary>
    Verify,
    /// <summary>Rebuilds all applicable stages for the selected file or source.</summary>
    Rebuild,
    /// <summary>Refreshes observed metadata and later dependent stages.</summary>
    RefreshMetadata,
    /// <summary>Refreshes extracted document text and later dependent stages.</summary>
    RefreshText,
    /// <summary>Refreshes OCR and later dependent stages.</summary>
    RefreshOcr,
    /// <summary>Regenerates summary and keywords and later dependent stages.</summary>
    RegenerateSummaryAndKeywords,
    /// <summary>Regenerates related-concept data and the Search record.</summary>
    RegenerateSemanticData,
    /// <summary>Retries the retained failed stage.</summary>
    RetryFailedStage,
}

/// <summary>Describes the privacy-relevant indexed data retained for one file.</summary>
public sealed record IndexPrivacyItem
{
    /// <summary>Gets the durable file identifier.</summary>
    public required string FileId { get; init; }

    /// <summary>Gets the durable source identifier.</summary>
    public required string SourceId { get; init; }

    /// <summary>Gets the display-safe source name.</summary>
    public required string SourceName { get; init; }

    /// <summary>Gets the source root required only by local repair coordination.</summary>
    public required string SourceRootPath { get; init; }

    /// <summary>Gets the filename.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the source-relative path.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Gets the effective indexing level.</summary>
    public required IndexingLevel IndexingLevel { get; init; }

    /// <summary>Gets whether watched-folder configuration owns the source lifecycle.</summary>
    public bool ManagedByWatchedFolders { get; init; }

    /// <summary>Gets the observed metadata byte estimate.</summary>
    public long MetadataBytes { get; init; }

    /// <summary>Gets retained extracted-text characters.</summary>
    public int ExtractedTextCharacters { get; init; }

    /// <summary>Gets retained OCR-text characters.</summary>
    public int OcrTextCharacters { get; init; }

    /// <summary>Gets whether a generated summary is retained.</summary>
    public bool HasSummary { get; init; }

    /// <summary>Gets the bounded generated keyword count.</summary>
    public int KeywordCount { get; init; }

    /// <summary>Gets whether related-concept data is retained.</summary>
    public bool HasSemanticData { get; init; }

    /// <summary>Gets the bounded selected-chunk count.</summary>
    public int ChunkCount { get; init; }

    /// <summary>Gets the number of other active records sharing identical content-derived data.</summary>
    public int SharedContentReferenceCount { get; init; }

    /// <summary>Gets retained failure count.</summary>
    public int FailureCount { get; init; }

    /// <summary>Gets retained durable stage count.</summary>
    public int StageHistoryCount { get; init; }

    /// <summary>Gets whether structured image, audio, or video evidence is retained.</summary>
    public bool HasMediaDerivedData { get; init; }

    /// <summary>Gets the retained media family without exposing content.</summary>
    public string? MediaKind { get; init; }

    /// <summary>Gets whether a bounded local transcript is retained.</summary>
    public bool HasMediaTranscript { get; init; }

    /// <summary>Gets whether media-specific OCR text is retained.</summary>
    public bool HasMediaOcr { get; init; }

    /// <summary>Gets whether an optional derived visual description is retained.</summary>
    public bool HasVisualDescription { get; init; }

    /// <summary>Gets whether bounded Content Intelligence is retained.</summary>
    public bool HasContentIntelligence { get; init; }

    /// <summary>Gets the number of retained bounded topics.</summary>
    public int ContentTopicCount { get; init; }

    /// <summary>Gets the number of retained textual entities.</summary>
    public int ContentEntityCount { get; init; }

    /// <summary>Gets the count of active schema-6 Smart Tag assignments without exposing their values.</summary>
    public int SmartTagCount { get; init; }

    /// <summary>Gets whether all applicable stages completed.</summary>
    public bool IsFullyIndexed { get; init; }

    /// <summary>Gets the last provider update time.</summary>
    public DateTimeOffset LastIndexedUtc { get; init; }

    /// <summary>Gets the current processor fingerprint without exposing prompts or content.</summary>
    public string ProcessorVersion { get; init; } = string.Empty;

    /// <summary>Gets a plain-language provider description without implementation-specific connection details.</summary>
    public string ProviderName { get; init; } = "Local index";

    /// <summary>Gets whether future deep indexing is excluded for this relative path.</summary>
    public bool IsExcluded { get; init; }

    /// <summary>Gets whether OCR is disabled for this relative path.</summary>
    public bool OcrSuppressed { get; init; }

    /// <summary>Gets whether generated summaries are disabled for this relative path.</summary>
    public bool SummarySuppressed { get; init; }

    /// <summary>Gets whether related-concept processing is disabled for this relative path.</summary>
    public bool SemanticSuppressed { get; init; }

    /// <summary>Gets whether evidence-backed relationship analysis is disabled for this relative path.</summary>
    public bool RelationshipAnalysisSuppressed { get; init; }

    /// <summary>Gets the number of retained direct file relationships.</summary>
    public int RelationshipCount { get; init; }

    /// <summary>Gets the number of retained Smart Collection memberships.</summary>
    public int CollectionCount { get; init; }
}

/// <summary>Contains a transactional index-only privacy or repair result.</summary>
public sealed record IndexPrivacyOperationResult(
    bool Applied,
    string? SourceId,
    int AffectedFileCount,
    string Message);

/// <summary>Contains one explicit per-file privacy policy update.</summary>
public sealed record IndexPrivacyPolicyChange(
    bool? Excluded = null,
    IndexingLevel? LevelOverride = null,
    bool? SuppressOcr = null,
    bool? SuppressSummary = null,
    bool? SuppressSemantic = null,
    bool? SuppressRelationships = null);

/// <summary>Provides provider-neutral privacy storage operations to application coordination.</summary>
public interface IIndexPrivacyStore
{
    /// <summary>Returns privacy-relevant retained information for one file.</summary>
    Task<IndexPrivacyItem?> InspectFileAsync(
        string fileId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns bounded privacy-relevant records for one source.</summary>
    Task<IReadOnlyList<IndexPrivacyItem>> InspectSourceAsync(
        string sourceId,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets one file's index record and retains an exclusion that prevents an immediate loop.</summary>
    Task<IndexPrivacyOperationResult> ForgetFileAsync(
        string fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets indexed files for a source without removing or modifying the source folder.</summary>
    Task<IndexPrivacyOperationResult> ForgetSourceAsync(
        string sourceId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a durable per-file processing policy.</summary>
    Task<IndexPrivacyOperationResult> SetFilePolicyAsync(
        string fileId,
        IndexPrivacyPolicyChange change,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Clears selected generated data and reports shared-content impact explicitly.</summary>
    Task<IndexPrivacyOperationResult> ClearFileDataAsync(
        string fileId,
        IndexedDataKind data,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Invalidates only stages affected by a selected file repair.</summary>
    Task<IndexPrivacyOperationResult> PrepareFileRepairAsync(
        string fileId,
        IndexRepairKind repair,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Invalidates only stages affected by a selected source repair.</summary>
    Task<IndexPrivacyOperationResult> PrepareSourceRepairAsync(
        string sourceId,
        IndexRepairKind repair,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Coordinates privacy and targeted repair while keeping the UI storage-provider independent.</summary>
public interface IIndexPrivacyService
{
    /// <summary>Returns privacy-relevant retained information for one indexed file.</summary>
    Task<IndexPrivacyItem?> InspectFileAsync(
        string fileId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns bounded privacy-relevant records for one indexed source.</summary>
    Task<IReadOnlyList<IndexPrivacyItem>> InspectSourceAsync(
        string sourceId,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets one file's index-only data without changing the original file.</summary>
    Task<IndexPrivacyOperationResult> ForgetFileAsync(
        string fileId,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets one source's indexed files while preserving source ownership.</summary>
    Task<IndexPrivacyOperationResult> ForgetSourceAsync(
        string sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Changes a per-file metadata-only, dependency, or exclusion policy.</summary>
    Task<IndexPrivacyOperationResult> SetFilePolicyAsync(
        string fileId,
        IndexPrivacyPolicyChange change,
        CancellationToken cancellationToken = default);

    /// <summary>Clears selected generated index-only data.</summary>
    Task<IndexPrivacyOperationResult> ClearFileDataAsync(
        string fileId,
        IndexedDataKind data,
        CancellationToken cancellationToken = default);

    /// <summary>Queues a selective durable file repair.</summary>
    Task<IndexPrivacyOperationResult> RepairFileAsync(
        string fileId,
        IndexRepairKind repair,
        CancellationToken cancellationToken = default);

    /// <summary>Queues a selective durable source repair.</summary>
    Task<IndexPrivacyOperationResult> RepairSourceAsync(
        string sourceId,
        IndexRepairKind repair,
        CancellationToken cancellationToken = default);
}
